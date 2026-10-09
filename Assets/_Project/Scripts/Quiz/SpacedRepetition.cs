using System;
using BuildAR.Save;

namespace BuildAR.Quiz
{
    /// <summary>
    /// Leitner-style spaced repetition. Each card has a level 0–5:
    ///   right when due  → level +1, next review after 1, 3, 7, 16, 35 days
    ///   wrong           → level −2, due again straight away (it comes back this round, tagged "Forgotten")
    ///   right early     → no change (practicing ahead doesn't skip the wait)
    /// </summary>
    public static class SpacedRepetition
    {
        public const int MaxLevel = 5;
        static readonly double[] IntervalDays = { 0, 1, 3, 7, 16, 35 };
        static readonly float[] LevelMastery = { 0f, 0.4f, 0.7f, 0.85f, 0.95f, 1f };
        static readonly TimeSpan RelearnDelay = TimeSpan.FromMinutes(10);

        public static bool IsNew(SaveData.CardState s) => s == null || s.reviews == 0;

        public static bool IsDue(SaveData.CardState s, DateTime utcNow) => IsNew(s) || s.dueTicks <= utcNow.Ticks;

        public static float Mastery01(SaveData.CardState s) => s == null ? 0f : LevelMastery[Math.Max(0, Math.Min(MaxLevel, s.level))];

        /// <summary>First time a card is asked in a round.</summary>
        public static void Review(SaveData.CardState s, bool correct, DateTime utcNow)
        {
            bool due = IsDue(s, utcNow);
            s.reviews++;
            s.lastReviewTicks = utcNow.Ticks;

            if (!correct)
            {
                s.lapses++;
                s.level = Math.Max(0, s.level - 2);
                s.dueTicks = utcNow.Ticks;
                return;
            }
            if (!due) return;
            s.level = Math.Min(MaxLevel, s.level + 1);
            s.dueTicks = (utcNow + TimeSpan.FromDays(IntervalDays[s.level])).Ticks;
        }

        /// <summary>Answered correctly later in the same round after forgetting it.</summary>
        public static void Relearned(SaveData.CardState s, DateTime utcNow)
        {
            s.lastReviewTicks = utcNow.Ticks;
            s.dueTicks = (utcNow + (s.level == 0 ? RelearnDelay : TimeSpan.FromDays(1))).Ticks;
        }
    }
}
