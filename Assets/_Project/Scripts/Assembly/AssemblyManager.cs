using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BuildAR.Data;
using BuildAR.Managers;

namespace BuildAR.Assembly
{
    public enum PlacementFeedback { Correct, Incorrect, Warning }

    /// <summary>
    /// Rules for Virtual Assembly: follows an AssemblyGuideSO step by step, validates placements and cable
    /// connections, and reports correct / incorrect / warning feedback. Once the build is complete, its power button
    /// switches it on: fans spin and lights glow only then.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AssemblyManager : MonoBehaviour
    {
        public static AssemblyManager Instance { get; private set; }

        [SerializeField] private string guideId = "first_build";

        public event Action<PlacementFeedback, string> OnPlacementFeedback;
        public event Action<int, AssemblyStep> OnStepChanged;
        public event Action<CablePort, CablePort> OnCableConnected;
        public event Action OnBuildCompleted;
        public event Action<bool> OnPowerChanged;

        public AssemblyGuideSO Guide { get; private set; }
        public int CurrentStepIndex { get; private set; }
        public AssemblyStep CurrentStep => Guide != null && CurrentStepIndex < Guide.steps.Count ? Guide.steps[CurrentStepIndex] : null;
        public bool IsComplete => Guide != null && CurrentStepIndex >= Guide.steps.Count;
        public bool IsReady => Guide != null;

        /// <summary>The finished PC has been switched on. Not saved: every visit starts with it off.</summary>
        public bool PoweredOn { get; private set; }

        /// <summary>
        /// Whether fans turn and LEDs glow. Outside Virtual Assembly (the 3D viewer, the scanner) there's no build to
        /// power, so always; in it, only while the finished PC is switched on.
        /// </summary>
        public static bool HasPower => Instance == null || Instance.PoweredOn;

        /// <summary>The case's power button, or null if the case model has none.</summary>
        public PowerButton PowerButton { get; private set; }

        SnapSlot[] _slots = Array.Empty<SnapSlot>();
        CablePort[] _ports = Array.Empty<CablePort>();
        readonly List<ComponentDefinitionSO> _placed = new List<ComponentDefinitionSO>();

        public IReadOnlyList<SnapSlot> Slots => _slots;
        public IReadOnlyList<CablePort> Ports => _ports;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        IEnumerator Start()
        {
            while (ComponentDatabase.Instance == null) yield return null; // DevBootstrap may still be loading managers
            Guide = ComponentDatabase.Instance.GetGuide(guideId) ?? ComponentDatabase.Instance.DefaultGuide;
            if (Guide == null) { Debug.LogError("BuildAR: no AssemblyGuide found. Run BuildAR > Setup > Generate Sample Content."); yield break; }

            _slots = FindObjectsByType<SnapSlot>(FindObjectsInactive.Include);
            _ports = FindObjectsByType<CablePort>(FindObjectsInactive.Include);
            PowerButton = FindAnyObjectByType<PowerButton>(FindObjectsInactive.Include);   // the case is hidden while AR looks for a table
            foreach (var root in gameObject.scene.GetRootGameObjects()) IndicatorLeds.AddTo(root);
            RestoreProgress();
        }

        // ------------------------------------------------------------------ progress

        /// <summary>Re-creates already-completed steps so "Resume" continues where the user left off.</summary>
        void RestoreProgress()
        {
            var done = ProgressManager.Instance != null ? ProgressManager.Instance.Data.completedAssemblyStepIds : new List<string>();
            CurrentStepIndex = 0;
            while (CurrentStepIndex < Guide.steps.Count && done.Contains(Guide.steps[CurrentStepIndex].stepId))
            {
                var step = Guide.steps[CurrentStepIndex];
                if (step.kind == AssemblyStep.StepKind.PlaceComponent)
                {
                    var def = ComponentDatabase.Instance.GetById(step.componentId);
                    var slot = ValidSlotsFor(step).FirstOrDefault(s => !s.IsOccupied && s.recommended) ?? ValidSlotsFor(step).FirstOrDefault(s => !s.IsOccupied);
                    if (def != null && slot != null) Install(Spawn(def), slot, animate: false);
                }
                else
                {
                    var src = _ports.FirstOrDefault(p => p.isSource && !p.IsConnected && p.connectorType == step.connectorType);
                    var dst = _ports.FirstOrDefault(p => !p.isSource && !p.IsConnected && p.connectorType == step.connectorType);
                    if (src != null && dst != null) Connect(src, dst);
                }
                CurrentStepIndex++;
            }
            RefreshPorts();
            RaiseStepChanged();
        }

        public void RestartBuild()
        {
            if (Guide == null) return;
            SetPower(false);
            ProgressManager.Instance?.ResetAssemblySteps(Guide);
            foreach (var d in FindObjectsByType<Draggable>()) Destroy(d.gameObject);
            foreach (var s in _slots) s.Occupant = null;
            foreach (var p in _ports) p.IsConnected = false;
            _placed.Clear();
            CurrentStepIndex = 0;
            RefreshPorts();
            RaiseStepChanged();
        }

        // ------------------------------------------------------------------ placement

        public Draggable Spawn(ComponentDefinitionSO def)
        {
            GameObject go;
            if (def.model3DPrefab != null) go = Instantiate(def.model3DPrefab);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = Vector3.one * 0.05f;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false; // parts never block port taps
            IndicatorLeds.AddTo(go);
            go.name = def.displayName;
            if (!go.TryGetComponent(out Draggable d)) d = go.AddComponent<Draggable>();
            d.definition = def;
            return d;
        }

        public IEnumerable<SnapSlot> SlotsFor(ComponentCategory category) => _slots.Where(s => s.acceptsCategory == category);

        IEnumerable<SnapSlot> ValidSlotsFor(AssemblyStep step)
        {
            var def = ComponentDatabase.Instance.GetById(step.componentId);
            if (def == null) return Enumerable.Empty<SnapSlot>();
            return SlotsFor(def.category).Where(s => step.validSlotIds.Count == 0 || step.validSlotIds.Contains(s.slotId) || !s.recommended);
        }

        /// <summary>Returns true if the part was installed.</summary>
        public bool TryPlace(Draggable draggable, SnapSlot slot)
        {
            var def = draggable.definition;
            var step = CurrentStep;

            if (step == null) return Fail("The build is already complete.");
            if (slot.IsOccupied) return Fail($"That slot already has {slot.Occupant.definition.displayName} in it.");
            if (def.category != slot.acceptsCategory)
                return Fail($"{def.displayName} doesn't fit there — that slot takes a {slot.acceptsCategory.DisplayName()}.");

            if (step.kind != AssemblyStep.StepKind.PlaceComponent || step.componentId != def.id)
                return Fail($"Not yet! Current step: {step.title}.");

            foreach (var r in CompatibilityChecker.CheckBuild(_placed.Append(def)))
                if (r.status == CompatibilityStatus.Incompatible && (r.componentA == def || r.componentB == def))
                    return Fail(r.message);

            bool listed = step.validSlotIds.Count == 0 || step.validSlotIds.Contains(slot.slotId);
            if (!listed && slot.recommended) return Fail("Wrong slot — use the glowing one.");

            Install(draggable, slot, animate: true);

            if (!listed)
            {
                Warn(string.IsNullOrEmpty(slot.notRecommendedMessage) ? "It works, but that isn't the recommended slot." : slot.notRecommendedMessage);
                StartCoroutine(FlashSlot(slot, HighlightState.Warning));
            }
            else
            {
                Succeed($"{def.displayName} installed correctly.");
                StartCoroutine(FlashSlot(slot, HighlightState.Correct));
            }
            CompleteCurrentStep();
            return true;
        }

        void Install(Draggable d, SnapSlot slot, bool animate)
        {
            slot.Occupant = d;
            d.Slot = slot;
            d.SnapTo(slot.transform, animate);
            _placed.Add(d.definition);
        }

        // ------------------------------------------------------------------ cables

        public bool TryConnect(CablePort a, CablePort b)
        {
            var step = CurrentStep;
            if (a == b) return false;
            if (a.isSource == b.isSource)
                return Fail(a.isSource ? "Both of those are cable ends — tap a port on the board next." : "Start from a cable on the PSU or case.");

            var source = a.isSource ? a : b;
            var target = a.isSource ? b : a;

            if (source.IsConnected || target.IsConnected) return Fail("That connector is already plugged in.");
            if (source.connectorType != target.connectorType)
                return Fail($"The {source.displayName} cable doesn't fit the {target.displayName} header.");
            if (step == null || step.kind != AssemblyStep.StepKind.ConnectCable || step.connectorType != source.connectorType)
                return Fail(step == null ? "The build is already complete." : $"Right match, wrong time. Current step: {step.title}.");

            Connect(source, target);
            Succeed($"{source.displayName} connected.");
            CompleteCurrentStep();
            return true;
        }

        void Connect(CablePort source, CablePort target)
        {
            source.IsConnected = target.IsConnected = true;
            source.SetHighlight(HighlightState.None);
            target.SetHighlight(HighlightState.None);
            OnCableConnected?.Invoke(source, target);
        }

        // ------------------------------------------------------------------ power

        /// <summary>The case's power button was pressed: starts the finished PC, or shuts it down if it's running.</summary>
        public void PressPower()
        {
            if (Guide == null) return;
            if (!IsComplete)
            {
                int left = Guide.steps.Count - CurrentStepIndex;
                Warn($"Nothing happens yet: the PC needs every part and cable first. {left} {(left == 1 ? "step" : "steps")} to go.");
                return;
            }
            SetPower(!PoweredOn);
            if (PoweredOn) Succeed("It's on! The fans are spinning and the lights are on.");
            else
            {
                AudioManager.Instance?.PlayClick();
                OnPlacementFeedback?.Invoke(PlacementFeedback.Correct, "Shut down. Press the power button to start it again.");
            }
        }

        void SetPower(bool on)
        {
            if (PoweredOn == on) return;
            PoweredOn = on;
            OnPowerChanged?.Invoke(on);
        }

        // ------------------------------------------------------------------ steps

        void CompleteCurrentStep()
        {
            var step = CurrentStep;
            var progress = ProgressManager.Instance;
            progress?.MarkAssemblyStepComplete(step.stepId);
            if (!string.IsNullOrEmpty(step.badgeOnComplete)) progress?.UnlockBadge(step.badgeOnComplete);

            CurrentStepIndex++;
            RefreshPorts();
            if (IsComplete)
            {
                progress?.UnlockBadge(BadgeCatalog.FullBuild);
                OnBuildCompleted?.Invoke();
            }
            RaiseStepChanged();
        }

        void RefreshPorts()
        {
            foreach (var p in _ports)
            {
                bool active = string.IsNullOrEmpty(p.requiresSlotId) || _slots.Any(s => s.slotId == p.requiresSlotId && s.IsOccupied);
                p.gameObject.SetActive(active);
            }
        }

        void RaiseStepChanged() => OnStepChanged?.Invoke(CurrentStepIndex, CurrentStep);

        /// <summary>Glows the slots or ports the current step needs.</summary>
        public void ShowHint(bool on)
        {
            var step = CurrentStep;
            foreach (var s in _slots) s.SetHighlight(HighlightState.None);
            foreach (var p in _ports) if (!p.IsConnected) p.SetHighlight(HighlightState.None);
            if (!on || step == null) return;

            if (step.kind == AssemblyStep.StepKind.PlaceComponent)
            {
                var def = ComponentDatabase.Instance.GetById(step.componentId);
                foreach (var s in SlotsFor(def != null ? def.category : ComponentCategory.Case))
                    if (!s.IsOccupied && (step.validSlotIds.Count == 0 || step.validSlotIds.Contains(s.slotId)))
                        s.SetHighlight(HighlightState.Target);
            }
            else
            {
                foreach (var p in _ports)
                    if (!p.IsConnected && p.gameObject.activeInHierarchy && p.connectorType == step.connectorType)
                        p.SetHighlight(HighlightState.Target);
            }
        }

        IEnumerator FlashSlot(SnapSlot slot, HighlightState state)
        {
            slot.SetHighlight(state);
            yield return new WaitForSeconds(0.9f);
            slot.SetHighlight(HighlightState.None);
        }

        bool Fail(string msg)
        {
            AudioManager.Instance?.PlayIncorrect();
            OnPlacementFeedback?.Invoke(PlacementFeedback.Incorrect, msg);
            return false;
        }

        void Warn(string msg)
        {
            AudioManager.Instance?.PlayWarning();
            OnPlacementFeedback?.Invoke(PlacementFeedback.Warning, msg);
        }

        void Succeed(string msg)
        {
            AudioManager.Instance?.PlayCorrect();
            OnPlacementFeedback?.Invoke(PlacementFeedback.Correct, msg);
        }
    }
}
