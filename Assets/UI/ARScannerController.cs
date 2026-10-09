using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using BuildAR.AR;
using BuildAR.AR.Detection;
using BuildAR.Assembly;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.UI;

/// <summary>
/// AR Scanner: listens to a ComponentDetectorBase, confirms a detection over several frames
/// (Searching → Recognising → Recognised), draws the glowing box, and shows the info card and optional port labels.
/// It can also scan a photo from the gallery instead of the camera: the whole photo is shown and read first, then
/// the view zooms in on the part it found.
/// Attach next to a UIDocument (ARScanner.uxml) in the AR scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ARScannerController : MonoBehaviour, AssemblyInteraction.IUiHitTest
{
    enum State { Searching, Recognising, Recognised }

    /// <summary>Why a photo scan ended without a part card (see ShowPhotoProblem).</summary>
    enum PhotoProblem { NotAPart, PartNotInApp, CantOpen }

    [Tooltip("First available detector is used (put the ML detector before image tracking and the simulator).")]
    [SerializeField] private ComponentDetectorBase[] detectors;
    [Tooltip("Spawns the recognised part's 3D model in the room.")]
    [SerializeField] private ScannedModelPresenter modelPresenter;
    [Tooltip("Demo mode: invent detections when nothing can really recognise a part, for showing the flow without " +
             "a camera. Off by default — a part should only be recognized when the camera actually sees it.")]
    [SerializeField] private bool demoWhenUnavailable = false;
    [SerializeField, Range(0f, 1f)] private float minConfidence = 0.55f;
    [SerializeField] private int framesToConfirm = 3;
    [Tooltip("How many recent looks the confirmations are counted out of. Confirming 3 of the last 5 rides out a " +
             "single odd frame without letting one lucky guess through.")]
    [SerializeField] private int voteWindow = 5;
    [Tooltip("Smallest share of the screen a box may cover. Stops something noticed across the room being read as " +
             "the part in your hand.")]
    [SerializeField, Range(0f, 0.5f)] private float minBoxArea = 0.03f;
    [SerializeField] private float lostAfterSeconds = 1.5f;
    [SerializeField] private float sweepSeconds = 2.2f;

    [Header("Photos")]
    [Tooltip("A part covering less of a photo than this gets a second, closer look. Read whole, a big photo " +
             "reaches the model at 640 pixels across, so a small part in it is only a few dozen pixels; cropped " +
             "out, it gets the model's full attention.")]
    [SerializeField, Range(0f, 1f)] private float closeLookArea = 0.2f;

    static readonly Rect WholePhoto = new Rect(0f, 0f, 1f, 1f);

    UIDocument _doc;
    VisualElement _root, _scanFrame, _scanLine, _detectBox, _labelsLayer, _infoSheet, _pickerSheet, _noPartSheet, _tipsSheet;
    VisualElement _scrim;          // behind an open menu (tips, photos); a tap on it closes the menu
    VisualElement _menuReturn;     // the card an open menu covered, shown again when the menu closes
    float _menuTouchedAt;          // last time the open menu was opened or touched
    const float MenuIdleSeconds = 30f;
    VisualElement _topBar, _photoView, _photoImage;
    Label _statusLabel, _detectLabel;
    ComponentDetectorBase _detector;

    State _state;
    ComponentDefinitionSO _candidate, _recognised;
    readonly List<ComponentDefinitionSO> _votes = new List<ComponentDefinitionSO>();   // recent looks, oldest first
    float _lastSeen;
    Rect _targetBox, _box;
    bool _boxValid, _labelsOn;
    float _sweep;
    bool _demoMode, _noDetector, _arUnsupported, _cameraDenied;
    Texture2D _livePhoto;   // the still being scanned instead of the camera, if any
    bool _ownsPhoto;        // loaded from a file (destroy it when done), not a sample inside the app
    int _photoScan;         // counts photos, so the answer to a look at an earlier one is ignored
    Rect _pictureBox;       // the part found in the photo, 0..1 of the photo with the origin top-left
    bool _boxInPicture;     // the detection box is _pictureBox, and moves with the photo as it zooms
    Rect _photoFocus = WholePhoto;   // the region of the photo framed on screen
    bool _photoZoomed, _photoPlaced;
    Rect _photoShown;       // where the whole photo is drawn right now, in photo-view pixels
    bool _waitingForGallery;   // the phone's photo picker is open, or its answer hasn't arrived yet
    bool _choosingKind;        // the card is asking which kind of part this is (DDR4 or DDR5, say)
    readonly List<IVisualElementScheduledItem> _ticks = new List<IVisualElementScheduledItem>();

    /// <summary>What a look at a photo found: the part, and where (0..1 of the photo).</summary>
    struct PhotoHit { public ComponentDefinitionSO def; public float confidence; public Rect box; }

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        _root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(_root);

        _scanFrame = _root.Q("scan-frame");
        _scanLine = _root.Q("scan-line");
        _detectBox = _root.Q("detect-box");
        _labelsLayer = _root.Q("ar-labels");
        _infoSheet = _root.Q("info-sheet");
        _pickerSheet = _root.Q("picker-sheet");
        _noPartSheet = _root.Q("nopart-sheet");
        _tipsSheet = _root.Q("tips-sheet");
        _statusLabel = _root.Q<Label>("status-label");
        _detectLabel = _root.Q<Label>("detect-label");
        _topBar = _root.Q(className: "ar-topbar");
        _photoView = _root.Q("photo-view");
        _photoImage = _root.Q("photo-image");
        if (_photoImage != null)   // the element is sized to the photo, so stretching it is exact
            _photoImage.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));

        UIUtil.OnClick(_root, "btn-back", () => GameManager.Instance?.ShowScreen(AppScreen.Home));
        UIUtil.OnClick(_root, "btn-photo", () => { if (PickerShown) CloseMenus(); else OpenPhotos(); });
        UIUtil.OnClick(_root, "btn-photo-zoom", () => ZoomPhoto(!_photoZoomed));
        UIUtil.OnClick(_root, "btn-close-picker", () => CloseMenus());
        UIUtil.OnClick(_root, "btn-close-sheet", CloseCard);
        UIUtil.OnClick(_root, "btn-scan-again", CloseCard);
        UIUtil.OnClick(_root, "btn-close-nopart", ShowLiveCamera);
        UIUtil.OnClick(_root, "btn-nopart-camera", ShowLiveCamera);
        UIUtil.OnClick(_root, "btn-nopart-retry", TryAnotherPhoto);
        UIUtil.OnClick(_root, "btn-tips", () => { if (TipsShown) HideTips(); else ShowTips(); });
        UIUtil.OnClick(_root, "btn-close-tips", HideTips);
        UIUtil.OnClick(_root, "btn-tips-start", HideTips);
        UIUtil.OnClick(_root, "btn-tips-hide", ToggleTipsHidden);
        UIUtil.OnClick(_root, "btn-view-3d", () => GameManager.Instance?.OpenComponentDetail(_recognised, "parts"));
        UIUtil.OnClick(_root, "btn-learn-more", () => GameManager.Instance?.OpenComponentDetail(_recognised, "overview"));
        UIUtil.OnClick(_root, "btn-labels", ToggleLabels);
        UIUtil.OnClick(_root, "btn-place-model", () => { modelPresenter?.Reposition(); RefreshModelRow(); });
        UIUtil.OnClick(_root, "btn-hide-model", ToggleModel);

        // Menus close when the learner taps outside them, and on their own after a while untouched.
        _scrim = _root.Q("sheet-scrim");
        _scrim?.RegisterCallback<PointerDownEvent>(_ => CloseMenus());
        foreach (var menu in new[] { _tipsSheet, _pickerSheet })
            menu?.RegisterCallback<PointerDownEvent>(_ => _menuTouchedAt = Time.realtimeSinceStartup, TrickleDown.TrickleDown);

        RequestCamera();
        if (modelPresenter == null) modelPresenter = EnsurePresenter();
        if (modelPresenter != null)
        {
            modelPresenter.UiHitTest = this;
            modelPresenter.Changed += RefreshModelRow;   // the photo takes a frame, so the note updates late
        }

        _ticks.Add(_root.schedule.Execute(Animate).Every(16));
        _ticks.Add(_root.schedule.Execute(() =>
        {
            _root.Query(className: "marker").ForEach(m => m.ToggleInClassList("pulse"));
            if (_state == State.Searching) _scanFrame.ToggleInClassList("breathe");
            CheckArSupport();
            if (MenuOpen && Time.realtimeSinceStartup - _menuTouchedAt > MenuIdleSeconds) CloseMenus();
        }).Every(700));

        SelectDetector();
        ResetToSearching();

        // "Before you scan": a smudged lens or a dim room is the commonest reason a scan finds nothing, and the
        // learner can fix both in seconds. Shown once the scanner has appeared, until they turn it off.
        _tipsSheet?.AddToClassList("sheet--hidden");
        if (ProgressManager.Instance == null || !ProgressManager.Instance.Data.scanTipsHidden)
            _ticks.Add(_root.schedule.Execute(ShowTips).StartingIn(400));
    }

    void OnDisable()
    {
        _waitingForGallery = false;
        foreach (var t in _ticks) t.Pause();
        _ticks.Clear();
        ReleasePhoto();
        if (_detector != null) _detector.DetectionsUpdated -= OnDetections;
        if (modelPresenter != null) modelPresenter.Changed -= RefreshModelRow;
        modelPresenter?.Hide();
    }

    void SelectDetector()
    {
        var list = detectors ?? new ComponentDetectorBase[0];
        _detector = null;

        foreach (var d in list)
        {
            if (d == null) continue;
            // The simulator makes detections up, so it only ever runs when demo mode is asked for.
            if (d is SimulatedComponentDetector) continue;
            if (_detector == null && d.IsAvailable) _detector = d;
            else if (!d.IsAvailable) Debug.Log($"BuildAR: {d.GetType().Name} unavailable — {d.UnavailableReason}");
        }

        if (_detector == null && demoWhenUnavailable)
            foreach (var d in list)
                if (d is SimulatedComponentDetector) { _detector = d; break; }

        foreach (var d in list) if (d != null) d.enabled = d == _detector;

        _demoMode = _detector is SimulatedComponentDetector;
        _noDetector = _detector == null;
        if (_detector != null) _detector.DetectionsUpdated += OnDetections;
        else Debug.LogWarning("BuildAR: no component detector available, so the scanner can't recognise parts.");
    }

    /// <summary>
    /// Builds the model presenter (and the plane / raycast managers it needs) when the scene predates it, so the
    /// scanner works without regenerating BuildAR_ARScanner. Rebuilding the scene wires it properly instead.
    /// </summary>
    ScannedModelPresenter EnsurePresenter()
    {
        var existing = FindAnyObjectByType<ScannedModelPresenter>();
        if (existing != null) return existing;

        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null) return null;

        var raycasts = origin.GetComponent<ARRaycastManager>();
        if (raycasts == null) raycasts = origin.gameObject.AddComponent<ARRaycastManager>();
        var planes = origin.GetComponent<ARPlaneManager>();
        if (planes == null) planes = origin.gameObject.AddComponent<ARPlaneManager>();

        var presenter = new GameObject("ScannedModel").AddComponent<ScannedModelPresenter>();
        presenter.Configure(raycasts, planes, origin.Camera);
        Debug.Log("BuildAR: created the AR model presenter at runtime. Run BuildAR > Setup > 4. Build Scenes to wire it into the scene.");
        return presenter;
    }

    /// <summary>
    /// Watches the AR session so a black screen explains itself. The two things that stop the camera feed are a
    /// device without ARCore and a denied camera permission; both now say so instead of showing nothing.
    /// </summary>
    void CheckArSupport()
    {
        bool unsupported = ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.NeedsInstall;
        bool denied = !CameraAllowed();
        if (unsupported == _arUnsupported && denied == _cameraDenied) return;

        _arUnsupported = unsupported;
        _cameraDenied = denied;
        if (_state != State.Recognised) SetState(_state);
    }

    /// <summary>Asks for the camera once. ARCore can't start without it, and a refusal leaves a black screen.</summary>
    void RequestCamera()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
