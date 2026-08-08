using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuneVector.Editor
{
    /// <summary>
    /// Installs the Sunspire Exchange hub authored by
    /// ArtSource/Blender/SunspireExchange/build_sunspire_exchange.py.
    ///
    /// The model arrives as a single FBX. This tool configures its import
    /// settings, extracts its materials so they can be textured by hand,
    /// builds the visual prefab, and points the runtime settings asset at it.
    /// Every value it writes is an asset reference or an import setting; all
    /// designer-facing hub tuning stays on Dune Vector Runtime Settings.
    /// </summary>
    public static class SunspireExchangeHubInstaller
    {
        private const string AssetFolder = "Assets/DuneVector/Resources/SunspireExchange";
        private const string ModelPath = AssetFolder + "/SunspireExchange.fbx";
        private const string MaterialFolder = AssetFolder + "/Materials";
        private const string PrefabPath = AssetFolder + "/SunspireExchangeVisual.prefab";
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        /// <summary>
        /// Mesh name prefixes that <see cref="DuneVectorCourierGame"/> turns into
        /// box colliders. Kept in sync with COLLIDER_PREFIXES in the Blender
        /// generator; every other part of the hub is merged into one mesh per
        /// material and is purely visual.
        /// </summary>
        private static readonly string[] StructuralColliderNamePrefixes =
        {
            "Aerie_Pylon_",
            "Pylon_Cap_",
            "Gantry_Leg_",
            "Dock_Backdrop_",
            "Dock_Buttress_",
            "Vent_Stack_",
            "Antenna_Mast_",
            "Deck_Crate_",
            "Dock_Crate_",
            "Mooring_Clamp_",
            "Fuel_Drum_",
            "Windsock_Pole",
        };

        // The Blender generator authors the walkable deck out to this radius.
        private const float SurfaceRadius = 25.35f;

        [MenuItem("Tools/Dune Vector/Install Sunspire Exchange Hub")]
        public static void Install()
        {
            if (!File.Exists(ModelPath))
            {
                EditorUtility.DisplayDialog(
                    "Sunspire Exchange",
                    $"{ModelPath} was not found.\n\nRun " +
                    "ArtSource/Blender/SunspireExchange/build_sunspire_exchange.py in Blender first.",
                    "OK");
                return;
            }

            ConfigureModelImporter();
            GameObject prefab = BuildPrefab();
            if (prefab == null)
            {
                return;
            }

            if (!AssignToRuntimeSettings(prefab))
            {
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log(
                "Sunspire Exchange installed. Dune Vector Runtime Settings > WORLD > World Hub now " +
                $"uses {PrefabPath}. Materials are extracted to {MaterialFolder} and are ready to texture.",
                prefab);
        }

        private static void ConfigureModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            // The generator exports in metres with Unity's axis convention, so the
            // model needs no rescaling and no rig or animation import.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.indexFormat = ModelImporterIndexFormat.Auto;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;

            if (!Directory.Exists(MaterialFolder))
            {
                Directory.CreateDirectory(MaterialFolder);
                AssetDatabase.Refresh();
            }

            importer.SaveAndReimport();

            // Extract materials as real assets so they can be textured and
            // version-controlled instead of living inside the FBX.
            foreach (Object embedded in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (embedded is not Material material)
                {
                    continue;
                }

                string destination = $"{MaterialFolder}/{material.name}.mat";
                if (File.Exists(destination))
                {
                    continue;
                }

                string error = AssetDatabase.ExtractAsset(embedded, destination);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning($"Could not extract {material.name}: {error}");
                }
            }

            AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        }

        private static GameObject BuildPrefab()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"Failed to load {ModelPath}.");
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "SunspireExchangeVisual";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            // The hub is streamed in once and never moves, so let Unity batch it.
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(
                    child.gameObject,
                    StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ReflectionProbeStatic);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        private static bool AssignToRuntimeSettings(GameObject prefab)
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"Failed to load {RuntimeSettingsPath}.");
                return false;
            }

            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty hub = serialized.FindProperty("WorldHub");
            if (hub == null)
            {
                Debug.LogError("Dune Vector Runtime Settings has no WorldHub section.");
                return false;
            }

            hub.FindPropertyRelative("PremiumVisualPrefab").objectReferenceValue = prefab;
            hub.FindPropertyRelative("PremiumVisualSurfaceRadius").floatValue = SurfaceRadius;
            hub.FindPropertyRelative("ReplaceProceduralStructureVisuals").boolValue = true;
            hub.FindPropertyRelative("PremiumVisualMeshCollisionEnabled").boolValue = true;

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            return true;
        }

        [MenuItem("Tools/Dune Vector/Verify Sunspire Exchange Hub")]
        public static void Verify()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"{PrefabPath} is missing. Run Install Sunspire Exchange Hub first.");
                return;
            }

            MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Dictionary<string, int> colliderSources = new Dictionary<string, int>();
            int triangles = 0;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsStarted = false;

            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                triangles += mesh.triangles.Length / 3;
                Bounds worldBounds = new Bounds(
                    meshFilter.transform.TransformPoint(mesh.bounds.center), Vector3.zero);
                worldBounds.Encapsulate(new Bounds(
                    meshFilter.transform.TransformPoint(mesh.bounds.min), Vector3.zero));
                worldBounds.Encapsulate(new Bounds(
                    meshFilter.transform.TransformPoint(mesh.bounds.max), Vector3.zero));
                if (boundsStarted)
                {
                    bounds.Encapsulate(worldBounds);
                }
                else
                {
                    bounds = worldBounds;
                    boundsStarted = true;
                }

                foreach (string prefix in StructuralColliderNamePrefixes)
                {
                    if (meshFilter.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        colliderSources.TryGetValue(prefix, out int count);
                        colliderSources[prefix] = count + 1;
                        break;
                    }
                }
            }

            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine($"Sunspire Exchange: {meshFilters.Length} renderers, {triangles} triangles.");
            report.AppendLine($"Bounds centre {bounds.center}, size {bounds.size}.");
            report.AppendLine("Collider sources per prefix:");
            int total = 0;
            foreach (string prefix in StructuralColliderNamePrefixes)
            {
                colliderSources.TryGetValue(prefix, out int count);
                total += count;
                report.AppendLine($"  {prefix} -> {count}");
            }

            report.AppendLine($"Total box colliders that will be generated: {total}.");
            Debug.Log(report.ToString(), prefab);
        }
    }
}
