using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Assembly;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.Quiz;
using BuildAR.UI;
using BuildAR.Viewer;

/// <summary>
/// Component Detail: interactive 3D model + Overview / Parts / Compatibility / Installation / Quiz tabs.
/// Shows GameManager.SelectedComponent. Attach next to a UIDocument (ComponentDetail.uxml).
/// Needs a ModelViewerStage in the same scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ComponentDetailController : MonoBehaviour
{
    static readonly string[] Tabs = { "overview", "parts", "compatibility", "installation", "quiz" };
    const float TapSlopPixels = 8f;

    UIDocument _doc;
    VisualElement _root, _viewer, _hotspotLayer, _hotspotCard;
    ScrollView _body;
    ComponentDefinitionSO _def;
    LessonSO _lesson;
    bool _compactViewer;   // the lesson checklist shows under the tabs, so the viewer gives it room
    string _tab;
    int _selectedHotspot = -1;
    readonly List<VisualElement> _markers = new List<VisualElement>();
    readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
    float _pinchDistance;
    IVisualElementScheduledItem _markerTick, _pulseTick;

    ModelViewerStage Stage => ModelViewerStage.Instance;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        _root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(_root);

        _viewer = _root.Q("viewer");
        _hotspotLayer = _root.Q("hotspot-layer");
        _hotspotCard = _root.Q("hotspot-card");
        _body = _root.Q<ScrollView>("tab-body");

        UIUtil.OnClick(_root, "btn-back", () => GameManager.Instance?.Back());
        UIUtil.OnClick(_root, "btn-scan", () => GameManager.Instance?.GoToARScanner());
        UIUtil.OnClick(_root, "btn-zoom-in", () => Stage?.Zoom(1.25f));
        UIUtil.OnClick(_root, "btn-zoom-out", () => Stage?.Zoom(0.8f));
        UIUtil.OnClick(_root, "btn-reset-view", () => Stage?.ResetView());
        UIUtil.OnClick(_root, "btn-explode", ToggleExplode);
        UIUtil.OnClick(_root, "btn-complete-lesson", CompleteLesson);
        foreach (var t in Tabs) { var tab = t; UIUtil.OnClick(_root, "tab-" + tab, () => SelectTab(tab)); }

        _viewer.RegisterCallback<PointerDownEvent>(OnPointerDown);
        _viewer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        _viewer.RegisterCallback<PointerUpEvent>(OnPointerUp);
        _viewer.RegisterCallback<PointerCancelEvent>(e => _pointers.Remove(e.pointerId));
        _viewer.RegisterCallback<WheelEvent>(e => Stage?.Zoom(e.delta.y < 0 ? 1.1f : 0.9f));
        _viewer.RegisterCallback<GeometryChangedEvent>(_ => ResizeViewer());
        _root.RegisterCallback<GeometryChangedEvent>(_ => SizeViewer());

        _def = GameManager.Instance != null ? GameManager.Instance.SelectedComponent : null;
        _lesson = GameManager.Instance != null ? GameManager.Instance.ActiveLesson : null;
        if (_def == null && ComponentDatabase.Instance != null) _def = ComponentDatabase.Instance.All.FirstOrDefault();
        if (_def == null) { Debug.LogWarning("BuildAR: no component to show. Run BuildAR > Setup > Generate Sample Content."); return; }
        _compactViewer = _lesson != null && ProgressManager.Instance != null
                         && !ProgressManager.Instance.Data.completedLessonIds.Contains(_lesson.lessonId);
        SizeViewer();

        Populate();
        SelectTab(GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.DetailInitialTab)
            ? GameManager.Instance.DetailInitialTab : "overview");

        _markerTick = _root.schedule.Execute(UpdateMarkers).Every(16);
        _pulseTick = _root.schedule.Execute(() => { foreach (var m in _markers) m.ToggleInClassList("pulse"); }).Every(900);
    }

    void OnDisable()
    {
        _markerTick?.Pause();
        _pulseTick?.Pause();
        _pointers.Clear();
        Stage?.Hide();
    }

    // ---------------------------------------------------------------- header + viewer

    void Populate()
    {
        _root.Q<Label>("crumb").text = _def.category.DisplayName();
        _root.Q<Label>("detail-name").text = _def.displayName;
        _root.Q<Label>("detail-sub").text = _def.category.LongName();
        bool mastered = ProgressManager.Instance != null && ProgressManager.Instance.Data.masteredComponentIds.Contains(_def.id);
        UIUtil.SetVisible(_root.Q("mastery-pill"), mastered);

        RefreshLessonFooter();

        if (Stage == null) { Debug.LogWarning("BuildAR: no ModelViewerStage in the App scene."); return; }
        var size = ViewerPixelSize();
        var rt = Stage.Show(_def, size.x, size.y);
        _viewer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        UIUtil.SetVisible(_root.Q("btn-explode"), Stage.HasExplodableParts);
        BuildMarkers();
    }

    Vector2Int ViewerPixelSize()
    {
        var r = _viewer.layout;
        float w = float.IsNaN(r.width) || r.width < 10 ? 350 : r.width;
        float h = float.IsNaN(r.height) || r.height < 10 ? 300 : r.height;
        float scale = PanelToScreenScale();
        return new Vector2Int(Mathf.RoundToInt(w * scale), Mathf.RoundToInt(h * scale));
    }

    float PanelToScreenScale()
    {
        if (_root.panel == null) return 2f;
        float panelHeight = _root.panel.visualTree.layout.height;
        return panelHeight > 0 && !float.IsNaN(panelHeight) ? Screen.height / panelHeight : 2f;
    }

    /// <summary>
    /// Sets the viewer's height from the screen alone, shorter when the lesson checklist is showing. It used
    /// to take whatever the tab's content left over, so each tab switch resized the render and the model
    /// jumped. Decided once per visit: finishing the lesson hides the checklist without moving the model.
    /// </summary>
    void SizeViewer()
    {
        if (_root.panel == null) return;
        float panelHeight = _root.panel.visualTree.layout.height;
        if (float.IsNaN(panelHeight) || panelHeight <= 0f) return;
        float height = Mathf.Round(Mathf.Clamp(panelHeight * (_compactViewer ? 0.3f : 0.36f), 180f, 300f));
        if (Mathf.Abs(_viewer.resolvedStyle.height - height) > 0.5f) _viewer.style.height = height;
    }

    void ResizeViewer()
    {
        if (Stage == null || Stage.Texture == null) return;
        var size = ViewerPixelSize();
        Stage.Resize(size.x, size.y);
        _viewer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(Stage.Texture));
    }

    void ToggleExplode()
    {
        if (Stage == null) return;
        bool exploded = !Stage.IsExploded;
        Stage.SetExploded(exploded);
        _root.Q<Button>("btn-explode").EnableInClassList("icon-btn--dark", exploded);
    }

    void BuildMarkers()
    {
        _hotspotLayer.Clear();
        _markers.Clear();
        _selectedHotspot = -1;
        UIUtil.SetVisible(_hotspotCard, false);
        if (Stage == null) return;

        for (int i = 0; i < Stage.Hotspots.Count; i++)
        {
            int index = i;
            var marker = UIUtil.Box("hotspot");
            marker.Add(UIUtil.Box("hotspot-ring"));
            marker.Add(UIUtil.Box("hotspot-dot"));
            marker.pickingMode = PickingMode.Position;
            marker.AddManipulator(UIUtil.Tap(() => SelectHotspot(index)));
            _hotspotLayer.Add(marker);
            _markers.Add(marker);
        }
    }

    void UpdateMarkers()
    {
        if (Stage == null || _markers.Count != Stage.Hotspots.Count) return;
        var size = _hotspotLayer.layout.size;
        for (int i = 0; i < _markers.Count; i++)
        {
            var hotspot = Stage.Hotspots[i];
            var vp = hotspot != null ? Stage.ViewportPoint(hotspot) : null;
            UIUtil.SetVisible(_markers[i], vp.HasValue);
            if (!vp.HasValue) continue;
            _markers[i].style.left = vp.Value.x * size.x;
            _markers[i].style.top = (1f - vp.Value.y) * size.y;
            _markers[i].EnableInClassList("hidden-side", !hotspot.IsFacing(Stage.Camera));
        }
    }

    void SelectHotspot(int index)
    {
        if (Stage == null || index < 0 || index >= Stage.Hotspots.Count) return;
        _selectedHotspot = _selectedHotspot == index ? -1 : index;
        for (int i = 0; i < _markers.Count; i++) _markers[i].EnableInClassList("selected", i == _selectedHotspot);

        UIUtil.SetVisible(_hotspotCard, _selectedHotspot >= 0);
        if (_selectedHotspot >= 0)
        {
            var h = Stage.Hotspots[index];
            _root.Q<Label>("hotspot-title").text = h.title;
            _root.Q<Label>("hotspot-body").text = h.description;
        }
        UIUtil.SetVisible(_root.Q("viewer-hint"), _selectedHotspot < 0);
        if (_tab == "parts") SelectTab("parts");
    }

    // ---------------------------------------------------------------- gestures

    static bool IsControl(IEventHandler target)
    {
        for (var el = target as VisualElement; el != null; el = el.parent)
        {
            if (el is Button || el.ClassListContains("hotspot") || el.ClassListContains("hotspot-card")) return true;
            if (el.name == "viewer") return false;
        }
        return false;
    }

    Vector2 _downPosition;

    void OnPointerDown(PointerDownEvent e)
    {
        if (IsControl(e.target)) return;
        _pointers[e.pointerId] = e.position;
        _viewer.CapturePointer(e.pointerId);
        if (_pointers.Count == 1) _downPosition = e.position;
        if (_pointers.Count == 2) _pinchDistance = PinchDistance();
    }

    void OnPointerMove(PointerMoveEvent e)
    {
        if (!_pointers.ContainsKey(e.pointerId) || Stage == null) return;
        var previous = _pointers[e.pointerId];
        _pointers[e.pointerId] = e.position;

        if (_pointers.Count >= 2)
        {
            float d = PinchDistance();
            if (_pinchDistance > 1f && d > 1f) Stage.Zoom(d / _pinchDistance);
            _pinchDistance = d;
        }
        else
        {
            Stage.Orbit((Vector2)e.position - previous);
        }
    }

    void OnPointerUp(PointerUpEvent e)
    {
        if (!_pointers.Remove(e.pointerId)) return;
        _viewer.ReleasePointer(e.pointerId);
        // A tap on empty space closes the hotspot card.
        if (_pointers.Count == 0 && Vector2.Distance(_downPosition, e.position) < TapSlopPixels && _selectedHotspot >= 0)
            SelectHotspot(_selectedHotspot);
    }

    float PinchDistance()
    {
        var p = _pointers.Values.Take(2).ToArray();
        return p.Length < 2 ? 0f : Vector2.Distance(p[0], p[1]);
    }

    // ---------------------------------------------------------------- tabs

    void SelectTab(string tab)
    {
        if (_def == null) return;
        _tab = tab;
        foreach (var t in Tabs) _root.Q<Button>("tab-" + t).EnableInClassList("active", t == tab);
        // The tab row is wider than a phone; a tab chosen from the checklist may be off the edge.
        var tabs = _root.Q<ScrollView>("tabs");
        var selected = _root.Q<Button>("tab-" + tab);
        tabs.schedule.Execute(() => tabs.ScrollTo(selected));
        if (_lesson != null && ProgressManager.Instance != null)
        {
            ProgressManager.Instance.RecordLessonTab(_lesson.lessonId, tab);
            RefreshLessonFooter();
        }
        _body.Clear();
        _body.scrollOffset = Vector2.zero;

        switch (tab)
        {
            case "parts": BuildParts(); break;
            case "compatibility": BuildCompatibility(); break;
            case "installation": BuildInstallation(); break;
            case "quiz": BuildQuiz(); break;
            default: BuildOverview(); break;
        }
    }

    void BuildOverview()
    {
        _body.Add(UIUtil.Text(string.IsNullOrEmpty(_def.whatItDoes) ? _def.shortDescription : _def.whatItDoes, "para"));

        var facts = _def.keyFacts.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (facts.Count > 0)
        {
            _body.Add(UIUtil.Text("Key facts", "h3"));
            var card = UIUtil.Box("card");
            card.style.marginTop = 10;
            foreach (var fact in facts)
            {
                var row = UIUtil.Box("fact-row");
                var check = UIUtil.Box("fact-check");
                check.Add(new LineIcon("check"));
                row.Add(check);
                row.Add(UIUtil.Text(fact, "fact-text"));
                card.Add(row);
            }
            _body.Add(card);
        }

        if (_def.specChips.Count > 0)
        {
            var chips = UIUtil.Box("chip-row");
            chips.style.marginTop = 12;
            foreach (var c in _def.specChips) chips.Add(UIUtil.Text(c, "chip"));
            _body.Add(chips);
        }

        if (!string.IsNullOrEmpty(_def.safetyTip)) _body.Add(Callout(_def.safetyTip, warning: true));
    }

    void BuildParts()
    {
        var hotspots = Stage != null ? Stage.Hotspots : new List<ModelHotspot>();
        if (hotspots.Count == 0 && _def.connectionPoints.Count == 0)
        {
            // Learners never see how labels are made: the Overview has the same facts in words. (For developers:
            // labels come from the component's Model Labels, see BuildAR ▸ Setup ▸ Label Model Parts.)
            _body.Add(Callout("The parts of this model aren't marked yet. The Overview tab describes what it does and its key features.", warning: false));
            return;
        }

        _body.Add(UIUtil.Text("Tap a part to highlight it on the model.", "para"));
        for (int i = 0; i < hotspots.Count; i++)
        {
            int index = i;
            var row = UIUtil.MakeButton("", () => SelectHotspot(index), "part-row");
            if (i == _selectedHotspot) row.AddToClassList("selected");
            row.Add(UIUtil.Box("part-dot"));
            var text = UIUtil.Box("part-text");
            text.Add(UIUtil.Text(hotspots[i].title, "part-title"));
            if (!string.IsNullOrEmpty(hotspots[i].description)) text.Add(UIUtil.Text(hotspots[i].description, "part-desc"));
            row.Add(text);
            _body.Add(row);
        }

        foreach (var cp in _def.connectionPoints)
        {
            var row = UIUtil.Box("part-row");
            row.Add(new LineIcon("cable", "icon-18", "icon-blue"));
            var text = UIUtil.Box("part-text");
            text.style.marginLeft = 10;
            text.Add(UIUtil.Text($"{cp.label} → {cp.connectsTo.DisplayName()}", "part-title"));
            if (!string.IsNullOrEmpty(cp.description)) text.Add(UIUtil.Text(cp.description, "part-desc"));
            row.Add(text);
            _body.Add(row);
        }
    }

    void BuildCompatibility()
    {
        ProgressManager.Instance?.RecordCompatibilityCheck(_def.id);

        if (_def.compatibilityRules.Count == 0)
        {
            _body.Add(Callout("This part has no compatibility specs yet.", warning: false));
            return;
        }

        _body.Add(UIUtil.Text("Specs", "h3"));
        var specs = UIUtil.Box("card");
        specs.style.marginTop = 10;
        specs.style.paddingTop = 4;
        specs.style.paddingBottom = 4;
        foreach (var rule in _def.compatibilityRules)
        {
            var row = UIUtil.Box("spec-row");
            row.Add(UIUtil.Text(rule.Label, "spec-key"));
            row.Add(UIUtil.Text(rule.Value, "spec-val"));
            specs.Add(row);
        }
        _body.Add(specs);

        var title = UIUtil.Text("Works with", "h3");
        title.style.marginTop = 20;
        title.style.marginBottom = 10;
        _body.Add(title);

        var db = ComponentDatabase.Instance;
        var results = db == null ? new List<CompatibilityResult>()
            : db.All.Where(c => c != _def && c.category != _def.category)
                    .Select(c => CompatibilityChecker.CheckPair(_def, c))
                    .Where(r => r.hasSharedRules)
                    .OrderByDescending(r => r.status)
                    .ToList();

        if (results.Count == 0) _body.Add(Callout("No other parts share specs with this one yet.", warning: false));
        foreach (var r in results) _body.Add(CompatibilityRow(r));
    }

    static VisualElement CompatibilityRow(CompatibilityResult r)
    {
        string cls, icon, verdict;
        switch (r.status)
        {
            case CompatibilityStatus.Compatible: cls = "compat-row--green"; icon = "check"; verdict = "Compatible"; break;
            case CompatibilityStatus.Warning: cls = "compat-row--yellow"; icon = "warning"; verdict = "Check required"; break;
            default: cls = "compat-row--red"; icon = "close"; verdict = "Incompatible"; break;
        }
        var row = UIUtil.Box("compat-row", cls);
        var status = UIUtil.Box("compat-status");
        status.Add(new LineIcon(icon));
        row.Add(status);
        var text = UIUtil.Box("part-text");
        text.Add(UIUtil.Text($"{r.componentB.displayName} · {verdict}", "part-title"));
        text.Add(UIUtil.Text(r.message, "part-desc"));
        row.Add(text);
        return row;
    }

    void BuildInstallation()
    {
        _body.Add(Callout("Unplug the PC and touch bare metal on the case before handling parts.", warning: true));

        var steps = _def.installationSteps.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (steps.Count == 0) _body.Add(UIUtil.Text("Installation steps coming soon.", "para"));
        for (int i = 0; i < steps.Count; i++)
        {
            var row = UIUtil.Box("step-row");
            row.Add(UIUtil.Text((i + 1).ToString(), "step-num"));
            row.Add(UIUtil.Text(steps[i], "step-text"));
            _body.Add(row);
        }

        if (!string.IsNullOrEmpty(_def.safetyTip)) _body.Add(Callout(_def.safetyTip, warning: true));

        var practice = UIUtil.MakeButton("", () => GameManager.Instance?.GoToVirtualAssembly(), "btn", "btn-navy");
        practice.style.marginTop = 12;
        practice.Add(new Label("Practice in Virtual Assembly"));
        practice.Add(new LineIcon("cube"));
        _body.Add(practice);

        // Parts without a quiz are mastered by reading how to install them.
        if (_def.quizQuestions.Count == 0) ProgressManager.Instance?.MarkComponentMastered(_def.id);
    }

    /// <summary>
    /// In a lesson: the lesson quiz (the test that completes it) and, below it, Memorize for extra practice.
    /// Opened outside a lesson (from the parts list or the scanner) it's Memorize alone.
    /// </summary>
    void BuildQuiz()
    {
        if (_lesson != null && LessonQuizSession.CardsFor(_lesson).Count > 0)
        {
            BuildLessonQuiz();
            var practice = UIUtil.Text("Extra practice", "h3");
            practice.style.marginTop = 20;
            practice.style.marginBottom = 10;
            _body.Add(practice);
        }
        BuildMemorize();
    }

    void BuildLessonQuiz()
    {
        var progress = ProgressManager.Instance;
        int total = Mathf.Min(LessonQuizSession.QuestionCount, LessonQuizSession.CardsFor(_lesson).Count);
        int passMark = LessonQuizSession.PassMarkFor(total);
        int best = progress != null ? progress.LessonQuizBest(_lesson.lessonId) : -1;
        bool passed = best >= LessonQuizSession.PassPercent;

        var card = UIUtil.Box("card", "memorize-card", "lesson-quiz-card");
        var head = UIUtil.Box("memorize-head");
        var icon = UIUtil.Box("action-icon", passed ? "action-icon--green" : "action-icon--blue");
        icon.Add(new LineIcon(passed ? "check-circle" : "quiz"));
        head.Add(icon);
        var titles = UIUtil.Box("grow");
        titles.Add(UIUtil.Text("Lesson quiz", "h3"));
        titles.Add(UIUtil.Text($"{total} questions · pass with {passMark} correct ({LessonQuizSession.PassPercent}%)", "small"));
        head.Add(titles);
        card.Add(head);

        var status = UIUtil.Box("memorize-mastery");
        var ring = new ProgressRing { progress = Mathf.Max(0, best) };
        ring.AddToClassList("ring--sm");
        ring.Add(UIUtil.Text(best < 0 ? "–" : $"{best}%", "ring-label"));
        status.Add(ring);
        var text = UIUtil.Box("grow");
        text.Add(UIUtil.Text(best < 0 ? "Not taken yet" : passed ? "Passed" : "Not passed yet", "part-title"));
        text.Add(UIUtil.Text(
            best < 0 ? "Answer all the questions in one go. You can retake it as many times as you like."
            : passed ? $"Best score {best}%. Retake it any time to beat it."
            : $"Best score {best}%. You need {LessonQuizSession.PassPercent}% to complete the lesson.", "part-desc"));
        status.Add(text);
        card.Add(status);

        var start = UIUtil.MakeButton("", () => GameManager.Instance?.StartLessonQuiz(_lesson), "btn", "btn-primary");
        start.Add(new Label(best < 0 ? "Start quiz" : passed ? "Retake quiz" : "Try again"));
        start.Add(new LineIcon("arrow-right"));
        card.Add(start);
        _body.Add(card);
    }

    /// <summary>Spaced-repetition practice on this part's cards. Optional: it doesn't gate the lesson.</summary>
    void BuildMemorize()
    {
        var deck = QuizDeck.ForComponent(_def);
        var progress = ProgressManager.Instance;
        var card = UIUtil.Box("card", "memorize-card");

        var head = UIUtil.Box("memorize-head");
        var icon = UIUtil.Box("action-icon", "action-icon--orange");
        icon.Add(new LineIcon("quiz"));
        head.Add(icon);
        var titles = UIUtil.Box("grow");
        titles.Add(UIUtil.Text("Memorize", "h3"));
        titles.Add(UIUtil.Text($"{deck.Cards.Count} card{(deck.Cards.Count == 1 ? "" : "s")} · cards come back just before you'd forget them", "small"));
        head.Add(titles);
        card.Add(head);

        if (deck.Cards.Count == 0 || progress == null)
        {
            card.Add(UIUtil.Text("No quiz cards for this component yet.", "para"));
            _body.Add(card);
            return;
        }

        int mastery = Mathf.RoundToInt(progress.DeckMastery01(deck.Cards) * 100f);
        var masteryRow = UIUtil.Box("memorize-mastery");
        var ring = new ProgressRing { progress = mastery };
        ring.AddToClassList("ring--sm");
        ring.Add(UIUtil.Text($"{mastery}%", "ring-label"));
        masteryRow.Add(ring);
        var masteryText = UIUtil.Box("grow");
        masteryText.Add(UIUtil.Text("Deck mastery", "part-title"));
        masteryText.Add(UIUtil.Text(mastery >= ProgressManager.MasteryScore
            ? "Mastered. Keep reviewing when cards come due."
            : $"Reach {ProgressManager.MasteryScore}% to master this part. Cards grow stronger each day you get them right.", "part-desc"));
        masteryRow.Add(masteryText);
        card.Add(masteryRow);

        var chips = UIUtil.Box("chip-row");
        chips.Add(UIUtil.Text($"{progress.DueCount(deck.Cards)} due", "chip"));
        chips.Add(UIUtil.Text($"{progress.NewCount(deck.Cards)} new", "chip"));
        chips.Add(UIUtil.Text($"{deck.Cards.Count(c => progress.CardLevel(c) >= 1)} learned", "chip"));
        card.Add(chips);

        var start = UIUtil.MakeButton("", () => GameManager.Instance?.StartQuiz(deck), "btn", "btn-primary");
        start.Add(new Label("Memorize"));
        start.Add(new LineIcon("arrow-right"));
        card.Add(start);
        _body.Add(card);

        _body.Add(Callout("Tap the cog during a round to switch between mixed questions, multiple choice, typing and flashcards.", warning: false));
    }

    static VisualElement Callout(string text, bool warning)
    {
        var c = UIUtil.Box("callout", warning ? "callout--warning" : "callout--info");
        c.Add(new LineIcon(warning ? "shield" : "info"));
        c.Add(UIUtil.Text(text, "callout-text"));
        return c;
    }

    /// <summary>
    /// A lesson can only be completed after every tab has been opened and its quiz passed with 75% or more. (It used
    /// to need one Memorize round finished, which any score managed: missed cards come back until they're right.)
    /// </summary>
    void RefreshLessonFooter()
    {
        var progress = ProgressManager.Instance;
        var footer = _root.Q("lesson-footer");
        bool show = _lesson != null && _def != null && progress != null && !progress.Data.completedLessonIds.Contains(_lesson.lessonId);
        UIUtil.SetVisible(footer, show);
        if (!show) return;

        var list = _root.Q("lesson-checklist");
        list.Clear();
        list.Add(UIUtil.Box("check-line"));
        int left = 0, count = 0;
        void Step(string label, string tab, bool done)
        {
            count++;
            if (!done) left++;
            var step = UIUtil.MakeButton("", () => SelectTab(tab), "check-step");
            step.EnableInClassList("done", done);
            step.EnableInClassList("current", tab == _tab);
            var node = UIUtil.Box("check-node");
            if (done) node.Add(new LineIcon("check"));
            else node.Add(new Label(count.ToString()));
            step.Add(node);
            step.Add(UIUtil.Text(label, "check-label"));
            list.Add(step);
        }
        foreach (var tab in ProgressManager.LessonTabs)
            Step(StepName(tab), tab, progress.LessonTabViewed(_lesson.lessonId, tab));
        if (LessonQuizSession.CardsFor(_lesson).Count > 0) Step("Quiz", "quiz", progress.LessonQuizPassed(_lesson.lessonId));

        _root.Q<Button>("btn-complete-lesson").SetEnabled(left == 0);
        _root.Q<Label>("btn-complete-lesson-label").text = left == 0 ? "Complete lesson" : $"{left} step{(left == 1 ? "" : "s")} left";
        _root.Q<LineIcon>("btn-complete-lesson-icon").icon = left == 0 ? "check" : "lock";
        _root.Q<Label>("lesson-steps-hint").text = left == 0 ? "All done. Claim your lesson!" : $"{count - left} of {count} done";
        _lessonStepsLeft = left;
    }

    /// <summary>Short enough that five fit side by side on a phone.</summary>
    static string StepName(string tab)
    {
        switch (tab)
        {
            case "compatibility": return "Compat.";
            case "installation": return "Install";
            default: return char.ToUpperInvariant(tab[0]) + tab.Substring(1);
        }
    }

    int _lessonStepsLeft = int.MaxValue;

    void CompleteLesson()
    {
        if (_lesson == null || _lessonStepsLeft > 0) return;
        ProgressManager.Instance?.MarkLessonComplete(_lesson.lessonId);
        UIUtil.SetVisible(_root.Q("lesson-footer"), false);
        Confetti.Burst(_root); // plays the confetti sound
        UIUtil.Toast(_root, "Lesson complete", _lesson.title, "check-circle");
    }
}
