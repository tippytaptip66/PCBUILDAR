using UnityEngine;

namespace BuildAR.Viewer
{
    /// <summary>
    /// Put this on an empty child GameObject inside a component prefab, positioned on the feature
    /// (pins, heat spreader, notch, locking clip...). Point its blue Z axis (forward) OUT of the surface
    /// so the marker dims when that side faces away from the camera.
    /// Parent it under the part it belongs to so it moves with that part when the model explodes.
    /// </summary>
    public class ModelHotspot : MonoBehaviour
    {
        public enum Kind { Feature, Pins, HeatSpreader, Notch, LockingMechanism, Port, Connector }

        public string title = "Hotspot";
        [TextArea] public string description;
        public Kind kind = Kind.Feature;
        [Tooltip("Dim the marker when the surface normal (forward) faces away from the camera.")]
        public bool dimWhenFacingAway = true;

        public bool IsFacing(Camera cam)
        {
            if (!dimWhenFacingAway || cam == null) return true;
            return Vector3.Dot(transform.forward, cam.transform.position - transform.position) > 0f;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            const float size = 0.004f;
            Gizmos.color = new Color(0.13f, 0.83f, 0.93f);
            Gizmos.DrawSphere(transform.position, size);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * size * 4f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * size * 2f, title);
        }
#endif
    }
}
