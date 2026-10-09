using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// BuildAR ▸ Debug ▸ Check ML Model. Loads the ONNX in ML/Models, runs one inference on the CPU and reports
    /// what the scanner will make of it: input size, output layout, how many classes it predicts, which labels
    /// file matches that count, and which component each class maps to.
    ///
    /// It catches the three mistakes that are otherwise only visible on the phone: an input size that doesn't
    /// match the detector, a labels file with the wrong number of classes, and class names nothing maps to.
    /// </summary>
    public static class MlModelCheck
    {
        const string MlFolder = "Assets/_Project/ML/Models";

        [MenuItem("BuildAR/Debug/Check ML Model", priority = 102)]
        public static void Check()
        {
            var modelPath = AssetDatabase.FindAssets("t:ModelAsset", new[] { "Assets/_Project/ML" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault();

            if (modelPath == null)
            {
                Debug.LogWarning($"BuildAR: no .onnx found under Assets/_Project/ML. Put your exported model in {MlFolder}.");
                return;
            }

            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            var report = new StringBuilder();
            report.AppendLine($"BuildAR ML model check — {Path.GetFileName(modelPath)}");

            Model model;
            try { model = ModelLoader.Load(modelAsset); }
            catch (System.Exception e) { Debug.LogError($"BuildAR: couldn't load the model. {e.Message}"); return; }

            // ---------- input ----------
            int inputSize = 640;
            if (model.inputs.Count == 0) { Debug.LogError("BuildAR: the model has no inputs."); return; }
            var inputShape = model.inputs[0].shape;
            report.AppendLine($"  input  : {model.inputs[0].name} {inputShape}");

            TensorShape concrete;
            if (inputShape.IsStatic())
            {
                concrete = inputShape.ToTensorShape();
                if (concrete.rank == 4) inputSize = concrete[2];
            }
            else
            {
                concrete = new TensorShape(1, 3, inputSize, inputSize);
                report.AppendLine($"  note   : input size isn't fixed in the model; testing at {inputSize}.");
            }
            report.AppendLine($"  → set the detector's Input Size to {inputSize}");

            // ---------- one inference, to learn the output shape ----------
            TensorShape outShape;
            try
            {
                using var input = new Tensor<float>(concrete);
                using var worker = new Worker(model, BackendType.CPU);
                worker.Schedule(input);
                using var output = (worker.PeekOutput() as Tensor<float>).ReadbackAndClone();
                outShape = output.shape;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"BuildAR: the model loaded but failed to run. {e.Message}");
                return;
            }

            report.AppendLine($"  output : {outShape}");
            string layout;
            int classes;
            if (outShape.rank == 2) { layout = "Classifier"; classes = outShape[1]; }
            else if (outShape.rank != 3) { Debug.LogError($"BuildAR: unsupported output shape {outShape}."); return; }
            else if (outShape[2] == 6) { layout = "EndToEndNms"; classes = -1; }
            else if (outShape[1] < outShape[2]) { layout = "YoloV8 / YOLO11"; classes = outShape[1] - 4; }
            else { layout = "YoloV5"; classes = outShape[2] - 5; }

            report.AppendLine($"  layout : {layout} (leave Output Layout on Auto)");
            report.AppendLine(classes >= 0 ? $"  classes: {classes}" : "  classes: decided by the model's own NMS");

            // ---------- labels ----------
            var labelFiles = Directory.GetFiles(MlFolder, "*.txt").OrderBy(f => f).ToList();
            if (labelFiles.Count == 0) report.AppendLine("  labels : none found in ML/Models.");

            string best = null;
            foreach (var file in labelFiles)
            {
                var names = ReadLabels(file);
                bool match = classes < 0 || names.Count == classes;
                if (match && best == null) best = file;
                report.AppendLine($"  labels : {Path.GetFileName(file)} — {names.Count} class(es) {(match ? "✓ matches" : "✗ does not match the model")}");
            }

            if (best != null)
            {
                report.AppendLine($"  → set the detector's Labels File to {Path.GetFileName(best)}");
                report.AppendLine("  class → component (order must match data.yaml):");
                var components = LoadComponents();
                foreach (var (name, index) in ReadLabels(best).Select((n, i) => (n, i)))
                {
                    string key = MlLabelMap.Resolve(name);
                    string target = components.TryGetValue(key, out var def) ? def.displayName : "— ignored, no component";
                    report.AppendLine($"     {index,2}. {name,-16} → {target}");
                }
            }
            else if (labelFiles.Count > 0)
            {
                report.AppendLine("  → no labels file has the right number of classes. Copy the names from your data.yaml, " +
                                  "one per line, in the same order.");
            }

            Debug.Log(report.ToString());
        }

        static List<string> ReadLabels(string path) =>
            File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        /// <summary>Component assets indexed the way ComponentDatabase does it at runtime.</summary>
        static Dictionary<string, ComponentDefinitionSO> LoadComponents()
        {
            var map = new Dictionary<string, ComponentDefinitionSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinitionSO"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                Add(MlLabelMap.Normalise(def.mlLabel), def);
                if (def.mlAliases == null) continue;
                foreach (var alias in def.mlAliases) Add(MlLabelMap.Resolve(alias), def);
            }
            return map;

            void Add(string key, ComponentDefinitionSO def)
            {
                if (key.Length > 0 && !map.ContainsKey(key)) map.Add(key, def);
            }
        }
    }
}
