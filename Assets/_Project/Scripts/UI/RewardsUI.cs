using System;
using UnityEngine.UIElements;
using BuildAR.Managers;
using BuildAR.Quiz;

namespace BuildAR.UI
{
    /// <summary>Shared pieces for Memorize rewards: the shop sheet and the weekly streak strip.</summary>
    public static class RewardsUI
    {
        static readonly string[] DayLetters = { "M", "T", "W", "T", "F", "S", "S" };

        public static string Countdown(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

        /// <summary>Monday–Sunday dots: flame for streak days, snowflake for frozen days.</summary>
        public static VisualElement WeekStrip(ProgressManager progress, bool dark)
        {
            var strip = UIUtil.Box("week-strip");
            if (dark) strip.AddToClassList("week-strip--dark");
            var week = progress.CurrentWeek();
            for (int i = 0; i < week.Length; i++)
            {
                var day = UIUtil.Box("week-day", "week-day--" + week[i].ToString().ToLowerInvariant());
                var dot = UIUtil.Box("week-dot");
                if (week[i] == ProgressManager.DayState.Done || week[i] == ProgressManager.DayState.TodayPending) dot.Add(new LineIcon("flame"));
                else if (week[i] == ProgressManager.DayState.Frozen) dot.Add(new LineIcon("snowflake"));
                day.Add(dot);
                day.Add(UIUtil.Text(DayLetters[i], "week-label"));
                strip.Add(day);
            }
            return strip;
        }

        /// <summary>Bottom sheet where coins buy hints, Super Hearts and streak freezes.</summary>
        public static void ShowShop(VisualElement root, Action onChanged = null)
        {
            var progress = ProgressManager.Instance;
            if (progress == null) return;
            var screen = root.Q(className: "screen") ?? root;

            var overlay = UIUtil.Box("overlay");
            var backdrop = UIUtil.Box("overlay-backdrop");
            overlay.Add(backdrop);
            var sheet = UIUtil.Box("sheet", "shop-sheet", "sheet--hidden");
            overlay.Add(sheet);

            sheet.Add(UIUtil.Box("sheet-handle"));
            var head = UIUtil.Box("shop-head");
            var titles = UIUtil.Box("grow");
            titles.Add(UIUtil.Text("Shop", "h2"));
            titles.Add(UIUtil.Text("Earn a coin for every question you get right first time.", "small"));
            head.Add(titles);
            var balance = UIUtil.Box("hud-chip", "hud-chip--coin");
            balance.Add(new LineIcon("coin"));
            var balanceLabel = UIUtil.Text("0");
            balance.Add(balanceLabel);
            head.Add(balance);
            sheet.Add(head);

            var list = UIUtil.Box("shop-list");
            sheet.Add(list);

            void Close()
            {
                sheet.AddToClassList("sheet--hidden");
                overlay.schedule.Execute(overlay.RemoveFromHierarchy).StartingIn(320);
            }

            void Refresh()
            {
                balanceLabel.text = progress.Data.coins.ToString();
                list.Clear();
                foreach (var item in QuizRewards.Shop)
                {
                    var row = UIUtil.Box("shop-row");
                    var icon = UIUtil.Box("shop-icon", "shop-icon--" + item.kind.ToString().ToLowerInvariant());
                    icon.Add(new LineIcon(item.icon));
                    row.Add(icon);

                    var text = UIUtil.Box("shop-text");
                    text.Add(UIUtil.Text(item.title, "shop-title"));
                    text.Add(UIUtil.Text(item.description, "shop-desc"));
                    text.Add(UIUtil.Text($"You have {progress.Owned(item.kind)}", "shop-owned"));
                    row.Add(text);

                    var captured = item;
                    var buy = UIUtil.MakeButton("", () =>
                    {
                        if (!progress.TryBuy(captured)) { BuildAR.Managers.AudioManager.Instance?.PlayWarning(); return; }
                        BuildAR.Managers.AudioManager.Instance?.PlayCoin();
                        Refresh();
                        onChanged?.Invoke();
                    }, "btn", "btn-sm", "btn-price");
                    buy.Add(new LineIcon("coin"));
                    buy.Add(new Label(item.price.ToString()));
                    buy.SetEnabled(progress.Data.coins >= item.price);
                    row.Add(buy);
                    list.Add(row);
                }
            }

            var close = UIUtil.MakeButton("", Close, "btn", "btn-secondary");
            close.Add(new Label("Close"));
            sheet.Add(close);
            backdrop.AddManipulator(new Clickable(Close));

            Refresh();
            screen.Add(overlay);
            sheet.schedule.Execute(() => sheet.RemoveFromClassList("sheet--hidden")).StartingIn(30);
        }
    }
}
