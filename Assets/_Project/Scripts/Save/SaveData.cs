using System;
using System.Collections.Generic;
using BuildAR.Quiz;

namespace BuildAR.Save
{
    [Serializable]
    public class SaveData
    {
        /// <summary>
        /// When the app this save belongs to was installed (Android's firstInstallTime, ms). Uninstalling deletes
        /// the save, but Android's automatic backup can bring it back on a fresh install; a save whose stamp doesn't
        /// match the installed app is one of those, and is dropped so a fresh install starts fresh.
        /// </summary>
        public long installTime;

        public bool onboardingComplete;
        /// <summary>
        /// Sprout's guided tour of Home is still to be shown. Set only when onboarding finishes for the first time,
        /// so learners with saves from before the tour existed aren't walked through an app they already know.
        /// </summary>
        public bool guideTourPending;
        public List<string> completedLessonIds = new List<string>();
        public List<string> inProgressLessonIds = new List<string>();
        public List<string> unlockedBadgeIds = new List<string>();
        public List<string> masteredComponentIds = new List<string>();
        public List<string> scannedComponentIds = new List<string>();
        public List<string> completedAssemblyStepIds = new List<string>();
        public List<string> compatibilityCheckedIds = new List<string>();
        public int safetyTipsViewed;

        // ---------- appearance + sound ----------
        /// <summary>Dark mode (see Theme.cs). Off = the light theme.</summary>
        public bool darkMode;
        /// <summary>Background music (see AudioManager). Saves from before this setting existed default to on.</summary>
        public bool musicOn = true;
        /// <summary>The learner turned off the "Before you scan" card the AR scanner opens with.</summary>
        public bool scanTipsHidden;

        // ---------- profile (first-launch setup) ----------
        public bool profileSet;
        public string profileName;
        public string profileGender;
        public string profileAvatar;

        // ---------- lesson requirements ----------
        /// <summary>"lessonId:tab" for each lesson tab the learner has opened.</summary>
        public List<string> lessonTabsViewed = new List<string>();
        /// <summary>"lessonId:percent", the best lesson-quiz score for each lesson taken (75 or more passes it).</summary>
        public List<string> lessonQuizScores = new List<string>();

        // ---------- Memorize (quiz) ----------
        public List<CardState> cards = new List<CardState>();
        public QuizSettings quizSettings = new QuizSettings();

        public int xp;
        public int weekXp;
        public string weekStart;
        public int coins;

        public int hearts = QuizRewards.MaxHearts;
        public long heartsRefillTicks;
        public bool heartsLockedOut;
        public int superHearts;
        public int hints = QuizRewards.StarterHints;

        public int streak;
        public string streakLastDay;
        public int streakFreezes;
        public bool starterFreezeGiven;
        public List<DayLog> dayLog = new List<DayLog>();

        /// <summary>Spaced-repetition state of one card.</summary>
        [Serializable]
        public class CardState
        {
            public string key;
            public int level;
            public long dueTicks;
            public long lastReviewTicks;
            public int reviews;
            public int lapses;
        }

        /// <summary>Questions answered on one local calendar day (yyyy-MM-dd).</summary>
        [Serializable]
        public class DayLog
        {
            public string date;
            public int questions;
            public bool frozen;
        }

        [Serializable]
        public class QuizSettings
        {
            public QuizStyle style = QuizStyle.Mixed;
            public bool hideOptions;
            public bool lightningRounds = true;
        }
    }
}
