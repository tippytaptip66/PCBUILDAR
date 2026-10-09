using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.Data
{
    /// <summary>
    /// Ordered steps for Virtual Assembly (and later Guided Real Assembly).
    /// Slot / port ids must match SnapSlot.slotId and CablePort.connectorType in the case prefab.
    /// </summary>
    [CreateAssetMenu(menuName = "BuildAR/Assembly Guide", fileName = "NewAssemblyGuide")]
    public class AssemblyGuideSO : ScriptableObject
    {
        public string guideId;
        public string title;
        public List<AssemblyStep> steps = new List<AssemblyStep>();
    }

    [System.Serializable]
    public class AssemblyStep
    {
        public enum StepKind { PlaceComponent, ConnectCable }

        public string stepId;
        public string title;
        [TextArea] public string instruction;
        public StepKind kind;

        [Tooltip("PlaceComponent: the ComponentDefinitionSO id to install.")]
        public string componentId;

        [Tooltip("PlaceComponent: slots that count as correct. Empty = any slot of the right category.")]
        public List<string> validSlotIds = new List<string>();

        [Tooltip("ConnectCable: connector type both ports must share, e.g. ATX24.")]
        public string connectorType;

        [TextArea] public string safetyTip;

        [Tooltip("Optional badge id unlocked when this step is completed.")]
        public string badgeOnComplete;
    }
}
