using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace BuildAR.AR.Detection
{
    /// <summary>
    /// Recognises components from reference photos using AR Foundation image tracking — no model training needed.
    /// Each reference image is named after the component's mlLabel ("gpu", "ram", …), so adding photos with
    /// BuildAR ▸ Setup ▸ Build Reference Image Library is enough for the scanner to identify those parts.
    ///
    /// It also knows where the part is in the room, so the 3D model can be anchored right on top of the real one.
    /// Photograph each part straight on, filling the frame, on a plain background and in even light.
    /// </summary>
    public class TrackedImageComponentDetector : ComponentDetectorBase
    {
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private float publishesPerSecond = 10f;
        [Tooltip("A part that stops tracking is still reported for this long, so the card doesn't flicker.")]
        [SerializeField] private float keepLostSeconds = 0.8f;

        public override bool IsAvailable =>
            trackedImageManager != null && trackedImageManager.referenceLibrary != null && trackedImageManager.referenceLibrary.count > 0;

        public override string UnavailableReason =>
            trackedImageManager == null ? "No ARTrackedImageManager in the scene."
            : "No reference images yet — add photos to Assets/_Project/ML/ReferenceImages and run BuildAR > Setup > Build Reference Image Library.";

        readonly List<Detection> _buffer = new List<Detection>(4);
        readonly Dictionary<string, Pose> _poses = new Dictionary<string, Pose>();
        readonly Dictionary<string, float> _seen = new Dictionary<string, float>();
        float _nextPublish;

        Camera Cam => arCamera != null ? arCamera : Camera.main;

        /// <summary>Where the real part is, for anchoring its 3D model. False when it hasn't been seen.</summary>
        public bool TryGetPose(string label, out Pose pose)
        {
            pose = default;
            if (string.IsNullOrEmpty(label) || !_poses.TryGetValue(label, out pose)) return false;
            return _seen.TryGetValue(label, out float t) && Time.time - t <= keepLostSeconds;
        }

        void OnEnable() { _poses.Clear(); _seen.Clear(); }

        void Update()
        {
            if (!IsAvailable || Time.time < _nextPublish) return;
            _nextPublish = Time.time + 1f / Mathf.Max(1f, publishesPerSecond);

            var cam = Cam;
            _buffer.Clear();
            foreach (var image in trackedImageManager.trackables)
            {
                string label = image.referenceImage.name;
                if (string.IsNullOrEmpty(label)) continue;

                if (image.trackingState == TrackingState.Tracking)
                {
                    _poses[label] = new Pose(image.transform.position, image.transform.rotation);
                    _seen[label] = Time.time;
                }
                else if (!_seen.TryGetValue(label, out float last) || Time.time - last > keepLostSeconds) continue;

                if (!TryScreenRect(image, cam, out Rect rect)) continue;
                _buffer.Add(new Detection
                {
                    label = label,
                    confidence = image.trackingState == TrackingState.Tracking ? 0.95f : 0.6f,
                    screenRect = rect,
                    hasBox = true,
                });
            }
            Publish(_buffer);
        }

        /// <summary>The marker lies in the trackable's XZ plane; project its corners to a normalised screen rect.</summary>
        bool TryScreenRect(ARTrackedImage image, Camera cam, out Rect rect)
        {
            rect = default;
            if (cam == null || Screen.width <= 0 || Screen.height <= 0) return false;

            var t = image.transform;
            Vector3 right = t.right * (image.size.x * 0.5f);
            Vector3 forward = t.forward * (image.size.y * 0.5f);
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = t.position + (i < 2 ? right : -right) + (i % 2 == 0 ? forward : -forward);
                Vector3 screen = cam.WorldToScreenPoint(corner);
                if (screen.z <= 0f) return false;                       // behind the camera
                float x = screen.x / Screen.width, y = 1f - screen.y / Screen.height;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }

            rect = Rect.MinMaxRect(Mathf.Clamp01(minX), Mathf.Clamp01(minY), Mathf.Clamp01(maxX), Mathf.Clamp01(maxY));
            return rect.width > 0.02f && rect.height > 0.02f;
        }
    }
}
