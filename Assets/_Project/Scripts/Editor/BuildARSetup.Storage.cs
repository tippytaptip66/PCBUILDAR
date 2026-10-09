using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BuildAR.Assembly;
using BuildAR.Data;
using BuildAR.Viewer;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// The 2.5-inch SATA SSD: its stand-in model, its mount and sockets in the assembly case, its steps in the build
    /// guide, and the upgrade that adds all of it to a project set up before it existed.
    /// </summary>
    public static partial class BuildARSetup
    {
        /// <summary>
        /// Where the SSD stands in the slot layout: on its long edge in a bracket on the floor of the PSU basement
        /// (floor surface 4 mm, bracket foot 1.5 mm, half the drive 35 mm), just past the PSU's cable panel, label
        /// facing the open side (100 × 70 × 7 mm). Moves with the case like the PSU bay.
        /// </summary>
        static readonly Vector3 SataSsdAt = new Vector3(0.062f, 0.0405f, -0.065f);
        /// <summary>The sockets on the SSD's end facing the PSU, relative to its centre: wide power, narrow data.</summary>
        static readonly Vector3 SataPowerSocket = new Vector3(-0.046f, -0.005f, 0.0005f);
        static readonly Vector3 SataDataSocket = new Vector3(-0.046f, -0.024f, 0.0005f);
        /// <summary>The loose end of the SATA data cable, hanging just above the SSD until it's plugged in.</summary>
        static readonly Vector3 SataDataLooseEnd = new Vector3(0.02f, 0.085f, -0.035f);
        /// <summary>Where the data cable's other end is: the board's SATA ports, bottom-right, behind the tray.</summary>
        static readonly Vector3 SataDataOrigin = new Vector3(0.072f, 0.1475f, 0.123f);

        static AssemblyStep SataSsdStep() => new AssemblyStep
        {
            stepId = "s06b_sata", title = "Install the SATA SSD", kind = AssemblyStep.StepKind.PlaceComponent, componentId = "sata_ssd",
            instruction = "Drag the 2.5-inch SATA SSD onto its mount in the basement, next to the PSU, label facing you.",
            safetyTip = "Hold drives by their edges and don't drop them.",
            validSlotIds = new List<string> { "sata_1" },
        };

        static AssemblyStep SataPowerStep() => new AssemblyStep
        {
            stepId = "s09b_sata_power", title = "Connect SATA power", kind = AssemblyStep.StepKind.ConnectCable, connectorType = "SATA",
            instruction = "Tap the PSU's SATA power cable, then the wide power socket on the SSD.",
            safetyTip = "The SATA power plug is L-shaped, so it only fits one way round.",
        };

        static AssemblyStep SataDataStep() => new AssemblyStep
        {
            stepId = "s09c_sata_data", title = "Connect SATA data", kind = AssemblyStep.StepKind.ConnectCable, connectorType = "SATADATA",
            instruction = "Tap the red SATA data cable, then the narrow data socket on the SSD. Its other end is already on the motherboard.",
            safetyTip = "Push the data plug straight in until it clicks; don't lever it sideways.",
        };

        // ------------------------------------------------------------------ the case

        /// <summary>
        /// The SSD's slot and glow box, its two sockets, and the SATA data cable (whose other end, the origin, is on
        /// the board behind the tray). Everything but the origin belongs to the case, so it's placed at
        /// <paramref name="caseOffset"/> from the layout, where the case's PSU bay has been moved to.
        /// </summary>
        static void AddSataSsdAnchors(Transform slots, Transform ports, Vector3 caseOffset)
        {
            var flat = Quaternion.LookRotation(Vector3.up, Vector3.back);   // like a board part: label to the open side
            var at = SataSsdAt + caseOffset;
            var slot = Slot(slots, "sata_1", ComponentCategory.Storage, at, flat, at, new Vector3(0.1f, 0.07f, 0.009f));
            slot.transform.localPosition = at;   // Slot() places in world space; here the groups may already be moved
            slot.transform.localRotation = flat;
            slot.mountedOnCase = true;
            BuildSsdBracket(slot.transform);

            // The sockets face the PSU (-X): a port's forward points into its socket, its width runs up the drive.
            var socket = Quaternion.LookRotation(Vector3.right, Vector3.forward);
            Port(ports, "ssd_sata_power", "SATA power socket", "SATA", false, "sata_1", at + SataPowerSocket,
                 new Vector3(0.021f, 0.0042f, 0.006f), Hex("1C1C1C"));
            Port(ports, "ssd_sata_data", "SATA data socket", "SATADATA", false, "sata_1", at + SataDataSocket,
                 new Vector3(0.011f, 0.0042f, 0.006f), Hex("C23B3B"));
            Port(ports, "sata_data", "SATA data cable", "SATADATA", true, "sata_1", SataDataLooseEnd + caseOffset,
                 new Vector3(0.011f, 0.004f, 0.012f), Hex("C23B3B"));

            var origin = new GameObject("Origin_sata_data").transform;
            origin.SetParent(ports, false);
            origin.localPosition = SataDataOrigin;

            foreach (var port in ports.GetComponentsInChildren<CablePort>(true))
            {
                if (port.portId == "ssd_sata_power" || port.portId == "ssd_sata_data")
                {
                    port.transform.localRotation = socket;
                    port.direct = true;
                }
                if (port.portId == "sata_data") port.origin = origin;
            }
        }

        // ------------------------------------------------------------------ upgrading an existing project

        /// <summary>
        /// Adds what newer versions of the practice build need to a project set up before them, leaving everything
        /// already there alone: the SATA SSD's quiz cards, stand-in model and component, its mount, sockets and data
        /// cable in the assembly case, and its three steps in the build guide. Safe to run any number of times;
        /// Import All Models and Import Assembly Case Model run it first.
        /// </summary>
        internal static void UpgradeProject()
        {
            CreateFolders();
            var quiz = CreateQuizQuestions(overwrite: false);
            BuildPlaceholders();
            CreateComponents(quiz);
            UpgradeBoardInterfaces();
            UpgradeAssemblyCase();
            UpgradeAssemblyGuide();
            AssetDatabase.SaveAssets();
        }

        /// <summary>The sample motherboard has SATA ports as well as M.2, so a SATA drive checks out against it.</summary>
        static void UpgradeBoardInterfaces()
        {
            var board = AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>($"{ComponentsRes}/motherboard.asset");
            if (board == null || board.compatibilityRules == null) return;
            bool offersSata = board.compatibilityRules.Any(r => r.ruleType == CompatibilityRule.RuleType.InterfaceType &&
                                                                 string.Equals(r.stringValue, "SATA", System.StringComparison.OrdinalIgnoreCase));
            if (offersSata) return;
            board.compatibilityRules.Add(Rule(CompatibilityRule.RuleType.InterfaceType, "SATA"));
            EditorUtility.SetDirty(board);
        }

        static void UpgradeAssemblyCase()
        {
            string path = $"{AssemblyPrefabs}/PH_AssemblyCase.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var slots = contents.transform.Find("Slots");
                var ports = contents.transform.Find("CablePorts");
                if (slots == null || ports == null) return;

                // An imported case has already moved its case anchors; these go where the PSU bay went.
                var fit = contents.GetComponent<AssemblyCaseFit>();
                var offset = fit != null ? fit.psuOffset : Vector3.zero;
                var slot = slots.GetComponentsInChildren<SnapSlot>(true).FirstOrDefault(s => s.slotId == "sata_1");
                if (slot == null)
                {
                    AddSataSsdAnchors(slots, ports, offset);
                    Debug.Log("BuildAR: added the SATA SSD's mount, sockets and data cable to the assembly case.");
                }
                else if (!SyncSataSsdAnchors(slots, ports, slot, offset)) return;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        /// <summary>
        /// Brings a SATA SSD mount added by an earlier version up to date: its slot, glow box, sockets and cable end
        /// where the layout now puts them, and its bracket. True if anything changed.
        /// </summary>
        static bool SyncSataSsdAnchors(Transform slots, Transform ports, SnapSlot slot, Vector3 offset)
        {
            bool changed = false;
            void Place(Transform t, Vector3 at)
            {
                if (t == null || (t.localPosition - at).sqrMagnitude < 1e-10f) return;
                t.localPosition = at;
                changed = true;
            }
            var at = SataSsdAt + offset;
            Place(slot.transform, at);
            Place(slots.Find("Highlight_sata_1"), at);
            foreach (var port in ports.GetComponentsInChildren<CablePort>(true))
            {
                if (port.portId == "ssd_sata_power") Place(port.transform, at + SataPowerSocket);
                else if (port.portId == "ssd_sata_data") Place(port.transform, at + SataDataSocket);
                else if (port.portId == "sata_data") Place(port.transform, SataDataLooseEnd + offset);
            }
            if (slot.transform.Find("SSD Bracket") == null)
            {
                BuildSsdBracket(slot.transform);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// The bracket the SSD stands in, in the slot's (the drive's) own axes — X along it, Y out of the label, Z up
        /// it: a foot on the floor, a back plate, a lip in front of the bottom edge and a clip round the far end,
        /// leaving the socket end open. Part of the case, so it shows where the drive goes before it's installed.
        /// </summary>
        static void BuildSsdBracket(Transform slot)
        {
            var bracket = new GameObject("SSD Bracket").transform;
            bracket.SetParent(slot, false);
            var steel = Lit("M_SSD_Bracket", Hex("2A2D33"), 0.7f, 0.5f);
            Model.Mesh(bracket, PrimitiveType.Cube, new Vector3(0f, -0.004f, -0.03575f), new Vector3(0.092f, 0.026f, 0.0015f), steel);  // foot
            Model.Mesh(bracket, PrimitiveType.Cube, new Vector3(0f, -0.00525f, -0.012f), new Vector3(0.092f, 0.0015f, 0.047f), steel);  // back
            Model.Mesh(bracket, PrimitiveType.Cube, new Vector3(0f, 0.00525f, -0.033f), new Vector3(0.092f, 0.0015f, 0.004f), steel);   // front lip
            Model.Mesh(bracket, PrimitiveType.Cube, new Vector3(0.05075f, 0f, -0.02f), new Vector3(0.0015f, 0.012f, 0.03f), steel);     // end clip
        }

        static void UpgradeAssemblyGuide()
        {
            var guide = AssetDatabase.LoadAssetAtPath<AssemblyGuideSO>($"{GuidesRes}/FirstBuild.asset");
            if (guide == null || guide.steps == null) return;
            bool changed = false;

            // Two storage slots now, so each drive's step names its own.
            var m2 = guide.steps.FirstOrDefault(s => s.stepId == "s06_ssd");
            if (m2 != null && (m2.validSlotIds == null || m2.validSlotIds.Count == 0))
            {
                m2.validSlotIds = new List<string> { "m2_1" };
                changed = true;
            }

            void InsertAfter(string afterId, AssemblyStep step)
            {
                if (guide.steps.Any(s => s.stepId == step.stepId)) return;
                int i = guide.steps.FindIndex(s => s.stepId == afterId);
                guide.steps.Insert(i < 0 ? guide.steps.Count : i + 1, step);
                changed = true;
            }
            InsertAfter("s06_ssd", SataSsdStep());
            InsertAfter("s09_eps", SataPowerStep());
            InsertAfter("s09b_sata_power", SataDataStep());

            if (!changed) return;
            EditorUtility.SetDirty(guide);
            Debug.Log($"BuildAR: the practice build now has {guide.steps.Count} steps, with the SATA SSD and its cables.");
        }

        // ------------------------------------------------------------------ the stand-in model

        /// <summary>
        /// A 2.5-inch SATA SSD at real size (100 × 69.85 × 7 mm): a dark aluminium shell with a printed BuildAR label
        /// on top, the L-shaped 15-pin power and 7-pin data sockets with their gold contacts on one end (-X), and the
        /// side mounting holes. Lies like a board part: label up (+Y), length along X.
        /// </summary>
        static GameObject BuildSataSsd(string path)
        {
            var shell = Lit("M_SSD_Shell", Hex("2A2E36"), 0.8f, 0.55f);
            var lid = Lit("M_SSD_Lid", Hex("1C1F25"), 0.7f, 0.62f);
            var m = new Model("PH_SSD_SATA");

            var body = m.Part("Body", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(body, PrimitiveType.Cube, new Vector3(0, -0.00125f, 0), new Vector3(0.1f, 0.0045f, 0.0698f), shell);
            Model.Mesh(body, PrimitiveType.Cube, new Vector3(0, 0.0023f, 0), new Vector3(0.0994f, 0.0024f, 0.0692f), lid);
            foreach (float x in new[] { -0.036f, 0.0406f })          // M3 side holes, 14 and 90.6 mm from the socket end
                foreach (float z in new[] { -0.0349f, 0.0349f })
                    Model.Mesh(body, PrimitiveType.Cylinder, new Vector3(x, -0.0005f, z), new Vector3(0.003f, 0.0003f, 0.003f), Chip, new Vector3(90, 0, 0));
            Model.Hotspot(body, "Mounting holes", "Screw holes on the sides and bottom fit drive bays and brackets.", ModelHotspot.Kind.Feature,
                          new Vector3(0.0406f, -0.0005f, -0.035f), Vector3.back);

            var label = m.Part("Label", new Vector3(0.008f, 0.00355f, 0f), Vector3.up, 1.2f);
            Model.Mesh(label, PrimitiveType.Quad, Vector3.zero, new Vector3(0.074f, 0.05f, 1f), LabelMaterial(), new Vector3(90, 0, 0));
            Model.Hotspot(label, "Label", "Model, capacity and interface: 1 TB, SATA III (6 Gb/s).", ModelHotspot.Kind.Feature, new Vector3(0, 0.0003f, 0), Vector3.up);

            // The sockets, set into the end: data (7-pin) near one edge, power (15-pin) beside it; each is L-keyed.
            // Across the drive is the model's Z, which is up when it stands in its mount (the offsets' y).
            var sockets = m.Part("Sockets", new Vector3(-0.0485f, -0.0005f, 0f), Vector3.left, 1.3f);
            Socket(sockets, SataDataSocket.y, 0.011f);
            Socket(sockets, SataPowerSocket.y, 0.021f);
            Model.Hotspot(sockets, "SATA power (15-pin)", "The wide socket: power from the PSU's SATA cable.", ModelHotspot.Kind.Connector,
                          new Vector3(-0.0018f, 0, SataPowerSocket.y), Vector3.left);
            Model.Hotspot(sockets, "SATA data (7-pin)", "The narrow socket: a SATA data cable to the motherboard.", ModelHotspot.Kind.Connector,
                          new Vector3(-0.0018f, 0, SataDataSocket.y), Vector3.left);
            return m.Save(path, 0.012f);
        }

        /// <summary>One socket at <paramref name="z"/> across the end, <paramref name="width"/> wide: housing, gold contacts, L key.</summary>
        static void Socket(Transform parent, float z, float width)
        {
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(0, 0, z), new Vector3(0.003f, 0.0042f, width), Chip);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(-0.0016f, -0.0007f, z), new Vector3(0.0002f, 0.0011f, width - 0.003f), Gold);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(-0.0016f, 0.001f, z + width * 0.5f - 0.001f), new Vector3(0.0003f, 0.0016f, 0.002f), Chip);
        }

        const string LabelTexturePath = Root + "/Art/Textures/T_SSD_Label.png";

        /// <summary>The SSD's printed label: a dark card with a cyan band, BUILDAR, SSD and the capacity and interface.</summary>
        static Material LabelMaterial()
        {
            var m = Lit("M_SSD_Label", Color.white, 0f, 0.35f);
            if (!File.Exists(LabelTexturePath))
            {
                File.WriteAllBytes(LabelTexturePath, DrawLabel());
                AssetDatabase.ImportAsset(LabelTexturePath);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LabelTexturePath));
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
        }

        static byte[] DrawLabel()
        {
            const int w = 512, h = 352;
            var px = new Color32[w * h];
            var card = new Color32(18, 20, 24, 255);
            var cyan = new Color32(34, 211, 238, 255);
            var white = new Color32(236, 240, 245, 255);
            var grey = new Color32(140, 148, 160, 255);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // A slanted cyan band across the lower left, and a thin frame.
                    float band = (x - (h - 1 - y) * 0.45f);
                    bool inBand = band > 10 && band < 50;
                    bool frame = x < 6 || x >= w - 6 || y < 6 || y >= h - 6;
                    px[y * w + x] = frame ? grey : inBand ? cyan : card;
                }

            void Text(string s, int left, int top, int scale, Color32 c)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    if (!LabelGlyphs.TryGetValue(s[i], out var rows)) continue;
                    for (int r = 0; r < 7; r++)
                        for (int col = 0; col < 5; col++)
                            if (rows[r][col] == '#')
                                for (int dy = 0; dy < scale; dy++)
                                    for (int dx = 0; dx < scale; dx++)
                                    {
                                        int x = left + i * 6 * scale + col * scale + dx;
                                        int y = h - 1 - (top + r * scale + dy);   // rows go down the image
                                        if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = c;
                                    }
                }
            }
            Text("BUILDAR", 150, 50, 8, white);   // all clear of the band on the left
            Text("SSD", 150, 140, 9, cyan);
            Text("1TB", 330, 152, 6, white);
            Text("SATA III  6GB/S", 190, 258, 3, grey);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                return tex.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(tex); }
        }

        // 5×7 letters for the label, top row first.
        static readonly Dictionary<char, string[]> LabelGlyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
            ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
            ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
        };
    }
}
