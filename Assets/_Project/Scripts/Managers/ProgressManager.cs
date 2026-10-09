using System;
using System.Linq;
using UnityEngine;
using BuildAR.Data;
using BuildAR.Quiz;
using BuildAR.Save;

namespace BuildAR.Managers
{
    public partial class ProgressManager : MonoBehaviour
    {
        public const int MasteryScore = 70;
        const int ChecksForCompatibilityBadge = 3;

        public static ProgressManager Instance { get; private set; }
        public event Action OnProgressChanged;
        public event Action<BadgeCatalog.Badge> OnBadgeUnlocked;
        public SaveData Data { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Data = SaveSystem.Load();
            CheckFreshInstall();
            // Saves from before hearts existed load with 0 hearts and no refill timer.
            if (Data.hearts <= 0 && Data.heartsRefillTicks == 0) { Data.hearts = QuizRewards.MaxHearts; Data.heartsLockedOut = false; }
        }

        /// <summary>
        /// A fresh install starts fresh: welcome slides, profile, tour, scanning tips, no progress. Updating the app
        /// keeps everything (the install time doesn't change on an update), but a save Android restored from a
        /// backup of an earlier install carries that install's time, so it's recognized and replaced.
        /// </summary>
        void CheckFreshInstall()
        {
            long installed = AppInstallTime();
            if (installed <= 0) return;   // the Editor, or the time couldn't be read
            if (Data.installTime != 0 && Data.installTime != installed)
            {
                Debug.Log("BuildAR: this is a fresh install; the restored save from an earlier install was reset.");
                Data = new SaveData();
            }
            if (Data.installTime == installed) return;
            Data.installTime = installed;   // a new save, or one from before installs were stamped
            SaveSystem.Save(Data);
        }

        /// <summary>When this app was installed on the phone (ms since 1970), or 0 where that isn't known.</summary>
        static long AppInstallTime()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packages = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = packages.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0))
                    return info.Get<long>("firstInstallTime");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BuildAR: couldn't read the install time. {e.Message}");
                return 0;
            }
#else
            return 0;
