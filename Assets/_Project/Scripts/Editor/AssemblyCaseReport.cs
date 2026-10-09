using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using BuildAR.Assembly;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// BuildAR ▸ Debug ▸ Report Assembly Case Layout.
    ///
    /// Prints the case's real measurements and where every slot and cable port sits inside it, so a case model
    /// whose interior is laid out differently can be corrected with numbers instead of guesswork.
    /// </summary>
    public static class AssemblyCaseReport
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Assembly/PH_AssemblyCase.prefab";

        [MenuItem("BuildAR/Debug/Report Assembly Case Layout", priority = 104)]
        public static void Report()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var root = contents.transform;
                var visuals = root.Find("Visuals");
                if (visuals == null) { Debug.LogError("BuildAR: no 'Visuals' child in the case prefab."); return; }

                var report = new StringBuilder("BuildAR assembly case layout\n");

                // --- the case itself, in the prefab root's space ---
                if (!TryBounds(root, visuals, out Bounds caseBounds, out int meshes))
                {
                    Debug.LogError("BuildAR: the case has no visible meshes.");
                    return;
                }

                report.AppendLine($"CASE ({meshes} visible meshes)");
                report.AppendLine($"  size   x {caseBounds.size.x:0.###}  y {caseBounds.size.y:0.###}  z {caseBounds.size.z:0.###}   (metres)");
                report.AppendLine($"  x from {caseBounds.min.x:0.###} to {caseBounds.max.x:0.###}");
                report.AppendLine($"  y from {caseBounds.min.y:0.###} to {caseBounds.max.y:0.###}   (0 = the floor the case stands on)");
                report.AppendLine($"  z from {caseBounds.min.z:0.###} to {caseBounds.max.z:0.###}   (-z = the side facing the learner)");

                var fit = contents.GetComponent<AssemblyCaseFit>();
                var board = root.Find("Slots");
                report.AppendLine(fit != null
                    ? $"  fit applied: board moved by {(board != null ? board.localPosition : Vector3.zero)}, " +
                      $"PSU by {fit.psuOffset}, front-panel cable by {fit.frontOffset}"
                    : "  fit applied: none — slots and ports are where they were authored");

                // --- where each anchor sits, and whether it is inside ---
                foreach (var group in new[] { "Slots", "CablePorts" })
                {
                    var parent = root.Find(group);
                    if (parent == null) continue;
                    report.AppendLine($"\n{group.ToUpperInvariant()}");

                    foreach (Transform child in parent.Cast<Transform>().OrderBy(t => t.name))
                    {
                        if (child.name.StartsWith("Highlight")) continue;
                        var p = root.InverseTransformPoint(child.position);
                        string verdict = Inside(caseBounds, p, out string axes) ? "inside" : "OUTSIDE (" + axes + ")";
                        report.AppendLine($"  {child.name,-22} x {p.x,7:0.###}  y {p.y,7:0.###}  z {p.z,7:0.###}   {verdict}");
                    }
                }

                report.AppendLine("\nPaste this whole block back if the parts are landing in the wrong place.");
                Debug.Log(report.ToString());
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static bool TryBounds(Transform root, Transform visuals, out Bounds bounds, out int meshes)
        {
            bounds = default;
            meshes = 0;
            foreach (var renderer in visuals.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.gameObject.activeInHierarchy) continue;
                var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var source = renderer.localBounds;
                var centre = matrix.MultiplyPoint3x4(source.center);
                var extents = matrix.MultiplyVector(source.extents);
                var one = new Bounds(centre, new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z)) * 2f);
                if (meshes == 0) bounds = one; else bounds.Encapsulate(one);
                meshes++;
            }
            return meshes > 0;
        }

        static bool Inside(Bounds b, Vector3 p, out string axes)
        {
            var outside = new System.Collections.Generic.List<string>();
            if (p.x < b.min.x || p.x > b.max.x) outside.Add("x");
            if (p.y < b.min.y || p.y > b.max.y) outside.Add("y");
            if (p.z < b.min.z || p.z > b.max.z) outside.Add("z");
            axes = string.Join("+", outside);
            return outside.Count == 0;
        }
    }
}
