using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BuildAR.UI
{
    /// <summary>
    /// A UIDocument without Panel Settings draws nothing (blank screen). Scene saves in this Unity version can drop
    /// that reference, so every loaded scene gets Resources/BuildAR_Mobile_PanelSettings assigned where it's missing.
    /// </summary>
    public static class PanelSettingsFallback
    {
        public const string ResourceName = "BuildAR_Mobile_PanelSettings";
        static PanelSettings _settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int fixedCount = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var doc in root.GetComponentsInChildren<UIDocument>(true))
                {
                    if (doc.panelSettings != null) continue;
                    if (_settings == null) _settings = Resources.Load<PanelSettings>(ResourceName);
                    if (_settings == null) { Debug.LogError($"BuildAR: Resources/{ResourceName} is missing, so UI can't be drawn."); return; }
                    doc.panelSettings = _settings;
                    fixedCount++;
                }
            if (fixedCount > 0) Debug.Log($"BuildAR: assigned Panel Settings to {fixedCount} UI Document(s) in {scene.name}.");
        }
    }
}
