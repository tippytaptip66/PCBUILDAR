using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BuildAR.Managers
{
    /// <summary>
    /// Lives in the App scene. Each 2D screen is a GameObject with a UIDocument + controller;
    /// only the active screen's GameObject is enabled.
    /// </summary>
    public class ScreenRouter : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public AppScreen screen;
            public GameObject root;
        }

        public static ScreenRouter Instance { get; private set; }

        [SerializeField] private List<Entry> screens = new List<Entry>();

        private void Awake()
        {
            Instance = this;
            foreach (var e in screens) if (e.root != null) e.root.SetActive(false);
        }

        private IEnumerator Start()
        {
            // When Play is pressed in this scene, DevBootstrap loads the managers one frame later.
            float timeout = Time.realtimeSinceStartup + 2f;
            while (GameManager.Instance == null && Time.realtimeSinceStartup < timeout) yield return null;
            Show(GameManager.Instance != null ? GameManager.Instance.PendingScreen : AppScreen.Home);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Show(AppScreen screen)
        {
            // Disable first so the outgoing controller's OnDisable runs before the new OnEnable.
            foreach (var e in screens) if (e.root != null && e.screen != screen) e.root.SetActive(false);
            foreach (var e in screens) if (e.root != null && e.screen == screen) e.root.SetActive(true);
        }
    }
}
