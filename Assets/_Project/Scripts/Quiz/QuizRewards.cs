using UnityEngine;

namespace BuildAR.Quiz
{
    /// <summary>Numbers behind Memorize: hearts, XP, levels, streak tiers and shop prices.</summary>
    public static class QuizRewards
    {
        public const int MaxHearts = 15;
        public const float HeartRefillMinutes = 10f;
        public const int StarterHints = 3;

        public const int BaseXp = 10;
        public const float LightningSeconds = 10f;
        public const float SpeedRunSeconds = 120f;

        /// <summary>Questions per day needed to keep the streak (the "Basic" tier).</summary>
        public const int StreakDailyMinimum = 2;

        /// <summary>Up to +10 XP for answering within about 2 s, fading to 0 at 15 s.</summary>
        public static int SpeedBonus(float seconds) => Mathf.RoundToInt(10f * Mathf.Clamp01(1f - (seconds - 2f) / 13f));

        /// <summary>Up to +20 XP on a lightning question, based on time left on its 10 s timer.</summary>
        public static int LightningBonus(float secondsLeft) => Mathf.RoundToInt(20f * Mathf.Clamp01(secondsLeft / LightningSeconds));

        public static int Multiplier(int combo) => combo >= 8 ? 3 : combo >= 4 ? 2 : 1;

        public static int XpFor(float seconds, float lightningSecondsLeft, bool firstAttempt, int multiplier)
        {
            int xp = BaseXp + SpeedBonus(seconds) + (lightningSecondsLeft > 0f ? LightningBonus(lightningSecondsLeft) : 0);
            if (!firstAttempt) xp = Mathf.Max(5, xp / 2);
            return xp * multiplier;
        }

        public static int XpToNextLevel(int level) => 100 * level;

        public static void Level(int totalXp, out int level, out int xpIntoLevel, out int xpNeeded)
        {
            level = 1;
            xpIntoLevel = Mathf.Max(0, totalXp);
            while (xpIntoLevel >= XpToNextLevel(level)) { xpIntoLevel -= XpToNextLevel(level); level++; }
            xpNeeded = XpToNextLevel(level);
        }

        public struct StreakTier
        {
            public string name;
            public int questions;
            public StreakTier(string name, int questions) { this.name = name; this.questions = questions; }
        }

        public static readonly StreakTier[] StreakTiers =
        {
            new StreakTier("Basic", 2), new StreakTier("Gold", 10), new StreakTier("Sapphire", 20),
            new StreakTier("Ruby", 30), new StreakTier("Emerald", 40), new StreakTier("Amethyst", 50),
            new StreakTier("Pearl", 75), new StreakTier("Diamond", 125), new StreakTier("Onyx", 250),
        };

        /// <summary>Index into StreakTiers reached with this many questions today, or -1.</summary>
        public static int TierIndex(int questionsToday)
        {
            int index = -1;
            for (int i = 0; i < StreakTiers.Length; i++) if (questionsToday >= StreakTiers[i].questions) index = i;
            return index;
        }

        public enum ShopKind { Hint, SuperHeart, StreakFreeze }

        public struct ShopItem
        {
            public ShopKind kind;
            public string title, description, icon;
            public int price;
            public ShopItem(ShopKind kind, string title, string description, string icon, int price)
            { this.kind = kind; this.title = title; this.description = description; this.icon = icon; this.price = price; }
        }

        public static readonly ShopItem[] Shop =
        {
            new ShopItem(ShopKind.Hint, "Hint", "Removes a wrong option or shows the first letter.", "bulb", 5),
            new ShopItem(ShopKind.SuperHeart, "Super Heart", "Kicks in automatically when your red hearts run out.", "heart", 15),
            new ShopItem(ShopKind.StreakFreeze, "Streak Freeze", "Protects your streak on a day you miss.", "snowflake", 25),
        };
    }
}
