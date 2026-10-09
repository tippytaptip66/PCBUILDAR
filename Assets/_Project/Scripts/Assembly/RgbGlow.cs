using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// An RGB LED, the way gaming hardware does it: the colour runs slowly round the colour wheel. Drives the emission
    /// of the renderer on this object and the colour of a Light on it. Every RgbGlow reads the same clock, so all the
    /// LEDs in a build change colour together. Added by the importers to fan rings, light bars and case lighting.
    /// In the PC builder they stay dark until the finished PC is switched on, then fade in.
    /// </summary>
    [DisallowMultipleComponent]
    public class RgbGlow : MonoBehaviour
    {
        const float FadeSeconds = 0.6f;

        [Tooltip("Seconds for one trip round the colour wheel.")]
        public float cycleSeconds = 10f;

        [Tooltip("Head start round the wheel (0–1), for an LED that should run a little ahead of the others.")]
        [Range(0f, 1f)] public float phase;

        [Tooltip("Emission brightness. Above 1 is what makes it bloom; ordinary surfaces never get there.")]
        public float intensity = 2.4f;

        [Range(0f, 1f)] public float saturation = 0.85f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Renderer _renderer;
        Light _light;
        MaterialPropertyBlock _block;
        float _level = -1f;   // 0 dark (no power) .. 1 full brightness; the first frame starts it where it should be

        /// <summary>The colour every LED shows right now, before its own brightness.</summary>
        public static Color Current(float cycleSeconds, float phase, float saturation) =>
            Color.HSVToRGB(Mathf.Repeat(Time.time / Mathf.Max(0.1f, cycleSeconds) + phase, 1f), saturation, 1f);

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _light = GetComponent<Light>();
            _block = new MaterialPropertyBlock();
        }

        void Update()
        {
            float target = AssemblyManager.HasPower ? 1f : 0f;
            _level = _level < 0f ? target : Mathf.MoveTowards(_level, target, Time.deltaTime / FadeSeconds);
            var color = Current(cycleSeconds, phase, saturation) * _level;
            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionColor, color * intensity);
                _renderer.SetPropertyBlock(_block);
            }
            if (_light != null)
            {
                _light.enabled = _level > 0f;
                _light.color = color;
            }
        }
    }
}
