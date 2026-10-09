using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using BuildAR.Data;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Touch / mouse input for Virtual Assembly:
    ///  • drag a part from the UI tray onto a glowing slot
    ///  • 3D view: one-finger drag on empty space orbits the camera, pinch / scroll zooms
    ///  • AR: the phone is the camera, so one-finger drag turns the case and pinch resizes it
    ///  • tap a cable end, then its matching port, to connect a cable
    ///  • tap the case's power button to switch the finished PC on or off
    /// </summary>
    public class AssemblyInteraction : MonoBehaviour
    {
        public interface IUiHitTest { bool IsOverUI(Vector2 screenPosition); }

        [SerializeField] private Camera cam;
        [SerializeField] private Transform orbitTarget;
        [SerializeField] private float snapRadiusDp = 70f;
        [SerializeField] private float orbitSpeed = 0.25f;
        [SerializeField] private Vector2 distanceRange = new Vector2(0.45f, 1.8f);
        [SerializeField] private Material cableMaterial;
        [SerializeField] private Vector2 arScaleRange = new Vector2(0.5f, 2f);

        public IUiHitTest UiHitTest { get; set; }
        public bool InputEnabled { get; set; } = true;

        public Camera Cam { get => cam; set => cam = value; }

        /// <summary>AR mode: the camera is driven by tracking; gestures move the case instead.</summary>
        public bool ArMode { get; set; }
        public Transform CaseRoot { get; set; }

        Draggable _dragging;
        SnapSlot _hoverSlot;
        CablePort _pendingPort;
        bool _orbiting;
        Vector2 _lastPointer;
        bool _pressingPower;
        Vector2 _powerPressAt;
        float _pinchDistance;
        float _yaw = -18f, _pitch = 12f, _distance = 1.05f;
        float _defaultYaw, _defaultPitch, _defaultDistance;

        float SnapRadiusPixels => snapRadiusDp * (Screen.dpi > 0 ? Screen.dpi / 160f : 2f);

        void Awake()
        {
            // AssemblyManager runs earlier (DefaultExecutionOrder) and restores saved cables in Start.
            _defaultYaw = _yaw; _defaultPitch = _pitch; _defaultDistance = _distance;
            if (AssemblyManager.Instance != null) AssemblyManager.Instance.OnCableConnected += DrawCable;
        }

        void Start() => ApplyCamera();

        void OnDestroy()
        {
            if (AssemblyManager.Instance != null) AssemblyManager.Instance.OnCableConnected -= DrawCable;
        }

        // ------------------------------------------------------------------ public API (UI buttons)

        public void BeginDrag(ComponentDefinitionSO def)
        {
            var manager = AssemblyManager.Instance;
            if (!InputEnabled || manager == null || !manager.IsReady || _dragging != null) return;
            CancelPendingCable();
            _dragging = manager.Spawn(def);
            _dragging.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            foreach (var s in manager.SlotsFor(def.category))
                if (!s.IsOccupied) s.SetHighlight(HighlightState.Target);
            MoveDragged(PointerPosition());
        }

        public void RotateBy(float degrees)
        {
            if (ArMode && CaseRoot != null) CaseRoot.Rotate(0f, degrees, 0f, Space.Self);
            else _yaw += degrees;
        }

        public void ResetView()
        {
            if (ArMode && CaseRoot != null)
            {
                CaseRoot.localScale = Vector3.one;
                Vector3 away = cam.transform.forward;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) CaseRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                return;
            }
            _yaw = _defaultYaw; _pitch = _defaultPitch; _distance = _defaultDistance;
        }

        /// <summary>3D view: tilts the camera to look down on the top of the case, where the power button is.</summary>
        public void LookAtTop()
        {
            if (!ArMode) _pitch = Mathf.Max(_pitch, 48f);
        }

        // ------------------------------------------------------------------ input loop

        void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null) { ApplyCamera(); return; }

            Vector2 pos = pointer.position.ReadValue();
            bool pressed = pointer.press.isPressed;

            if (_dragging != null)
            {
                MoveDragged(pos);
                if (!pressed) Drop();
                ApplyCamera();
                return;
            }
            if (!InputEnabled) { _orbiting = false; _pressingPower = false; ApplyCamera(); return; }

            if (HandlePinch()) { ApplyCamera(); return; }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !IsOverUI(pos)) Zoom(scroll > 0 ? 1.1f : 0.9f);
            }

            if (pointer.press.wasPressedThisFrame && !IsOverUI(pos))
            {
                // The power button acts on release, so a drag that starts on it still turns the view.
                var button = AssemblyManager.Instance != null ? AssemblyManager.Instance.PowerButton : null;
                _pressingPower = button != null && button.IsUnder(cam, pos);
                _powerPressAt = pos;
                if (!TryTapPort(pos)) { _orbiting = true; _lastPointer = pos; }
            }
            if (_pressingPower && !pressed)
            {
                _pressingPower = false;
                if (Vector2.Distance(pos, _powerPressAt) < SnapRadiusPixels * 0.2f) AssemblyManager.Instance?.PressPower();
            }
            if (_orbiting && pressed)
            {
                var delta = pos - _lastPointer;
                _lastPointer = pos;
                if (ArMode && CaseRoot != null) CaseRoot.Rotate(0f, -delta.x * orbitSpeed * 1.4f, 0f, Space.Self);
                else
                {
                    _yaw += delta.x * orbitSpeed;
                    _pitch = Mathf.Clamp(_pitch - delta.y * orbitSpeed, -30f, 70f);
                }
            }
            if (!pressed) _orbiting = false;

            ApplyCamera();
        }

        bool HandlePinch()
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;
            var active = ts.touches.Where(t => t.press.isPressed).Take(2).ToList();
            if (active.Count < 2) { _pinchDistance = 0; return false; }
            _orbiting = false;
            _pressingPower = false;
            float d = Vector2.Distance(active[0].position.ReadValue(), active[1].position.ReadValue());
            if (_pinchDistance > 0 && d > 0) Zoom(d / _pinchDistance);
            _pinchDistance = d;
            return true;
        }

        void Zoom(float factor)
        {
            if (ArMode && CaseRoot != null)
                CaseRoot.localScale = Vector3.one * Mathf.Clamp(CaseRoot.localScale.x * factor, arScaleRange.x, arScaleRange.y);
            else
                _distance = Mathf.Clamp(_distance / factor, distanceRange.x, distanceRange.y);
        }

        void ApplyCamera()
        {
            if (ArMode || cam == null || orbitTarget == null) return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            cam.transform.SetPositionAndRotation(orbitTarget.position - rot * Vector3.forward * _distance, rot);
        }

        Vector2 PointerPosition() => Pointer.current != null ? Pointer.current.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f);

        bool IsOverUI(Vector2 screenPos) => UiHitTest != null && UiHitTest.IsOverUI(screenPos);

        // ------------------------------------------------------------------ dragging

        void MoveDragged(Vector2 screenPos)
        {
            var plane = new Plane(-cam.transform.forward, orbitTarget.position - cam.transform.forward * 0.08f);
            var ray = cam.ScreenPointToRay(screenPos);
            if (plane.Raycast(ray, out float enter)) _dragging.transform.position = ray.GetPoint(enter);

            SnapSlot nearest = null;
            float best = SnapRadiusPixels;
            foreach (var s in AssemblyManager.Instance.SlotsFor(_dragging.definition.category))
            {
                if (s.IsOccupied) continue;
                var sp = cam.WorldToScreenPoint(s.transform.position);
                if (sp.z <= 0) continue;
                float d = Vector2.Distance(screenPos, sp);
                if (d < best) { best = d; nearest = s; }
            }

            if (nearest == _hoverSlot) return;
            if (_hoverSlot != null) _hoverSlot.SetHighlight(HighlightState.Target);
            _hoverSlot = nearest;
            if (_hoverSlot != null) _hoverSlot.SetHighlight(HighlightState.Hover);
        }

        void Drop()
        {
            var manager = AssemblyManager.Instance;
            var dragged = _dragging;
            var slot = _hoverSlot;
            _dragging = null;
            _hoverSlot = null;
            foreach (var s in manager.Slots) s.SetHighlight(HighlightState.None);

            if (slot == null)
            {
                Destroy(dragged.gameObject);
                return;
            }
            if (!manager.TryPlace(dragged, slot)) Destroy(dragged.gameObject);
        }

        // ------------------------------------------------------------------ cables

        bool TryTapPort(Vector2 screenPos)
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out var hit, 10f)) return false;
            var port = hit.collider.GetComponentInParent<CablePort>();
            if (port == null) return false;

            if (port.IsConnected) return true;
            if (_pendingPort == null)
            {
                _pendingPort = port;
                port.SetHighlight(HighlightState.Hover);
                return true;
            }

            var first = _pendingPort;
            CancelPendingCable();
            if (first == port) return true;
            if (!AssemblyManager.Instance.TryConnect(first, port))
                StartCoroutine(Flash(port, HighlightState.Incorrect));
            return true;
        }

        void CancelPendingCable()
        {
            if (_pendingPort != null && !_pendingPort.IsConnected) _pendingPort.SetHighlight(HighlightState.None);
            _pendingPort = null;
        }

        System.Collections.IEnumerator Flash(CablePort port, HighlightState state)
        {
            port.SetHighlight(state);
            yield return new WaitForSeconds(0.6f);
            if (!port.IsConnected) port.SetHighlight(HighlightState.None);
        }

        readonly List<GameObject> _cables = new List<GameObject>();

        void DrawCable(CablePort source, CablePort target) => _cables.Add(CableBuilder.Build(source, target, cableMaterial));

        public void ClearCables()
        {
            foreach (var c in _cables) if (c != null) Destroy(c);
            _cables.Clear();
        }
    }
}
