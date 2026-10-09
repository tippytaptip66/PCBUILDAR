using System.Linq;
using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Keeps a part's indicator LEDs (the power and drive lights on the case's top panel, the network and BIOS lights
    /// on the rear I/O) dark while the PC in Virtual Assembly is off. Those LEDs are baked into their panel's mesh as
    /// submeshes, so this turns off the glow of just those submeshes. Added at runtime by AssemblyManager to every
    /// renderer wearing an LED material; colour-cycling LEDs (RgbGlow) switch themselves.
    /// </summary>
    public class IndicatorLeds : MonoBehaviour
    {
        static readonly string[] LedMaterialPrefixes = { "M_IO_LED_", "M_LED_" };
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Renderer _renderer;
        int[] _leds;
        bool? _lit;
        MaterialPropertyBlock _block;

        /// <summary>Adds one to every renderer under <paramref name="root"/> with an indicator LED among its materials.</summary>
        public static void AddTo(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponent<RgbGlow>() == null && r.GetComponent<IndicatorLeds>() == null && LedSlots(r).Length > 0)
                    r.gameObject.AddComponent<IndicatorLeds>();
        }

        static int[] LedSlots(Renderer r)
        {
            var materials = r.sharedMaterials;
            return Enumerable.Range(0, materials.Length)
                .Where(i => materials[i] != null && LedMaterialPrefixes.Any(p => materials[i].name.StartsWith(p)))
                .ToArray();
        }

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _leds = LedSlots(_renderer);
            _block = new MaterialPropertyBlock();
        }

        void Update()
        {
            bool lit = AssemblyManager.HasPower;
            if (_lit == lit) return;
            _lit = lit;
            _block.Clear();
            if (!lit) _block.SetColor(EmissionColor, Color.black);
            foreach (int i in _leds) _renderer.SetPropertyBlock(_block, i);   // an empty block: the material's own glow
        }
    }
}
