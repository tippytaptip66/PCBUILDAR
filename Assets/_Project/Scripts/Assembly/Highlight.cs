using UnityEngine;

namespace BuildAR.Assembly
{
    public enum HighlightState { None, Target, Hover, Correct, Incorrect, Warning }

    /// <summary>Colours a renderer through a MaterialPropertyBlock (works with URP Lit and Unlit).</summary>
    public static class Highlight
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static MaterialPropertyBlock _block;

        public static Color ColorFor(HighlightState s)
        {
            switch (s)
            {
                case HighlightState.Target: return new Color(0.13f, 0.83f, 0.93f, 0.55f);   // cyan
                case HighlightState.Hover: return new Color(0.18f, 0.42f, 1f, 0.8f);        // electric blue
                case HighlightState.Correct: return new Color(0.09f, 0.77f, 0.5f, 0.75f);   // green
                case HighlightState.Incorrect: return new Color(0.94f, 0.27f, 0.27f, 0.75f); // red
                case HighlightState.Warning: return new Color(1f, 0.54f, 0.12f, 0.75f);     // orange
                default: return new Color(1f, 1f, 1f, 0f);
            }
        }

        public static void Apply(Renderer r, HighlightState state, float pulse = 1f)
        {
            if (r == null) return;
            r.enabled = state != HighlightState.None;
            if (!r.enabled) return;
            _block ??= new MaterialPropertyBlock();
            var c = ColorFor(state);
            c.a *= pulse;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, c);
            _block.SetColor(EmissionColor, c * 1.5f);
            r.SetPropertyBlock(_block);
        }
    }
}
