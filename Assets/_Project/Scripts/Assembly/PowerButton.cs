using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// The case's power button in Virtual Assembly. Once every part is in and every cable connected, its ring pulses
    /// to invite a press, and a tap on it (see AssemblyInteraction) switches the PC on or off. It needs no collider:
    /// the button is only a couple of centimetres across, so a tap anywhere near it on screen counts.
    /// Sits on the button's RGB ring, whose local Y is the way the button faces. Added by the model importer; does
    /// nothing outside Virtual Assembly.
    /// </summary>
    public class PowerButton : MonoBehaviour
    {
        [Tooltip("How close to the button a tap must land, in density-independent pixels.")]
        public float tapRadiusDp = 34f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Renderer _ring;
        MaterialPropertyBlock _block;

        /// <summary>Which way the button faces, in world space.</summary>
        public Vector3 Facing => transform.TransformVector(Vector3.up).normalized;

        void Awake()
        {
            _ring = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Whether a tap at <paramref name="screenPosition"/> lands on the button: near it on screen, on its visible side.</summary>
        public bool IsUnder(Camera cam, Vector2 screenPosition)
        {
            if (cam == null || !isActiveAndEnabled) return false;
            Vector3 at = transform.position;
            if (Vector3.Dot(Facing, cam.transform.position - at) <= 0f) return false;   // seen from behind: the case is in the way
            var onScreen = cam.WorldToScreenPoint(at);
            if (onScreen.z <= 0f) return false;
            float radius = tapRadiusDp * (Screen.dpi > 0 ? Screen.dpi / 160f : 2f);
            return Vector2.Distance(screenPosition, onScreen) <= radius;
        }

        // After RgbGlow's Update, so while the PC waits to be switched on the invitation shows instead of the dark ring.
        void LateUpdate()
        {
            var manager = AssemblyManager.Instance;
            if (_ring == null || manager == null || !manager.IsComplete || manager.PoweredOn) return;
            var target = Highlight.ColorFor(HighlightState.Target);
            float pulse = 0.3f + 0.7f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            _ring.GetPropertyBlock(_block);
            _block.SetColor(EmissionColor, new Color(target.r, target.g, target.b) * (2.2f * pulse));
            _ring.SetPropertyBlock(_block);
        }
    }
}
