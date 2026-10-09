using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using BuildAR.Assembly;
using BuildAR.Data;

namespace BuildAR.EditorTools
{
    /// <summary>Fans and RGB lighting: see AddFansAndLights and AddCaseLighting.</summary>
    public static partial class ComponentModelImporter
    {
        const string LedMaterialPath = "Assets/_Project/Art/Materials/M_LED_RGB.mat";

        static string AddFansAndLights(GameObject instance, ComponentDefinitionSO def, string modelPath) =>
            AddFansAndLights(instance, def.modelFans, def.modelLightBars, def.modelPanels, Path.ChangeExtension(modelPath, null) + "_fx.asset", null);

        /// <summary>
        /// Brings a part to life the way gaming hardware looks. Each fan (see ModelFan) is cut out of the single welded
        /// mesh generated models come as — a lump that can't turn cleanly — and a real one is built in its place: a
        /// rotor with proper blades that spins, a closed fan well behind it (or a whole framed fan, for one standing on
        /// its own), and an RGB ring. Light bars (see ModelLightBar) and I/O panels (see ModelIoPanel) are added too.
        /// Everything goes on the mesh's own object, in the mesh's own space, so it follows whatever turn and scale
        /// the part was given. <paramref name="meshBounds"/> is the bounds the spec was measured against, when the
        /// mesh on the object isn't the original (the assembly case's has its side panel cut off). The cut-up mesh and
        /// the fan meshes are saved to <paramref name="assetPath"/>. Returns a note for the report, or null when there
        /// is nothing to add.
        /// </summary>
        static string AddFansAndLights(GameObject instance, List<ModelFan> fans, List<ModelLightBar> bars,
                                       List<ModelIoPanel> panels, string assetPath, Bounds? meshBounds)
        {
            AssetDatabase.DeleteAsset(assetPath);
            fans ??= new List<ModelFan>();
            bars ??= new List<ModelLightBar>();
            panels ??= new List<ModelIoPanel>();
            if (fans.Count == 0 && bars.Count == 0 && panels.Count == 0) return null;

            var filter = instance.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null)
                .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude)
                .FirstOrDefault();
            if (filter == null) return null;

            var mesh = filter.sharedMesh;
            var box = meshBounds ?? mesh.bounds;
            float big = Mathf.Max(box.size.x, box.size.y, box.size.z);
            Vector3 At(Vector3 unit) => box.min + Vector3.Scale(unit, box.size);
            var led = LedMaterial();
            var meshes = new List<Mesh>();
            var notes = new List<string>();

            if (fans.Count > 0 && !mesh.isReadable)
                notes.Add("fans left as modelled — the mesh isn't readable");
            else if (fans.Count > 0)
            {
                var body = CutOutFans(mesh, fans, At, big);
                filter.sharedMesh = body;
                meshes.Add(body);
                Mesh ring = null;
                for (int k = 0; k < fans.Count; k++)
                {
                    if (fans[k].rgbRing && ring == null) { ring = RingMesh(); meshes.Add(ring); }
                    BuildFan(filter.transform, fans[k], k, At(fans[k].center), big, ring, led, meshes);
                }
                notes.Add($"{fans.Count} spinning fan{(fans.Count == 1 ? "" : "s")}");
            }

            for (int k = 0; k < bars.Count; k++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"RGB Light Bar {k + 1}";
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                bar.transform.SetParent(filter.transform, false);
                bar.transform.localPosition = At(bars[k].center);
                bar.transform.localScale = Vector3.Scale(bars[k].size, box.size);
                AddLed(bar, led);
            }
            if (bars.Count > 0) notes.Add($"{bars.Count} RGB light bar{(bars.Count == 1 ? "" : "s")}");

            if (panels.Count > 0)
            {
                var surface = filter.sharedMesh;
                int placed = 0;
                if (surface.isReadable)
                    foreach (var panel in panels)
                        if (BuildPanel(filter.transform, panel, surface, At, big)) placed++;
                notes.Add(placed == panels.Count
                    ? $"{placed} I/O panel{(placed == 1 ? "" : "s")}"
                    : $"{placed} of {panels.Count} I/O panels (no surface found for the rest)");
            }

