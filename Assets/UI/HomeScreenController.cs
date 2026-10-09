using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.Quiz;
using BuildAR.UI;

/// <summary>Home dashboard. Attach next to a UIDocument (Source Asset = HomeScreen.uxml).</summary>
[RequireComponent(typeof(UIDocument))]
public class HomeScreenController : MonoBehaviour
{
    static readonly ComponentCategory[] HomeCategories =
    {
        ComponentCategory.CPU, ComponentCategory.RAM, ComponentCategory.Motherboard,
        ComponentCategory.Storage, ComponentCategory.PowerSupply
    };

    UIDocument _doc;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(root);
        UIUtil.BindBottomNav(root, "nav-home");

        if (UIUtil.BrandLogo != null) root.Q("app-logo").style.backgroundImage = new StyleBackground(UIUtil.BrandLogo);

        UIUtil.OnClick(root, "btn-resume", () => GameManager.Instance?.GoToVirtualAssembly());
        UIUtil.OnClick(root, "btn-profile", () => ProfileSetup.Show(root, firstTime: false, onDone: Refresh));
        UIUtil.OnClick(root, "btn-theme", ToggleTheme);
        UIUtil.OnClick(root, "btn-see-all", () => GameManager.Instance?.ShowScreen(AppScreen.Progress));
        UIUtil.OnClick(root, "qa-scan", () => GameManager.Instance?.GoToARScanner());
        UIUtil.OnClick(root, "qa-lesson", StartNextLesson);
        UIUtil.OnClick(root, "qa-practice", () => GameManager.Instance?.GoToVirtualAssembly());
        UIUtil.OnClick(root, "qa-quiz", StartDailyReview);
        UIUtil.OnClick(root, "chip-streak", () => GameManager.Instance?.ShowScreen(AppScreen.Progress));
        UIUtil.OnClick(root, "chip-level", () => GameManager.Instance?.ShowScreen(AppScreen.Progress));
        UIUtil.OnClick(root, "chip-coins", () => RewardsUI.ShowShop(root, Refresh));
        UIUtil.OnClick(root, "chip-hearts", ShowHearts);

