using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using BuildAR.Data;

namespace BuildAR.AR
{
    /// <summary>
    /// Optional "place a 3D model on the table" mode: set SelectedComponent, tap a detected plane, and the
    /// component's model3DPrefab is placed there. Uses the Input System (the project has the old Input Manager disabled).
    /// Needs an ARPlaneManager + ARRaycastManager on the XR Origin.
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public class ARComponentPlacer : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARScannerController scannerUI;

        private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        public ComponentDefinitionSO SelectedComponent { get; set; }
        private GameObject _placedInstance;

        private void Update()
        {
            if (SelectedComponent == null) return;
            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            if (raycastManager.Raycast(touch.position.ReadValue(), Hits, TrackableType.PlaneWithinPolygon))
                PlaceComponent(Hits[0].pose);
        }

        private void PlaceComponent(Pose pose)
        {
            if (_placedInstance != null) Destroy(_placedInstance);
            if (SelectedComponent.model3DPrefab != null)
                _placedInstance = Instantiate(SelectedComponent.model3DPrefab, pose.position, pose.rotation);

            SetPlanesVisible(false);
            scannerUI?.ShowComponent(SelectedComponent);
        }

        public void ClearPlacement()
        {
            if (_placedInstance != null) Destroy(_placedInstance);
            SetPlanesVisible(true);
            scannerUI?.HideDetectedComponent();
        }

        private void SetPlanesVisible(bool visible)
        {
            if (planeManager == null) return;
            foreach (var plane in planeManager.trackables) plane.gameObject.SetActive(visible);
            planeManager.enabled = visible;
        }
    }
}
