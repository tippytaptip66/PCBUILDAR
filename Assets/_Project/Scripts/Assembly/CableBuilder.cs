using System.Collections.Generic;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Builds a connected cable the way a tidy real build runs it. A PSU cable leaves the PSU's connector panel;
    /// when the header has a route exit (a grommet in the motherboard tray, see CablePort.routeExit) the cable runs
    /// back behind the tray, along and up out of sight, comes through the grommet and plugs straight into the header.
    /// A header marked direct (a drive beside the PSU) gets the short way; a cable with an origin (a SATA data cable
    /// fitted to the board) drops from behind the tray to it. Otherwise it arcs across the front of the case. The
    /// cable itself is a flat loom of individually sleeved wires as wide as its connector (24-pin: 12 × 2), turned
    /// so it lines up with the header, and every plug goes on the way its header faces.
    /// Built in the case's local space so it moves with the case (AR placement, rotation, scale).
    /// </summary>
    public static class CableBuilder
    {
        const int Sides = 6;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Material _material;

        /// <summary>How a connector's wires are laid out: across × rows, and the sleeve radius of one wire.</summary>
        struct Loom
        {
            public int across, rows;
            public float wire;
            public Loom(int across, int rows, float wire) { this.across = across; this.rows = rows; this.wire = wire; }
            public float Pitch => wire * 2.15f;
        }

        static Loom LoomFor(string connectorType)
        {
            switch (connectorType)
            {
                case "ATX24": return new Loom(12, 2, 0.0016f);
                case "EPS8":
                case "PCIE8": return new Loom(4, 2, 0.0017f);
                case "SATA": return new Loom(5, 1, 0.0011f);
                case "SATADATA": return new Loom(7, 1, 0.00055f);   // a flat 8 mm data ribbon
                case "FPANEL": return new Loom(4, 1, 0.0008f);
                default: return new Loom(2, 1, 0.0015f);
            }
        }

        // Front-panel leads are thin wires in their usual colours: power switch, reset, and the two LED pairs.
        static readonly Color[] PanelWires =
        {
            new Color(0.75f, 0.12f, 0.1f), new Color(0.85f, 0.85f, 0.82f), new Color(0.12f, 0.55f, 0.2f), new Color(0.05f, 0.05f, 0.05f),
        };

        public static GameObject Build(CablePort source, CablePort target, Material fallback)
        {
            var caseRoot = CaseRootOf(source);
            var go = new GameObject($"Cable {source.portId} -> {target.portId}");
            go.transform.SetParent(caseRoot, false);

            Vector3 b = caseRoot.InverseTransformPoint(target.transform.position);
            Vector3 open = Vector3.back; // the case's open side faces local -Z
            // The way a plug goes onto this header, backwards: out of a board header towards the open side, out of a
            // drive's socket towards whatever feeds it. A port's forward points into its socket.
            Vector3 approach = caseRoot.InverseTransformDirection(-target.transform.forward).normalized;

            // Where the cable comes from. Its loose end hides once it's plugged in (it's the plug on the header now),
            // so the cable starts at what it belongs to: the PSU's panel, the header its other end is already fitted
            // to (origin), or the case's front panel for case cables.
            Vector3 start = caseRoot.InverseTransformPoint(source.transform.position);
            var route = new List<Vector3>();
            bool fromPsu = PsuPanel(caseRoot, source, out Vector3 panel, out Vector3 outward);
            if (fromPsu)
            {
                route.Add(panel);
                start = panel + outward * 0.02f;
            }
            else if (source.origin != null)
            {
                // A data cable coming down from the board behind the tray: drop behind it to the target's height first.
                start = caseRoot.InverseTransformPoint(source.origin.position);
                route.Add(start);
                start = new Vector3(start.x, b.y, start.z);
            }
            else if (string.IsNullOrEmpty(source.requiresSlotId)) route.Add(start + Vector3.right * 0.03f);

            if (target.direct) route.AddRange(new[] { start, b + approach * 0.035f, b });
            else if (target.routeExit == null) route.AddRange(Arced(start, b, approach));
            else if (fromPsu) route.AddRange(BehindTray(start, b, caseRoot.InverseTransformPoint(target.routeExit.position), open, approach));
            else route.AddRange(AlongTray(start, b, approach));

            Dress(go, source, route, HeaderAxis(caseRoot, target), fallback);
            AddPlug(caseRoot, go.transform, target, b, approach, source.cableColor);
            return go;
        }

        /// <summary>
        /// The cable hanging from what it belongs to — the PSU's panel, the board behind the tray (its origin) or the
        /// front panel — to its loose end, before it's plugged in, so a cable end never floats in mid-air. Parented to
        /// the loose end so it shows and hides with it. Null if there's nothing for it to hang from.
        /// </summary>
        public static GameObject BuildSlack(CablePort source)
        {
            var caseRoot = CaseRootOf(source);
            Vector3 end = caseRoot.InverseTransformPoint(source.transform.position);
            var route = new List<Vector3>();
            if (PsuPanel(caseRoot, source, out Vector3 panel, out Vector3 outward))
            {
                // Out of the panel, up beside it, then over to where the end rests on top of the PSU.
                Vector3 start = panel + outward * 0.02f;
                route.AddRange(new[] { panel, start, new Vector3(start.x, end.y, start.z), end });
            }
            else if (source.origin != null)
            {
                // Down from the board behind the tray, then forward out to where the end hangs.
                Vector3 origin = caseRoot.InverseTransformPoint(source.origin.position);
                route.AddRange(new[] { origin, new Vector3(origin.x, end.y, origin.z), end });
            }
            else if (string.IsNullOrEmpty(source.requiresSlotId))
                route.AddRange(new[] { end + Vector3.right * 0.03f, end });   // out of the front panel
            else return null;

            var visual = source.transform.Find("Visual");
            var axis = caseRoot.InverseTransformDirection(visual != null ? visual.right : source.transform.right);
            var go = new GameObject("Slack");
            go.transform.SetParent(caseRoot, false);
            Dress(go, source, route, axis, null);
            go.transform.SetParent(source.transform, true);
            return go;
        }

        /// <summary>Gives <paramref name="go"/> the source's cable along the route: a rounded loom of its wires, in its colours.</summary>
        static void Dress(GameObject go, CablePort source, List<Vector3> route, Vector3 headerAxis, Material fallback)
        {
            var loom = LoomFor(source.connectorType);
            var path = Rounded(route, Mathf.Max(0.012f, loom.Pitch * loom.across * 0.5f));
            var colours = source.connectorType == "FPANEL" ? PanelWires : new[] { source.cableColor };

            go.AddComponent<MeshFilter>().sharedMesh = LoomMesh(path, loom, headerAxis, colours.Length);
            var renderer = go.AddComponent<MeshRenderer>();
            var material = MaterialFor(fallback);
            var materials = new Material[colours.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            for (int i = 0; i < colours.Length; i++)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, colours[i]);
                block.SetColor(ColorId, colours[i]);
                renderer.SetPropertyBlock(block, i);
            }
        }

        static Transform CaseRootOf(CablePort port)
        {
            var ports = port.transform.parent; // CablePorts
            return ports != null && ports.parent != null ? ports.parent : port.transform.root;
        }

        // ------------------------------------------------------------------ routes

        /// <summary>
        /// From just off the PSU's panel: back to a couple of centimetres behind the tray, up beside the panel (never
        /// through the PSU) and across behind the tray to the grommet, out through it, and into the header from the front.
        /// </summary>
        static List<Vector3> BehindTray(Vector3 start, Vector3 header, Vector3 exit, Vector3 open, Vector3 approach)
        {
            float behind = exit.z - open.z * 0.022f;
            return new List<Vector3>
            {
                start,
                new Vector3(start.x, start.y, behind),
                new Vector3(start.x, exit.y, behind),
                new Vector3(exit.x, exit.y, behind),
                exit,
                exit + open * 0.016f,
                header + approach * 0.034f,
                header,
            };
        }

        /// <summary>
        /// A case cable (the front panel's) already comes out in front of the tray, so it runs low and close along
        /// the tray to its header instead of going behind it.
        /// </summary>
        static List<Vector3> AlongTray(Vector3 start, Vector3 header, Vector3 approach)
        {
            Vector3 front = header + approach * 0.03f;
            return new List<Vector3> { start, new Vector3(start.x, start.y, front.z), front, header };
        }

        /// <summary>The old way, for a case with no route exits: out from the start and across the front.</summary>
        static List<Vector3> Arced(Vector3 a, Vector3 b, Vector3 approach)
        {
            Vector3 open = Vector3.back;
            float front = Mathf.Min(a.z, b.z) - 0.06f;
            return new List<Vector3>
            {
                a,
                a + open * 0.025f,
                new Vector3(Mathf.Lerp(a.x, b.x, 0.25f), Mathf.Lerp(a.y, b.y, 0.3f) - 0.03f, front),
                new Vector3(Mathf.Lerp(a.x, b.x, 0.8f), Mathf.Lerp(a.y, b.y, 0.8f), front + 0.01f),
                b + approach * 0.045f,
                b,
            };
        }

        /// <summary>
        /// Where a PSU cable leaves the PSU: its modular panel, on the end facing into the case (+X), each connector
        /// type at its own spot on it. False if the cable doesn't hang off a PSU in a bay.
        /// </summary>
        static bool PsuPanel(Transform caseRoot, CablePort source, out Vector3 panel, out Vector3 outward)
        {
            panel = outward = Vector3.zero;
            if (string.IsNullOrEmpty(source.requiresSlotId)) return false;
            foreach (var slot in caseRoot.GetComponentsInChildren<SnapSlot>(true))
            {
                if (slot.slotId != source.requiresSlotId || slot.acceptsCategory != ComponentCategory.PowerSupply) continue;
                Vector3 centre = caseRoot.InverseTransformPoint(slot.transform.position);
                outward = caseRoot.InverseTransformDirection(slot.transform.right).normalized;
                Vector3 up = caseRoot.InverseTransformDirection(slot.transform.up).normalized;
                Vector3 side = Vector3.Cross(outward, up);
                Vector2 at;
                switch (source.connectorType)
                {
                    case "ATX24": at = new Vector2(0.018f, -0.022f); break;
                    case "EPS8": at = new Vector2(-0.006f, -0.036f); break;
                    case "PCIE8": at = new Vector2(-0.006f, 0.0f); break;
                    default: at = new Vector2(-0.022f, -0.02f); break;
                }
                panel = centre + outward * 0.077f + up * at.x + side * at.y;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A path through the route's points with every corner rounded off (a bend of up to <paramref name="bend"/>),
        /// sampled every few millimetres, the way a stiff cable actually bends.
        /// </summary>
        static List<Vector3> Rounded(IReadOnlyList<Vector3> route, float bend)
        {
            var pts = new List<Vector3> { route[0] };
            for (int i = 1; i < route.Count; i++)
                if ((route[i] - pts[pts.Count - 1]).sqrMagnitude > 1e-8f) pts.Add(route[i]);

            var result = new List<Vector3>();
            void Line(Vector3 from, Vector3 to)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.006f));
                for (int s = 0; s < n; s++) result.Add(Vector3.Lerp(from, to, s / (float)n));
            }

            Vector3 cursor = pts[0];
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 inDir = pts[i] - pts[i - 1], outDir = pts[i + 1] - pts[i];
                float r = Mathf.Min(bend, inDir.magnitude * 0.45f, outDir.magnitude * 0.45f);
                Vector3 a = pts[i] - inDir.normalized * r, c = pts[i] + outDir.normalized * r;
                Line(cursor, a);
                for (int s = 0; s < 8; s++)
                {
                    float t = s / 8f;
                    result.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * pts[i] + t * t * c);
                }
                cursor = c;
            }
            Line(cursor, pts[pts.Count - 1]);
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        /// <summary>The header's long side in case space: the loom lies flat along it where it plugs in.</summary>
        static Vector3 HeaderAxis(Transform caseRoot, CablePort target)
        {
            var visual = target.transform.Find("Visual");
            Vector3 size = visual != null ? visual.localScale : Vector3.one;
            Vector3 local = size.x >= size.y ? Vector3.right : Vector3.up;
            return caseRoot.InverseTransformDirection((visual != null ? visual : target.transform).TransformDirection(local)).normalized;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>
        /// One sleeved tube per wire, side by side. The frame along the path is carried back from the header end, so
        /// the loom lies flat along the header where it plugs in and just follows the path everywhere else. Wires
        /// cycle through <paramref name="colours"/> submeshes.
        /// </summary>
        static Mesh LoomMesh(List<Vector3> points, Loom loom, Vector3 headerAxis, int colours)
        {
            int n = points.Count;
            var tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
                tangents[i] = (points[Mathf.Min(i + 1, n - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;

            var sides = new Vector3[n];
            Vector3 side = Vector3.ProjectOnPlane(headerAxis, tangents[n - 1]);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(tangents[n - 1], Vector3.up);
            for (int i = n - 1; i >= 0; i--)
            {
                side = Vector3.ProjectOnPlane(side, tangents[i]);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(tangents[i], Mathf.Abs(tangents[i].y) < 0.9f ? Vector3.up : Vector3.right);
                sides[i] = side.normalized;
                side = sides[i];
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>[colours];
            for (int c = 0; c < colours; c++) triangles[c] = new List<int>();

            int wireIndex = 0;
            for (int row = 0; row < loom.rows; row++)
                for (int col = 0; col < loom.across; col++, wireIndex++)
                {
                    float u = (col - (loom.across - 1) * 0.5f) * loom.Pitch;
                    float v = (row - (loom.rows - 1) * 0.5f) * loom.Pitch;
                    int start = vertices.Count;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 up = Vector3.Cross(tangents[i], sides[i]);
                        Vector3 centre = points[i] + sides[i] * u + up * v;
                        for (int s = 0; s <= Sides; s++)
                        {
                            float angle = s * Mathf.PI * 2f / Sides;
                            Vector3 dir = sides[i] * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                            vertices.Add(centre + dir * loom.wire);
                            normals.Add(dir);
                        }
                    }
                    var list = triangles[wireIndex % colours];
                    int ring = Sides + 1;
                    for (int i = 0; i < n - 1; i++)
                        for (int s = 0; s < Sides; s++)
                        {
                            int a = start + i * ring + s, b = a + ring;
                            list.Add(a); list.Add(b); list.Add(a + 1);
                            list.Add(a + 1); list.Add(b); list.Add(b + 1);
                        }
                }

            var mesh = new Mesh { name = "Cable" };
            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = colours;
            for (int c = 0; c < colours; c++) mesh.SetTriangles(triangles[c], c);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material MaterialFor(Material fallback)
        {
            if (_material != null) return _material;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return fallback;
            _material = new Material(lit) { name = "Cable (runtime)" };
            _material.SetFloat("_Smoothness", 0.45f);   // woven sleeving has a soft sheen
            _material.SetFloat("_Cull", 0f); // tube ends are open
            return _material;
        }

        /// <summary>
        /// Connector housing pushed onto the header, so the cable end doesn't just vanish into the board. It's turned
        /// the way the header is and sits out along <paramref name="approach"/>.
        /// </summary>
        static void AddPlug(Transform caseRoot, Transform parent, CablePort target, Vector3 headerLocal, Vector3 approach, Color color)
        {
            var visual = target.transform.Find("Visual");
            Vector3 size = visual != null ? visual.localScale : Vector3.one * 0.01f;
            var plug = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plug.name = "Plug";
            Object.Destroy(plug.GetComponent<Collider>());
            plug.transform.SetParent(parent, false);
            plug.transform.localRotation = Quaternion.Inverse(caseRoot.rotation) * target.transform.rotation;
            plug.transform.localScale = new Vector3(size.x * 1.2f + 0.002f, size.y * 1.2f + 0.002f, 0.012f);
            plug.transform.localPosition = headerLocal + approach * (size.z * 0.5f + 0.006f);
            var r = plug.GetComponent<MeshRenderer>();
            r.sharedMaterial = MaterialFor(null) ?? r.sharedMaterial;
            var block = new MaterialPropertyBlock();
            var housing = Color.Lerp(color, Color.black, 0.5f);
            block.SetColor(BaseColorId, housing);
            block.SetColor(ColorId, housing);
            r.SetPropertyBlock(block);
        }
    }
}