        if (ProgressManager.Instance != null)
        {
            ProgressManager.Instance.OnProgressChanged += Refresh;
            ProgressManager.Instance.OnBadgeUnlocked += OnBadge;
            // Players who finished onboarding before profiles existed get the setup once.
            if (ProgressManager.Instance.Data.onboardingComplete && !ProgressManager.Instance.Data.profileSet)
                ProfileSetup.Show(root, firstTime: true, onDone: () => { Refresh(); StartTourIfPending(); });
            else StartTourIfPending();
        }
        Refresh();
    }

    /// <summary>First visit to Home: Sprout shows the learner around, once the screen has settled.</summary>
    void StartTourIfPending()
    {
        if (ProgressManager.Instance == null || !ProgressManager.Instance.Data.guideTourPending) return;
        var root = _doc.rootVisualElement;
        root.schedule.Execute(() =>
        {
            if (isActiveAndEnabled && ProgressManager.Instance.Data.guideTourPending) GuideTour.Show(root);
        }).StartingIn(600);
    }

    void OnDisable()
    {
        if (ProgressManager.Instance == null) return;
        ProgressManager.Instance.OnProgressChanged -= Refresh;
        ProgressManager.Instance.OnBadgeUnlocked -= OnBadge;
    }

    void OnBadge(BadgeCatalog.Badge b) => UIUtil.Toast(_doc.rootVisualElement, "Badge unlocked!", b.title, b.icon);

    void ToggleTheme()
    {
        Theme.Toggle();
        RefreshThemeButton();
    }

    /// <summary>The button shows where you're going: a moon in light mode, a sun in dark mode.</summary>
    void RefreshThemeButton()
    {
        var icon = _doc.rootVisualElement.Q<LineIcon>("theme-icon");
        if (icon != null) icon.icon = Theme.ToggleIcon;
    }

    void Refresh()
    {
        var root = _doc.rootVisualElement;
        var progress = ProgressManager.Instance;
        var db = ComponentDatabase.Instance;
        if (progress == null || db == null) return;

        RefreshThemeButton();
        string name = progress.DisplayName;
        root.Q<Label>("greeting").text = name.Length == 0 ? UIUtil.Greeting() : $"{UIUtil.Greeting()}, {name}";
        Avatars.Apply(root.Q("profile-avatar"), string.IsNullOrEmpty(progress.Data.profileAvatar) ? Avatars.DefaultId : progress.Data.profileAvatar);

        int overall = Mathf.RoundToInt(progress.OverallCompletion01() * 100f);
        root.Q<ProgressRing>("overall-ring").progress = overall;
        root.Q<Label>("overall-ring-label").text = $"{overall}%";

        // Hero: current step of the default build guide.
        var guide = db.DefaultGuide;
        if (guide != null && guide.steps.Count > 0)
        {
            var done = progress.Data.completedAssemblyStepIds;
            int completed = guide.steps.Count(s => done.Contains(s.stepId));
            int currentIndex = guide.steps.FindIndex(s => !done.Contains(s.stepId));
            bool finished = currentIndex < 0;
            var step = finished ? null : guide.steps[currentIndex];
            float pct = (float)completed / guide.steps.Count;

            root.Q<Label>("hero-count").text = finished ? "ALL STEPS DONE" : $"STEP {currentIndex + 1} OF {guide.steps.Count}";
            root.Q<Label>("hero-title").text = finished ? "Full build completed!" : step.title;
            root.Q<Label>("hero-desc").text = finished ? "Practice again or try the AR scanner on a real PC." : step.instruction;
            root.Q("hero-fill").style.width = Length.Percent(pct * 100f);
            root.Q<Label>("hero-percent").text = $"{Mathf.RoundToInt(pct * 100f)}% of build complete";
            root.Q<Label>("btn-resume-label").text = finished ? "Practice" : completed == 0 ? "Start" : "Resume";
        }

        var next = NextLesson();
        root.Q<Label>("qa-lesson-sub").text = next != null ? next.title : "All lessons done";

        int streak = progress.CurrentStreak;
        root.Q<Label>("chip-streak-label").text = streak.ToString();
        root.Q("chip-streak").EnableInClassList("inactive", streak == 0 || progress.QuestionsToday < QuizRewards.StreakDailyMinimum);
        root.Q<Label>("chip-hearts-label").text = progress.Hearts.ToString();
        root.Q<Label>("chip-coins-label").text = progress.Data.coins.ToString();
        QuizRewards.Level(progress.Data.xp, out int level, out _, out _);
        root.Q<Label>("chip-level-label").text = $"Lv {level}";

        var review = QuizDeck.DailyReview(db);
        int due = progress.DueCount(review.Cards), fresh = progress.NewCount(review.Cards);
        root.Q<Label>("qa-quiz-sub").text = due > 0 ? $"{due} card{(due == 1 ? "" : "s")} due" : fresh > 0 ? $"{fresh} new cards" : "All caught up";

        var list = root.Q("category-progress");
        list.Clear();
        foreach (var c in HomeCategories)
            list.Add(UIUtil.CategoryProgressRow(c, progress.CategoryCompletion01(c)));
        list[list.childCount - 1].AddToClassList("last");
    }

    LessonSO NextLesson()
    {
        var db = ComponentDatabase.Instance;
        var data = ProgressManager.Instance?.Data;
        if (db == null || data == null) return null;
        return db.Lessons.FirstOrDefault(l => data.inProgressLessonIds.Contains(l.lessonId))
            ?? db.Lessons.FirstOrDefault(l => !data.completedLessonIds.Contains(l.lessonId)
                                              && l.prerequisiteLessonIds.All(p => data.completedLessonIds.Contains(p)));
    }

    void StartNextLesson()
    {
        var lesson = NextLesson();
        if (lesson == null) { GameManager.Instance?.ShowScreen(AppScreen.Learn); return; }
        ProgressManager.Instance?.MarkLessonInProgress(lesson.lessonId);
        GameManager.Instance?.OpenComponentDetail(ComponentDatabase.Instance.ComponentForLesson(lesson), "overview", lesson);
    }

    void StartDailyReview()
    {
        var db = ComponentDatabase.Instance;
        if (db == null) return;
        var deck = QuizDeck.DailyReview(db);
        if (deck.Cards.Count == 0) { UIUtil.Toast(_doc.rootVisualElement, "No quiz yet", "Add quiz questions to components.", "info"); return; }
        GameManager.Instance?.StartQuiz(deck);
    }

    void ShowHearts()
    {
        var progress = ProgressManager.Instance;
        if (progress == null) return;
        int hearts = progress.Hearts;
        string body = hearts >= QuizRewards.MaxHearts
            ? "Full hearts. You lose one for each wrong answer or reveal in Memorize."
            : $"{hearts} left · full again in {RewardsUI.Countdown(progress.HeartsRefillIn)}";
        UIUtil.Toast(_doc.rootVisualElement, "Hearts", body, "heart");
    }
}
