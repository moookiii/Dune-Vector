using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuneVector.Editor
{
    public static class DuneVectorSceneBuilder
    {
        public const string ScenePath = "Assets/DuneVector/Scenes/DuneVector.unity";

        [MenuItem("Dune Vector/Rebuild Playable Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DuneVector";

            GameObject root = new GameObject("DUNE VECTOR - Runtime Prototype");
            root.AddComponent<DuneVectorBootstrap>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/DuneVector/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            DuneVectorTitleSceneBuilder.RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log($"DUNE_VECTOR_SCENE_READY: {ScenePath}");
        }

        public static void BuildSceneCommandLine()
        {
            BuildScene();
        }

        public static void RunValidationCommandLine()
        {
            BuildScene();
            EditorPrefs.SetBool("DuneVector.ValidationRequested", true);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
    }
}
