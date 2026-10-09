using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BuildAR.Managers
{
    /// <summary>
    /// One-shot effects plus the looping background track. Clips left empty in the Inspector are loaded from
    /// Assets/_Project/Resources/Audio/, so dropping a different file in there is enough to change a sound.
    /// Music is quiet by design and can be switched off in the profile panel.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public const string ClickResource = "Audio/buttonsfx";
        public const string ConfettiResource = "Audio/confetti";
        public const string CoinResource = "Audio/coin";
        public const string MusicResource = "Audio/bgmusic";

        const float MusicFadeSeconds = 1.5f;

        public static AudioManager Instance { get; private set; }

        [SerializeField] private AudioSource source;
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip correctClip;
        [SerializeField] private AudioClip incorrectClip;
        [SerializeField] private AudioClip warningClip;
        [SerializeField] private AudioClip badgeUnlockClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip confettiClip;
        [SerializeField] private AudioClip coinClip;
        [SerializeField] private AudioClip musicClip;

        [Header("Volumes")]
        [Tooltip("The tap sound plays on every button, so it sits under the other effects.")]
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] private float confettiVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float coinVolume = 0.55f;
        [Tooltip("Background music plays under everything else, so keep it low.")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.12f;

        private bool _clickTried, _confettiTried, _coinTried, _musicTried;
        private AudioListener _listener;
        private Coroutine _fade;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.volume = 0f;

            // The App and Boot scenes have no camera with an AudioListener, so nothing would be heard at all.
            // This one travels with the manager; every sound here is 2D, so its position doesn't matter.
            _listener = gameObject.AddComponent<AudioListener>();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }

        private void Start()
        {
            ClaimListener();
            RefreshMusic();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ClaimListener();

        /// <summary>Keeps exactly one listener: Unity warns (and picks arbitrarily) when a loaded scene brings its own.</summary>
        private void ClaimListener()
        {
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l != _listener) l.enabled = false;
            if (_listener != null) _listener.enabled = true;
        }

        // ---------------------------------------------------------------- effects

        public void PlayCorrect() => PlayClip(correctClip, 1f);
        public void PlayIncorrect() => PlayClip(incorrectClip, 1f);
        public void PlayWarning() => PlayClip(warningClip, 1f);
        public void PlayBadgeUnlock() => PlayClip(badgeUnlockClip, 1f);

        /// <summary>Played for every button press (see UIUtil.PrepareScreen).</summary>
        public void PlayClick() => PlayClip(Load(ref clickClip, ref _clickTried, ClickResource), clickVolume);

        /// <summary>Played with the confetti burst (see Confetti.Burst).</summary>
        public void PlayConfetti() => PlayClip(Load(ref confettiClip, ref _confettiTried, ConfettiResource), confettiVolume);

        /// <summary>Played when coins are spent in the shop.</summary>
        public void PlayCoin() => PlayClip(Load(ref coinClip, ref _coinTried, CoinResource), coinVolume);

        private void PlayClip(AudioClip clip, float volume)
        {
            if (clip != null && source != null) source.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ music

        /// <summary>Whether the learner wants background music. Stored with their progress.</summary>
        public bool MusicOn => ProgressManager.Instance == null || ProgressManager.Instance.Data.musicOn;

        public void SetMusic(bool on)
        {
            ProgressManager.Instance?.SetMusicOn(on);
            RefreshMusic();
        }

        /// <summary>Fades the loop in or out to match the setting. Safe to call at any time.</summary>
        public void RefreshMusic()
        {
            if (musicSource == null) return;
            if (!MusicOn)
            {
                if (musicSource.isPlaying) Fade(0f, stopAtEnd: true);
                return;
            }
            if (musicSource.clip == null) musicSource.clip = Load(ref musicClip, ref _musicTried, MusicResource);
            if (musicSource.clip == null) return;
            if (!musicSource.isPlaying) { musicSource.volume = 0f; musicSource.Play(); }
            Fade(musicVolume);
        }

        private void Fade(float target, bool stopAtEnd = false)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(target, stopAtEnd));
        }

        private IEnumerator FadeRoutine(float target, bool stopAtEnd)
        {
            float from = musicSource.volume;
            for (float t = 0f; t < MusicFadeSeconds; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = Mathf.Lerp(from, target, t / MusicFadeSeconds);
                yield return null;
            }
            musicSource.volume = target;
            if (stopAtEnd) musicSource.Stop();
            _fade = null;
        }

        private AudioClip Load(ref AudioClip clip, ref bool tried, string resource)
        {
            if (clip == null && !tried)
            {
                tried = true;
                clip = Resources.Load<AudioClip>(resource);
                if (clip == null) Debug.LogWarning($"BuildAR: no audio at Assets/_Project/Resources/{resource}.");
            }
            return clip;
        }
    }
}
