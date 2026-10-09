using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.AR.Detection
{
    /// <summary>
    /// Fake detector for testing the scanner UI in the Editor (or on a phone before a model is trained).
    /// After a short delay it "sees" the current label in the middle of the screen with a little jitter.
    /// Right-click the component header > Next Label to switch parts.
    /// </summary>
    public class SimulatedComponentDetector : ComponentDetectorBase
    {
        [SerializeField] private string[] labels = { "gpu", "ram", "cpu", "motherboard", "psu" };
        [SerializeField] private float firstDetectionDelay = 2.5f;
        [SerializeField] private float publishesPerSecond = 6f;
        [SerializeField] private bool allowInPlayerBuilds = false;

        int _labelIndex;
        float _visibleSince;
        float _nextPublish;
        readonly List<Detection> _buffer = new List<Detection>(1);

        public override bool IsAvailable => Application.isEditor || allowInPlayerBuilds;
        public override string UnavailableReason => "Simulated detector is disabled in player builds.";

        void OnEnable() => _visibleSince = Time.time + firstDetectionDelay;

        [ContextMenu("Next Label")]
        public void NextLabel()
        {
            _labelIndex = (_labelIndex + 1) % labels.Length;
            _visibleSince = Time.time + firstDetectionDelay;
        }

        void Update()
        {
            if (Time.time < _nextPublish || labels.Length == 0) return;
            _nextPublish = Time.time + 1f / publishesPerSecond;

            _buffer.Clear();
            if (Time.time >= _visibleSince)
            {
                float jx = (Mathf.PerlinNoise(Time.time * 0.8f, 0f) - 0.5f) * 0.03f;
                float jy = (Mathf.PerlinNoise(0f, Time.time * 0.8f) - 0.5f) * 0.03f;
                _buffer.Add(new Detection
                {
                    label = labels[_labelIndex],
                    confidence = 0.82f + Mathf.PerlinNoise(Time.time, 3f) * 0.15f,
                    screenRect = new Rect(0.16f + jx, 0.30f + jy, 0.68f, 0.26f),
                    hasBox = true,
                });
            }
            Publish(_buffer);
        }
    }
}
