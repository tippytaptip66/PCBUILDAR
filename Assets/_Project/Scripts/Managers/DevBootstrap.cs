using UnityEngine;
using UnityEngine.SceneManagement;

namespace BuildAR.Managers
{
    /// <summary>
    /// Lets you press Play in any content scene: if the Boot scene (managers) isn't loaded yet, load it additively.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class DevBootstrap : MonoBehaviour
    {
        [SerializeField] private string bootScene = "BuildAR_Boot";

        private void Awake()
        {
            if (GameManager.Instance == null && !SceneManager.GetSceneByName(bootScene).isLoaded)
                SceneManager.LoadScene(bootScene, LoadSceneMode.Additive);
        }
    }
}
