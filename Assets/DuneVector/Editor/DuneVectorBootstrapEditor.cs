using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuneVector.Editor
{
    [CustomEditor(typeof(DuneVectorBootstrap))]
    public sealed class DuneVectorBootstrapEditor : UnityEditor.Editor
    {
        private SerializedProperty _runtimeSettings;

        private void OnEnable()
        {
            _runtimeSettings = serializedObject.FindProperty("RuntimeSettings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DuneVectorSettingsInspector.DrawBanner(
                "DUNE VECTOR",
                "Runtime bootstrap",
                new Color(0.94f, 0.48f, 0.13f));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_runtimeSettings, new GUIContent(
                "Runtime Settings",
                "The reusable asset that contains all player, gameplay, enemy, and world tuning."));
            serializedObject.ApplyModifiedProperties();

            DuneVectorRuntimeSettings settings = _runtimeSettings.objectReferenceValue as DuneVectorRuntimeSettings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Runtime Settings asset. Without one, play mode uses temporary default values that are not saved.",
                    MessageType.Warning);

                if (GUILayout.Button("Create and Assign Runtime Settings", GUILayout.Height(30f)))
                {
                    CreateAndAssignSettings();
                }
                return;
            }

            EditorGUILayout.Space(5f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Active tuning", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Ground {settings.PlayerTuning.MaxGroundSpeed:0.#}  •  Flight {settings.PlayerTuning.FlightSpeed:0.#}  •  " +
                    $"Dunes {settings.SelectedDunePreset}",
                    EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Open Runtime Settings", GUILayout.Height(32f)))
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }

            EditorGUILayout.HelpBox(
                "All tuning now lives in the settings asset. Changes are reusable and no longer buried on the scene object.",
                MessageType.Info);
        }

        private void CreateAndAssignSettings()
        {
            const string folder = "Assets/DuneVector/ScriptableObjects";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Dune Vector Runtime Settings.asset");
            DuneVectorRuntimeSettings settings = CreateInstance<DuneVectorRuntimeSettings>();
            settings.EnsureInitialized();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();

            serializedObject.Update();
            _runtimeSettings.objectReferenceValue = settings;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            Selection.activeObject = settings;
        }
    }

    [CustomEditor(typeof(DuneVectorRuntimeSettings))]
    public sealed class DuneVectorRuntimeSettingsEditor : UnityEditor.Editor
    {
        private static readonly string[] Tabs = { "Player", "Gameplay", "Enemies", "World" };
        private static readonly string[] PresetDescriptions =
        {
            "Balanced layered dunes used by the original prototype.",
            "Low, broad, smooth dunes suited to relaxed traversal and cinematic shots.",
            "Huge sweeping dune formations with tall crests and long wavelengths.",
            "Tight, angular ridgelines with crisp crests and stronger surface definition.",
            "Heavily warped directional dunes shaped by uneven crosswinds.",
            "Soft, rounded wind-shaped dunes with broad crests and restrained fine detail.",
            "Flowing ribbon-like dune bands with strong warp and smooth rolling surfaces.",
            "Large rounded wind swells with long wavelengths and monumental soft crests.",
            "Broad rolling terrain where secondary landforms dominate the ridge pattern.",
            "Dense small ripples with high mesh detail; visually rich but more expensive.",
            "Tall, chaotic, highly warped terrain intended for dramatic or difficult routes.",
        };

        private int _selectedTab;
        private SerializedProperty _player;
        private SerializedProperty _clouds;
        private SerializedProperty _deliveries;
        private SerializedProperty _pyramids;
        private SerializedProperty _worldStreaming;
        private SerializedProperty _health;
        private SerializedProperty _flyingEnemies;
        private SerializedProperty _groundExploders;
        private SerializedProperty _rings;
        private SerializedProperty _dunes;
        private SerializedProperty _meshResolution;
        private SerializedProperty _chunkSize;

        private void OnEnable()
        {
            _player = serializedObject.FindProperty("PlayerTuning");
            _clouds = serializedObject.FindProperty("Clouds");
            _deliveries = serializedObject.FindProperty("Deliveries");
            _pyramids = serializedObject.FindProperty("Pyramids");
            _worldStreaming = serializedObject.FindProperty("WorldStreaming");
            _health = serializedObject.FindProperty("HealthSettings");
            _flyingEnemies = serializedObject.FindProperty("FlyingEnemies");
            _groundExploders = serializedObject.FindProperty("GroundExploders");
            _rings = serializedObject.FindProperty("Rings");
            _dunes = serializedObject.FindProperty("DuneGeneration");
            _meshResolution = serializedObject.FindProperty("DuneMeshResolution");
            _chunkSize = serializedObject.FindProperty("DuneChunkSize");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DuneVectorSettingsInspector.DrawBanner(
                "RUNTIME SETTINGS",
                "One asset for the complete prototype",
                new Color(0.13f, 0.67f, 0.72f));

            EditorGUILayout.Space(7f);
            EditorGUILayout.HelpBox(
                "This asset is shared by the scene. Edit it outside Play Mode for permanent tuning changes.",
                MessageType.None);

            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs, GUILayout.Height(26f));
            EditorGUILayout.Space(5f);

            switch (_selectedTab)
            {
                case 0:
                    DrawPlayerTab();
                    break;
                case 1:
                    DrawGameplayTab();
                    break;
                case 2:
                    DrawEnemiesTab();
                    break;
                default:
                    DrawWorldTab();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPlayerTab()
        {
            DuneVectorSettingsInspector.DrawSection(
                "Drone Movement & Camera",
                "Ground handling, boosts, flight controls, and camera response.",
                _player);
            DuneVectorSettingsInspector.DrawSection(
                "Player Health",
                "Hull capacity and the grace period between damage events.",
                _health);
        }

        private void DrawGameplayTab()
        {
            DuneVectorSettingsInspector.DrawSection(
                "Pickup & Delivery",
                "Objective placement, travel ranges, package size, and job rings.",
                _deliveries);
            DuneVectorSettingsInspector.DrawSection(
                "Traversal Rings",
                "Sizes, height bands, and active enlargement for both ring types.",
                _rings);
        }

        private void DrawEnemiesTab()
        {
            DuneVectorSettingsInspector.DrawSection(
                "Flying Enemies",
                "Spawning, pursuit, dive attacks, damage, and recovery.",
                _flyingEnemies);
            DuneVectorSettingsInspector.DrawSection(
                "Ground Exploders",
                "Patrol motion, proximity wind-up, radial damage, and presentation.",
                _groundExploders);
        }

        private void DrawWorldTab()
        {
            DrawDunePresetControls();
            DuneVectorSettingsInspector.DrawSection(
                "Dune Generation",
                "Large landforms, directional ridges, secondary forms, and fine detail.",
                _dunes);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("World Streaming", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Terrain chunk dimensions and mesh density.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(3f);
                EditorGUILayout.PropertyField(_chunkSize);
                EditorGUILayout.PropertyField(_meshResolution);
                EditorGUILayout.PropertyField(_worldStreaming, GUIContent.none, true);
            }

            DuneVectorSettingsInspector.DrawSection(
                "Cloud Field",
                "Sky coverage, altitude, extent, and drift speed.",
                _clouds);
            DuneVectorSettingsInspector.DrawSection(
                "Pyramids",
                "Landmark density and randomized size range.",
                _pyramids);
        }

        private void DrawDunePresetControls()
        {
            DuneVectorRuntimeSettings settings = (DuneVectorRuntimeSettings)target;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Dune Presets", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Start with a designed terrain style, then fine-tune it below.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(3f);

                DuneGenerationPreset selected = (DuneGenerationPreset)EditorGUILayout.EnumPopup(
                    "Preset",
                    settings.SelectedDunePreset);
                if (selected != settings.SelectedDunePreset)
                {
                    Undo.RecordObject(settings, "Select Dune Preset");
                    settings.SelectedDunePreset = selected;
                    EditorUtility.SetDirty(settings);
                }

                int descriptionIndex = Mathf.Clamp((int)settings.SelectedDunePreset, 0, PresetDescriptions.Length - 1);
                EditorGUILayout.HelpBox(PresetDescriptions[descriptionIndex], MessageType.Info);

                if (GUILayout.Button("Apply Preset", GUILayout.Height(28f)))
                {
                    Undo.RecordObject(settings, $"Apply {settings.SelectedDunePreset} Preset");
                    settings.ApplyDunePreset(settings.SelectedDunePreset);
                    EditorUtility.SetDirty(settings);
                    serializedObject.Update();
                }

                EditorGUILayout.LabelField(
                    "Applying preserves the world seed and replaces the dune-shape values.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }
    }

    internal static class DuneVectorSettingsInspector
    {
        public static void DrawBanner(string title, string subtitle, Color accent)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 58f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.105f, 0.12f, 0.15f)
                : new Color(0.16f, 0.18f, 0.21f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), accent);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = Color.white },
            };
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.73f, 0.77f, 0.82f) },
            };

            GUI.Label(new Rect(rect.x + 16f, rect.y + 9f, rect.width - 24f, 23f), title, titleStyle);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 32f, rect.width - 24f, 18f), subtitle, subtitleStyle);
        }

        public static void DrawSection(string title, string subtitle, SerializedProperty property)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(subtitle, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(3f);
                EditorGUILayout.PropertyField(property, GUIContent.none, true);
            }
        }
    }
}
