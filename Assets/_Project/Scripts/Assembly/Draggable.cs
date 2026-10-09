using System.Collections;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.Assembly
{
    /// <summary>A component instance being dragged or installed in Virtual Assembly (added at spawn time).</summary>
    public class Draggable : MonoBehaviour
    {
        public ComponentDefinitionSO definition;
        public SnapSlot Slot { get; internal set; }

        public void SnapTo(Transform slot, bool animate = true)
        {
            transform.SetParent(slot, true);
            if (!animate || !isActiveAndEnabled) { transform.SetPositionAndRotation(slot.position, slot.rotation); return; }
            StopAllCoroutines();
            StartCoroutine(SnapRoutine(slot));
        }

        IEnumerator SnapRoutine(Transform slot)
        {
            // Hover just above the slot, then press in — mimics a real insertion.
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            var cam = Camera.main;
            Vector3 above = slot.position + (cam != null ? -cam.transform.forward : Vector3.up) * 0.03f;
            const float align = 0.18f, press = 0.12f;

            for (float t = 0; t < 1f; t += Time.deltaTime / align)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                transform.SetPositionAndRotation(Vector3.Lerp(startPos, above, e), Quaternion.Slerp(startRot, slot.rotation, e));
                yield return null;
            }
            for (float t = 0; t < 1f; t += Time.deltaTime / press)
            {
                transform.SetPositionAndRotation(Vector3.Lerp(above, slot.position, t * t), slot.rotation);
                yield return null;
            }
            transform.SetPositionAndRotation(slot.position, slot.rotation);
        }
    }
}
