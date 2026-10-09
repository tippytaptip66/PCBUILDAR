using System.Collections.Generic;
using UnityEngine;
using BuildAR.Data;

namespace BuildAR.Viewer
{
    /// <summary>
    /// Off-screen turntable for the Component Detail screen. Renders the component's model3DPrefab into a
    /// RenderTexture that the UI shows as a background image. Sits far below the scene origin so nothing else
    /// is in its camera's view.
    /// </summary>
    public class ModelViewerStage : MonoBehaviour
    {
        public static ModelViewerStage Instance { get; private set; }

        [SerializeField] private Camera viewerCamera;
        [SerializeField] private Transform pivot;
        [SerializeField] private float explodeSpeed = 4f;
        [SerializeField] private float orbitDegreesPerPixel = 0.35f;

        GameObject _instance;
        ExplodableModel _explodable;
        readonly List<ModelHotspot> _hotspots = new List<ModelHotspot>();
        RenderTexture _rt;

        float _yaw = 35f, _pitch = 20f, _distance = 0.3f, _minDistance = 0.05f, _maxDistance = 2f, _defaultDistance = 0.3f;
        float _explodeTarget;

        public Camera Camera => viewerCamera;
        public IReadOnlyList<ModelHotspot> Hotspots => _hotspots;
        public bool IsExploded => _explodeTarget > 0.5f;
        public bool HasExplodableParts => _explodable != null;

        void Awake()
        {
            Instance = this;
            if (viewerCamera != null) viewerCamera.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ReleaseTexture();
        }

        public RenderTexture Show(ComponentDefinitionSO def, int width, int height)
        {
            Clear();
            EnsureTexture(width, height);

            var prefab = def != null ? def.model3DPrefab : null;
            _instance = prefab != null ? Instantiate(prefab, pivot) : CreateFallback();
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;

            // Centre the model on the pivot and frame it.
            var bounds = WorldBounds(_instance);
            _instance.transform.position += pivot.position - bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            float halfFov = viewerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            _defaultDistance = radius / Mathf.Sin(halfFov) * 1.15f;
            _minDistance = _defaultDistance * 0.35f;
            _maxDistance = _defaultDistance * 2.5f;
            viewerCamera.nearClipPlane = Mathf.Max(0.001f, radius * 0.02f);
            viewerCamera.farClipPlane = _maxDistance + radius * 4f;

            _explodable = _instance.GetComponentInChildren<ExplodableModel>();
            _hotspots.Clear();
            _hotspots.AddRange(_instance.GetComponentsInChildren<ModelHotspot>());
            _explodeTarget = 0f;
            _explodable?.SetAmount(0f);

            ResetView();
            viewerCamera.enabled = true;
            return _rt;
        }

        public void Hide()
        {
            Clear();
            if (viewerCamera != null) viewerCamera.enabled = false;
        }

        public void Resize(int width, int height)
        {
            if (_rt != null && _rt.width == width && _rt.height == height) return;
            EnsureTexture(width, height);
        }

        public RenderTexture Texture => _rt;

        public void Orbit(Vector2 pixelDelta)
        {
            _yaw += pixelDelta.x * orbitDegreesPerPixel;
            _pitch = Mathf.Clamp(_pitch + pixelDelta.y * orbitDegreesPerPixel, -80f, 80f);
        }

        /// <summary>factor &gt; 1 zooms in.</summary>
        public void Zoom(float factor) => _distance = Mathf.Clamp(_distance / Mathf.Max(0.01f, factor), _minDistance, _maxDistance);

        public void ResetView()
        {
            _yaw = 35f; _pitch = 22f; _distance = _defaultDistance;
            ApplyCamera();
        }

        public void SetExploded(bool exploded) => _explodeTarget = exploded ? 1f : 0f;

        public Vector2? ViewportPoint(ModelHotspot h)
        {
            var vp = viewerCamera.WorldToViewportPoint(h.transform.position);
            if (vp.z <= 0) return null;
            return new Vector2(vp.x, vp.y);
        }

        void LateUpdate()
        {
            if (_instance == null) return;
            if (_explodable != null && !Mathf.Approximately(_explodable.Amount, _explodeTarget))
                _explodable.SetAmount(Mathf.MoveTowards(_explodable.Amount, _explodeTarget, Time.deltaTime * explodeSpeed));
            ApplyCamera();
        }

        void ApplyCamera()
        {
            if (viewerCamera == null || pivot == null) return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            viewerCamera.transform.position = pivot.position - rot * Vector3.forward * _distance;
            viewerCamera.transform.rotation = rot;
        }

        void Clear()
        {
            if (_instance != null) Destroy(_instance);
            _instance = null;
            _explodable = null;
            _hotspots.Clear();
        }

        void EnsureTexture(int width, int height)
        {
            width = Mathf.Clamp(width, 64, 2048);
            height = Mathf.Clamp(height, 64, 2048);
            if (_rt != null && _rt.width == width && _rt.height == height) return;
            ReleaseTexture();
            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "ModelViewerRT" };
            _rt.Create();
            viewerCamera.targetTexture = _rt;
        }

        void ReleaseTexture()
        {
            if (_rt == null) return;
            if (viewerCamera != null) viewerCamera.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        GameObject CreateFallback()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(pivot, false);
            go.transform.localScale = Vector3.one * 0.1f;
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
