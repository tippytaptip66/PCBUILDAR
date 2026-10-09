using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// A connector end in Virtual Assembly. The user taps a source (PSU / case cable) then a target
    /// (motherboard / GPU header). Needs a Collider for tapping — make it a bit larger than the visual.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CablePort : MonoBehaviour
    {
        public string portId;
        public string displayName = "24-pin ATX";
        [Tooltip("Both ends must share this, e.g. ATX24, EPS8, PCIE8, FPANEL, SATA.")]
        public string connectorType;
        public bool isSource;
        [Tooltip("Port only appears once this slot is occupied (e.g. PSU cables need the PSU installed). Empty = always.")]
        public string requiresSlotId;
        public Color cableColor = new Color(0.1f, 0.1f, 0.12f);

        [Tooltip("On a header: where the cable comes out from behind the motherboard tray to reach it (a grommet), as " +
                 "in a tidy real build. Empty: the cable arcs across the front of the case instead.")]
        public Transform routeExit;

        [Tooltip("On a header: the cable runs straight to it by the shortest tidy path (a drive right beside the PSU) " +
                 "instead of behind the tray or across the case.")]
        public bool direct;

        [Tooltip("On a cable end: where the cable's other end is plugged in (a data cable already fitted to the " +
                 "motherboard, hidden behind the tray). Empty: the PSU's panel for PSU cables, or the loose end itself.")]
        public Transform origin;

        [SerializeField] private Renderer highlight;

        bool _connected;
        HighlightState _state;
        GameObject _slack;

        /// <summary>
        /// Plugged in. A cable end (source) hides its loose connector and the cable hanging to it while it is,
        /// because both are now the cable running to the header.
        /// </summary>
        public bool IsConnected
        {
            get => _connected;
            internal set
            {
                _connected = value;
                if (!isSource) return;
                var loose = transform.Find("Visual");
                if (loose != null) loose.gameObject.SetActive(!value);
                if (_slack != null) _slack.SetActive(!value);
            }
        }

        void OnEnable()
        {
            // A cable end appears (the PSU went in, say): hang its cable from where it comes from, so the connector
            // isn't floating on its own. Built once; the port's own activation shows and hides it after that.
            if (!isSource || !Application.isPlaying || _slack != null) return;
            _slack = CableBuilder.BuildSlack(this);
            if (_slack != null) _slack.SetActive(!_connected);
        }

        public void SetHighlight(HighlightState state)
        {
            _state = state;
            Highlight.Apply(highlight, state);
        }

        void Update()
        {
            if (_state == HighlightState.Target)
                Highlight.Apply(highlight, _state, 0.55f + 0.45f * Mathf.Sin(Time.time * 5f));
        }
    }
}
