using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorCloudField : MonoBehaviour
    {
        private sealed class ArchetypeMeshes
        {
            public int Index;
            public CloudArchetypeTuning Tuning;
            public Mesh Sunlit;
            public Mesh Underbelly;
        }

        private sealed class CloudMeshLibrary
        {
            public readonly List<ArchetypeMeshes> Archetypes = new List<ArchetypeMeshes>();

            public void DestroyMeshes()
            {
                for (int i = 0; i < Archetypes.Count; i++)
                {
                    DestroyRuntimeMesh(Archetypes[i].Sunlit);
                    DestroyRuntimeMesh(Archetypes[i].Underbelly);
                }
                Archetypes.Clear();
            }
        }

        private static readonly Dictionary<CloudTuning, CloudMeshLibrary> MeshLibraries =
            new Dictionary<CloudTuning, CloudMeshLibrary>();

        private float _driftSpeed;
        private Vector2 _driftDirection;
        private float _weatherWindSpeedMultiplier;
        private float _weatherWindDirectionBlend;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMeshLibraries()
        {
            foreach (CloudMeshLibrary library in MeshLibraries.Values)
            {
                library.DestroyMeshes();
            }
            MeshLibraries.Clear();
        }

        public void Initialize(
            Material sunlitMaterial,
            Material underbellyMaterial,
            int clusterCount,
            float chunkSize,
            CloudTuning tuning,
            CloudArrangementTuning arrangement,
            int randomSeed)
        {
            tuning.EnsureInitialized();
            _driftSpeed = Mathf.Max(0f, tuning.DriftSpeed);
            _driftDirection = tuning.DriftDirection.sqrMagnitude > 0.0001f
                ? tuning.DriftDirection.normalized
                : Vector2.zero;
            _weatherWindSpeedMultiplier = Mathf.Max(0f, tuning.WeatherWindSpeedMultiplier);
            _weatherWindDirectionBlend = Mathf.Clamp01(tuning.WeatherWindDirectionBlend);

            CloudMeshLibrary library = GetOrCreateMeshLibrary(tuning);
            if (library.Archetypes.Count == 0)
            {
                Debug.LogWarning("Cloud generation requires at least one authored archetype with lobes.", this);
                return;
            }

            System.Random random = new System.Random(randomSeed);
            List<Vector2> occupiedPositions = new List<Vector2>(clusterCount);
            float inset = Mathf.Clamp(tuning.PlacementInset, 0f, chunkSize * 0.45f);
            for (int clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
            {
                ArchetypeMeshes archetype = SelectArchetype(library.Archetypes, arrangement, random);
                Vector2 planarPosition = FindPlacement(
                    random,
                    occupiedPositions,
                    inset,
                    chunkSize,
                    tuning.MinimumLocalSeparation,
                    tuning.PlacementAttempts);
                occupiedPositions.Add(planarPosition);

                CloudArchetypeTuning shape = archetype.Tuning;
                GameObject clusterObject = new GameObject(
                    $"Cloud {shape.DisplayName} {clusterIndex + 1:00}");
                Transform cluster = clusterObject.transform;
                cluster.SetParent(transform, false);
                cluster.localPosition = new Vector3(
                    planarPosition.x,
                    tuning.Altitude + arrangement.AltitudeOffset + OrderedRange(random, shape.AltitudeOffsetRange),
                    planarPosition.y);
                cluster.localRotation = Quaternion.Euler(
                    Range(random, -shape.PitchRollVariation, shape.PitchRollVariation),
                    OrderedRange(random, shape.YawRange),
                    Range(random, -shape.PitchRollVariation, shape.PitchRollVariation));
                Vector3 clusterScale = RandomScale(random, shape.MinimumScale, shape.MaximumScale);
                float horizontalAverage = (clusterScale.x + clusterScale.z) * 0.5f;
                float clusterRoundness = Mathf.Clamp01(tuning.ClusterHorizontalRoundness);
                clusterScale.x = Mathf.Lerp(clusterScale.x, horizontalAverage, clusterRoundness);
                clusterScale.z = Mathf.Lerp(clusterScale.z, horizontalAverage, clusterRoundness);
                cluster.localScale = Vector3.Scale(
                    clusterScale,
                    PositiveScale(arrangement.ScaleMultiplier));

                List<Renderer> renderers = new List<Renderer>(2);
                AddLayer("Cool Underside", cluster, archetype.Underbelly, underbellyMaterial, renderers);
                AddLayer("Warm Sunlit Mass", cluster, archetype.Sunlit, sunlitMaterial, renderers);
                AddCullingGroup(clusterObject, renderers, tuning.CullScreenRelativeHeight);
            }
        }

        internal void Tick(float deltaTime)
        {
            Vector2 driftDirection = _driftDirection;
            float driftSpeed = _driftSpeed;
            DuneVectorWeatherController weather = DuneVectorBootstrap.Instance?.WeatherSystem;
            if (weather != null)
            {
                Vector3 weatherDirection3 = weather.CurrentWindDirection;
                Vector2 weatherDirection = new Vector2(weatherDirection3.x, weatherDirection3.z);
                if (weatherDirection.sqrMagnitude > 0.0001f)
                {
                    driftDirection = Vector2.Lerp(
                        _driftDirection,
                        weatherDirection.normalized,
                        _weatherWindDirectionBlend).normalized;
                }

                driftSpeed += weather.CurrentWindSpeed * _weatherWindSpeedMultiplier;
            }

            Vector3 drift = new Vector3(driftDirection.x, 0f, driftDirection.y) * (driftSpeed * deltaTime);
            transform.localPosition += drift;
        }

        private static CloudMeshLibrary GetOrCreateMeshLibrary(CloudTuning tuning)
        {
            if (MeshLibraries.TryGetValue(tuning, out CloudMeshLibrary cached))
            {
                return cached;
            }

            CloudMeshLibrary library = new CloudMeshLibrary();
            Mesh facetedSphere = CreateFacetedIcosphere(Mathf.Clamp(tuning.FacetSubdivisions, 0, 2));
            CloudArchetypeTuning[] definitions = tuning.GetArchetypes();
            for (int i = 0; i < definitions.Length; i++)
            {
                CloudArchetypeTuning definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                Mesh sunlit = BuildLayerMesh(
                    definition.DisplayName,
                    "Sunlit",
                    facetedSphere,
                    definition.SunlitLobes,
                    tuning.LobeHorizontalRoundness,
                    tuning.LobeDepthSpread);
                Mesh underbelly = BuildLayerMesh(
                    definition.DisplayName,
                    "Underbelly",
                    facetedSphere,
                    definition.UnderbellyLobes,
                    tuning.LobeHorizontalRoundness,
                    tuning.LobeDepthSpread);
                if (sunlit == null && underbelly == null)
                {
                    continue;
                }

                library.Archetypes.Add(new ArchetypeMeshes
                {
                    Index = i,
                    Tuning = definition,
                    Sunlit = sunlit,
                    Underbelly = underbelly,
                });
            }

            DestroyRuntimeMesh(facetedSphere);
            MeshLibraries.Add(tuning, library);
            return library;
        }

        private static Mesh BuildLayerMesh(
            string archetypeName,
            string layerName,
            Mesh sourceMesh,
            CloudLobeTuning[] lobes,
            float horizontalRoundness,
            float depthSpread)
        {
            if (lobes == null || lobes.Length == 0)
            {
                return null;
            }

            float maximumLateralOffset = 0f;
            for (int i = 0; i < lobes.Length; i++)
            {
                maximumLateralOffset = Mathf.Max(maximumLateralOffset, Mathf.Abs(lobes[i].Position.x));
            }
            float roundedAmount = Mathf.Clamp01(horizontalRoundness);
            float depthAmount = Mathf.Clamp(depthSpread, 0f, 0.75f);
            CombineInstance[] instances = new CombineInstance[lobes.Length];
            for (int i = 0; i < lobes.Length; i++)
            {
                CloudLobeTuning lobe = lobes[i];
                Vector3 lobeScale = PositiveScale(lobe.Scale);
                float broadestHorizontalAxis = Mathf.Max(lobeScale.x, lobeScale.z);
                lobeScale.x = Mathf.Lerp(lobeScale.x, broadestHorizontalAxis, roundedAmount);
                lobeScale.z = Mathf.Lerp(lobeScale.z, broadestHorizontalAxis, roundedAmount);
                Vector3 lobePosition = lobe.Position;
                float normalizedLateralPosition = maximumLateralOffset > 0.001f
                    ? Mathf.Clamp(lobePosition.x / maximumLateralOffset, -1f, 1f)
                    : 0f;
                float coherentDepthCurve = Mathf.Sin(normalizedLateralPosition * Mathf.PI * 0.5f);
                lobePosition.z += coherentDepthCurve * maximumLateralOffset * depthAmount;
                instances[i] = new CombineInstance
                {
                    mesh = sourceMesh,
                    transform = Matrix4x4.TRS(
                        lobePosition,
                        Quaternion.Euler(lobe.Rotation),
                        lobeScale),
                };
            }

            Mesh mesh = new Mesh
            {
                name = $"Cloud Kit - {archetypeName} - {layerName}",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave,
            };
            mesh.CombineMeshes(instances, true, true, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Mesh CreateFacetedIcosphere(int subdivisions)
        {
            float goldenRatio = (1f + Mathf.Sqrt(5f)) * 0.5f;
            List<Vector3> vertices = new List<Vector3>
            {
                new Vector3(-1f, goldenRatio, 0f), new Vector3(1f, goldenRatio, 0f),
                new Vector3(-1f, -goldenRatio, 0f), new Vector3(1f, -goldenRatio, 0f),
                new Vector3(0f, -1f, goldenRatio), new Vector3(0f, 1f, goldenRatio),
                new Vector3(0f, -1f, -goldenRatio), new Vector3(0f, 1f, -goldenRatio),
                new Vector3(goldenRatio, 0f, -1f), new Vector3(goldenRatio, 0f, 1f),
                new Vector3(-goldenRatio, 0f, -1f), new Vector3(-goldenRatio, 0f, 1f),
            };
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = vertices[i].normalized;
            }

            List<int> triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int subdivision = 0; subdivision < subdivisions; subdivision++)
            {
                Dictionary<long, int> midpointCache = new Dictionary<long, int>();
                List<int> refined = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    int ab = GetMidpoint(a, b, vertices, midpointCache);
                    int bc = GetMidpoint(b, c, vertices, midpointCache);
                    int ca = GetMidpoint(c, a, vertices, midpointCache);
                    refined.AddRange(new[]
                    {
                        a, ab, ca,
                        b, bc, ab,
                        c, ca, bc,
                        ab, bc, ca,
                    });
                }
                triangles = refined;
            }

            Vector3[] flatVertices = new Vector3[triangles.Count];
            Vector3[] flatNormals = new Vector3[triangles.Count];
            int[] flatTriangles = new int[triangles.Count];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]] * 0.5f;
                Vector3 b = vertices[triangles[i + 1]] * 0.5f;
                Vector3 c = vertices[triangles[i + 2]] * 0.5f;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                flatVertices[i] = a;
                flatVertices[i + 1] = b;
                flatVertices[i + 2] = c;
                flatNormals[i] = normal;
                flatNormals[i + 1] = normal;
                flatNormals[i + 2] = normal;
                flatTriangles[i] = i;
                flatTriangles[i + 1] = i + 1;
                flatTriangles[i + 2] = i + 2;
            }

            Mesh mesh = new Mesh
            {
                name = $"Cloud Faceted Icosphere {subdivisions}",
                hideFlags = HideFlags.DontSave,
            };
            mesh.vertices = flatVertices;
            mesh.normals = flatNormals;
            mesh.triangles = flatTriangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int GetMidpoint(
            int first,
            int second,
            List<Vector3> vertices,
            Dictionary<long, int> cache)
        {
            int minimum = Mathf.Min(first, second);
            int maximum = Mathf.Max(first, second);
            long key = ((long)minimum << 32) | (uint)maximum;
            if (cache.TryGetValue(key, out int midpoint))
            {
                return midpoint;
            }

            midpoint = vertices.Count;
            vertices.Add(((vertices[first] + vertices[second]) * 0.5f).normalized);
            cache.Add(key, midpoint);
            return midpoint;
        }

        private static void AddLayer(
            string name,
            Transform parent,
            Mesh mesh,
            Material material,
            List<Renderer> renderers)
        {
            if (mesh == null || material == null)
            {
                return;
            }

            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            MeshFilter filter = layer.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderers.Add(renderer);
        }

        private static void AddCullingGroup(GameObject cloud, List<Renderer> renderers, float cullHeight)
        {
            if (renderers.Count == 0 || cullHeight <= 0f)
            {
                return;
            }

            LODGroup lodGroup = cloud.AddComponent<LODGroup>();
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.SetLODs(new[]
            {
                new LOD(Mathf.Clamp(cullHeight, 0.0001f, 0.1f), renderers.ToArray()),
            });
            lodGroup.RecalculateBounds();
        }

        private static ArchetypeMeshes SelectArchetype(
            List<ArchetypeMeshes> archetypes,
            CloudArrangementTuning arrangement,
            System.Random random)
        {
            float totalWeight = 0f;
            for (int i = 0; i < archetypes.Count; i++)
            {
                totalWeight += Mathf.Max(0f, arrangement.GetArchetypeWeight(archetypes[i].Index));
            }

            if (totalWeight <= 0f)
            {
                return archetypes[0];
            }

            float selection = Range(random, 0f, totalWeight);
            for (int i = 0; i < archetypes.Count; i++)
            {
                selection -= Mathf.Max(0f, arrangement.GetArchetypeWeight(archetypes[i].Index));
                if (selection <= 0f)
                {
                    return archetypes[i];
                }
            }
            return archetypes[archetypes.Count - 1];
        }

        private static Vector2 FindPlacement(
            System.Random random,
            List<Vector2> occupiedPositions,
            float inset,
            float chunkSize,
            float minimumSeparation,
            int placementAttempts)
        {
            Vector2 bestCandidate = Vector2.zero;
            float bestDistanceSquared = -1f;
            float minimumDistanceSquared = minimumSeparation * minimumSeparation;
            int attempts = Mathf.Max(1, placementAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector2 candidate = new Vector2(
                    Range(random, inset, chunkSize - inset),
                    Range(random, inset, chunkSize - inset));
                float nearestDistanceSquared = float.MaxValue;
                for (int i = 0; i < occupiedPositions.Count; i++)
                {
                    nearestDistanceSquared = Mathf.Min(
                        nearestDistanceSquared,
                        (candidate - occupiedPositions[i]).sqrMagnitude);
                }

                if (occupiedPositions.Count == 0 || nearestDistanceSquared >= minimumDistanceSquared)
                {
                    return candidate;
                }
                if (nearestDistanceSquared > bestDistanceSquared)
                {
                    bestDistanceSquared = nearestDistanceSquared;
                    bestCandidate = candidate;
                }
            }
            return bestCandidate;
        }

        private static Vector3 RandomScale(System.Random random, Vector3 minimum, Vector3 maximum)
        {
            return new Vector3(
                Mathf.Max(0.01f, Range(random, Mathf.Min(minimum.x, maximum.x), Mathf.Max(minimum.x, maximum.x))),
                Mathf.Max(0.01f, Range(random, Mathf.Min(minimum.y, maximum.y), Mathf.Max(minimum.y, maximum.y))),
                Mathf.Max(0.01f, Range(random, Mathf.Min(minimum.z, maximum.z), Mathf.Max(minimum.z, maximum.z))));
        }

        private static Vector3 PositiveScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Max(0.01f, scale.x),
                Mathf.Max(0.01f, scale.y),
                Mathf.Max(0.01f, scale.z));
        }

        private static float OrderedRange(System.Random random, Vector2 range)
        {
            return Range(random, Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }

        private static float Range(System.Random random, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private static void DestroyRuntimeMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
