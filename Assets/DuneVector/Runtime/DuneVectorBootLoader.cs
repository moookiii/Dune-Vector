using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuneVector
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorBootLoader : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // Let the loading scene render once so it replaces Unity's splash before
            // the gameplay scene begins its asynchronous load.
            yield return null;

            int gameplaySceneIndex = gameObject.scene.buildIndex + 1;
            if (gameplaySceneIndex < 0 || gameplaySceneIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError(
                    "Dune Vector Boot Loading must be immediately before the gameplay scene in Build Profiles.",
                    this);
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(gameplaySceneIndex, LoadSceneMode.Single);
        }
    }
}
