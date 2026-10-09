using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.AR.Detection
{
    public struct Detection
    {
        /// <summary>Class name from the labels file, matched against ComponentDefinitionSO.mlLabel.</summary>
        public string label;
        public float confidence;
        /// <summary>
        /// Normalised screen rect, origin top-left (0..1). From <see cref="IStillDetector.DetectStill"/> it is
        /// normalised to the whole picture instead. Ignored when hasBox is false.
        /// </summary>
        public Rect screenRect;
        /// <summary>False for image classifiers: the UI uses the scan frame as the box.</summary>
        public bool hasBox;
    }

    /// <summary>A detector that can also be pointed at a single picture instead of the camera feed.</summary>
    public interface IStillDetector
    {
        /// <summary>True when stills can be read here, even if the live camera can't (the Editor, say).</summary>
        bool CanDetectStills { get; }

        /// <summary>
        /// Looks at <paramref name="region"/> of the picture (0..1, origin top-left; the whole picture is 0,0,1,1)
        /// with nothing cut off, and reports boxes normalised to the whole picture, whatever region was read.
        /// </summary>
        void DetectStill(Texture source, Rect region, Action<IReadOnlyList<Detection>> done);
    }

    /// <summary>Anything that turns camera frames into detections (ML model, simulator, marker tracking...).</summary>
    public abstract class ComponentDetectorBase : MonoBehaviour
    {
        /// <summary>Raised on the main thread with the latest detections (possibly empty).</summary>
        public event Action<IReadOnlyList<Detection>> DetectionsUpdated;

        /// <summary>True when this detector can run on the current platform with its current settings.</summary>
        public abstract bool IsAvailable { get; }

        /// <summary>Human-readable reason when IsAvailable is false (shown in logs).</summary>
        public virtual string UnavailableReason => "";

        protected void Publish(IReadOnlyList<Detection> detections) => DetectionsUpdated?.Invoke(detections);
    }
}
