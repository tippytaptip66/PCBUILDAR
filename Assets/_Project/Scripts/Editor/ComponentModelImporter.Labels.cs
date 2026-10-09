using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using BuildAR.Data;
using BuildAR.Viewer;

namespace BuildAR.EditorTools
{
    /// <summary>Part labels: the 3D viewer's tappable dots and the Parts tab's rows, see ModelLabel.</summary>
    public static partial class ComponentModelImporter
    {
        const string LabelPrefix = "Label_";

        /// <summary>
        /// Puts the part labels back on the imported models without re-importing them. Placeholder models were built
        /// with their labels; a downloaded model is one welded mesh with none, so each component lists its labels
        /// (ComponentDefinitionSO.modelLabels) and this turns them into ModelHotspots. Import Component Models does the
        /// same for every model it builds, so this is only needed for models imported before labels existed.
        /// </summary>
        [MenuItem("BuildAR/Setup/Label Model Parts", priority = 29)]
        public static void LabelModelParts()
        {
            var report = new StringBuilder("BuildAR: part labels on the imported models\n");
            int done = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinitionSO"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.modelLabels == null || def.modelLabels.Count == 0 || def.model3DPrefab == null) continue;
                string path = AssetDatabase.GetAssetPath(def.model3DPrefab);
                if (!path.StartsWith(PrefabFolder))
                {
                    report.AppendLine($"  - {def.displayName,-24} skipped: not an imported model ({path})");
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string note = AddLabels(contents, def.modelLabels);
                    if (note == null) { report.AppendLine($"  ? {def.displayName,-24} no model mesh found in {path}"); continue; }
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    report.AppendLine($"  ✓ {def.displayName,-24} {note}");
                    done++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            report.AppendLine($"\n{done} model(s) labeled. Open a part's 3D viewer and its Parts tab to see them.");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Adds a ModelHotspot for each label onto the model's own mesh object, in the mesh's space (so they follow
        /// whatever turn and scale the part was given), standing just off the surface and facing out of it. Labels
        /// from an earlier run are replaced. Returns a note for the report, or null when there's no model mesh.
        /// </summary>
        static string AddLabels(GameObject model, List<ModelLabel> labels)
        {
            if (labels == null || labels.Count == 0) return null;
            var filter = ModelMeshFilter(model, out Mesh original);
            if (filter == null) return null;

            foreach (var old in filter.transform.Cast<Transform>().Where(t => t.name.StartsWith(LabelPrefix)).ToList())
                Object.DestroyImmediate(old.gameObject);

            // The bounds the labels were measured against: the model file's own mesh. A part whose fans were cut out
            // wears a cut copy now, which can be a little smaller.
            var box = original.bounds;
            float big = Mathf.Max(box.size.x, box.size.y, box.size.z);
            foreach (var label in labels)
            {
                var facing = label.facing.sqrMagnitude > 1e-6f ? label.facing.normalized : Vector3.up;
                var spot = new GameObject(LabelPrefix + label.title).transform;
                spot.SetParent(filter.transform, false);
                spot.localPosition = box.min + Vector3.Scale(label.center, box.size) + facing * (0.008f * big);
                spot.localRotation = Quaternion.LookRotation(facing, Mathf.Abs(facing.y) < 0.99f ? Vector3.up : Vector3.forward);
                var hotspot = spot.gameObject.AddComponent<ModelHotspot>();
                hotspot.title = label.title;
                hotspot.description = label.description;
                hotspot.kind = label.kind;
                hotspot.dimWhenFacingAway = true;
            }
            return $"{labels.Count} part label{(labels.Count == 1 ? "" : "s")}";
        }

        /// <summary>
        /// The mesh that came from the model file, not one the importer added (fans, light bars, I/O panels), and that
        /// file's original mesh. Generated parts can have bigger bounds than a downloaded model's tiny mesh units, so
        /// "the largest mesh" isn't a safe test here.
        /// </summary>
        static MeshFilter ModelMeshFilter(GameObject model, out Mesh original)
        {
            original = null;
            MeshFilter best = null;
            float bestSize = -1f;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(filter);
                if (source == null || source.sharedMesh == null || source == filter) continue;
                string file = AssetDatabase.GetAssetPath(source).ToLowerInvariant();
                if (!ModelExtensions.Any(file.EndsWith)) continue;
                float size = source.sharedMesh.bounds.size.sqrMagnitude;
                if (size <= bestSize) continue;
                best = filter;
                bestSize = size;
                original = source.sharedMesh;
            }
            return best;
        }
    }
}
