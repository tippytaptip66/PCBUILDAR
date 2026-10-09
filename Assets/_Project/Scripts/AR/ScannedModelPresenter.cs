using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using BuildAR.Assembly;
using BuildAR.Data;

namespace BuildAR.AR
{
    /// <summary>
    /// Puts the recognised component's 3D model into the room. It goes, in order of preference:
    ///   1. on the real part, when the detector tracked an image and knows its pose;
    ///   2. on the surface the part is sitting on (plane raycast through the detection box);
    ///   3. floating in front of the camera, framed for its size.
    /// The model keeps its real-world scale (1 unit = 1 m). One finger turns it, two fingers resize it.
    /// </summary>
    public class ScannedModelPresenter : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private float spawnSeconds = 0.35f;
        [Tooltip("How far the pinch can take the model from its real size.")]
        [SerializeField] private Vector2 scaleRange = new Vector2(0.35f, 4f);

        /// <summary>Set by the scanner UI so drags on the card don't turn the model.</summary>
        public AssemblyInteraction.IUiHitTest UiHitTest { get; set; }

        public ComponentDefinitionSO Current { get; private set; }
        /// <summary>True while the model is on screen.</summary>
        public bool HasModel => _instance != null;
        /// <summary>One line for the card: where the model ended up, and why.</summary>
        public string PlacementNote { get; private set; } = "";
        /// <summary>Raised when the model appears, moves or goes away, so the card can refresh its note.</summary>
        public event System.Action Changed;

        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        GameObject _instance;
        Rect _lastBox = new Rect(0.5f, 0.5f, 0f, 0f);
        Pose? _lastAnchor;
        float _spawnT = 1f, _userScale = 1f, _pinchDistance;
        Vector2 _lastPointer;
        bool _dragging;

        Camera Cam => arCamera != null ? arCamera : Camera.main;

        /// <summary>Used when the scanner has to build the presenter itself (scene made before it existed).</summary>
        public void Configure(ARRaycastManager raycasts, ARPlaneManager planes, Camera cam)
        {
            raycastManager = raycasts;
            planeManager = planes;
            arCamera = cam;
        }

        /// <summary>Spawns (or swaps) the model for a recognised component. Box is normalised, origin top-left.</summary>
        public void Show(ComponentDefinitionSO def, Rect screenBox, Pose? anchor)
        {
            if (def == null) return;
            Current = def;
            _lastBox = screenBox;
            _lastAnchor = anchor;

            Clear();
            if (def.model3DPrefab == null)
            {
                PlacementNote = "No 3D model for this part yet.";
                Changed?.Invoke();
                return;
            }

            // The surface visuals would only clutter the model, so they stay hidden while it is shown.
            SetPlanesVisible(false);

            _instance = Instantiate(def.model3DPrefab);
            _instance.name = $"AR Model · {def.displayName}";
            foreach (var c in _instance.GetComponentsInChildren<Collider>()) c.enabled = false;

            _userScale = 1f;
            _spawnT = 0f;
            Place();
            Changed?.Invoke();
        }

        /// <summary>Drops the model again where the camera is pointing now.</summary>
        public void Reposition()
        {
            if (_instance == null) return;
            _lastAnchor = null;
            _lastBox = new Rect(0.5f, 0.45f, 0f, 0f);
            _spawnT = 0f;
            Place();
            Changed?.Invoke();
        }

        public void Hide()
        {
            Current = null;
            PlacementNote = "";
            Clear();
            SetPlanesVisible(true);
            Changed?.Invoke();
        }

        void Clear()
        {
            if (_instance != null) Destroy(_instance);
            _instance = null;
        }

        // ------------------------------------------------------------------ placement

        void Place()
        {
            var cam = Cam;
            if (_instance == null || cam == null) return;

            if (_lastAnchor.HasValue)
            {
                _instance.transform.SetPositionAndRotation(_lastAnchor.Value.position, _lastAnchor.Value.rotation);
                PlacementNote = "Anchored on the real part.";
                return;
            }

            Vector2 aim = new Vector2(Mathf.Clamp01(_lastBox.center.x) * Screen.width,
                                      (1f - Mathf.Clamp01(_lastBox.center.y)) * Screen.height);

            if (raycastManager != null && raycastManager.Raycast(aim, Hits, TrackableType.PlaneWithinPolygon))
            {
                var pose = Hits[0].pose;
                _instance.transform.SetPositionAndRotation(pose.position + Vector3.up * 0.001f, FacingUser(pose.position, cam));
                PlacementNote = "Placed on the surface in front of you.";
                return;
            }

            // Nothing tracked yet: hang it in front of the camera, far enough away to see all of it.
            float size = Mathf.Max(0.04f, ModelSize());
            float distance = Mathf.Clamp(size * 2.2f, 0.3f, 1.8f);
            Vector3 position = cam.transform.position + cam.transform.forward * distance;
            _instance.transform.SetPositionAndRotation(position, FacingUser(position, cam));
            PlacementNote = "Move your phone around to find a surface for it.";
        }

        static Quaternion FacingUser(Vector3 position, Camera cam)
        {
            Vector3 away = position - cam.transform.position;
            away.y = 0f;
            return away.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>Largest dimension of the spawned model, in metres.</summary>
        float ModelSize()
        {
            var renderers = _instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0.1f;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        }

        void SetPlanesVisible(bool visible)
        {
            if (planeManager == null) return;
            foreach (var plane in planeManager.trackables) plane.gameObject.SetActive(visible);
        }

        // ------------------------------------------------------------------ handling

        void Update()
        {
            if (_instance == null) return;

            if (_spawnT < 1f)
            {
                _spawnT = Mathf.Min(1f, _spawnT + Time.deltaTime / Mathf.Max(0.05f, spawnSeconds));
                ApplyScale(1f - Mathf.Pow(1f - _spawnT, 3f));   // ease-out pop
                if (_spawnT < 1f) return;
            }

            if (HandlePinch()) return;

            var pointer = Pointer.current;
            if (pointer == null) return;
            Vector2 pos = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame && !IsOverUI(pos)) { _dragging = true; _lastPointer = pos; }
            else if (!pointer.press.isPressed) _dragging = false;

            if (!_dragging) return;
            Vector2 delta = pos - _lastPointer;
            _lastPointer = pos;
            _instance.transform.Rotate(Vector3.up, -delta.x * 0.4f, Space.World);
        }

        bool HandlePinch()
        {
            var screen = Touchscreen.current;
            if (screen == null) return false;

            int found = 0;
            Vector2 a = default, b = default;
            foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed) continue;
                if (found == 0) a = touch.position.ReadValue();
                else if (found == 1) b = touch.position.ReadValue();
                found++;
                if (found == 2) break;
            }
            if (found < 2) { _pinchDistance = 0f; return false; }

            _dragging = false;
            float distance = Vector2.Distance(a, b);
            if (_pinchDistance > 0f && distance > 0f)
            {
                _userScale = Mathf.Clamp(_userScale * (distance / _pinchDistance), scaleRange.x, scaleRange.y);
                ApplyScale(1f);
            }
            _pinchDistance = distance;
            return true;
        }

        void ApplyScale(float spawn)
        {
            if (_instance == null) return;
            _instance.transform.localScale = Current != null && Current.model3DPrefab != null
                ? Current.model3DPrefab.transform.localScale * (_userScale * spawn)
                : Vector3.one * (_userScale * spawn);
        }

        bool IsOverUI(Vector2 screenPos) => UiHitTest != null && UiHitTest.IsOverUI(screenPos);
    }
}
