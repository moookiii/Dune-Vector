using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DuneVector.Editor
{
    public static class DuneVectorUrpMigrationEditor
    {
        private const string AssetFolder = "Assets/DuneVector/ScriptableObjects";
        private const string RendererPath = AssetFolder + "/Dune Vector URP Renderer.asset";
        private const string PipelinePath = AssetFolder + "/Dune Vector URP Pipeline.asset";
        private const string VolumePath = AssetFolder + "/Dune Vector URP Volume Profile.asset";
        private const string RuntimeSettingsPath = AssetFolder + "/Dune Vector Runtime Settings.asset";
        private const string MainScenePath = "Assets/DuneVector/Scenes/DuneVector.unity";
        private const string WebGlBuildPath = "Builds/WebGL";

        [MenuItem("Tools/Dune Vector/Migrate Entire Project to URP")]
        public static void MigrateEntireProject()
        {
            EnsureFolder();
            UniversalRenderPipelineAsset pipeline = EnsurePipeline();
            VolumeProfile profile = EnsureVolumeProfile();
            int materialCount = MigrateMaterials();
            int sceneCount = MigrateScenes(profile);
            AssignPipelineEverywhere();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Dune Vector URP migration complete: {materialCount} materials and {sceneCount} scenes updated.");
        }

        public static void RunBatchMigration()
        {
            MigrateEntireProject();
        }

        [MenuItem("Tools/Dune Vector/Open Main Scene")]
        public static void OpenMainScene()
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("Exit Play Mode before opening the Dune Vector scene.");
            }
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/Dune Vector/Apply Pre-Runtime Fog Settings")]
        public static void ApplyPreRuntimeFogSettings()
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("Exit Play Mode before applying Dune Vector fog settings.");
            }

            DuneVectorRuntimeSettings runtimeSettings = LoadRuntimeSettings();
            string originalScenePath = SceneManager.GetActiveScene().path;
            try
            {
                Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                ApplyPreRuntimeFog(runtimeSettings.Weather.Atmosphere, runtimeSettings.Weather.Cycle);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalScenePath) && originalScenePath != MainScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }
        }

        [MenuItem("Tools/Dune Vector/Build WebGL")]
        public static void BuildWebGl()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                scenes = new[] { MainScenePath };
            }

            Directory.CreateDirectory(WebGlBuildPath);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = WebGlBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Dune Vector WebGL build failed with {report.summary.totalErrors} errors.");
            }
            Debug.Log($"Dune Vector WebGL build complete: {report.summary.totalSize} bytes at {WebGlBuildPath}.");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
            {
                throw new InvalidOperationException($"Required ScriptableObject folder is missing: {AssetFolder}");
            }
        }

        private static UniversalRenderPipelineAsset EnsurePipeline()
        {
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "Dune Vector URP Renderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            if (ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath))
            {
                EditorUtility.SetDirty(renderer);
            }

            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Dune Vector URP Pipeline";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            return pipeline;
        }

        private static VolumeProfile EnsureVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Dune Vector URP Volume Profile";
                profile.Add<Bloom>(true);
                profile.Add<Vignette>(true);
                profile.Add<FilmGrain>(true);
                AssetDatabase.CreateAsset(profile, VolumePath);
            }

            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            }

            colorAdjustments.active = true;
            EditorUtility.SetDirty(colorAdjustments);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void AssignPipelineEverywhere()
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                throw new InvalidOperationException($"URP pipeline asset was not found at {PipelinePath}");
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int originalQuality = QualitySettings.GetQualityLevel();
            for (int quality = 0; quality < QualitySettings.names.Length; quality++)
            {
                QualitySettings.SetQualityLevel(quality, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalQuality, false);
            EditorUtility.SetDirty(pipeline);
        }

        private static int MigrateMaterials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                throw new InvalidOperationException("The URP Lit shader is unavailable.");
            }

            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || !IsHdrpMaterial(material, path))
                {
                    continue;
                }

                Color baseColor = ReadColor(material, "_BaseColor", "_Color", Color.white);
                Color emission = ReadColor(material, "_EmissiveColor", "_EmissionColor", Color.black);
                if (Mathf.Approximately(ReadFloat(material, "_EmissiveIntensityUnit", -1f), 0f))
                {
                    // HDRP stores emissive luminance in nits; URP Lit expects scene-linear color.
                    emission *= 0.01f;
                }
                Texture baseMap = ReadTexture(material, "_BaseColorMap", "_BaseMap", "_MainTex");
                Texture normalMap = ReadTexture(material, "_NormalMap", "_BumpMap");
                Texture emissionMap = ReadTexture(material, "_EmissiveColorMap", "_EmissionMap");
                float metallic = ReadFloat(material, "_Metallic", 0f);
                float smoothness = ReadFloat(material, "_Smoothness", ReadFloat(material, "_SmoothnessRemapMax", 0.5f));
                bool transparent = ReadFloat(material, "_SurfaceType", 0f) > 0.5f;

                material.shader = lit;
                material.SetColor("_BaseColor", baseColor);
                material.SetTexture("_BaseMap", baseMap);
                material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
                material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
                if (normalMap != null)
                {
                    material.SetTexture("_BumpMap", normalMap);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (emission.maxColorComponent > 0f || emissionMap != null)
                {
                    material.SetColor("_EmissionColor", emission);
                    material.SetTexture("_EmissionMap", emissionMap);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (transparent)
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                EditorUtility.SetDirty(material);
                count++;
            }
            return count;
        }

        private static bool IsHdrpMaterial(Material material, string path)
        {
            string shaderName = material.shader == null ? string.Empty : material.shader.name;
            if (shaderName.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static Color ReadColor(Material material, string first, string second, Color fallback)
        {
            return TryReadSavedProperty(material, "m_Colors", first, out SerializedProperty value) ||
                   TryReadSavedProperty(material, "m_Colors", second, out value)
                ? value.colorValue
                : fallback;
        }

        private static float ReadFloat(Material material, string propertyName, float fallback)
        {
            return TryReadSavedProperty(material, "m_Floats", propertyName, out SerializedProperty value)
                ? value.floatValue
                : fallback;
        }

        private static Texture ReadTexture(Material material, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (TryReadSavedProperty(material, "m_TexEnvs", propertyName, out SerializedProperty value))
                {
                    return value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                }
            }
            return null;
        }

        private static bool TryReadSavedProperty(Material material, string collectionName, string propertyName, out SerializedProperty value)
        {
            SerializedProperty collection = new SerializedObject(material).FindProperty("m_SavedProperties." + collectionName);
            if (collection != null)
            {
                for (int i = 0; i < collection.arraySize; i++)
                {
                    SerializedProperty pair = collection.GetArrayElementAtIndex(i);
                    if (pair.FindPropertyRelative("first").stringValue == propertyName)
                    {
                        value = pair.FindPropertyRelative("second");
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        private static int MigrateScenes(VolumeProfile profile)
        {
            string originalScenePath = SceneManager.GetActiveScene().path;
            DuneVectorRuntimeSettings runtimeSettings = LoadRuntimeSettings();
            int count = 0;
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    bool changed = false;
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (Volume volume in root.GetComponentsInChildren<Volume>(true))
                        {
                            if (volume.sharedProfile != null && HasHdrpComponents(volume.sharedProfile))
                            {
                                volume.sharedProfile = profile;
                                EditorUtility.SetDirty(volume);
                                changed = true;
                            }
                        }
                    }
                    if (path == MainScenePath)
                    {
                        ApplyPreRuntimeFog(runtimeSettings.Weather.Atmosphere, runtimeSettings.Weather.Cycle);
                        changed = true;
                    }
                    if (changed)
                    {
                        EditorSceneManager.SaveScene(scene);
                        count++;
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalScenePath))
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }
            return count;
        }

        private static DuneVectorRuntimeSettings LoadRuntimeSettings()
        {
            DuneVectorRuntimeSettings runtimeSettings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            if (runtimeSettings == null)
            {
                throw new InvalidOperationException($"Runtime settings were not found at {RuntimeSettingsPath}");
            }
            return runtimeSettings;
        }

        private static void ApplyPreRuntimeFog(
            DesertWeatherAtmosphereTuning atmosphere,
            DesertWeatherCycleTuning cycle)
        {
            bool startsWithStorm = cycle.StartWithFullSandstorm;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = startsWithStorm ? atmosphere.StormFogColor : atmosphere.ClearFogColor;
            RenderSettings.fogStartDistance = startsWithStorm
                ? atmosphere.StormFogStartDistance
                : atmosphere.ClearFogStartDistance;
            RenderSettings.fogEndDistance = startsWithStorm
                ? atmosphere.StormMaximumFogDistance
                : atmosphere.ClearMaximumFogDistance;
        }

        private static bool HasHdrpComponents(VolumeProfile profile)
        {
            foreach (VolumeComponent component in profile.components)
            {
                if (component != null && component.GetType().AssemblyQualifiedName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
