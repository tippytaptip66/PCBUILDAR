using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// Avatar pictures from Assets/_Project/Resources/Avatars (any PNG, shown in file-name order).
    /// Until that folder has images, coloured circles with a person icon are offered instead.
    /// </summary>
    public static class Avatars
    {
        public const string ResourceFolder = "Avatars";
        const string ColorPrefix = "color-";

        static readonly Color[] Colors =
        {
            new Color32(0x2F, 0x6B, 0xFF, 0xFF), new Color32(0x22, 0xD3, 0xEE, 0xFF), new Color32(0x16, 0xC4, 0x7F, 0xFF),
            new Color32(0xFF, 0x8A, 0x1F, 0xFF), new Color32(0x54, 0x56, 0xB6, 0xFF), new Color32(0xFA, 0x5A, 0x5F, 0xFF),
        };

        static Texture2D[] _images;

        public static IReadOnlyList<Texture2D> Images =>
            _images ?? (_images = Resources.LoadAll<Texture2D>(ResourceFolder).OrderBy(t => t.name, StringComparer.OrdinalIgnoreCase).ToArray());

        public static IEnumerable<string> Ids =>
            Images.Count > 0 ? Images.Select(t => t.name) : Enumerable.Range(0, Colors.Length).Select(i => ColorPrefix + i);

        public static string DefaultId => Ids.First();

        /// <summary>Shows avatar <paramref name="id"/> on an element (give it class "avatar-img" for the round crop).</summary>
        public static void Apply(VisualElement el, string id)
        {
            el.Clear();
            var tex = Images.FirstOrDefault(t => t.name == id);
            if (tex != null)
            {
                el.style.backgroundImage = new StyleBackground(tex);
                el.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                return;
            }

            int index = id != null && id.StartsWith(ColorPrefix) && int.TryParse(id.Substring(ColorPrefix.Length), out int n)
                ? n : Math.Abs((id ?? "").GetHashCode());
            el.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
            el.style.backgroundColor = Colors[index % Colors.Length];
            el.Add(new LineIcon("user", "avatar-img__icon"));
        }
    }
}
