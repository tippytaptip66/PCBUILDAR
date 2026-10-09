using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using BuildAR.Data;
using BuildAR.Quiz;
using BuildAR.UI;

namespace BuildAR.Managers
{
    public enum AppScreen { Welcome, Home, Learn, ComponentDetail, Progress, Quiz }

    /// <summary>
    /// App entry point and navigation. Lives in the persistent Boot scene.
    ///   BuildAR_App             all 2D screens (switched by ScreenRouter)
    ///   BuildAR_ARScanner       AR camera + detector
    ///   BuildAR_VirtualAssembly 3D case practice
    /// Exactly one of those is loaded additively at a time.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Scene names (must match Build Settings)")]
        public string appScene = "BuildAR_App";
        public string arScannerScene = "BuildAR_ARScanner";
        public string virtualAssemblyScene = "BuildAR_VirtualAssembly";

        /// <summary>Screen ScreenRouter should show when the App scene (re)loads.</summary>
        public AppScreen PendingScreen { get; private set; } = AppScreen.Home;
        public AppScreen CurrentScreen { get; private set; } = AppScreen.Home;

        // Hand-off state between screens.
        public ComponentDefinitionSO SelectedComponent { get; private set; }
        public string DetailInitialTab { get; private set; }
        public LessonSO ActiveLesson { get; private set; }
        public QuizDeck PendingDeck { get; private set; }
        public LessonSO PendingLessonQuiz { get; private set; }

