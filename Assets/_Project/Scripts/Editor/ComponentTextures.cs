using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// BuildAR ▸ Setup ▸ Apply Component Textures.
    ///
    /// The built-in parts are built from primitives, so without a surface they read as flat coloured boxes. This
    /// puts the generated textures in Art/Textures onto the shared materials: circuit board, brushed metal,
    /// painted steel, dark plastic and gold contacts. Every placeholder model uses those materials, so one run
    /// dresses the whole set — in the scanner, the 3D viewer and the PC builder.
    ///
    /// Regenerate or edit the textures with Tools/make-textures.ps1, then run this again.
    /// </summary>
    public static class ComponentTextures
    {
        const string Materials = "Assets/_Project/Art/Materials";
        const string Textures = "Assets/_Project/Art/Textures";

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");

        struct Skin
        {
            public string texture;
            public float smoothness;
            public float metallic;
            public Skin(string texture, float smoothness, float metallic)
            {
                this.texture = texture; this.smoothness = smoothness; this.metallic = metallic;
            }
        }

        static readonly Dictionary<string, Skin> Skins = new Dictionary<string, Skin>
        {
            { "M_PCB_Green",   new Skin("T_PCB_Green", 0.45f, 0f) },
            { "M_PCB_Black",   new Skin("T_PCB_Black", 0.42f, 0f) },
            { "M_PCB_Board",   new Skin("T_PCB_Board", 0.42f, 0f) },   // the assembly case's board
            { "M_Metal",       new Skin("T_Metal",     0.62f, 0.85f) },
            { "M_Case",        new Skin("T_Case",      0.42f, 0.30f) },
            { "M_DarkPlastic", new Skin("T_Plastic",   0.30f, 0f) },
            { "M_Chip",        new Skin("T_Plastic",   0.45f, 0f) },
            { "M_Gold",        new Skin("T_Gold",      0.70f, 1f) },
        };

        /// <summary>
        /// Materials whose colour matters — cables, copper, connectors, the accent blue. They get a near-white
        /// detail texture that multiplies with the colour, so the surface stops being flat without changing hue.
        /// </summary>
        static readonly string[] Detailed =
        {
            "M_Accent_Blue", "M_Copper", "M_AudioGreen", "M_Cable",
            "M_Port_ATX24", "M_Port_EPS8", "M_Port_FPANEL", "M_Port_SATA",
        };

        [MenuItem("BuildAR/Setup/Apply Component Textures", priority = 31)]
        public static void Apply()
        {
            var report = new StringBuilder("BuildAR: component textures\n");
            int done = 0;

            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/T_Surface.png");
            foreach (var name in Detailed)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/{name}.mat");
                if (material == null || detail == null) continue;
                material.SetTexture(BaseMap, detail);   // colour is left alone on purpose
                EditorUtility.SetDirty(material);
                report.AppendLine($"  ✓ {name,-16} → T_Surface (keeps its colour)");
                done++;
            }

            foreach (var pair in Skins)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/{pair.Key}.mat");
                if (material == null) { report.AppendLine($"  ? {pair.Key,-16} material not found"); continue; }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{pair.Value.texture}.png");
                if (texture == null)
                {
                    report.AppendLine($"  ? {pair.Key,-16} {pair.Value.texture}.png not found — run Tools/make-textures.ps1");
                    continue;
                }

                material.SetTexture(BaseMap, texture);
                // The texture carries the colour now; a tint on top would darken it twice over.
                if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, Color.white);
                if (material.HasProperty(Smoothness)) material.SetFloat(Smoothness, pair.Value.smoothness);
                if (material.HasProperty(Metallic)) material.SetFloat(Metallic, pair.Value.metallic);
                EditorUtility.SetDirty(material);

                report.AppendLine($"  ✓ {pair.Key,-16} → {pair.Value.texture}");
                done++;
            }

            AssetDatabase.SaveAssets();
            report.AppendLine($"\n{done} material(s) textured. Every built-in part uses these, so the change shows " +
                              "everywhere at once.");
            Debug.Log(report.ToString());
        }

        /// <summary>Puts the materials back to plain colours, keeping the look they had before texturing.</summary>
        [MenuItem("BuildAR/Debug/Remove Component Textures", priority = 105)]
        public static void Remove()
        {
            foreach (var name in Skins.Keys)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/{name}.mat");
                if (material == null) continue;
                material.SetTexture(BaseMap, null);
                EditorUtility.SetDirty(material);
            }
            foreach (var name in Detailed)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/{name}.mat");
                if (material == null) continue;
                material.SetTexture(BaseMap, null);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BuildAR: textures removed. Set each material's Base Color again if it now looks washed out.");
        }
    }
}
