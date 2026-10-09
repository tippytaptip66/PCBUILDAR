using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using BuildAR.Data;
using BuildAR.Managers;
using BuildAR.Quiz;
using BuildAR.UI;

/// <summary>My Progress tab. Attach next to a UIDocument (ProgressScreen.uxml).</summary>
[RequireComponent(typeof(UIDocument))]
public class ProgressScreenController : MonoBehaviour
{
    UIDocument _doc;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;
        UIUtil.PrepareScreen(root);
        UIUtil.BindBottomNav(root, "nav-progress");
        UIUtil.OnClick(root, "btn-shop", () => RewardsUI.ShowShop(root, Refresh));
        UIUtil.OnClick(root, "btn-inventory-shop", () => RewardsUI.ShowShop(root, Refresh));

        if (ProgressManager.Instance != null) ProgressManager.Instance.OnProgressChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (ProgressManager.Instance != null) ProgressManager.Instance.OnProgressChanged -= Refresh;
    }

    void Refresh()
    {
        var progress = ProgressManager.Instance;
        var db = ComponentDatabase.Instance;
        if (progress == null || db == null) return;
        var data = progress.Data;
        var root = _doc.rootVisualElement;

        root.Q<Label>("journey-label").text = progress.DisplayName.Length == 0 ? "Your journey" : $"{progress.DisplayName}'s journey";

        int overall = Mathf.RoundToInt(progress.OverallCompletion01() * 100f);
        root.Q<ProgressRing>("overall-ring").progress = overall;
        root.Q<Label>("overall-percent").text = $"{overall}%";
        root.Q<Label>("overall-title").text = overall >= 100 ? "Master builder!" : overall >= 60 ? "Almost there!" : overall >= 20 ? "Making progress" : "Great start!";
        int lessonsDone = db.Lessons.Count(l => data.completedLessonIds.Contains(l.lessonId));
        root.Q<Label>("overall-body").text = $"{lessonsDone} of {db.Lessons.Count} lessons · {data.scannedComponentIds.Count} parts scanned";

        int mastered = db.All.Count(c => data.masteredComponentIds.Contains(c.id));
        root.Q<Label>("stat-components").text = $"{mastered}/{db.All.Count}";

        var guide = db.DefaultGuide;
        int steps = guide == null ? 0 : guide.steps.Count(s => data.completedAssemblyStepIds.Contains(s.stepId));
        root.Q<Label>("stat-assembly").text = $"{steps}/{(guide == null ? 0 : guide.steps.Count)}";
        root.Q<Label>("stat-cards").text = progress.CardsLearned.ToString();

        int streak = progress.CurrentStreak;
        root.Q<Label>("streak-count").text = $"{streak} day streak";
        root.Q<Label>("streak-tier").text = StreakTierText(progress.QuestionsToday);
        root.Q<Label>("streak-freezes").text = data.streakFreezes.ToString();
        var weekHost = root.Q("week-strip-host");
        weekHost.Clear();
        weekHost.Add(RewardsUI.WeekStrip(progress, true));

        QuizRewards.Level(data.xp, out int level, out int into, out int needed);
        root.Q<Label>("level-number").text = level.ToString();
        root.Q<Label>("level-title").text = $"Level {level}";
        root.Q<Label>("level-sub").text = $"{into} / {needed} XP to level {level + 1}";
        root.Q("level-fill").style.width = Length.Percent(100f * into / needed);
        root.Q<Label>("coins-label").text = $"{data.coins} · Shop";
        root.Q<Label>("week-xp").text = $"{progress.WeekXp} XP this week";
        root.Q<Label>("inventory").text = $"{data.hints} hints · {data.superHearts} Super Hearts";
        BuildInventory(root, progress);

        var grid = root.Q("badge-grid");
        grid.Clear();
        foreach (var badge in BadgeCatalog.All)
        {
            bool earned = data.unlockedBadgeIds.Contains(badge.id);
            var item = UIUtil.Box("badge");
            item.EnableInClassList("earned", earned);
            var medal = UIUtil.Box("badge-medal");
            medal.Add(new LineIcon(badge.icon));
            if (!earned)
            {
                var lockBox = UIUtil.Box("badge-lock");
                lockBox.Add(new LineIcon("lock"));
                medal.Add(lockBox);
            }
            item.Add(medal);
            item.Add(UIUtil.Text(badge.title, "badge-title"));
            item.tooltip = badge.description;
            item.AddManipulator(UIUtil.Tap(() => UIUtil.Toast(root, badge.title, earned ? "Unlocked" : badge.description, earned ? badge.icon : "lock")));
            grid.Add(item);
        }
        root.Q<Label>("badge-count").text = $"{data.unlockedBadgeIds.Count(id => BadgeCatalog.All.Any(b => b.id == id))} of {BadgeCatalog.All.Length}";

        var list = root.Q("category-progress");
        list.Clear();
        foreach (ComponentCategory c in Enum.GetValues(typeof(ComponentCategory)))
            list.Add(UIUtil.CategoryProgressRow(c, progress.CategoryCompletion01(c)));
        list[list.childCount - 1].AddToClassList("last");
    }

    /// <summary>
    /// Everything bought in the shop, with what it's for and how many are left. Built from the same list the shop
    /// sells from, so an item added there appears here with no extra work.
    /// </summary>
    static void BuildInventory(VisualElement root, ProgressManager progress)
    {
        var list = root.Q("inventory-list");
        if (list == null) return;
        list.Clear();

        foreach (var item in QuizRewards.Shop)
        {
            int owned = progress.Owned(item.kind);
            var row = UIUtil.Box("shop-row");

            var icon = UIUtil.Box("shop-icon", "shop-icon--" + item.kind.ToString().ToLowerInvariant());
            icon.Add(new LineIcon(item.icon));
            row.Add(icon);

            var text = UIUtil.Box("shop-text");
            text.Add(UIUtil.Text(item.title, "shop-title"));
            text.Add(UIUtil.Text(item.description, "shop-desc"));
            row.Add(text);

            var count = UIUtil.Text(owned.ToString(), "stat-value");
            count.style.opacity = owned > 0 ? 1f : 0.4f;
            row.Add(count);
            list.Add(row);
        }
        if (list.childCount > 0) list[list.childCount - 1].AddToClassList("last");
    }

    static string StreakTierText(int questionsToday)
    {
        int tier = QuizRewards.TierIndex(questionsToday);
        if (tier < 0)
        {
            int more = QuizRewards.StreakDailyMinimum - questionsToday;
            return $"Answer {more} more question{(more == 1 ? "" : "s")} in Memorize today to keep it going.";
        }
        var reached = QuizRewards.StreakTiers[tier];
        if (tier == QuizRewards.StreakTiers.Length - 1) return $"Today: {reached.name}, the top tier!";
        var next = QuizRewards.StreakTiers[tier + 1];
        return $"Today: {reached.name} · {next.questions - questionsToday} more questions for {next.name}";
    }
}
