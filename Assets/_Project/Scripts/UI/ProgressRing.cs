using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// Circular progress indicator. USS: --ring-track, --ring-fill, --ring-thickness (set on the ring element).
    /// Put a Label inside it for the percentage text.
    /// </summary>
    [UxmlElement]
    public partial class ProgressRing : VisualElement
    {
        static readonly CustomStyleProperty<Color> TrackProperty = new CustomStyleProperty<Color>("--ring-track");
        static readonly CustomStyleProperty<Color> FillProperty = new CustomStyleProperty<Color>("--ring-fill");
        static readonly CustomStyleProperty<float> ThicknessProperty = new CustomStyleProperty<float>("--ring-thickness");

        float _progress;
        Color _track = new Color(0.89f, 0.91f, 0.95f);
        Color _fill = new Color(0.18f, 0.42f, 1f);
        float _thickness = 5f;

        /// <summary>0..100</summary>
        [UxmlAttribute]
        public float progress
        {
            get => _progress;
            set { _progress = Mathf.Clamp(value, 0f, 100f); MarkDirtyRepaint(); }
        }

        public ProgressRing()
        {
            AddToClassList("progress-ring");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                if (customStyle.TryGetValue(TrackProperty, out var t)) _track = t;
                if (customStyle.TryGetValue(FillProperty, out var f)) _fill = f;
                if (customStyle.TryGetValue(ThicknessProperty, out var th)) _thickness = th;
                MarkDirtyRepaint();
            });
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - _thickness * 0.5f;
            if (radius <= 0) return;
            var center = r.center;
            var p = ctx.painter2D;
            p.lineWidth = _thickness;
            p.lineCap = LineCap.Round;

            p.strokeColor = _track;
            p.BeginPath();
            p.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360));
            p.Stroke();

            if (_progress <= 0.01f) return;
            p.strokeColor = _fill;
            p.BeginPath();
            p.Arc(center, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360f * _progress / 100f));
            p.Stroke();
        }
    }
}
