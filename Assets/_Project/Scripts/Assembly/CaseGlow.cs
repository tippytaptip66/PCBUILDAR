using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Makes the build's RGB LEDs glow. The scene camera doesn't render post-processing, so this adds its own global
    /// Volume with a Bloom override and switches post-processing on for whichever camera is current (the 3D camera,
    /// or the AR camera in AR mode). The same Volume turns tonemapping and vignette off, so switching post-processing
    /// on changes nothing else — the AR camera feed in particular keeps its true colours.
    ///
    /// The threshold sits above plain white, so only emission driven past 1 (the LEDs) blooms — a white label or a
    /// bright highlight doesn't. That needs HDR, which both render pipeline assets have on.
    /// Added to the assembly case by Import Assembly Case Model.
    /// </summary>
    public class CaseGlow : MonoBehaviour
    {
        [Tooltip("How strongly the LEDs bleed light.")]
        public float bloomIntensity = 0.9f;
        [Tooltip("Brightness a pixel needs before it blooms. Above 1, so ordinary surfaces never do.")]
        public float threshold = 1.1f;
        [Range(0f, 1f)] public float scatter = 0.6f;

        GameObject _volumeObject;
        VolumeProfile _profile;
        Camera _camera;

        void OnEnable()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = _profile.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(threshold);
            bloom.scatter.Override(scatter);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);   // cheaper on phones, and the glow is soft anyway
            _profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            _profile.Add<Vignette>(true).intensity.Override(0f);

            _volumeObject = new GameObject("LED Bloom") { hideFlags = HideFlags.DontSave };
            _volumeObject.transform.SetParent(transform, false);
            var volume = _volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = _profile;
        }

        void OnDisable()
        {
            if (_volumeObject != null) Destroy(_volumeObject);
            if (_profile != null) Destroy(_profile);
            _camera = null;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || cam == _camera) return;
            _camera = cam;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            // One broken pixel (a model with a bad tangent, say) is invisible on its own, but bloom would smear it
            // into a blotch across the screen; this turns such pixels black before the bloom sees them.
            data.stopNaN = true;
        }
    }
}
