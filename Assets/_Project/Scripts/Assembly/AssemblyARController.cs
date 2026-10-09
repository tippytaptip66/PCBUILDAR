using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Runs Virtual Assembly in AR when the device supports it: scan for a table, show the case's footprint where it
    /// will go, tap to place the real-size case, then assemble it. Falls back to the 3D camera in the Editor and on
    /// phones without ARCore. The AR Session and XR Origin are saved inactive in the scene and switched on here.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class AssemblyARController : MonoBehaviour
    {
        public enum Mode { Checking, ThreeD, Scanning, ReadyToPlace, Placed }

        public static AssemblyARController Instance { get; private set; }

        [SerializeField] private ARSession session;
        [SerializeField] private XROrigin origin;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera threeDCamera;
        [SerializeField] private AssemblyInteraction interaction;
        [SerializeField] private Transform caseRoot;
        [SerializeField] private Transform orbitTarget;
        [Tooltip("Transparent material for the placement outline.")]
        [SerializeField] private Material reticleMaterial;
        [Tooltip("Case footprint on the table in metres (width, depth).")]
        [SerializeField] private Vector2 footprint = new Vector2(0.45f, 0.21f);
        [Tooltip("Offer AR while testing in the Editor. Off keeps Play mode in the 3D view, where the build is " +
                 "easier to work on; the phone is unaffected either way.")]
        [SerializeField] private bool allowArInEditor = false;

        public Mode State { get; private set; } = Mode.Checking;
        public bool IsAR => State == Mode.Scanning || State == Mode.ReadyToPlace || State == Mode.Placed;
        /// <summary>True once the device is known to support ARCore, so the UI can offer to build in the room.</summary>
        public bool ArSupported { get; private set; }
        public event Action<Mode> StateChanged;

        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
        GameObject _reticle;
        Pose _pose;
        Transform _orbitParent;
        Vector3 _orbitLocalPosition;

        void Awake()
        {
            Instance = this;
            if (session != null) session.gameObject.SetActive(false);
            if (origin != null) origin.gameObject.SetActive(false);
            if (orbitTarget != null) { _orbitParent = orbitTarget.parent; _orbitLocalPosition = orbitTarget.localPosition; }
            MeasureFootprint();
        }

        /// <summary>
        /// Sizes the placement outline from the case actually in the scene, so importing a different case model
        /// doesn't leave the reticle showing the old one's footprint. Read from the meshes, because the case is
        /// hidden while scanning and an inactive renderer has no usable bounds.
        /// </summary>
        void MeasureFootprint()
        {
            if (caseRoot == null) return;
            var filters = caseRoot.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;
            Bounds local = default;

            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                // Mesh bounds in the case's own space.
                var matrix = caseRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var centre = matrix.MultiplyPoint3x4(mesh.bounds.center);
                var extents = matrix.MultiplyVector(mesh.bounds.extents);
                var one = new Bounds(centre, new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z)) * 2f);
                if (!any) { local = one; any = true; } else local.Encapsulate(one);
            }

            if (!any || local.size.x < 0.05f || local.size.z < 0.05f) return;
            footprint = new Vector2(local.size.x, local.size.z);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// Opens in the 3D view so the case is there straight away, then finds out whether the room can be used.
        /// Going into AR is the learner's choice (the scan button), rather than a scanning screen they didn't ask for.
        /// </summary>
        IEnumerator Start()
        {
            UseThreeD();

            if (session == null || origin == null || raycastManager == null || caseRoot == null) yield break;
            if (Application.isEditor && !allowArInEditor) yield break;   // stay in the 3D view while testing

            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
                yield return ARSession.CheckAvailability();

            ArSupported = ARSession.state != ARSessionState.Unsupported && ARSession.state != ARSessionState.NeedsInstall;
            // Tell the UI again now that we know, so it can show the "build in AR" button.
            StateChanged?.Invoke(State);
        }

        /// <summary>Switches from the 3D view into AR and starts looking for a surface.</summary>
        public void EnterAR()
        {
            if (!ArSupported || IsAR) return;
            StartAR();
        }

        // ------------------------------------------------------------------ modes

        void StartAR()
        {
            if (threeDCamera != null) threeDCamera.gameObject.SetActive(false);
            session.gameObject.SetActive(true);
            origin.gameObject.SetActive(true);
            if (planeManager != null) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

            interaction.Cam = origin.Camera;
            interaction.ArMode = true;
            interaction.CaseRoot = caseRoot;
            // The drag plane follows the case once it's placed.
            if (orbitTarget != null) orbitTarget.SetParent(caseRoot, true);

            BeginScanning();
        }

        /// <summary>Plain 3D view (Editor, unsupported phones, or the "Use 3D view" button).</summary>
        public void UseThreeD()
        {
            if (session != null) session.gameObject.SetActive(false);
            if (origin != null) origin.gameObject.SetActive(false);
            DestroyReticle();

            if (caseRoot != null)
            {
                caseRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                caseRoot.localScale = Vector3.one;
                caseRoot.gameObject.SetActive(true);
            }
            if (orbitTarget != null)
            {
                orbitTarget.SetParent(_orbitParent, false);
                orbitTarget.localPosition = _orbitLocalPosition;
            }
            if (threeDCamera != null) threeDCamera.gameObject.SetActive(true);
            if (interaction != null)
            {
                interaction.Cam = threeDCamera;
                interaction.ArMode = false;
                interaction.InputEnabled = true;
                interaction.ResetView();
            }
            SetState(Mode.ThreeD);
        }

        /// <summary>Hides the case and scans for a table again.</summary>
        public void BeginScanning()
        {
            if (session == null || !session.gameObject.activeSelf) return;
            interaction.InputEnabled = false;
            caseRoot.gameObject.SetActive(false);
            SetPlanesVisible(true);
            SetState(Mode.Scanning);
        }

        public void Place()
        {
            if (State != Mode.ReadyToPlace) return;
            Vector3 away = origin.Camera.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;

            // Open side (local -Z) faces the user.
            caseRoot.SetPositionAndRotation(_pose.position, Quaternion.LookRotation(away.normalized, Vector3.up));
            caseRoot.localScale = Vector3.one;
            caseRoot.gameObject.SetActive(true);
            DestroyReticle();
            SetPlanesVisible(false);
            interaction.InputEnabled = true;
            SetState(Mode.Placed);
        }

        void SetState(Mode mode)
        {
            if (State == mode) return;
            State = mode;
            StateChanged?.Invoke(mode);
        }

        // ------------------------------------------------------------------ scanning

        void Update()
        {
            if (State != Mode.Scanning && State != Mode.ReadyToPlace) return;

            var aim = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
            bool found = raycastManager.Raycast(aim, Hits, TrackableType.PlaneWithinPolygon)
                         && Hits[0].trackable is ARPlane plane && plane.alignment == PlaneAlignment.HorizontalUp;
            if (!found)
            {
                if (_reticle != null) _reticle.SetActive(false);
                SetState(Mode.Scanning);
                return;
            }

            _pose = Hits[0].pose;
            ShowReticle();
            SetState(Mode.ReadyToPlace);

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                Vector2 pos = pointer.position.ReadValue();
                if (interaction.UiHitTest == null || !interaction.UiHitTest.IsOverUI(pos)) Place();
            }
        }

        void ShowReticle()
        {
            if (_reticle == null) _reticle = BuildReticle();
            _reticle.SetActive(true);
            Vector3 away = origin.Camera.transform.forward;
            away.y = 0f;
            var rotation = away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.identity;
            _reticle.transform.SetPositionAndRotation(_pose.position + Vector3.up * 0.002f, rotation);
            float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 4f);
            _reticle.transform.localScale = new Vector3(pulse, 1f, pulse);
        }

        GameObject BuildReticle()
        {
            var root = new GameObject("Case Placement Outline");

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(fill.GetComponent<Collider>());
            fill.transform.SetParent(root.transform, false);
            fill.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            fill.transform.localScale = new Vector3(footprint.x, footprint.y, 1f);
            if (reticleMaterial != null) fill.GetComponent<MeshRenderer>().sharedMaterial = reticleMaterial;

            var edge = root.AddComponent<LineRenderer>();
            edge.useWorldSpace = false;
            edge.loop = true;
            edge.widthMultiplier = 0.006f;
            edge.numCornerVertices = 4;
            edge.positionCount = 4;
            float hx = footprint.x * 0.5f, hz = footprint.y * 0.5f;
            edge.SetPositions(new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) });
            if (reticleMaterial != null) edge.sharedMaterial = reticleMaterial;
            edge.startColor = edge.endColor = new Color(0.7f, 0.95f, 1f, 1f);
            return root;
        }

        void DestroyReticle()
        {
            if (_reticle != null) Destroy(_reticle);
            _reticle = null;
        }

        void SetPlanesVisible(bool visible)
        {
            if (planeManager == null) return;
            planeManager.enabled = visible;
            foreach (var plane in planeManager.trackables) plane.gameObject.SetActive(visible);
        }
    }
}