        private string _activeAdditiveScene;
        private readonly Stack<AppScreen> _history = new Stack<AppScreen>();
        private Coroutine _switch;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            LoadingOverlay.Create(transform);
        }

        private void Start()
        {
            // Entering Play mode from a content scene (see DevBootstrap): adopt it instead of loading the App.
            foreach (var name in new[] { appScene, arScannerScene, virtualAssemblyScene })
                if (SceneManager.GetSceneByName(name).isLoaded) { _activeAdditiveScene = name; return; }

            bool onboarded = ProgressManager.Instance != null && ProgressManager.Instance.Data.onboardingComplete;
            ShowScreen(onboarded ? AppScreen.Home : AppScreen.Welcome, addToHistory: false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // UI test shortcuts (Game view must have focus, and not while typing an answer):
        // 1 Welcome · 2 Home · 3 Learn · 4 Component Detail · 5 Progress · 6 Quiz · 7 AR Scanner · 8 Virtual Assembly
        // F1 refill hearts · F2 +50 coins · F3 skip a day (cards come due)
        private void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            var progress = ProgressManager.Instance;
            if (kb.f1Key.wasPressedThisFrame) progress?.DebugRefillHearts();
            else if (kb.f2Key.wasPressedThisFrame) progress?.DebugAddCoins(50);
            else if (kb.f3Key.wasPressedThisFrame) progress?.DebugSkipDay();
            if (CurrentScreen == AppScreen.Quiz && _activeAdditiveScene == appScene) return;

            if (kb.digit1Key.wasPressedThisFrame) ShowScreen(AppScreen.Welcome);
            else if (kb.digit2Key.wasPressedThisFrame) ShowScreen(AppScreen.Home);
            else if (kb.digit3Key.wasPressedThisFrame) ShowScreen(AppScreen.Learn);
            else if (kb.digit4Key.wasPressedThisFrame) OpenComponentDetail(SelectedComponent ?? FirstComponent());
            else if (kb.digit5Key.wasPressedThisFrame) ShowScreen(AppScreen.Progress);
            else if (kb.digit6Key.wasPressedThisFrame)
            {
                var c = SelectedComponent ?? FirstComponent();
                if (c != null) StartQuiz(QuizDeck.ForComponent(c));
            }
            else if (kb.digit7Key.wasPressedThisFrame) GoToARScanner();
            else if (kb.digit8Key.wasPressedThisFrame) GoToVirtualAssembly();
        }

        private static ComponentDefinitionSO FirstComponent() =>
            ComponentDatabase.Instance != null && ComponentDatabase.Instance.All.Count > 0 ? ComponentDatabase.Instance.All[0] : null;
#endif

        // ---------- 2D screens ----------

        public void ShowScreen(AppScreen screen) => ShowScreen(screen, addToHistory: true);

        void ShowScreen(AppScreen screen, bool addToHistory)
        {
            bool inApp = _activeAdditiveScene == appScene;
            bool sameScreen = inApp && screen == CurrentScreen;
            if (addToHistory && inApp && !sameScreen) _history.Push(CurrentScreen);
            if (screen == AppScreen.Home) _history.Clear();

            PendingScreen = screen;
            CurrentScreen = screen;

            if (!inApp) LoadAdditive(appScene); // ScreenRouter reads PendingScreen on Start
            else if (sameScreen) ScreenRouter.Instance?.Show(screen);
            else
            {
                if (_switch != null) StopCoroutine(_switch);
                _switch = StartCoroutine(SwitchWithLoading(screen));
            }
        }

        /// <summary>Swaps screens behind the loading animation so the change isn't a hard cut.</summary>
        IEnumerator SwitchWithLoading(AppScreen screen)
        {
            LoadingOverlay.Instance?.Show();
            yield return new WaitForSecondsRealtime(0.15f);
            ScreenRouter.Instance?.Show(screen);
            yield return null;
            LoadingOverlay.Instance?.Hide();
            _switch = null;
        }

        public void Back()
        {
            ShowScreen(_history.Count > 0 ? _history.Pop() : AppScreen.Home, addToHistory: false);
        }

        public void GoToMainMenu() => ShowScreen(AppScreen.Home);

        /// <summary>After a reset: forget where the learner has been and start again at the Welcome slides.</summary>
        public void RestartFromWelcome()
        {
            _history.Clear();
            SelectedComponent = null;
            DetailInitialTab = null;
            ActiveLesson = null;
            PendingDeck = null;
            PendingLessonQuiz = null;
            ShowScreen(AppScreen.Welcome, addToHistory: false);
        }

        public void OpenComponentDetail(ComponentDefinitionSO def, string initialTab = null, LessonSO fromLesson = null)
        {
            if (def == null) return;
            SelectedComponent = def;
            DetailInitialTab = initialTab;
            ActiveLesson = fromLesson;
            ShowScreen(AppScreen.ComponentDetail);
        }

        /// <summary>A Memorize round (practice) on a deck.</summary>
        public void StartQuiz(QuizDeck deck)
        {
            PendingDeck = deck;
            PendingLessonQuiz = null;
            ShowScreen(AppScreen.Quiz);
        }

        /// <summary>A lesson's 20-question quiz (the test that completes it). Back returns to the lesson's Quiz tab.</summary>
        public void StartLessonQuiz(LessonSO lesson)
        {
            if (lesson == null) return;
            PendingLessonQuiz = lesson;
            PendingDeck = null;
            DetailInitialTab = "quiz";
            ShowScreen(AppScreen.Quiz);
        }

        // ---------- 3D / AR scenes ----------

        public void GoToARScanner() => LoadAdditive(arScannerScene);
        public void GoToVirtualAssembly() => LoadAdditive(virtualAssemblyScene);

        private void LoadAdditive(string sceneName)
        {
            if (_activeAdditiveScene == sceneName) return;
            if (_switch != null) { StopCoroutine(_switch); _switch = null; }
            var loading = LoadingOverlay.Instance;
            loading?.Show(sceneName == arScannerScene ? "Starting AR scanner…" : sceneName == virtualAssemblyScene ? "Preparing your build…" : "Loading…");

            if (!string.IsNullOrEmpty(_activeAdditiveScene) && SceneManager.GetSceneByName(_activeAdditiveScene).isLoaded)
                SceneManager.UnloadSceneAsync(_activeAdditiveScene);

            _activeAdditiveScene = sceneName;
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                loading?.Hide();
                Debug.LogError($"BuildAR: scene '{sceneName}' is not in Build Settings. Run BuildAR > Setup > Build Scenes.");
                return;
            }
            op.completed += _ =>
            {
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));
                loading?.Hide();
            };
        }
    }
}
