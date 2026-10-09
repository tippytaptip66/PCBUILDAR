using UnityEngine;

namespace BuildAR.Data
{
    /// <summary>
    /// A port/connector label shown in the AR scanner when the user taps Show Labels.
    /// positionInBox is normalised inside the detection box: (0,0) top-left, (1,1) bottom-right.
    /// </summary>
    [System.Serializable]
    public class ARLabelDefinition
    {
        public string text;
        public Vector2 positionInBox = new Vector2(0.5f, 0.5f);
        [TextArea] public string description;
    }
}
