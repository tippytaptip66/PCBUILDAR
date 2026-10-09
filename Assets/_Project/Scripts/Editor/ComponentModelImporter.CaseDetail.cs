using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.EditorTools
{
    /// <summary>Case detail: see BakeCaseDetail.</summary>
    public static partial class ComponentModelImporter
    {
        /// <summary>
        /// Generated cases come with a flat, featureless colour map, so the case read as plain grey plastic. This
        /// paints the detail of a real case into its own textures — colour, normal map, metallic/smoothness — working
        /// out what every texel is from where it lands on the fitted case, so nothing depends on how the generator laid
        /// out its texture atlas:
        ///   • fine powder-coat grain everywhere;
        ///   • perforated dust-filter mesh on the front and top panels, inside and out;
        ///   • vent slots and a raised BuildAR logo on the PSU shroud, if the case has one;
        ///   • rubber cable-management grommets and tie-down slots on the motherboard tray beside the board, where
        ///     the cables come through (see AddCableRoutes);
        ///   • a filtered intake vent in the floor under the PSU, whose fan faces down.
        /// Runs on the fitted, recentred case (metres, open side towards -Z); <paramref name="floor"/> is the floor
        /// the PSU stands on and <paramref name="psu"/> its footprint (x, z). <paramref name="source"/> is the model's
        /// original mesh, whose UVs the textures follow. Returns a note for the report, or null.
        /// </summary>
        static string BakeCaseDetail(GameObject caseModel, Mesh source, string modelPath, float trayZ, Rect board, float floor, Rect psu)
        {
            if (source == null || !source.isReadable) return null;
            var filter = caseModel.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null)
                .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude)
                .FirstOrDefault();
            if (filter == null || !TryWorldBounds(caseModel, out Bounds caseBounds)) return null;

            string folder = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            string prefix = $"{folder}/Textures/{Path.GetFileNameWithoutExtension(modelPath)}";
            string basePath = prefix + "_BaseMap.png", normalPath = prefix + "_Normal.png", maskPath = prefix + "_MetallicSmoothness.png";
            if (!File.Exists(basePath)) return null;

            var colour = LoadPixels(basePath, out int size);
            var normal = File.Exists(normalPath) ? LoadPixels(normalPath, out int ns) : null;
            var mask = File.Exists(maskPath) ? LoadPixels(maskPath, out int ms) : null;
            if (colour == null) return null;
            if (normal != null && normal.Length != colour.Length) normal = null;
            if (mask == null || mask.Length != colour.Length)
                mask = Enumerable.Repeat(new Color32(40, 40, 40, 90), colour.Length).ToArray();
            if (normal == null) normal = Enumerable.Repeat(new Color32(128, 128, 255, 255), colour.Length).ToArray();

            var surface = new CaseSurface(filter.transform.localToWorldMatrix, source, size);
            var look = new CaseLook(caseBounds, trayZ, board, FindShroud(surface, caseBounds, trayZ), floor, psu);

            var painted = new HashSet<CaseLook.Region>();
            var covered = new bool[colour.Length];
            for (int i = 0; i < colour.Length; i++)
            {
                if (!surface.At(i, out Vector3 p, out Vector3 n, out Vector3 tangent, out Vector3 bitangent, out float texel)) continue;
                covered[i] = true;

                var region = look.Classify(p, n);
                var here = look.Sample(p, region, texel);
                painted.Add(region);

                // Bump from the height, sampled a little way along the surface each way.
                const float step = 0.00035f;
                float du = (look.Sample(p + tangent * step, region, texel).height - here.height) / step;
                float dv = (look.Sample(p + bitangent * step, region, texel).height - here.height) / step;
                du = Mathf.Clamp(du, -3f, 3f);
                dv = Mathf.Clamp(dv, -3f, 3f);

                var c = colour[i];
                colour[i] = new Color32(Tint(c.r, here.paint.r, here), Tint(c.g, here.paint.g, here), Tint(c.b, here.paint.b, here), 255);

                var m = normal[i];
                var baked = new Vector3(m.r / 127.5f - 1f, m.g / 127.5f - 1f, m.b / 127.5f - 1f);
                var blended = new Vector3(baked.x - du, baked.y - dv, Mathf.Max(0.05f, baked.z)).normalized;
                normal[i] = new Color32(Encode(blended.x), Encode(blended.y), Encode(blended.z), 255);

                mask[i] = new Color32(Byte(here.metallic), Byte(here.metallic), Byte(here.metallic), Byte(here.smoothness));
            }
            Dilate(covered, size, colour, normal, mask);

            SaveTexture(basePath, EncodePng(colour, size, false), size, TextureImporterType.Default, true);
            SaveTexture(normalPath, EncodePng(normal, size, true), size, TextureImporterType.NormalMap, false);
            SaveTexture(maskPath, EncodePng(mask, size, true), size, TextureImporterType.Default, false);

            var features = new List<string> { "powder-coat grain" };
            if (painted.Contains(CaseLook.Region.FrontMesh) || painted.Contains(CaseLook.Region.TopMesh)) features.Add("mesh front and top panels");
            if (painted.Contains(CaseLook.Region.Shroud)) features.Add("vented PSU shroud with a BuildAR logo");
            if (painted.Contains(CaseLook.Region.Tray)) features.Add("cable grommets on the tray");
            if (painted.Contains(CaseLook.Region.FloorVent)) features.Add("a PSU intake vent in the floor");
            return string.Join(", ", features);
        }

        /// <summary>
        /// The model file of the PC Case component when it's the very same file as <paramref name="model"/> (the
        /// assembly case is normally a copy of it), with that component. Null if there's no such twin.
        /// </summary>
        static string CaseComponentModel(string model, out ComponentDefinitionSO def)
        {
            def = null;
            var components = AssetDatabase.FindAssets("t:ComponentDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null && c.category == ComponentCategory.Case)
                .ToList();
            if (components.Count == 0) return null;

            var bytes = File.ReadAllBytes(model);
            foreach (var path in ModelFiles(ModelsRoot))
            {
                var match = Match(path, components);
                if (match == null || new FileInfo(path).Length != bytes.Length || !File.ReadAllBytes(path).SequenceEqual(bytes)) continue;
                def = match;
                return path;
            }
            return null;
        }

        /// <summary>Gives the twin model (same file, same UVs) the detailed textures just painted for <paramref name="model"/>.</summary>
        static void ShareTextures(string model, string twin)
        {
            string From(string suffix) => $"{Path.GetDirectoryName(model)?.Replace('\\', '/')}/Textures/{Path.GetFileNameWithoutExtension(model)}{suffix}";
            string To(string suffix) => $"{Path.GetDirectoryName(twin)?.Replace('\\', '/')}/Textures/{Path.GetFileNameWithoutExtension(twin)}{suffix}";
            foreach (var (suffix, type, srgb) in new[]
                     {
                         ("_BaseMap.png", TextureImporterType.Default, true),
                         ("_Normal.png", TextureImporterType.NormalMap, false),
                         ("_MetallicSmoothness.png", TextureImporterType.Default, false),
                     })
            {
                if (!File.Exists(From(suffix)) || !File.Exists(To(suffix))) continue;
                var png = File.ReadAllBytes(From(suffix));
                SaveTexture(To(suffix), png, 2048, type, srgb);
            }
        }

        static byte Tint(byte channel, float paint, CaseLook.Finish s) =>
            (byte)Mathf.Clamp(Mathf.Lerp(Flatten(channel) * s.grain, paint * 255f, s.paintAmount), 0f, 255f);

        /// <summary>
        /// Evens out the blotches and pale smudges the generator bakes into a case's colour map (lighting it imagined,
        /// which real light shows up as camouflage): differences from the painted steel's typical value are squeezed
        /// hard and capped, leaving a faint unevenness under the powder-coat grain. The painted detail replaces
        /// whatever real features the smudges could have been.
        /// </summary>
        static float Flatten(byte channel)
        {
            const float mid = SteelMid, target = 36f, knee = 15f;
            float d = channel - mid, a = Mathf.Abs(d);
            float kept = a < knee ? a * 0.3f : knee * 0.3f + Mathf.Min((a - knee) * 0.15f, 10f);
            return target + Mathf.Sign(d) * kept;
        }

        /// <summary>
        /// Spreads the painted texels a few pixels into the empty space round each island of the atlas, so the
        /// smaller mip levels, which blend across island edges, don't pick up the old unpainted colours as seams.
        /// </summary>
        static void Dilate(bool[] covered, int size, params Color32[][] maps)
        {
            var filled = (bool[])covered.Clone();
            var next = new List<(int to, int from)>();
            for (int pass = 0; pass < 8; pass++)
            {
                next.Clear();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        if (filled[i]) continue;
                        if (x > 0 && filled[i - 1]) next.Add((i, i - 1));
                        else if (x < size - 1 && filled[i + 1]) next.Add((i, i + 1));
                        else if (y > 0 && filled[i - size]) next.Add((i, i - size));
                        else if (y < size - 1 && filled[i + size]) next.Add((i, i + size));
                    }
                foreach (var (to, from) in next)
                {
                    foreach (var map in maps) map[to] = map[from];
                    filled[to] = true;
                }
            }
        }

        static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        static byte Encode(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v * 0.5f + 0.5f) * 255f), 0, 255);

        static Color32[] LoadPixels(string path, out int size)
        {
            size = 0;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path)) || tex.width != tex.height) return null;
                size = tex.width;
                return tex.GetPixels32();
            }
            finally { Object.DestroyImmediate(tex); }
        }

        static byte[] EncodePng(Color32[] pixels, int size, bool linear)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            try
            {
                tex.SetPixels32(pixels);
                return tex.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// Height of the PSU shroud's top — the biggest upward-facing surface in the case's lower half, in front of
        /// the tray — and where its front edge is. Null if the case has none.
        /// </summary>
        static Vector2? FindShroud(CaseSurface surface, Bounds b, float trayZ)
        {
            var area = new Dictionary<int, float>();
            var fronts = new Dictionary<int, List<float>>();
            foreach (var (centre, n, a) in surface.Faces())
            {
                if (n.y < 0.85f || centre.y < b.min.y + 0.06f || centre.y > b.min.y + 0.26f) continue;
                if (centre.z < b.min.z + 0.01f || centre.z > trayZ - 0.01f) continue;
                if (centre.x < b.min.x + 0.03f || centre.x > b.max.x - 0.03f) continue;
                int bin = Mathf.FloorToInt(centre.y / 0.004f);
                area[bin] = (area.TryGetValue(bin, out float s) ? s : 0f) + a;
                if (!fronts.TryGetValue(bin, out var list)) fronts[bin] = list = new List<float>();
                list.Add(centre.z);
            }
            if (area.Count == 0) return null;
            var best = area.OrderByDescending(p => p.Value).First();
            if (best.Value < 0.012f) return null;
            var z = fronts[best.Key];
            z.Sort();
            return new Vector2((best.Key + 0.5f) * 0.004f, z[z.Count / 50]);
        }

        // ------------------------------------------------------------------ where each texel is

        /// <summary>
        /// The case's surface laid out on its texture: for every texel the triangle covering it, so its position,
        /// normal and surface directions on the fitted case can be looked up.
        /// </summary>
        sealed class CaseSurface
        {
            readonly Vector3[] _p, _n;
            readonly int[] _tris;
            readonly int[] _owner;
            readonly float[] _w1, _w2;
            readonly Vector3[] _tangent, _bitangent;
            readonly float[] _texel;

            public CaseSurface(Matrix4x4 toWorld, Mesh mesh, int size)
            {
                var positions = mesh.vertices;
                var normals = mesh.normals;
                var uvs = mesh.uv;
                _p = positions.Select(p => toWorld.MultiplyPoint3x4(p)).ToArray();
                _n = normals.Length == positions.Length
                    ? normals.Select(n => toWorld.MultiplyVector(n).normalized).ToArray()
                    : new Vector3[positions.Length];
                _tris = Enumerable.Range(0, mesh.subMeshCount).SelectMany(s => mesh.GetTriangles(s)).ToArray();

                int faces = _tris.Length / 3;
                _tangent = new Vector3[faces];
                _bitangent = new Vector3[faces];
                _texel = new float[faces];
                _owner = Enumerable.Repeat(-1, size * size).ToArray();
                _w1 = new float[size * size];
                _w2 = new float[size * size];
                if (uvs.Length != positions.Length) return;

                for (int f = 0; f < faces; f++)
                {
                    int a = _tris[f * 3], b = _tris[f * 3 + 1], c = _tris[f * 3 + 2];
                    Vector2 ua = uvs[a] * size - Vector2.one * 0.5f, ub = uvs[b] * size - Vector2.one * 0.5f, uc = uvs[c] * size - Vector2.one * 0.5f;
                    float det = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                    if (Mathf.Abs(det) < 1e-8f) continue;

                    // Which way the texture's u and v run across this face, and how big a texel is on it.
                    Vector3 e1 = _p[b] - _p[a], e2 = _p[c] - _p[a];
                    Vector2 d1 = ub - ua, d2 = uc - ua;
                    var normal = Vector3.Cross(e1, e2).normalized;
                    var t = (e1 * d2.y - e2 * d1.y) / det;
                    var bt = (e2 * d1.x - e1 * d2.x) / det;
                    _tangent[f] = Vector3.ProjectOnPlane(t, normal).normalized;
                    _bitangent[f] = Vector3.ProjectOnPlane(bt, normal).normalized;
                    _texel[f] = Mathf.Sqrt(Vector3.Cross(e1, e2).magnitude / Mathf.Abs(det));

                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, ub.x, uc.x)));
                    int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.x, ub.x, uc.x)));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, ub.y, uc.y)));
                    int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.y, ub.y, uc.y)));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float w1 = ((x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (y - ua.y)) / det;
                            float w2 = ((ub.x - ua.x) * (y - ua.y) - (x - ua.x) * (ub.y - ua.y)) / det;
                            if (w1 < -0.002f || w2 < -0.002f || w1 + w2 > 1.002f) continue;
                            int i = y * size + x;
                            _owner[i] = f;
                            _w1[i] = w1;
                            _w2[i] = w2;
                        }
                }
            }

            public bool At(int texel, out Vector3 p, out Vector3 n, out Vector3 tangent, out Vector3 bitangent, out float size)
            {
                int f = _owner[texel];
                p = n = tangent = bitangent = default;
                size = 0f;
                if (f < 0) return false;
                int a = _tris[f * 3], b = _tris[f * 3 + 1], c = _tris[f * 3 + 2];
                float w1 = _w1[texel], w2 = _w2[texel], w0 = 1f - w1 - w2;
                p = _p[a] * w0 + _p[b] * w1 + _p[c] * w2;
                n = (_n[a] * w0 + _n[b] * w1 + _n[c] * w2).normalized;
                tangent = _tangent[f];
                bitangent = _bitangent[f];
                size = _texel[f];
                return tangent != Vector3.zero;
            }

            /// <summary>Every face's centre, outward normal and area.</summary>
            public IEnumerable<(Vector3 centre, Vector3 normal, float area)> Faces()
            {
                for (int f = 0; f < _tris.Length / 3; f++)
                {
                    Vector3 a = _p[_tris[f * 3]], b = _p[_tris[f * 3 + 1]], c = _p[_tris[f * 3 + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    float twice = cross.magnitude;
                    if (twice < 1e-12f) continue;
                    yield return ((a + b + c) / 3f, cross / twice, twice * 0.5f);
                }
            }
        }

        // ------------------------------------------------------------------ what each texel looks like

        /// <summary>The finish and features of a real case, as functions of position on the fitted case.</summary>
        sealed class CaseLook
        {
            public enum Region { Paint, FrontMesh, TopMesh, Shroud, Tray, FloorVent }

            public struct Finish
            {
                public float height;       // metres, out of the surface
                public float grain;        // multiplies the colour map
                public Color paint;        // colour painted over it…
                public float paintAmount;  // …and how much
                public float smoothness, metallic;
            }

            readonly Bounds _b;
            readonly float _trayZ;
            readonly Rect _board;
            readonly Vector2? _shroud;   // top height, front edge z
            readonly float _floor;
            readonly Rect _psu;          // x, z

            public CaseLook(Bounds caseBounds, float trayZ, Rect board, Vector2? shroud, float floor, Rect psu)
            {
                _b = caseBounds;
                _trayZ = trayZ;
                _board = board;
                _shroud = shroud;
                _floor = floor;
                _psu = psu;
            }

            public Region Classify(Vector3 p, Vector3 n)
            {
                var b = _b;
                bool inFrontFrame = p.y > b.min.y + 0.045f && p.y < b.max.y - 0.03f && p.z > b.min.z + 0.018f && p.z < b.max.z - 0.018f;
                if (inFrontFrame && ((n.x > 0.75f && p.x > b.max.x - 0.012f) || (n.x < -0.75f && p.x > b.max.x - 0.035f && p.x < b.max.x - 0.004f)))
                    return Region.FrontMesh;

                bool inTopFrame = p.x > b.min.x + 0.06f && p.x < b.max.x - 0.05f && p.z > b.min.z + 0.03f && p.z < b.max.z - 0.03f;
                if (inTopFrame && ((n.y > 0.75f && p.y > b.max.y - 0.012f) || (n.y < -0.75f && p.y > b.max.y - 0.06f && p.y < b.max.y - 0.015f)))
                    return Region.TopMesh;

                if (_shroud.HasValue && n.y > 0.75f && Mathf.Abs(p.y - _shroud.Value.x) < 0.006f) return Region.Shroud;
                if (n.z < -0.75f && Mathf.Abs(p.z - _trayZ) < 0.012f) return Region.Tray;

                // The floor slab under the PSU, top and underside (the fan faces down and breathes through it).
                bool underPsu = p.x > _psu.xMin + 0.012f && p.x < _psu.xMax - 0.012f && p.z > _psu.yMin + 0.012f && p.z < _psu.yMax - 0.012f;
                if (underPsu && Mathf.Abs(n.y) > 0.75f && p.y < _floor + 0.008f && p.y > _floor - 0.03f) return Region.FloorVent;
                return Region.Paint;
            }

            public Finish Sample(Vector3 p, Region region, float texel)
            {
                float aa = Mathf.Max(0.0002f, texel * 0.75f);
                float g = Noise(p / 0.0016f), g2 = Noise(p / 0.0045f + Vector3.one * 17.3f);
                var s = new Finish
                {
                    height = 0.00004f * g,
                    grain = 0.93f + 0.14f * g2,
                    paint = Color.black,
                    paintAmount = 0f,
                    smoothness = 0.3f + 0.1f * g,
                    metallic = 0.12f,
                };

                switch (region)
                {
                    case Region.FrontMesh: Perforate(ref s, HexHole(p.z, p.y, 0.0048f), 0.0017f, aa); break;
                    case Region.TopMesh: Perforate(ref s, HexHole(p.x, p.z, 0.0042f), 0.0015f, aa); break;
                    case Region.Shroud: Shroud(ref s, p, aa); break;
                    case Region.Tray: Tray(ref s, p, aa); break;
                    case Region.FloorVent: Perforate(ref s, HexHole(p.x, p.z, 0.0052f), 0.0019f, aa); break;
                }
                return s;
            }

            /// <summary>A hole at distance <paramref name="d"/> from its centre: dark, matte and sunk.</summary>
            static void Perforate(ref Finish s, float d, float radius, float aa)
            {
                float hole = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius + aa, radius - aa, d));
                s.height -= 0.0012f * hole;
                s.paint = new Color(0.008f, 0.008f, 0.01f);
                s.paintAmount = Mathf.Max(s.paintAmount, hole);
                s.smoothness = Mathf.Lerp(s.smoothness, 0.05f, hole);
                s.metallic = Mathf.Lerp(s.metallic, 0f, hole);
            }

            /// <summary>Distance to the nearest hole centre of a hexagonal perforation with this pitch.</summary>
            static float HexHole(float a, float c, float pitch)
            {
                float rowHeight = pitch * 0.8660254f;
                int row = Mathf.RoundToInt(c / rowHeight);
                float best = float.MaxValue;
                for (int r = row - 1; r <= row + 1; r++)
                {
                    float offset = (r & 1) != 0 ? pitch * 0.5f : 0f;
                    float column = Mathf.Round((a - offset) / pitch) * pitch + offset;
                    best = Mathf.Min(best, Mathf.Sqrt((a - column) * (a - column) + (c - r * rowHeight) * (c - r * rowHeight)));
                }
                return best;
            }

            /// <summary>The shroud's top: a block of vent slots over the PSU, and the logo along its front edge.</summary>
            void Shroud(ref Finish s, Vector3 p, float aa)
            {
                float front = _shroud.Value.y;

                // Vent slots, 5 mm wide and 55 mm long, running back from near the front edge over the PSU end.
                float x0 = _b.min.x + 0.04f;
                const float pitch = 0.0095f; const int slots = 14;
                float column = Mathf.Clamp(Mathf.Round((p.x - x0) / pitch), 0, slots - 1);
                float d = RoundedBox(new Vector2(p.x - (x0 + column * pitch), p.z - (front + 0.05f)), new Vector2(0.0024f, 0.0275f), 0.0024f);
                Perforate(ref s, d + 0.0017f, 0.0017f, aa);

                // "BUILDAR" in raised letters, 16 mm tall, reading from the open side.
                const float letter = 0.016f;
                float cell = letter / 7f;
                float textWidth = (Logo.Length * 6 - 1) * cell;
                float left = Mathf.Max(x0 + slots * pitch + 0.03f, (_b.max.x - 0.03f + x0 + slots * pitch) * 0.5f - textWidth * 0.5f);
                float ink = LogoInk((p.x - left) / cell, (p.z - (front + 0.012f)) / cell, aa / cell);
                s.height += 0.0004f * ink;
                s.paint = Color.Lerp(s.paint, new Color(0.62f, 0.64f, 0.68f), ink);
                s.paintAmount = Mathf.Max(s.paintAmount, ink);
                s.smoothness = Mathf.Lerp(s.smoothness, 0.55f, ink);
                s.metallic = Mathf.Lerp(s.metallic, 0.6f, ink);
            }

            /// <summary>
            /// The tray beside the board's front edge: three rubber grommets for the cables, each with its slit and
            /// fingers, and a row of tie-down slots.
            /// </summary>
            void Tray(ref Finish s, Vector3 p, float aa)
            {
                float cx = _board.xMax + GrommetGap;   // where AddCableRoutes brings the cables through
                if (cx + 0.02f > _b.max.x - 0.03f) return;
                var half = new Vector2(0.0115f, 0.032f);
                foreach (float t in GrommetHeights)
                {
                    var local = new Vector2(p.x - cx, p.y - Mathf.Lerp(_board.yMin, _board.yMax, t));
                    float d = RoundedBox(local, half, 0.009f);
                    float rubber = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(aa, -aa, d));
                    if (rubber <= 0f) continue;

                    // A rolled rim, then fingers sloping in to the slit down the middle.
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 0.0018f) / 0.0018f);
                    float finger = Mathf.Abs(Mathf.Repeat(local.y, 0.004f) - 0.002f) < 0.00035f && Mathf.Abs(local.x) < half.x - 0.003f ? 1f : 0f;
                    float slit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0007f + aa, 0.0007f - aa, Mathf.Abs(local.x))) *
                                 (Mathf.Abs(local.y) < half.y - 0.004f ? 1f : 0f);
                    s.height += rubber * (0.0012f * rim + 0.0006f * (1f - Mathf.Abs(local.x) / half.x)) - 0.0008f * (slit + 0.5f * finger);
                    s.paint = Color.Lerp(new Color(0.05f, 0.05f, 0.055f), new Color(0.01f, 0.01f, 0.01f), Mathf.Max(slit, finger * 0.7f));
                    s.paintAmount = Mathf.Max(s.paintAmount, rubber);
                    s.smoothness = Mathf.Lerp(s.smoothness, 0.18f, rubber);
                    s.metallic = Mathf.Lerp(s.metallic, 0f, rubber);
                    return;
                }

                // Tie-down slots beside the grommets, every 7 cm.
                float y = Mathf.Repeat(p.y - _board.yMin, 0.07f) - 0.035f;
                float slot = RoundedBox(new Vector2(p.x - (cx + 0.022f), y), new Vector2(0.0022f, 0.0045f), 0.0015f);
                Perforate(ref s, slot + 0.0015f, 0.0015f, aa);
            }

            static float RoundedBox(Vector2 p, Vector2 half, float radius)
            {
                var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
                return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            }

            // A 5×7 bitmap font, just the letters it needs. Each string is a row, top first.
            const string Logo = "BUILDAR";
            static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
            {
                ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
                ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
                ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
                ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
                ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
                ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
                ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            };

            /// <summary>Whether a cell of the logo is inked; columns run along the text, rows from the top.</summary>
            static bool Inked(int col, int row)
            {
                if (col < 0 || row < 0 || row > 6) return false;
                int letter = col / 6, inLetter = col % 6;
                return letter < Logo.Length && inLetter <= 4 && Glyphs[Logo[letter]][row][inLetter] == '#';
            }

            /// <summary>How much of the logo covers a point, in cells from the logo's bottom-left corner.</summary>
            static float LogoInk(float x, float y, float aa)
            {
                if (x < 0f || y < 0f) return 0f;
                int col = Mathf.FloorToInt(x), row = 6 - Mathf.FloorToInt(y);
                if (!Inked(col, row)) return 0f;
                // Soften only the edges that face bare paint, so the strokes stay solid where cells join.
                float fx = x - col, fy = y - Mathf.Floor(y), edge = 1f;
                if (!Inked(col - 1, row)) edge = Mathf.Min(edge, fx);
                if (!Inked(col + 1, row)) edge = Mathf.Min(edge, 1f - fx);
                if (!Inked(col, row + 1)) edge = Mathf.Min(edge, fy);        // the row below is lower in y
                if (!Inked(col, row - 1)) edge = Mathf.Min(edge, 1f - fy);
                return Mathf.Clamp01(0.5f + edge / Mathf.Max(0.05f, aa));
            }

            /// <summary>Smooth value noise in 0–1.</summary>
            static float Noise(Vector3 p)
            {
                int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
                float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
                float Corner(int dx, int dy, int dz) => Hash(xi + dx, yi + dy, zi + dz);
                float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
                float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
                return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
            }

            static float Hash(int x, int y, int z)
            {
                unchecked
                {
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    return (h & 0xFFFFFF) / 16777215f;
                }
            }
        }
    }
}
