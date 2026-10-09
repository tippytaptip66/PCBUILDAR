using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.UI;

/// <summary>Learn tab: category filter, guided path and lesson cards. Attach next to a UIDocument (LearnScreen.uxml).</summary>
[RequireComponent(typeof(UIDocument))]
public class LearnScreenController : MonoBehaviour
{
    enum LessonState { Locked, InProgress, Completed, Recommended, Available }

    UIDocument _doc;
    ComponentCategory? _filter;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(root);
        UIUtil.BindBottomNav(root, "nav-learn");
        _filter = null;
        BuildChips();
        Refresh();
    }

    void BuildChips()
    {
        var chips = _doc.rootVisualElement.Q<ScrollView>("category-chips");
        chips.Clear();
        chips.Add(MakeChip("All", "list", null));
        foreach (ComponentCategory c in Enum.GetValues(typeof(ComponentCategory)))
            chips.Add(MakeChip(c.DisplayName(), c.IconName(), c));
    }

    VisualElement MakeChip(string text, string icon, ComponentCategory? category)
    {
        var chip = UIUtil.MakeButton("", () => { _filter = category; Refresh(); }, "cat-chip");
        chip.userData = category;
        chip.Add(new LineIcon(icon));
        chip.Add(new Label(text));
        return chip;
    }

    void Refresh()
    {
        var root = _doc.rootVisualElement;
        root.Q<ScrollView>("category-chips").Query<Button>().ForEach(b =>
            b.EnableInClassList("active", Equals(b.userData as ComponentCategory?, _filter)));

        var db = ComponentDatabase.Instance;
        var data = ProgressManager.Instance?.Data;
        var list = root.Q("lesson-list");
        list.Clear();
        if (db == null || data == null) return;

        string recommendedId = db.Lessons
            .FirstOrDefault(l => GetBaseState(l, data) == LessonState.Available)?.lessonId;

        BuildPath(db, data);

        foreach (LessonSO.PathStage stage in Enum.GetValues(typeof(LessonSO.PathStage)))
        {
            var lessons = db.Lessons.Where(l => l.stage == stage && (_filter == null || l.relatedCategory == _filter)).ToList();
            if (lessons.Count == 0) continue;

            var header = UIUtil.Box("stage-title");
            header.Add(UIUtil.Text(((int)stage + 1).ToString(), "stage-num"));
            header.Add(UIUtil.Text(LessonSO.StageTitle(stage), "h3"));
            list.Add(header);

            foreach (var lesson in lessons)
            {
                var state = GetBaseState(lesson, data);
                if (state == LessonState.Available && lesson.lessonId == recommendedId) state = LessonState.Recommended;
                list.Add(MakeLessonCard(lesson, state));
            }
        }
    }

    static LessonState GetBaseState(LessonSO lesson, BuildAR.Save.SaveData data)
    {
        if (data.completedLessonIds.Contains(lesson.lessonId)) return LessonState.Completed;
        if (data.inProgressLessonIds.Contains(lesson.lessonId)) return LessonState.InProgress;
        if (lesson.prerequisiteLessonIds.Any(p => !data.completedLessonIds.Contains(p))) return LessonState.Locked;
        return LessonState.Available;
    }

    void BuildPath(ComponentDatabase db, BuildAR.Save.SaveData data)
    {
        var steps = _doc.rootVisualElement.Q("path-steps");
        while (steps.childCount > 1) steps.RemoveAt(1); // keep the connecting line

        int doneStages = 0;
        bool currentMarked = false;
        foreach (LessonSO.PathStage stage in Enum.GetValues(typeof(LessonSO.PathStage)))
        {
            var lessons = db.Lessons.Where(l => l.stage == stage).ToList();
            bool done = lessons.Count > 0 && lessons.All(l => data.completedLessonIds.Contains(l.lessonId));
            if (done) doneStages++;

            var step = UIUtil.Box("path-step");
            var node = UIUtil.Box("path-node");
            if (done) { step.AddToClassList("done"); node.Add(new LineIcon("check")); }
            else
            {
                if (!currentMarked) { step.AddToClassList("current"); currentMarked = true; }
                node.Add(new Label(((int)stage + 1).ToString()));
            }
            step.Add(node);
            step.Add(UIUtil.Text(LessonSO.StageTitle(stage), "path-label"));
            steps.Add(step);
        }
        _doc.rootVisualElement.Q<Label>("path-summary").text = $"{doneStages} of {steps.childCount - 1} stages";
    }

    VisualElement MakeLessonCard(LessonSO lesson, LessonState state)
    {
        var card = UIUtil.MakeButton("", () => OnLessonTapped(lesson, state), "lesson-card");
        if (state == LessonState.Locked) card.AddToClassList("locked");
        if (state == LessonState.Recommended) card.AddToClassList("recommended");

        var icon = UIUtil.Box("lesson-icon");
        icon.Add(new LineIcon(state == LessonState.Locked ? "lock" : lesson.relatedCategory.IconName()));
        card.Add(icon);

        var info = UIUtil.Box("lesson-info");
        info.Add(UIUtil.Text(lesson.title, "lesson-title"));
        var meta = UIUtil.Box("lesson-meta");
        meta.Add(new Label(lesson.difficulty.ToString()));
        meta.Add(new LineIcon("clock"));
        meta.Add(new Label($"{lesson.estimatedMinutes} min"));
        info.Add(meta);
        info.Add(StatusPill(state));
        card.Add(info);

        card.Add(new LineIcon("chevron-right", "icon-18", "icon-muted"));
        return card;
    }

    static VisualElement StatusPill(LessonState state)
    {
        string text, cls, icon;
        switch (state)
        {
            case LessonState.Locked: text = "Locked"; cls = "pill--gray"; icon = "lock"; break;
            case LessonState.InProgress: text = "In progress"; cls = "pill--orange"; icon = "clock"; break;
            case LessonState.Completed: text = "Completed"; cls = "pill--green"; icon = "check"; break;
            case LessonState.Recommended: text = "Recommended"; cls = "pill--blue"; icon = "star"; break;
            default: text = "Start"; cls = "pill--blue"; icon = "play"; break;
        }
        var pill = UIUtil.Box("pill", cls, "lesson-status");
        pill.Add(new LineIcon(icon));
        pill.Add(new Label(text));
        return pill;
    }

    void OnLessonTapped(LessonSO lesson, LessonState state)
    {
        if (state == LessonState.Locked)
        {
            var prereq = ComponentDatabase.Instance.Lessons.FirstOrDefault(l => lesson.prerequisiteLessonIds.Contains(l.lessonId)
                                                                            && !ProgressManager.Instance.Data.completedLessonIds.Contains(l.lessonId));
            UIUtil.Toast(_doc.rootVisualElement, "Lesson locked", prereq != null ? $"Finish \"{prereq.title}\" first." : "Finish earlier lessons first.", "lock");
            return;
        }
        var component = ComponentDatabase.Instance.ComponentForLesson(lesson);
        if (component == null) { Debug.LogWarning($"BuildAR: lesson '{lesson.lessonId}' has no related component."); return; }
        ProgressManager.Instance?.MarkLessonInProgress(lesson.lessonId);
        GameManager.Instance?.OpenComponentDetail(component, "overview", lesson);
    }
}
