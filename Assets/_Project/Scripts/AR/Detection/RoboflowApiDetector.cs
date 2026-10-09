using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace BuildAR.AR.Detection
{
    /// <summary>
    /// Runs a model that lives on Roboflow's servers instead of one inside the app. Camera frames are sent up as
    /// JPEGs and predictions come back as JSON.
    ///
    /// Default target is James Manalili's published PC parts model (`pc-parts-5uy7m/10`, YOLOv8s, mAP@50 98.6%),
    /// whose weights Roboflow does not let you download — the API is the only way to use that particular model.
    ///
    /// What you give up next to <see cref="InferenceComponentDetector"/>:
    ///   • it needs a working internet connection, so the app stops being usable offline;
    ///   • every scan is a round trip, so expect 1-2 looks per second instead of 5, and worse on poor signal;
    ///   • the API key ships inside the APK, where anyone can pull it out, and usage is billed to your account.
    /// Keep the on-device detector first in the scanner's list and treat this as the fallback, or swap the order
    /// to demonstrate the published model.
    /// </summary>
    public class RoboflowApiDetector : ComponentDetectorBase, IStillDetector
    {
        [Header("AR")]
        [SerializeField] private ARCameraManager cameraManager;

        [Header("Roboflow")]
        [Tooltip("roboflow.com -> Settings -> API Keys. This is visible to anyone who unpacks the APK.")]
        [SerializeField] private string apiKey = "";
        [Tooltip("Project slug and version, as shown on the model page.")]
        [SerializeField] private string modelId = "pc-parts-5uy7m/10";
        [SerializeField] private string apiUrl = "https://serverless.roboflow.com";

        [Header("Detection")]
        [Range(0f, 1f)] [SerializeField] private float scoreThreshold = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float overlapThreshold = 0.3f;
        [Tooltip("Each request is a round trip over the network; above ~2 they queue up behind each other.")]
        [SerializeField] private float requestsPerSecond = 1.5f;
        [Range(1, 100)] [SerializeField] private int jpegQuality = 70;
        [SerializeField] private int captureSize = 640;
        [SerializeField] private InferenceComponentDetector.ImageRotation rotation =
            InferenceComponentDetector.ImageRotation.Clockwise90;

        Texture2D _frame;
        byte[] _scratch;
        Coroutine _loop;
        int _side;
        int _imageWidth, _imageHeight;

        public override bool IsAvailable =>
            apiKey.Length > 0 && modelId.Length > 0 && cameraManager != null && !Application.isEditor;

        /// <summary>A still needs only the key and model, so photos can be scanned in the Editor too.</summary>
        public bool CanDetectStills => apiKey.Length > 0 && modelId.Length > 0;

        public override string UnavailableReason =>
            apiKey.Length == 0 ? "No Roboflow API key set on the RoboflowApiDetector."
            : modelId.Length == 0 ? "No Roboflow model id set (for example pc-parts-5uy7m/10)."
            : cameraManager == null ? "No ARCameraManager assigned."
            : Application.isEditor ? "Camera CPU images aren't available in the Editor; using the simulated detector." : "";

        void OnEnable()
        {
            if (!IsAvailable) return;
            if (cameraManager != null) cameraManager.autoFocusRequested = true;
            if (Application.internetReachability == NetworkReachability.NotReachable)
                Debug.LogWarning("BuildAR: no internet connection — the Roboflow detector will keep retrying.");
            _loop = StartCoroutine(Loop());
        }

        void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
        }

        void OnDestroy()
        {
            if (_frame != null) Destroy(_frame);
        }

        string Url => $"{apiUrl.TrimEnd('/')}/{modelId}" +
                      $"?api_key={UnityWebRequest.EscapeURL(apiKey)}" +
                      $"&confidence={Mathf.RoundToInt(scoreThreshold * 100f)}" +
                      $"&overlap={Mathf.RoundToInt(overlapThreshold * 100f)}";

        IEnumerator Loop()
        {
            var wait = new WaitForSeconds(1f / Mathf.Max(0.2f, requestsPerSecond));
            while (true)
            {
                yield return wait;
                if (!CaptureFrame()) continue;
                yield return Send(_frame, CropToScreen, list => Publish(list));
            }
        }

        /// <summary>
        /// One request. Errors are logged and swallowed: a dropped frame is not worth stopping the scan.
        /// <paramref name="map"/> turns a box in the picture that was sent into the box to report.
        /// </summary>
        IEnumerator Send(Texture2D image, Func<Rect, Rect> map, Action<IReadOnlyList<Detection>> done)
        {
            string body = Convert.ToBase64String(image.EncodeToJPG(jpegQuality));
            using (var request = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"BuildAR: Roboflow request failed ({request.responseCode}) {request.error}");
                    done?.Invoke(Array.Empty<Detection>());
                    yield break;
                }
                done?.Invoke(Parse(request.downloadHandler.text, image, map));
            }
        }

        // ------------------------------------------------------------------ still pictures

        /// <summary>
        /// Sends the region of a still at its own shape (the server resizes it for the model), so nothing at the
        /// sides is cut off; boxes come back in the whole picture's coordinates.
        /// </summary>
        public async void DetectStill(Texture source, Rect region, Action<IReadOnlyList<Detection>> done)
        {
            if (source == null || !CanDetectStills) { done?.Invoke(Array.Empty<Detection>()); return; }

            IReadOnlyList<Detection> result = Array.Empty<Detection>();
            using (var frame = StillFrame.Read(source, region, captureSize, square: false))
            {
                var request = Send(frame.Texture, frame.ToPicture, list => result = list);
                try
                {
                    // Pumped by hand, so wait out the web request it yields instead of stepping past it.
                    while (request.MoveNext())
                    {
                        if (request.Current is AsyncOperation pending)
                            while (!pending.isDone) await Awaitable.NextFrameAsync(destroyCancellationToken);
                        else await Awaitable.NextFrameAsync(destroyCancellationToken);
                    }
                }
                catch (OperationCanceledException) { return; }
            }
            done?.Invoke(result);
        }

        // ------------------------------------------------------------------ camera frame

        // Deliberately a copy of the capture and box-mapping in InferenceComponentDetector rather than a shared
        // helper: that detector is the one that ships, and it is not worth editing to add an optional alternative.
        bool CaptureFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image)) return false;
            using (image)
            {
                bool swap = rotation == InferenceComponentDetector.ImageRotation.Clockwise90 ||
                            rotation == InferenceComponentDetector.ImageRotation.CounterClockwise90;
                _imageWidth = swap ? image.height : image.width;
                _imageHeight = swap ? image.width : image.height;

                int crop = Mathf.Min(image.width, image.height);
                _side = Mathf.Min(crop, captureSize);
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY)
                {
                    inputRect = new RectInt((image.width - crop) / 2, (image.height - crop) / 2, crop, crop),
                    outputDimensions = new Vector2Int(_side, _side),
                };

                if (_frame == null || _frame.width != _side)
                {
                    if (_frame != null) Destroy(_frame);
                    _frame = new Texture2D(_side, _side, TextureFormat.RGBA32, false);
                    _scratch = new byte[_side * _side * 4];
                }
                image.Convert(conversion, _frame.GetRawTextureData<byte>());
            }

            RotateSquare();
            _frame.Apply(false);
            return true;
        }

        /// <summary>Rotates the RGBA square in place (texture rows are bottom-up).</summary>
        void RotateSquare()
        {
            if (rotation == InferenceComponentDetector.ImageRotation.None) return;
            var pixels = _frame.GetRawTextureData<byte>();
            pixels.CopyTo(_scratch);
            int n = _side;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int dx, dy;
                    switch (rotation)
                    {
                        case InferenceComponentDetector.ImageRotation.Clockwise90: dx = y; dy = n - 1 - x; break;
                        case InferenceComponentDetector.ImageRotation.CounterClockwise90: dx = n - 1 - y; dy = x; break;
                        default: dx = n - 1 - x; dy = n - 1 - y; break;
                    }
                    int s = (y * n + x) * 4, d = (dy * n + dx) * 4;
                    pixels[d] = _scratch[s];
                    pixels[d + 1] = _scratch[s + 1];
                    pixels[d + 2] = _scratch[s + 2];
                    pixels[d + 3] = _scratch[s + 3];
                }
            }
        }

        // ------------------------------------------------------------------ response

        // Roboflow returns boxes as a centre point and a size, in pixels of the picture that was sent.
        [Serializable] class Prediction { public float x, y, width, height, confidence; public string label; }
        [Serializable] class ImageSize { public float width, height; }
        [Serializable] class Response { public ImageSize image; public Prediction[] predictions; }

        List<Detection> Parse(string json, Texture2D sent, Func<Rect, Rect> map)
        {
            var list = new List<Detection>();
            Response parsed;
            try
            {
                // "class" can't be a C# field name, so rename it before handing the text to JsonUtility.
                // "class_id" is left alone — it doesn't match the colon in the pattern.
                parsed = JsonUtility.FromJson<Response>(json.Replace("\"class\":", "\"label\":"));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BuildAR: couldn't read the Roboflow reply. {e.Message}");
                return list;
            }
            if (parsed?.predictions == null) return list;

            float w = parsed.image != null && parsed.image.width > 0 ? parsed.image.width : Mathf.Max(1, sent.width);
            float h = parsed.image != null && parsed.image.height > 0 ? parsed.image.height : Mathf.Max(1, sent.height);

            foreach (var p in parsed.predictions)
            {
                if (p == null || string.IsNullOrEmpty(p.label)) continue;
                var box = new Rect((p.x - p.width / 2f) / w, (p.y - p.height / 2f) / h, p.width / w, p.height / h);
                list.Add(new Detection
                {
                    label = p.label,
                    confidence = p.confidence,
                    screenRect = map(box),
                    hasBox = true,
                });
            }
            list.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            return list;
        }

        /// <summary>Square-crop coords (0..1, top-left) -> screen coords (0..1, top-left), assuming aspect-fill display.</summary>
        Rect CropToScreen(Rect r)
        {
            Vector2 min = CropPointToScreen(new Vector2(r.xMin, r.yMin));
            Vector2 max = CropPointToScreen(new Vector2(r.xMax, r.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        Vector2 CropPointToScreen(Vector2 p)
        {
            float w = Mathf.Max(1, _imageWidth), h = Mathf.Max(1, _imageHeight), side = Mathf.Min(w, h);
            float u = (w - side) / 2f / w + p.x * side / w;
            float v = (h - side) / 2f / h + p.y * side / h;

            float imageAspect = w / h, screenAspect = (float)Screen.width / Screen.height;
            if (screenAspect < imageAspect)
            {
                float f = screenAspect / imageAspect; // visible fraction of image width
                u = (u - (1f - f) / 2f) / f;
            }
            else
            {
                float f = imageAspect / screenAspect; // visible fraction of image height
                v = (v - (1f - f) / 2f) / f;
            }
            return new Vector2(u, v);
        }
    }
}
