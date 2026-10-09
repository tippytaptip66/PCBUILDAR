using System;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Data;
using BuildAR.Managers;

namespace BuildAR.UI
{
    public static class UIUtil
    {
        /// <summary>Cropped logo in a Resources folder (Assets/_Project/Art/Branding/Resources/BuildAR_Logo.png).</summary>
        public const string LogoResource = "BuildAR_Logo";
        static Texture2D _logo;

        /// <summary>The app logo, or null until the PNG has been added.</summary>
        public static Texture2D BrandLogo => _logo != null ? _logo : (_logo = Resources.Load<Texture2D>(LogoResource));

        const string ClickSfxClass = "click-sfx";

        /// <summary>
        /// Call once per screen: applies the theme, strips Unity's default button theme (so BuildAR.uss fully
        /// controls buttons), wires the button tap sound, and pads the root for notches / gesture bars.
        /// </summary>
        public static void PrepareScreen(VisualElement root)
        {
            Theme.Apply(root);
            root.Query<Button>().ForEach(StripButtonTheme);
            root.Query<ScrollView>().ForEach(ScrollFx.Attach);
            root.pickingMode = PickingMode.Ignore;
            // One listener per screen covers every button, including ones built later in code.
            if (!root.ClassListContains(ClickSfxClass))
            {
                root.AddToClassList(ClickSfxClass);
                root.RegisterCallback<ClickEvent>(OnScreenClick, TrickleDown.TrickleDown);
            }
            var screen = root.Q(className: "screen");
            if (screen != null) ApplySafeArea(screen);
        }

        /// <summary>Tap sound for anything inside a Button (the icon or label is usually the event target).</summary>
        static void OnScreenClick(ClickEvent evt)
        {
            for (var el = evt.target as VisualElement; el != null; el = el.parent)
                if (el is Button) { PlayClick(); return; }
        }

        public static void PlayClick() => AudioManager.Instance?.PlayClick();

        /// <summary>Clickable with the tap sound, for tappable things that aren't Buttons (hotspots, chips, tiles).</summary>
        public static Clickable Tap(Action action) => new Clickable(() => { PlayClick(); action(); });

        public static void StripButtonTheme(Button b) => b.RemoveFromClassList(Button.ussClassName);

        public static Button MakeButton(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            StripButtonTheme(b);
            foreach (var c in classes) b.AddToClassList(c);
            return b;
        }

        public static void OnClick(VisualElement root, string name, Action action)
        {
            var el = root.Q(name);
            if (el == null) { Debug.LogWarning($"BuildAR UI: element '{name}' not found."); return; }
            // Buttons get their tap sound from OnScreenClick; anything else gets it from Tap.
            if (el is Button b) b.clicked += action;
            else el.AddManipulator(Tap(action));
        }

        public static void SetVisible(VisualElement el, bool visible)
        {
            if (el != null) el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement Box(params string[] classes)
        {
            var v = new VisualElement();
            foreach (var c in classes) v.AddToClassList(c);
            return v;
        }

        /// <summary>
        /// Pads an element clear of the notch and the gesture bar. A bar along the bottom (the nav, the lesson
        /// checklist) takes the bottom inset in its own .safe-inset strip instead, so it runs to the bottom edge
        /// in its own colour: padding the screen left a strip of page background under it, and the bar looked
        /// lifted off the bottom. When that bar is hidden, the screen is padded as usual.
        /// </summary>
        public static void ApplySafeArea(VisualElement el)
        {
            var insets = el.Query(className: "safe-inset").ToList();
            void Apply()
            {
                if (el.panel == null || Screen.height <= 0) return;
                float scale = el.panel.visualTree.layout.height / Screen.height;
                if (float.IsNaN(scale) || scale <= 0) return;
                var safe = Screen.safeArea;
                float top = (Screen.height - safe.yMax) * scale;
                float bottom = safe.yMin * scale;
                bool barTakesInset = insets.Exists(IsShown);
                float padBottom = barTakesInset ? 0f : bottom;
                if (Mathf.Abs(el.resolvedStyle.paddingTop - top) > 0.5f) el.style.paddingTop = top;
                if (Mathf.Abs(el.resolvedStyle.paddingBottom - padBottom) > 0.5f) el.style.paddingBottom = padBottom;
                foreach (var inset in insets)
                    if (Mathf.Abs(inset.resolvedStyle.height - bottom) > 0.5f) inset.style.height = bottom;
            }
            el.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            // Showing or hiding a bar moves the screen's children without resizing the screen itself.
            foreach (var child in el.Children()) child.RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }

        static bool IsShown(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        public static string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
        }

        /// <summary>Row used on Home and Progress: icon, name, bar, percent.</summary>
        public static VisualElement CategoryProgressRow(ComponentCategory category, float completion01)
        {
            int pct = Mathf.RoundToInt(completion01 * 100f);
            var row = Box("cat-row");
            var icon = Box("cat-icon");
            icon.Add(new LineIcon(category.IconName(), "icon-18", "icon-blue"));
            row.Add(icon);

            var mid = Box("cat-mid");
            var top = Box("cat-top");
            top.Add(Text(category.DisplayName(), "cat-name"));
            top.Add(Text($"{pct}%", "cat-pct"));
            mid.Add(top);
            var track = Box("progress-track");
            var fill = Box("progress-fill", pct >= 100 ? "progress-fill--green" : "progress-fill--blue");
            fill.style.width = Length.Percent(pct);
            track.Add(fill);
            mid.Add(track);
            row.Add(mid);
            return row;
        }

        /// <summary>Wires the shared BottomNav.uxml instance and highlights the active tab.</summary>
        public static void BindBottomNav(VisualElement root, string activeItem)
        {
            var nav = root.Q("bottom-nav");
            if (nav == null) return;

            nav.Query(className: "nav-item").ForEach(i => i.EnableInClassList("active", i.name == activeItem));

            OnClick(root, "nav-home", () => GameManager.Instance?.ShowScreen(AppScreen.Home));
            OnClick(root, "nav-learn", () => GameManager.Instance?.ShowScreen(AppScreen.Learn));
            OnClick(root, "nav-assembly", () => GameManager.Instance?.GoToVirtualAssembly());
            OnClick(root, "nav-progress", () => GameManager.Instance?.ShowScreen(AppScreen.Progress));
            OnClick(root, "nav-fab", () => GameManager.Instance?.GoToARScanner());
        }

        /// <summary>Short auto-hiding banner at the top of a screen (badge unlocks etc.).</summary>
        public static void Toast(VisualElement root, string title, string body, string icon = "trophy")
        {
            var screen = root.Q(className: "screen") ?? root;
            var toast = Box("toast");
            var iconBox = Box("toast-icon");
            iconBox.Add(new LineIcon(icon, "icon-20", "icon-white"));
            toast.Add(iconBox);
            var text = Box("toast-text");
            text.Add(Text(title, "toast-title"));
            text.Add(Text(body, "toast-body"));
            toast.Add(text);
            screen.Add(toast);
            toast.schedule.Execute(() => toast.AddToClassList("toast--visible")).StartingIn(30);
            toast.schedule.Execute(() => toast.RemoveFromClassList("toast--visible")).StartingIn(3000);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(3400);
        }
    }
}
