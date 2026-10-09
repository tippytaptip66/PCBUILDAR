using UnityEngine;
using BuildAR.Data;

namespace BuildAR.Assembly
{
    /// <summary>
    /// A place in the case where a component snaps in. The transform is the exact pose the component's
    /// pivot (its geometric centre) will take, so position/rotate this object to fit your case model.
    /// </summary>
    public class SnapSlot : MonoBehaviour
    {
        [Tooltip("Referenced by AssemblyStep.validSlotIds, e.g. ram_a2.")]
        public string slotId;
        public ComponentCategory acceptsCategory;

        [Tooltip("False = works but isn't best practice (e.g. RAM in A1/B1). Placing here gives a warning.")]
        public bool recommended = true;

        [Tooltip("Mounted on the case rather than the motherboard (a drive mount in the PSU basement). Import Assembly " +
                 "Case Model moves it, its glow box and its ports with the case's floor and rear wall, as it does the PSU bay.")]
        public bool mountedOnCase;
        [TextArea] public string notRecommendedMessage;

        [Tooltip("Glowing outline shown when this slot is a target.")]
        [SerializeField] private Renderer highlight;

        public Draggable Occupant { get; internal set; }
        public bool IsOccupied => Occupant != null;

        HighlightState _state;

        public void SetHighlight(HighlightState state)
        {
            _state = state;
            Highlight.Apply(highlight, state);
        }

        void Update()
        {
            if (_state == HighlightState.Target)
                Highlight.Apply(highlight, _state, 0.55f + 0.45f * Mathf.Sin(Time.time * 4f));
        }
    }
}
