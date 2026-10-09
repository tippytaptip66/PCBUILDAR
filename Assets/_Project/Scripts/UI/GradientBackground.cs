using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// The BuildAR logo's background gradient (light cyan → indigo → navy), drawn as a mesh because USS has no gradients.
    /// Class "brand-gradient" fills the parent; give the parent overflow: hidden to clip rounded corners.
    /// USS: --gradient-from / --gradient-to (0..1) show part of it, e.g. 0.45–1 behind white text.
    /// </summary>
    [UxmlElement]
    public partial class GradientBackground : VisualElement
    {
        static readonly CustomStyleProperty<float> FromProperty = new CustomStyleProperty<float>("--gradient-from");
        static readonly CustomStyleProperty<float> ToProperty = new CustomStyleProperty<float>("--gradient-to");

        // Sampled from Art/Branding/Resources/BuildAR_AppIcon.png.
        static readonly float[] StopPositions = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        static readonly Color32[] StopColors =
        {
            new Color32(0xB2, 0xF1, 0xFF, 0xFF),
            new Color32(0x83, 0xA4, 0xDA, 0xFF),
            new Color32(0x54, 0x56, 0xB6, 0xFF),
            new Color32(0x2D, 0x33, 0x75, 0xFF),
            new Color32(0x05, 0x13, 0x32, 0xFF),
        };

        float _from, _to = 1f;

        public GradientBackground()
        {
            AddToClassList("brand-gradient");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                _from = customStyle.TryGetValue(FromProperty, out var f) ? Mathf.Clamp01(f) : 0f;
                _to = customStyle.TryGetValue(ToProperty, out var t) ? Mathf.Clamp01(t) : 1f;
                MarkDirtyRepaint();
            });
        }

        static Color32 Sample(float t)
        {
            for (int i = 1; i < StopPositions.Length; i++)
                if (t <= StopPositions[i])
                    return Color32.Lerp(StopColors[i - 1], StopColors[i], Mathf.InverseLerp(StopPositions[i - 1], StopPositions[i], t));
            return StopColors[StopColors.Length - 1];
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0 || r.height <= 0 || _to <= _from) return;

            var rows = new List<float> { _from };
            foreach (var p in StopPositions) if (p > _from && p < _to) rows.Add(p);
            rows.Add(_to);

            var vertices = new Vertex[rows.Count * 2];
            var indices = new ushort[(rows.Count - 1) * 6];
            for (int i = 0; i < rows.Count; i++)
            {
                float y = r.yMin + r.height * (rows[i] - _from) / (_to - _from);
                var color = Sample(rows[i]);
                vertices[i * 2] = new Vertex { position = new Vector3(r.xMin, y, Vertex.nearZ), tint = color };
                vertices[i * 2 + 1] = new Vertex { position = new Vector3(r.xMax, y, Vertex.nearZ), tint = color };
            }
            for (int i = 0; i < rows.Count - 1; i++)
            {
                int k = i * 6, a = i * 2;
                indices[k] = (ushort)a; indices[k + 1] = (ushort)(a + 1); indices[k + 2] = (ushort)(a + 3);
                indices[k + 3] = (ushort)a; indices[k + 4] = (ushort)(a + 3); indices[k + 5] = (ushort)(a + 2);
            }

            var mesh = ctx.Allocate(vertices.Length, indices.Length);
            mesh.SetAllVertices(vertices);
            mesh.SetAllIndices(indices);
        }
    }
}
