using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace BuildAR.AR.Detection
{
    /// <summary>
    /// Runs an ONNX model (imported by com.unity.ai.inference) on ARCore camera frames.
    ///
    /// Supported model outputs (auto-detected from the output shape):
    ///   YOLOv8 / YOLO11 detection   [1, 4 + classes, anchors]   (Ultralytics "export format=onnx")
    ///   YOLOv5 detection            [1, anchors, 5 + classes]
    ///   End-to-end NMS (YOLOv10/26) [1, N, 6]  x1,y1,x2,y2,score,class
    ///   Image classifier            [1, classes]               (the scan frame is used as the box)
    /// Input: [1, 3, inputSize, inputSize], RGB 0..1. Bake any mean/std normalisation into the model.
    ///
    /// Pipeline: centre square crop of the camera image -> rotate to portrait -> tensor -> worker ->
    /// async readback -> decode + NMS -> map to screen coordinates (aspect-fill, like ARCameraBackground).
    /// Stills are padded square instead of cropped (see <see cref="StillFrame"/>) and map to the picture.
    /// </summary>
    public class InferenceComponentDetector : ComponentDetectorBase, IStillDetector
    {
        public enum OutputLayout { Auto, YoloV8, YoloV5, EndToEndNms, Classifier }
        public enum ImageRotation { None, Clockwise90, CounterClockwise90, Rotate180 }

        [Header("AR")]
        [SerializeField] private ARCameraManager cameraManager;

        [Header("Model")]
        [SerializeField] private ModelAsset modelAsset;
        [Tooltip("One class name per line, in the model's class order. Must match ComponentDefinitionSO.mlLabel.")]
        [SerializeField] private TextAsset labelsFile;
        [SerializeField] private int inputSize = 640;
        [SerializeField] private OutputLayout outputLayout = OutputLayout.Auto;
        [SerializeField] private bool preferGpu = true;
        [Tooltip("Run in the Editor too (needs XR Simulation camera images).")]
        [SerializeField] private bool runInEditor = false;

        [Header("Detection")]
        [Range(0f, 1f)] [SerializeField] private float scoreThreshold = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float iouThreshold = 0.5f;
        [SerializeField] private int maxDetections = 5;
        [SerializeField] private float inferencesPerSecond = 5f;

        [Tooltip("How far ahead of the runner-up class a guess must be. Parts that look alike (a GPU and a " +
                 "motherboard, a RAM stick and a slot) make the model split its confidence between them; without a " +
                 "margin it reports whichever is a hair higher, which flips about from frame to frame.")]
        [Range(0f, 0.5f)] [SerializeField] private float classMargin = 0.10f;

        [Tooltip("Frames blurrier than this are thrown away instead of guessed at (0 turns the check off). A moving " +
                 "or out-of-focus frame is the usual reason a scan reports the wrong part. Watch the number in the " +
                 "scanner's debug row: a steady shot of a part reads well above a smeared one.")]
        [SerializeField] private float minSharpness = 0f;

        [Tooltip("Android back camera images are landscape; in a portrait app they must be rotated 90 degrees clockwise. " +
                 "If boxes appear mirrored/rotated on your device, try another value.")]
        [SerializeField] private ImageRotation rotation = ImageRotation.Clockwise90;

        Worker _worker;
        Tensor<float> _input;
        Texture2D _frame;
        byte[] _scratch;
        string[] _labels = Array.Empty<string>();
        bool _running, _loopActive;
        bool _busy;   // the worker is mid-look; the camera loop and a still take turns with it

        // Size of the rotated full camera image, used to map boxes back to the screen.
        int _imageWidth, _imageHeight;

        public override bool IsAvailable => modelAsset != null && labelsFile != null && cameraManager != null && (!Application.isEditor || runInEditor);

        /// <summary>A still needs only the model, so photos can be scanned in the Editor too.</summary>
        public bool CanDetectStills => modelAsset != null && labelsFile != null;

        /// <summary>How camera frames are turned before the model sees them.</summary>
        public string RotationName => rotation.ToString();

        /// <summary>Sharpness of the last camera frame (higher is crisper). Shown on the device to pick a threshold.</summary>
        public float LastSharpness { get; private set; }

        /// <summary>
        /// Writes the exact picture the model was last given into the app's photos folder, where "Scan a picture"
        /// lists it. Worth a look whenever results are poor: it shows at a glance whether the frame is sideways,
        /// mirrored, dark or blurred, any of which makes the model guess.
        /// </summary>
        public string SaveDebugFrame()
        {
            if (_frame == null) return null;
            try
            {
                string folder = ScanPhotoLibrary.EnsurePhotoFolder();
                string path = System.IO.Path.Combine(folder, "model-sees.png");
                System.IO.File.WriteAllBytes(path, _frame.EncodeToPNG());
                Debug.Log($"BuildAR: saved the model's view to {path} (sharpness {LastSharpness:0.0}).");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BuildAR: couldn't save the model's view. {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tries the next rotation. Camera frames arrive landscape and must be turned to match the app; if the turn
        /// is wrong the model sees a sideways picture and its guesses look random. Being able to change it on the
        /// phone saves a rebuild per guess.
        /// </summary>
        public void CycleRotation()
        {
            rotation = (ImageRotation)(((int)rotation + 1) % 4);
            Debug.Log($"BuildAR: camera frame rotation is now {rotation}.");
        }

        public override string UnavailableReason =>
            modelAsset == null ? "No ModelAsset assigned (put your .onnx in Assets/_Project/ML/Models and assign it)."
            : labelsFile == null ? "No labels file assigned."
            : cameraManager == null ? "No ARCameraManager assigned."
            : Application.isEditor && !runInEditor ? "Camera CPU images aren't available in the Editor; using the simulated detector." : "";

        void OnEnable()
        {
            if (!IsAvailable) return;
            // ARCore leaves the camera on a fixed focus by default, which is fine for tracking a room and useless
            // for a part held up close: every frame arrives soft and the model has nothing sharp to work with.
            if (cameraManager != null) cameraManager.autoFocusRequested = true;
            if (_worker == null && !TryInitialise()) return;
            _running = true;
            if (!_loopActive) RunLoop();
        }

        void OnDisable() => _running = false;

        void OnDestroy()
        {
            _running = false;
            _worker?.Dispose();
            _input?.Dispose();
            if (_frame != null) Destroy(_frame);
        }

        /// <summary>
        /// Runs the model once over a still picture instead of the camera — a photo from the phone, or one of the
        /// samples shipped with the app. The region is padded square rather than cropped, so the model sees all of
        /// it, and the boxes come back in the whole picture's coordinates.
        /// </summary>
        public async void DetectStill(Texture source, Rect region, Action<IReadOnlyList<Detection>> done)
        {
            var found = new List<Detection>();
            if (source == null || !CanDetectStills || (_worker == null && !TryInitialise()))
            {
                done?.Invoke(found);
                return;
            }

            bool claimed = false;
            StillFrame frame = null;
            try
            {
                // The camera loop shares the worker: wait for its look to finish instead of overwriting its output.
                while (_busy) await Awaitable.NextFrameAsync(destroyCancellationToken);
                _busy = claimed = true;

                frame = StillFrame.Read(source, region, inputSize, square: true);
                TextureConverter.ToTensor(frame.Texture, _input, new TextureTransform());
                _worker.Schedule(_input);
                using (var output = await (_worker.PeekOutput() as Tensor<float>).ReadbackAndCloneAsync())
                    found = Decode(output.DownloadToArray(), output.shape, frame.ToPicture);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Debug.LogError($"BuildAR: couldn't run the model on that picture. {e.Message}"); }
            finally
            {
                if (claimed) _busy = false;
                frame?.Dispose();
            }
            done?.Invoke(found);
        }

        bool TryInitialise()
        {
            try
            {
                _labels = labelsFile.text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                var model = ModelLoader.Load(modelAsset);
                var backend = preferGpu && SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
                _worker = new Worker(model, backend);
                _input = new Tensor<float>(new TensorShape(1, 3, inputSize, inputSize));
                Debug.Log($"BuildAR: detector ready ({_labels.Length} classes, backend {backend}).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"BuildAR: failed to load detection model. {e}");
                enabled = false;
                return false;
            }
        }

        async void RunLoop()
        {
            _loopActive = true;
            try
            {
                while (_running)
                {
                    await Awaitable.WaitForSecondsAsync(1f / Mathf.Max(0.5f, inferencesPerSecond), destroyCancellationToken);
                    if (!_running) break;
                    if (_busy || !CaptureFrame()) continue;

                    _busy = true;
                    try
                    {
                        TextureConverter.ToTensor(_frame, _input, new TextureTransform());
                        _worker.Schedule(_input);
                        using (var output = await (_worker.PeekOutput() as Tensor<float>).ReadbackAndCloneAsync())
                        {
                            if (!_running) break;
                            Publish(Decode(output.DownloadToArray(), output.shape, CropToScreen));
                        }
                    }
                    finally { _busy = false; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogError($"BuildAR: detection loop stopped. {e}"); }
            finally { _loopActive = false; }
        }

        // ------------------------------------------------------------------ camera frame

        bool CaptureFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image)) return false;
            int side;
            using (image)
            {
                bool swap = rotation == ImageRotation.Clockwise90 || rotation == ImageRotation.CounterClockwise90;
                _imageWidth = swap ? image.height : image.width;
                _imageHeight = swap ? image.width : image.height;

                int crop = Mathf.Min(image.width, image.height);
                side = Mathf.Min(crop, inputSize);
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY)
                {
                    inputRect = new RectInt((image.width - crop) / 2, (image.height - crop) / 2, crop, crop),
                    outputDimensions = new Vector2Int(side, side),
                };

                if (_frame == null || _frame.width != side)
                {
                    if (_frame != null) Destroy(_frame);
                    _frame = new Texture2D(side, side, TextureFormat.RGBA32, false);
                    _scratch = new byte[side * side * 4];
                }
                image.Convert(conversion, _frame.GetRawTextureData<byte>());
            }

            var pixels = _frame.GetRawTextureData<byte>();
            RotateSquare(pixels, side);
            LastSharpness = Sharpness(pixels, side);
            _frame.Apply(false);
            return minSharpness <= 0f || LastSharpness >= minSharpness;
        }

        /// <summary>
        /// Rough measure of how much fine detail a frame holds: the average step in brightness between neighbouring
        /// pixels. A crisp part against a desk is full of edges; a smeared or defocused one is nearly flat.
        /// Sampled every eighth row so it costs almost nothing.
        /// </summary>
        static float Sharpness(NativeArray<byte> pixels, int n)
        {
            long total = 0;
            int count = 0;
            for (int y = 0; y < n; y += 8)
            {
                int row = y * n * 4;
                for (int x = 0; x < n - 2; x += 2)
                {
                    int a = row + x * 4, b = row + (x + 2) * 4;
                    int la = pixels[a] + pixels[a + 1] * 2 + pixels[a + 2];
                    int lb = pixels[b] + pixels[b + 1] * 2 + pixels[b + 2];
                    total += Math.Abs(la - lb);
                    count++;
                }
            }
            return count == 0 ? 0f : total / (4f * count);
        }

        /// <summary>Rotates the RGBA square in place (texture rows are bottom-up).</summary>
        void RotateSquare(NativeArray<byte> pixels, int n)
        {
            if (rotation == ImageRotation.None) return;
            pixels.CopyTo(_scratch);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int dx, dy;
                    switch (rotation)
                    {
                        case ImageRotation.Clockwise90: dx = y; dy = n - 1 - x; break;
                        case ImageRotation.CounterClockwise90: dx = n - 1 - y; dy = x; break;
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

        // ------------------------------------------------------------------ decoding

        struct Candidate { public int cls; public float score; public Rect box; } // box normalised to the square crop

        /// <param name="toOutput">Maps a box in the square the model saw to the screen, or to the still picture.</param>
        List<Detection> Decode(float[] data, TensorShape shape, Func<Rect, Rect> toOutput)
        {
            var layout = outputLayout == OutputLayout.Auto ? DetectLayout(shape) : outputLayout;
            var candidates = new List<Candidate>();

            switch (layout)
            {
                case OutputLayout.Classifier:
                    return DecodeClassifier(data);

                case OutputLayout.YoloV8:
                {
                    int channels = shape[1], anchors = shape[2], classes = channels - 4;
                    for (int a = 0; a < anchors; a++)
                    {
                        int best = -1; float bestScore = 0f, second = 0f;
                        for (int c = 0; c < classes; c++)
                        {
                            float s = data[(4 + c) * anchors + a];
                            if (s > bestScore) { second = bestScore; bestScore = s; best = c; }
                            else if (s > second) second = s;
                        }
                        if (best < 0 || bestScore < scoreThreshold || bestScore - second < classMargin) continue;
                        candidates.Add(new Candidate { cls = best, score = bestScore,
                            box = CenterBox(data[a], data[anchors + a], data[2 * anchors + a], data[3 * anchors + a]) });
                    }
                    break;
                }

                case OutputLayout.YoloV5:
                {
                    int anchors = shape[1], stride = shape[2], classes = stride - 5;
                    for (int a = 0; a < anchors; a++)
                    {
                        int o = a * stride;
                        float objectness = data[o + 4];
                        if (objectness < scoreThreshold) continue;
                        int best = -1; float bestScore = 0f, second = 0f;
                        for (int c = 0; c < classes; c++)
                        {
                            float s = objectness * data[o + 5 + c];
                            if (s > bestScore) { second = bestScore; bestScore = s; best = c; }
                            else if (s > second) second = s;
                        }
                        if (best < 0 || bestScore < scoreThreshold || bestScore - second < classMargin) continue;
                        candidates.Add(new Candidate { cls = best, score = bestScore,
                            box = CenterBox(data[o], data[o + 1], data[o + 2], data[o + 3]) });
                    }
                    break;
                }

                case OutputLayout.EndToEndNms:
                {
                    int rows = shape[1], stride = shape[2];
                    for (int r = 0; r < rows; r++)
                    {
                        int o = r * stride;
                        float score = data[o + 4];
                        if (score < scoreThreshold) continue;
                        float k = Normaliser(Mathf.Max(data[o + 2], data[o + 3]));
                        candidates.Add(new Candidate { cls = Mathf.RoundToInt(data[o + 5]), score = score,
                            box = Rect.MinMaxRect(data[o] * k, data[o + 1] * k, data[o + 2] * k, data[o + 3] * k) });
                    }
                    break;
                }
            }

            var kept = layout == OutputLayout.EndToEndNms ? candidates.OrderByDescending(c => c.score).ToList() : Nms(candidates);
            return kept.Take(maxDetections).Select(c => new Detection
            {
                label = c.cls >= 0 && c.cls < _labels.Length ? _labels[c.cls] : $"class_{c.cls}",
                confidence = c.score,
                screenRect = toOutput(c.box),
                hasBox = true,
            }).ToList();
        }

        OutputLayout DetectLayout(TensorShape shape)
        {
            if (shape.rank == 2) return OutputLayout.Classifier;
            if (shape.rank != 3) throw new InvalidOperationException($"Unsupported model output shape {shape}.");
            if (shape[2] == 6 && shape[1] <= 1000 && _labels.Length != 1) return OutputLayout.EndToEndNms;
            return shape[1] < shape[2] ? OutputLayout.YoloV8 : OutputLayout.YoloV5;
        }

        List<Detection> DecodeClassifier(float[] logits)
        {
            // Apply softmax only if the model outputs raw logits.
            bool isProbabilities = logits.All(v => v >= 0f && v <= 1f) && Mathf.Abs(logits.Sum() - 1f) < 0.05f;
            float[] p = logits;
            if (!isProbabilities)
            {
                float max = logits.Max();
                var exp = logits.Select(v => Mathf.Exp(v - max)).ToArray();
                float sum = exp.Sum();
                p = exp.Select(v => v / sum).ToArray();
            }
            int best = Array.IndexOf(p, p.Max());
            float runnerUp = p.Where((_, i) => i != best).DefaultIfEmpty(0f).Max();
            var list = new List<Detection>();
            if (p[best] >= scoreThreshold && p[best] - runnerUp >= classMargin)
                list.Add(new Detection { label = best < _labels.Length ? _labels[best] : $"class_{best}", confidence = p[best], hasBox = false });
            return list;
        }

        Rect CenterBox(float cx, float cy, float w, float h)
        {
            float k = Normaliser(Mathf.Max(cx, cy));
            return new Rect((cx - w / 2f) * k, (cy - h / 2f) * k, w * k, h * k);
        }

        /// <summary>Most exports use pixel coordinates; some use 0..1.</summary>
        float Normaliser(float sample) => sample > 1.5f ? 1f / inputSize : 1f;

        /// <summary>
        /// Drops boxes that cover the same thing twice. Rivals of a different class are thrown out as well, and
        /// when the rival is nearly as confident the winner goes too: a RAM stick and the slot it sits in, or a GPU
        /// and the board behind it, look alike enough that the model often calls the same patch both, and reporting
        /// whichever scored a hair higher is how a scanner ends up naming the wrong part with great confidence.
        /// </summary>
        List<Candidate> Nms(List<Candidate> input)
        {
            var sorted = input.OrderByDescending(c => c.score).ToList();
            var kept = new List<Candidate>();
            foreach (var c in sorted)
            {
                int rival = kept.FindIndex(k => IoU(k.box, c.box) > iouThreshold);
                if (rival < 0) { kept.Add(c); if (kept.Count >= maxDetections) break; continue; }

                // Same class: just the duplicate box. Different class, close score: neither can be trusted.
                if (kept[rival].cls != c.cls && kept[rival].score - c.score < classMargin) kept.RemoveAt(rival);
            }
            return kept;
        }

        static float IoU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
            float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float union = a.width * a.height + b.width * b.height - inter;
            return union <= 0 ? 0 : inter / union;
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
