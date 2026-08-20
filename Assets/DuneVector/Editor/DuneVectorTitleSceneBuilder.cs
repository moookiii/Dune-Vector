using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace DuneVector.Editor
{
    /// <summary>
    /// Rebuilds the startup title scene and keeps its background video imported without
    /// transcoding, so the authored clip plays back exactly as it was encoded.
    /// </summary>
    public static class DuneVectorTitleSceneBuilder
    {
        public const string ScenePath = "Assets/DuneVector/Scenes/DuneVectorTitle.unity";
        public const string VideoPath = "Assets/DuneVector/Video/DuneVectorTitleBackground.mp4";
        public const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        [MenuItem("Dune Vector/Rebuild Title Screen Scene")]
        public static void BuildScene()
        {
            DuneVectorRuntimeSettings settings = ConfigureBackgroundVideo();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DuneVectorTitle";

            GameObject cameraObject = new GameObject("Title Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.useOcclusionCulling = false;
            camera.allowMSAA = false;

            GameObject root = new GameObject("DUNE VECTOR - Title Screen");
            DuneVectorTitleScreen titleScreen = root.AddComponent<DuneVectorTitleScreen>();
            titleScreen.RuntimeSettings = settings;
            titleScreen.SceneCamera = camera;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/DuneVector/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log($"DUNE_VECTOR_TITLE_SCENE_READY: {ScenePath}");
        }

        /// <summary>
        /// Puts the title scene first so a build boots into it, keeping the gameplay scene loadable
        /// by name from the START entry.
        /// </summary>
        public static void RegisterBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(DuneVectorSceneBuilder.ScenePath, true),
            };
        }

        /// <summary>
        /// Disables transcoding on the background clip and repairs the runtime settings reference to
        /// it, which is the pairing the title screen needs to play the video untouched.
        /// </summary>
        public static DuneVectorRuntimeSettings ConfigureBackgroundVideo()
        {
            if (AssetImporter.GetAtPath(VideoPath) is VideoClipImporter importer)
            {
                VideoImporterTargetSettings target = importer.defaultTargetSettings;
                if (target.enableTranscoding)
                {
                    target.enableTranscoding = false;
                    importer.defaultTargetSettings = target;
                    importer.SaveAndReimport();
                }
            }
            else
            {
                Debug.LogWarning($"No title background video was found at {VideoPath}.");
            }

            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"The Dune Vector runtime settings asset is missing at {RuntimeSettingsPath}.");
                return null;
            }

            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
            if (clip != null && settings.TitleScreen.BackgroundVideo != clip)
            {
                settings.TitleScreen.BackgroundVideo = clip;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            return settings;
        }

        public static void BuildSceneCommandLine()
        {
            BuildScene();
        }
    }
}
