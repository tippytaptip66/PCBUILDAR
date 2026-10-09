using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>Party-popper confetti: two bursts from the bottom corners that arc up and fall. Removes itself when done.</summary>
    public static class Confetti
    {
        const float Gravity = 1500f;
        const float Seconds = 2.6f;

        static readonly Color[] Palette =
        {
            new Color32(0x22, 0xD3, 0xEE, 0xFF), new Color32(0x2F, 0x6B, 0xFF, 0xFF), new Color32(0xFF, 0x8A, 0x1F, 0xFF),
            new Color32(0x16, 0xC4, 0x7F, 0xFF), new Color32(0xFF, 0xC5, 0x3D, 0xFF), new Color32(0xFA, 0x5A, 0x5F, 0xFF),
            new Color32(0xB2, 0xF1, 0xFF, 0xFF), new Color32(0x54, 0x56, 0xB6, 0xFF),
        };

        class Piece
        {
            public VisualElement el;
            public float x, y, vx, vy, angle, spin, flutter;
        }

        public static void Burst(VisualElement root, int count = 110)
        {
            BuildAR.Managers.AudioManager.Instance?.PlayConfetti();
            var host = root.Q(className: "screen") ?? root;
            float w = float.IsNaN(host.layout.width) || host.layout.width < 10 ? 390f : host.layout.width;
            float h = float.IsNaN(host.layout.height) || host.layout.height < 10 ? 844f : host.layout.height;

            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("confetti-layer");
            host.Add(layer);

            var pieces = new List<Piece>(count);
            for (int i = 0; i < count; i++)
            {
                bool fromLeft = i % 2 == 0;
                var el = new VisualElement { pickingMode = PickingMode.Ignore };
                el.AddToClassList("confetti");
                float size = Random.Range(6f, 11f);
                el.style.width = size;
                el.style.height = Random.value < 0.35f ? size : size * Random.Range(1.4f, 2f);
                if (Random.value < 0.25f) el.AddToClassList("confetti--round");
                el.style.backgroundColor = Palette[Random.Range(0, Palette.Length)];
                layer.Add(el);

                pieces.Add(new Piece
                {
                    el = el,
                    x = fromLeft ? Random.Range(-10f, 30f) : w - Random.Range(-10f, 30f),
                    y = h * Random.Range(0.72f, 0.82f),
                    vx = (fromLeft ? 1f : -1f) * Random.Range(160f, 620f),
                    vy = -Random.Range(850f, 1350f),
                    angle = Random.Range(0f, 360f),
                    spin = Random.Range(-540f, 540f),
                    flutter = Random.Range(0f, 6.28f),
                });
            }

            float elapsed = 0f;
            layer.schedule.Execute(timer =>
            {
                float dt = Mathf.Min(timer.deltaTime / 1000f, 0.05f);
                elapsed += dt;
                float fade = Mathf.Clamp01((Seconds - elapsed) / 0.6f);
                foreach (var p in pieces)
                {
                    p.vy += Gravity * dt;
                    p.vx *= 1f - 1.6f * dt;
                    if (p.vy > 260f) p.vy = 260f;
                    p.x += (p.vx + Mathf.Sin(elapsed * 6f + p.flutter) * 40f) * dt;
                    p.y += p.vy * dt;
                    p.angle += p.spin * dt;
                    p.el.style.translate = new Translate(p.x, p.y);
                    p.el.style.rotate = new Rotate(Angle.Degrees(p.angle));
                    p.el.style.opacity = fade;
                }
                if (elapsed >= Seconds) layer.RemoveFromHierarchy();
            }).Every(16).Until(() => elapsed >= Seconds);
        }
    }
}
