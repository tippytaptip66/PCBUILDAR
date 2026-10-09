using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.Viewer
{
    /// <summary>
    /// Add to the root of a component prefab. Each part slides away from the model's centre
    /// when SetAmount(1) is called. Split your model into separate child meshes (e.g. GPU:
    /// Shroud, Fans, PCB, Backplate) — parts are the direct children unless listed explicitly.
    /// </summary>
    public class ExplodableModel : MonoBehaviour
    {
        [Tooltip("Parts that move. Empty = all direct children.")]
        public List<Transform> parts = new List<Transform>();

        [Tooltip("How far (metres) a part travels at full explode, multiplied by ExplodePart.distanceMultiplier.")]
        public float distance = 0.05f;

        Vector3[] _home;
        Vector3[] _direction;
        float[] _multiplier;
        bool _initialised;

        public float Amount { get; private set; }

        void Awake() => Initialise();

        void Initialise()
        {
            if (_initialised) return;
            _initialised = true;

            if (parts.Count == 0)
                foreach (Transform child in transform) parts.Add(child);

            var center = LocalBoundsCenter(transform, transform);
            _home = new Vector3[parts.Count];
            _direction = new Vector3[parts.Count];
            _multiplier = new float[parts.Count];

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                _home[i] = part.localPosition;
                var over = part.GetComponent<ExplodePart>();
                _multiplier[i] = over != null ? over.distanceMultiplier : 1f;

                Vector3 dir = over != null && over.direction != Vector3.zero
                    ? over.direction
                    : LocalBoundsCenter(part, transform) - center;
                if (dir.sqrMagnitude < 1e-8f) dir = Vector3.up * (i % 2 == 0 ? 1f : -1f);
                _direction[i] = dir.normalized;
            }
        }

        /// <summary>0 = assembled, 1 = fully exploded.</summary>
        public void SetAmount(float t)
        {
            Initialise();
            Amount = Mathf.Clamp01(t);
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null)
                    parts[i].localPosition = _home[i] + _direction[i] * (distance * _multiplier[i] * Amount);
        }

        static Vector3 LocalBoundsCenter(Transform target, Transform space)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return space.InverseTransformPoint(target.position);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return space.InverseTransformPoint(b.center);
        }
    }
}
