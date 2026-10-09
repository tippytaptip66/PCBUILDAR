using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// Full-screen loading animation (logo + spinning ring) drawn above every screen. GameManager shows it while
    /// switching screens and loading scenes. Styled in code so it works before any screen stylesheet is loaded.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        const float MinVisibleSeconds = 0.35f;
        const int FadeMs = 180;

        public static LoadingOverlay Instance { get; private set; }

        UIDocument _doc;
        VisualElement _layer, _card;
        Label _label;
        bool _visible;
        float _shownAt;
        IVisualElementScheduledItem _pending;

        public static LoadingOverlay Create(Transform parent)
        {
            var go = new GameObject("LoadingOverlay");
            go.transform.SetParent(parent, false);
            return go.AddComponent<LoadingOverlay>();
        }

        void Awake()
        {
            Instance = this;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.sortingOrder = 1000;
            _doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsFallback.ResourceName);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        bool Build()
        {
            if (_layer != null && _layer.panel != null) return true;
            var root = _doc.rootVisualElement;
            if (root == null) return false;

            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;

            _layer = new VisualElement();
            var s = _layer.style;
            s.position = Position.Absolute;
            s.left = 0; s.top = 0; s.right = 0; s.bottom = 0;
            s.alignItems = Align.Center;
            s.justifyContent = Justify.Center;
            s.backgroundColor = new Color(0.02f, 0.075f, 0.196f, 0.55f);
            s.opacity = 0f;
            s.display = DisplayStyle.None;
            s.transitionProperty = new StyleList<StylePropertyName>(new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("opacity") });
            s.transitionDuration = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(FadeMs, TimeUnit.Millisecond) });

            var card = _card = new VisualElement();
            card.style.width = 112; card.style.height = 112;
            SetRadius(card, 30);
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.Center;

            var ring = new Spinner { color = new Color32(0x54, 0x56, 0xB6, 0xFF), thickness = 5f };
            ring.style.position = Position.Absolute;
            ring.style.width = 88; ring.style.height = 88;
            card.Add(ring);

            var logo = new VisualElement { pickingMode = PickingMode.Ignore };
            logo.style.width = 50; logo.style.height = 40;
            SetRadius(logo, 9);
            logo.style.backgroundColor = (Color)new Color32(0x54, 0x56, 0xB6, 0xFF);
            if (UIUtil.BrandLogo != null)
            {
                logo.style.backgroundImage = new StyleBackground(UIUtil.BrandLogo);
                logo.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            card.Add(logo);
            _layer.Add(card);

            _label = new Label("Loading…");
            _label.style.marginTop = 14;
            _label.style.color = Color.white;
            _label.style.fontSize = 14;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _layer.Add(_label);

            root.Add(_layer);
            return true;
        }

        static void SetRadius(VisualElement el, float r)
        {
            el.style.borderTopLeftRadius = r; el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r; el.style.borderBottomRightRadius = r;
        }

        public void Show(string message = null)
        {
            if (!Build()) return;
            // This overlay is styled in code, so it follows the theme by hand.
            _card.style.backgroundColor = Theme.IsDark ? (Color)new Color32(0x15, 0x1D, 0x2E, 0xFF) : Color.white;
            _label.text = string.IsNullOrEmpty(message) ? "Loading…" : message;
            _pending?.Pause();
            if (_visible) { _layer.style.opacity = 1f; return; }
            _visible = true;
            _shownAt = Time.realtimeSinceStartup;
            _layer.pickingMode = PickingMode.Position;
            _layer.style.display = DisplayStyle.Flex;
            _pending = _layer.schedule.Execute(() => _layer.style.opacity = 1f).StartingIn(16);
        }

        public void Hide()
        {
            if (!_visible || _layer == null) return;
            float wait = Mathf.Max(0f, MinVisibleSeconds - (Time.realtimeSinceStartup - _shownAt));
            _pending?.Pause();
            _pending = _layer.schedule.Execute(() =>
            {
                _visible = false;
                _layer.style.opacity = 0f;
                _layer.pickingMode = PickingMode.Ignore;
                _pending = _layer.schedule.Execute(() => { if (!_visible) _layer.style.display = DisplayStyle.None; }).StartingIn(FadeMs);
            }).StartingIn((long)(wait * 1000f));
        }
    }
}
