using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using BuildAR.Assembly;
using BuildAR.Data;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// BuildAR ▸ Setup ▸ Import Component Models.
    ///
    /// Turns the .fbx files you drop into Art/Models/Components/&lt;Category&gt;/ into usable component prefabs:
    /// sane import settings, the model's own textures on a URP material, turned to fit its slot, scaled to the
    /// part's real size (1 unit = 1 m), saved to Prefabs/Components/ and assigned to the matching component asset.
    ///
    /// Matching is by file name first (gpu.fbx, rtx4070.fbx → the component whose id, ML label, alias or name it
    /// looks like), then by the folder it sits in. Anything it can't place is listed at the end, and you can always
    /// assign a prefab by hand in the component's **Model 3D Prefab** field.
    ///
    /// Re-running is safe: existing prefabs are replaced, and nothing else about the component is touched.
    /// </summary>
    public static partial class ComponentModelImporter
    {
        const string ModelsRoot = "Assets/_Project/Art/Models/Components";
        const string PrefabFolder = "Assets/_Project/Prefabs/Components";
        /// <summary>The house case material (matte black), for case models that come without textures.</summary>
        const string CaseMaterial = "Assets/_Project/Art/Materials/M_Case.mat";

        /// <summary>Replaces a model's own materials with the project's case material.</summary>
        static bool PaintWithCaseMaterial(GameObject root)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CaseMaterial);
            if (material == null || root == null) return false;

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
            return true;
        }

        /// <summary>.blend only imports on a machine with Blender installed — Unity shells out to it to convert.</summary>
        static readonly string[] ModelExtensions = { ".fbx", ".obj", ".blend", ".dae", ".glb", ".gltf" };

        static IEnumerable<string> ModelFiles(string folder) =>
            !Directory.Exists(folder)
                ? Enumerable.Empty<string>()
                : Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => ModelExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => f.Replace('\\', '/'))
                    .OrderBy(f => f);

        /// <summary>Explains an import that produced nothing, which for .blend usually means Blender is missing.</summary>
        static string CantReadMessage(string path) =>
            Path.GetExtension(path).ToLowerInvariant() == ".blend"
                ? "Unity couldn't read this .blend — Blender has to be installed for that. Export it as FBX instead."
                : "no meshes in the file";

        /// <summary>Largest real-world dimension of each kind of part, in metres. Used to scale imported meshes.</summary>
        static readonly Dictionary<ComponentCategory, float> RealSize = new Dictionary<ComponentCategory, float>
        {
            { ComponentCategory.CPU, 0.04f },
            { ComponentCategory.Motherboard, 0.305f },
            { ComponentCategory.RAM, 0.133f },
            { ComponentCategory.Storage, 0.08f },
            { ComponentCategory.GraphicsCard, 0.28f },
            { ComponentCategory.PowerSupply, 0.15f },
            { ComponentCategory.Cooling, 0.15f },
            { ComponentCategory.Case, 0.45f },
            { ComponentCategory.CableConnector, 0.30f },
            { ComponentCategory.InputOutput, 0.16f },
        };

        /// <summary>
        /// Most a part may stick out of the board, in metres. Generated models don't always keep real proportions
        /// (the Hyper3D graphics card came out 20 cm tall), and one that reaches past the open side of the case looks
        /// wrong, so it is scaled down to fit.
        /// </summary>
        static readonly Dictionary<ComponentCategory, float> MaxHeight = new Dictionary<ComponentCategory, float>
        {
            { ComponentCategory.GraphicsCard, 0.13f },
            { ComponentCategory.Cooling, 0.155f },
        };

        /// <summary>
        /// How far below its pivot each placeholder's board-facing side sits: CPU pads, RAM and GPU edge connectors,
        /// the SSD's underside, the cooler's base. The slots were laid out for the placeholders, so an imported part
        /// is put the same distance down and lands on the board whatever its proportions.
        /// </summary>
        static readonly Dictionary<ComponentCategory, float> ContactDepth = new Dictionary<ComponentCategory, float>
        {
            { ComponentCategory.CPU, 0.0022f },
            { ComponentCategory.RAM, 0.0155f },
            { ComponentCategory.Storage, 0.00045f },
            { ComponentCategory.GraphicsCard, 0.062f },
            { ComponentCategory.Cooling, 0.08f },
        };

        /// <summary>Big parts are seen up close in the builder; small ones never fill enough of the screen for 2k.</summary>
        static int TextureSizeFor(ComponentCategory category) =>
            RealSize.TryGetValue(category, out float size) && size >= 0.25f ? 2048 : 1024;

        /// <summary>Folder name → category, for files that don't say what they are.</summary>
        static readonly Dictionary<string, ComponentCategory> FolderCategory = new Dictionary<string, ComponentCategory>
        {
            { "cpu", ComponentCategory.CPU },
            { "motherboard", ComponentCategory.Motherboard },
            { "ram", ComponentCategory.RAM },
            { "storage", ComponentCategory.Storage },
            { "graphicscard", ComponentCategory.GraphicsCard },
            { "powersupply", ComponentCategory.PowerSupply },
            { "cooling", ComponentCategory.Cooling },
            { "case", ComponentCategory.Case },
            { "cables", ComponentCategory.CableConnector },
            { "inputoutput", ComponentCategory.InputOutput },
        };

        /// <summary>Which generated stand-in each kind of part goes back to.</summary>
        static readonly Dictionary<ComponentCategory, string> Placeholders = new Dictionary<ComponentCategory, string>
        {
            { ComponentCategory.CPU, "PH_CPU" },
            { ComponentCategory.RAM, "PH_RAM" },
            { ComponentCategory.Motherboard, "PH_Motherboard" },
            { ComponentCategory.Storage, "PH_SSD_M2" },
            { ComponentCategory.GraphicsCard, "PH_GPU" },
            { ComponentCategory.PowerSupply, "PH_PSU" },
            { ComponentCategory.Cooling, "PH_Cooler" },
            { ComponentCategory.Case, "PH_Case" },
            { ComponentCategory.CableConnector, "PH_Cable24" },
            { ComponentCategory.InputOutput, "PH_IOPanel" },
        };

        /// <summary>
        /// Points every component back at its generated stand-in. The imported prefabs and the .fbx files are left
        /// on disk, so running Import Component Models again puts them back.
        /// </summary>
        [MenuItem("BuildAR/Setup/Restore Placeholder Component Models", priority = 30)]
        public static void RestorePlaceholderModels()
        {
            int changed = 0;
            var report = new StringBuilder("BuildAR: components back on their generated models\n");

            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinitionSO"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || !Placeholders.TryGetValue(def.category, out string name)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Placeholders/{name}.prefab");
                if (prefab == null) { report.AppendLine($"  ? {def.displayName,-20} {name}.prefab is missing"); continue; }
                if (def.model3DPrefab == prefab) continue;

                def.model3DPrefab = prefab;
                EditorUtility.SetDirty(def);
                report.AppendLine($"  ✓ {def.displayName,-20} → {name}");
                changed++;
            }

            AssetDatabase.SaveAssets();
            report.AppendLine($"\n{changed} component(s) changed. The .fbx files and their prefabs are untouched — " +
                              "Import Component Models puts them back whenever you want.");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Both importers in one go — the parts, then the case they're built into — so a new set of models never
        /// ends up half imported.
        /// </summary>
        [MenuItem("BuildAR/Setup/Import All Models", priority = 26)]
        public static void ImportEverything()
        {
            ImportAll();
            ImportAssemblyCase();
        }

        [MenuItem("BuildAR/Setup/Import Component Models", priority = 27)]
        public static void ImportAll()
        {
            if (!Directory.Exists(ModelsRoot))
            {
                EditorUtility.DisplayDialog("BuildAR", $"{ModelsRoot} doesn't exist yet.\n\nRun BuildAR ▸ Setup ▸ 1. Create Folders first.", "OK");
                return;
            }

            var files = ModelFiles(ModelsRoot).ToList();

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog("BuildAR",
                    $"No model files found under {ModelsRoot}.\n\n" +
                    "Drop each model into its category folder (GraphicsCard, CPU, RAM, …). Naming the file after " +
                    "the part (gpu.fbx, motherboard.fbx) helps it find the right component.", "OK");
                return;
            }

            var components = AssetDatabase.FindAssets("t:ComponentDefinitionSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null)
                .ToList();

            EnsureFolder(PrefabFolder);
            var report = new StringBuilder("BuildAR: imported component models\n");
            int done = 0;
            var matches = files.ToDictionary(p => p, p => Match(p, components));

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var path in files)
                {
                    EditorUtility.DisplayProgressBar("BuildAR", $"Importing {Path.GetFileName(path)}…", (float)done / files.Count);
                    // Cutting fans out needs the mesh data on the CPU; everything else stays GPU-only.
                    var def = matches[path];
                    ApplyImportSettings(path, readable: def != null && (def.modelFans?.Count > 0 || def.modelPanels?.Count > 0));
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            try
            {
                done = 0;
                foreach (var path in files)
                {
                    var def = matches[path];
                    if (def == null)
                    {
                        report.AppendLine($"  ? {Path.GetFileName(path),-28} no matching component — assign it by hand");
                        continue;
                    }

                    // Textures and materials are made here, outside the batch above, because they have to be
                    // imported before a material can point at them.
                    EditorUtility.DisplayProgressBar("BuildAR", $"Texturing {Path.GetFileName(path)}…", (float)done++ / files.Count);
                    bool textured = DressModel(path, TextureSizeFor(def.category), def.category == ComponentCategory.Case);

                    var prefab = BuildPrefab(path, def, textured, out string extras);
                    if (prefab == null)
                    {
                        report.AppendLine($"  ✗ {Path.GetFileName(path),-28} {CantReadMessage(path)}");
                        continue;
                    }

                    def.model3DPrefab = prefab;
                    EditorUtility.SetDirty(def);
                    report.AppendLine($"  ✓ {Path.GetFileName(path),-28} → {def.displayName}" +
                                      (textured ? "" : "  (no textures inside the file — kept its own materials)") +
                                      (extras != null ? $"  [{extras}]" : ""));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.AppendLine("\nEach part wears the textures that came inside its model file; they are unpacked into a " +
                              "Textures folder beside the model, with its URP material in Materials. If a part sits the " +
                              "wrong way round in its slot, set its Model Rotation Euler (usually 180 on one axis) and run " +
                              "this again.");
            Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------ the assembly case

        const string AssemblyModels = "Assets/_Project/Art/Models/Assembly";
        const string AssemblyCasePrefab = "Assets/_Project/Prefabs/Assembly/PH_AssemblyCase.prefab";
        /// <summary>Height the case model is first scaled to, in metres, before it is fitted to the board.</summary>
        const float CaseHeight = 0.46f;
        /// <summary>
        /// Outer face of the rear wall, and so of the front (+0.225), in the slot layout: the PSU bay and the
        /// front-panel cable were placed against these.
        /// </summary>
        const float CaseRear = -0.225f;
        /// <summary>Tray surface the built-in motherboard's back rests on in the slot layout.</summary>
        const float TrayZ = 0.101f;
        /// <summary>The built-in motherboard's footprint (x, y), where the case's tray is looked for.</summary>
        static readonly Rect BoardArea = new Rect(-0.192f, 0.1275f, 0.244f, 0.305f);
        /// <summary>Space left between the board's edges and the edges of the tray it sits on.</summary>
        const float TrayMargin = 0.01f;
        /// <summary>The PSU's back in the slot layout (bay centre -0.13, PSU 15 cm long).</summary>
        const float PsuRear = -0.205f;
        /// <summary>
        /// The floor's surface in the slot layout: the built-in case's floor panel is 4 mm thick, and the PSU bay and
        /// SSD mount stand on it. A fitted case's measured floor is lined up with this, not with y 0, or they hover.
        /// </summary>
        const float LayoutFloor = 0.004f;
        /// <summary>Cable grommets in the tray beside the board's front edge: how far out from it, and how high up it (0–1).</summary>
        const float GrommetGap = 0.03f;
        static readonly float[] GrommetHeights = { 0.18f, 0.5f, 0.82f };

        /// <summary>
        /// Swaps the case you build inside for your own model. The slot layout — the built-in motherboard and every
        /// slot and port on it — stays as authored; the case is fitted around it: stood upright with its open side
        /// towards the learner (local -Z), its near side panel taken off, and scaled and moved so its motherboard tray
        /// sits under the board. The PSU bay and the front-panel cable follow the case, and everything is recentred on
        /// the case's floor so the scene frames it as before.
        /// </summary>
        [MenuItem("BuildAR/Setup/Import Assembly Case Model", priority = 28)]
        public static void ImportAssemblyCase()
        {
            // Bring an older project up to date first (the SATA SSD's component, mount and guide steps), so the
            // fitting below places everything.
            BuildARSetup.UpgradeProject();

            string model = ModelFiles(AssemblyModels).FirstOrDefault();

            if (model == null)
            {
                EditorUtility.DisplayDialog("BuildAR",
                    $"Put your case model (.fbx) in {AssemblyModels}.\n\n" +
                    "Use a case with the side panel off or missing — a closed box hides everything you build inside it.", "OK");
                return;
            }

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssemblyCasePrefab);
            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("BuildAR", $"{AssemblyCasePrefab} doesn't exist yet.\n\nRun BuildAR ▸ Setup ▸ 4. Build Scenes first.", "OK");
                return;
            }

            ApplyImportSettings(model, readable: true);   // the mesh is read to find the open side and the tray
            bool textured = DressModel(model, 2048, caseLook: true);

            var contents = PrefabUtility.LoadPrefabContents(AssemblyCasePrefab);
            try
            {
                var visuals = contents.transform.Find("Visuals");
                if (visuals == null) { Debug.LogError("BuildAR: the assembly case prefab has no 'Visuals' child."); return; }

                ResetLayout(contents);
                for (int i = visuals.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visuals.GetChild(i).gameObject);

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(model);
                if (source == null) { Debug.LogError($"BuildAR: '{Path.GetFileName(model)}' — {CantReadMessage(model)}."); return; }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, visuals);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Keep the model's own rotation: a Z-up export carries its correction on the root, and dropping it
                // lays the case on its back.
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = source.transform.localRotation;
                instance.transform.localScale = Vector3.one;

                if (!TryWorldBounds(instance, out Bounds bounds))
                {
                    Debug.LogError($"BuildAR: '{Path.GetFileName(model)}' — {CantReadMessage(model)}.");
                    return;
                }

                string turned = FaceOpenSideToLearner(instance);

                // Scale by height, stand it on the floor, and put its rear wall on the layout's rear wall.
                TryWorldBounds(instance, out bounds);
                instance.transform.localScale = Vector3.one * (bounds.size.y > 0.0001f ? CaseHeight / bounds.size.y : 1f);
                TryWorldBounds(instance, out bounds);
                instance.transform.position += new Vector3(CaseRear - bounds.min.x, -bounds.min.y, -bounds.center.z);

                int removed = OpenUpCase(instance, model);

                // Put the tray under the board, growing the case if the tray is smaller than the board. The board goes
                // in the tray's rear-top corner, as in a real case: its I/O against the rear cut-out and its PCIe
                // slots beside the expansion slot covers, with any spare tray left at the front and bottom.
                TryWorldBounds(instance, out bounds);
                var triangles = WorldTriangles(instance);
                float? tray = FindTray(triangles, bounds);
                Rect? area = tray.HasValue ? TrayArea(triangles, bounds, tray.Value) : null;
                float fit = 1f;
                if (area.HasValue)
                {
                    var a = area.Value;
                    fit = Mathf.Clamp(Mathf.Max((BoardArea.width + 2f * TrayMargin) / a.width,
                                                (BoardArea.height + 2f * TrayMargin) / a.height), 1f, 1.5f);
                    var corner = new Vector3(a.xMin, a.yMax, tray.Value);
                    instance.transform.localScale *= fit;
                    instance.transform.position = corner + (instance.transform.position - corner) * fit;
                    instance.transform.position += new Vector3(BoardArea.xMin - TrayMargin, BoardArea.yMax + TrayMargin, TrayZ) - corner;
                }
                else
                {
                    // Without a tray to go on, the board goes against the inside of the far wall.
                    instance.transform.position += new Vector3(0f, 0f, TrayZ - (tray ?? bounds.max.z - 0.004f));
                }

                bool painted = !textured && PaintWithCaseMaterial(instance);

                // The PSU bay and its cables sit against the case's rear and floor, the front-panel cable against its
                // front: move them with the case. The floor is measured, not taken as the model's lowest point —
                // generated cases stand on feet, and a PSU put at the bottom of the feet pokes out under the case.
                // Then recentre everything on the case's floor.
                TryWorldBounds(instance, out bounds);
                var (floor, rearStop) = FindPsuBay(WorldTriangles(instance), bounds);
                float feet = floor - bounds.min.y;
                var fitState = contents.GetComponent<AssemblyCaseFit>();
                if (fitState == null) fitState = contents.AddComponent<AssemblyCaseFit>();
                fitState.psuOffset = new Vector3(rearStop - PsuRear, floor - LayoutFloor, bounds.center.z);
                fitState.frontOffset = new Vector3(bounds.max.x + CaseRear, floor - LayoutFloor, bounds.center.z);
                OffsetCaseAnchors(contents, fitState.psuOffset, fitState.frontOffset, +1f);
                AddCableRoutes(contents);

                AddBuiltInMotherboard(contents);
                var recentre = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                instance.transform.position += recentre;
                foreach (var group in LayoutGroups)
                {
                    var parent = contents.transform.Find(group);
                    if (parent != null) parent.localPosition = recentre;
                }

                // The detail the generated textures lack, and the PC Case component's fans when it's this same model.
                var sourceMesh = source.GetComponentsInChildren<MeshFilter>()
                    .Select(f => f.sharedMesh).Where(m => m != null)
                    .OrderByDescending(m => m.bounds.size.sqrMagnitude).FirstOrDefault();
                var board = new Rect(BoardArea.x + recentre.x, BoardArea.y + recentre.y, BoardArea.width, BoardArea.height);
                var psuBay = contents.GetComponentsInChildren<SnapSlot>(true).FirstOrDefault(s => s.acceptsCategory == ComponentCategory.PowerSupply);
                Vector3 psuAt = psuBay != null ? psuBay.transform.position : new Vector3(-0.15f, 0f, 0f);
                var psuFootprint = new Rect(psuAt.x - 0.075f, psuAt.z - 0.07f, 0.15f, 0.14f);
                string detail = textured
                    ? BakeCaseDetail(instance, sourceMesh, model, TrayZ + recentre.z, board, floor + recentre.y, psuFootprint)
                    : null;
                string twin = CaseComponentModel(model, out var caseDef);
                if (detail != null && twin != null) ShareTextures(model, twin);
                string fxPath = Path.ChangeExtension(model, null) + "_fx.asset";
                string fans = caseDef != null && sourceMesh != null
                    ? AddFansAndLights(instance, caseDef.modelFans, null, caseDef.modelPanels, fxPath, sourceMesh.bounds)
                    : null;
                var caseMesh = instance.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).Where(m => m != null)
                    .OrderByDescending(m => m.bounds.size.sqrMagnitude).FirstOrDefault();
                bool cut = caseMesh != null && AssetDatabase.GetAssetPath(caseMesh) == fxPath;
                if (cut) AssetDatabase.DeleteAsset(Path.ChangeExtension(model, null) + "_open.asset");   // superseded by _fx

                AddCaseLighting(contents, instance);

                PrefabUtility.SaveAsPrefabAsset(contents, AssemblyCasePrefab);
                TryWorldBounds(instance, out bounds);
                string meshAsset = Path.GetFileNameWithoutExtension(model) + (cut ? "_fx.asset" : "_open.asset");
                Debug.Log($"BuildAR: '{Path.GetFileName(model)}' is now the assembly case — " +
                          $"{bounds.size.x:0.###} × {bounds.size.y:0.###} × {bounds.size.z:0.###} m.\n" +
                          $"  • {turned}.\n" +
                          (removed > 0
                              ? $"  • Took the near side panel off ({removed} faces) and gave the walls back faces; " +
                                $"saved as {meshAsset}.\n"
                              : "  • Found no side panel to take off. If you can't see inside, the model's open side may be facing away.\n") +
                          (detail != null ? $"  • Painted into its textures: {detail}" +
                                            (twin != null ? $" (the PC Case component, the same model, wears them too)" : "") + ".\n" : "") +
                          (fans != null ? $"  • From the PC Case component (the same model): {fans}.\n" : "") +
                          (area.HasValue
                              ? $"  • Its motherboard tray holds the board{(fit > 1.001f ? $" once the case is scaled up ×{fit:0.00}" : "")}; " +
                                "the case sits so the board rests on it.\n"
                              : tray.HasValue
                                  ? "  • Motherboard tray found, but not its edges; the board rests on it at its usual place.\n"
                                  : "  • No motherboard tray found; the board rests against the far wall.\n") +
                          $"  • The PSU stands on the case floor ({feet * 100f:0.#} cm above the bottom of the model), its back " +
                          "against the rear wall, with the SATA SSD's mount beside it; the front-panel cable moved with the case; " +
                          "slots and ports on the board didn't.\n" +
                          "  • Cables run behind the motherboard tray and come out through the grommets beside the board " +
                          "(CPU power over its top edge), as in a tidy real build.\n" +
                          (textured ? "  • Wearing the textures that came inside the model, darkened to painted steel.\n"
                           : painted ? "  • No textures inside the model, so it's painted with M_Case (matte black).\n" : "") +
                          "  • The built-in motherboard is under 'Motherboard' — the slots are laid out on it.\n" +
                          "  • RGB: a strip along the top of the opening and a colour-cycling light over the board, " +
                          "under 'Lighting'; CaseGlow makes every LED in the build bloom.\n" +
                          "If the open side faces away from you, rotate the 'Visuals' child by 180° on Y in the prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>The children of the case prefab that hold the layout: the board and everything plugged into it.</summary>
        static readonly string[] LayoutGroups = { "Slots", "CablePorts", "Motherboard" };

        /// <summary>
        /// Puts the slots and ports back where they were authored: the layout groups back on the origin, the PSU and
        /// front-panel offsets taken off, and the stretch older versions of this importer applied undone.
        /// </summary>
        internal static void ResetLayout(GameObject root)
        {
            foreach (var group in LayoutGroups)
            {
                var parent = root.transform.Find(group);
                if (parent != null) parent.localPosition = Vector3.zero;
            }

            var fit = root.GetComponent<AssemblyCaseFit>();
            if (fit == null) return;

            OffsetCaseAnchors(root, fit.psuOffset, fit.frontOffset, -1f);
            var ratio = fit.appliedRatio;
            if (ratio.x > 0.0001f && ratio.z > 0.0001f && (ratio.x != 1f || ratio.z != 1f))
            {
                foreach (var group in new[] { "Slots", "CablePorts" })
                {
                    var parent = root.transform.Find(group);
                    if (parent == null) continue;
                    foreach (Transform child in parent)
                    {
                        var p = child.localPosition;
                        child.localPosition = new Vector3(p.x / ratio.x, p.y, p.z / ratio.z);
                    }
                }
            }
            fit.appliedRatio = Vector3.one;
            fit.psuOffset = fit.frontOffset = Vector3.zero;
        }

        /// <summary>
        /// Moves the anchors that belong to the case rather than the board (by psu): the PSU bay and any other slot
        /// mounted on the case (the SATA SSD's), their glow boxes, and the ports that only appear once one is filled —
        /// the PSU's cables and the drive's sockets. Source cables that need nothing, like the front-panel one, move
        /// with the front (by front).
        /// </summary>
        static void OffsetCaseAnchors(GameObject root, Vector3 psu, Vector3 front, float sign)
        {
            var slots = root.GetComponentsInChildren<SnapSlot>(true);
            bool OnCase(SnapSlot s) => s.acceptsCategory == ComponentCategory.PowerSupply || s.mountedOnCase;
            bool CaseSlot(string slotId) => slots.Any(s => s.slotId == slotId && OnCase(s));

            foreach (var group in new[] { "Slots", "CablePorts" })
            {
                var parent = root.transform.Find(group);
                if (parent == null) continue;
                foreach (Transform child in parent)
                {
                    Vector3 offset = Vector3.zero;
                    if (child.TryGetComponent(out SnapSlot slot))
                    {
                        if (OnCase(slot)) offset = psu;
                    }
                    else if (child.TryGetComponent(out CablePort port))
                    {
                        if (CaseSlot(port.requiresSlotId)) offset = psu;
                        else if (port.isSource && string.IsNullOrEmpty(port.requiresSlotId)) offset = front;
                    }
                    else if (child.name.StartsWith("Highlight_") && CaseSlot(child.name.Substring("Highlight_".Length)))
                    {
                        offset = psu;   // glow boxes are named after their slot
                    }
                    child.localPosition += offset * sign;
                }
            }
        }

        /// <summary>
        /// Turns the case so one of its big sides faces the learner (local -Z), and makes it the open one. Which side
        /// is open is read from the mesh: a side panel is a large area of faces lying flat against its side, while an
        /// open side has only its frame. Returns what it did, for the log.
        /// </summary>
        static string FaceOpenSideToLearner(GameObject instance)
        {
            var t = instance.transform;
            TryWorldBounds(instance, out Bounds bounds);
            var size = bounds.size;
            string turned = null;
            if (size.x < size.z && size.x <= size.y)
            {
                t.rotation = Quaternion.Euler(0f, 90f, 0f) * t.rotation;
                turned = "Turned 90° to put a side towards you";
            }
            else if (size.y < size.z && size.y < size.x)
            {
                t.rotation = Quaternion.Euler(90f, 0f, 0f) * t.rotation;
                turned = "Stood it up (it was lying on its side)";
            }

            string open;
            var triangles = WorldTriangles(instance);
            if (triangles.Count == 0) open = "couldn't read the mesh to tell which side is open";
            else
            {
                TryWorldBounds(instance, out bounds);
                float slab = bounds.size.z * 0.1f;
                float near = PanelArea(triangles, bounds.min.z, slab), far = PanelArea(triangles, bounds.max.z, slab);
                if (far < near)
                {
                    t.rotation = Quaternion.Euler(0f, 180f, 0f) * t.rotation;
                    open = "turned 180° so the open side faces you";
                }
                else open = "the open side already faced you";
            }

            return turned != null ? $"{turned}; {open}" : char.ToUpperInvariant(open[0]) + open.Substring(1);
        }

        /// <summary>Area of the faces lying flat against one big side (within <paramref name="slab"/> of it).</summary>
        static float PanelArea(List<Vector3> triangles, float sideZ, float slab)
        {
            float area = 0f;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var cross = Vector3.Cross(b - a, c - a);
                float twice = cross.magnitude;
                if (Mathf.Abs(cross.z) < 0.7f * twice) continue;
                if (Mathf.Abs((a.z + b.z + c.z) / 3f - sideZ) > slab) continue;
                area += twice * 0.5f;
            }
            return area;
        }

        /// <summary>
        /// Takes the side panel off a case that comes as one welded mesh, as generated models do, and makes what's
        /// left look solid from inside:
        ///   • faces lying flat against the near side (within 10% of the depth) go — the panel, its frame, any glass;
        ///   • everything in front of a cut 3% into the case is sliced off, so no thin rims of the old frame are left
        ///     hanging around the opening;
        ///   • every face gets a back face, because the model's walls are single-sided shells, and looking in through
        ///     the opening would otherwise show straight through them.
        /// The result is saved beside the model as &lt;name&gt;_open.asset. Returns how many faces were removed.
        /// </summary>
        static int OpenUpCase(GameObject instance, string modelPath)
        {
            TryWorldBounds(instance, out Bounds bounds);
            float flatLimit = bounds.min.z + bounds.size.z * 0.1f;
            float cut = bounds.min.z + bounds.size.z * 0.03f;
            string assetPath = Path.ChangeExtension(modelPath, null) + "_open.asset";
            AssetDatabase.DeleteAsset(assetPath);

            int removed = 0;
            bool saved = false;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                var open = OpenedMesh(mesh, filter.transform.localToWorldMatrix, flatLimit, cut, out int dropped);
                removed += dropped;
                if (!saved) { AssetDatabase.CreateAsset(open, assetPath); saved = true; }
                else AssetDatabase.AddObjectToAsset(open, assetPath);
                filter.sharedMesh = open;
            }

            if (saved) AssetDatabase.SaveAssets();
            return removed;
        }

        /// <summary>A copy of the mesh with the near side removed and cut clean, and back faces added (see OpenUpCase).</summary>
        static Mesh OpenedMesh(Mesh mesh, Matrix4x4 toWorld, float flatLimit, float cut, out int dropped)
        {
            var positions = new List<Vector3>(mesh.vertices);
            var normals = new List<Vector3>(mesh.normals);
            var tangents = new List<Vector4>(mesh.tangents);
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var colors = new List<Color32>(mesh.colors32);
            int count = positions.Count;
            bool hasNormals = normals.Count == count, hasTangents = tangents.Count == count;
            bool hasUvs = uvs.Count == count, hasColors = colors.Count == count;

            var depth = new List<float>(count);   // world z, which decides what is cut
            foreach (var p in positions) depth.Add(toWorld.MultiplyPoint3x4(p).z);

            int Between(int a, int b, float t)
            {
                positions.Add(Vector3.Lerp(positions[a], positions[b], t));
                depth.Add(Mathf.Lerp(depth[a], depth[b], t));
                if (hasNormals) normals.Add(Vector3.Lerp(normals[a], normals[b], t).normalized);
                if (hasTangents)
                {
                    var tangent = ((Vector3)Vector4.Lerp(tangents[a], tangents[b], t)).normalized;
                    tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, tangents[a].w));
                }
                if (hasUvs) uvs.Add(Vector2.Lerp(uvs[a], uvs[b], t));
                if (hasColors) colors.Add(Color32.Lerp(colors[a], colors[b], t));
                return positions.Count - 1;
            }

            dropped = 0;
            var submeshes = new List<List<int>>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var source = mesh.GetTriangles(s);
                var kept = new List<int>(source.Length);
                for (int t = 0; t < source.Length; t += 3)
                {
                    int ia = source[t], ib = source[t + 1], ic = source[t + 2];
                    Vector3 a = toWorld.MultiplyPoint3x4(positions[ia]), b = toWorld.MultiplyPoint3x4(positions[ib]),
                            c = toWorld.MultiplyPoint3x4(positions[ic]);
                    var cross = Vector3.Cross(b - a, c - a);
                    if (Mathf.Abs(cross.z) > 0.7f * cross.magnitude && (a.z + b.z + c.z) / 3f < flatLimit) { dropped++; continue; }

                    // Keep what lies beyond the cut; split triangles that cross it.
                    var corners = new[] { ia, ib, ic };
                    var polygon = new List<int>(4);
                    for (int k = 0; k < 3; k++)
                    {
                        int from = corners[k], to = corners[(k + 1) % 3];
                        float df = depth[from] - cut, dt = depth[to] - cut;
                        if (df >= 0f) polygon.Add(from);
                        if ((df >= 0f) != (dt >= 0f)) polygon.Add(Between(from, to, df / (df - dt)));
                    }
                    if (polygon.Count < 3) { dropped++; continue; }
                    for (int k = 1; k < polygon.Count - 1; k++) { kept.Add(polygon[0]); kept.Add(polygon[k]); kept.Add(polygon[k + 1]); }
                }
                submeshes.Add(kept);
            }

            // Back faces: the same surface facing the other way, so single-sided walls look solid from inside.
            int front = positions.Count;
            for (int i = 0; i < front; i++)
            {
                positions.Add(positions[i]);
                if (hasNormals) normals.Add(-normals[i]);
                if (hasTangents) tangents.Add(new Vector4(tangents[i].x, tangents[i].y, tangents[i].z, -tangents[i].w));
                if (hasUvs) uvs.Add(uvs[i]);
                if (hasColors) colors.Add(colors[i]);
            }
            foreach (var list in submeshes)
            {
                int faces = list.Count;
                for (int t = 0; t < faces; t += 3)
                {
                    list.Add(list[t] + front); list.Add(list[t + 2] + front); list.Add(list[t + 1] + front);
                }
            }

            var open = new Mesh { name = mesh.name + " (open)" };
            if (positions.Count > 65535) open.indexFormat = IndexFormat.UInt32;
            open.SetVertices(positions);
            if (hasNormals) open.SetNormals(normals);
            if (hasTangents) open.SetTangents(tangents);
            if (hasUvs) open.SetUVs(0, uvs);
            if (hasColors) open.SetColors(colors);
            open.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) open.SetTriangles(submeshes[s], s);
            if (!hasNormals) open.RecalculateNormals();
            if (!hasTangents && hasUvs) open.RecalculateTangents();
            var finalNormals = open.normals;
            var finalTangents = open.tangents;
            if (finalTangents.Length == finalNormals.Length && finalTangents.Length > 0)
            {
                for (int i = 0; i < finalTangents.Length; i++) finalTangents[i] = SafeTangent(finalTangents[i], finalNormals[i]);
                open.tangents = finalTangents;
            }
            open.RecalculateBounds();
            return open;
        }

        /// <summary>
        /// Depth of the motherboard tray: looks into the case from the open side across the board's footprint and
        /// takes the first surface each line of sight meets (the median, so a fan or cable duct in the way doesn't
        /// count). Real cases keep the tray a few centimetres inside the far wall, with cables run behind it, so
        /// using the far wall would hide the board. Null when too little is hit or the hits are in the near half.
        /// </summary>
        static float? FindTray(List<Vector3> triangles, Bounds caseBounds)
        {
            var hits = new List<float>();
            for (int ix = 0; ix < 7; ix++)
            {
                for (int iy = 0; iy < 7; iy++)
                {
                    // The middle 80% of the board, so the rays stay clear of the walls around it.
                    float x = BoardArea.center.x + (ix / 6f - 0.5f) * BoardArea.width * 0.8f;
                    float y = BoardArea.center.y + (iy / 6f - 0.5f) * BoardArea.height * 0.8f;
                    float nearest = float.MaxValue;
                    for (int i = 0; i < triangles.Count; i += 3)
                        if (CrossesAt(triangles[i], triangles[i + 1], triangles[i + 2], x, y, out float z) && z < nearest)
                            nearest = z;
                    if (nearest < float.MaxValue) hits.Add(nearest);
                }
            }

            if (hits.Count < 10) return null;
            hits.Sort();
            float median = hits[hits.Count / 2];
            return median > caseBounds.center.z ? median : (float?)null;
        }

        /// <summary>
        /// The part of the case's face where the tray shows through the opening, found on a 1 cm grid: the longest
        /// run of rows, and of columns, in which at least a quarter of the lines of sight end on the tray (within
        /// 1 cm of its depth). Null if too little of it shows.
        /// </summary>
        static Rect? TrayArea(List<Vector3> triangles, Bounds caseBounds, float trayZ)
        {
            const float step = 0.01f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(caseBounds.size.x / step));
            int rows = Mathf.Max(1, Mathf.FloorToInt(caseBounds.size.y / step));
            int n = triangles.Count / 3;
            var minX = new float[n]; var maxX = new float[n]; var minY = new float[n]; var maxY = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = triangles[3 * i], b = triangles[3 * i + 1], c = triangles[3 * i + 2];
                minX[i] = Mathf.Min(a.x, b.x, c.x); maxX[i] = Mathf.Max(a.x, b.x, c.x);
                minY[i] = Mathf.Min(a.y, b.y, c.y); maxY[i] = Mathf.Max(a.y, b.y, c.y);
            }

            var onTray = new bool[rows, columns];
            var inRow = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                float y = caseBounds.min.y + (r + 0.5f) * step;
                inRow.Clear();
                for (int i = 0; i < n; i++) if (minY[i] <= y && y <= maxY[i]) inRow.Add(i);
                for (int col = 0; col < columns; col++)
                {
                    float x = caseBounds.min.x + (col + 0.5f) * step;
                    float nearest = float.MaxValue;
                    foreach (int i in inRow)
                    {
                        if (x < minX[i] || x > maxX[i]) continue;
                        if (CrossesAt(triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2], x, y, out float z) && z < nearest)
                            nearest = z;
                    }
                    onTray[r, col] = Mathf.Abs(nearest - trayZ) < 0.01f;
                }
            }

            var rowShare = new float[rows];
            var columnShare = new float[columns];
            for (int r = 0; r < rows; r++)
                for (int col = 0; col < columns; col++)
                    if (onTray[r, col]) { rowShare[r] += 1f / columns; columnShare[col] += 1f / rows; }

            var (r0, r1) = LongestRun(rowShare, 0.25f);
            var (c0, c1) = LongestRun(columnShare, 0.25f);
            if (r1 - r0 < 5 || c1 - c0 < 5) return null;
            return Rect.MinMaxRect(caseBounds.min.x + c0 * step, caseBounds.min.y + r0 * step,
                                   caseBounds.min.x + (c1 + 1) * step, caseBounds.min.y + (r1 + 1) * step);
        }

        /// <summary>First and last index of the longest stretch of values at or above the threshold.</summary>
        static (int first, int last) LongestRun(float[] values, float threshold)
        {
            int bestStart = 0, bestLength = 0, start = -1;
            for (int i = 0; i <= values.Length; i++)
            {
                bool inside = i < values.Length && values[i] >= threshold;
                if (inside && start < 0) start = i;
                if (!inside && start >= 0)
                {
                    if (i - start > bestLength) { bestLength = i - start; bestStart = start; }
                    start = -1;
                }
            }
            return (bestStart, bestStart + bestLength - 1);
        }

        /// <summary>
        /// Where the PSU goes in a fitted case: the height of the floor under the bay (looking straight down across
        /// the bay's footprint, the first surface each line of sight meets, the median) and how far forward the
        /// rear wall's inside face is at PSU height (looking back towards it the same way). A PSU's back normally
        /// sits in the rear panel's cut-out, 2 cm in from the outside; where the model's rear wall is thicker than
        /// that, the PSU stops against it instead (at most 7 cm in). Falls back to the model's bounds.
        /// </summary>
        static (float floor, float rear) FindPsuBay(List<Vector3> triangles, Bounds caseBounds)
        {
            var floors = new List<float>();
            float top = caseBounds.min.y + 0.1f;
            for (int ix = 0; ix < 5; ix++)
                for (int iz = 0; iz < 5; iz++)
                {
                    float x = caseBounds.min.x + 0.035f + ix / 4f * 0.13f;
                    float z = caseBounds.center.z + (iz / 4f - 0.5f) * 0.12f;
                    float highest = float.MinValue;
                    for (int i = 0; i < triangles.Count; i += 3)
                        if (CrossesAt(Swizzle(triangles[i], 0), Swizzle(triangles[i + 1], 0), Swizzle(triangles[i + 2], 0), x, z, out float y) &&
                            y < top && y > highest)
                            highest = y;
                    if (highest > float.MinValue) floors.Add(highest);
                }
            floors.Sort();
            float floor = floors.Count >= 8 ? floors[floors.Count / 2] : caseBounds.min.y;

            var rears = new List<float>();
            float inside = caseBounds.min.x + 0.12f;
            for (int iy = 0; iy < 4; iy++)
                for (int iz = 0; iz < 5; iz++)
                {
                    float y = floor + 0.02f + iy / 3f * 0.06f;
                    float z = caseBounds.center.z + (iz / 4f - 0.5f) * 0.1f;
                    float nearest = float.MinValue;
                    for (int i = 0; i < triangles.Count; i += 3)
                        if (CrossesAt(Swizzle(triangles[i], 1), Swizzle(triangles[i + 1], 1), Swizzle(triangles[i + 2], 1), y, z, out float x) &&
                            x < inside && x > nearest)
                            nearest = x;
                    if (nearest > float.MinValue) rears.Add(nearest);
                }
            rears.Sort();
            float rear = caseBounds.min.x + 0.02f;
            if (rears.Count >= 8) rear = Mathf.Clamp(rears[rears.Count / 2] + 0.003f, rear, caseBounds.min.x + 0.07f);
            return (floor, rear);
        }

        /// <summary>Reorders a point's axes so CrossesAt's line of sight runs along Y (0: sees X,Z) or X (1: sees Y,Z).</summary>
        static Vector3 Swizzle(Vector3 p, int along) => along == 0 ? new Vector3(p.x, p.z, p.y) : new Vector3(p.y, p.z, p.x);

        /// <summary>
        /// Gives every header on the board a route exit (see CablePort.routeExit): the grommet in the tray nearest to
        /// its height beside the board's front edge, or just over the board's top edge for a header along the top
        /// (CPU power), where real cases have a cut-out. Cables then run behind the tray like a tidy real build.
        /// </summary>
        static void AddCableRoutes(GameObject root)
        {
            RemoveCableRoutes(root);
            var ports = root.transform.Find("CablePorts");
            if (ports == null) return;
            foreach (var port in ports.GetComponentsInChildren<CablePort>(true))
            {
                if (port.isSource || port.direct) continue;   // direct: a drive's socket, reached the short way
                Vector3 p = port.transform.localPosition;   // slot layout: CablePorts only ever moves as a whole
                Vector3 exit;
                if (p.y > BoardArea.yMax - 0.04f && p.x < BoardArea.xMax - 0.06f)
                    exit = new Vector3(p.x, BoardArea.yMax + 0.006f, TrayZ);
                else
                {
                    float height = GrommetHeights
                        .Select(t => Mathf.Lerp(BoardArea.yMin, BoardArea.yMax, t))
                        .OrderBy(y => Mathf.Abs(y - p.y)).First();
                    exit = new Vector3(BoardArea.xMax + GrommetGap, height, TrayZ);
                }
                var route = new GameObject($"Route_{port.portId}").transform;
                route.SetParent(ports, false);
                route.localPosition = exit;
                port.routeExit = route;
            }
        }

        /// <summary>Takes the route exits off again, for a case without grommets (the built-in one).</summary>
        internal static void RemoveCableRoutes(GameObject root)
        {
            var ports = root.transform.Find("CablePorts");
            if (ports == null) return;
            foreach (var port in ports.GetComponentsInChildren<CablePort>(true)) port.routeExit = null;
            for (int i = ports.childCount - 1; i >= 0; i--)
                if (ports.GetChild(i).name.StartsWith("Route_")) Object.DestroyImmediate(ports.GetChild(i).gameObject);
        }

        /// <summary>Where a line of sight along +Z through (x, y) crosses the triangle, if it does.</summary>
        static bool CrossesAt(Vector3 a, Vector3 b, Vector3 c, float x, float y, out float z)
        {
            z = 0f;
            float e1x = b.x - a.x, e1y = b.y - a.y, e2x = c.x - a.x, e2y = c.y - a.y, px = x - a.x, py = y - a.y;
            float det = e1x * e2y - e2x * e1y;
            if (Mathf.Abs(det) < 1e-12f) return false;   // seen edge-on
            float u = (px * e2y - e2x * py) / det, v = (e1x * py - px * e1y) / det;
            if (u < 0f || v < 0f || u + v > 1f) return false;
            z = a.z + u * (b.z - a.z) + v * (c.z - a.z);
            return true;
        }

        /// <summary>Every triangle of the model in world space, three corners at a time. Unreadable meshes are skipped.</summary>
        static List<Vector3> WorldTriangles(GameObject instance)
        {
            var corners = new List<Vector3>();
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var toWorld = filter.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                foreach (int index in mesh.triangles) corners.Add(toWorld.MultiplyPoint3x4(vertices[index]));
            }
            return corners;
        }

        static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            bounds = renderers.Length > 0 ? renderers[0].bounds : default;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return renderers.Length > 0;
        }

        /// <summary>
        /// The slots are laid out on the built-in motherboard, so an imported case gets that board too, in its own
        /// 'Motherboard' child. Delete it if your case model has a board of its own and you've moved the slots onto it.
        /// </summary>
        static void AddBuiltInMotherboard(GameObject root)
        {
            var old = root.transform.Find("Motherboard");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var board = new GameObject("Motherboard").transform;
            board.SetParent(root.transform, false);
            BuildARSetup.BuildCaseMotherboard(board);
        }

        // ------------------------------------------------------------------ import settings

        static void ApplyImportSettings(string path, bool readable = false)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;

            importer.globalScale = 1f;                 // real size is handled on the prefab instead
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            importer.importConstraints = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.isReadable = readable;            // GPU-only saves memory on the phone
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// Gives a model its real colours. Generated models (Hyper3D Rodin, most photogrammetry exports) carry their
        /// textures inside the .fbx. Those are unpacked into Textures/, metallic and roughness are packed the way
        /// URP/Lit reads them, and every material in the model is remapped onto one URP material in Materials/.
        /// A model with no embedded textures keeps its own materials, converted to URP where needed.
        /// <paramref name="caseLook"/> darkens the colour map to painted steel (see DarkSteel).
        /// Returns true when the model was given its textures.
        /// </summary>
        static bool DressModel(string modelPath, int maxTextureSize, bool caseLook = false)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer)) return false;

            var material = BuildTexturedMaterial(modelPath, maxTextureSize, caseLook);
            if (material == null)
            {
                RemapNonUrpMaterials(modelPath, importer);
                return false;
            }

            foreach (string name in SourceMaterialNames(modelPath, importer))
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>The model's own material names, including ones already remapped by an earlier run.</summary>
        static IEnumerable<string> SourceMaterialNames(string modelPath, ModelImporter importer)
        {
            var names = new HashSet<string>();
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>()) names.Add(material.name);
            foreach (var pair in importer.GetExternalObjectMap())
                if (pair.Key.type == typeof(Material)) names.Add(pair.Key.name);
            return names;
        }

        static Material BuildTexturedMaterial(string modelPath, int maxTextureSize, bool caseLook)
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null || Path.GetExtension(modelPath).ToLowerInvariant() != ".fbx") return null;

            var maps = EmbeddedMaps(modelPath);
            if (!maps.TryGetValue(TextureMap.BaseColor, out byte[] baseColor)) return null;
            if (caseLook) baseColor = DarkSteel(baseColor);

            string folder = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(modelPath);
            EnsureFolder(folder + "/Textures");
            EnsureFolder(folder + "/Materials");
            string prefix = $"{folder}/Textures/{name}";

            var baseMap = SaveTexture($"{prefix}_BaseMap.png", baseColor, maxTextureSize, TextureImporterType.Default, true);
            var normalMap = maps.TryGetValue(TextureMap.Normal, out byte[] normal)
                ? SaveTexture($"{prefix}_Normal.png", normal, maxTextureSize, TextureImporterType.NormalMap, false)
                : null;
            byte[] packed = maps.TryGetValue(TextureMap.Metallic, out byte[] metallic) &&
                            maps.TryGetValue(TextureMap.Roughness, out byte[] roughness)
                ? PackMetallicSmoothness(metallic, roughness)
                : null;
            var maskMap = packed != null
                ? SaveTexture($"{prefix}_MetallicSmoothness.png", packed, maxTextureSize, TextureImporterType.Default, false)
                : null;

            string materialPath = $"{folder}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(urpLit) { name = name };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = urpLit;

            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);

            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            if (normalMap != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");

            material.SetTexture("_MetallicGlossMap", maskMap);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            if (maskMap != null)
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 1f);   // multiplies the map's alpha
            }
            else
            {
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.4f);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// Generated cases can come out mid-grey with pale smudges where the generator baked in lighting. Half
        /// desaturating the colour map and squeezing it so its typical brightness lands at 16% reads as the black
        /// painted steel of a real case while keeping its detail. The squeeze never goes past 1:1, so a map that is
        /// already dark keeps its contrast rather than being crushed to flat black.
        /// </summary>
        static byte[] DarkSteel(byte[] png)
        {
            var source = new Texture2D(2, 2);
            try
            {
                if (!source.LoadImage(png)) return png;
                var pixels = source.GetPixels32();
                var greys = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                    greys[i] = 0.2126f * pixels[i].r + 0.7152f * pixels[i].g + 0.0722f * pixels[i].b;
                var sorted = (float[])greys.Clone();
                System.Array.Sort(sorted);
                float median = Mathf.Max(1f, sorted[sorted.Length / 2]);
                float gain = Mathf.Clamp((SteelMid - SteelFloor) / median, 0.2f, 1f);

                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    float grey = greys[i];
                    pixels[i] = new Color32(Steel(c.r, grey, gain), Steel(c.g, grey, gain), Steel(c.b, grey, gain), 255);
                }

                var result = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                try
                {
                    result.SetPixels32(pixels);
                    return result.EncodeToPNG();
                }
                finally { Object.DestroyImmediate(result); }
            }
            finally { Object.DestroyImmediate(source); }
        }

        /// <summary>Darkest value the grade leaves (7%) and where it puts the map's median (16%), out of 255.</summary>
        const float SteelFloor = 18f, SteelMid = 41f;

        static byte Steel(byte channel, float grey, float gain) =>
            (byte)Mathf.Clamp(SteelFloor + gain * (0.5f * channel + 0.5f * grey), 0f, 255f);

        /// <summary>Writes an image into the project (only when it changed) and sets it up for its job.</summary>
        static Texture2D SaveTexture(string path, byte[] png, int maxSize, TextureImporterType type, bool sRGB)
        {
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = type;
                importer.sRGBTexture = sRGB;
                importer.maxTextureSize = maxSize;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// URP/Lit reads metallic from the red channel of one map and smoothness from its alpha, while generated
        /// models ship separate metallic and roughness images. Null if they can't be read or don't match in size.
        /// </summary>
        static byte[] PackMetallicSmoothness(byte[] metallicPng, byte[] roughnessPng)
        {
            var metallic = new Texture2D(2, 2);
            var roughness = new Texture2D(2, 2);
            try
            {
                if (!metallic.LoadImage(metallicPng) || !roughness.LoadImage(roughnessPng)) return null;
                if (metallic.width != roughness.width || metallic.height != roughness.height) return null;

                var m = metallic.GetPixels32();
                var r = roughness.GetPixels32();
                var packed = new Color32[m.Length];
                for (int i = 0; i < packed.Length; i++)
                {
                    byte metal = Grey(m[i], metallic.format);
                    packed[i] = new Color32(metal, metal, metal, (byte)(255 - Grey(r[i], roughness.format)));
                }

                var result = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
                try
                {
                    result.SetPixels32(packed);
                    return result.EncodeToPNG();
                }
                finally { Object.DestroyImmediate(result); }
            }
            finally
            {
                Object.DestroyImmediate(metallic);
                Object.DestroyImmediate(roughness);
            }
        }

        /// <summary>The value of a greyscale image's pixel, whichever format it loaded as.</summary>
        static byte Grey(Color32 c, TextureFormat format) => format == TextureFormat.Alpha8 ? c.a : c.r;

        enum TextureMap { BaseColor, Normal, Metallic, Roughness }

        /// <summary>
        /// PNG images embedded in a binary FBX, by what they are. FBX stores each one right after the file name it was
        /// exported under (texture_diffuse.png, texture_normal.png, …), which is what tells them apart.
        /// </summary>
        static Dictionary<TextureMap, byte[]> EmbeddedMaps(string fbxPath)
        {
            var maps = new Dictionary<TextureMap, byte[]>();
            byte[] data;
            try { data = File.ReadAllBytes(fbxPath); }
            catch (IOException) { return maps; }

            for (int i = System.Array.IndexOf(data, (byte)0x89); i >= 0 && i + 8 < data.Length;
                 i = System.Array.IndexOf(data, (byte)0x89, i + 1))
            {
                if (!IsPngSignature(data, i)) continue;
                int end = PngEnd(data, i);
                if (end < 0) continue;

                var role = RoleOf(NameBefore(data, i));
                if (role.HasValue && !maps.ContainsKey(role.Value))
                {
                    var png = new byte[end - i];
                    System.Buffer.BlockCopy(data, i, png, 0, png.Length);
                    maps[role.Value] = png;
                }
                i = end - 1;
            }
            return maps;
        }

        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        static bool IsPngSignature(byte[] data, int at)
        {
            for (int k = 0; k < PngSignature.Length; k++)
                if (data[at + k] != PngSignature[k]) return false;
            return true;
        }

        /// <summary>Index just past the image's IEND chunk, or -1 if the chunks run off the end.</summary>
        static int PngEnd(byte[] data, int start)
        {
            long pos = start + 8;
            while (pos + 8 <= data.Length)
            {
                long length = (long)data[pos] << 24 | (long)data[pos + 1] << 16 | (long)data[pos + 2] << 8 | data[pos + 3];
                bool iend = data[pos + 4] == 'I' && data[pos + 5] == 'E' && data[pos + 6] == 'N' && data[pos + 7] == 'D';
                pos += 12 + length;
                if (iend) return pos <= data.Length ? (int)pos : -1;
            }
            return -1;
        }

        /// <summary>The file name stored just before an embedded image, lower case, without its folder.</summary>
        static string NameBefore(byte[] data, int start)
        {
            int from = Mathf.Max(0, start - 512);
            for (int i = start - 4; i >= from; i--)
            {
                if (data[i] != '.' || (data[i + 1] | 0x20) != 'p' || (data[i + 2] | 0x20) != 'n' || (data[i + 3] | 0x20) != 'g')
                    continue;
                int begin = i;
                while (begin > from && IsNameChar(data[begin - 1])) begin--;
                return Encoding.ASCII.GetString(data, begin, i - begin).ToLowerInvariant();
            }
            return "";
        }

        static bool IsNameChar(byte b) =>
            b == '_' || b == '-' || b == '.' || (b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z');

        static TextureMap? RoleOf(string name)
        {
            if (name.Contains("normal")) return TextureMap.Normal;
            if (name.Contains("rough")) return TextureMap.Roughness;
            if (name.Contains("metal")) return TextureMap.Metallic;
            if (name.Contains("diffuse") || name.Contains("albedo") || name.Contains("basecolor") || name.Contains("base_color"))
                return TextureMap.BaseColor;
            return null;   // emission, previews and anything else are left out
        }

        /// <summary>
        /// Unity 6 keeps imported materials inside the model file and no longer writes them out beside it. In a URP
        /// project they normally arrive on URP/Lit already; any that don't would render pink, so those get a URP
        /// copy in a Materials folder and the model is remapped onto it.
        /// </summary>
        static void RemapNonUrpMaterials(string modelPath, ModelImporter importer)
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;

            var strays = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<Material>()
                .Where(m => m != null && m.shader != urpLit)
                .ToList();
            if (strays.Count == 0) return;

            string folder = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') + "/Materials";
            EnsureFolder(folder);

            foreach (var src in strays)
            {
                string matPath = $"{folder}/{src.name}.mat";
                var urp = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (urp == null)
                {
                    urp = new Material(urpLit) { name = src.name };
                    CopyToUrp(src, urp);
                    AssetDatabase.CreateAsset(urp, matPath);
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), urp);
            }
            importer.SaveAndReimport();
            Debug.Log($"BuildAR: converted {strays.Count} material(s) of {Path.GetFileName(modelPath)} to URP.");
        }

        static void CopyToUrp(Material src, Material dst)
        {
            var albedo = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex")
                       : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
            if (albedo != null) dst.SetTexture("_BaseMap", albedo);

            if (src.HasProperty("_Color")) dst.SetColor("_BaseColor", src.GetColor("_Color"));
            else if (src.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", src.GetColor("_BaseColor"));

            if (src.HasProperty("_BumpMap"))
            {
                var normal = src.GetTexture("_BumpMap");
                if (normal != null) { dst.SetTexture("_BumpMap", normal); dst.EnableKeyword("_NORMALMAP"); }
            }
            if (src.HasProperty("_Metallic")) dst.SetFloat("_Metallic", src.GetFloat("_Metallic"));
            if (src.HasProperty("_Glossiness")) dst.SetFloat("_Smoothness", src.GetFloat("_Glossiness"));
            else if (src.HasProperty("_Smoothness")) dst.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
        }

        // ------------------------------------------------------------------ prefab

        /// <summary>
        /// Turns the model to fit its slot, scales it to the part's real size and saves a prefab. The pivot is the
        /// geometric centre, except that a part going onto the board has its board-facing side where the placeholder's
        /// was (see ContactDepth). Fans and RGB lights go on last (see AddFansAndLights); <paramref name="extras"/>
        /// says what was added, or is null.
        /// </summary>
        static GameObject BuildPrefab(string modelPath, ComponentDefinitionSO def, bool textured, out string extras)
        {
            extras = null;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Object.DestroyImmediate(instance); return null; }

            var raw = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) raw.Encapsulate(renderers[i].bounds);

            // Turn the model so its shape matches the slot it will go into, then apply any hand correction. A case
            // isn't slotted anywhere, so it keeps the upright pose it was modelled in.
            var shape = def.category == ComponentCategory.Case ? source.transform.localRotation : OrientationFor(def.category, raw.size);
            instance.transform.rotation = Quaternion.Euler(def.modelRotationEuler) * shape;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var root = new GameObject(def.id);
            instance.transform.SetParent(root.transform, true);
            instance.transform.position = -bounds.center;                    // pivot at the geometric centre

            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float target = RealSize.TryGetValue(def.category, out float t) ? t : 0.15f;
            if (largest > 0.0001f)
            {
                float scale = target / largest;
                if (MaxHeight.TryGetValue(def.category, out float cap) && bounds.size.y * scale > cap)
                    scale = cap / bounds.size.y;
                if (ContactDepth.TryGetValue(def.category, out float contact))
                    instance.transform.position += Vector3.up * (bounds.extents.y - contact / scale);
                root.transform.localScale = Vector3.one * scale;
                if (scale < 0.33f || scale > 3f)
                    Debug.Log($"BuildAR: '{Path.GetFileName(modelPath)}' was {largest:0.###} units across; scaled by {scale:0.####} to {largest * scale:0.###} m.");
            }

            // A case that brought no textures of its own wears the house colour rather than plain white.
            if (def.category == ComponentCategory.Case && !textured) PaintWithCaseMaterial(instance);

            // Part labels go on before the fans are cut out, while the mesh is still the model file's own.
            string labels = AddLabels(instance, def.modelLabels);

            // After the sizing, so a light bar standing proud of the part doesn't change its size or pivot.
            extras = AddFansAndLights(instance, def, modelPath);
            if (labels != null) extras = extras == null ? labels : $"{extras}, {labels}";

            string prefabPath = $"{PrefabFolder}/{def.id}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ orientation

        /// <summary>
        /// Downloaded models point whichever way the artist left them, while the snap slots expect the placeholders'
        /// convention: a part's local Y sticks out of the motherboard, local X runs across it and local Z runs up it.
        ///
        /// Rather than ask you to measure each model, this works it out from the shape (see AxisTargets): the
        /// longest side lies across the board, and which of the other two sticks out of it depends on the part.
        /// Shape can't tell top from bottom, though, so a part can come out upside down — set the component's Model
        /// Rotation Euler (usually 180 on Z) to turn it over.
        /// </summary>
        static Quaternion OrientationFor(ComponentCategory category, Vector3 size)
        {
            // Axis indices ordered longest → shortest.
            var order = new[] { 0, 1, 2 };
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));

            // Where each of those should end up: X across the board, Y out of it, Z up it.
            int[] targets = AxisTargets(category);

            var columns = new Vector3[3];
            for (int rank = 0; rank < 3; rank++) columns[order[rank]] = Axis(targets[rank]);

            // Keep the basis right-handed, or the model comes out mirrored.
            if (Vector3.Dot(Vector3.Cross(columns[0], columns[1]), columns[2]) < 0f) columns[2] = -columns[2];

            return Quaternion.LookRotation(columns[2], columns[1]);
        }

        /// <summary>Where the longest, middle and shortest sides go (0 = X across the board, 1 = Y out of it, 2 = Z up it).</summary>
        static int[] AxisTargets(ComponentCategory category)
        {
            switch (category)
            {
                // RAM and graphics cards stand on their edge connector: their height sticks out of the board and
                // their thickness runs along it.
                case ComponentCategory.RAM:
                case ComponentCategory.GraphicsCard:
                    return new[] { 0, 1, 2 };
                // A tower cooler: tallest out of the board, fan facing across it.
                case ComponentCategory.Cooling:
                    return new[] { 1, 2, 0 };
                // Everything else lies flat: longest across, shortest is its thickness.
                default:
                    return new[] { 0, 2, 1 };
            }
        }

        static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

        // ------------------------------------------------------------------ matching

        static ComponentDefinitionSO Match(string path, List<ComponentDefinitionSO> components)
        {
            string file = MlLabelMap.Resolve(Path.GetFileNameWithoutExtension(path));

            foreach (var c in components)
            {
                if (MlLabelMap.Normalise(c.id) == file) return c;
                if (MlLabelMap.Normalise(c.mlLabel) == file) return c;
                if (MlLabelMap.Normalise(c.displayName) == file) return c;
                if (c.mlAliases != null && c.mlAliases.Any(a => MlLabelMap.Resolve(a) == file)) return c;
            }

            // Fall back to the folder it lives in: GraphicsCard/anything.fbx is a graphics card.
            string folder = MlLabelMap.Normalise(Path.GetFileName(Path.GetDirectoryName(path))).Replace("_", "");
            if (FolderCategory.TryGetValue(folder, out var category))
                return components.FirstOrDefault(c => c.category == category && !string.IsNullOrEmpty(c.mlLabel))
                    ?? components.FirstOrDefault(c => c.category == category);

            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
