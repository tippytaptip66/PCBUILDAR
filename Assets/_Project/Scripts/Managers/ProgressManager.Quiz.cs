using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildAR.Data;
using BuildAR.Quiz;
using BuildAR.Save;

namespace BuildAR.Managers
{
    /// <summary>Memorize state: card schedules, hearts, XP, coins, streaks and the shop.</summary>
    public partial class ProgressManager
    {
        public enum DayState { Future, Missed, Done, Frozen, TodayPending }

        const string DateFormat = "yyyy-MM-dd";
        const int DayLogLength = 60;

        public SaveData.QuizSettings QuizSettings => Data.quizSettings;
        public void SaveQuizSettings() => Persist();

        /// <summary>Writes quiz changes made during a round (one save per answer).</summary>
        public void SaveQuiz() => Persist();

        // ------------------------------------------------------------------ cards

        public SaveData.CardState FindCard(string key) => Data.cards.Find(c => c.key == key);

        public SaveData.CardState CardFor(string key)
        {
            var s = FindCard(key);
            if (s == null) { s = new SaveData.CardState { key = key }; Data.cards.Add(s); }
            return s;
        }

        public int CardLevel(QuizQuestionSO q) => FindCard(q.Key)?.level ?? 0;

        public float DeckMastery01(IReadOnlyCollection<QuizQuestionSO> cards) =>
            cards.Count == 0 ? 0f : cards.Sum(c => SpacedRepetition.Mastery01(FindCard(c.Key))) / cards.Count;

        public int DueCount(IEnumerable<QuizQuestionSO> cards)
        {
            var now = DateTime.UtcNow;
            return cards.Count(c => { var s = FindCard(c.Key); return !SpacedRepetition.IsNew(s) && SpacedRepetition.IsDue(s, now); });
        }

        public int NewCount(IEnumerable<QuizQuestionSO> cards) => cards.Count(c => SpacedRepetition.IsNew(FindCard(c.Key)));

        public int CardsLearned => Data.cards.Count(c => c.level >= 1);

        // ------------------------------------------------------------------ hearts

        public int Hearts { get { RefreshHearts(); return Data.hearts; } }
        public int SuperHearts => Data.superHearts;

        /// <summary>False when out of hearts. Super Hearts only help if you had them before running out.</summary>
        public bool CanQuiz { get { RefreshHearts(); return !Data.heartsLockedOut && (Data.hearts > 0 || Data.superHearts > 0); } }

