using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Managers;

namespace BuildAR.UI
{
    /// <summary>
    /// Light / dark appearance. Every screen root gets the class "theme", plus "theme-dark" while dark mode is on.
    /// BuildAR.uss redefines its colour tokens under .theme.theme-dark (two classes, so they outweigh :root),
    /// which means screens and controllers need no theme-specific code of their own.
    /// The choice is stored in the save file (SaveData.darkMode) so the app reopens the same way.
    /// </summary>
    public static class Theme
    {
        public const string BaseClass = "theme";
        public const string DarkClass = "theme-dark";

        /// <summary>Roots currently on screen. Dead ones (screen disabled) are dropped as we go.</summary>
        static readonly List<VisualElement> Roots = new List<VisualElement>();

        /// <summary>Raised after the theme changes, for anything that picks its colours or icons in code.</summary>
        public static event Action Changed;

        public static bool IsDark => ProgressManager.Instance != null && ProgressManager.Instance.Data.darkMode;

        /// <summary>Icon for a button that switches to the other theme.</summary>
        public static string ToggleIcon => IsDark ? "sun" : "moon";

        /// <summary>Default icon colour, matching --icon-color in BuildAR.uss (LineIcon falls back to this).</summary>
        public static Color IconInk => IsDark ? new Color32(0xD3, 0xDC, 0xEE, 0xFF) : new Color32(0x0B, 0x15, 0x33, 0xFF);

        /// <summary>Called by UIUtil.PrepareScreen for every screen. Safe to call again on the same root.</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            Prune();
            if (!Roots.Contains(root)) Roots.Add(root);
            Paint(root);
        }

        public static void Toggle() => Set(!IsDark);

        public static void Set(bool dark)
        {
            var progress = ProgressManager.Instance;
            if (progress == null || progress.Data.darkMode == dark) return;
            progress.SetDarkMode(dark);
            Prune();
            foreach (var root in Roots) Paint(root);
            Changed?.Invoke();
        }

        static void Paint(VisualElement root)
        {
            root.AddToClassList(BaseClass);
            root.EnableInClassList(DarkClass, IsDark);
            root.Query<LineIcon>().ForEach(i => i.RefreshTheme());
        }

        static void Prune() => Roots.RemoveAll(r => r == null || r.panel == null);
    }
}
