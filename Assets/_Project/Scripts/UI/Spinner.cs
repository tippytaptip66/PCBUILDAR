using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>Animated loading arc. USS: --spinner-color, --spinner-thickness.</summary>
    [UxmlElement]
    public partial class Spinner : VisualElement
    {
        static readonly CustomStyleProperty<Color> ColorProperty = new CustomStyleProperty<Color>("--spinner-color");
        static readonly CustomStyleProperty<float> ThicknessProperty = new CustomStyleProperty<float>("--spinner-thickness");

        Color _color = new Color(0.13f, 0.83f, 0.93f);
        float _thickness = 2.5f;
        float _angle;
        IVisualElementScheduledItem _tick;

        public Color color { get => _color; set { _color = value; MarkDirtyRepaint(); } }
        public float thickness { get => _thickness; set { _thickness = value; MarkDirtyRepaint(); } }

        public Spinner()
        {
            AddToClassList("spinner");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                if (customStyle.TryGetValue(ColorProperty, out var c)) _color = c;
                if (customStyle.TryGetValue(ThicknessProperty, out var t)) _thickness = t;
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
                _tick = schedule.Execute(() => { _angle = (_angle + 9f) % 360f; MarkDirtyRepaint(); }).Every(16));
            RegisterCallback<DetachFromPanelEvent>(_ => _tick?.Pause());
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - _thickness;
            if (radius <= 0) return;
            var p = ctx.painter2D;
            p.strokeColor = _color;
            p.lineWidth = _thickness;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(r.center, radius, Angle.Degrees(_angle), Angle.Degrees(_angle + 270f));
            p.Stroke();
        }
    }
}
