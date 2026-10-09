using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// Thin line icons drawn with Painter2D on a 24x24 grid — no textures needed.
    /// USS: --icon-color and --icon-stroke must be set on the icon element itself (see .line-icon rules in BuildAR.uss).
    /// UXML: &lt;BuildAR.UI.LineIcon icon="cpu" class="icon-20" /&gt;
    /// </summary>
    [UxmlElement]
    public partial class LineIcon : VisualElement
    {
        static readonly CustomStyleProperty<Color> ColorProperty = new CustomStyleProperty<Color>("--icon-color");
        static readonly CustomStyleProperty<float> StrokeProperty = new CustomStyleProperty<float>("--icon-stroke");
        static readonly CustomStyleProperty<Color> FillProperty = new CustomStyleProperty<Color>("--icon-fill");

        string _icon = "cube";
        Color _color = Theme.IconInk;
        Color _fill = Color.clear;
        float _stroke = 1.8f;
        /// <summary>True when a USS rule gives this icon its own colour; otherwise it follows the theme.</summary>
        bool _themedColor = true;

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> Images = new System.Collections.Generic.Dictionary<string, Texture2D>();
        bool _hasImage;

        [UxmlAttribute]
        public string icon
        {
            get => _icon;
            set { _icon = value; ApplyImage(); MarkDirtyRepaint(); }
        }

        /// <summary>A full-colour PNG at Resources/Icons/&lt;icon&gt;.png replaces the drawn icon (--icon-color is then ignored).</summary>
        void ApplyImage()
        {
            string key = _icon ?? "";
            if (!Images.TryGetValue(key, out var tex))
            {
                tex = Resources.Load<Texture2D>("Icons/" + key);
                Images[key] = tex;
            }
            _hasImage = tex != null;
            style.backgroundImage = _hasImage ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.Null);
            EnableInClassList("line-icon--image", _hasImage);
        }

        public LineIcon()
        {
            AddToClassList("line-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                _themedColor = !customStyle.TryGetValue(ColorProperty, out var c);
                _color = _themedColor ? Theme.IconInk : c;
                if (customStyle.TryGetValue(StrokeProperty, out var s)) _stroke = s;
                _fill = customStyle.TryGetValue(FillProperty, out var f) ? f : Color.clear;
                MarkDirtyRepaint();
            });
        }

        public LineIcon(string iconName, params string[] classes) : this()
        {
            icon = iconName;
            foreach (var c in classes) AddToClassList(c);
        }

        /// <summary>Called by Theme after a light/dark switch, for icons that don't set their own colour.</summary>
        public void RefreshTheme()
        {
            if (!_themedColor) return;
            _color = Theme.IconInk;
            MarkDirtyRepaint();
        }

        Painter2D _p;
        float _s, _ox, _oy;

        Vector2 V(float x, float y) => new Vector2(_ox + x * _s, _oy + y * _s);

        void Line(float x1, float y1, float x2, float y2)
        {
            _p.BeginPath(); _p.MoveTo(V(x1, y1)); _p.LineTo(V(x2, y2)); _p.Stroke();
        }

        void Dot(float x, float y) => Line(x, y, x + 0.05f, y);

        void Poly(bool close, params float[] pts)
        {
            _p.BeginPath();
            _p.MoveTo(V(pts[0], pts[1]));
            for (int i = 2; i < pts.Length; i += 2) _p.LineTo(V(pts[i], pts[i + 1]));
            if (close) _p.ClosePath();
            _p.Stroke();
        }

        void Circle(float cx, float cy, float r) => Arc(cx, cy, r, 0, 360);

        void Arc(float cx, float cy, float r, float fromDeg, float toDeg)
        {
            _p.BeginPath();
            _p.Arc(V(cx, cy), r * _s, Angle.Degrees(fromDeg), Angle.Degrees(toDeg));
            _p.Stroke();
        }

        void RRect(float x, float y, float w, float h, float r)
        {
            _p.BeginPath();
            _p.MoveTo(V(x + r, y));
            _p.LineTo(V(x + w - r, y));
            _p.ArcTo(V(x + w, y), V(x + w, y + r), r * _s);
            _p.LineTo(V(x + w, y + h - r));
            _p.ArcTo(V(x + w, y + h), V(x + w - r, y + h), r * _s);
            _p.LineTo(V(x + r, y + h));
            _p.ArcTo(V(x, y + h), V(x, y + h - r), r * _s);
            _p.LineTo(V(x, y + r));
            _p.ArcTo(V(x, y), V(x + r, y), r * _s);
            _p.ClosePath();
            _p.Stroke();
        }

        void Curve(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            _p.BeginPath(); _p.MoveTo(V(x0, y0)); _p.BezierCurveTo(V(x1, y1), V(x2, y2), V(x3, y3)); _p.Stroke();
        }

        /// <summary>Closed path from cubic segments (6 numbers each), filled with --icon-fill when set.</summary>
        void Shape(float x0, float y0, params float[] segments)
        {
            _p.BeginPath();
            _p.MoveTo(V(x0, y0));
            for (int i = 0; i + 5 < segments.Length; i += 6)
                _p.BezierCurveTo(V(segments[i], segments[i + 1]), V(segments[i + 2], segments[i + 3]), V(segments[i + 4], segments[i + 5]));
            _p.ClosePath();
            if (_fill.a > 0.01f) { _p.fillColor = _fill; _p.Fill(); }
            _p.Stroke();
        }

        void FilledCircle(float cx, float cy, float r)
        {
            _p.BeginPath();
            _p.Arc(V(cx, cy), r * _s, Angle.Degrees(0), Angle.Degrees(360));
            _p.ClosePath();
            if (_fill.a > 0.01f) { _p.fillColor = _fill; _p.Fill(); }
            _p.Stroke();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (_hasImage || r.width <= 0 || r.height <= 0) return;

            _p = ctx.painter2D;
            _s = Mathf.Min(r.width, r.height) / 24f;
            _ox = r.x + (r.width - 24f * _s) * 0.5f;
            _oy = r.y + (r.height - 24f * _s) * 0.5f;
            _p.strokeColor = _color;
            _p.lineWidth = Mathf.Max(1f, _stroke * _s);
            _p.lineCap = LineCap.Round;
            _p.lineJoin = LineJoin.Round;

            switch (_icon)
            {
                case "home":
                    Poly(false, 3, 11, 12, 3.5f, 21, 11);
                    Poly(false, 5.5f, 9.5f, 5.5f, 20.5f, 18.5f, 20.5f, 18.5f, 9.5f);
                    Poly(false, 10, 20.5f, 10, 15, 14, 15, 14, 20.5f);
                    break;
                case "book":
                    Line(12, 6.5f, 12, 20);
                    Poly(true, 12, 6.5f, 8, 5, 3, 5, 3, 18.5f, 8, 18.5f, 12, 20);
                    Poly(true, 12, 6.5f, 16, 5, 21, 5, 21, 18.5f, 16, 18.5f, 12, 20);
                    break;
                case "scan":
                    Poly(false, 3, 8, 3, 3, 8, 3); Poly(false, 16, 3, 21, 3, 21, 8);
                    Poly(false, 21, 16, 21, 21, 16, 21); Poly(false, 8, 21, 3, 21, 3, 16);
                    Line(6.5f, 12, 17.5f, 12);
                    break;
                case "camera":
                    RRect(3, 7, 18, 13, 2.5f);
                    Poly(false, 8, 7, 9.5f, 4.5f, 14.5f, 4.5f, 16, 7);
                    Circle(12, 13.5f, 3.5f);
                    break;
                case "chart":
                    Line(4, 20.5f, 20, 20.5f);
                    Line(7, 17, 7, 12); Line(12, 17, 12, 6); Line(17, 17, 17, 9.5f);
                    break;
                case "user":
                    Circle(12, 8.5f, 4);
                    Curve(4.5f, 20.5f, 5.5f, 14.5f, 18.5f, 14.5f, 19.5f, 20.5f);
                    break;
                case "cpu":
                    RRect(6, 6, 12, 12, 1.5f); RRect(9.5f, 9.5f, 5, 5, 0.6f);
                    foreach (var k in new[] { 9f, 12f, 15f })
                    { Line(k, 2.5f, k, 6); Line(k, 18, k, 21.5f); Line(2.5f, k, 6, k); Line(18, k, 21.5f, k); }
                    break;
                case "ram":
                    RRect(2.5f, 6, 19, 9, 1);
                    RRect(5, 8.5f, 3, 4, 0.4f); RRect(10.5f, 8.5f, 3, 4, 0.4f); RRect(16, 8.5f, 3, 4, 0.4f);
                    Line(4, 18, 11, 18); Line(13, 18, 20, 18);
                    break;
                case "motherboard":
                    RRect(3, 3, 18, 18, 2); RRect(6, 6, 5, 5, 0.6f);
                    Line(14, 6.5f, 18, 6.5f); Line(14, 9.5f, 18, 9.5f);
                    Line(6, 14.5f, 18, 14.5f); Line(6, 17.5f, 13, 17.5f);
                    break;
                case "storage":
                    RRect(3, 6, 18, 12, 2);
                    Line(6.5f, 10, 14, 10); Dot(17, 14);
                    break;
                case "gpu":
                    RRect(2.5f, 6, 17, 11, 1.5f);
                    Circle(7.5f, 11.5f, 2.8f); Circle(14.5f, 11.5f, 2.8f);
                    Poly(false, 19.5f, 8, 21.5f, 8, 21.5f, 15);
                    Line(5, 17, 5, 20); Line(9, 17, 9, 19);
                    break;
                case "psu":
                    RRect(3, 6, 18, 13, 1.5f); Circle(10, 12.5f, 3.5f);
                    Poly(false, 17.5f, 9, 15.5f, 12.5f, 17.5f, 12.5f, 15.5f, 16);
                    break;
                case "bolt":
                    _p.BeginPath();
                    _p.MoveTo(V(13, 2.5f));
                    foreach (var pt in new[] { new Vector2(5, 13.5f), new Vector2(11, 13.5f), new Vector2(10, 21.5f), new Vector2(19, 10), new Vector2(13, 10) })
                        _p.LineTo(V(pt.x, pt.y));
                    _p.ClosePath();
                    if (_fill.a > 0.01f) { _p.fillColor = _fill; _p.Fill(); }
                    _p.Stroke();
                    break;
                case "fan":
                    Circle(12, 12, 9); Circle(12, 12, 1.6f);
                    for (int i = 0; i < 3; i++) Blade(i * 120f);
                    break;
                case "case":
                    RRect(6, 2.5f, 12, 19, 1.5f); Circle(12, 6.5f, 1.3f);
                    Line(9, 11, 15, 11); Line(9, 13.5f, 15, 13.5f); Line(9, 16, 15, 16);
                    break;
                case "cable":
                    RRect(13.5f, 3.5f, 7, 6, 1);
                    Line(15.5f, 1.5f, 15.5f, 3.5f); Line(18.5f, 1.5f, 18.5f, 3.5f);
                    Curve(17, 9.5f, 17, 17, 7, 11, 7, 18.5f);
                    Line(7, 18.5f, 7, 22);
                    break;
                case "keyboard":
                    RRect(2.5f, 6.5f, 19, 11, 2);
                    foreach (var k in new[] { 6f, 9f, 12f, 15f, 18f }) { Dot(k, 10); Dot(k, 12.8f); }
                    Line(8, 15, 16, 15);
                    break;
                case "quiz":
                    Circle(12, 12, 9);
                    Curve(9.5f, 9.5f, 9.5f, 6.3f, 14.5f, 6.3f, 14.5f, 9.5f);
                    Curve(14.5f, 9.5f, 14.5f, 11.8f, 12, 11.5f, 12, 14);
                    Dot(12, 17);
                    break;
                case "wrench":
                    Circle(16, 8, 4.2f);
                    Line(13, 11, 4.5f, 19.5f);
                    break;
                case "check":
                    Poly(false, 5, 12.5f, 10, 17.5f, 19, 7);
                    break;
                case "check-circle":
                    Circle(12, 12, 9); Poly(false, 8, 12.5f, 11, 15.5f, 16.5f, 9);
                    break;
                case "lock":
                    RRect(5, 10.5f, 14, 10, 2);
                    Curve(8, 10.5f, 8, 3, 16, 3, 16, 10.5f);
                    Line(12, 14.5f, 12, 16.5f);
                    break;
                case "play":
                    Poly(true, 8, 5, 19, 12, 8, 19);
                    break;
                case "pause":
                    Line(9, 5, 9, 19); Line(15, 5, 15, 19);
                    break;
                case "chevron-left":
                    Poly(false, 15, 5, 8, 12, 15, 19);
                    break;
                case "chevron-right":
                    Poly(false, 9, 5, 16, 12, 9, 19);
                    break;
                case "arrow-right":
                    Line(4, 12, 20, 12); Poly(false, 14, 6, 20, 12, 14, 18);
                    break;
                case "close":
                    Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                    break;
                case "info":
                    Circle(12, 12, 9); Line(12, 11, 12, 16.5f); Dot(12, 7.5f);
                    break;
                case "tag":
                    Poly(true, 3, 3, 11, 3, 21, 13, 13, 21, 3, 11); Circle(7.5f, 7.5f, 1.3f);
                    break;
                case "layers":
                    Poly(true, 12, 3, 21, 8, 12, 13, 3, 8);
                    Poly(false, 3, 12, 12, 17, 21, 12);
                    Poly(false, 3, 16, 12, 21, 21, 16);
                    break;
                case "rotate":
                    Arc(12, 12, 8, -40, 230);
                    Poly(false, 3.2f, 6.8f, 6.9f, 5.9f, 7.6f, 9.6f);
                    break;
                case "rotate-left":
                    Arc(12, 13, 7.5f, 180, 450);
                    Poly(false, 1.8f, 10, 4.5f, 13, 7.5f, 10.5f);
                    break;
                case "rotate-right":
                    Arc(12, 13, 7.5f, 90, 360);
                    Poly(false, 16.5f, 10.5f, 19.5f, 13, 22.2f, 10);
                    break;
                case "zoom-in":
                    Circle(10.5f, 10.5f, 6.5f); Line(15.5f, 15.5f, 20.5f, 20.5f);
                    Line(8, 10.5f, 13, 10.5f); Line(10.5f, 8, 10.5f, 13);
                    break;
                case "zoom-out":
                    Circle(10.5f, 10.5f, 6.5f); Line(15.5f, 15.5f, 20.5f, 20.5f);
                    Line(8, 10.5f, 13, 10.5f);
                    break;
                case "shield":
                    _p.BeginPath();
                    _p.MoveTo(V(12, 3)); _p.LineTo(V(19.5f, 6)); _p.LineTo(V(19.5f, 11.5f));
                    _p.BezierCurveTo(V(19.5f, 16.5f), V(16, 19.5f), V(12, 21));
                    _p.BezierCurveTo(V(8, 19.5f), V(4.5f, 16.5f), V(4.5f, 11.5f));
                    _p.LineTo(V(4.5f, 6)); _p.ClosePath(); _p.Stroke();
                    Poly(false, 8.5f, 12, 11, 14.5f, 15.5f, 9.5f);
                    break;
                case "trophy":
                    Circle(12, 9, 6);
                    Poly(false, 8.5f, 13.8f, 7, 21, 12, 18.5f, 17, 21, 15.5f, 13.8f);
                    break;
                case "clock":
                    Circle(12, 12, 9); Poly(false, 12, 7, 12, 12, 15.5f, 14);
                    break;
                case "warning":
                    Poly(true, 12, 3.5f, 21.5f, 20, 2.5f, 20); Line(12, 9.5f, 12, 14); Dot(12, 17);
                    break;
                case "star":
                    Poly(true, 12, 3, 14.6f, 9, 21, 9.5f, 16, 13.8f, 17.6f, 20.5f, 12, 17, 6.4f, 20.5f, 8, 13.8f, 3, 9.5f, 9.4f, 9);
                    break;
                case "compat":
                    Circle(9, 12, 6); Circle(15, 12, 6);
                    break;
                case "list":
                    Line(9, 6, 20, 6); Line(9, 12, 20, 12); Line(9, 18, 20, 18);
                    Dot(4.5f, 6); Dot(4.5f, 12); Dot(4.5f, 18);
                    break;
                case "eye":
                    Curve(2.5f, 12, 6, 5.5f, 18, 5.5f, 21.5f, 12);
                    Curve(21.5f, 12, 18, 18.5f, 6, 18.5f, 2.5f, 12);
                    Circle(12, 12, 3);
                    break;
                case "power":
                    Arc(12, 13, 8, -60, 240); Line(12, 3, 12, 11);
                    break;
                case "hand":
                    Curve(8, 13, 8, 8, 8, 5, 9.5f, 5);
                    Curve(9.5f, 5, 11, 5, 11, 7, 11, 11);
                    Curve(11, 8, 11, 6, 12.5f, 6, 14, 7);
                    Curve(14, 7, 14, 9, 14, 10, 14, 11.5f);
                    Curve(14, 9, 15.5f, 8, 17, 9, 17, 12);
                    Curve(17, 12, 17, 18, 15, 21, 11.5f, 21);
                    Curve(11.5f, 21, 8, 21, 6, 17, 5, 14);
                    break;
                case "refresh":
                    Arc(12, 12, 8, -60, 210);
                    Poly(false, 16, 2.5f, 16.2f, 5.6f, 19.3f, 5.4f);
                    break;
                case "heart":
                    Shape(12, 20.5f,
                        6, 16.5f, 2.8f, 12.8f, 2.8f, 8.8f,
                        2.8f, 5.8f, 5.2f, 3.6f, 7.9f, 3.6f,
                        9.7f, 3.6f, 11.1f, 4.6f, 12, 6.1f,
                        12.9f, 4.6f, 14.3f, 3.6f, 16.1f, 3.6f,
                        18.8f, 3.6f, 21.2f, 5.8f, 21.2f, 8.8f,
                        21.2f, 12.8f, 18, 16.5f, 12, 20.5f);
                    break;
                case "flame":
                    Shape(12, 21.5f,
                        8, 21.5f, 5, 18.8f, 5, 14.8f,
                        5, 11.2f, 7.6f, 9.4f, 8.6f, 6.2f,
                        10, 7.2f, 10.8f, 8.6f, 10.8f, 10.4f,
                        12.6f, 9, 13.6f, 6.2f, 13.1f, 2.5f,
                        16.8f, 4.8f, 19, 9.2f, 19, 14.2f,
                        19, 18.6f, 16, 21.5f, 12, 21.5f);
                    break;
                case "coin":
                    FilledCircle(12, 12, 9);
                    Circle(12, 12, 5.5f);
                    break;
                case "bulb":
                    Shape(9, 16.5f,
                        9, 14.8f, 5.8f, 13, 5.8f, 9.6f,
                        5.8f, 6.2f, 8.6f, 3.4f, 12, 3.4f,
                        15.4f, 3.4f, 18.2f, 6.2f, 18.2f, 9.6f,
                        18.2f, 13, 15, 14.8f, 15, 16.5f);
                    Line(9.5f, 19.5f, 14.5f, 19.5f);
                    break;
                case "gear":
                    Circle(12, 12, 3);
                    Circle(12, 12, 6.8f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        Line(12 + c * 6.8f, 12 + s * 6.8f, 12 + c * 9.4f, 12 + s * 9.4f);
                    }
                    break;
                case "snowflake":
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (90f + i * 60f) * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        Line(12 - c * 9, 12 - s * 9, 12 + c * 9, 12 + s * 9);
                        foreach (var sign in new[] { -1f, 1f })
                        {
                            float ex = 12 + sign * c * 9, ey = 12 + sign * s * 9, bx = 12 + sign * c * 5.5f, by = 12 + sign * s * 5.5f;
                            float px = -s * 2.6f, py = c * 2.6f;
                            Poly(false, bx + px, by + py, ex - sign * c * 1.5f, ey - sign * s * 1.5f, bx - px, by - py);
                        }
                    }
                    break;
                case "timer":
                    Circle(12, 13.5f, 7.5f);
                    Poly(false, 12, 9.5f, 12, 13.5f, 14.5f, 15.5f);
                    Line(10, 2.8f, 14, 2.8f); Line(12, 2.8f, 12, 6);
                    break;
                case "cards":
                    RRect(3, 8, 13, 13, 2);
                    Poly(false, 7.5f, 8, 7.5f, 5.5f, 9.5f, 3.5f, 19, 3.5f, 21, 5.5f, 21, 14.5f, 19, 16.5f, 16, 16.5f);
                    break;
                case "swap":
                    Line(4, 8, 20, 8); Poly(false, 16, 4, 20, 8, 16, 12);
                    Line(20, 16, 4, 16); Poly(false, 8, 12, 4, 16, 8, 20);
                    break;
                case "sort":
                    Line(10, 6, 20, 6); Line(10, 12, 17, 12); Line(10, 18, 14, 18);
                    Line(5, 4, 5, 20); Poly(false, 2.5f, 17.5f, 5, 20, 7.5f, 17.5f);
                    break;
                case "bag":
                    RRect(4, 8, 16, 13, 2.5f);
                    Curve(8.5f, 8, 8.5f, 3, 15.5f, 3, 15.5f, 8);
                    break;
                case "moon": // crescent: circle (12,12,r9) with a bite taken out by (17,7,r9.2)
                    Shape(20.25f, 15.6f,
                        18.64f, 19.29f, 14.77f, 21.47f, 10.77f, 20.92f,
                        6.78f, 20.37f, 3.63f, 17.22f, 3.08f, 13.23f,
                        2.53f, 9.24f, 4.71f, 5.36f, 8.4f, 3.75f,
                        7.11f, 7.13f, 7.94f, 10.95f, 10.5f, 13.51f,
                        13.06f, 16.07f, 16.87f, 16.89f, 20.25f, 15.6f);
                    break;
                case "image":
                    RRect(3, 5, 18, 14, 2);
                    Circle(8, 9.5f, 1.6f);
                    Poly(false, 4, 17, 10, 11.5f, 14, 15, 17, 12.5f, 20, 15.5f);
                    break;
                case "music":
                    Circle(7, 17.3f, 3);
                    Line(10, 17.3f, 10, 4.5f);
                    Curve(10, 4.5f, 13.5f, 5.2f, 16.5f, 6.6f, 18, 9.2f);
                    break;
                case "music-off":
                    Circle(7, 17.3f, 3);
                    Line(10, 17.3f, 10, 4.5f);
                    Curve(10, 4.5f, 13.5f, 5.2f, 16.5f, 6.6f, 18, 9.2f);
                    Line(3.5f, 3.5f, 20.5f, 20.5f);
                    break;
                case "sun":
                    FilledCircle(12, 12, 4.6f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        Line(12 + c * 7.5f, 12 + s * 7.5f, 12 + c * 9.6f, 12 + s * 9.6f);
                    }
                    break;
                default: // cube
                    Poly(true, 12, 3, 20, 7.5f, 20, 16.5f, 12, 21, 4, 16.5f, 4, 7.5f);
                    Poly(false, 4, 7.5f, 12, 12, 20, 7.5f);
                    Line(12, 12, 12, 21);
                    break;
            }
        }

        void Blade(float deg)
        {
            float rad = deg * Mathf.Deg2Rad, c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 R(float x, float y) { x -= 12; y -= 12; return V(12 + x * c - y * s, 12 + x * s + y * c); }
            _p.BeginPath();
            _p.MoveTo(R(12, 10.4f));
            _p.BezierCurveTo(R(9.5f, 8), R(10, 4.5f), R(13.5f, 4.2f));
            _p.Stroke();
        }
    }
}
