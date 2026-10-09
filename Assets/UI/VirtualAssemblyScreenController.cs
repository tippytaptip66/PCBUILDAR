using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Assembly;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.UI;

/// <summary>
/// Virtual Assembly UI: full-screen 3D case with a floating top bar, parts tray (right) and instruction card (bottom), feedback states,
/// safety tips and pause. Attach next to a UIDocument (VirtualAssembly.uxml) in the Virtual Assembly scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class VirtualAssemblyScreenController : MonoBehaviour, AssemblyInteraction.IUiHitTest
{
    [SerializeField] private Camera assemblyCamera;
    [SerializeField] private AssemblyInteraction interaction;

    UIDocument _doc;
    VisualElement _root, _viewport, _instruction, _tray, _modal;
    bool _hintOn;
    bool _celebrated;   // the "Build complete" card has shown for this build's first power-on
    enum ModalMode { Pause, Safety, Complete }
    ModalMode _modalMode;

    AssemblyManager Manager => AssemblyManager.Instance;
    AssemblyARController AR => AssemblyARController.Instance;

    // ------------------------------------------------------------------ AR placement

    void OnArStateChanged(AssemblyARController.Mode mode)
    {
        bool placing = mode == AssemblyARController.Mode.Scanning || mode == AssemblyARController.Mode.ReadyToPlace;
        bool checking = mode == AssemblyARController.Mode.Checking;

        // The scan button both enters AR from the 3D view and re-places the case once it's in the room.
        bool canEnterAr = mode == AssemblyARController.Mode.ThreeD && AR != null && AR.ArSupported;

        UIUtil.SetVisible(_root.Q("ar-panel"), placing);
        UIUtil.SetVisible(_root.Q("btn-place-case"), mode == AssemblyARController.Mode.ReadyToPlace);
        UIUtil.SetVisible(_root.Q("btn-move-case"), mode == AssemblyARController.Mode.Placed || canEnterAr);
        UIUtil.SetVisible(_root.Q("asm-tray"), !placing && !checking);
        UIUtil.SetVisible(_root.Q("asm-view-tools"), !placing && !checking);
        UIUtil.SetVisible(_root.Q("view-hint"), !placing && !checking);
        UIUtil.SetVisible(_instruction, !placing && !checking);

        if (mode == AssemblyARController.Mode.Scanning)
        {
            _root.Q<LineIcon>("ar-panel-icon").icon = "scan";
            _root.Q<Label>("ar-panel-title").text = "Find a table";
            _root.Q<Label>("ar-panel-body").text = "Point your camera at a table or the floor and move your phone slowly until an outline appears.";
        }
        else if (mode == AssemblyARController.Mode.ReadyToPlace)
        {
            _root.Q<LineIcon>("ar-panel-icon").icon = "cube";
            _root.Q<Label>("ar-panel-title").text = "Place your case";
            _root.Q<Label>("ar-panel-body").text = "The outline shows where the case will sit. Tap the screen or press Place.";
        }

        if (Manager != null && Manager.IsReady && Manager.CurrentStep != null)
            _root.Q<Label>("view-hint-label").text = HintText(Manager.CurrentStep);
        FitCameraToViewport();
    }

    string HintText(AssemblyStep step)
    {
        bool cable = step.kind == AssemblyStep.StepKind.ConnectCable;
        string action = cable ? "Tap a cable end, then its port" : "Drag a part onto the glowing slot";
        if (AR == null) return action;
        if (AR.IsAR) return action + " · swipe to turn the case";
        return AR.ArSupported ? action + " · tap the scan button to build in your room" : action;
    }

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        _root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(_root);

        _viewport = _root.Q("asm-viewport");
        _instruction = _root.Q("instruction");
        _tray = _root.Q("tray");
        _modal = _root.Q("modal");

        UIUtil.OnClick(_root, "btn-back", () => GameManager.Instance?.ShowScreen(AppScreen.Home));
        UIUtil.OnClick(_root, "btn-pause", () => ShowModal(ModalMode.Pause));
        UIUtil.OnClick(_root, "btn-rotate-left", () => interaction?.RotateBy(-45f));
        UIUtil.OnClick(_root, "btn-rotate-right", () => interaction?.RotateBy(45f));
        UIUtil.OnClick(_root, "btn-reset-view", () => interaction?.ResetView());
        UIUtil.OnClick(_root, "btn-hint", ToggleHint);
        UIUtil.OnClick(_root, "btn-safety", () => ShowModal(ModalMode.Safety));
        UIUtil.OnClick(_root, "btn-modal-primary", CloseModal);
        UIUtil.OnClick(_root, "btn-modal-secondary", RestartBuild);
        UIUtil.OnClick(_root, "btn-modal-exit", () => GameManager.Instance?.ShowScreen(AppScreen.Home));
        UIUtil.OnClick(_root, "btn-place-case", () => AR?.Place());
        UIUtil.OnClick(_root, "btn-use-3d", () => AR?.UseThreeD());
        UIUtil.OnClick(_root, "btn-move-case", () =>
        {
            if (AR == null) return;
            if (AR.IsAR) AR.BeginScanning();   // already in the room: pick a new spot
            else AR.EnterAR();                 // in the 3D view: move the build into the room
        });
        if (AR != null)
        {
            AR.StateChanged += OnArStateChanged;
            OnArStateChanged(AR.State);
        }

        _viewport.RegisterCallback<GeometryChangedEvent>(_ => FitCameraToViewport());
        _root.Q("asm-tray").RegisterCallback<GeometryChangedEvent>(_ => FitCameraToViewport());
        if (interaction != null) interaction.UiHitTest = this;

        if (Manager != null)
        {
            Manager.OnStepChanged += OnStepChanged;
            Manager.OnPlacementFeedback += OnFeedback;
            Manager.OnPowerChanged += OnPowerChanged;
            if (Manager.IsReady) OnStepChanged(Manager.CurrentStepIndex, Manager.CurrentStep);
        }
        if (ProgressManager.Instance != null) ProgressManager.Instance.OnBadgeUnlocked += OnBadge;
    }

    void OnDisable()
    {
        if (AR != null) AR.StateChanged -= OnArStateChanged;
        if (Manager != null)
        {
            Manager.OnStepChanged -= OnStepChanged;
            Manager.OnPlacementFeedback -= OnFeedback;
            Manager.OnPowerChanged -= OnPowerChanged;
        }
        if (ProgressManager.Instance != null) ProgressManager.Instance.OnBadgeUnlocked -= OnBadge;
    }

    // ------------------------------------------------------------------ layout / hit testing

    /// <summary>
    /// The 3D camera renders full screen; its projection centre is shifted into the free area (between the top bar,
    /// instruction card and parts tray) so the case isn't hidden behind the panels.
    /// </summary>
    void FitCameraToViewport()
    {
        // In AR the camera projection comes from the phone's camera and must not be changed.
        if (assemblyCamera == null || _root.panel == null || (AR != null && AR.IsAR)) return;
        var panelSize = _root.panel.visualTree.layout.size;
        var free = _viewport.worldBound;
        var tray = _root.Q("asm-tray").worldBound;
        if (panelSize.x <= 0 || float.IsNaN(free.width) || float.IsNaN(tray.width)) return;

        float right = Mathf.Min(free.xMax, tray.xMin);
        float centerX = (free.xMin + right) * 0.5f / panelSize.x;
        float centerY = 1f - (free.yMin + free.yMax) * 0.5f / panelSize.y;

        assemblyCamera.rect = new Rect(0, 0, 1, 1);
        assemblyCamera.ResetProjectionMatrix();
        var m = assemblyCamera.projectionMatrix;
        m.m02 = -(centerX * 2f - 1f);
        m.m12 = -(centerY * 2f - 1f);
        assemblyCamera.projectionMatrix = m;
    }

    public bool IsOverUI(Vector2 screenPosition)
    {
        var panel = _root?.panel;
        if (panel == null) return false;
        var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        var picked = panel.Pick(panelPos);
        return picked != null && picked != _root && picked != panel.visualTree && picked.name != "asm-viewport" && !picked.ClassListContains("screen");
    }

    // ------------------------------------------------------------------ steps & tray

    void OnStepChanged(int index, AssemblyStep step)
    {
        var guide = Manager.Guide;
        int total = guide.steps.Count;
        _root.Q<Label>("step-count").text = step == null ? "Build complete" : $"Step {index + 1} of {total}";
        _root.Q("step-fill").style.width = Length.Percent(100f * index / Mathf.Max(1, total));

        SetState(null);
        if (step != null)
        {
            bool cable = step.kind == AssemblyStep.StepKind.ConnectCable;
            _root.Q<Label>("instruction-kicker").text = $"STEP {index + 1} · {(cable ? "CONNECT" : "INSTALL")}";
            _root.Q<Label>("instruction-title").text = step.title;
            _root.Q<Label>("instruction-text").text = step.instruction;
            _root.Q<LineIcon>("step-kind-icon").icon = cable ? "cable" : "hand";
            _root.Q<Label>("step-kind-label").text = cable ? "Tap to connect" : "Drag & drop";
            _root.Q<Label>("view-hint-label").text = HintText(step);
        }
        else ShowPowerStep();

        BuildTray();
        _hintOn = false;
        Manager.ShowHint(false);
        _root.Q<Label>("btn-hint-label").text = Manager.IsComplete && Manager.PowerButton == null ? "Power on" : "Show me";
    }

    /// <summary>Every part is in and every cable connected: the last thing to do is switch the PC on.</summary>
    void ShowPowerStep()
    {
        bool on = Manager.PoweredOn;
        _root.Q<Label>("step-count").text = on ? "Build complete" : "Ready to power on";
        _root.Q<Label>("instruction-kicker").text = on ? "ALL DONE · POWERED ON" : "LAST STEP · POWER ON";
        _root.Q<Label>("instruction-title").text = on ? "Your PC is running!" : "Turn it on";
        _root.Q<Label>("instruction-text").text = on
            ? "The fans are spinning and the lights are on. Press the power button again to shut it down."
            : "Every part is installed and every cable connected. Press the glowing power button on top of the case.";
        _root.Q<LineIcon>("step-kind-icon").icon = "power";
        _root.Q<Label>("step-kind-label").text = on ? "On" : "Tap to start";
        _root.Q<Label>("view-hint-label").text = on ? "Tap the power button to shut it down" : "Tap the glowing power button";
    }

    void OnPowerChanged(bool on)
    {
        if (!Manager.IsComplete) return;
        SetState(null);
        ShowPowerStep();
        if (!on || _celebrated) return;
        _celebrated = true;
        // Long enough to watch the fans spin up and the lights come on first.
        _root.schedule.Execute(() => { if (Manager != null && Manager.PoweredOn) ShowModal(ModalMode.Complete); }).StartingIn(2600);
    }

    void BuildTray()
    {
        _tray.Clear();
        var guide = Manager.Guide;
        var db = ComponentDatabase.Instance;
        if (guide == null || db == null) return;

        var current = Manager.CurrentStep;
        var groups = guide.steps.Select((s, i) => (s, i))
            .Where(x => x.s.kind == AssemblyStep.StepKind.PlaceComponent)
            .GroupBy(x => x.s.componentId);

        foreach (var g in groups)
        {
            var def = db.GetById(g.Key);
            if (def == null) continue;
            int remaining = g.Count(x => x.i >= Manager.CurrentStepIndex);
            bool isNext = current != null && current.componentId == def.id;

            var item = UIUtil.Box("tray-item");
            item.EnableInClassList("next", isNext);
            item.EnableInClassList("done", remaining == 0);
            item.Add(new LineIcon(remaining == 0 ? "check-circle" : def.category.IconName()));
            item.Add(UIUtil.Text(ShortName(def), "tray-name"));
            if (remaining > 1) item.Add(UIUtil.Text($"×{remaining}", "tray-count"));

            if (remaining > 0)
                item.RegisterCallback<PointerDownEvent>(_ => { if (_modal.resolvedStyle.display == DisplayStyle.None) interaction?.BeginDrag(def); });
            _tray.Add(item);
        }
    }

    static string ShortName(ComponentDefinitionSO def)
    {
        switch (def.category)
        {
            case ComponentCategory.PowerSupply: return "PSU";
            case ComponentCategory.GraphicsCard: return "GPU";
            case ComponentCategory.Storage:
                // Two kinds of drive in one build: "Storage Drive (M.2 SSD)" → "M.2 SSD".
                int open = def.displayName.IndexOf('('), close = def.displayName.LastIndexOf(')');
                return open >= 0 && close > open ? def.displayName.Substring(open + 1, close - open - 1) : "SSD";
            case ComponentCategory.Cooling: return "Cooler";
            default: return def.category.DisplayName();
        }
    }

    // ------------------------------------------------------------------ feedback

    void OnFeedback(PlacementFeedback feedback, string message)
    {
        SetState(feedback);
        _root.Q<Label>("feedback-text").text = message;
        _root.Q<LineIcon>("feedback-icon").icon = feedback == PlacementFeedback.Correct ? "check-circle"
                                                : feedback == PlacementFeedback.Warning ? "warning" : "close";
    }

    void SetState(PlacementFeedback? feedback)
    {
        _instruction.EnableInClassList("state-correct", feedback == PlacementFeedback.Correct);
        _instruction.EnableInClassList("state-incorrect", feedback == PlacementFeedback.Incorrect);
        _instruction.EnableInClassList("state-warning", feedback == PlacementFeedback.Warning);
    }

    void ToggleHint()
    {
        if (Manager != null && Manager.IsComplete)
        {
            // The power step: turn the view to the button (its ring is already pulsing), or press power for a case
            // model that has no button to tap.
            if (Manager.PowerButton == null) Manager.PressPower();
            else interaction?.LookAtTop();
            return;
        }
        _hintOn = !_hintOn;
        Manager?.ShowHint(_hintOn);
        _root.Q<Label>("btn-hint-label").text = _hintOn ? "Hide hint" : "Show me";
    }

    void OnBadge(BadgeCatalog.Badge b) => UIUtil.Toast(_root, "Badge unlocked!", b.title, b.icon);

    // ------------------------------------------------------------------ modal

    void ShowModal(ModalMode mode)
    {
        _modalMode = mode;
        var step = Manager?.CurrentStep;
        string icon, title, body, primary, secondary;
        switch (mode)
        {
            case ModalMode.Safety:
                icon = "shield"; title = "Safety tip";
                body = step != null && !string.IsNullOrEmpty(step.safetyTip)
                    ? step.safetyTip
                    : "Switch off and unplug the PC, touch bare metal on the case to discharge static, and handle parts by their edges.";
                primary = "Got it"; secondary = null;
                ProgressManager.Instance?.RecordSafetyTipViewed();
                break;
            case ModalMode.Complete:
                icon = "trophy"; title = "Build complete!";
                body = "You installed every component, connected all the cables and powered it on. Your PC boots!";
                primary = "Keep exploring"; secondary = "Build again";
                break;
            default:
                icon = "pause"; title = "Paused";
                body = "Your progress is saved after every step.";
                primary = "Resume"; secondary = "Restart build";
                break;
        }
        _root.Q<LineIcon>("modal-icon").icon = icon;
        _root.Q("modal-icon-box").EnableInClassList("action-icon--orange", mode == ModalMode.Safety);
        _root.Q<Label>("modal-title").text = title;
        _root.Q<Label>("modal-body").text = body;
        _root.Q<Label>("btn-modal-primary-label").text = primary;
        UIUtil.SetVisible(_root.Q("btn-modal-secondary"), secondary != null);
        if (secondary != null) _root.Q<Label>("btn-modal-secondary-label").text = secondary;
        UIUtil.SetVisible(_root.Q("btn-modal-exit"), mode != ModalMode.Safety);
        UIUtil.SetVisible(_modal, true);
        if (interaction != null) interaction.InputEnabled = false;
    }

    void CloseModal()
    {
        UIUtil.SetVisible(_modal, false);
        if (interaction != null) interaction.InputEnabled = true;
    }

    void RestartBuild()
    {
        _celebrated = false;
        interaction?.ClearCables();
        Manager?.RestartBuild();
        CloseModal();
    }
}