#endif
    }

    static bool CameraAllowed()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
#else
        return true;
#endif
    }

    // ------------------------------------------------------------------ detection state machine

    void OnDetections(IReadOnlyList<Detection> detections)
    {
        var db = ComponentDatabase.Instance;
        if (db == null || _livePhoto != null) return;   // a photo is up: the camera isn't what's being scanned
        // The learner is reading the tips or choosing a photo, not aiming: don't open a card underneath.
        if (MenuOpen && _state != State.Recognised) return;

        Detection best = default;
        ComponentDefinitionSO bestDef = null;
        foreach (var d in detections.OrderByDescending(x => x.confidence))
        {
            if (d.confidence < minConfidence) continue;
            if (d.hasBox && !WellFramed(d.screenRect)) continue;
            var def = db.GetByMlLabel(d.label);
            if (def == null) continue;
            // While a card is open, keep tracking the same part. Compared by kind of part: the model reports every
            // RAM stick as the same component, whichever kind the learner then said it was.
            if (_state == State.Recognised && def.category != _recognised.category) continue;
            best = d; bestDef = def;
            break;
        }

        Vote(bestDef);

        if (bestDef == null)
        {
            if (Time.time - _lastSeen > lostAfterSeconds)
            {
                _boxValid = false;
                if (_state == State.Recognising) SetState(State.Searching);
            }
            return;
        }

        _lastSeen = Time.time;
        _targetBox = best.hasBox ? best.screenRect : ScanFrameRect();
        if (!_boxValid) _box = _targetBox;
        _boxValid = true;
        _detectLabel.text = $"{TagName(bestDef)} · {Mathf.RoundToInt(best.confidence * 100)}%";

        if (_state == State.Recognised) return;

        _candidate = bestDef;
        if (_state == State.Searching) SetState(State.Recognising);
        if (Agreement(bestDef) >= framesToConfirm) Recognise(bestDef);
    }

    /// <summary>
    /// Keeps the last few looks. Judging on a run of identical frames means one bad frame in the middle throws the
    /// count away and one good frame after two bad ones can still win; counting agreement across a window asks the
    /// model to be right most of the time, which is the thing that actually matters.
    /// </summary>
    void Vote(ComponentDefinitionSO def)
    {
        _votes.Add(def);
        int window = Mathf.Max(framesToConfirm, voteWindow);
        if (_votes.Count > window) _votes.RemoveRange(0, _votes.Count - window);
    }

    int Agreement(ComponentDefinitionSO def) => _votes.Count(v => v == def);

    /// <summary>
    /// True when a box is big enough and near enough the middle to be the part the learner is holding up. Small
    /// boxes off at the edge are things in the background, and acting on them is what makes a scan look random.
    /// </summary>
    bool WellFramed(Rect box)
    {
        if (box.width * box.height < minBoxArea) return false;
        var f = ScanFrameRect();
        // A generous margin around the frame: the guide is a hint, not a rule, and parts often stick out of it.
        var allowed = new Rect(f.x - f.width * 0.35f, f.y - f.height * 0.6f, f.width * 1.7f, f.height * 2.2f);
        return allowed.Contains(box.center);
    }

    void SetState(State s)
    {
        _state = s;
        bool searching = s == State.Searching, photo = _livePhoto != null;
        // The frame is a guide for aiming the camera; a photo is already framed.
        _scanFrame.EnableInClassList("hidden", s == State.Recognised || photo);
        _detectBox.EnableInClassList("recognizing", s == State.Recognising);
        // The problem card says it all; the status pill above it would only repeat it.
        UIUtil.SetVisible(_root.Q("status"), s != State.Recognised && !ProblemShown);
        UIUtil.SetVisible(_root.Q("status-spinner"), s == State.Recognising);
        UIUtil.SetVisible(_root.Q("status-icon"), searching);

        _statusLabel.text =
            s == State.Recognising ? (photo ? "Reading the picture…" : "Recognizing component…")
            : photo ? "Nothing recognized in that picture"
            : _cameraDenied ? "Camera permission is off"
            : _arUnsupported ? "AR isn't available on this device"
            : _noDetector ? "Recognition isn't set up yet"
            : "Looking for a component…";

        var hint = _root.Q<Label>("status-hint");
        hint.text =
            photo ? "Try a clearer photo of a single part."
            : _cameraDenied ? "Allow the camera in Android Settings ▸ Apps ▸ PCBuildAR ▸ Permissions, then come back."
            : _arUnsupported ? "You can still scan a photo with the picture button."
            : _noDetector ? "You can still explore every part in Learn."
            : "Fit the part inside the frame, in good light";
        UIUtil.SetVisible(hint, searching);

        UIUtil.SetVisible(_root.Q("scan-mode"), searching && _demoMode && !photo);
        if (_demoMode) _root.Q<Label>("scan-mode-label").text = "Demo mode · simulated scan";
    }

    /// <summary>
    /// Opens the card for a part. When the app has several kinds of that part and the scanner can't tell them apart
    /// (the model knows "a RAM stick", not DDR4 from DDR5), the card first asks which kind it is, rather than
    /// claiming one; the rest of the card, the 3D model and the scan record wait for the answer.
    /// </summary>
    void Recognise(ComponentDefinitionSO def, bool askKind = true, bool sound = true)
    {
        var kinds = askKind ? Kinds(def) : null;
        _choosingKind = kinds != null;
        _recognised = def;
        SetState(State.Recognised);

        _root.Q<LineIcon>("sheet-icon").icon = def.category.IconName();
        _root.Q<Label>("sheet-title").text = _choosingKind ? KindTitle(def.category) : def.displayName;
        _root.Q<Label>("sheet-desc").text = _choosingKind ? KindQuestion(def.category) : def.shortDescription;
        var chips = _root.Q("sheet-chips");
        chips.Clear();
        if (!_choosingKind) foreach (var c in def.specChips) chips.Add(UIUtil.Text(c, "chip"));

        UIUtil.SetVisible(_root.Q("kind-choice"), _choosingKind);
        UIUtil.SetVisible(_root.Q("sheet-actions"), !_choosingKind);
        if (_choosingKind) BuildKindButtons(kinds);

        _labelsOn = false;
        RefreshLabelsButton();
        UIUtil.SetVisible(_root.Q("btn-labels"), !_choosingKind && def.arLabels.Count > 0);

        CloseMenus(restore: false);
        HideProblem();
        _infoSheet.RemoveFromClassList("sheet--hidden");
        if (sound) AudioManager.Instance?.PlayCorrect();
        if (_choosingKind)
        {
            modelPresenter?.Hide();
            RefreshModelRow();
            return;
        }
        ProgressManager.Instance?.RecordScan(def.id);
        // A photo covers the camera view, so a model placed in the room would sit hidden behind it.
        if (_livePhoto == null) ShowModel(def);
        else RefreshModelRow();
    }

    // ------------------------------------------------------------------ parts that come in several kinds

    /// <summary>
    /// Every component of the same kind of part, when the app has more than one (RAM: DDR3, DDR4 and DDR5; storage: M.2
    /// and SATA). The model can't tell those apart, so they are asked about; null when there's nothing to ask.
    /// </summary>
    static List<ComponentDefinitionSO> Kinds(ComponentDefinitionSO def)
    {
        var db = ComponentDatabase.Instance;
        if (db == null || def == null) return null;
        var kinds = db.GetByCategory(def.category).OrderBy(k => k.displayName).ToList();
        return kinds.Count > 1 ? kinds : null;
    }

    void BuildKindButtons(List<ComponentDefinitionSO> kinds)
    {
        var row = _root.Q("kind-row");
        row.Clear();
        for (int i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            var button = UIUtil.MakeButton("", () => ChooseKind(kind), "btn", "btn-secondary", "grow", "kind-btn");
            if (i < kinds.Count - 1) button.AddToClassList("gap-right");
            button.Add(new Label(ShortName(kind)));
            row.Add(button);
        }
        _root.Q<Label>("kind-hint").text = KindHint(kinds[0].category);
    }

    /// <summary>The learner said which kind it is: now the card, the 3D model and the scan record are about that one.</summary>
    void ChooseKind(ComponentDefinitionSO kind)
    {
        _detectLabel.text = kind.displayName;
        Recognise(kind, askKind: false, sound: false);
    }

    /// <summary>What the box's tag calls a part: the general name while its kind is unknown, the chosen one after.</summary>
    string TagName(ComponentDefinitionSO def)
    {
        if (!_choosingKind && _state == State.Recognised && _recognised != null && _recognised.category == def.category)
            return _recognised.displayName;
        return Kinds(def) != null ? KindTitle(def.category) : def.displayName;
    }

    /// <summary>"Memory (DDR4 RAM)" → "DDR4 RAM": the part of the name that tells the kinds apart.</summary>
    static string ShortName(ComponentDefinitionSO def)
    {
        string n = def.displayName;
        int open = n.IndexOf('('), close = n.LastIndexOf(')');
        return open >= 0 && close > open ? n.Substring(open + 1, close - open - 1) : n;
    }

    static string KindTitle(ComponentCategory category) =>
        category == ComponentCategory.RAM ? "Memory (RAM)"
        : category == ComponentCategory.Storage ? "Storage Drive"
        : category.LongName();

    static string KindQuestion(ComponentCategory category) =>
        category == ComponentCategory.RAM
            ? "This is a RAM stick. DDR3, DDR4 and DDR5 look almost the same in a photo, so the scanner can't tell which one you have."
        : category == ComponentCategory.Storage
            ? "This is a storage drive. Different kinds look alike to the scanner, so it can't tell which one you have."
        : "This part comes in more than one kind, and they look alike to the scanner.";

    static string KindHint(ComponentCategory category) =>
        category == ComponentCategory.RAM
            ? "Tip: check the sticker on the stick. It says DDR3 or PC3, DDR4 or PC4, or DDR5 or PC5."
        : category == ComponentCategory.Storage
            ? "Tip: an M.2 SSD is a thin, stick-shaped board that screws onto the motherboard. A SATA SSD is a flat 2.5-inch box with two sockets at one end."
        : "";

    /// <summary>
    /// Closing the card. In demo mode the simulator moves on, so the next scan shows a different part. A scanned
    /// photo is done with once its card is closed, so the camera comes back.
    /// </summary>
    void CloseCard()
    {
        if (_livePhoto != null) { ShowLiveCamera(); return; }
        if (_demoMode && _detector is SimulatedComponentDetector sim) sim.NextLabel();
        ResetToSearching();
    }

    void ResetToSearching()
    {
        _recognised = null;
        _choosingKind = false;
        _candidate = null;
        _votes.Clear();
        _boxValid = false;
        _boxInPicture = false;
        RefreshZoomButton();
        _labelsOn = false;
        _labelsLayer.Clear();
        _infoSheet.AddToClassList("sheet--hidden");
        HideProblem();
        _menuReturn = null;   // the card a menu covered is gone, so closing the menu shouldn't bring it back
        modelPresenter?.Hide();
        SetState(State.Searching);
    }

    // ------------------------------------------------------------------ 3D model in the room

    /// <summary>Spawns the recognised part's model, on the real part when the detector knows where it is.</summary>
    void ShowModel(ComponentDefinitionSO def)
    {
        if (modelPresenter == null) { RefreshModelRow(); return; }

        Pose? anchor = null;
        if (_detector is TrackedImageComponentDetector tracked && tracked.TryGetPose(def.mlLabel, out var pose))
            anchor = pose;

        modelPresenter.Show(def, _boxValid ? _box : ScanFrameRect(), anchor);
        RefreshModelRow();
    }

    void ToggleModel()
    {
        if (modelPresenter == null || _recognised == null) return;
        if (modelPresenter.HasModel) modelPresenter.Hide();
        else ShowModel(_recognised);
        RefreshModelRow();
    }

    void RefreshModelRow()
    {
        var note = _root.Q<Label>("model-note");
        var actions = _root.Q("model-actions");
        if (note == null || actions == null) return;

        bool has = modelPresenter != null && modelPresenter.HasModel, photo = _livePhoto != null;
        // When there's no model the note says why: either the part has no prefab, or the learner hid it.
        note.text = modelPresenter == null || photo || _choosingKind ? ""
            : has || modelPresenter.Current != null ? modelPresenter.PlacementNote
            : "Model hidden — tap Show model.";
        UIUtil.SetVisible(note, !string.IsNullOrEmpty(note.text));
        UIUtil.SetVisible(actions, !photo && !_choosingKind && modelPresenter != null && _recognised != null && _recognised.model3DPrefab != null);
        _root.Q("btn-place-model").SetEnabled(has);
        _root.Q<Label>("btn-hide-model-label").text = has ? "Hide model" : "Show model";
    }

    /// <summary>Keeps drags on the card from turning the model (IUiHitTest for ScannedModelPresenter).</summary>
    public bool IsOverUI(Vector2 screenPosition)
    {
        var panel = _root?.panel;
        if (panel == null) return false;
        var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        var picked = panel.Pick(panelPos);
        return picked != null && picked != _root && picked != panel.visualTree
               && !picked.ClassListContains("screen") && !picked.ClassListContains("ar-dim");
    }

    // ------------------------------------------------------------------ model tuning (development builds)

    /// <summary>
    /// Lets the frame rotation be changed on the device. A wrong rotation feeds the model a sideways picture, so
    /// it answers confidently with the wrong part — which looks like the scanner picking components at random.
    /// </summary>
    void AddRotationTool(VisualElement list)
    {
        if (!(_detector is InferenceComponentDetector inference)) return;

        var row = UIUtil.MakeButton("", null, "picker-item");
        row.Add(new LineIcon("rotate"));
        var label = new Label($"Camera rotation: {inference.RotationName}");
        row.Add(label);
        row.clicked += () =>
        {
            inference.CycleRotation();
            label.text = $"Camera rotation: {inference.RotationName}";
        };
        list.Add(row);

        // The single most useful thing when results are poor: look at the picture the model was actually given.
        var peek = UIUtil.MakeButton("", null, "picker-item");
        peek.Add(new LineIcon("image"));
        var peekLabel = new Label($"See what the model sees · sharpness {inference.LastSharpness:0}");
        peek.Add(peekLabel);
        peek.clicked += () =>
        {
            string path = inference.SaveDebugFrame();
            peekLabel.text = path == null
                ? "Nothing captured yet — point the camera first"
                : $"Saved as 'model-sees' · sharpness {inference.LastSharpness:0}";
            if (path != null) UIUtil.Toast(_root, "Saved the model's view", "Open it from Scan a picture ▸ model-sees", "image");
        };
        list.Add(peek);
    }

    // ------------------------------------------------------------------ scanning a picture

    /// <summary>
    /// Lists the pictures the model can be run on: samples inside the app and photos copied onto the phone.
    /// Useful when the part isn't to hand, and for showing the scanner working on a known-good image.
    /// </summary>
    void OpenPhotos()
    {
        var list = _root.Q<ScrollView>("picker-list");
        list.Clear();
        _root.Q<Label>("picker-kicker").text = "SCAN A PICTURE";
        _root.Q<Label>("picker-title").text = "Choose a photo";

        if (NativeGallery.IsSupported)
        {
            var browse = UIUtil.MakeButton("", PickFromGallery, "picker-item");
            browse.Add(new LineIcon("image"));
            browse.Add(new Label("Choose from my gallery"));
            list.Add(browse);
        }

        var entries = ScanPhotoLibrary.All();
        if (entries.Count == 0 && !NativeGallery.IsSupported)
        {
            string folder = ScanPhotoLibrary.EnsurePhotoFolder();
            var empty = UIUtil.Box("picker-item");
            empty.Add(new LineIcon("info"));
            empty.Add(UIUtil.Text("No pictures yet. Copy .jpg files to\n" + folder, "small"));
            list.Add(empty);
        }
        else
        {
            foreach (var entry in entries)
            {
                var captured = entry;
                var item = UIUtil.MakeButton("", () => ScanPicture(captured), "picker-item");
                item.Add(new LineIcon("image"));
                item.Add(new Label(entry.name));
                list.Add(item);
            }
        }

        if (_livePhoto != null)
        {
            var back = UIUtil.MakeButton("", ShowLiveCamera, "picker-item");
            back.Add(new LineIcon("camera"));
            back.Add(new Label("Back to the live camera"));
            list.Add(back);
        }

        // The rotation and "what the model sees" tools are for tuning the model, not for learners.
        if (Debug.isDebugBuild) AddRotationTool(list);

        OpenMenu(_pickerSheet);
    }

    /// <summary>Opens the phone's gallery (a file dialog in the Editor), then scans whatever comes back.</summary>
    void PickFromGallery()
    {
        _statusLabel.text = "Opening your gallery…";
        _waitingForGallery = true;
        NativeGallery.PickImage(path =>
        {
            // The answer can come late, after the learner has left the scanner: there's nothing to show it on.
            if (this == null || !isActiveAndEnabled) return;
            _waitingForGallery = false;
            if (string.IsNullOrEmpty(path)) { SetState(_state); return; }
            ScanPicture(new ScanPhotoLibrary.Entry { name = "Chosen photo", path = path });
        });
    }

    /// <summary>
    /// The picker's answer normally arrives a frame or two after the app comes back to the front. If Android threw
    /// the hidden picker away while the gallery was open (it can, when the phone is short of memory), the answer
    /// never comes and the status would sit on "Opening your gallery…" for good. So once the app has been back for
    /// a moment with no answer, the status goes back to what the scanner is doing. A photo that does turn up later
    /// is still scanned.
    /// </summary>
    void OnApplicationFocus(bool focused)
    {
        if (!focused || !_waitingForGallery || _root == null) return;
        _ticks.Add(_root.schedule.Execute(() =>
        {
            if (!_waitingForGallery) return;
            _waitingForGallery = false;
            SetState(_state);
        }).StartingIn(1500));
    }

    /// <summary>
    /// Scans a photo. It is shown whole (zoomed out) and the model reads the whole of it; a small part, or none,
    /// earns a closer look at a crop. Then the view zooms in on the part, and the zoom button swaps back.
    /// </summary>
    void ScanPicture(ScanPhotoLibrary.Entry entry)
    {
        var texture = ScanPhotoLibrary.Load(entry);
        if (texture == null)
        {
            if (_livePhoto != null) ShowLiveCamera();   // the card explains over the camera, not an older photo
            CloseMenus(restore: false);
            ShowPhotoProblem(PhotoProblem.CantOpen);
            return;
        }

        ResetToSearching();
        CloseMenus(restore: false);
        ShowPhoto(texture, owned: entry.bundled == null);

        var still = StillDetector();
        if (still == null)
        {
            SetState(State.Searching);
            UIUtil.Toast(_root, "No model loaded", "The scanner's recognition model isn't set up yet.", "info");
            return;
        }

        SetState(State.Recognising);
        int scan = ++_photoScan;
        still.DetectStill(texture, WholePhoto, found =>
        {
            if (scan != _photoScan) return;
            var first = BestIn(found, WholePhoto);

            // Found big enough to trust: done. Found small: the whole-photo view gave it few pixels, so look again
            // at a crop around it. Found nothing: the part may just be small, so try the middle, where people
            // frame things.
            var closer = new Queue<Rect>();
            if (first.def == null) foreach (var r in MiddleRegions()) closer.Enqueue(r);
            else if (first.box.width * first.box.height < closeLookArea) closer.Enqueue(RegionAround(first.box, 1.6f, 0.35f));
            LookCloser(still, scan, first, closer, PartNotInApp(found));
        });
    }

    /// <summary>
    /// Runs the model on each region in turn until one finds a part, then shows the answer. <paramref name="otherPart"/>:
    /// a part the whole photo showed that the app has no lesson for, explained if no look finds a known one.
    /// </summary>
    void LookCloser(IStillDetector still, int scan, PhotoHit first, Queue<Rect> regions, string otherPart)
    {
        if (regions.Count == 0) { ShowPhotoResult(first, otherPart); return; }

        var region = regions.Dequeue();
        _statusLabel.text = "Taking a closer look…";
        still.DetectStill(_livePhoto, region, found =>
        {
            if (scan != _photoScan) return;
            var hit = BestIn(found, region);
            // The closer look sees more detail, so it wins unless it names a different part less surely.
            if (hit.def != null && (first.def == null || hit.def == first.def || hit.confidence >= first.confidence))
                ShowPhotoResult(hit);
            else if (first.def != null) ShowPhotoResult(first);
            else LookCloser(still, scan, first, regions, otherPart);
        });
    }

    /// <summary>The most confident detection that names a known part. A box-less classifier answer covers the region.</summary>
    PhotoHit BestIn(IReadOnlyList<Detection> found, Rect region)
    {
        var db = ComponentDatabase.Instance;
        if (db == null || found == null) return default;
        foreach (var d in found.OrderByDescending(x => x.confidence))
        {
            if (d.confidence < minConfidence) continue;
            var def = db.GetByMlLabel(d.label);
            if (def == null) continue;
            return new PhotoHit { def = def, confidence = d.confidence, box = d.hasBox ? d.screenRect : region };
        }
        return default;
    }

    /// <summary>
    /// A still gets one look (or a few), so there's no waiting for the same part over several frames. No part:
    /// the photo-problem card says why.
    /// </summary>
    void ShowPhotoResult(PhotoHit hit, string otherPart = null)
    {
        if (_livePhoto == null) return;
        if (hit.def == null)
        {
            ShowPhotoProblem(otherPart != null ? PhotoProblem.PartNotInApp : PhotoProblem.NotAPart, otherPart);
            return;
        }

        _pictureBox = hit.box;
        _boxInPicture = _boxValid = true;
        _lastSeen = Time.time + 3600f;
        _detectLabel.text = $"{TagName(hit.def)} · {Mathf.RoundToInt(hit.confidence * 100)}%";
        ZoomPhoto(true);
        Recognise(hit.def);
    }

    // ------------------------------------------------------------------ menus: tips and photos

    // The scanner's bottom sheets are of two kinds. Cards (the part card, the photo-problem card) are results.
    // Menus (the "Before you scan" tips, "Choose a photo") are things the learner opened. Only one sheet shows at a
    // time: opening a menu tucks away whatever card was up and closes the other menu, and closing the menu brings
    // that card back. A menu closes when the learner taps outside it, and on its own after a while untouched.

    static bool IsShown(VisualElement sheet) => sheet != null && !sheet.ClassListContains("sheet--hidden");

    bool TipsShown => IsShown(_tipsSheet);
    bool PickerShown => IsShown(_pickerSheet);
    bool MenuOpen => TipsShown || PickerShown;

    void OpenMenu(VisualElement menu)
    {
        foreach (var card in new[] { _infoSheet, _noPartSheet })
        {
            if (!IsShown(card)) continue;
            _menuReturn = card;
            card.AddToClassList("sheet--hidden");
        }
        foreach (var other in new[] { _tipsSheet, _pickerSheet })
            if (other != menu) other?.AddToClassList("sheet--hidden");
        menu.RemoveFromClassList("sheet--hidden");
        UIUtil.SetVisible(_scrim, true);
        _menuTouchedAt = Time.realtimeSinceStartup;
    }

    /// <summary>Closes whichever menu is open. <paramref name="restore"/>: show the card it covered again.</summary>
    void CloseMenus(bool restore = true)
    {
        if (TipsShown) _votes.Clear();   // whatever the camera saw while the tips were up wasn't aimed at anything
        _tipsSheet?.AddToClassList("sheet--hidden");
        _pickerSheet?.AddToClassList("sheet--hidden");
        UIUtil.SetVisible(_scrim, false);
        var card = _menuReturn;
        _menuReturn = null;
        if (restore && card != null) card.RemoveFromClassList("sheet--hidden");
    }

    void ShowTips()
    {
        if (_tipsSheet == null) return;
        bool hidden = ProgressManager.Instance != null && ProgressManager.Instance.Data.scanTipsHidden;
        _root.Q("tips-hide-switch")?.EnableInClassList("on", hidden);
        OpenMenu(_tipsSheet);
    }

    void HideTips() => CloseMenus();

    /// <summary>"Don't show this again": saved straight away, and the (i) button still opens the tips.</summary>
    void ToggleTipsHidden()
    {
        var progress = ProgressManager.Instance;
        if (progress == null) return;
        progress.SetScanTipsHidden(!progress.Data.scanTipsHidden);
        _root.Q("tips-hide-switch")?.EnableInClassList("on", progress.Data.scanTipsHidden);
    }

    // ------------------------------------------------------------------ photo problems

    bool ProblemShown => _noPartSheet != null && !_noPartSheet.ClassListContains("sheet--hidden");

    void HideProblem() => _noPartSheet?.AddToClassList("sheet--hidden");

    /// <summary>
    /// Explains why a photo gave no part card, and offers the two ways on: another photo, or the camera. A learner
    /// who photographs their homework, a cat or a blurry desk should be told plainly that the scanner found no
    /// PC part, not be left reading a status line, and certainly not be shown a part that isn't there.
    /// </summary>
    void ShowPhotoProblem(PhotoProblem problem, string part = null)
    {
        if (_noPartSheet == null) { SetState(State.Searching); return; }

        string icon, kicker, title, desc;
        switch (problem)
        {
            case PhotoProblem.PartNotInApp:
                icon = "info";
                kicker = "NOT IN THE APP YET";
                title = "That part isn't covered here";
                desc = $"It looks like {Article(part)} {part}, which this app doesn't have a lesson for yet. " +
                       "Try a photo of a CPU, RAM stick, graphics card, motherboard, SSD, power supply or CPU cooler.";
                break;
            case PhotoProblem.CantOpen:
                icon = "image";
                kicker = "PHOTO PROBLEM";
                title = "We couldn't open that photo";
                desc = "It may be damaged, or saved in a format the app can't read. Try another photo, or take a " +
                       "new one with your camera app.";
                break;
            default:
                icon = "warning";
                kicker = "NOT RECOGNIZED";
                title = "This doesn't look like a PC part";
                desc = "We couldn't find a computer component in this photo. The scanner knows parts like CPUs, RAM " +
                       "sticks, graphics cards, motherboards, SSDs and power supplies.";
                break;
        }

        _root.Q<LineIcon>("nopart-icon").icon = icon;
        _root.Q<Label>("nopart-kicker").text = kicker;
        _root.Q<Label>("nopart-title").text = title;
        _root.Q<Label>("nopart-desc").text = desc;
        // The photo tips only help when the photo itself was the trouble.
        UIUtil.SetVisible(_root.Q("nopart-tips"), problem == PhotoProblem.NotAPart);

        _infoSheet.AddToClassList("sheet--hidden");
        CloseMenus(restore: false);
        _noPartSheet.RemoveFromClassList("sheet--hidden");
        SetState(State.Searching);   // after the sheet is up, so the status pill stays hidden behind it
        AudioManager.Instance?.PlayWarning();
    }

    /// <summary>
    /// A confident detection of a part the model knows but the app has no component for (an optical drive, say),
    /// as words; null when there was none.
    /// </summary>
    string PartNotInApp(IReadOnlyList<Detection> found)
    {
        var db = ComponentDatabase.Instance;
        if (db == null || found == null) return null;
        foreach (var d in found.OrderByDescending(x => x.confidence))
            if (d.confidence >= minConfidence && !string.IsNullOrEmpty(d.label) && db.GetByMlLabel(d.label) == null)
                return d.label.Replace('_', ' ').Trim().ToLowerInvariant();
        return null;
    }

    static string Article(string word) => !string.IsNullOrEmpty(word) && "aeiou".IndexOf(word[0]) >= 0 ? "an" : "a";

    /// <summary>Straight to the phone's gallery when that's the only place photos come from, otherwise the photo list.</summary>
    void TryAnotherPhoto()
    {
        if (NativeGallery.IsSupported && ScanPhotoLibrary.All().Count == 0)
        {
            HideProblem();
            SetState(_state);   // bring the status pill back: it is about to say "Opening your gallery…"
            PickFromGallery();
        }
        else OpenPhotos();      // covers the problem card, which comes back if no photo is chosen
    }

    /// <summary>
    /// Crops tried when the whole photo showed nothing: the middle square, if the photo is far from square (the
    /// whole-photo look squeezed it), then a tighter square for a small part.
    /// </summary>
    IEnumerable<Rect> MiddleRegions()
    {
        float w = _livePhoto.width, h = _livePhoto.height, side = Mathf.Min(w, h);
        var middle = new Vector2(w / 2f, h / 2f);
        if (Mathf.Max(w, h) > side * 1.15f) yield return PixelRegion(middle, side, side);
        yield return PixelRegion(middle, side * 0.55f, side * 0.55f);
    }

    /// <summary>
    /// A square around a box (0..1 of the photo), <paramref name="grow"/> times its longer side, and no smaller
    /// than <paramref name="least"/> of the photo's shorter side.
    /// </summary>
    Rect RegionAround(Rect box, float grow, float least)
    {
        float w = _livePhoto.width, h = _livePhoto.height;
        float side = Mathf.Max(Mathf.Max(box.width * w, box.height * h) * grow, Mathf.Min(w, h) * least);
        return PixelRegion(new Vector2(box.center.x * w, box.center.y * h), side, side);
    }

    /// <summary>A region of the photo given in its pixels, kept inside it, as 0..1 of the photo.</summary>
    Rect PixelRegion(Vector2 centre, float width, float height)
    {
        float w = _livePhoto.width, h = _livePhoto.height;
        width = Mathf.Min(width, w);
        height = Mathf.Min(height, h);
        float x = Mathf.Clamp(centre.x - width / 2f, 0f, w - width), y = Mathf.Clamp(centre.y - height / 2f, 0f, h - height);
        return new Rect(x / w, y / h, width / w, height / h);
    }

    /// <summary>
    /// The detector that reads photos: the live one if it can, otherwise any that can without a camera, so a
    /// photo can be scanned in the Editor, where the model can't see the camera.
    /// </summary>
    IStillDetector StillDetector()
    {
        if (_detector is IStillDetector live && live.CanDetectStills) return live;
        if (detectors != null)
            foreach (var d in detectors)
                if (d is IStillDetector still && still.CanDetectStills) return still;
        return null;
    }

    /// <summary>Puts a photo up in place of the camera, zoomed out so all of it shows.</summary>
    void ShowPhoto(Texture2D texture, bool owned)
    {
        ReleasePhoto();
        _livePhoto = texture;
        _ownsPhoto = owned;
        _photoFocus = WholePhoto;
        _photoZoomed = _photoPlaced = false;
        if (_photoImage != null) _photoImage.style.backgroundImage = new StyleBackground(texture);
        if (_photoView != null) UIUtil.SetVisible(_photoView, true);
        // Nobody is looking at the camera while a photo is up; stop reading it, which also frees the model.
        if (_detector != null) _detector.enabled = false;
        RefreshZoomButton();
    }

    /// <summary>Takes the photo down (and frees it, if it was loaded from a file).</summary>
    void ReleasePhoto()
    {
        _photoScan++;   // a look still running was at this photo: ignore its answer
        if (_livePhoto != null && _ownsPhoto) Destroy(_livePhoto);
        _livePhoto = null;
        _ownsPhoto = false;
        _boxInPicture = false;
        if (_photoImage != null) _photoImage.style.backgroundImage = new StyleBackground(StyleKeyword.Null);
        if (_photoView != null) UIUtil.SetVisible(_photoView, false);
    }

    /// <summary>Drops the still and goes back to the camera feed.</summary>
    void ShowLiveCamera()
    {
        bool hadPhoto = _livePhoto != null;
        ReleasePhoto();
        if (hadPhoto && _detector != null) _detector.enabled = true;
        CloseMenus(restore: false);
        ResetToSearching();
    }

    /// <summary>Frames the part (auto-crop) or the whole photo (zoomed out). The view eases between the two.</summary>
    void ZoomPhoto(bool toPart)
    {
        _photoZoomed = toPart && _boxInPicture && _livePhoto != null;
        _photoFocus = WholePhoto;
        if (_photoZoomed)
        {
            // The part's own shape with a margin round it, so it reads in context, but never closer than a third
            // of the photo: past that a phone photo just goes soft.
            float w = _livePhoto.width, h = _livePhoto.height, least = Mathf.Min(w, h) / 3f;
            _photoFocus = PixelRegion(new Vector2(_pictureBox.center.x * w, _pictureBox.center.y * h),
                Mathf.Max(_pictureBox.width * w * 1.5f, least), Mathf.Max(_pictureBox.height * h * 1.5f, least));
        }
        RefreshZoomButton();
    }

    void RefreshZoomButton()
    {
        var button = _root?.Q("btn-photo-zoom");
        if (button == null) return;
        bool shown = _livePhoto != null && _boxInPicture;
        UIUtil.SetVisible(button, shown);
        _root.Q("ar-topbar")?.EnableInClassList("ar-topbar--crowded", shown);
        var icon = _root.Q<LineIcon>("photo-zoom-icon");
        if (icon != null) icon.icon = _photoZoomed ? "zoom-out" : "zoom-in";
    }

    /// <summary>Shows a part's card without a scan (for ARComponentPlacer, through ShowComponent).</summary>
    void ShowChosen(ComponentDefinitionSO def)
    {
        // Chosen elsewhere: whatever photo was up didn't show it, so go back to the camera behind the card.
        if (_livePhoto != null) ShowLiveCamera();
        _targetBox = _box = ScanFrameRect();
        _boxValid = true;
        _lastSeen = Time.time + 3600f; // keep the frame box until the user closes the card
        _detectLabel.text = def.displayName;
        Recognise(def, askKind: false);   // chosen by name, so the kind is already known
    }

    /// <summary>Allows other scripts (e.g. ARComponentPlacer) to show a component card.</summary>
    public void ShowComponent(ComponentDefinitionSO def) { if (def != null) ShowChosen(def); }
    public void HideDetectedComponent() => ResetToSearching();

    Rect ScanFrameRect()
    {
        var panel = _root.layout;
        var f = _scanFrame.worldBound;
        if (panel.width <= 0 || float.IsNaN(f.width)) return new Rect(0.16f, 0.3f, 0.68f, 0.3f);
        return new Rect(f.x / panel.width, f.y / panel.height, f.width / panel.width, f.height / panel.height);
    }

    // ------------------------------------------------------------------ per-frame visuals

    void Animate()
    {
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);

        _sweep = (_sweep + dt / sweepSeconds) % 2f;
        float t = _sweep < 1f ? _sweep : 2f - _sweep;
        _scanLine.style.top = Length.Percent(Mathf.Lerp(6f, 92f, Mathf.SmoothStep(0, 1, t)));

        bool framed = _livePhoto != null && UpdatePhotoView(dt);

        bool showBox = _boxValid && _state != State.Searching && (!_boxInPicture || framed);
        UIUtil.SetVisible(_detectBox, showBox);
        if (!showBox) { if (_labelsLayer.childCount > 0) _labelsLayer.Clear(); return; }

        if (_boxInPicture)
        {
            // The photo view already eases as it zooms; the box rides on it exactly.
            _box = _targetBox = PhotoToOverlay(_pictureBox);
        }
        else
        {
            float k = 1f - Mathf.Exp(-dt * 14f);
            _box = new Rect(Vector2.Lerp(_box.position, _targetBox.position, k), Vector2.Lerp(_box.size, _targetBox.size, k));
        }
        _detectBox.style.left = Length.Percent(_box.x * 100f);
        _detectBox.style.top = Length.Percent(_box.y * 100f);
        _detectBox.style.width = Length.Percent(_box.width * 100f);
        _detectBox.style.height = Length.Percent(_box.height * 100f);

        UpdateLabels();
    }

    /// <summary>
    /// Eases the photo toward framing <see cref="_photoFocus"/> in the space left clear by the top bar and the
    /// card. Returns false until the view has been laid out.
    /// </summary>
    bool UpdatePhotoView(float dt)
    {
        if (_photoView == null || _photoImage == null) return false;
        var size = _photoView.layout.size;
        if (float.IsNaN(size.x) || size.x <= 0f || size.y <= 0f) return false;

        var target = PhotoPlacement(_photoFocus, PhotoArea(size));
        if (!_photoPlaced) { _photoShown = target; _photoPlaced = true; }   // appears framed, not zooming in from nowhere
        float k = 1f - Mathf.Exp(-dt * 7f);
        _photoShown = new Rect(Vector2.Lerp(_photoShown.position, target.position, k),
                               Vector2.Lerp(_photoShown.size, target.size, k));

        _photoImage.style.left = _photoShown.x;
        _photoImage.style.top = _photoShown.y;
        _photoImage.style.width = _photoShown.width;
        _photoImage.style.height = _photoShown.height;
        return true;
    }

    /// <summary>The part of the photo view not under the top bar or the card (which moves as it slides in).</summary>
    Rect PhotoArea(Vector2 size)
    {
        const float margin = 12f;
        float top = 0f, bottom = size.y;
        if (_topBar != null) top = _photoView.WorldToLocal(new Vector2(0f, _topBar.worldBound.yMax)).y;
        // Whichever card is up (a hidden one sits below the screen, so the lower edge wins on its own).
        float sheetTop = Mathf.Min(_infoSheet.worldBound.yMin, _noPartSheet != null ? _noPartSheet.worldBound.yMin : float.MaxValue);
        float card = _photoView.WorldToLocal(new Vector2(0f, sheetTop)).y;
        if (!float.IsNaN(card)) bottom = Mathf.Min(bottom, card);
        if (float.IsNaN(top) || bottom - top < size.y * 0.25f) { top = 0f; bottom = size.y; }
        return Rect.MinMaxRect(margin, top + margin, size.x - margin, bottom - margin);
    }

    /// <summary>Where to draw the whole photo, in photo-view pixels, so that <paramref name="focus"/> fits the area.</summary>
    Rect PhotoPlacement(Rect focus, Rect area)
    {
        float w = _livePhoto.width, h = _livePhoto.height;
        float scale = Mathf.Min(area.width / Mathf.Max(1f, focus.width * w), area.height / Mathf.Max(1f, focus.height * h));
        var focusCentre = new Vector2(focus.center.x * w, focus.center.y * h) * scale;
        return new Rect(area.center - focusCentre, new Vector2(w, h) * scale);
    }

    /// <summary>A box in the photo (0..1) to the overlay's 0..1, where the photo is drawn right now.</summary>
    Rect PhotoToOverlay(Rect box)
    {
        var size = _photoView.layout.size;
        return new Rect((_photoShown.x + box.x * _photoShown.width) / size.x,
                        (_photoShown.y + box.y * _photoShown.height) / size.y,
                        box.width * _photoShown.width / size.x,
                        box.height * _photoShown.height / size.y);
    }

    void ToggleLabels()
    {
        _labelsOn = !_labelsOn;
        RefreshLabelsButton();
        _labelsLayer.Clear();
    }

    void RefreshLabelsButton()
    {
        _root.Q<Label>("btn-labels-label").text = _labelsOn ? "Hide labels" : "Show labels";
        _root.Q("btn-labels").EnableInClassList("btn-primary", _labelsOn);
        _root.Q("btn-labels").EnableInClassList("btn-navy", !_labelsOn);
    }

    void UpdateLabels()
    {
        if (!_labelsOn || _recognised == null) { if (_labelsLayer.childCount > 0) _labelsLayer.Clear(); return; }

        var defs = _recognised.arLabels;
        if (_labelsLayer.childCount != defs.Count)
        {
            _labelsLayer.Clear();
            foreach (var l in defs)
            {
                bool left = l.positionInBox.x > 0.6f; // put text on the side with more room
                var el = UIUtil.Box("ar-label", left ? "ar-label--left" : "ar-label--right");
                el.Add(UIUtil.Box("ar-label-dot"));
                el.Add(UIUtil.Box("ar-label-line"));
                el.Add(UIUtil.Text(l.text, "ar-label-text"));
                el.pickingMode = PickingMode.Ignore;
                _labelsLayer.Add(el);
            }
        }

        var size = _labelsLayer.layout.size;
        for (int i = 0; i < defs.Count; i++)
        {
            var el = _labelsLayer[i];
            float x = (_box.x + defs[i].positionInBox.x * _box.width) * size.x;
            float y = (_box.y + defs[i].positionInBox.y * _box.height) * size.y;
            bool left = el.ClassListContains("ar-label--left");
            // Anchor the dot on the point: left-aligned labels grow leftwards.
            el.style.left = left ? new StyleLength(StyleKeyword.Auto) : new StyleLength(x);
            el.style.right = left ? new StyleLength(size.x - x) : new StyleLength(StyleKeyword.Auto);
            el.style.top = y;
        }
    }
}