        public TimeSpan HeartsRefillIn
        {
            get
            {
                RefreshHearts();
                return Data.heartsRefillTicks == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, Data.heartsRefillTicks - DateTime.UtcNow.Ticks));
            }
        }

        /// <summary>Hearts refill to full 10 minutes after the last one was lost.</summary>
        void RefreshHearts()
        {
            if (Data.heartsRefillTicks == 0 || DateTime.UtcNow.Ticks < Data.heartsRefillTicks) return;
            Data.hearts = QuizRewards.MaxHearts;
            Data.heartsRefillTicks = 0;
            Data.heartsLockedOut = false;
        }

        /// <summary>Uses a red heart, or a Super Heart when red ones are gone. Returns false if none were left.</summary>
        public bool LoseHeart()
        {
            RefreshHearts();
            if (Data.hearts > 0) Data.hearts--;
            else if (Data.superHearts > 0) Data.superHearts--;
            else return false;
            Data.heartsRefillTicks = (DateTime.UtcNow + TimeSpan.FromMinutes(QuizRewards.HeartRefillMinutes)).Ticks;
            if (Data.hearts == 0 && Data.superHearts == 0) Data.heartsLockedOut = true;
            return true;
        }

        // ------------------------------------------------------------------ xp / coins / shop

        /// <summary>
        /// Saved as it is earned. These used to be kept in memory until the round finished, so leaving a round
        /// part-way through threw away the XP and coins won in it — and the totals on Home never moved until the
        /// end either. Persisting here also refreshes every screen showing a balance.
        /// </summary>
        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            RollWeek();
            Data.xp += amount;
            Data.weekXp += amount;
            Persist();
        }

        public int WeekXp { get { RollWeek(); return Data.weekXp; } }

        void RollWeek()
        {
            var today = DateTime.Now.Date;
            string monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).ToString(DateFormat, CultureInfo.InvariantCulture);
            if (Data.weekStart == monday) return;
            Data.weekStart = monday;
            Data.weekXp = 0;
        }

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Data.coins += amount;
            Persist();
        }

        public int Owned(QuizRewards.ShopKind kind)
        {
            switch (kind)
            {
                case QuizRewards.ShopKind.Hint: return Data.hints;
                case QuizRewards.ShopKind.SuperHeart: return Data.superHearts;
                default: return Data.streakFreezes;
            }
        }

        public bool TryBuy(QuizRewards.ShopItem item)
        {
            if (Data.coins < item.price) return false;
            Data.coins -= item.price;
            switch (item.kind)
            {
                case QuizRewards.ShopKind.Hint: Data.hints++; break;
                case QuizRewards.ShopKind.SuperHeart: Data.superHearts++; break;
                default: Data.streakFreezes++; break;
            }
            Persist();
            return true;
        }

        public bool TryUseHint()
        {
            if (Data.hints <= 0) return false;
            Data.hints--;
            Persist();
            return true;
        }

        // ------------------------------------------------------------------ streak

        public int CurrentStreak { get { ApplyStreakFreezes(DateTime.Now.Date); return Data.streak; } }

        public int QuestionsToday => DayEntry(DateTime.Now.Date, false)?.questions ?? 0;

        /// <summary>Counts one tested answer towards today's streak.</summary>
        public void RecordQuestionAnswered()
        {
            var today = DateTime.Now.Date;
            ApplyStreakFreezes(today);
            var entry = DayEntry(today, true);
            entry.questions++;
            if (entry.questions != QuizRewards.StreakDailyMinimum) return;

            var last = ParseDate(Data.streakLastDay);
            if (last == today) return;
            Data.streak = last == today.AddDays(-1) ? Data.streak + 1 : 1;
            Data.streakLastDay = FormatDate(today);
        }

        /// <summary>Spends freezes on missed days since the last streak day, or breaks the streak.</summary>
        void ApplyStreakFreezes(DateTime today)
        {
            var last = ParseDate(Data.streakLastDay);
            if (!last.HasValue || Data.streak == 0) return;
            for (var day = last.Value.AddDays(1); day < today; day = day.AddDays(1))
            {
                if (Data.streakFreezes <= 0) { Data.streak = 0; return; }
                Data.streakFreezes--;
                DayEntry(day, true).frozen = true;
                Data.streakLastDay = FormatDate(day);
            }
        }

        /// <summary>Monday to Sunday of the current week.</summary>
        public DayState[] CurrentWeek()
        {
            var today = DateTime.Now.Date;
            ApplyStreakFreezes(today);
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var week = new DayState[7];
            for (int i = 0; i < 7; i++)
            {
                var day = monday.AddDays(i);
                var entry = DayEntry(day, false);
                if (day > today) week[i] = DayState.Future;
                else if (entry != null && entry.questions >= QuizRewards.StreakDailyMinimum) week[i] = DayState.Done;
                else if (entry != null && entry.frozen) week[i] = DayState.Frozen;
                else week[i] = day == today ? DayState.TodayPending : DayState.Missed;
            }
            return week;
        }

        SaveData.DayLog DayEntry(DateTime day, bool create)
        {
            string key = FormatDate(day);
            var entry = Data.dayLog.Find(d => d.date == key);
            if (entry != null || !create) return entry;
            entry = new SaveData.DayLog { date = key };
            Data.dayLog.Add(entry);
            if (Data.dayLog.Count > DayLogLength) Data.dayLog.RemoveRange(0, Data.dayLog.Count - DayLogLength);
            return entry;
        }

        static string FormatDate(DateTime d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

        static DateTime? ParseDate(string s) =>
            DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        // ------------------------------------------------------------------ rounds

        /// <summary>Call when a Memorize round ends.</summary>
        public void CompleteRound(QuizDeck deck, bool answeredAny)
        {
            if (answeredAny && !Data.starterFreezeGiven)
            {
                Data.starterFreezeGiven = true;
                Data.streakFreezes++;
            }

            var db = ComponentDatabase.Instance;
            if (db != null)
                foreach (var c in db.All)
                {
                    var cards = QuizDeck.ForComponent(c).Cards;
                    bool inDeck = deck.ComponentId == c.id || (deck.ComponentId == null && cards.Any(deck.Cards.Contains));
                    if (inDeck && cards.Count > 0 && DeckMastery01(cards) * 100f >= MasteryScore)
                        AddUnique(Data.masteredComponentIds, c.id);
                }
            Persist();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugRefillHearts()
        {
            Data.hearts = QuizRewards.MaxHearts;
            Data.heartsRefillTicks = 0;
            Data.heartsLockedOut = false;
            Persist();
        }

        public void DebugAddCoins(int amount) { Data.coins += amount; Persist(); }

        /// <summary>Moves quiz timestamps one day back, as if a day had passed (cards come due, streak days shift).</summary>
        public void DebugSkipDay()
        {
            long day = TimeSpan.TicksPerDay;
            foreach (var c in Data.cards)
            {
                if (c.dueTicks > 0) c.dueTicks -= day;
                if (c.lastReviewTicks > 0) c.lastReviewTicks -= day;
            }
            if (Data.heartsRefillTicks > 0) Data.heartsRefillTicks -= day;
            foreach (var d in Data.dayLog) d.date = Shift(d.date);
            Data.streakLastDay = Shift(Data.streakLastDay);
            Persist();

            string Shift(string s) { var parsed = ParseDate(s); return parsed.HasValue ? FormatDate(parsed.Value.AddDays(-1)) : s; }
        }
#endif
    }
}