            SaveMeshes(meshes, assetPath);
            return string.Join(", ", notes);
        }

        /// <summary>
        /// Lays an I/O panel on the model: finds the surface by looking back along the panel's facing from just
        /// outside the model, turns the panel to face out of it with its up where the spec says, and scales it to the
        /// spec's width. The rear port column rests its plate on the surface (the recess of the case's cut-out); the
        /// top panel's bezel stands on it. Adds the viewer's hotspots for what's on it.
        /// </summary>
        static bool BuildPanel(Transform parent, ModelIoPanel spec, Mesh surface, System.Func<Vector3, Vector3> at, float big)
        {
            var facing = spec.facing.sqrMagnitude > 1e-6f ? spec.facing.normalized : Vector3.forward;
            var up = Vector3.ProjectOnPlane(spec.up, facing);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Mathf.Abs(facing.y) < 0.9f ? Vector3.up : Vector3.right, facing);
            up.Normalize();

            Vector3 from = at(spec.center) + facing * (0.08f * big);
            if (!Raycast(surface, from, -facing, 0.25f * big, out float distance)) return false;
            Vector3 point = from - facing * distance;

            bool rear = spec.kind == ModelIoPanel.Kind.RearPorts;
            Vector2 design = rear ? IoParts.RearColumnSize : IoParts.TopPanelSize;
            float scale = spec.width * big / design.x;
            var root = rear ? IoParts.RearColumn(parent, "Rear I/O Shield") : IoParts.TopPanel(parent, "Top I/O");
            root.localRotation = Quaternion.LookRotation(facing, up);
            root.localScale = Vector3.one * scale;
            root.localPosition = point + facing * (rear ? IoParts.RearPlateDepth * scale : 0f);

            if (rear)
                Hotspot(root, "Rear I/O ports", "USB, network, video and audio from the motherboard, through the case's I/O shield. " +
                        "Blue USB is 5 Gb/s, red 10 Gb/s; plug monitors into the graphics card, not HDMI here.",
                        new Vector3(0f, 0.02f, 0.001f));
            else
            {
                Hotspot(root, "Power button", "Press once to turn the PC on. Its ring lights up while it runs; holding it for " +
                        "five seconds forces a shutdown.", new Vector3(-0.0455f, 0f, 0.0045f));
                Hotspot(root, "Front USB and audio", "USB-C, two blue USB-A and a headset jack. Their cables run inside to headers " +
                        "on the motherboard.", new Vector3(0.025f, 0f, 0.002f));
            }
            return true;
        }

        static void Hotspot(Transform panel, string title, string description, Vector3 at)
        {
            var h = new GameObject("Hotspot_" + title.Replace(" ", "")).transform;
            h.SetParent(panel, false);
            h.localPosition = at;
            var spot = h.gameObject.AddComponent<BuildAR.Viewer.ModelHotspot>();
            spot.title = title;
            spot.description = description;
            spot.kind = BuildAR.Viewer.ModelHotspot.Kind.Port;
        }

        /// <summary>Distance along a ray (in the mesh's space) to the first triangle it meets, if within reach.</summary>
        static bool Raycast(Mesh mesh, Vector3 origin, Vector3 direction, float reach, out float distance)
        {
            var v = mesh.vertices;
            distance = reach;
            bool hit = false;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], e1 = v[tris[i + 1]] - a, e2 = v[tris[i + 2]] - a;
                    var p = Vector3.Cross(direction, e2);
                    float det = Vector3.Dot(e1, p);
                    if (Mathf.Abs(det) < 1e-18f) continue;
                    float inv = 1f / det;
                    var o = origin - a;
                    float u = Vector3.Dot(o, p) * inv;
                    if (u < 0f || u > 1f) continue;
                    var q = Vector3.Cross(o, e1);
                    float w = Vector3.Dot(direction, q) * inv;
                    if (w < 0f || u + w > 1f) continue;
                    float t = Vector3.Dot(e2, q) * inv;
                    if (t > 0f && t < distance) { distance = t; hit = true; }
                }
            }
            return hit;
        }

        static void SaveMeshes(List<Mesh> meshes, string assetPath)
        {
            for (int i = 0; i < meshes.Count; i++)
            {
                // GPU-only like the model. Not compressed: compressing a mesh made this way spoils its tangents, and
                // the normal map then shades it in blotches.
                if (i == 0) AssetDatabase.CreateAsset(meshes[i], assetPath);
                else AssetDatabase.AddObjectToAsset(meshes[i], assetPath);
                var serialized = new SerializedObject(meshes[i]);
                serialized.FindProperty("m_IsReadable").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static Vector3 Facing(ModelFan fan) => fan.facing.sqrMagnitude > 1e-6f ? fan.facing.normalized : Vector3.up;

        /// <summary>
        /// A usable tangent. Generated meshes have triangles whose UVs collapse to a point, and the tangents worked out
        /// for them are zero; URP normalises the tangent before using the normal map, so a zero one turns the pixel
        /// into NaN, and bloom smears every NaN pixel into a big blotch. Any tangent across the surface will do there.
        /// </summary>
        static Vector4 SafeTangent(Vector4 tangent, Vector3 normal)
        {
            var t = new Vector3(tangent.x, tangent.y, tangent.z);
            if (!float.IsNaN(t.x + t.y + t.z) && t.sqrMagnitude > 1e-8f && Vector3.Cross(t, normal).sqrMagnitude > 1e-10f)
                return tangent;
            var across = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right);
            if (across.sqrMagnitude < 1e-12f) across = Vector3.right;
            across = across / across.magnitude;
            return new Vector4(across.x, across.y, across.z, tangent.w < 0f ? -1f : 1f);
        }

        /// <summary>
        /// A copy of the mesh with each fan's space emptied. Inside the blade radius everything goes from the face
        /// (or from behind the wall the fan sits behind) to the fan's depth. A framed fan also clears a wider circle
        /// from where it starts, so nothing the model had there pokes out round the new frame.
        /// </summary>
        static Mesh CutOutFans(Mesh mesh, List<ModelFan> fans, System.Func<Vector3, Vector3> at, float big)
        {
            Vector3[] positions = mesh.vertices, normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uvs = mesh.uv;
            Color32[] colors = mesh.colors32;
            int count = positions.Length;
            var hubs = fans.Select(f => at(f.center)).ToArray();
            var axes = fans.Select(Facing).ToArray();

            bool Cut(Vector3 middle)
            {
                for (int k = 0; k < fans.Count; k++)
                {
                    var fan = fans[k];
                    var offset = middle - hubs[k];
                    float behind = -Vector3.Dot(offset, axes[k]) / big;
                    float radial = (offset + behind * big * axes[k]).magnitude / big;
                    if (behind >= fan.depth) continue;
                    float front = fan.inset > 0f ? Mathf.Min(fan.inset, 0.015f) : -0.01f;
                    if (radial < fan.radius && behind > front) return true;
                    if (fan.frame && radial < fan.radius * 1.2f && behind > fan.inset + 0.005f) return true;
                }
                return false;
            }

            var submeshes = new List<int>[mesh.subMeshCount];
            var used = new bool[count];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var source = mesh.GetTriangles(s);
                var kept = new List<int>(source.Length);
                for (int t = 0; t < source.Length; t += 3)
                {
                    if (Cut((positions[source[t]] + positions[source[t + 1]] + positions[source[t + 2]]) / 3f)) continue;
                    for (int j = 0; j < 3; j++) { kept.Add(source[t + j]); used[source[t + j]] = true; }
                }
                submeshes[s] = kept;
            }

            // Keep only the vertices still in use.
            var remap = new int[count];
            var order = new List<int>();
            for (int i = 0; i < count; i++) { remap[i] = used[i] ? order.Count : -1; if (used[i]) order.Add(i); }

            var body = new Mesh { name = $"{mesh.name} (fans cut out)" };
            if (order.Count > 65535) body.indexFormat = IndexFormat.UInt32;
            body.SetVertices(order.Select(v => positions[v]).ToList());
            if (normals.Length == count) body.SetNormals(order.Select(v => normals[v]).ToList());
            if (tangents.Length == count && normals.Length == count)
                body.SetTangents(order.Select(v => SafeTangent(tangents[v], normals[v])).ToList());
            if (uvs.Length == count) body.SetUVs(0, order.Select(v => uvs[v]).ToList());
            if (colors.Length == count) body.SetColors(order.Select(v => colors[v]).ToList());
            body.subMeshCount = submeshes.Length;
            for (int s = 0; s < submeshes.Length; s++) body.SetTriangles(submeshes[s].Select(v => remap[v]).ToList(), s);
            body.RecalculateBounds();
            return body;
        }

        /// <summary>
        /// Builds fan <paramref name="index"/> under <paramref name="parent"/> (the mesh's object): a "Fan n" object at
        /// the fan's front face with +Z pointing the way the fan faces, holding the spinning rotor, the well or frame,
        /// and the RGB ring. All sizes follow the blade radius.
        /// </summary>
        static void BuildFan(Transform parent, ModelFan fan, int index, Vector3 hub, float big, Mesh ring, Material led,
                             List<Mesh> meshes)
        {
            var facing = Facing(fan);
            float radius = fan.radius * big;
            // A standing fan is 25 mm deep for 120 mm across; one in a shroud fills most of the space it's given.
            float room = Mathf.Max(0.02f, fan.depth - fan.inset) * big;
            float thickness = fan.frame ? radius * 0.42f : room * 0.75f;

            var up = new[] { Vector3.forward, Vector3.up, Vector3.right }.First(a => Mathf.Abs(Vector3.Dot(a, facing)) < 0.7f);
            var root = new GameObject($"Fan {index + 1}").transform;
            root.SetParent(parent, false);
            root.localPosition = hub - facing * (fan.inset * big);
            root.localRotation = Quaternion.LookRotation(facing, up);

            // Frosted blades that pick up the RGB colour, as on most RGB fans — which also makes the spin easy to see.
            var frosted = FanMaterial("M_FanBlade", new Color(0.62f, 0.64f, 0.68f), 0.45f, 0f, glow: 0.5f);
            var cap = FanMaterial("M_FanHub", new Color(0.1f, 0.1f, 0.11f), 0.6f, 0.3f);

            // The meshes are built one blade-radius across and scaled up here: generated models can be a couple of
            // centimetres across in their own units, too small for the maths that works out the blades' normals.
            // The rotor's front sits just inside the fan's front face (a framed fan's front is its outlet).
            float front = fan.frame ? -thickness * 0.1f : -room * 0.12f;
            var rotorMesh = RotorMesh(fan.blades, thickness * (fan.frame ? 0.8f : 1f) / radius, fan.frame);
            rotorMesh.name = $"Fan {index + 1} rotor";
            meshes.Add(rotorMesh);
            var rotor = new GameObject("Rotor");
            rotor.transform.SetParent(root, false);
            rotor.transform.localPosition = new Vector3(0f, 0f, front);
            rotor.transform.localScale = Vector3.one * radius;
            rotor.AddComponent<MeshFilter>().sharedMesh = rotorMesh;
            rotor.AddComponent<MeshRenderer>().sharedMaterials = new[] { frosted, cap };
            var spin = rotor.AddComponent<SpinningPart>();
            spin.rpm = fan.rpm;
            spin.axis = Vector3.forward;
            spin.maxDegreesPerFrame = 0.4f * 360f / Mathf.Max(3, fan.blades);
            if (fan.rgbRing)
            {
                var tint = rotor.AddComponent<RgbGlow>();
                tint.phase = index * 0.06f;
                tint.intensity = 0.55f;                                             // a tint, not a light: stays under the bloom
            }

            var housing = fan.frame ? FrameMesh(thickness / radius) : WellMesh(room / radius);
            housing.name = $"Fan {index + 1} {(fan.frame ? "frame" : "well")}";
            meshes.Add(housing);
            var body = new GameObject(fan.frame ? "Frame" : "Well");
            body.transform.SetParent(root, false);
            body.transform.localScale = Vector3.one * radius;
            body.AddComponent<MeshFilter>().sharedMesh = housing;
            body.AddComponent<MeshRenderer>().sharedMaterial = fan.frame
                ? FanMaterial("M_FanFrame", new Color(0.03f, 0.031f, 0.035f), 0.35f, 0f)
                : FanMaterial("M_FanWell", new Color(0.012f, 0.012f, 0.014f), 0.15f, 0f);

            if (!fan.rgbRing || ring == null) return;
            // A shrouded fan shows its face; a framed one mostly its intake side, so that gets a ring too.
            var sides = fan.frame ? new[] { 0.01f * radius, -thickness - 0.01f * radius } : new[] { 0.004f * big };
            for (int s = 0; s < sides.Length; s++)
            {
                var glow = new GameObject(s == 0 ? "RGB Ring" : "RGB Ring (intake)");
                glow.transform.SetParent(root, false);
                glow.transform.localPosition = new Vector3(0f, 0f, sides[s]);
                glow.transform.localScale = Vector3.one * radius;
                glow.AddComponent<MeshFilter>().sharedMesh = ring;
                AddLed(glow, led).phase = index * 0.06f;                          // a slow rainbow along the fans
            }
        }

        /// <summary>Makes the object an RGB LED: the LED material, no shadow, and the colour cycle.</summary>
        static RgbGlow AddLed(GameObject go, Material led)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = led;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go.AddComponent<RgbGlow>();
        }

        // ------------------------------------------------------------------ fan meshes

        /// <summary>
        /// Rotor with its front face at z 0 and its back at -<paramref name="thickness"/>, turning about Z: a hub with a
        /// sticker (submesh 1) and swept, twisted blades out to just inside radius 1.
        /// </summary>
        static Mesh RotorMesh(int blades, float thickness, bool framed)
        {
            const float radius = 1f;
            var b = new MeshBuilder(2);
            float hubR = radius * (framed ? 0.34f : 0.3f), tip = radius * 0.96f;
            float mid = -thickness * 0.5f;

            b.Tube(0, hubR, 0f, -thickness, outward: true);
            b.Disc(0, hubR, 0f, Vector3.forward);
            b.Disc(0, hubR, -thickness, Vector3.back);
            // The sticker on the hub, on the side you see: a shrouded fan's face, a framed fan's intake (its back).
            if (framed) b.Disc(1, hubR * 0.72f, -thickness - radius * 0.004f, Vector3.back);
            else b.Disc(1, hubR * 0.72f, radius * 0.004f, Vector3.forward);

            const int along = 9, across = 6;
            float pitch = 2f * Mathf.PI / blades;
            for (int k = 0; k < blades; k++)
            {
                var grid = new Vector3[along + 1, across + 1];
                for (int i = 0; i <= along; i++)
                {
                    float s = i / (float)along;
                    float r = Mathf.Lerp(hubR * 0.96f, tip, s);
                    float span = pitch * Mathf.Lerp(0.5f, 0.74f, s);
                    float sweep = 0.5f * s * s;
                    float rise = thickness * 0.8f * Mathf.Lerp(1f, 0.65f, s);         // twist: flatter at the tip
                    for (int j = 0; j <= across; j++)
                    {
                        float c = j / (float)across;                                   // 0 leading edge .. 1 trailing
                        float angle = k * pitch + sweep + (1f - c) * span;             // rotation runs towards larger angles
                        float z = mid + (0.5f - c) * rise - thickness * 0.06f * Mathf.Sin(Mathf.PI * c);
                        grid[i, j] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z);
                    }
                }
                b.Sheet(0, grid);
            }
            return b.ToMesh("Rotor");
        }

        /// <summary>The closed well a shrouded fan of radius 1 turns in: a wall round it and a floor behind it.</summary>
        static Mesh WellMesh(float depth)
        {
            var b = new MeshBuilder(1);
            b.Tube(0, 1f, 0f, -depth, outward: false);
            b.Disc(0, 1f, -depth, Vector3.forward);
            return b.ToMesh("Well");
        }

        /// <summary>
        /// The frame of a standing fan of radius 1, front face at z 0, <paramref name="depth"/> deep: square outside,
        /// round inside, with the motor mount and four struts across its front (outlet) side.
        /// </summary>
        static Mesh FrameMesh(float depth)
        {
            const float radius = 1f;
            var b = new MeshBuilder(1);
            float half = radius * 1.06f;
            b.SquareRing(0, half, radius, 0f, Vector3.forward);
            b.SquareRing(0, half, radius, -depth, Vector3.back);
            b.Box(0, new Vector3(0f, half, -depth * 0.5f), new Vector3(half, 0f, depth * 0.5f), Vector3.up);
            b.Box(0, new Vector3(0f, -half, -depth * 0.5f), new Vector3(half, 0f, depth * 0.5f), Vector3.down);
            b.Box(0, new Vector3(half, 0f, -depth * 0.5f), new Vector3(0f, half, depth * 0.5f), Vector3.right);
            b.Box(0, new Vector3(-half, 0f, -depth * 0.5f), new Vector3(0f, half, depth * 0.5f), Vector3.left);
            b.Tube(0, radius, 0f, -depth, outward: false);

            float motor = radius * 0.36f, strutDepth = depth * 0.12f;
            b.Tube(0, motor, 0.001f * radius, -strutDepth, outward: true);
            b.Disc(0, motor, 0.001f * radius, Vector3.forward);
            for (int k = 0; k < 4; k++)
            {
                float angle = Mathf.PI * 0.25f + k * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                b.Strut(0, dir * motor * 0.95f, dir * radius * 1.01f, radius * 0.07f, 0.001f * radius, -strutDepth);
            }
            return b.ToMesh("Frame");
        }

        /// <summary>A flat ring of radius 1 facing +Z, with a little thickness so it still shows edge-on.</summary>
        static Mesh RingMesh()
        {
            var b = new MeshBuilder(1);
            const float inner = 0.94f, outer = 1.04f, half = 0.02f;
            b.Annulus(0, inner, outer, half, Vector3.forward);
            b.Annulus(0, inner, outer, -half, Vector3.back);
            b.Tube(0, outer, half, -half, outward: true);
            b.Tube(0, inner, half, -half, outward: false);
            return b.ToMesh("RGB Ring");
        }

        /// <summary>A fan material, created or brought up to date. <paramref name="glow"/> &gt; 0 lets RgbGlow tint it.</summary>
        static Material FanMaterial(string name, Color color, float smoothness, float metallic, float glow = 0f)
        {
            string path = $"Assets/_Project/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (glow > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white * glow);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The material every RGB LED wears. It glows white here; RgbGlow colours it while the app runs.</summary>
        static Material LedMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(LedMaterialPath);
            if (existing != null) return existing;

            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_LED_RGB" };
            m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            m.SetFloat("_Smoothness", 0.8f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white * 2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EnsureFolder(Path.GetDirectoryName(LedMaterialPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(m, LedMaterialPath);
            return m;
        }

        // ------------------------------------------------------------------ case lighting

        /// <summary>
        /// Lights the build like a gaming PC: an RGB strip along the top of the opening and a colour-cycling spot light
        /// washing into the case from above the opening, under a 'Lighting' child, on the same colour clock as the
        /// parts' fan rings and light bars. Adds CaseGlow so they bloom.
        /// </summary>
        static void AddCaseLighting(GameObject root, GameObject caseModel)
        {
            var old = root.transform.Find("Lighting");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            if (root.GetComponent<CaseGlow>() == null) root.AddComponent<CaseGlow>();
            if (!TryWorldBounds(caseModel, out Bounds b)) return;

            var lighting = new GameObject("Lighting").transform;
            lighting.SetParent(root.transform, false);

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "RGB Strip";
            Object.DestroyImmediate(strip.GetComponent<Collider>());
            strip.transform.SetParent(lighting, false);
            strip.transform.localPosition = new Vector3(b.center.x, b.max.y - 0.028f, b.min.z + 0.018f);   // just inside the top edge
            strip.transform.localScale = new Vector3(b.size.x - 0.08f, 0.006f, 0.006f);
            AddLed(strip, LedMaterial());

            // Real lights fade with the square of the distance, so a light inside the case burns out whatever is next
            // to it. From a little in front of the opening the whole interior is at a similar distance, and a weak
            // spot gives an even wash of colour.
            var glow = new GameObject("RGB Light");
            glow.transform.SetParent(lighting, false);
            glow.transform.localPosition = new Vector3(b.center.x, b.max.y - 0.03f, b.min.z - 0.1f);
            glow.transform.LookAt(glow.transform.parent.TransformPoint(new Vector3(b.center.x, b.min.y + 0.25f, b.center.z + 0.05f)));
            var light = glow.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 100f;
            light.innerSpotAngle = 60f;
            light.range = 0.9f;
            light.intensity = 0.045f;
            light.shadows = LightShadows.None;
            glow.AddComponent<RgbGlow>();
        }

        // ------------------------------------------------------------------ mesh building

        /// <summary>Small helper for the fan meshes: every face is wound to face the way it's asked to.</summary>
        sealed class MeshBuilder
        {
            const int Segments = 48;
            readonly List<Vector3> _positions = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<int>[] _triangles;

            public MeshBuilder(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
            }

            int Vertex(Vector3 p, Vector3 n) { _positions.Add(p); _normals.Add(n); return _positions.Count - 1; }

            // Unity draws a triangle's front where Cross(b - a, c - a) points.
            void Triangle(int sub, int a, int b, int c, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]), facing) < 0f)
                    (b, c) = (c, b);
                _triangles[sub].Add(a); _triangles[sub].Add(b); _triangles[sub].Add(c);
            }

            void Quad(int sub, int a, int b, int c, int d, Vector3 facing)
            {
                Triangle(sub, a, b, c, facing);
                Triangle(sub, a, c, d, facing);
            }

            static Vector3 Around(float angle) => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            static float Angle(int i) => i * Mathf.PI * 2f / Segments;

            /// <summary>A cylinder wall from z0 to z1, normals out of it or into it.</summary>
            public void Tube(int sub, float radius, float z0, float z1, bool outward)
            {
                for (int i = 0; i < Segments; i++)
                {
                    Vector3 d0 = Around(Angle(i)), d1 = Around(Angle(i + 1));
                    Vector3 n0 = outward ? d0 : -d0, n1 = outward ? d1 : -d1;
                    int a = Vertex(d0 * radius + Vector3.forward * z0, n0), b = Vertex(d0 * radius + Vector3.forward * z1, n0);
                    int c = Vertex(d1 * radius + Vector3.forward * z1, n1), d = Vertex(d1 * radius + Vector3.forward * z0, n1);
                    Quad(sub, a, b, c, d, (n0 + n1) * 0.5f);
                }
            }

            public void Disc(int sub, float radius, float z, Vector3 facing)
            {
                int centre = Vertex(Vector3.forward * z, facing);
                for (int i = 0; i < Segments; i++)
                    Triangle(sub, centre, Vertex(Around(Angle(i)) * radius + Vector3.forward * z, facing),
                             Vertex(Around(Angle(i + 1)) * radius + Vector3.forward * z, facing), facing);
            }

            public void Annulus(int sub, float inner, float outer, float z, Vector3 facing)
            {
                for (int i = 0; i < Segments; i++)
                {
                    Vector3 d0 = Around(Angle(i)), d1 = Around(Angle(i + 1)), zz = Vector3.forward * z;
                    Quad(sub, Vertex(d0 * inner + zz, facing), Vertex(d0 * outer + zz, facing),
                         Vertex(d1 * outer + zz, facing), Vertex(d1 * inner + zz, facing), facing);
                }
            }

            /// <summary>Flat face between a square of half-size <paramref name="half"/> and a circle inside it.</summary>
            public void SquareRing(int sub, float half, float radius, float z, Vector3 facing)
            {
                for (int i = 0; i < Segments; i++)
                {
                    Vector3 d0 = Around(Angle(i)), d1 = Around(Angle(i + 1)), zz = Vector3.forward * z;
                    Vector3 s0 = d0 * (half / Mathf.Max(Mathf.Abs(d0.x), Mathf.Abs(d0.y)));
                    Vector3 s1 = d1 * (half / Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.y)));
                    Quad(sub, Vertex(d0 * radius + zz, facing), Vertex(s0 + zz, facing),
                         Vertex(s1 + zz, facing), Vertex(d1 * radius + zz, facing), facing);
                }
            }

            /// <summary>One flat face of a box: centre, half extents along its two sides (one of them 0), facing.</summary>
            public void Box(int sub, Vector3 centre, Vector3 half, Vector3 facing)
            {
                Vector3 u, v;
                if (half.x == 0f) { u = new Vector3(0, half.y, 0); v = new Vector3(0, 0, half.z); }
                else if (half.y == 0f) { u = new Vector3(half.x, 0, 0); v = new Vector3(0, 0, half.z); }
                else { u = new Vector3(half.x, 0, 0); v = new Vector3(0, half.y, 0); }
                Quad(sub, Vertex(centre - u - v, facing), Vertex(centre + u - v, facing),
                     Vertex(centre + u + v, facing), Vertex(centre - u + v, facing), facing);
            }

            /// <summary>A flat bar from <paramref name="from"/> to <paramref name="to"/> (in the XY plane), z0..z1 deep.</summary>
            public void Strut(int sub, Vector3 from, Vector3 to, float width, float z0, float z1)
            {
                var along = (to - from).normalized;
                var side = new Vector3(-along.y, along.x, 0f) * (width * 0.5f);
                Vector3 f0 = Vector3.forward * z0, f1 = Vector3.forward * z1;
                Vector3[] c = { from - side, to - side, to + side, from + side };
                Quad(sub, Vertex(c[0] + f0, Vector3.forward), Vertex(c[1] + f0, Vector3.forward),
                     Vertex(c[2] + f0, Vector3.forward), Vertex(c[3] + f0, Vector3.forward), Vector3.forward);
                Quad(sub, Vertex(c[0] + f1, Vector3.back), Vertex(c[1] + f1, Vector3.back),
                     Vertex(c[2] + f1, Vector3.back), Vertex(c[3] + f1, Vector3.back), Vector3.back);
                var n = side.normalized;
                Quad(sub, Vertex(c[2] + f0, n), Vertex(c[3] + f0, n), Vertex(c[3] + f1, n), Vertex(c[2] + f1, n), n);
                Quad(sub, Vertex(c[0] + f0, -n), Vertex(c[1] + f0, -n), Vertex(c[1] + f1, -n), Vertex(c[0] + f1, -n), -n);
            }

            /// <summary>A two-sided curved sheet (a blade) through a grid of points.</summary>
            public void Sheet(int sub, Vector3[,] grid)
            {
                int rows = grid.GetLength(0), cols = grid.GetLength(1);
                for (int side = 0; side < 2; side++)
                {
                    var facing = side == 0 ? Vector3.forward : Vector3.back;
                    var index = new int[rows, cols];
                    for (int i = 0; i < rows; i++)
                        for (int j = 0; j < cols; j++)
                        {
                            var du = grid[Mathf.Min(i + 1, rows - 1), j] - grid[Mathf.Max(i - 1, 0), j];
                            var dv = grid[i, Mathf.Min(j + 1, cols - 1)] - grid[i, Mathf.Max(j - 1, 0)];
                            var n = Vector3.Cross(du, dv).normalized;
                            if (Vector3.Dot(n, facing) < 0f) n = -n;
                            index[i, j] = Vertex(grid[i, j], n);
                        }
                    for (int i = 0; i < rows - 1; i++)
                        for (int j = 0; j < cols - 1; j++)
                        {
                            var n = _normals[index[i, j]] + _normals[index[i + 1, j + 1]];
                            Quad(sub, index[i, j], index[i + 1, j], index[i + 1, j + 1], index[i, j + 1], n);
                        }
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (_positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.subMeshCount = _triangles.Length;
                for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
