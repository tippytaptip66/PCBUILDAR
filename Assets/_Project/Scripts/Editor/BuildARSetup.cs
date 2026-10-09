using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEditor.XR.ARSubsystems;
using BuildAR.AR;
using BuildAR.AR.Detection;
using BuildAR.Assembly;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.Viewer;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// BuildAR > Setup menu. Creates folders, materials, placeholder 3D models, sample content and the four
    /// scenes (Boot, App, ARScanner, VirtualAssembly). Existing assets are never overwritten — delete one to regenerate it.
    /// </summary>
    public static partial class BuildARSetup
    {
        const string Root = "Assets/_Project";
        const string ArtMaterials = Root + "/Art/Materials";
        const string PlaceholderPrefabs = Root + "/Prefabs/Placeholders";
        const string AssemblyPrefabs = Root + "/Prefabs/Assembly";
        const string ComponentsRes = Root + "/Data/Resources/Components";
        const string LessonsRes = Root + "/Data/Resources/Lessons";
        const string GuidesRes = Root + "/Data/Resources/AssemblyGuides";
        const string QuizFolder = Root + "/Data/QuizQuestions";
        const string ScenesFolder = Root + "/Scenes";
        const string MlModels = Root + "/ML/Models";
        const string ReferenceImages = Root + "/ML/ReferenceImages";
        const string ReferenceLibraryPath = Root + "/ML/ComponentImageLibrary.asset";
        const string UiFolder = "Assets/UI";

        public const string BootScene = "BuildAR_Boot";
        public const string AppScene = "BuildAR_App";
        public const string ArScene = "BuildAR_ARScanner";
        public const string AssemblyScene = "BuildAR_VirtualAssembly";

        // ================================================================== menu

        [MenuItem("BuildAR/Setup/Run Full Setup", priority = 0)]
        public static void RunAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateFolders();
            AddArBackgroundRendererFeature();
            GeneratePlaceholderModels();
            GenerateSampleContent();
            BuildScenes();
            EditorUtility.DisplayDialog("BuildAR", "Setup complete.\n\nOpen Assets/_Project/Scenes/BuildAR_Boot and press Play.", "OK");
        }

        [MenuItem("BuildAR/Setup/1. Create Folders", priority = 20)]
        public static void CreateFolders()
        {
            string[] folders =
            {
                Root + "/Art/Fonts", ArtMaterials, Root + "/Art/Textures",
                Root + "/Art/Models/Components/CPU", Root + "/Art/Models/Components/Motherboard", Root + "/Art/Models/Components/RAM",
                Root + "/Art/Models/Components/Storage", Root + "/Art/Models/Components/GraphicsCard", Root + "/Art/Models/Components/PowerSupply",
                Root + "/Art/Models/Components/Cooling", Root + "/Art/Models/Components/Case", Root + "/Art/Models/Components/Cables",
                Root + "/Art/Models/Components/InputOutput", Root + "/Art/Models/Assembly",
                Root + "/Prefabs/Components", PlaceholderPrefabs, AssemblyPrefabs, Root + "/Prefabs/AR",
                ComponentsRes, LessonsRes, GuidesRes, QuizFolder, ScenesFolder, MlModels, Root + "/ML/Training",
                ReferenceImages, Root + "/UI",
            };
            foreach (var f in folders) EnsureFolder(f);

            WriteTextIfMissing(MlModels + "/labels.txt", string.Join("\n", new[] { "cpu", "motherboard", "ram", "ssd", "gpu", "psu", "cooler", "case", "cable", "io_panel" }) + "\n");

            // Classes of the Roboflow Universe "PC Parts Detection" dataset (CC BY 4.0), in its data.yaml order.
            // Assign this as the detector's Labels File when you train on that dataset — MlLabelMap maps the
            // names onto this app's components. Always check the order against the downloaded data.yaml.
            WriteTextIfMissing(MlModels + "/labels_pcparts_roboflow.txt", string.Join("\n", new[]
            {
                "cpu", "cpu_cooler", "disk_drive", "front_panel", "gpu", "gpu_slot",
                "motherboard", "optical_drive", "psu", "ram_slot", "ram_stick", "rear_io",
            }) + "\n");
            AssetDatabase.Refresh();
        }

        [MenuItem("BuildAR/Setup/2. Generate Placeholder 3D Models", priority = 21)]
        public static void GeneratePlaceholderModels()
        {
            CreateFolders();
            BuildPlaceholders();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BuildAR/Setup/3. Generate Sample Content", priority = 22)]
        public static void GenerateSampleContent()
        {
            CreateFolders();
            var quiz = CreateQuizQuestions(overwrite: false);
            CreateComponents(quiz);
            CreateLessons();
            CreateAssemblyGuide();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BuildAR/Setup/4. Build Scenes + Build Settings", priority = 23)]
        public static void BuildScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateFolders();
            var panel = CreatePanelSettings();
            var boot = BuildBootScene();
            var app = BuildAppScene(panel);
            var ar = BuildArScene(panel);
            var asm = BuildAssemblyScene(panel);

            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(boot, true), new EditorBuildSettingsScene(app, true),
                new EditorBuildSettingsScene(ar, true), new EditorBuildSettingsScene(asm, true),
            };
            foreach (var s in EditorBuildSettings.scenes)
                if (list.All(x => x.path != s.path)) list.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = list.ToArray();

            EditorSceneManager.OpenScene(boot);
            Debug.Log("BuildAR: scenes built and added to Build Settings (Boot first).");
        }

        /// <summary>URP only shows the AR camera feed if each renderer has the AR Background Renderer Feature.</summary>
        [MenuItem("BuildAR/Setup/Add AR Background Renderer Feature (URP)", priority = 41)]
        public static void AddArBackgroundRendererFeature()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!path.StartsWith("Assets/")) continue; // package assets are read-only
                if (data == null || data.rendererFeatures.Any(x => x is ARBackgroundRendererFeature)) continue;

                var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                feature.name = "ARBackgroundRendererFeature";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

                var so = new SerializedObject(data);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log($"BuildAR: added AR Background Renderer Feature to {path}");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BuildAR/Setup/Apply Android AR Player Settings", priority = 40)]
        public static void ApplyAndroidSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrEmpty(id) || id.Contains("unity.template"))
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BrandingSetup.AndroidPackage);
            Debug.Log("BuildAR: Android set to Portrait, IL2CPP, ARM64. Also check Project Settings > XR Plug-in Management > Android > ARCore is ticked.");
        }

        // ================================================================== debug

        [MenuItem("BuildAR/Debug/Reset Saved Progress", priority = 100)]
        public static void ResetProgress()
        {
            var path = Path.Combine(Application.persistentDataPath, "buildar_save.json");
            if (File.Exists(path)) File.Delete(path);
            Debug.Log("BuildAR: saved progress cleared — the Welcome screen will show on next Play.");
        }

        [MenuItem("BuildAR/Debug/Open App Scene (UI test)", priority = 101)]
        public static void OpenAppScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene($"{ScenesFolder}/{AppScene}.unity");
        }

        // ================================================================== materials

        static Material Lit(string name, Color color, float metallic = 0f, float smoothness = 0.35f)
        {
            string path = $"{ArtMaterials}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (color.a < 1f) MakeTransparent(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material UnlitTransparent(string name, Color color)
        {
            string path = $"{ArtMaterials}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            MakeTransparent(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>A length of cable between two points. Cylinders are 2 units tall, hence the half length.</summary>
        static void CableRun(Transform parent, Vector3 from, Vector3 to, float radius, Material mat)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < 0.001f) return;

            var rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
            Model.Mesh(parent, PrimitiveType.Cylinder, from + direction * 0.5f,
                       new Vector3(radius * 2f, length * 0.5f, radius * 2f), mat, rotation.eulerAngles);
        }

        /// <summary>A material that glows: LED strips and fan rings, which is what makes a build look alive.</summary>
        static Material Led(string name, Color color, float strength)
        {
            string path = $"{ArtMaterials}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material CableMaterial()
        {
            string path = $"{ArtMaterials}/M_Cable.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material PcbGreen => Lit("M_PCB_Green", Hex("1F5E3A"), 0f, 0.5f);
        static Material PcbBlack => Lit("M_PCB_Black", Hex("1B1F27"), 0f, 0.45f);

        /// <summary>
        /// The assembly case's motherboard. It gets its own board-sized texture (T_PCB_Board: hairline traces and
        /// pin-prick vias, mapped once) — the small-part T_PCB_Black stretched over 30 cm reads as polka dots.
        /// </summary>
        static Material BoardPcb
        {
            get
            {
                var m = Lit("M_PCB_Board", Hex("1B1F27"), 0f, 0.45f);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Art/Textures/T_PCB_Board.png");
                if (texture != null)
                {
                    m.SetTexture("_BaseMap", texture);
                    m.SetColor("_BaseColor", Color.white);   // the texture carries the colour
                }
                m.SetTextureScale("_BaseMap", Vector2.one);
                EditorUtility.SetDirty(m);
                return m;
            }
        }
        static Material Metal => Lit("M_Metal", Hex("B9C0CC"), 0.85f, 0.6f);
        static Material Gold => Lit("M_Gold", Hex("D4A63A"), 1f, 0.7f);
        static Material Plastic => Lit("M_DarkPlastic", Hex("22262E"), 0f, 0.3f);
        static Material Accent => Lit("M_Accent_Blue", Hex("2F6BFF"), 0.2f, 0.55f);
        static Material Chip => Lit("M_Chip", Hex("0E1116"), 0f, 0.55f);
        /// <summary>Matte black painted steel — the default look of a PC case.</summary>
        static Material CaseMat => Lit("M_Case", Hex("121417"), 0.3f, 0.42f);
        static Material Glass => Lit("M_Glass", new Color(0.55f, 0.75f, 0.95f, 0.18f), 0f, 0.9f);
        static Material HighlightMat => UnlitTransparent("M_Highlight", new Color(0.13f, 0.83f, 0.93f, 0.5f));

        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        // ================================================================== placeholder models

        /// <summary>Tiny builder for primitive-based prefabs. Parts are unscaled groups so hotspots aren't skewed.</summary>
        class Model
        {
            public readonly GameObject Root;
            public Model(string name) { Root = new GameObject(name); }

            public Transform Part(string name, Vector3 pos, Vector3? explodeDir = null, float mult = 1f)
            {
                var g = new GameObject(name).transform;
                g.SetParent(Root.transform, false);
                g.localPosition = pos;
                if (explodeDir.HasValue || mult != 1f)
                {
                    var e = g.gameObject.AddComponent<ExplodePart>();
                    e.direction = explodeDir ?? Vector3.zero;
                    e.distanceMultiplier = mult;
                }
                return g;
            }

            public static GameObject Mesh(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
            {
                var go = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = type.ToString();
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localEulerAngles = euler;
                go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = mat;
                return go;
            }

            public static void Hotspot(Transform parent, string title, string description, ModelHotspot.Kind kind, Vector3 pos, Vector3 normal)
            {
                var h = new GameObject("Hotspot_" + title.Replace(" ", "")).transform;
                h.SetParent(parent, false);
                h.localPosition = pos;
                if (normal != Vector3.zero) h.rotation = Quaternion.LookRotation(parent.TransformDirection(normal));
                var c = h.gameObject.AddComponent<ModelHotspot>();
                c.title = title;
                c.description = description;
                c.kind = kind;
                c.dimWhenFacingAway = normal != Vector3.zero;
            }

            public GameObject Save(string path, float explodeDistance)
            {
                var ex = Root.AddComponent<ExplodableModel>();
                ex.distance = explodeDistance;
                var prefab = PrefabUtility.SaveAsPrefabAsset(Root, path);
                Object.DestroyImmediate(Root);
                return prefab;
            }
        }

        static void BuildPlaceholders(bool overwrite = false)
        {
            TryBuild("PH_CPU", BuildCpu, overwrite);
            TryBuild("PH_RAM", BuildRam, overwrite);
            TryBuild("PH_Motherboard", BuildMotherboard, overwrite);
            TryBuild("PH_SSD_M2", BuildSsd, overwrite);
            TryBuild("PH_SSD_SATA", BuildSataSsd, overwrite);
            TryBuild("PH_GPU", BuildGpu, overwrite);
            TryBuild("PH_PSU", BuildPsu, overwrite);
            TryBuild("PH_Cooler", BuildCooler, overwrite);
            TryBuild("PH_Case", BuildCase, overwrite);
            TryBuild("PH_Cable24", BuildCable, overwrite);
            TryBuild("PH_IOPanel", BuildIoPanel, overwrite);
        }

        /// <summary>
        /// Rebuilds every stand-in model from the current code, writing over the existing prefabs. Saving to the
        /// same path keeps each prefab's id, so components and scenes stay pointed at them.
        /// </summary>
        [MenuItem("BuildAR/Setup/Rebuild Placeholder Models", priority = 32)]
        public static void RebuildPlaceholders()
        {
            CreateFolders();
            BuildPlaceholders(overwrite: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("BuildAR: stand-in models rebuilt from code. Existing links to them are unchanged.");
        }

        static void TryBuild(string name, System.Func<string, GameObject> build, bool overwrite = false)
        {
            string path = $"{PlaceholderPrefabs}/{name}.prefab";
            if (overwrite || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) build(path);
        }

        static GameObject Placeholder(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PlaceholderPrefabs}/{name}.prefab");

        static GameObject BuildCpu(string path)
        {
            var m = new Model("PH_CPU");
            var sub = m.Part("Substrate", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(sub, PrimitiveType.Cube, Vector3.zero, new Vector3(0.04f, 0.0015f, 0.04f), PcbGreen);
            Model.Mesh(sub, PrimitiveType.Cube, new Vector3(-0.017f, 0.0009f, -0.017f), new Vector3(0.003f, 0.0002f, 0.003f), Gold);
            Model.Hotspot(sub, "Pin 1 triangle", "Gold triangle marks pin 1. Match it to the triangle on the socket.", ModelHotspot.Kind.Notch, new Vector3(-0.017f, 0.001f, -0.017f), Vector3.up);
            Model.Hotspot(sub, "Alignment notches", "Side notches line up with keys in the socket so the CPU only fits one way.", ModelHotspot.Kind.Notch, new Vector3(0.02f, 0f, 0.008f), Vector3.right);

            var ihs = m.Part("HeatSpreader", new Vector3(0, 0.0022f, 0), Vector3.up);
            Model.Mesh(ihs, PrimitiveType.Cube, Vector3.zero, new Vector3(0.032f, 0.003f, 0.032f), Metal);
            Model.Hotspot(ihs, "Heat spreader", "Metal lid (IHS) that carries heat from the chip to the cooler.", ModelHotspot.Kind.HeatSpreader, new Vector3(0, 0.0016f, 0), Vector3.up);

            var pins = m.Part("Pins", new Vector3(0, -0.0016f, 0), Vector3.down);
            Model.Mesh(pins, PrimitiveType.Cube, Vector3.zero, new Vector3(0.036f, 0.0012f, 0.036f), Gold);
            Model.Hotspot(pins, "Contact pads", "Hundreds of gold contacts connect the CPU to the motherboard. Never touch them.", ModelHotspot.Kind.Pins, new Vector3(0, -0.0007f, 0), Vector3.down);
            return m.Save(path, 0.02f);
        }

        static GameObject BuildRam(string path)
        {
            var m = new Model("PH_RAM");
            var pcb = m.Part("PCB", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(pcb, PrimitiveType.Cube, Vector3.zero, new Vector3(0.133f, 0.031f, 0.0012f), PcbGreen);
            Model.Hotspot(pcb, "Key notch", "Off-center notch matches the slot key. DDR4 and DDR5 notches are in different places.", ModelHotspot.Kind.Notch, new Vector3(0.012f, -0.0155f, 0), Vector3.zero);
            Model.Hotspot(pcb, "Locking notches", "The slot's clips snap into these side cut-outs to hold the stick.", ModelHotspot.Kind.LockingMechanism, new Vector3(0.0665f, 0.003f, 0), Vector3.zero);

            var contacts = m.Part("GoldContacts", new Vector3(0, -0.0135f, 0), Vector3.down);
            Model.Mesh(contacts, PrimitiveType.Cube, Vector3.zero, new Vector3(0.125f, 0.004f, 0.0014f), Gold);
            Model.Hotspot(contacts, "Gold contacts", "Edge connector that carries data and power. Hold the stick by its edges.", ModelHotspot.Kind.Pins, new Vector3(-0.03f, 0, 0), Vector3.zero);

            var front = m.Part("HeatSpreaderFront", new Vector3(0, 0.002f, 0.0017f), Vector3.forward);
            Model.Mesh(front, PrimitiveType.Cube, Vector3.zero, new Vector3(0.12f, 0.024f, 0.002f), Accent);
            Model.Hotspot(front, "Heat spreader", "Aluminum cover that spreads heat from the memory chips.", ModelHotspot.Kind.HeatSpreader, new Vector3(0.03f, 0.004f, 0.001f), Vector3.forward);

            var back = m.Part("HeatSpreaderBack", new Vector3(0, 0.002f, -0.0017f), Vector3.back);
            Model.Mesh(back, PrimitiveType.Cube, Vector3.zero, new Vector3(0.12f, 0.024f, 0.002f), Accent);
            return m.Save(path, 0.015f);
        }

        static GameObject BuildMotherboard(string path)
        {
            var m = new Model("PH_Motherboard");
            var pcb = m.Part("PCB", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(pcb, PrimitiveType.Cube, Vector3.zero, new Vector3(0.244f, 0.0016f, 0.305f), PcbBlack);

            var socket = m.Part("CPUSocket", new Vector3(0.02f, 0.003f, 0.07f), Vector3.up);
            Model.Mesh(socket, PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 0.004f, 0.05f), Metal);
            Model.Hotspot(socket, "CPU socket", "AM5 socket. The CPU drops in with zero force when the lever is up.", ModelHotspot.Kind.Connector, new Vector3(0, 0.0021f, 0), Vector3.up);
            Model.Hotspot(socket, "Socket lever", "Lift to open the retention frame; lower to lock the CPU.", ModelHotspot.Kind.LockingMechanism, new Vector3(0.028f, 0.002f, -0.01f), Vector3.up);

            var ram = m.Part("RAMSlots", new Vector3(0.085f, 0.004f, 0.06f), Vector3.up);
            for (int i = 0; i < 4; i++)
                Model.Mesh(ram, PrimitiveType.Cube, new Vector3(-0.0135f + i * 0.009f, 0, 0), new Vector3(0.006f, 0.006f, 0.133f), Plastic);
            Model.Hotspot(ram, "DIMM slots", "Four RAM slots: A1, A2, B1, B2. With two sticks use A2 and B2.", ModelHotspot.Kind.Connector, new Vector3(0, 0.003f, 0), Vector3.up);
            Model.Hotspot(ram, "Retention clips", "Clips at the slot ends click shut when RAM is fully seated.", ModelHotspot.Kind.LockingMechanism, new Vector3(0, 0.003f, 0.068f), Vector3.up);

            var pcie = m.Part("PCIeSlot", new Vector3(-0.01f, 0.004f, -0.06f), Vector3.up);
            Model.Mesh(pcie, PrimitiveType.Cube, Vector3.zero, new Vector3(0.089f, 0.006f, 0.007f), Plastic);
            Model.Hotspot(pcie, "PCIe x16 slot", "Long slot for the graphics card. A latch at the end locks the card.", ModelHotspot.Kind.Connector, new Vector3(0, 0.003f, 0), Vector3.up);

            var m2 = m.Part("M2Slot", new Vector3(0f, 0.002f, -0.02f), Vector3.up);
            Model.Mesh(m2, PrimitiveType.Cube, Vector3.zero, new Vector3(0.022f, 0.003f, 0.008f), Plastic);
            Model.Hotspot(m2, "M.2 slot", "Slot for NVMe SSDs; a standoff screw holds the far end.", ModelHotspot.Kind.Connector, new Vector3(0, 0.0015f, 0), Vector3.up);

            var atx = m.Part("ATX24", new Vector3(0.115f, 0.006f, 0.02f), Vector3.up);
            Model.Mesh(atx, PrimitiveType.Cube, Vector3.zero, new Vector3(0.006f, 0.01f, 0.052f), Plastic);
            Model.Hotspot(atx, "24-pin ATX power", "Main power input from the PSU.", ModelHotspot.Kind.Connector, new Vector3(0, 0.005f, 0), Vector3.up);

            var eps = m.Part("EPS8", new Vector3(-0.03f, 0.006f, 0.145f), Vector3.up);
            Model.Mesh(eps, PrimitiveType.Cube, Vector3.zero, new Vector3(0.02f, 0.01f, 0.006f), Plastic);
            Model.Hotspot(eps, "CPU power (EPS 8-pin)", "Dedicated CPU power from the PSU.", ModelHotspot.Kind.Connector, new Vector3(0, 0.005f, 0), Vector3.up);

            var vrm = m.Part("VRMHeatsink", new Vector3(-0.06f, 0.008f, 0.08f), Vector3.up);
            Model.Mesh(vrm, PrimitiveType.Cube, Vector3.zero, new Vector3(0.02f, 0.014f, 0.1f), Metal);

            var io = m.Part("RearIO", new Vector3(-0.115f, 0.02f, 0.09f), Vector3.left);
            Model.Mesh(io, PrimitiveType.Cube, Vector3.zero, new Vector3(0.012f, 0.04f, 0.16f), Metal);
            Model.Hotspot(io, "Rear I/O ports", "USB, network, audio and video ports that show through the back of the case.", ModelHotspot.Kind.Port, new Vector3(-0.006f, 0, 0), Vector3.left);

            var fp = m.Part("FrontPanelHeader", new Vector3(0.1f, 0.004f, -0.14f), Vector3.up);
            Model.Mesh(fp, PrimitiveType.Cube, Vector3.zero, new Vector3(0.02f, 0.006f, 0.006f), Plastic);
            Model.Hotspot(fp, "Front-panel header", "Pins for the case power button, reset button and LEDs.", ModelHotspot.Kind.Pins, new Vector3(0, 0.003f, 0), Vector3.up);
            return m.Save(path, 0.03f);
        }

        static GameObject BuildSsd(string path)
        {
            var m = new Model("PH_SSD_M2");
            var pcb = m.Part("PCB", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(pcb, PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 0.0008f, 0.022f), PcbBlack);
            Model.Hotspot(pcb, "Screw notch", "Half-moon cut-out held down by the M.2 standoff screw.", ModelHotspot.Kind.LockingMechanism, new Vector3(0.04f, 0.0005f, 0), Vector3.up);

            var contacts = m.Part("EdgeConnector", new Vector3(-0.038f, 0, 0), Vector3.left);
            Model.Mesh(contacts, PrimitiveType.Cube, Vector3.zero, new Vector3(0.004f, 0.0009f, 0.02f), Gold);
            Model.Hotspot(contacts, "M-key connector", "Gold edge with a notch (the M key) that matches NVMe M.2 slots.", ModelHotspot.Kind.Notch, new Vector3(-0.002f, 0, 0), Vector3.zero);

            var ctrl = m.Part("Controller", new Vector3(-0.012f, 0.001f, 0), Vector3.up);
            Model.Mesh(ctrl, PrimitiveType.Cube, Vector3.zero, new Vector3(0.012f, 0.0012f, 0.012f), Chip);
            Model.Hotspot(ctrl, "Controller", "The SSD's processor; it manages where data is stored.", ModelHotspot.Kind.Feature, new Vector3(0, 0.0007f, 0), Vector3.up);

            var nand = m.Part("NAND", new Vector3(0.018f, 0.001f, 0), Vector3.up, 1.4f);
            Model.Mesh(nand, PrimitiveType.Cube, Vector3.zero, new Vector3(0.03f, 0.0012f, 0.016f), Chip);
            Model.Hotspot(nand, "NAND flash", "Memory chips that keep your files even without power.", ModelHotspot.Kind.Feature, new Vector3(0, 0.0007f, 0), Vector3.up);
            return m.Save(path, 0.012f);
        }

        static GameObject BuildGpu(string path)
        {
            // Local axes: X length, Y card height (connector at -Y), Z thickness (fans at -Z).
            var m = new Model("PH_GPU");
            var pcb = m.Part("PCB", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(pcb, PrimitiveType.Cube, Vector3.zero, new Vector3(0.27f, 0.11f, 0.002f), PcbBlack);
            Model.Mesh(pcb, PrimitiveType.Cube, new Vector3(0.1f, 0.056f, 0.004f), new Vector3(0.02f, 0.006f, 0.008f), Plastic);
            Model.Hotspot(pcb, "8-pin PCIe power", "Extra power from the PSU. Many cards need one or more of these.", ModelHotspot.Kind.Connector, new Vector3(0.1f, 0.06f, 0.004f), Vector3.up);

            var edge = m.Part("PCIeConnector", new Vector3(-0.03f, -0.058f, 0), Vector3.down);
            Model.Mesh(edge, PrimitiveType.Cube, Vector3.zero, new Vector3(0.089f, 0.008f, 0.0021f), Gold);
            Model.Hotspot(edge, "PCIe x16 connector", "Gold fingers that plug into the motherboard's PCIe x16 slot.", ModelHotspot.Kind.Pins, new Vector3(0, -0.004f, 0), Vector3.down);

            // Fin stack under the shroud, seen through the gaps between the fans.
            var heatsink = m.Part("Heatsink", new Vector3(0, 0, -0.009f), Vector3.back, 0f);
            for (int i = 0; i < 26; i++)
                Model.Mesh(heatsink, PrimitiveType.Cube, new Vector3(-0.125f + i * 0.01f, 0, 0), new Vector3(0.0016f, 0.1f, 0.013f), Metal);

            // Shroud as a frame rather than a slab, so the fans are visible inside it.
            var shroud = m.Part("Shroud", new Vector3(0, 0, -0.016f), Vector3.back);
            Model.Mesh(shroud, PrimitiveType.Cube, new Vector3(0, 0.056f, 0), new Vector3(0.28f, 0.008f, 0.022f), Plastic);
            Model.Mesh(shroud, PrimitiveType.Cube, new Vector3(0, -0.056f, 0), new Vector3(0.28f, 0.008f, 0.022f), Plastic);
            Model.Mesh(shroud, PrimitiveType.Cube, new Vector3(-0.138f, 0, 0), new Vector3(0.006f, 0.12f, 0.022f), Plastic);
            Model.Mesh(shroud, PrimitiveType.Cube, new Vector3(0.138f, 0, 0), new Vector3(0.006f, 0.12f, 0.022f), Plastic);
            Model.Mesh(shroud, PrimitiveType.Cube, Vector3.zero, new Vector3(0.008f, 0.12f, 0.022f), Plastic);
            Model.Mesh(shroud, PrimitiveType.Cube, new Vector3(0.06f, 0.056f, -0.004f), new Vector3(0.07f, 0.003f, 0.006f), Led("M_LED_Card", Hex("22D3EE"), 1.3f));

            // Two real fans, which turn like every other fan in the app.
            var fans = m.Part("Fans", new Vector3(0, 0, -0.018f), Vector3.back, 1.8f);
            foreach (var x in new[] { -0.07f, 0.07f })
            {
                var unit = new GameObject("Fan").transform;
                unit.SetParent(fans, false);
                unit.localPosition = new Vector3(x, 0, 0);
                FanAssembly(unit, 0.088f, 9, Plastic, Plastic, Accent);
            }
            Model.Hotspot(fans, "Cooling fans", "Pull air through the heatsink to cool the GPU chip.", ModelHotspot.Kind.Feature, new Vector3(0.07f, 0, -0.002f), Vector3.back);

            var backplate = m.Part("Backplate", new Vector3(0, 0, 0.004f), Vector3.forward);
            Model.Mesh(backplate, PrimitiveType.Cube, Vector3.zero, new Vector3(0.27f, 0.11f, 0.002f), Metal);

            var bracket = m.Part("Bracket", new Vector3(-0.141f, 0, -0.006f), Vector3.left);
            Model.Mesh(bracket, PrimitiveType.Cube, Vector3.zero, new Vector3(0.002f, 0.12f, 0.02f), Metal);
            Model.Hotspot(bracket, "Display outputs", "HDMI and DisplayPort — plug your monitor in here, not into the motherboard.", ModelHotspot.Kind.Port, new Vector3(-0.001f, 0.02f, 0), Vector3.left);
            return m.Save(path, 0.04f);
        }

        /// <summary>
        /// A fan you can actually recognise: square frame with corner bosses, a hub, angled blades and a cable
        /// tail. Built in the parent's XY plane blowing towards -Z; rotate the parent to point it elsewhere.
        /// </summary>
        static void FanAssembly(Transform parent, float size, int blades, Material frame, Material blade, Material hub)
        {
            float bar = size * 0.09f, deep = size * 0.2f, inner = size - bar * 2f;

            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(0, (size - bar) * 0.5f, 0), new Vector3(size, bar, deep), frame);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(0, -(size - bar) * 0.5f, 0), new Vector3(size, bar, deep), frame);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(-(size - bar) * 0.5f, 0, 0), new Vector3(bar, inner, deep), frame);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3((size - bar) * 0.5f, 0, 0), new Vector3(bar, inner, deep), frame);

            // Screw bosses at the corners.
            float boss = size * 0.14f, offset = (size - boss) * 0.5f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                    Model.Mesh(parent, PrimitiveType.Cube, new Vector3(sx * offset, sy * offset, 0), new Vector3(boss, boss, deep * 1.04f), frame);

            // Hub and blades live on their own child so they can spin while the frame stays put.
            var spinner = new GameObject("Blades").transform;
            spinner.SetParent(parent, false);
            var spin = spinner.gameObject.AddComponent<SpinningPart>();
            spin.rpm = 420f;
            spin.axis = Vector3.forward;

            Model.Mesh(spinner, PrimitiveType.Cylinder, Vector3.zero, new Vector3(size * 0.26f, deep * 0.3f, size * 0.26f), hub, new Vector3(90, 0, 0));
            float radius = size * 0.21f, length = size * 0.32f;
            for (int i = 0; i < blades; i++)
            {
                float angle = i * 360f / blades;
                float rad = angle * Mathf.Deg2Rad;
                Model.Mesh(spinner, PrimitiveType.Cube,
                           new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0),
                           new Vector3(length, size * 0.012f, size * 0.16f), blade,
                           new Vector3(24f, 0, angle));
            }

            // A lit ring behind the blades, the way most case fans look.
            Model.Mesh(parent, PrimitiveType.Cylinder, new Vector3(0, 0, deep * 0.3f), new Vector3(size * 0.62f, deep * 0.08f, size * 0.62f),
                       Led("M_LED_Cyan", Hex("22D3EE"), 1.6f), new Vector3(90, 0, 0));

            // Cable tail out of one corner.
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(offset, -offset - size * 0.07f, 0), new Vector3(size * 0.05f, size * 0.12f, deep * 0.35f), hub);
        }

        static GameObject BuildPsu(string path)
        {
            var m = new Model("PH_PSU");
            var housing = m.Part("Housing", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(housing, PrimitiveType.Cube, Vector3.zero, new Vector3(0.15f, 0.086f, 0.14f), Plastic);

            var fan = m.Part("Fan", new Vector3(0, -0.044f, 0), Vector3.down);
            var psuBlades = new GameObject("Blades").transform;
            psuBlades.SetParent(fan, false);
            psuBlades.localRotation = Quaternion.Euler(-90, 0, 0);   // lay the fan flat, blowing downwards
            FanAssembly(psuBlades, 0.12f, 9, Plastic, Plastic, Accent);
            Model.Hotspot(fan, "Intake fan", "Install the PSU fan-down when the case has a bottom vent.", ModelHotspot.Kind.Feature, new Vector3(0, -0.002f, 0), Vector3.down);

            var cables = m.Part("Cables", new Vector3(0, 0, 0.075f), Vector3.forward);
            Model.Mesh(cables, PrimitiveType.Cube, Vector3.zero, new Vector3(0.06f, 0.03f, 0.01f), Chip);
            Model.Hotspot(cables, "24-pin ATX cable", "Main motherboard power.", ModelHotspot.Kind.Connector, new Vector3(-0.02f, 0.008f, 0.006f), Vector3.forward);
            Model.Hotspot(cables, "EPS 8-pin cable", "CPU power — labeled CPU, don't confuse it with PCIe 8-pin.", ModelHotspot.Kind.Connector, new Vector3(0.02f, 0.008f, 0.006f), Vector3.forward);

            var sw = m.Part("Switch", new Vector3(-0.05f, 0.02f, -0.071f), Vector3.back);
            Model.Mesh(sw, PrimitiveType.Cube, Vector3.zero, new Vector3(0.012f, 0.018f, 0.004f), Accent);
            Model.Hotspot(sw, "Power switch", "I = on, O = off. Keep it at O while you build.", ModelHotspot.Kind.Feature, new Vector3(0, 0, -0.002f), Vector3.back);
            Model.Hotspot(sw, "AC inlet", "The wall power cable plugs in here. Unplug it before working inside.", ModelHotspot.Kind.Port, new Vector3(0.04f, -0.02f, -0.002f), Vector3.back);
            return m.Save(path, 0.03f);
        }

        static GameObject BuildCooler(string path)
        {
            // A downdraft cooler: base plate on the CPU, fins above it, fan on top blowing down onto the board.
            // The slot mounts this with local Y pointing out of the motherboard, so local -Y faces the CPU and
            // the fan, sitting at +Y, is turned to face back down towards it.
            // The slot sits 83 mm out from the board, so the contact plate has to be that far down the model or
            // the cooler hangs in mid-air instead of gripping the CPU.
            var m = new Model("PH_Cooler");
            var basePlate = m.Part("BasePlate", new Vector3(0, -0.076f, 0), Vector3.down);
            Model.Mesh(basePlate, PrimitiveType.Cube, Vector3.zero, new Vector3(0.04f, 0.008f, 0.04f), Metal);
            Model.Hotspot(basePlate, "Contact plate", "Sits on the CPU with a thin layer of thermal paste in between.", ModelHotspot.Kind.HeatSpreader, new Vector3(0, -0.004f, 0), Vector3.down);

            var bracket = m.Part("MountingBracket", new Vector3(0, -0.069f, 0), Vector3.zero, 0.3f);
            Model.Mesh(bracket, PrimitiveType.Cube, Vector3.zero, new Vector3(0.09f, 0.004f, 0.012f), Metal);
            Model.Hotspot(bracket, "Mounting bracket", "Screws into the socket backplate. Tighten diagonally, a little at a time.", ModelHotspot.Kind.LockingMechanism, new Vector3(0.042f, 0.002f, 0), Vector3.up);

            var pipes = m.Part("HeatPipes", new Vector3(0, -0.058f, 0), Vector3.zero, 0f);
            foreach (var x in new[] { -0.02f, 0.02f })
                foreach (var z in new[] { -0.02f, 0.02f })
                    Model.Mesh(pipes, PrimitiveType.Cylinder, new Vector3(x, 0, z), new Vector3(0.006f, 0.016f, 0.006f), Lit("M_Copper", Hex("B87333"), 1f, 0.6f));
            Model.Hotspot(pipes, "Heat pipes", "Copper pipes move heat from the base up into the fins.", ModelHotspot.Kind.Feature, new Vector3(0.014f, 0.01f, 0.004f), Vector3.back);

            // Separate fins rather than one block — it's the stack of thin plates that makes a tower cooler read.
            // Upright fins between the base and the fan, with the gaps running the way the air is pushed.
            var fins = m.Part("FinStack", new Vector3(0, -0.038f, 0), Vector3.up);
            for (int i = 0; i < 16; i++)
                Model.Mesh(fins, PrimitiveType.Cube, new Vector3(-0.048f + i * 0.0064f, 0, 0), new Vector3(0.0018f, 0.05f, 0.1f), Metal);

            // Fan sits on top of the fins facing the board, so it blows down onto the CPU.
            var fan = m.Part("Fan", new Vector3(0, 0.002f, 0), Vector3.up, 1.2f);
            var body = new GameObject("FanBody").transform;
            body.SetParent(fan, false);
            body.localRotation = Quaternion.Euler(-90, 0, 0);
            FanAssembly(body, 0.11f, 9, Plastic, Plastic, Accent);
            Model.Hotspot(fan, "Fan + 4-pin header", "Plug the fan cable into CPU_FAN on the motherboard.", ModelHotspot.Kind.Connector, new Vector3(0, 0.012f, 0), Vector3.up);
            return m.Save(path, 0.04f);
        }

        static GameObject BuildCase(string path)
        {
            var m = new Model("PH_Case");
            var chassis = m.Part("Chassis", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(chassis, PrimitiveType.Cube, new Vector3(0, 0, 0.103f), new Vector3(0.45f, 0.46f, 0.004f), CaseMat);
            Model.Mesh(chassis, PrimitiveType.Cube, new Vector3(0, 0.228f, 0), new Vector3(0.45f, 0.004f, 0.21f), CaseMat);
            Model.Mesh(chassis, PrimitiveType.Cube, new Vector3(0, -0.228f, 0), new Vector3(0.45f, 0.004f, 0.21f), CaseMat);
            Model.Mesh(chassis, PrimitiveType.Cube, new Vector3(-0.223f, 0, 0), new Vector3(0.004f, 0.46f, 0.21f), CaseMat);
            Model.Hotspot(chassis, "Motherboard tray", "Standoffs here hold the motherboard off the metal.", ModelHotspot.Kind.Feature, new Vector3(-0.05f, 0.05f, 0.1f), Vector3.back);
            Model.Hotspot(chassis, "Expansion slot covers", "Remove covers where the graphics card's bracket will go.", ModelHotspot.Kind.Port, new Vector3(-0.222f, -0.05f, 0), Vector3.left);

            var front = m.Part("FrontPanel", new Vector3(0.223f, 0, 0), Vector3.right);
            Model.Mesh(front, PrimitiveType.Cube, Vector3.zero, new Vector3(0.004f, 0.46f, 0.21f), Plastic);
            Model.Hotspot(front, "Front I/O", "Power button, USB and audio. Their cables go to front-panel headers on the board.", ModelHotspot.Kind.Port, new Vector3(0.003f, 0.2f, 0), Vector3.right);

            var shroud = m.Part("PSUShroud", new Vector3(0.05f, -0.125f, 0), Vector3.zero, 0.5f);
            Model.Mesh(shroud, PrimitiveType.Cube, Vector3.zero, new Vector3(0.34f, 0.004f, 0.21f), CaseMat);
            Model.Hotspot(shroud, "PSU shroud", "Hides the power supply and spare cables.", ModelHotspot.Kind.Feature, new Vector3(0, 0.003f, -0.05f), Vector3.up);

            var side = m.Part("GlassSidePanel", new Vector3(0, 0, -0.106f), Vector3.back, 3f);
            Model.Mesh(side, PrimitiveType.Cube, Vector3.zero, new Vector3(0.45f, 0.46f, 0.003f), Glass);
            return m.Save(path, 0.06f);
        }

        static GameObject BuildCable(string path)
        {
            var m = new Model("PH_Cable24");
            var conn = m.Part("Connector", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(conn, PrimitiveType.Cube, Vector3.zero, new Vector3(0.052f, 0.018f, 0.012f), Plastic);
            Model.Hotspot(conn, "Keyed pins", "Square and rounded pin shapes mean it only fits one way.", ModelHotspot.Kind.Pins, new Vector3(0, -0.009f, 0), Vector3.down);

            var latch = m.Part("Latch", new Vector3(0, 0.002f, 0.009f), Vector3.forward);
            Model.Mesh(latch, PrimitiveType.Cube, Vector3.zero, new Vector3(0.008f, 0.014f, 0.004f), Plastic);
            Model.Hotspot(latch, "Latch clip", "Press to release; it clicks when fully inserted.", ModelHotspot.Kind.LockingMechanism, new Vector3(0, 0.004f, 0.002f), Vector3.forward);

            var wires = m.Part("Wires", new Vector3(0, 0.059f, 0), Vector3.up);
            Model.Mesh(wires, PrimitiveType.Cube, Vector3.zero, new Vector3(0.048f, 0.1f, 0.01f), Chip);
            return m.Save(path, 0.02f);
        }

        static GameObject BuildIoPanel(string path)
        {
            var m = new Model("PH_IOPanel");
            var plate = m.Part("Plate", Vector3.zero, Vector3.zero, 0f);
            Model.Mesh(plate, PrimitiveType.Cube, Vector3.zero, new Vector3(0.16f, 0.045f, 0.002f), Metal);

            var usb = m.Part("USB", new Vector3(-0.05f, 0.008f, -0.003f), Vector3.back);
            Model.Mesh(usb, PrimitiveType.Cube, Vector3.zero, new Vector3(0.013f, 0.005f, 0.004f), Accent);
            Model.Hotspot(usb, "USB-A", "Keyboard, mouse and flash drives.", ModelHotspot.Kind.Port, new Vector3(0, 0, -0.002f), Vector3.back);

            var hdmi = m.Part("HDMI", new Vector3(-0.02f, 0.008f, -0.003f), Vector3.back);
            Model.Mesh(hdmi, PrimitiveType.Cube, Vector3.zero, new Vector3(0.015f, 0.005f, 0.004f), Chip);
            Model.Hotspot(hdmi, "HDMI", "Video from the CPU's integrated graphics.", ModelHotspot.Kind.Port, new Vector3(0, 0, -0.002f), Vector3.back);

            var lan = m.Part("Ethernet", new Vector3(0.015f, 0.006f, -0.004f), Vector3.back);
            Model.Mesh(lan, PrimitiveType.Cube, Vector3.zero, new Vector3(0.016f, 0.014f, 0.006f), Metal);
            Model.Hotspot(lan, "Ethernet (RJ-45)", "Wired network connection.", ModelHotspot.Kind.Port, new Vector3(0, 0, -0.003f), Vector3.back);

            var audio = m.Part("Audio", new Vector3(0.055f, 0, -0.003f), Vector3.back);
            for (int i = 0; i < 3; i++)
                Model.Mesh(audio, PrimitiveType.Cylinder, new Vector3(0, -0.012f + i * 0.012f, 0), new Vector3(0.007f, 0.002f, 0.007f), i == 1 ? Lit("M_AudioGreen", Hex("6CC04A")) : Chip, new Vector3(90, 0, 0));
            Model.Hotspot(audio, "3.5 mm audio jacks", "Green = speakers/headphones, pink = microphone.", ModelHotspot.Kind.Port, new Vector3(0, 0, -0.003f), Vector3.back);
            return m.Save(path, 0.015f);
        }

        // ================================================================== content

        static T CreateOrLoad<T>(string path, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            created = existing == null;
            if (!created) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Quiz card ids per sample component (in study order).</summary>
        static readonly Dictionary<string, string[]> ComponentCards = new Dictionary<string, string[]>
        {
            ["cpu"] = new[] { "cpu_1", "cpu_2", "cpu_3", "cpu_4" },
            ["motherboard"] = new[] { "mobo_1", "mobo_2", "mobo_3", "mobo_4" },
            ["ram_ddr5"] = new[] { "ram_1", "ram_2", "ram_3", "ram_4" },
            ["ssd_m2"] = new[] { "ssd_1", "ssd_2", "ssd_3" },
            ["sata_ssd"] = new[] { "sata_1", "sata_2", "sata_3" },
            ["gpu"] = new[] { "gpu_1", "gpu_2", "gpu_3" },
            ["psu"] = new[] { "psu_1", "psu_2", "psu_3", "psu_4" },
            ["cpu_cooler"] = new[] { "cooler_1", "cooler_2", "cooler_3" },
            ["pc_case"] = new[] { "case_1", "case_2", "case_3" },
            ["cable_24pin"] = new[] { "cable_1", "cable_2" },
            ["io_panel"] = new[] { "io_1", "io_2" },
        };

        [MenuItem("BuildAR/Setup/Regenerate Quiz Cards", priority = 24)]
        public static void RegenerateQuizCards()
        {
            CreateFolders();
            var quiz = CreateQuizQuestions(overwrite: true);
            foreach (var entry in ComponentCards)
            {
                var c = AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>($"{ComponentsRes}/{entry.Key}.asset");
                if (c == null) continue;
                c.quizQuestions = entry.Value.Select(id => quiz[id]).ToList();
                EditorUtility.SetDirty(c);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"BuildAR: regenerated {quiz.Count} quiz cards and linked them to the sample components.");
        }

        static Dictionary<string, QuizQuestionSO> CreateQuizQuestions(bool overwrite)
        {
            var map = new Dictionary<string, QuizQuestionSO>();
            QuizQuestionSO Card(string id, QuizQuestionSO.QuestionType type, string text, string explanation, bool safety = false)
            {
                var q = CreateOrLoad<QuizQuestionSO>($"{QuizFolder}/{id}.asset", out bool created);
                map[id] = q;
                if (!created && !overwrite) return null;
                q.questionType = type; q.questionText = text; q.explanation = explanation; q.isSafetyQuestion = safety;
                q.options = new List<string>(); q.correctOptionIndex = 0; q.statementIsTrue = false;
                q.answer = ""; q.acceptedAnswers = new List<string>(); q.distractors = new List<string>();
                q.pairs = new List<QuizQuestionSO.MatchPair>();
                EditorUtility.SetDirty(q);
                return q;
            }
            void MC(string id, string text, int correct, string explanation, bool safety, params string[] options)
            {
                var q = Card(id, QuizQuestionSO.QuestionType.MultipleChoice, text, explanation, safety);
                if (q != null) { q.options = options.ToList(); q.correctOptionIndex = correct; }
            }
            void TF(string id, string statement, bool isTrue, string explanation, bool safety = false)
            {
                var q = Card(id, QuizQuestionSO.QuestionType.TrueFalse, statement, explanation, safety);
                if (q != null) q.statementIsTrue = isTrue;
            }
            void Type(string id, string text, string answer, string[] accepted, string[] distractors, string explanation)
            {
                var q = Card(id, QuizQuestionSO.QuestionType.Typing, text, explanation);
                if (q != null) { q.answer = answer; q.acceptedAnswers = accepted.ToList(); q.distractors = distractors.ToList(); }
            }
            void Match(string id, string text, string explanation, params string[] leftRight)
            {
                var q = Card(id, QuizQuestionSO.QuestionType.Matching, text, explanation);
                if (q == null) return;
                for (int i = 0; i + 1 < leftRight.Length; i += 2)
                    q.pairs.Add(new QuizQuestionSO.MatchPair { left = leftRight[i], right = leftRight[i + 1] });
            }
            void Order(string id, string text, string explanation, params string[] stepsInOrder)
            {
                var q = Card(id, QuizQuestionSO.QuestionType.Ordering, text, explanation);
                if (q != null) q.options = stepsInOrder.ToList();
            }

            // CPU
            MC("cpu_1", "What does the CPU do?", 1, "The CPU fetches, decodes and executes program instructions, billions of times a second.", false,
               "Stores files when the PC is off", "Executes program instructions", "Converts wall power to DC", "Sends video to the monitor");
            Order("cpu_2", "Put the CPU install steps in order.", "Open the socket, match the triangles, drop it in without pushing, then lock the lever.",
                  "Lift the socket lever", "Line up the gold triangle", "Lower the CPU straight down", "Close the lever to lock it");
            Type("cpu_3", "The small gold ___ on the corner of the CPU shows which way it goes in.", "triangle",
                 new[] { "gold triangle", "arrow", "triangle marker" }, new[] { "notch", "latch", "screw" },
                 "Line up the gold triangle with the triangle printed on the socket.");
            TF("cpu_4", "You should press down firmly to seat a CPU in its socket.", false,
               "Never force it. When it's lined up, a CPU drops in under its own weight.");

            // Motherboard
            Match("mobo_1", "Match each slot to the part that plugs into it.", "Each slot has its own shape, so a part only fits where it belongs.",
                  "DIMM slots", "RAM", "PCIe x16 slot", "Graphics card", "M.2 slot", "NVMe SSD", "24-pin header", "PSU main power");
            MC("mobo_2", "The long 24-pin connector on the board's edge is for…", 0, "It's the main power input from the PSU.", false,
               "Main power from the PSU", "Front-panel USB", "The CPU fan", "SATA drives");
            Type("mobo_3", "The small metal posts that hold the motherboard off the case are called…", "standoffs",
                 new[] { "standoff", "stand-offs", "motherboard standoffs" }, new[] { "grommets", "brackets", "latches" },
                 "Standoffs lift the board so the solder points on its back never touch the metal case.");
            TF("mobo_4", "An extra standoff where the motherboard has no screw hole is harmless.", false,
               "It can touch the back of the board and cause a short circuit.");

            // RAM
            MC("ram_1", "RAM is used for…", 1, "RAM holds data for running programs. It's cleared when the power goes off.", false,
               "Permanent storage", "Fast, temporary storage for running programs", "Cooling the CPU", "Supplying power");
            MC("ram_2", "A DDR4 stick won't go into a DDR5 slot. Why?", 1, "The key notch is in a different place, so the wrong memory type can't be inserted.", false,
               "It's upside down", "The key notch is in a different position", "The clips are broken", "It needs more power");
            TF("ram_3", "With two RAM sticks, slots A2 and B2 usually give you dual-channel mode.", true,
               "Most boards want the second and fourth slots (A2 and B2) for two sticks. Check the manual to be sure.");
            Type("ram_4", "What does RAM stand for?", "Random Access Memory", new[] { "random-access memory" },
                 new[] { "Read Access Memory", "Rapid Action Memory", "Random Array Module" },
                 "Random Access Memory: any byte can be read directly, which makes it very fast.");

            // Storage
            MC("ssd_1", "Which part keeps your files when the PC is switched off?", 2, "SSDs store data permanently in flash memory.", false,
               "RAM", "CPU cache", "SSD", "Graphics memory");
            Order("ssd_2", "Put the M.2 SSD install steps in order.", "Angle it in, press it flat, then secure it.",
                  "Remove the M.2 screw", "Insert the SSD at about 30°", "Press it flat", "Secure the screw");
            TF("ssd_3", "An M.2 NVMe SSD needs a SATA data cable.", false, "M.2 drives plug straight into the motherboard. No cables at all.");
            MC("sata_1", "A 2.5-inch SATA SSD needs which cables?", 1,
               "A SATA drive takes power from the PSU and talks to the motherboard over a separate data cable.", false,
               "Just one USB cable", "SATA power and SATA data", "24-pin and EPS 8-pin", "None — it plugs into the board");
            TF("sata_2", "An M.2 NVMe SSD is usually several times faster than a SATA SSD.", true,
               "SATA tops out around 550 MB/s; NVMe drives on PCIe reach several thousand MB/s.");
            Match("sata_3", "Match each SATA connector to its job.", "The wide L-shaped plug is power; the narrow one is data.",
                  "15-pin (wide)", "Power from the PSU", "7-pin (narrow)", "Data to the motherboard");

            // Graphics card
            Type("gpu_1", "Which slot does a graphics card use?", "PCIe x16",
                 new[] { "PCIe", "PCI Express x16", "PCI-E x16", "PCIe x16 slot", "x16" }, new[] { "DIMM", "M.2", "SATA" },
                 "Graphics cards use the long PCIe x16 slot, usually the one closest to the CPU.");
            MC("gpu_2", "Your GPU needs at least a 650 W PSU but you have 550 W. What does the compatibility checker show?", 2,
               "Below the minimum is incompatible (red). Just above it is \"check required\" (yellow).", false,
               "Green — compatible", "Yellow — check required", "Red — incompatible");
            TF("gpu_3", "With a graphics card installed, plug the monitor into the graphics card, not the motherboard.", true,
               "The motherboard's video ports use the CPU's graphics, not your graphics card.");

            // Power supply
            MC("psu_1", "You're about to open the case. What should you do first?", 1,
               "Switch the PSU off and unplug the power cable before touching anything inside.", true,
               "Leave it plugged in so it stays grounded", "Switch off and unplug the PC", "Remove the CPU cooler", "Tap the parts to check for static");
            Type("psu_2", "A power supply turns AC wall power into ___ power for the components.", "DC",
                 new[] { "direct current" }, new[] { "AC", "RF", "USB" }, "Components run on DC: 12 V, 5 V and 3.3 V.");
            TF("psu_3", "It's safe to open a power supply once it's unplugged.", false,
               "Capacitors inside can hold a dangerous charge long after unplugging. Never open a PSU.", true);
            Match("psu_4", "Match each PSU cable to where it goes.", "Each connector is keyed so it only fits its own socket.",
                  "24-pin ATX", "Motherboard", "EPS 8-pin", "CPU power", "PCIe 8-pin", "Graphics card", "SATA power", "SATA drives");

            // Cooling
            MC("cooler_1", "What goes between the CPU and the cooler?", 0, "A thin layer of thermal paste fills tiny gaps so heat moves across well.", false,
               "Thermal paste", "Double-sided tape", "Nothing", "A drop of water");
            Type("cooler_2", "The cooler's fan cable plugs into which motherboard header?", "CPU_FAN",
                 new[] { "CPU fan", "CPU fan header", "cpufan" }, new[] { "SYS_FAN", "PUMP", "USB 3.0" },
                 "CPU_FAN lets the board speed the fan up as the CPU gets hotter.");
            TF("cooler_3", "Tighten cooler screws a little at a time in a cross pattern.", true,
               "Even pressure spreads the paste evenly and protects the CPU.");

            // Case
            MC("case_1", "How should you discharge static before touching components?", 1,
               "Touching bare, unpainted metal on the case (or wearing an anti-static strap) equalizes the charge.", true,
               "Rub your feet on the carpet", "Touch bare metal on the case", "Wear wool socks", "Blow on the parts");
            TF("case_2", "Building on a carpet is a good idea because it's soft.", false, "Carpet builds up static. Work on a hard table.", true);
            Order("case_3", "Put the build in the order used in Virtual Assembly.", "Power supply first, graphics card last.",
                  "Install the power supply", "Seat the CPU", "Mount the cooler", "Install the RAM", "Install the graphics card");

            // Cables
            MC("cable_1", "Which cable powers the CPU?", 2, "The EPS 8-pin (often labeled CPU) powers the processor.", false,
               "SATA power", "24-pin ATX", "EPS 8-pin", "USB");
            TF("cable_2", "If a connector won't go in, push harder.", false, "Connectors are keyed. If one resists, flip it and check the latch side.");

            // I/O
            MC("io_1", "Which port is used for a wired network?", 1, "RJ-45 Ethernet is the wired network port.", false,
               "HDMI", "RJ-45 Ethernet", "USB-A", "3.5 mm audio");
            Match("io_2", "Match each rear port to what it's for.", "Shape and color tell you what each port is for.",
                  "USB-A", "Keyboard and mouse", "RJ-45", "Wired network", "HDMI", "Display", "Green 3.5 mm jack", "Speakers");

            return map;
        }

        static CompatibilityRule Rule(CompatibilityRule.RuleType type, string s = null, int i = 0) =>
            new CompatibilityRule { ruleType = type, stringValue = s, intValue = i };

        static ARLabelDefinition ArLabel(string text, float x, float y) => new ARLabelDefinition { text = text, positionInBox = new Vector2(x, y) };

        static void CreateComponents(Dictionary<string, QuizQuestionSO> quiz)
        {
            void C(string id, string name, ComponentCategory cat, string ml, string prefab, string shortDesc, string what,
                   string[] facts, string[] chips, string[] install, string safety, ARLabelDefinition[] labels,
                   CompatibilityRule[] rules)
            {
                var c = CreateOrLoad<ComponentDefinitionSO>($"{ComponentsRes}/{id}.asset", out bool created);
                if (created)
                {
                    c.id = id; c.displayName = name; c.category = cat; c.mlLabel = ml;
                    c.shortDescription = shortDesc; c.whatItDoes = what;
                    c.keyFacts = facts.ToList(); c.specChips = chips.ToList(); c.installationSteps = install.ToList();
                    c.safetyTip = safety; c.arLabels = labels.ToList(); c.compatibilityRules = rules.ToList();
                    c.quizQuestions = ComponentCards.TryGetValue(id, out var cardIds) ? cardIds.Select(q => quiz[q]).ToList() : new List<QuizQuestionSO>();
                }
                if (c.model3DPrefab == null) c.model3DPrefab = Placeholder(prefab);
                EditorUtility.SetDirty(c);
            }

            C("cpu", "Processor (CPU)", ComponentCategory.CPU, "cpu", "PH_CPU",
              "The brain of the computer. It runs program instructions and does the calculations.",
              "The CPU (Central Processing Unit) repeats a simple cycle billions of times a second: fetch an instruction from memory, decode it, execute it, and store the result.",
              new[] { "Fits in an AM5 socket on the motherboard", "6 cores / 12 threads", "Needs a cooler and thermal paste", "Works with DDR5 memory only" },
              new[] { "AM5", "6 cores", "DDR5", "65 W" },
              new[] { "Lift the socket's retention lever.", "Line up the gold triangle with the triangle on the socket.", "Lower the CPU straight down — never push.", "Close the lever to lock it in." },
              "Hold the CPU by its edges and never touch the contacts underneath.",
              new[] { ArLabel("Heat spreader", 0.5f, 0.45f), ArLabel("Pin 1 triangle", 0.1f, 0.88f), ArLabel("Substrate", 0.9f, 0.9f) },
              new[] { Rule(CompatibilityRule.RuleType.SocketType, "AM5"), Rule(CompatibilityRule.RuleType.RamGeneration, "DDR5") });

            C("motherboard", "Motherboard", ComponentCategory.Motherboard, "motherboard", "PH_Motherboard",
              "The main circuit board. Every other part plugs into it.",
              "The motherboard connects the CPU, memory, storage, graphics card and power, and carries data between them.",
              new[] { "AM5 CPU socket", "4 DDR5 DIMM slots", "PCIe x16 slot for the graphics card", "M.2 slot for NVMe SSDs" },
              new[] { "AM5", "ATX", "DDR5", "M.2 NVMe" },
              new[] { "Install the I/O shield (if separate) in the case.", "Check the standoffs match the board's screw holes.", "Lower the board in at an angle so ports go through the I/O shield.", "Screw it down — snug, not tight." },
              "Make sure standoffs are only under screw holes; an extra standoff can short the board.",
              new[] { ArLabel("CPU socket", 0.45f, 0.3f), ArLabel("DIMM slots", 0.72f, 0.3f), ArLabel("PCIe x16", 0.4f, 0.62f), ArLabel("24-pin ATX", 0.95f, 0.4f), ArLabel("M.2 slot", 0.45f, 0.5f), ArLabel("Rear I/O", 0.05f, 0.2f) },
              new[] { Rule(CompatibilityRule.RuleType.SocketType, "AM5"), Rule(CompatibilityRule.RuleType.RamGeneration, "DDR5"), Rule(CompatibilityRule.RuleType.FormFactor, "ATX"), Rule(CompatibilityRule.RuleType.InterfaceType, "M.2 NVMe"), Rule(CompatibilityRule.RuleType.InterfaceType, "SATA") });

            C("ram_ddr5", "Memory (DDR5 RAM)", ComponentCategory.RAM, "ram", "PH_RAM",
              "Fast, temporary memory for running programs. Cleared when the PC turns off.",
              "RAM (Random Access Memory) holds the data the CPU is working on right now. More RAM lets you run more programs at once.",
              new[] { "DDR5 — only fits DDR5 slots", "Install sticks in pairs for dual-channel", "The notch only lines up one way" },
              new[] { "DDR5", "16 GB", "5600 MT/s" },
              new[] { "Open the clips at the ends of the slot.", "Line up the notch with the key in the slot.", "Press down firmly on both ends until the clips click.", "With two sticks, use slots A2 and B2." },
              "Hold RAM by its edges — don't touch the gold contacts.",
              new[] { ArLabel("Gold contacts", 0.5f, 0.92f), ArLabel("Key notch", 0.42f, 0.98f), ArLabel("Memory chips", 0.3f, 0.45f), ArLabel("Locking notch", 0.02f, 0.6f) },
              new[] { Rule(CompatibilityRule.RuleType.RamGeneration, "DDR5") });

            C("ram_ddr4", "Memory (DDR4 RAM)", ComponentCategory.RAM, "", "PH_RAM",
              "Previous-generation memory. Shown to practice spotting incompatible parts.",
              "DDR4 looks similar to DDR5, but the notch is in a different place and the electrical design differs, so they're not interchangeable.",
              new[] { "Not compatible with DDR5 motherboards", "Key notch is in a different position" },
              new[] { "DDR4", "16 GB" },
              new string[0], "", new ARLabelDefinition[0],
              new[] { Rule(CompatibilityRule.RuleType.RamGeneration, "DDR4") });

            C("ram_ddr3", "Memory (DDR3 RAM)", ComponentCategory.RAM, "", "PH_RAM",
              "Older memory, from before DDR4. Shown to practice spotting incompatible parts.",
              "DDR3 was the standard desktop memory before DDR4. It is slower, runs at a higher voltage, has fewer pins and its notch sits in another place again, so it only fits DDR3 motherboards.",
              new[] { "Only fits DDR3 motherboards", "Not compatible with DDR4 or DDR5 boards", "240 pins (DDR4 and DDR5 have 288)", "Runs at 1.5 V (DDR4 1.2 V, DDR5 1.1 V)" },
              new[] { "DDR3", "8 GB", "1600 MT/s" },
              new string[0], "", new ARLabelDefinition[0],
              new[] { Rule(CompatibilityRule.RuleType.RamGeneration, "DDR3") });

            C("ssd_m2", "Storage Drive (M.2 SSD)", ComponentCategory.Storage, "ssd", "PH_SSD_M2",
              "Stores the operating system, apps and files — even when the power is off.",
              "An NVMe SSD stores data in flash memory chips and talks to the CPU over PCIe, which makes it many times faster than a hard drive.",
              new[] { "Plugs straight into the motherboard — no cables", "Uses the M-key M.2 slot", "Held down by a small screw or latch" },
              new[] { "M.2 2280", "NVMe", "1 TB" },
              new[] { "Remove the M.2 heatsink or screw if present.", "Insert the SSD at about 30° into the slot.", "Press it flat.", "Secure with the screw or latch." },
              "Don't overtighten the tiny M.2 screw.",
              new[] { ArLabel("M-key connector", 0.03f, 0.5f), ArLabel("Controller", 0.35f, 0.5f), ArLabel("NAND flash", 0.7f, 0.5f), ArLabel("Screw notch", 0.98f, 0.5f) },
              new[] { Rule(CompatibilityRule.RuleType.InterfaceType, "M.2 NVMe") });

            C("sata_ssd", "Storage Drive (SATA SSD)", ComponentCategory.Storage, "", "PH_SSD_SATA",
              "A 2.5-inch drive for extra space: games, photos and backups. Keeps everything when the power is off.",
              "A SATA SSD stores data in flash memory like an M.2 drive, but in a 2.5-inch case with two cables: SATA power from the PSU and SATA data to the motherboard. It's slower than NVMe but still far faster than a hard drive.",
              new[] { "Needs two cables: SATA power and SATA data", "Up to about 550 MB/s", "Mounts in a drive bay, bracket or the PSU basement" },
              new[] { "2.5-inch", "SATA III", "1 TB" },
              new[] { "Mount the drive in its bay or bracket.", "Plug the SATA power cable from the PSU into the wide socket.", "Plug a SATA data cable from the motherboard into the narrow socket.", "Tidy both cables behind the tray." },
              "Hold drives by their edges and don't drop them.",
              new ARLabelDefinition[0],
              new[] { Rule(CompatibilityRule.RuleType.InterfaceType, "SATA") });

            C("gpu", "Graphics Card (GPU)", ComponentCategory.GraphicsCard, "gpu", "PH_GPU",
              "Draws images, video and 3D graphics. Plugs into the PCIe x16 slot and needs extra power.",
              "The GPU has thousands of small cores that work in parallel, which is ideal for drawing frames in games and 3D apps.",
              new[] { "Uses the top PCIe x16 slot", "Needs an 8-pin PCIe power cable", "Plug your monitor into the card, not the motherboard" },
              new[] { "PCIe x16", "8-pin power", "650 W PSU" },
              new[] { "Remove the expansion slot covers on the case.", "Open the PCIe slot latch.", "Press the card straight down until the latch clicks.", "Screw the bracket to the case and connect PCIe power." },
              "Support long, heavy cards so they don't sag and stress the slot.",
              new[] { ArLabel("PCIe x16 connector", 0.4f, 0.96f), ArLabel("8-pin power", 0.85f, 0.04f), ArLabel("Display outputs", 0.02f, 0.5f), ArLabel("Fans", 0.5f, 0.45f) },
              new[] { Rule(CompatibilityRule.RuleType.WattageMinimum, i: 650) });

            C("psu", "Power Supply (PSU)", ComponentCategory.PowerSupply, "psu", "PH_PSU",
              "Turns wall power into the steady DC voltages every component needs.",
              "The PSU converts AC mains electricity into 12 V, 5 V and 3.3 V DC and distributes it through its cables.",
              new[] { "Rated at 700 W", "Has 24-pin, EPS 8-pin, PCIe and SATA cables", "Keep the switch at O (off) while building" },
              new[] { "ATX", "700 W", "80+ Gold" },
              new[] { "Set the switch to O and unplug it.", "Slide it into the bottom bay, fan facing down.", "Screw it in from the back of the case.", "Route cables through the grommets." },
              "Never open a power supply — capacitors inside can hold a dangerous charge.",
              new[] { ArLabel("24-pin cable", 0.8f, 0.2f), ArLabel("Power switch", 0.1f, 0.8f), ArLabel("Fan", 0.5f, 0.5f), ArLabel("AC inlet", 0.1f, 0.5f) },
              new[] { Rule(CompatibilityRule.RuleType.WattageProvided, i: 700), Rule(CompatibilityRule.RuleType.FormFactor, "ATX") });

            C("cpu_cooler", "CPU Cooler", ComponentCategory.Cooling, "cooler", "PH_Cooler",
              "Pulls heat away from the CPU so it doesn't slow down or overheat.",
              "Heat pipes carry heat from the CPU into a stack of metal fins, and a fan blows air through the fins.",
              new[] { "Needs thermal paste between it and the CPU", "Fan cable goes to the CPU_FAN header", "Bracket must match the socket" },
              new[] { "AM5", "120 mm fan", "4-pin PWM" },
              new[] { "Fit the AM5 mounting bracket.", "Check thermal paste is on the CPU.", "Place the cooler and tighten screws diagonally.", "Connect the fan to CPU_FAN." },
              "Tighten screws a little at a time in a cross pattern for even pressure.",
              new[] { ArLabel("Fin stack", 0.5f, 0.35f), ArLabel("Fan", 0.15f, 0.5f), ArLabel("Heat pipes", 0.5f, 0.85f), ArLabel("Mounting bracket", 0.9f, 0.95f) },
              new[] { Rule(CompatibilityRule.RuleType.SocketType, "AM5") });

            C("pc_case", "PC Case", ComponentCategory.Case, "case", "PH_Case",
              "Holds and protects every part and guides airflow.",
              "The case has mounting points for the motherboard, drives and PSU, plus front-panel buttons and ports.",
              new[] { "Supports ATX motherboards", "Bottom PSU shroud", "Front I/O: USB and audio" },
              new[] { "ATX", "Mid tower", "Tempered glass" },
              new[] { "Remove both side panels and store the screws.", "Check pre-installed standoffs.", "Lay the case on its side on a clear, non-carpeted table." },
              "Work on a hard surface and touch the bare metal frame to discharge static.",
              new[] { ArLabel("Front I/O", 0.95f, 0.1f), ArLabel("PSU shroud", 0.6f, 0.85f), ArLabel("Motherboard tray", 0.4f, 0.4f), ArLabel("Rear fan", 0.05f, 0.2f) },
              new[] { Rule(CompatibilityRule.RuleType.FormFactor, "ATX") });

            C("cable_24pin", "24-pin ATX Power Cable", ComponentCategory.CableConnector, "cable", "PH_Cable24",
              "Carries main power from the PSU to the motherboard.",
              "The 24-pin cable supplies the motherboard's main voltages. Its keyed pins and latch make sure it only fits one way and stays in place.",
              new[] { "Only fits one way", "Press until the latch clicks", "Separate from the CPU EPS 8-pin cable" },
              new[] { "ATX 24-pin", "Latching" },
              new[] { "Line up the latch with the tab on the header.", "Push straight in until it clicks.", "Tug gently to check it's locked." },
              "Never force a connector — if it doesn't fit, check the orientation.",
              new[] { ArLabel("Latch clip", 0.5f, 0.1f), ArLabel("Keyed pins", 0.5f, 0.85f) },
              new CompatibilityRule[0]);

            C("io_panel", "Rear I/O Ports", ComponentCategory.InputOutput, "io_panel", "PH_IOPanel",
              "Where your keyboard, mouse, network and speakers connect.",
              "The rear I/O panel exposes the motherboard's USB, video, network and audio ports through the back of the case.",
              new[] { "USB-A for keyboard and mouse", "RJ-45 for wired network", "Green audio jack for speakers" },
              new[] { "USB-A", "HDMI", "RJ-45", "Audio" },
              new[] { "Connect keyboard and mouse to USB.", "Connect the monitor to the graphics card (not HDMI here) if you have one.", "Plug in Ethernet and speakers." },
              "",
              new[] { ArLabel("USB-A", 0.15f, 0.4f), ArLabel("HDMI", 0.35f, 0.4f), ArLabel("Ethernet", 0.6f, 0.5f), ArLabel("Audio", 0.9f, 0.5f) },
              new CompatibilityRule[0]);
        }

        static void CreateLessons()
        {
            void L(string id, string title, LessonSO.PathStage stage, int order, ComponentCategory cat, string comp,
                   LessonSO.Difficulty diff, int minutes, string summary, params string[] prereqs)
            {
                var l = CreateOrLoad<LessonSO>($"{LessonsRes}/{id}.asset", out bool created);
                if (!created) return;
                l.lessonId = id; l.title = title; l.stage = stage; l.order = order; l.relatedCategory = cat;
                l.relatedComponentId = comp; l.difficulty = diff; l.estimatedMinutes = minutes; l.summary = summary;
                l.prerequisiteLessonIds = prereqs.ToList();
                EditorUtility.SetDirty(l);
            }
            var B = LessonSO.Difficulty.Beginner;
            var I = LessonSO.Difficulty.Intermediate;
            var ID = LessonSO.PathStage.IdentifyComponents;

            L("l01_inside_pc", "What's inside a PC", ID, 0, ComponentCategory.Case, "pc_case", B, 5, "A tour of the main parts.");
            L("l02_cpu", "The CPU: the brain", ID, 1, ComponentCategory.CPU, "cpu", B, 6, "What a processor does.", "l01_inside_pc");
            L("l03_ram", "Memory (RAM) basics", ID, 2, ComponentCategory.RAM, "ram_ddr5", B, 5, "Short-term memory.", "l01_inside_pc");
            L("l04_motherboard", "Motherboard tour", ID, 3, ComponentCategory.Motherboard, "motherboard", B, 8, "Sockets, slots and headers.", "l01_inside_pc");
            L("l05_storage", "Storage drives", ID, 4, ComponentCategory.Storage, "ssd_m2", B, 5, "SSDs and hard drives.", "l01_inside_pc");
            L("l06_gpu", "Graphics cards", ID, 5, ComponentCategory.GraphicsCard, "gpu", B, 6, "How pictures get drawn.", "l01_inside_pc");
            L("l07_psu", "Power supplies", ID, 6, ComponentCategory.PowerSupply, "psu", B, 5, "Where power comes from.", "l01_inside_pc");
            L("l08_sockets", "Sockets & memory types", LessonSO.PathStage.Compatibility, 0, ComponentCategory.CPU, "cpu", I, 7, "Matching CPU, board and RAM.", "l02_cpu", "l03_ram");
            L("l09_power_budget", "Power budget", LessonSO.PathStage.Compatibility, 1, ComponentCategory.PowerSupply, "psu", I, 6, "Choosing enough wattage.", "l06_gpu", "l07_psu");
            L("l10_workspace", "Prepare a safe workspace", LessonSO.PathStage.WorkspacePreparation, 0, ComponentCategory.Case, "pc_case", B, 4, "Static, tools and light.", "l08_sockets");
            L("l11_install_core", "Install CPU, cooler & RAM", LessonSO.PathStage.InstallationSteps, 0, ComponentCategory.Cooling, "cpu_cooler", I, 10, "The core install.", "l10_workspace");
            L("l12_cables", "Cables & front-panel connectors", LessonSO.PathStage.InstallationSteps, 1, ComponentCategory.CableConnector, "cable_24pin", I, 8, "Power and header cables.", "l11_install_core");
            L("l13_first_boot", "First boot & troubleshooting", LessonSO.PathStage.FirstBoot, 0, ComponentCategory.InputOutput, "io_panel", I, 8, "Power on and check.", "l12_cables");
        }

        static void CreateAssemblyGuide()
        {
            var g = CreateOrLoad<AssemblyGuideSO>($"{GuidesRes}/FirstBuild.asset", out bool created);
            if (!created) return;
            g.guideId = "first_build";
            g.title = "Your first build";

            AssemblyStep Place(string id, string title, string text, string comp, string safety, string badge = null, params string[] slots) =>
                new AssemblyStep { stepId = id, title = title, instruction = text, kind = AssemblyStep.StepKind.PlaceComponent, componentId = comp, safetyTip = safety, badgeOnComplete = badge, validSlotIds = slots.ToList() };
            AssemblyStep Cable(string id, string title, string text, string type, string safety, string badge = null) =>
                new AssemblyStep { stepId = id, title = title, instruction = text, kind = AssemblyStep.StepKind.ConnectCable, connectorType = type, safetyTip = safety, badgeOnComplete = badge };

            g.steps = new List<AssemblyStep>
            {
                Place("s01_psu", "Install the power supply", "Drag the PSU into the bay at the bottom of the case, fan facing down.", "psu",
                      "Make sure the PSU switch is at O (off) and the wall cable is unplugged."),
                Place("s02_cpu", "Seat the CPU", "Drag the CPU onto the socket. The gold triangle must match the socket marker.", "cpu",
                      "Hold the CPU by its edges. Never touch the contacts or the socket pins."),
                Place("s03_cooler", "Mount the CPU cooler", "Drag the cooler onto the CPU. Thermal paste is already applied.", "cpu_cooler",
                      "Tighten cooler screws a little at a time in a cross pattern."),
                Place("s04_ram_1", "Install the first RAM stick", "Drag a RAM stick into slot A2 (second from the CPU).", "ram_ddr5",
                      "Line up the notch before pressing — forcing it can crack the slot.", null, "ram_a2", "ram_b2"),
                Place("s05_ram_2", "Install the second RAM stick", "Put the second stick in B2 so the pair runs in dual-channel.", "ram_ddr5",
                      "Press both ends until the clips click.", BadgeCatalog.RamInstaller, "ram_a2", "ram_b2"),
                Place("s06_ssd", "Install the M.2 SSD", "Drag the SSD into the M.2 slot below the CPU.", "ssd_m2",
                      "Don't overtighten the small M.2 screw.", null, "m2_1"),
                SataSsdStep(),
                Place("s07_gpu", "Install the graphics card", "Drag the graphics card into the PCIe x16 slot.", "gpu",
                      "Support heavy cards so they don't sag."),
                Cable("s08_atx24", "Connect 24-pin motherboard power", "Tap the PSU's 24-pin cable end, then the 24-pin header on the right edge of the board.", "ATX24",
                      "Connectors only fit one way. If it resists, check the latch side."),
                Cable("s09_eps", "Connect CPU power (EPS 8-pin)", "Tap the PSU's CPU 8-pin cable, then the header at the top-left of the board.", "EPS8",
                      "Don't mix up CPU 8-pin and PCIe 8-pin cables — they're keyed differently."),
                SataPowerStep(),
                SataDataStep(),
                Cable("s10_fpanel", "Connect the front-panel header", "Tap the case's front-panel cable, then the header at the bottom-right of the board.", "FPANEL",
                      "Check the motherboard manual for the power switch and LED pin layout.", BadgeCatalog.CableExpert),
            };
            EditorUtility.SetDirty(g);
        }

        // ================================================================== scenes

        static PanelSettings CreatePanelSettings()
        {
            EnsureFolder(Root + "/UI/Resources");
            string path = Root + "/UI/Resources/" + BuildAR.UI.PanelSettingsFallback.ResourceName + ".asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(390, 844);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0f;
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss")
                                 ?? AssetDatabase.FindAssets("t:ThemeStyleSheet").Select(g => AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            AssetDatabase.CreateAsset(ps, path);
            return ps;
        }

        static bool ConfirmOverwrite(string path) =>
            !File.Exists(path) || EditorUtility.DisplayDialog("BuildAR", $"{path} already exists. Rebuild it?", "Rebuild", "Keep existing");

        static Scene NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static void Save(Scene scene, string path) => EditorSceneManager.SaveScene(scene, path);

        static void AddLight()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50, -35, 0);
        }

        static void AddBootstrap() => new GameObject("DevBootstrap").AddComponent<DevBootstrap>();

        static GameObject AddScreen(Transform parent, string name, PanelSettings panel, string uxml, System.Type controller)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UiFolder}/{uxml}");
            if (tree == null) Debug.LogError($"BuildAR: {UiFolder}/{uxml} not found.");
            // Assign through SerializedObject: setting UIDocument.panelSettings in edit mode isn't saved to the scene.
            Set(doc, "m_PanelSettings", panel);
            Set(doc, "sourceAsset", tree);
            go.AddComponent(controller);
            return go;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"BuildAR: field '{field}' not found on {target.GetType().Name}."); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static string BuildBootScene()
        {
            string path = $"{ScenesFolder}/{BootScene}.unity";
            if (!ConfirmOverwrite(path)) return path;
            var scene = NewScene();
            new GameObject("GameManager").AddComponent<GameManager>();
            new GameObject("ProgressManager").AddComponent<ProgressManager>();
            new GameObject("AudioManager").AddComponent<AudioManager>();
            new GameObject("ComponentDatabase").AddComponent<ComponentDatabase>();
            Save(scene, path);
            return path;
        }

        static string BuildAppScene(PanelSettings panel)
        {
            string path = $"{ScenesFolder}/{AppScene}.unity";
            if (!ConfirmOverwrite(path)) return path;
            var scene = NewScene();
            AddBootstrap();
            AddLight();

            var uiCam = new GameObject("UI Camera").AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = Hex("F3F6FB");
            uiCam.cullingMask = 0;

            // 3D model viewer stage, far away from everything else.
            var stage = new GameObject("ModelViewerStage");
            stage.transform.position = new Vector3(0, -1000, 0);
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(stage.transform, false);
            var viewerCam = new GameObject("Viewer Camera").AddComponent<Camera>();
            viewerCam.transform.SetParent(stage.transform, false);
            viewerCam.clearFlags = CameraClearFlags.SolidColor;
            viewerCam.backgroundColor = Hex("0B1533");
            viewerCam.fieldOfView = 30;
            var rim = new GameObject("Rim Light").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = Hex("22D3EE");
            rim.intensity = 0.6f;
            rim.transform.SetParent(stage.transform, false);
            rim.transform.rotation = Quaternion.Euler(20, 150, 0);
            var stageComp = stage.AddComponent<ModelViewerStage>();
            Set(stageComp, "viewerCamera", viewerCam);
            Set(stageComp, "pivot", pivot);

            var screens = new GameObject("Screens").transform;
            // Screens are saved inactive; ScreenRouter enables one once the managers are ready.
            var entries = new (AppScreen screen, GameObject go)[]
            {
                (AppScreen.Welcome, AddScreen(screens, "Welcome", panel, "WelcomeScreen.uxml", typeof(WelcomeScreenController))),
                (AppScreen.Home, AddScreen(screens, "Home", panel, "HomeScreen.uxml", typeof(HomeScreenController))),
                (AppScreen.Learn, AddScreen(screens, "Learn", panel, "LearnScreen.uxml", typeof(LearnScreenController))),
                (AppScreen.ComponentDetail, AddScreen(screens, "ComponentDetail", panel, "ComponentDetail.uxml", typeof(ComponentDetailController))),
                (AppScreen.Progress, AddScreen(screens, "Progress", panel, "ProgressScreen.uxml", typeof(ProgressScreenController))),
                (AppScreen.Quiz, AddScreen(screens, "Quiz", panel, "QuizScreen.uxml", typeof(QuizScreenController))),
            };

            var router = screens.gameObject.AddComponent<ScreenRouter>();
            var so = new SerializedObject(router);
            var list = so.FindProperty("screens");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("screen").enumValueIndex = (int)entries[i].screen;
                e.FindPropertyRelative("root").objectReferenceValue = entries[i].go;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach (var e in entries) e.go.SetActive(false);

            Save(scene, path);
            return path;
        }

        static string BuildArScene(PanelSettings panel)
        {
            string path = $"{ScenesFolder}/{ArScene}.unity";
            if (!ConfirmOverwrite(path)) return path;
            var scene = NewScene();
            AddBootstrap();
            AddLight();

            Selection.activeObject = null;
            bool session = EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            Selection.activeObject = null;
            bool origin = EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            if (!session || !origin)
                Debug.LogError("BuildAR: couldn't create AR Session / XR Origin via the GameObject > XR menu. Add them manually to BuildAR_ARScanner.");

            var cameraManager = Object.FindAnyObjectByType<ARCameraManager>();
            var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
            Camera arCamera = xrOrigin != null ? xrOrigin.Camera : null;

            // Surfaces + raycasting, so a scanned part's model can be dropped onto the table it is sitting on.
            ARPlaneManager planes = null;
            ARRaycastManager raycasts = null;
            ARTrackedImageManager images = null;
            if (xrOrigin != null)
            {
                if (arCamera != null) arCamera.nearClipPlane = 0.01f;
                planes = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                planes.planePrefab = BuildArPlanePrefab();
                EditorUtility.SetDirty(planes);
                raycasts = xrOrigin.gameObject.AddComponent<ARRaycastManager>();

                images = xrOrigin.gameObject.AddComponent<ARTrackedImageManager>();
                var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(ReferenceLibraryPath);
                if (library != null) images.referenceLibrary = library;
                images.requestedMaxNumberOfMovingImages = 2;
                EditorUtility.SetDirty(images);
            }

            var detectors = new GameObject("Detectors");
            var ml = detectors.AddComponent<InferenceComponentDetector>();
            Set(ml, "cameraManager", cameraManager);
            var modelGuid = AssetDatabase.FindAssets("t:ModelAsset", new[] { Root + "/ML" }).FirstOrDefault();
            if (modelGuid != null) Set(ml, "modelAsset", AssetDatabase.LoadAssetAtPath<ModelAsset>(AssetDatabase.GUIDToAssetPath(modelGuid)));

            // The trained model's own labels win over the placeholder. Assigning labels.txt unconditionally used to
            // undo a hand-set labels file every time scenes were rebuilt, which shows up as the scanner naming
            // parts at random — the class list and the model disagree and nothing says so.
            var labels = AssetDatabase.LoadAssetAtPath<TextAsset>(MlModels + "/labels_pcparts_roboflow.txt")
                         ?? AssetDatabase.LoadAssetAtPath<TextAsset>(MlModels + "/labels.txt");
            Set(ml, "labelsFile", labels);

            var tracked = detectors.AddComponent<TrackedImageComponentDetector>();
            Set(tracked, "trackedImageManager", images);
            Set(tracked, "arCamera", arCamera);

            // Present but inert until an API key is pasted in, so it costs nothing to leave wired up.
            var roboflow = detectors.AddComponent<RoboflowApiDetector>();
            Set(roboflow, "cameraManager", cameraManager);

            var sim = detectors.AddComponent<SimulatedComponentDetector>();

            var presenter = new GameObject("ScannedModel").AddComponent<ScannedModelPresenter>();
            Set(presenter, "raycastManager", raycasts);
            Set(presenter, "planeManager", planes);
            Set(presenter, "arCamera", arCamera);

            var ui = AddScreen(null, "ARScannerUI", panel, "ARScanner.uxml", typeof(ARScannerController));
            var controller = ui.GetComponent<ARScannerController>();
            // On-device model first, then photos, then the hosted API, then the simulator. Move roboflow ahead of
            // ml to demonstrate the published cloud model instead.
            SetArray(controller, "detectors", ml, tracked, roboflow, sim);
            Set(controller, "modelPresenter", presenter);

            Save(scene, path);
            return path;
        }

        /// <summary>
        /// Turns the photos in ML/ReferenceImages into an AR Foundation image library, so the scanner can recognise
        /// those parts with no trained model. Each file is named after the component's mlLabel (gpu.jpg, cpu.jpg…).
        /// </summary>
        [MenuItem("BuildAR/Setup/Build Reference Image Library", priority = 26)]
        public static void BuildReferenceImageLibrary()
        {
            EnsureFolder(ReferenceImages);
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { ReferenceImages })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("BuildAR",
                    $"No photos found.\n\nPut one photo of each part in {ReferenceImages}, named after its ML label:\n" +
                    "cpu.jpg, motherboard.jpg, ram.jpg, ssd.jpg, gpu.jpg, psu.jpg, cooler.jpg, case.jpg, cable.jpg, io_panel.jpg\n\n" +
                    "Shoot each part straight on, filling the frame, on a plain background in even light. " +
                    "Then run this menu item again.", "OK");
                return;
            }

            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(ReferenceLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, ReferenceLibraryPath);
            }
            while (library.count > 0) library.RemoveAt(library.count - 1);

            foreach (var texturePath in paths)
            {
                // The library build needs to read the pixels.
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) continue;
                string label = Path.GetFileNameWithoutExtension(texturePath).ToLowerInvariant();

                library.Add();
                int i = library.count - 1;
                library.SetTexture(i, texture, true);
                library.SetName(i, label);
                library.SetSpecifySize(i, true);
                float width = ReferenceWidth(label);
                library.SetSize(i, new Vector2(width, width * texture.height / Mathf.Max(1, texture.width)));
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BuildAR: reference image library rebuilt with {library.count} image(s) at {ReferenceLibraryPath}. " +
                      "Run BuildAR > Setup > 4. Build Scenes so the scanner picks it up.");
        }

        /// <summary>Real width of each part in metres, so the tracked pose lands at the right distance.</summary>
        static float ReferenceWidth(string label)
        {
            switch (label)
            {
                case "cpu": return 0.04f;
                case "ram": return 0.133f;
                case "motherboard": return 0.305f;
                case "ssd": return 0.08f;
                case "gpu": return 0.28f;
                case "psu": return 0.15f;
                case "cooler": return 0.12f;
                case "case": return 0.45f;
                case "cable": return 0.12f;
                case "io_panel": return 0.16f;
                default: return 0.15f;
            }
        }

        static string BuildAssemblyScene(PanelSettings panel)
        {
            string path = $"{ScenesFolder}/{AssemblyScene}.unity";
            if (!ConfirmOverwrite(path)) return path;
            var scene = NewScene();
            AddBootstrap();
            AddLight();
            // Studio lighting rather than the default sky: a bright bluish sky for ambient and reflections turns
            // dark parts (the black case, the board) a washed-out grey-blue.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.35f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.21f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.09f, 0.09f);
            RenderSettings.reflectionIntensity = 0.3f;
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.5f;
            fill.color = Hex("9FB0D9");
            fill.transform.rotation = Quaternion.Euler(20, 160, 0);

            var cam = new GameObject("Assembly Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("0B1533");
            cam.fieldOfView = 40;
            cam.nearClipPlane = 0.01f;

            var casePrefab = BuildAssemblyCasePrefab();
            var caseInstance = (GameObject)PrefabUtility.InstantiatePrefab(casePrefab);

            var target = new GameObject("Orbit Target").transform;
            target.position = new Vector3(-0.03f, 0.26f, 0.02f);

            new GameObject("AssemblyManager").AddComponent<AssemblyManager>();
            var interaction = new GameObject("AssemblyInteraction").AddComponent<AssemblyInteraction>();
            Set(interaction, "cam", cam);
            Set(interaction, "orbitTarget", target);
            Set(interaction, "cableMaterial", CableMaterial());

            // AR rig (saved inactive): AssemblyARController switches to it on phones that support ARCore.
            Selection.activeObject = null;
            bool session = EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            Selection.activeObject = null;
            bool originCreated = EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            var arSession = Object.FindAnyObjectByType<ARSession>();
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var ar = new GameObject("AssemblyAR").AddComponent<AssemblyARController>();
            if (!session || !originCreated || arSession == null || origin == null)
            {
                Debug.LogError("BuildAR: couldn't create the AR Session / XR Origin for Virtual Assembly. It will use the 3D view only.");
            }
            else
            {
                origin.Camera.nearClipPlane = 0.01f;
                var planes = origin.gameObject.AddComponent<ARPlaneManager>();
                planes.planePrefab = BuildArPlanePrefab();
                EditorUtility.SetDirty(planes);
                var raycasts = origin.gameObject.AddComponent<ARRaycastManager>();

                Set(ar, "session", arSession);
                Set(ar, "origin", origin);
                Set(ar, "raycastManager", raycasts);
                Set(ar, "planeManager", planes);
                arSession.gameObject.SetActive(false);
                origin.gameObject.SetActive(false);
            }
            Set(ar, "threeDCamera", cam);
            Set(ar, "interaction", interaction);
            Set(ar, "caseRoot", caseInstance.transform);
            Set(ar, "orbitTarget", target);
            Set(ar, "reticleMaterial", UnlitTransparent("M_AR_Placement", new Color(0.7f, 0.95f, 1f, 0.28f)));

            var ui = AddScreen(null, "VirtualAssemblyUI", panel, "VirtualAssembly.uxml", typeof(VirtualAssemblyScreenController));
            var controller = ui.GetComponent<VirtualAssemblyScreenController>();
            Set(controller, "assemblyCamera", cam);
            Set(controller, "interaction", interaction);

            Save(scene, path);
            return path;
        }

        /// <summary>Detected-surface visual for AR: a faint cyan fill with a brighter edge.</summary>
        static GameObject BuildArPlanePrefab()
        {
            string path = Root + "/Prefabs/AR/AR_SurfacePlane.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            EnsureFolder(Root + "/Prefabs/AR");

            var go = new GameObject("AR_SurfacePlane");
            go.AddComponent<ARPlane>();
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = UnlitTransparent("M_AR_Plane", new Color(0.7f, 0.95f, 1f, 0.12f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var edge = go.AddComponent<LineRenderer>();
            edge.sharedMaterial = UnlitTransparent("M_AR_PlaneEdge", new Color(0.7f, 0.95f, 1f, 0.8f));
            edge.widthMultiplier = 0.004f;
            edge.useWorldSpace = false;
            edge.loop = true;
            go.AddComponent<ARPlaneMeshVisualizer>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// Placeholder mid-tower with SnapSlots and CablePorts. Origin = case floor centre; the open side faces -Z (camera).
        /// Replace the visual meshes with your case model but keep (and reposition) the Slots / Ports children.
        /// </summary>
        static GameObject BuildAssemblyCasePrefab()
        {
            string path = $"{AssemblyPrefabs}/PH_AssemblyCase.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var root = new GameObject("PH_AssemblyCase");
            var visuals = new GameObject("Visuals").transform;
            visuals.SetParent(root.transform, false);
            BuildCaseVisuals(visuals);

            const float bx = -0.07f, by = 0.28f, zs = 0.0994f;

            var slots = new GameObject("Slots").transform;
            slots.SetParent(root.transform, false);
            var flat = Quaternion.LookRotation(Vector3.up, Vector3.back);     // part's local Y points out of the board
            var upright = Quaternion.LookRotation(Vector3.right, Vector3.back); // RAM: length along Y, height out of the board

            Slot(slots, "psu_bay", ComponentCategory.PowerSupply, new Vector3(-0.13f, 0.047f, 0), Quaternion.identity,
                 new Vector3(-0.13f, 0.047f, 0), new Vector3(0.155f, 0.09f, 0.145f));
            Slot(slots, "cpu_socket", ComponentCategory.CPU, new Vector3(bx, by + 0.07f, zs - 0.007f), flat,
                 new Vector3(bx, by + 0.07f, zs - 0.0045f), new Vector3(0.052f, 0.052f, 0.001f));
            Slot(slots, "cooler_mount", ComponentCategory.Cooling, new Vector3(bx, by + 0.07f, zs - 0.09f), flat,
                 new Vector3(bx, by + 0.07f, zs - 0.012f), new Vector3(0.075f, 0.075f, 0.001f));
            string[] ramIds = { "ram_a1", "ram_a2", "ram_b1", "ram_b2" };
            for (int i = 0; i < 4; i++)
            {
                float x = bx + 0.062f + i * 0.0095f;
                var s = Slot(slots, ramIds[i], ComponentCategory.RAM, new Vector3(x, by + 0.07f, zs - 0.0215f), upright,
                             new Vector3(x, by + 0.07f, zs - 0.0065f), new Vector3(0.008f, 0.137f, 0.001f));
                if (i % 2 == 0)
                {
                    s.recommended = false;
                    s.notRecommendedMessage = "It works, but with two sticks use A2 + B2 for dual-channel speed. Check your board's manual.";
                }
            }
            Slot(slots, "m2_1", ComponentCategory.Storage, new Vector3(bx + 0.005f, by - 0.02f, zs - 0.0031f), flat,
                 new Vector3(bx + 0.005f, by - 0.02f, zs - 0.001f), new Vector3(0.084f, 0.025f, 0.001f));
            Slot(slots, "pcie_x16_1", ComponentCategory.GraphicsCard, new Vector3(bx + 0.02f, by - 0.07f, zs - 0.064f), flat,
                 new Vector3(bx - 0.01f, by - 0.07f, zs - 0.0065f), new Vector3(0.095f, 0.012f, 0.001f));

            var ports = new GameObject("CablePorts").transform;
            ports.SetParent(root.transform, false);
            Port(ports, "psu_24pin", "24-pin ATX", "ATX24", true, "psu_bay", new Vector3(-0.07f, 0.1f, -0.03f), new Vector3(0.02f, 0.012f, 0.012f), Hex("2A2A2E"));
            Port(ports, "psu_eps", "CPU 8-pin (EPS)", "EPS8", true, "psu_bay", new Vector3(-0.11f, 0.1f, -0.03f), new Vector3(0.014f, 0.012f, 0.012f), Hex("3B2A18"));
            Port(ports, "psu_sata", "SATA power", "SATA", true, "psu_bay", new Vector3(-0.15f, 0.1f, -0.03f), new Vector3(0.018f, 0.006f, 0.012f), Hex("1C1C1C"));
            Port(ports, "case_fpanel", "front-panel", "FPANEL", true, null, new Vector3(0.17f, 0.13f, -0.03f), new Vector3(0.012f, 0.008f, 0.012f), Hex("5A6785"));
            Port(ports, "mb_24pin", "24-pin ATX", "ATX24", false, null, new Vector3(bx + 0.115f, by, zs - 0.006f), new Vector3(0.006f, 0.052f, 0.01f), Hex("2A2A2E"));
            Port(ports, "mb_eps", "CPU 8-pin (EPS)", "EPS8", false, null, new Vector3(bx - 0.1f, by + 0.145f, zs - 0.006f), new Vector3(0.02f, 0.006f, 0.01f), Hex("3B2A18"));
            Port(ports, "mb_fpanel", "front-panel header", "FPANEL", false, null, new Vector3(bx + 0.1f, by - 0.14f, zs - 0.004f), new Vector3(0.02f, 0.006f, 0.006f), Hex("5A6785"));
            AddSataSsdAnchors(slots, ports, Vector3.zero);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The built-in mid-tower: chassis panels, the motherboard and its connectors, and the DIMM slots.
        /// 0.45 × 0.46 × 0.21 m, floor at y = 0, open side facing -Z. Shared by the generator and by
        /// Restore Placeholder Assembly Case.
        /// </summary>
        static void BuildCaseVisuals(Transform visuals)
        {
            // A mid-tower seen through its removed side panel: -x is the rear (I/O, expansion slots),
            // +x the front bezel, y 0 the floor, +z the motherboard tray, -z open towards the learner.
            const float top = 0.458f, depth = 0.21f;

            // ---- shell ----
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0, 0.23f, 0.103f), new Vector3(0.45f, 0.46f, 0.004f), CaseMat);   // tray wall
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0, top, 0), new Vector3(0.45f, 0.004f, depth), CaseMat);          // roof
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0, 0.002f, 0), new Vector3(0.45f, 0.004f, depth), CaseMat);       // floor
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.223f, 0.23f, 0), new Vector3(0.004f, 0.46f, depth), CaseMat);  // rear
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.223f, 0.23f, 0), new Vector3(0.004f, 0.46f, depth), CaseMat);   // front bezel
            // Lip along the open edge, so the case reads as a box with a panel removed rather than a flat wall.
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0, top - 0.006f, -0.103f), new Vector3(0.45f, 0.008f, 0.004f), Plastic);
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0, 0.008f, -0.103f), new Vector3(0.45f, 0.008f, 0.004f), Plastic);

            // ---- roof vents ----
            for (int i = 0; i < 7; i++)
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.09f + i * 0.026f, top - 0.003f, 0), new Vector3(0.013f, 0.002f, 0.15f), Chip);

            // ---- rear: I/O cutout, expansion brackets, exhaust ----
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.2205f, 0.402f, 0.055f), new Vector3(0.003f, 0.046f, 0.09f), Chip);
            for (int i = 0; i < 7; i++)
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.2205f, 0.247f - i * 0.0135f, 0.045f), new Vector3(0.003f, 0.009f, 0.075f), Metal);
            for (int i = 0; i < 5; i++)
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.2205f, 0.35f - i * 0.011f, -0.045f), new Vector3(0.003f, 0.006f, 0.07f), Chip);

            // ---- front bezel: mesh intake, power button, USB ----
            for (int i = 0; i < 12; i++)
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.2255f, 0.07f + i * 0.026f, 0), new Vector3(0.003f, 0.012f, 0.16f), Chip);
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.2265f, 0.432f, -0.06f), new Vector3(0.005f, 0.009f, 0.009f), Metal);
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.2265f, 0.432f, -0.028f), new Vector3(0.004f, 0.006f, 0.012f), Chip);
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.2265f, 0.432f, -0.008f), new Vector3(0.004f, 0.006f, 0.012f), Chip);

            // ---- basement: PSU shroud and drive cage ----
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.115f, 0.105f, 0), new Vector3(0.22f, 0.004f, depth), CaseMat);    // shroud lid
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.007f, 0.053f, 0), new Vector3(0.004f, 0.105f, depth), CaseMat);   // shroud wall
            for (int i = 0; i < 4; i++)   // vent line along the shroud
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.05f + i * 0.045f, 0.1075f, 0.06f), new Vector3(0.035f, 0.002f, 0.004f), Chip);
            Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.165f, 0.175f, 0.04f), new Vector3(0.12f, 0.13f, 0.005f), Plastic); // drive cage plate
            for (int i = 0; i < 3; i++)   // drive trays
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(0.165f, 0.135f + i * 0.04f, 0.015f), new Vector3(0.115f, 0.03f, 0.05f), Chip);

            BuildCaseMotherboard(visuals);

            // ---- case fans: intake at the front, exhaust at the back, both turning ----
            var intake = new GameObject("FanIntake").transform;
            intake.SetParent(visuals, false);
            intake.localPosition = new Vector3(0.20f, 0.30f, 0.02f);
            intake.localRotation = Quaternion.Euler(0, -90, 0);            // faces into the case
            FanAssembly(intake, 0.12f, 9, Plastic, Plastic, Accent);

            var exhaust = new GameObject("FanExhaust").transform;
            exhaust.SetParent(visuals, false);
            exhaust.localPosition = new Vector3(-0.205f, 0.35f, -0.02f);
            exhaust.localRotation = Quaternion.Euler(0, 90, 0);
            FanAssembly(exhaust, 0.10f, 7, Plastic, Plastic, Accent);

            // No decorative wiring in here on purpose: loose cylinders read as clutter rather than cable
            // management, and the cables that matter are the ones the learner connects during the build.
            // CableRun() is still available if you want to try again.

            // ---- lighting strip along the open top edge ----
            var strip = Led("M_LED_Strip", Hex("B2F1FF"), 1.4f);
            for (int i = 0; i < 9; i++)
                Model.Mesh(visuals, PrimitiveType.Cube, new Vector3(-0.17f + i * 0.043f, top - 0.012f, -0.095f),
                           new Vector3(0.03f, 0.004f, 0.004f), strip);
        }

        /// <summary>
        /// The motherboard the assembly slots are laid out on: board, socket, slots, heatsinks and standoffs, its back
        /// against the tray at z = 0.101. Import Assembly Case Model puts it inside an imported case too, since the
        /// CPU, RAM, SSD and GPU slots only make sense on this board.
        /// </summary>
        internal static void BuildCaseMotherboard(Transform parent)
        {
            const float bx = -0.07f, by = 0.28f, zs = 0.0994f;   // motherboard centre and surface

            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx, by, 0.1002f), new Vector3(0.244f, 0.305f, 0.0016f), BoardPcb);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx, by + 0.07f, zs - 0.002f), new Vector3(0.05f, 0.05f, 0.004f), Metal);   // socket
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx - 0.01f, by - 0.07f, zs - 0.003f), new Vector3(0.089f, 0.007f, 0.006f), Plastic); // PCIe
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx - 0.03f, by - 0.02f, zs - 0.0015f), new Vector3(0.022f, 0.008f, 0.003f), Plastic); // M.2
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx - 0.06f, by + 0.08f, zs - 0.007f), new Vector3(0.02f, 0.1f, 0.014f), Metal);  // VRM
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx - 0.025f, by - 0.115f, zs - 0.004f), new Vector3(0.036f, 0.036f, 0.006f), Metal); // chipset heatsink
            BuildRearIo(parent, bx - 0.122f, by + 0.1525f, zs);
            for (int i = 0; i < 4; i++)   // standoffs under the corners
                Model.Mesh(parent, PrimitiveType.Cube,
                           new Vector3(bx + (i % 2 == 0 ? -0.108f : 0.108f), by + (i < 2 ? 0.138f : -0.138f), 0.1015f),
                           new Vector3(0.008f, 0.008f, 0.003f), Metal);

            for (int i = 0; i < 4; i++)   // DIMM slots
                Model.Mesh(parent, PrimitiveType.Cube, new Vector3(bx + 0.062f + i * 0.0095f, by + 0.07f, zs - 0.003f),
                           new Vector3(0.006f, 0.133f, 0.006f), Plastic);
        }

        /// <summary>
        /// The board's rear I/O along its rear edge (<paramref name="edge"/>), below its top (<paramref name="top"/>):
        /// the port column facing out of the back of the case (see IoParts.RearColumn), and over its upper half the
        /// sculpted I/O cover modern boards have, with a brushed insert and an RGB line on the side you see. The cover
        /// stops short of the CPU power header above it.
        /// </summary>
        static void BuildRearIo(Transform parent, float edge, float top, float surface)
        {
            var size = IoParts.RearColumnSize;
            var column = IoParts.RearColumn(parent, "Rear I/O");
            column.localPosition = new Vector3(edge - 0.002f, top - 0.004f - size.y * 0.5f, surface - size.x * 0.5f - 0.0005f);
            column.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);   // ports face out of the back

            // The cover runs the column's full height, stopping 1.7 cm under the board's top edge (the CPU power header).
            var cover = Lit("M_IO_Cover", Hex("23272F"), 0.6f, 0.5f);
            float coverTop = top - 0.017f, coverBottom = top - 0.004f - size.y, height = coverTop - coverBottom;
            float front = surface - 0.0425f, x = edge + 0.0145f, y = (coverTop + coverBottom) * 0.5f;   // front: its open-side face
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(x, y, surface - 0.0215f), new Vector3(0.029f, height, 0.042f), cover);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(x + 0.001f, y, front - 0.001f), new Vector3(0.031f, height + 0.004f, 0.002f), Chip);
            Model.Mesh(parent, PrimitiveType.Cube, new Vector3(x - 0.002f, coverTop - 0.024f, front - 0.0024f), new Vector3(0.019f, 0.032f, 0.0008f), Metal);
            var line = Model.Mesh(parent, PrimitiveType.Cube, new Vector3(x + 0.0128f, y, front - 0.0024f), new Vector3(0.0015f, height - 0.01f, 0.0008f),
                                  Led("M_LED_RGB", new Color(0.05f, 0.05f, 0.05f), 40f));
            line.name = "I/O Cover Light";
            line.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            line.AddComponent<RgbGlow>();
        }

        /// <summary>
        /// Puts the built-in case back after an imported model didn't suit, rebuilding it inside the existing
        /// prefab so the scene's reference to it survives. Slot positions are returned to their authored values.
        /// </summary>
        [MenuItem("BuildAR/Setup/Restore Placeholder Assembly Case", priority = 29)]
        public static void RestorePlaceholderCase()
        {
            string path = $"{AssemblyPrefabs}/PH_AssemblyCase.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                EditorUtility.DisplayDialog("BuildAR", $"{path} doesn't exist.\n\nRun BuildAR ▸ Setup ▸ 4. Build Scenes first.", "OK");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visuals = contents.transform.Find("Visuals");
                if (visuals == null) { Debug.LogError("BuildAR: the case prefab has no 'Visuals' child."); return; }

                for (int i = visuals.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visuals.GetChild(i).gameObject);
                BuildCaseVisuals(visuals);

                // An imported case keeps the board in its own child; the built-in case has it under Visuals.
                var importedBoard = contents.transform.Find("Motherboard");
                if (importedBoard != null) Object.DestroyImmediate(importedBoard.gameObject);
                // Its RGB strip and light were placed for the imported case's shape. CaseGlow stays: the built-in
                // case's LEDs bloom with it too.
                var importedLighting = contents.transform.Find("Lighting");
                if (importedLighting != null) Object.DestroyImmediate(importedLighting.gameObject);

                // Undo whatever the imported case moved, so the slots line up with the built-in one again. The
                // built-in case has no grommets, so cables go back to arcing across the front.
                ComponentModelImporter.ResetLayout(contents);
                ComponentModelImporter.RemoveCableRoutes(contents);
                var fit = contents.GetComponent<AssemblyCaseFit>();
                if (fit != null) Object.DestroyImmediate(fit);

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                AssetDatabase.SaveAssets();
                Debug.Log("BuildAR: the built-in case is back (0.45 × 0.46 × 0.21 m, matte black M_Case) " +
                          "and every slot and port is back where it was authored.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static SnapSlot Slot(Transform parent, string id, ComponentCategory category, Vector3 pos, Quaternion rot, Vector3 highlightPos, Vector3 highlightSize)
        {
            var go = new GameObject("Slot_" + id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var slot = go.AddComponent<SnapSlot>();
            slot.slotId = id;
            slot.acceptsCategory = category;

            // World-aligned glow box on the board surface (a sibling, so the slot's rotation doesn't skew it).
            var hl = Model.Mesh(parent, PrimitiveType.Cube, highlightPos, highlightSize, HighlightMat);
            hl.name = "Highlight_" + id;
            var r = hl.GetComponent<Renderer>();
            r.sharedMaterial = HighlightMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.enabled = false;
            Set(slot, "highlight", r);
            return slot;
        }

        static void Port(Transform parent, string id, string displayName, string type, bool source, string requiresSlot, Vector3 pos, Vector3 size, Color cableColor)
        {
            var go = new GameObject("Port_" + id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.Max(size * 2.5f, Vector3.one * 0.035f);

            var visual = Model.Mesh(go.transform, PrimitiveType.Cube, Vector3.zero, size, Lit("M_Port_" + type, cableColor, 0f, 0.3f));
            visual.name = "Visual";
            var hl = Model.Mesh(go.transform, PrimitiveType.Cube, Vector3.zero, size * 1.8f + Vector3.one * 0.004f, HighlightMat);
            hl.name = "Highlight";
            var r = hl.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.enabled = false;

            var port = go.AddComponent<CablePort>();
            port.portId = id;
            port.displayName = displayName;
            port.connectorType = type;
            port.isSource = source;
            port.requiresSlotId = requiresSlot;
            port.cableColor = cableColor;
            Set(port, "highlight", r);
        }

        // ================================================================== utils

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void WriteTextIfMissing(string assetPath, string text)
        {
            if (File.Exists(assetPath)) return;
            File.WriteAllText(assetPath, text);
        }
    }
}
