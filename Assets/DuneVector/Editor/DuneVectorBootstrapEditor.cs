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
        private SerializedProperty _droneVisuals;
        private SerializedProperty _flightSwooshes;
        private SerializedProperty _windFields;
        private SerializedProperty _clouds;
        private SerializedProperty _weather;
        private SerializedProperty _environmentalHazards;
        private SerializedProperty _deliveries;
        private SerializedProperty _contracts;
        private SerializedProperty _deliveryMessages;
        private SerializedProperty _worldHub;
        private SerializedProperty _landmarks;
        private SerializedProperty _geoglyphs;
        private SerializedProperty _routeEncounters;
        private SerializedProperty _dynamicCouriers;
        private SerializedProperty _pyramids;
        private SerializedProperty _cacti;
        private SerializedProperty _worldStreaming;
        private SerializedProperty _rendererFrustumCulling;
        private SerializedProperty _health;
        private SerializedProperty _energyLauncher;
        private SerializedProperty _flyingEnemies;
        private SerializedProperty _stormPyramids;
        private SerializedProperty _playerStrikeOrbs;
        private SerializedProperty _groundExploders;
        private SerializedProperty _rings;
        private SerializedProperty _permanentUpgrades;
        private SerializedProperty _dunes;
        private SerializedProperty _duneTexture;
        private SerializedProperty _duneTextureTileSize;
        private SerializedProperty _meshResolution;
        private SerializedProperty _chunkSize;

        private void OnEnable()
        {
            _player = serializedObject.FindProperty("PlayerTuning");
            _droneVisuals = serializedObject.FindProperty("DroneVisuals");
            _flightSwooshes = serializedObject.FindProperty("FlightSwooshes");
            _windFields = serializedObject.FindProperty("WindFields");
            _clouds = serializedObject.FindProperty("Clouds");
            _weather = serializedObject.FindProperty("Weather");
            _environmentalHazards = serializedObject.FindProperty("EnvironmentalHazards");
            _deliveries = serializedObject.FindProperty("Deliveries");
            _contracts = serializedObject.FindProperty("Contracts");
            _deliveryMessages = serializedObject.FindProperty("DeliveryMessages");
            _worldHub = serializedObject.FindProperty("WorldHub");
            _landmarks = serializedObject.FindProperty("Landmarks");
            _geoglyphs = serializedObject.FindProperty("Geoglyphs");
            _routeEncounters = serializedObject.FindProperty("RouteEncounters");
            _dynamicCouriers = serializedObject.FindProperty("DynamicCouriers");
            _pyramids = serializedObject.FindProperty("Pyramids");
            _cacti = serializedObject.FindProperty("Cacti");
            _worldStreaming = serializedObject.FindProperty("WorldStreaming");
            _rendererFrustumCulling = serializedObject.FindProperty("RendererFrustumCulling");
            _health = serializedObject.FindProperty("HealthSettings");
            _energyLauncher = serializedObject.FindProperty("EnergyLauncher");
            _flyingEnemies = serializedObject.FindProperty("FlyingEnemies");
            _stormPyramids = serializedObject.FindProperty("StormPyramids");
            _playerStrikeOrbs = serializedObject.FindProperty("PlayerStrikeOrbs");
            _groundExploders = serializedObject.FindProperty("GroundExploders");
            _rings = serializedObject.FindProperty("Rings");
            _permanentUpgrades = serializedObject.FindProperty("PermanentUpgrades");
            _dunes = serializedObject.FindProperty("DuneGeneration");
            _duneTexture = serializedObject.FindProperty("DuneTexture");
            _duneTextureTileSize = serializedObject.FindProperty("DuneTextureTileSize");
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
                "Drone Visuals",
                "Hull, wing inlays, rotor geometry, materials, animation, and trails.",
                _droneVisuals);
            DuneVectorSettingsInspector.DrawSection(
                "Flight Speed Swooshes",
                "Local camera-edge motion streak density, speed response, shape, spawn area, and fade.",
                _flightSwooshes);
            DuneVectorSettingsInspector.DrawSection(
                "Player Health",
                "Hull capacity and the grace period between damage events.",
                _health);
            DuneVectorSettingsInspector.DrawSection(
                "Lock-On Energy Launcher",
                "View-centered target acquisition, homing energy shots, feedback, and targeting HUD.",
                _energyLauncher);
        }

        private void DrawGameplayTab()
        {
            DuneVectorSettingsInspector.DrawSection(
                "Pickup & Delivery",
                "Objective placement, travel ranges, package size, and job rings.",
                _deliveries);
            DuneVectorSettingsInspector.DrawSection(
                "Courier Contracts",
                "Contract offers, modifier progression, cargo consequences, rewards, and active-contract HUD.",
                _contracts);
            DuneVectorSettingsInspector.DrawSection(
                "Delivery Messages",
                "Authored narrative order, typewriter timing, replay policy, and FMOD typing loop.",
                _deliveryMessages);
            DuneVectorSettingsInspector.DrawSection(
                "Dynamic Couriers & Convoys",
                "Ambient rescue events, open-route races, moving convoy attacks, rewards, and faction colors.",
                _dynamicCouriers);
            DuneVectorSettingsInspector.DrawSection(
                "World Hub & Teleport",
                "Safe-hub layout, terminal presentation, deployment, and return sequence.",
                _worldHub);
            DuneVectorSettingsInspector.DrawSection(
                "Traversal Rings",
                "Sizes, height bands, and active enlargement for both ring types.",
                _rings);
            DuneVectorSettingsInspector.DrawSection(
                "Permanent Upgrade Shop",
                "Upgrade Tier progression curves, gold costs, and pause-shop presentation.",
                _permanentUpgrades);
        }

        private void DrawEnemiesTab()
        {
            DuneVectorSettingsInspector.DrawSection(
                "Flying Enemies",
                "Spawning, pursuit, dive attacks, damage, and recovery.",
                _flyingEnemies);
            DuneVectorSettingsInspector.DrawSection(
                "Storm Pyramids",
                "High-altitude patrol and telegraphed straight-down ground lightning.",
                _stormPyramids);
            DuneVectorSettingsInspector.DrawSection(
                "Strike Orbs",
                "Air-only player detection, live intercept prediction, orbiting satellites, and targeted lightning.",
                _playerStrikeOrbs);
            DuneVectorSettingsInspector.DrawSection(
                "Ground Exploders",
                "Patrol motion, proximity wind-up, radial damage, and presentation.",
                _groundExploders);
            DuneVectorSettingsInspector.DrawSection(
                "Route Encounter Formations",
                "Encounter volumes, five approach formations, attack passes, break-off behavior, and rewards.",
                _routeEncounters);
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
                EditorGUILayout.LabelField("Dune Surface", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Terrain PNG and the world-space size of each repeated tile.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(3f);
                EditorGUILayout.PropertyField(_duneTexture);
                EditorGUILayout.PropertyField(_duneTextureTileSize);
            }

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
                "Renderer Frustum Culling",
                "Suppress renderers beyond a padded camera frustum and discover newly spawned renderers.",
                _rendererFrustumCulling);

            DuneVectorSettingsInspector.DrawSection(
                "Dynamic Desert Weather",
                "Storm frequency and progression, global wind, HDRP visibility, and recycled dust layers.",
                _weather);
            DuneVectorSettingsInspector.DrawSection(
                "Environmental Hazards",
                "Electrical sandstorm strikes, interference, deterministic heat zones, drone temperature, cooling, and gameplay consequences.",
                _environmentalHazards);
            DuneVectorSettingsInspector.DrawSection(
                "Wind Fields",
                "World-space wind regions, gameplay forces, falloff, streamlines, surface sand, and distance LOD.",
                _windFields);
            DuneVectorSettingsInspector.DrawSection(
                "Cloud Field",
                "Sky coverage, altitude, extent, and drift speed.",
                _clouds);
            DuneVectorSettingsInspector.DrawSection(
                "Pyramids",
                "Landmark density and randomized size range.",
                _pyramids);
            DuneVectorSettingsInspector.DrawSection(
                "Cacti",
                "Saguaro density, proportions, ribbing, arm silhouettes, color, and blossoms.",
                _cacti);
            DuneVectorSettingsInspector.DrawSection(
                "Authored Landmarks",
                "Placement tiers, spacing, silhouettes, sockets, and all ten landmark templates.",
                _landmarks);
            DuneVectorSettingsInspector.DrawSection(
                "World Geoglyph Artwork",
                "Unique mask landmarks projected once in persistent logical world coordinates across streamed dunes.",
                _geoglyphs);
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