#endif
        }

        public void MarkOnboardingComplete()
        {
            if (!Data.onboardingComplete) Data.guideTourPending = true; // first launch: Home opens with the tour
            Data.onboardingComplete = true;
            Persist();
        }

        /// <summary>
        /// Back to a fresh install: progress, quiz cards, rewards, badges, profile and settings all return to their
        /// defaults and the save file is overwritten. Pictures in the photos folder are the learner's own and stay.
        /// Callers repaint the theme and music (see ProfileSetup) and send the app back to Welcome.
        /// </summary>
        public void ResetAll()
        {
            Data = new SaveData { installTime = Data.installTime };   // still the same install
            Persist();
        }

        /// <summary>The tour was finished or skipped; it won't start by itself again.</summary>
        public void CompleteGuideTour()
        {
            if (!Data.guideTourPending) return;
            Data.guideTourPending = false;
            Persist();
        }

        public void MarkComponentMastered(string id) { if (AddUnique(Data.masteredComponentIds, id)) Persist(); }

        public void RecordScan(string componentId)
        {
            if (!AddUnique(Data.scannedComponentIds, componentId)) return;
            UnlockBadge(BadgeCatalog.FirstScan, persist: false);
            Persist();
        }

        public void MarkLessonInProgress(string id)
        {
            if (Data.completedLessonIds.Contains(id) || !AddUnique(Data.inProgressLessonIds, id)) return;
            Persist();
        }

        /// <summary>Tabs a lesson's component page must be opened on before the lesson can be completed.</summary>
        public static readonly string[] LessonTabs = { "overview", "parts", "compatibility", "installation" };

        public void RecordLessonTab(string lessonId, string tab)
        {
            if (string.IsNullOrEmpty(lessonId) || Array.IndexOf(LessonTabs, tab) < 0) return;
            if (AddUnique(Data.lessonTabsViewed, lessonId + ":" + tab)) Persist();
        }

        public bool LessonTabViewed(string lessonId, string tab) => Data.lessonTabsViewed.Contains(lessonId + ":" + tab);

        // ---------- lesson quizzes ----------

        /// <summary>Best lesson-quiz score in percent, or -1 if the quiz hasn't been finished yet.</summary>
        public int LessonQuizBest(string lessonId)
        {
            string prefix = lessonId + ":";
            var entry = Data.lessonQuizScores.Find(s => s.StartsWith(prefix, StringComparison.Ordinal));
            return entry != null && int.TryParse(entry.Substring(prefix.Length), out int percent) ? percent : -1;
        }

        public bool LessonQuizPassed(string lessonId) => LessonQuizBest(lessonId) >= LessonQuizSession.PassPercent;

        /// <summary>Keeps the better of this score and the best so far.</summary>
        public void RecordLessonQuiz(string lessonId, int percent)
        {
            if (string.IsNullOrEmpty(lessonId) || percent <= LessonQuizBest(lessonId)) return;
            Data.lessonQuizScores.RemoveAll(s => s.StartsWith(lessonId + ":", StringComparison.Ordinal));
            Data.lessonQuizScores.Add($"{lessonId}:{percent}");
            Persist();
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Data.profileName) ? "" : Data.profileName.Trim();

        /// <summary>Dark mode on/off. Go through BuildAR.UI.Theme so open screens repaint too.</summary>
        public void SetDarkMode(bool on)
        {
            if (Data.darkMode == on) return;
            Data.darkMode = on;
            Persist();
        }

        /// <summary>Whether the AR scanner opens with its "Before you scan" card.</summary>
        public void SetScanTipsHidden(bool hidden)
        {
            if (Data.scanTipsHidden == hidden) return;
            Data.scanTipsHidden = hidden;
            Persist();
        }

        /// <summary>Background music on/off. Go through AudioManager.SetMusic so it fades in or out.</summary>
        public void SetMusicOn(bool on)
        {
            if (Data.musicOn == on) return;
            Data.musicOn = on;
            Persist();
        }

        public void SaveProfile(string name, string gender, string avatarId)
        {
            name = (name ?? "").Trim();
            Data.profileName = name.Length > ProfileNameMaxLength ? name.Substring(0, ProfileNameMaxLength) : name;
            Data.profileGender = gender;
            Data.profileAvatar = avatarId;
            Data.profileSet = true;
            Persist();
        }

        public const int ProfileNameMaxLength = 20;

        public void MarkLessonComplete(string id)
        {
            Data.inProgressLessonIds.Remove(id);
            AddUnique(Data.completedLessonIds, id);
            Persist();
        }

        public void MarkAssemblyStepComplete(string id) { if (AddUnique(Data.completedAssemblyStepIds, id)) Persist(); }

        public void ResetAssemblySteps(AssemblyGuideSO guide)
        {
            foreach (var s in guide.steps) Data.completedAssemblyStepIds.Remove(s.stepId);
            Persist();
        }

        public void RecordCompatibilityCheck(string componentId)
        {
            if (!AddUnique(Data.compatibilityCheckedIds, componentId)) return;
            if (Data.compatibilityCheckedIds.Count >= ChecksForCompatibilityBadge)
                UnlockBadge(BadgeCatalog.CompatibilityChecker, persist: false);
            Persist();
        }

        public void RecordSafetyTipViewed()
        {
            Data.safetyTipsViewed++;
            UnlockBadge(BadgeCatalog.SafetyFirst, persist: false);
            Persist();
        }

        public void UnlockBadge(string id) => UnlockBadge(id, persist: true);

        void UnlockBadge(string id, bool persist)
        {
            if (string.IsNullOrEmpty(id) || !AddUnique(Data.unlockedBadgeIds, id)) return;
            AudioManager.Instance?.PlayBadgeUnlock();
            var badge = BadgeCatalog.All.FirstOrDefault(b => b.id == id);
            if (badge.id != null) OnBadgeUnlocked?.Invoke(badge);
            if (persist) Persist();
        }

        // ---------- derived numbers used by Home / Progress ----------

        public float OverallCompletion01()
        {
            var db = ComponentDatabase.Instance;
            if (db == null) return 0f;
            var guide = db.DefaultGuide;
            int total = db.All.Count + db.Lessons.Count + (guide != null ? guide.steps.Count : 0);
            if (total == 0) return 0f;
            int done = db.All.Count(c => Data.masteredComponentIds.Contains(c.id))
                     + db.Lessons.Count(l => Data.completedLessonIds.Contains(l.lessonId))
                     + (guide != null ? guide.steps.Count(s => Data.completedAssemblyStepIds.Contains(s.stepId)) : 0);
            return (float)done / total;
        }

        /// <summary>Mastered components + completed lessons in a category, 0..1.</summary>
        public float CategoryCompletion01(ComponentCategory category)
        {
            var db = ComponentDatabase.Instance;
            if (db == null) return 0f;
            var comps = db.GetByCategory(category).ToList();
            var lessons = db.Lessons.Where(l => l.relatedCategory == category).ToList();
            int total = comps.Count + lessons.Count;
            if (total == 0) return 0f;
            int done = comps.Count(c => Data.masteredComponentIds.Contains(c.id))
                     + lessons.Count(l => Data.completedLessonIds.Contains(l.lessonId));
            return (float)done / total;
        }

        static bool AddUnique(System.Collections.Generic.List<string> list, string id)
        {
            if (string.IsNullOrEmpty(id) || list.Contains(id)) return false;
            list.Add(id);
            return true;
        }

        private void Persist() { SaveSystem.Save(Data); OnProgressChanged?.Invoke(); }
    }
}
