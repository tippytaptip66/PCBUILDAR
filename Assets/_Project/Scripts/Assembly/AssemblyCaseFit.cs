using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Records how Import Assembly Case Model moved the slots and cable ports around an imported case, so the next
    /// import (or Restore Placeholder Assembly Case) can put them back where they were authored first and offsets
    /// never compound. The board and everything on it move as a group (the Slots, CablePorts and Motherboard
    /// children's own positions); the PSU bay and its cables, and the front-panel cable, hang off the case itself.
    /// </summary>
    public class AssemblyCaseFit : MonoBehaviour
    {
        [Tooltip("Width and depth stretch left by older versions of the importer. Undone on the next import.")]
        public Vector3 appliedRatio = Vector3.one;

        [Tooltip("How far the PSU bay and the PSU's cables were moved to sit in the case's basement.")]
        public Vector3 psuOffset;

        [Tooltip("How far the front-panel cable was moved to reach the case's front.")]
        public Vector3 frontOffset;
    }
}
