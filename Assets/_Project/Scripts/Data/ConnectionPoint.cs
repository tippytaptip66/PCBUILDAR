using UnityEngine;
namespace BuildAR.Data
{
    [System.Serializable]
    public class ConnectionPoint
    {
        public string label;
        public Vector3 localPosition;
        public ComponentCategory connectsTo;
        [TextArea] public string description;
    }
}
