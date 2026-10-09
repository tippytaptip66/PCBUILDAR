using UnityEngine;

namespace BuildAR.Viewer
{
    /// <summary>Optional per-part override for ExplodableModel.</summary>
    public class ExplodePart : MonoBehaviour
    {
        [Tooltip("Direction in the model root's local space. Zero = automatic (away from centre).")]
        public Vector3 direction;
        public float distanceMultiplier = 1f;
    }
}
