using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    internal sealed class DesertShrubField : IDisposable
    {
        private sealed class PatchVariant
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        private sealed class VariantBatch
        {
            public Mesh Mesh;
            public int SubmeshIndex;
            public Material Material;
            public Matrix4x4[] LocalMatrices;
            public Matrix4x4[] WorldMatrices;
            public Bounds WorldBounds;
        }

        private readonly Transform _root;
        private readonly DesertShrubTuning _settings;
        private readonly float _chunkSize;
        private readonly List<VariantBatch> _batches = new List<VariantBatch>();

        public int InstanceCount { get; private set; }

        public DesertShrubField(
            Vector2Int coordinate,
            Transform root,
            float chunkSize,
            DuneHeightField heightField,
            int worldSeed,
            DesertShrubTuning settings,
            LandmarkSystemTuning landmarkSettings,
            IReadOnlyList<Vector2> gameplayExclusions,
            IReadOnlyList<Vector2> sceneryExclusions)
        {
            _root = root;
            _settings = settings;
            _chunkSize = chunkSize;
            if (settings == null || !settings.Enabled || settings.DensityPerChunk <= 0f)
            {
                return;
            }

            List<PatchVariant> variants = LoadPatchVariants(settings.PatchResourcePath);
            if (variants.Count == 0)
            {
                Debug.LogWarning($"No desert shrub patch prefabs were found at Resources/{settings.PatchResourcePath}.");
                return;
            }

            List<Matrix4x4>[] matricesByVariant = new List<Matrix4x4>[variants.Count];
            for (int i = 0; i < matricesByVariant.Length; i++)
            {
                matricesByVariant[i] = new List<Matrix4x4>();
            }

            GeneratePlacements(
                coordinate,
                chunkSize,
                heightField,
                worldSeed,
                settings,
                landmarkSettings,
                gameplayExclusions,
                sceneryExclusions,
                matricesByVariant);

            for (int i = 0; i < variants.Count; i++)
            {
                PatchVariant variant = variants[i];
                if (matricesByVariant[i].Count == 0)
                {
                    continue;
                }

                List<Matrix4x4> variantMatrices = matricesByVariant[i];
                int maximumInstancesPerDraw = DuneVectorSpatialInstancing.MaximumInstancesPerDraw;
                for (int submesh = 0; submesh < variant.Mesh.subMeshCount; submesh++)
                {
                    Material material = variant.Materials[Mathf.Min(submesh, variant.Materials.Length - 1)];
                    if (material == null)
                    {
                        continue;
                    }
                    material.enableInstancing = true;
                    for (int start = 0; start < variantMatrices.Count; start += maximumInstancesPerDraw)
                    {
                        int count = Mathf.Min(maximumInstancesPerDraw, variantMatrices.Count - start);
                        Matrix4x4[] localMatrices = new Matrix4x4[count];
                        variantMatrices.CopyTo(start, localMatrices, 0, count);
                        _batches.Add(new VariantBatch
                        {
                            Mesh = variant.Mesh,
                            SubmeshIndex = submesh,
                            Material = material,
                            LocalMatrices = localMatrices,
                            WorldMatrices = new Matrix4x4[count],
                        });
                    }
                }
                InstanceCount += variantMatrices.Count;
            }
            RebuildWorldMatrices();
        }

        public void RebuildWorldMatrices()
        {
            if (_root == null)
            {
                return;
            }
            Matrix4x4 rootMatrix = _root.localToWorldMatrix;
            for (int batchIndex = 0; batchIndex < _batches.Count; batchIndex++)
            {
                VariantBatch batch = _batches[batchIndex];
                bool hasBounds = false;
                for (int i = 0; i < batch.LocalMatrices.Length; i++)
                {
                    batch.WorldMatrices[i] = rootMatrix * batch.LocalMatrices[i];
                    Bounds meshBounds = DuneVectorSpatialInstancing.TransformBounds(
                        batch.WorldMatrices[i],
                        batch.Mesh.bounds);
                    if (hasBounds)
                    {
                        batch.WorldBounds.Encapsulate(meshBounds);
                    }
                    else
                    {
                        batch.WorldBounds = meshBounds;
                        hasBounds = true;
                    }
                }
            }
        }

        public void Draw(Camera viewCamera)
        {
            if (viewCamera == null || _root == null || _batches.Count == 0)
            {
                return;
            }

            Vector3 center = _root.position + new Vector3(_chunkSize * 0.5f, 0f, _chunkSize * 0.5f);
            float distance = Vector3.Distance(viewCamera.transform.position, center);
            float chunkAllowance = _chunkSize * 0.72f;
            if (distance > Mathf.Max(1f, _settings.CullDistance) + chunkAllowance)
            {
                return;
            }

            ShadowCastingMode shadows = _settings.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int i = 0; i < _batches.Count; i++)
            {
                VariantBatch batch = _batches[i];
                Graphics.DrawMeshInstanced(
                    batch.Mesh,
                    batch.SubmeshIndex,
                    batch.Material,
                    batch.WorldMatrices,
                    batch.WorldMatrices.Length,
                    null,
                    shadows,
                    _settings.ReceiveShadows,
                    _root.gameObject.layer,
                    viewCamera,
                    LightProbeUsage.Off);
            }
        }

        public void Dispose()
        {
            _batches.Clear();
        }

        private static void GeneratePlacements(
            Vector2Int coordinate,
            float chunkSize,
            DuneHeightField heightField,
            int worldSeed,
            DesertShrubTuning settings,
            LandmarkSystemTuning landmarkSettings,
            IReadOnlyList<Vector2> gameplayExclusions,
            IReadOnlyList<Vector2> sceneryExclusions,
            List<Matrix4x4>[] matricesByVariant)
        {
            double originX = coordinate.x * (double)chunkSize;
            double originZ = coordinate.y * (double)chunkSize;
            float cellSize = Mathf.Max(8f, settings.ClusterCellSize);
            float radius = Mathf.Max(0.1f, settings.ClusterRadius);
            int minimumCellX = Mathf.FloorToInt((float)((originX - radius) / cellSize));
            int maximumCellX = Mathf.FloorToInt((float)((originX + chunkSize + radius) / cellSize));
            int minimumCellZ = Mathf.FloorToInt((float)((originZ - radius) / cellSize));
            int maximumCellZ = Mathf.FloorToInt((float)((originZ + chunkSize + radius) / cellSize));
            float clusterChance = Mathf.Clamp01(settings.ClusterChance);
            float expectedPerCluster = settings.DensityPerChunk * cellSize * cellSize /
                Mathf.Max(1f, chunkSize * chunkSize * Mathf.Max(0.05f, clusterChance));
            int minimumCluster = Mathf.Max(1, settings.MinimumClusterSize);
            int maximumCluster = Mathf.Max(minimumCluster, settings.MaximumClusterSize);
            List<Vector2> accepted = new List<Vector2>();

            for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
            {
                for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                {
                    if (DuneVectorMath.Hash01(cellX, cellZ, worldSeed, 15001) >= clusterChance)
                    {
                        continue;
                    }

                    double centerX = ((cellX + 0.5) * cellSize) +
                        DuneVectorMath.HashRange(cellX, cellZ, worldSeed, 15007, -cellSize * 0.38f, cellSize * 0.38f);
                    double centerZ = ((cellZ + 0.5) * cellSize) +
                        DuneVectorMath.HashRange(cellX, cellZ, worldSeed, 15013, -cellSize * 0.38f, cellSize * 0.38f);
                    float centerWeight = SampleBiomeWeight(centerX, centerZ, worldSeed, settings);
                    if (DuneVectorMath.Hash01(cellX, cellZ, worldSeed, 15017) > centerWeight)
                    {
                        continue;
                    }

                    float countValue = expectedPerCluster;
                    if (countValue < minimumCluster)
                    {
                        float clusterRetention = countValue / minimumCluster;
                        if (DuneVectorMath.Hash01(cellX, cellZ, worldSeed, 15023) >= clusterRetention)
                        {
                            continue;
                        }
                        countValue = minimumCluster;
                    }
                    countValue = Mathf.Min(countValue, maximumCluster);
                    int memberCount = Mathf.FloorToInt(countValue);
                    if (DuneVectorMath.Hash01(cellX, cellZ, worldSeed, 15031) < countValue - memberCount)
                    {
                        memberCount++;
                    }

                    for (int member = 0; member < memberCount; member++)
                    {
                        int salt = 15101 + (member * 31);
                        float angle = DuneVectorMath.HashRange(cellX, cellZ, worldSeed, salt, 0f, Mathf.PI * 2f);
                        float memberRadius = Mathf.Sqrt(DuneVectorMath.Hash01(cellX, cellZ, worldSeed, salt + 3)) * radius;
                        double logicalX = centerX + (Math.Cos(angle) * memberRadius);
                        double logicalZ = centerZ + (Math.Sin(angle) * memberRadius);
                        Vector2 local = new Vector2((float)(logicalX - originX), (float)(logicalZ - originZ));
                        if (local.x < 0f || local.x >= chunkSize || local.y < 0f || local.y >= chunkSize)
                        {
                            continue;
                        }

                        float biomeWeight = SampleBiomeWeight(logicalX, logicalZ, worldSeed, settings);
                        if (biomeWeight <= 0f ||
                            IsNear(local, accepted, settings.MinimumSpacing) ||
                            IsNear(local, gameplayExclusions, settings.GameplayExclusionRadius) ||
                            IsNear(local, sceneryExclusions, settings.SceneryExclusionRadius) ||
                            IsInsideHub(logicalX, logicalZ, settings.HubExclusionRadius) ||
                            IsNearProceduralLandmark(logicalX, logicalZ, worldSeed, heightField, landmarkSettings, settings.LandmarkExclusionRadius))
                        {
                            continue;
                        }

                        Vector3 normal = heightField.SampleNormal(logicalX, logicalZ);
                        if (Vector3.Angle(normal, Vector3.up) > settings.MaximumSlope)
                        {
                            continue;
                        }

                        int variantIndex = Mathf.FloorToInt(
                            DuneVectorMath.Hash01(cellX, cellZ, worldSeed, salt + 11) * matricesByVariant.Length);
                        variantIndex = Mathf.Clamp(variantIndex, 0, matricesByVariant.Length - 1);
                        float minimumScale = Mathf.Max(0.05f, settings.MinimumScale);
                        float maximumScale = Mathf.Max(minimumScale, settings.MaximumScale);
                        float scale = DuneVectorMath.HashRange(cellX, cellZ, worldSeed, salt + 13, minimumScale, maximumScale);
                        float yaw = DuneVectorMath.HashRange(cellX, cellZ, worldSeed, salt + 17, 0f, 360f);
                        float minimumBurial = Mathf.Max(0f, settings.MinimumBurialDepth);
                        float maximumBurial = Mathf.Max(minimumBurial, settings.MaximumBurialDepth);
                        float burial = DuneVectorMath.HashRange(cellX, cellZ, worldSeed, salt + 19, minimumBurial, maximumBurial);
                        float height = (float)heightField.SampleHeight(logicalX, logicalZ) - burial;
                        Quaternion surfaceRotation = Quaternion.Slerp(
                            Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, normal),
                            Mathf.Clamp01(settings.SurfaceAlignment));
                        Quaternion rotation = surfaceRotation * Quaternion.Euler(0f, yaw, 0f);
                        matricesByVariant[variantIndex].Add(Matrix4x4.TRS(
                            new Vector3(local.x, height, local.y),
                            rotation,
                            Vector3.one * scale));
                        accepted.Add(local);
                    }
                }
            }
        }

        private static float SampleBiomeWeight(double x, double z, int worldSeed, DesertShrubTuning settings)
        {
            double scale = Math.Max(1.0, settings.BiomeNoiseScale);
            float noise = (float)DuneVectorMath.FractalNoise(x / scale, z / scale, worldSeed, 15299, 3, 0.52, 2.07);
            if (noise < settings.MinimumBiomeNoise)
            {
                return 0f;
            }
            float fullDensityNoise = Mathf.Max(settings.MinimumBiomeNoise + 0.001f, settings.FullDensityBiomeNoise);
            float normalized = Mathf.InverseLerp(settings.MinimumBiomeNoise, fullDensityNoise, noise);
            return Mathf.Lerp(settings.MinimumRegionWeight, 1f, Mathf.Pow(normalized, settings.BiomeWeightPower));
        }

        private static bool IsInsideHub(double x, double z, float radius)
        {
            double dx = x - DesertWorldStreamer.StartingLogicalPosition.x;
            double dz = z - DesertWorldStreamer.StartingLogicalPosition.y;
            return (dx * dx) + (dz * dz) < radius * radius;
        }

        private static bool IsNearProceduralLandmark(
            double x,
            double z,
            int worldSeed,
            DuneHeightField heightField,
            LandmarkSystemTuning landmarks,
            float exclusionRadius)
        {
            if (landmarks == null || !landmarks.Enabled || exclusionRadius <= 0f)
            {
                return false;
            }
            float size = Mathf.Max(1f, landmarks.PlacementCellSize);
            int radiusInCells = Mathf.CeilToInt(exclusionRadius / size) + 1;
            int centerCellX = Mathf.FloorToInt((float)(x / size));
            int centerCellZ = Mathf.FloorToInt((float)(z / size));
            double exclusionSquared = exclusionRadius * exclusionRadius;
            for (int dz = -radiusInCells; dz <= radiusInCells; dz++)
            {
                for (int dx = -radiusInCells; dx <= radiusInCells; dx++)
                {
                    int cellX = centerCellX + dx;
                    int cellZ = centerCellZ + dz;
                    float roll = DuneVectorMath.Hash01(cellX, cellZ, worldSeed, 7103);
                    if (roll > landmarks.RegionDefiningCellChance + landmarks.RareCellChance +
                        landmarks.StandardCellChance + landmarks.CommonCellChance)
                    {
                        continue;
                    }
                    float inset = size * 0.2f;
                    double landmarkX = (cellX * size) + DuneVectorMath.HashRange(cellX, cellZ, worldSeed, 7111, inset, size - inset);
                    double landmarkZ = (cellZ * size) + DuneVectorMath.HashRange(cellX, cellZ, worldSeed, 7113, inset, size - inset);
                    if (Vector3.Angle(heightField.SampleNormal(landmarkX, landmarkZ), Vector3.up) > landmarks.MaximumPlacementSlope)
                    {
                        landmarkX = (cellX + 0.5) * size;
                        landmarkZ = (cellZ + 0.5) * size;
                        if (Vector3.Angle(heightField.SampleNormal(landmarkX, landmarkZ), Vector3.up) > landmarks.MaximumPlacementSlope)
                        {
                            continue;
                        }
                    }
                    double deltaX = x - landmarkX;
                    double deltaZ = z - landmarkZ;
                    if ((deltaX * deltaX) + (deltaZ * deltaZ) < exclusionSquared)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsNear(Vector2 position, IReadOnlyList<Vector2> others, float distance)
        {
            if (others == null || distance <= 0f)
            {
                return false;
            }
            float squared = distance * distance;
            for (int i = 0; i < others.Count; i++)
            {
                if ((position - others[i]).sqrMagnitude < squared)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<PatchVariant> LoadPatchVariants(string resourcePath)
        {
            GameObject[] prefabs = Resources.LoadAll<GameObject>(resourcePath ?? string.Empty);
            Array.Sort(prefabs, (left, right) => string.CompareOrdinal(left.name, right.name));
            List<PatchVariant> variants = new List<PatchVariant>(prefabs.Length);
            for (int i = 0; i < prefabs.Length; i++)
            {
                MeshFilter meshFilter = prefabs[i].GetComponentInChildren<MeshFilter>();
                MeshRenderer meshRenderer = meshFilter != null ? meshFilter.GetComponent<MeshRenderer>() : null;
                if (meshFilter == null || meshFilter.sharedMesh == null || meshRenderer == null ||
                    meshRenderer.sharedMaterials == null || meshRenderer.sharedMaterials.Length == 0)
                {
                    continue;
                }
                variants.Add(new PatchVariant
                {
                    Mesh = meshFilter.sharedMesh,
                    Materials = meshRenderer.sharedMaterials,
                });
            }
            return variants;
        }

    }
}
