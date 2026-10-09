using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using BuildAR.Assembly;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// The ports, buttons and LEDs of a PC's I/O, modelled at real size from primitives: the rear port column (used
    /// on the built-in motherboard's rear edge and as the case's rear I/O shield) and the case's top button panel.
    ///
    /// Each panel is built once into a mesh with one submesh per material, kept in IO_Panels.asset, so a panel of
    /// sixty little parts costs one renderer. LEDs that cycle colour (RgbGlow) stay separate objects. A panel is built
    /// with its face in its root's XY plane at z 0, facing +Z, width along X and height along Y, in metres.
    /// </summary>
    internal static class IoParts
    {
        const string MaterialFolder = "Assets/_Project/Art/Materials";
        const string MeshAsset = "Assets/_Project/Art/Models/Generated/IO_Panels.asset";
        const string LedRgbPath = "Assets/_Project/Art/Materials/M_LED_RGB.mat";

        /// <summary>The rear column's size: one column of ports, the width of the generated case's I/O cut-out.</summary>
        public static readonly Vector2 RearColumnSize = new Vector2(0.0224f, 0.149f);
        /// <summary>The top panel's bezel.</summary>
        public static readonly Vector2 TopPanelSize = new Vector2(0.136f, 0.024f);
        /// <summary>How far the rear column's plate sits behind its face (it rests on the surface it's put on).</summary>
        public const float RearPlateDepth = 0.0008f;

        // ------------------------------------------------------------------ materials

        static Material Shell => Mat("M_IO_Shell", 0xB8BEC8, 0.9f, 0.6f);          // steel port shells
        static Material Hole => Mat("M_IO_Hole", 0x040405, 0f, 0.1f);
        static Material Usb3 => Mat("M_IO_USB3", 0x1F5FD6, 0f, 0.5f);              // USB 5 Gb/s blue
        static Material Usb10 => Mat("M_IO_USB10G", 0xC8202A, 0f, 0.5f);           // 10 Gb/s red
        static Material Usb2 => Mat("M_IO_USB2", 0x16181C, 0f, 0.45f);
        static Material Gold => Mat("M_IO_Gold", 0xD4A63A, 1f, 0.75f);
        static Material Plate => Mat("M_IO_Plate", 0x1A1D22, 0.55f, 0.45f);        // shield / bezel gunmetal
        static Material Button => Mat("M_IO_Button", 0x2E323A, 0.9f, 0.72f);       // brushed dark aluminium
        static Material Icon => Mat("M_IO_Icon", 0xD5DCE6, 0.1f, 0.4f, glow: 0.35f);
        static Material Print => Mat("M_IO_Print", 0xC9D0DA, 0f, 0.3f);            // white labels
        static Material LedWhite => Mat("M_IO_LED_White", 0xE6F0FF, 0f, 0.8f, glow: 2.2f);
        static Material LedAmber => Mat("M_IO_LED_Amber", 0xFFA126, 0f, 0.8f, glow: 1.8f);
        static Material LedGreen => Mat("M_IO_LED_Green", 0x39E05A, 0f, 0.8f, glow: 1.8f);
        static Material Jack(string name, int hex) => Mat("M_IO_Jack_" + name, hex, 0f, 0.5f);

        static Material Mat(string name, int hex, float metallic, float smoothness, float glow = 0f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            var color = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (glow > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * glow);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LedRgb()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(LedRgbPath);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_LED_RGB" };
            m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            m.SetFloat("_Smoothness", 0.8f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white * 2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, LedRgbPath);
            return m;
        }

        // ------------------------------------------------------------------ panels

        /// <summary>
        /// The rear I/O column, top to bottom: Wi-Fi antennas, BIOS flashback button, HDMI, DisplayPort, USB-C,
        /// USB-A at 10 Gb/s (red), 5 Gb/s (blue) and USB 2.0 (black), Ethernet with its link LEDs, the audio jacks
        /// and optical out, and a vent — on a gunmetal shield plate with a raised rim. Port bodies run back 12 mm.
        /// </summary>
        public static Transform RearColumn(Transform parent, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            AddBaked(root, "Rear I/O column", BuildRearColumn);
            return root;
        }

        /// <summary>
        /// The case's top buttons and ports on a bezel: power button with an RGB ring and power symbol, reset,
        /// power and drive LEDs, headset jack, USB-C and two USB-A, and an RGB line along the bezel's front edge
        /// (the -Y side). The bezel stands on the surface at z 0.
        /// </summary>
        public static Transform TopPanel(Transform parent, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            AddBaked(root, "Top I/O panel", BuildTopPanel);

            // The colour-cycling bits: the power button's ring and the bezel's edge light. The ring also carries the
            // button itself, which switches the finished PC on in Virtual Assembly.
            float x0 = TopLayout[0];
            Rgb(root, "Power Ring", PrimitiveType.Cylinder, new Vector3(x0, 0f, 0.0017f), new Vector3(0.0172f, 0.0172f, 0.0010f), cylinder: true)
                .AddComponent<PowerButton>();
            Rgb(root, "Edge Light", PrimitiveType.Cube, new Vector3(0f, -TopPanelSize.y * 0.5f + 0.0006f, 0.0013f), new Vector3(TopPanelSize.x - 0.008f, 0.0008f, 0.0004f));
            return root;
        }

        /// <summary>X of each item on the top panel, left to right: power, reset, LEDs, audio, USB-C, USB-A, USB-A.</summary>
        static readonly float[] TopLayout = { -0.0455f, -0.0265f, -0.0175f, -0.0075f, 0.0065f, 0.0255f, 0.0445f };

        static void BuildTopPanel(Transform t)
        {
            // Bezel, and a thin darker border inset round it.
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.0006f), new Vector3(TopPanelSize.x, TopPanelSize.y, 0.0012f), Plate);
            float z = 0.0012f;
            PowerButtonCap(t, new Vector3(TopLayout[0], 0, z));
            SmallButton(t, new Vector3(TopLayout[1], 0, z), 0.0060f);
            P(t, PrimitiveType.Cylinder, new Vector3(TopLayout[2], 0.0035f, z + 0.0002f), new Vector3(0.0018f, 0.0018f, 0.0004f), LedWhite, cylinder: true);
            P(t, PrimitiveType.Cylinder, new Vector3(TopLayout[2], -0.0035f, z + 0.0002f), new Vector3(0.0018f, 0.0018f, 0.0004f), LedAmber, cylinder: true);
            AudioJack(t, new Vector3(TopLayout[3], 0, z), Jack("Black", 0x202226));
            UsbC(t, new Vector3(TopLayout[4], 0, z));
            UsbA(t, new Vector3(TopLayout[5], 0, z), Usb3);
            UsbA(t, new Vector3(TopLayout[6], 0, z), Usb3);
            // Small printed marks under the ports, like the icons on a real front panel.
            foreach (int i in new[] { 3, 4, 5, 6 })
                P(t, PrimitiveType.Cube, new Vector3(TopLayout[i], -0.0082f, z + 0.00005f), new Vector3(0.0035f, 0.0006f, 0.0001f), Print);
        }

        static void BuildRearColumn(Transform t)
        {
            var size = RearColumnSize;
            float top = size.y * 0.5f;
            // Shield plate with a raised rim.
            P(t, PrimitiveType.Cube, new Vector3(0, 0, -RearPlateDepth * 0.5f), new Vector3(size.x, size.y, RearPlateDepth), Plate);
            foreach (float s in new[] { -1f, 1f })
            {
                P(t, PrimitiveType.Cube, new Vector3(s * (size.x * 0.5f - 0.0006f), 0, 0.0002f), new Vector3(0.0012f, size.y, 0.0004f), Plate);
                P(t, PrimitiveType.Cube, new Vector3(0, s * (top - 0.0006f), 0.0002f), new Vector3(size.x, 0.0012f, 0.0004f), Plate);
            }

            float y = top;
            float Row(float height, float gap = 0.0022f) { y -= gap + height * 0.5f; float at = y; y -= height * 0.5f; return at; }

            float r = Row(0.008f, 0.0035f);                          // Wi-Fi antenna connectors
            Sma(t, new Vector3(-0.0055f, r, 0)); Sma(t, new Vector3(0.0055f, r, 0));
            r = Row(0.0045f);                                         // BIOS flashback button and its LED
            SmallButton(t, new Vector3(-0.004f, r, 0), 0.0042f);
            P(t, PrimitiveType.Cylinder, new Vector3(0.004f, r, 0.0002f), new Vector3(0.0016f, 0.0016f, 0.0004f), LedWhite, cylinder: true);
            Hdmi(t, new Vector3(0, Row(0.0062f), 0));
            DisplayPort(t, new Vector3(0, Row(0.0062f), 0));
            UsbC(t, new Vector3(0, Row(0.0034f), 0));
            UsbA(t, new Vector3(0, Row(0.0058f), 0), Usb10);
            UsbA(t, new Vector3(0, Row(0.0058f, 0.0018f), 0), Usb10);
            UsbA(t, new Vector3(0, Row(0.0058f), 0), Usb3);
            UsbA(t, new Vector3(0, Row(0.0058f, 0.0018f), 0), Usb3);
            Rj45(t, new Vector3(0, Row(0.0135f, 0.0028f), 0));
            UsbA(t, new Vector3(0, Row(0.0058f, 0.0028f), 0), Usb2);
            UsbA(t, new Vector3(0, Row(0.0058f, 0.0018f), 0), Usb2);
            r = Row(0.0064f, 0.003f);                                 // audio: line out, mic
            AudioJack(t, new Vector3(-0.0045f, r, 0), Jack("Green", 0x7CC243)); AudioJack(t, new Vector3(0.0045f, r, 0), Jack("Pink", 0xE0457B));
            r = Row(0.0064f, 0.0016f);                                // line in, centre/sub
            AudioJack(t, new Vector3(-0.0045f, r, 0), Jack("Blue", 0x4FB3E8)); AudioJack(t, new Vector3(0.0045f, r, 0), Jack("Orange", 0xF0932B));
            r = Row(0.0064f, 0.0016f);                                // rear surround, optical out
            AudioJack(t, new Vector3(-0.0045f, r, 0), Jack("Black", 0x202226)); Spdif(t, new Vector3(0.0045f, r, 0));
            for (int i = 0; i < 3; i++)                               // vent slots in what's left
                P(t, PrimitiveType.Cube, new Vector3(0, Row(0.0014f, 0.0022f), 0.00005f), new Vector3(size.x - 0.007f, 0.0014f, 0.0002f), Hole);
        }

        // ------------------------------------------------------------------ ports (face at z, facing +Z)

        /// <summary>USB-A: steel shell, black opening, the coloured tongue in its upper half with four gold contacts.</summary>
        static void UsbA(Transform t, Vector3 at, Material tongue)
        {
            Body(t, at, new Vector2(0.0132f, 0.0058f), Shell, 0.012f);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0, 0.0004f), new Vector3(0.0122f, 0.0048f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0.0011f, 0.0006f), new Vector3(0.0112f, 0.0018f, 0.0002f), tongue);
            for (int i = 0; i < 4; i++)
                P(t, PrimitiveType.Cube, at + new Vector3(-0.0036f + i * 0.0024f, 0.0001f, 0.0006f), new Vector3(0.0011f, 0.0004f, 0.0002f), Gold);
        }

        /// <summary>USB-C: a pill-shaped shell and opening with the flat tongue across it.</summary>
        static void UsbC(Transform t, Vector3 at)
        {
            Pill(t, at + new Vector3(0, 0, -0.004f), 0.0090f, 0.0034f, 0.0086f, Shell);
            Pill(t, at + new Vector3(0, 0, 0.0004f), 0.0082f, 0.0026f, 0.0002f, Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0, 0.0006f), new Vector3(0.0064f, 0.0007f, 0.0002f), Usb2);
        }

        /// <summary>HDMI: the stepped opening that narrows at the bottom, and its tongue.</summary>
        static void Hdmi(Transform t, Vector3 at)
        {
            Body(t, at, new Vector2(0.0152f, 0.0062f), Shell, 0.011f);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0.0006f, 0.0004f), new Vector3(0.0140f, 0.0032f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, -0.0016f, 0.0004f), new Vector3(0.0106f, 0.0014f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0.0003f, 0.0006f), new Vector3(0.0112f, 0.0011f, 0.0002f), Usb2);
        }

        /// <summary>DisplayPort: like HDMI but with only one corner cut, so it's easy to tell apart.</summary>
        static void DisplayPort(Transform t, Vector3 at)
        {
            Body(t, at, new Vector2(0.0162f, 0.0062f), Shell, 0.011f);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0.0006f, 0.0004f), new Vector3(0.0150f, 0.0032f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(-0.0017f, -0.0016f, 0.0004f), new Vector3(0.0116f, 0.0014f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(-0.0005f, 0.0003f, 0.0006f), new Vector3(0.0120f, 0.0011f, 0.0002f), Usb2);
        }

        /// <summary>Ethernet: a deep jack with the latch notch, eight gold pins and the green and amber link LEDs.</summary>
        static void Rj45(Transform t, Vector3 at)
        {
            Body(t, at, new Vector2(0.0160f, 0.0135f), Shell, 0.02f);
            P(t, PrimitiveType.Cube, at + new Vector3(0, -0.0006f, 0.0004f), new Vector3(0.0118f, 0.0082f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, -0.0054f, 0.0004f), new Vector3(0.0060f, 0.0024f, 0.0002f), Hole);
            for (int i = 0; i < 8; i++)
                P(t, PrimitiveType.Cube, at + new Vector3(-0.0042f + i * 0.0012f, 0.0024f, 0.0006f), new Vector3(0.0005f, 0.0014f, 0.0002f), Gold);
            P(t, PrimitiveType.Cube, at + new Vector3(-0.0056f, 0.0054f, 0.0005f), new Vector3(0.0026f, 0.0016f, 0.0002f), LedGreen);
            P(t, PrimitiveType.Cube, at + new Vector3(0.0056f, 0.0054f, 0.0005f), new Vector3(0.0026f, 0.0016f, 0.0002f), LedAmber);
        }

        /// <summary>3.5 mm jack: a black body with a thin coloured collar at the face round the socket.</summary>
        static void AudioJack(Transform t, Vector3 at, Material collar)
        {
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, -0.0039f), new Vector3(0.0060f, 0.0060f, 0.0078f), Usb2, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0003f), new Vector3(0.0064f, 0.0064f, 0.0006f), collar, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0007f), new Vector3(0.0036f, 0.0036f, 0.0002f), Hole, cylinder: true);
        }

        /// <summary>Optical S/PDIF out: a square socket with its little shutter.</summary>
        static void Spdif(Transform t, Vector3 at)
        {
            Body(t, at, new Vector2(0.0064f, 0.0064f), Usb2, 0.009f);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0, 0.0004f), new Vector3(0.0044f, 0.0040f, 0.0002f), Hole);
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0, 0.0006f), new Vector3(0.0030f, 0.0026f, 0.0002f), Jack("Grey", 0x8C939C));
        }

        /// <summary>Antenna connector: gold nut and threaded barrel standing out of the plate, with its centre pin.</summary>
        static void Sma(Transform t, Vector3 at)
        {
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0012f), new Vector3(0.0078f, 0.0078f, 0.0024f), Gold, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0055f), new Vector3(0.0062f, 0.0062f, 0.0064f), Gold, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0088f), new Vector3(0.0042f, 0.0042f, 0.0002f), Hole, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0089f), new Vector3(0.0012f, 0.0012f, 0.0003f), Gold, cylinder: true);
        }

        /// <summary>Power button: a brushed cap standing proud, with the power symbol on it. Its RGB ring is added separately.</summary>
        static void PowerButtonCap(Transform t, Vector3 at)
        {
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, -0.0002f), new Vector3(0.0186f, 0.0186f, 0.0004f), Hole, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0014f), new Vector3(0.0142f, 0.0142f, 0.0028f), Button, cylinder: true);
            // The symbol: a broken circle with a bar through the gap, facing "up" the panel (+Y).
            float face = at.z + 0.0028f + 0.00005f;
            const float radius = 0.0033f;
            for (int i = 0; i <= 16; i++)   // overlapping segments, so the circle reads as one stroke
            {
                float angle = Mathf.Lerp(40f, 320f, i / 16f);
                var p = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * radius, Mathf.Cos(angle * Mathf.Deg2Rad) * radius, 0f);
                P(t, PrimitiveType.Cube, new Vector3(at.x + p.x, at.y + p.y, face), new Vector3(0.0013f, 0.0007f, 0.0001f), Icon, zRotation: -angle);
            }
            P(t, PrimitiveType.Cube, new Vector3(at.x, at.y + 0.0021f, face), new Vector3(0.0007f, 0.0036f, 0.0001f), Icon);
        }

        static void SmallButton(Transform t, Vector3 at, float diameter)
        {
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, -0.0001f), new Vector3(diameter + 0.0016f, diameter + 0.0016f, 0.0004f), Hole, cylinder: true);
            P(t, PrimitiveType.Cylinder, at + new Vector3(0, 0, 0.0006f), new Vector3(diameter, diameter, 0.0012f), Button, cylinder: true);
        }

        /// <summary>A port's shell: its outline 0.3 mm proud of the face, its body running <paramref name="depth"/> back.</summary>
        static void Body(Transform t, Vector3 at, Vector2 outline, Material m, float depth) =>
            P(t, PrimitiveType.Cube, at + new Vector3(0, 0, 0.0003f - depth * 0.5f), new Vector3(outline.x, outline.y, depth), m);

        /// <summary>A pill (stadium) shape across X: a box between two half-round ends.</summary>
        static void Pill(Transform t, Vector3 centre, float width, float height, float depth, Material m)
        {
            P(t, PrimitiveType.Cube, centre, new Vector3(width - height, height, depth), m);
            foreach (float s in new[] { -1f, 1f })
                P(t, PrimitiveType.Cylinder, centre + new Vector3(s * (width - height) * 0.5f, 0, 0), new Vector3(height, height, depth), m, cylinder: true);
        }

        /// <summary>
        /// A primitive at <paramref name="pos"/> sized <paramref name="size"/> (a cylinder: diameter, diameter, length
        /// along Z), turned <paramref name="zRotation"/> degrees about Z.
        /// </summary>
        static void P(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material m, bool cylinder = false, float zRotation = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (cylinder)
            {
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(size.x, size.z * 0.5f, size.y);
            }
            else
            {
                go.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
                go.transform.localScale = size;
            }
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>An RGB LED piece: its own renderer so RgbGlow can colour it.</summary>
        static GameObject Rgb(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 size, bool cylinder = false)
        {
            P(parent, type, pos, size, LedRgb(), cylinder);
            var go = parent.GetChild(parent.childCount - 1).gameObject;
            go.name = name;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<RgbGlow>();
            return go;
        }

        // ------------------------------------------------------------------ baking

        /// <summary>
        /// Builds a panel's parts, merges them into one mesh with a submesh per material (kept in IO_Panels.asset
        /// under <paramref name="meshName"/>, updated in place so everything using it stays linked), and puts it
        /// on a "Body" child of <paramref name="root"/>.
        /// </summary>
        static void AddBaked(Transform root, string meshName, System.Action<Transform> build)
        {
            var temp = new GameObject("IO build");
            Mesh mesh;
            Material[] materials;
            try
            {
                build(temp.transform);
                var groups = new List<(Material material, List<CombineInstance> parts)>();
                foreach (var filter in temp.GetComponentsInChildren<MeshFilter>())
                {
                    var material = filter.GetComponent<MeshRenderer>().sharedMaterial;
                    int g = groups.FindIndex(x => x.material == material);
                    if (g < 0) { groups.Add((material, new List<CombineInstance>())); g = groups.Count - 1; }
                    groups[g].parts.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        transform = temp.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                    });
                }

                var perMaterial = groups.Select(g =>
                {
                    var m = new Mesh();
                    m.CombineMeshes(g.parts.ToArray(), true, true);
                    return m;
                }).ToList();
                var combined = new Mesh();
                combined.CombineMeshes(perMaterial.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
                foreach (var m in perMaterial) Object.DestroyImmediate(m);

                mesh = Store(meshName, combined);
                Object.DestroyImmediate(combined);
                materials = groups.Select(g => g.material).ToArray();
            }
            finally { Object.DestroyImmediate(temp); }

            var body = new GameObject("Body");
            body.transform.SetParent(root, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        /// <summary>Copies <paramref name="source"/> into the mesh called <paramref name="name"/> in IO_Panels.asset.</summary>
        static Mesh Store(string name, Mesh source)
        {
            string folder = Path.GetDirectoryName(MeshAsset)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder)?.Replace('\\', '/'), Path.GetFileName(folder));

            var mesh = AssetDatabase.LoadAllAssetsAtPath(MeshAsset).OfType<Mesh>().FirstOrDefault(m => m.name == name);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = source.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(source.vertices);
            mesh.SetNormals(source.normals);
            mesh.SetUVs(0, source.uv);
            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(source.GetTriangles(s), s);
            mesh.RecalculateBounds();

            if (isNew)
            {
                if (!File.Exists(MeshAsset)) AssetDatabase.CreateAsset(mesh, MeshAsset);
                else AssetDatabase.AddObjectToAsset(mesh, MeshAsset);
            }
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
