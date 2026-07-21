using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    internal sealed class DesertShrubField : IDisposable
    {
        private static readonly Dictionary<int, Mesh> SharedMeshes = new Dictionary<int, Mesh>();

        private sealed class VariantBatch
        {
            public Mesh HighMesh;
            public Mesh LowMesh;
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
            IReadOnlyList<Material> materials,
            IReadOnlyList<Vector2> gameplayExclusions,
            IReadOnlyList<Vector2> sceneryExclusions)
        {
            _root = root;
            _settings = settings;
            _chunkSize = chunkSize;
            if (settings == null || !settings.Enabled || settings.DensityPerChunk <= 0f ||
                settings.Variants == null || settings.Variants.Count == 0 || materials == null)
            {
                return;
            }

            List<Matrix4x4>[] matricesByVariant = new List<Matrix4x4>[settings.Variants.Count];
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

            int variantCount = Mathf.Min(settings.Variants.Count, materials.Count);
            for (int i = 0; i < variantCount; i++)
            {
                DesertShrubVariantTuning variant = settings.Variants[i];
                if (variant == null || materials[i] == null || matricesByVariant[i].Count == 0)
                {
                    continue;
                }

                List<Matrix4x4> variantMatrices = matricesByVariant[i];
                materials[i].enableInstancing = true;
                int maximumInstancesPerDraw = DuneVectorSpatialInstancing.MaximumInstancesPerDraw;
                for (int start = 0; start < variantMatrices.Count; start += maximumInstancesPerDraw)
                {
                    int count = Mathf.Min(maximumInstancesPerDraw, variantMatrices.Count - start);
                    Matrix4x4[] localMatrices = new Matrix4x4[count];
                    variantMatrices.CopyTo(start, localMatrices, 0, count);
                    _batches.Add(new VariantBatch
                    {
                        HighMesh = GetShrubMesh(variant, false),
                        LowMesh = GetShrubMesh(variant, true),
                        Material = materials[i],
                        LocalMatrices = localMatrices,
                        WorldMatrices = new Matrix4x4[count],
                    });
                    InstanceCount += count;
                }
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
                    Bounds highBounds = DuneVectorSpatialInstancing.TransformBounds(
                        batch.WorldMatrices[i],
                        batch.HighMesh.bounds);
                    Bounds lowBounds = DuneVectorSpatialInstancing.TransformBounds(
                        batch.WorldMatrices[i],
                        batch.LowMesh.bounds);
                    highBounds.Encapsulate(lowBounds);
                    if (hasBounds)
                    {
                        batch.WorldBounds.Encapsulate(highBounds);
                    }
                    else
                    {
                        batch.WorldBounds = highBounds;
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

            bool useLowLod = distance > Mathf.Min(_settings.LodDistance, _settings.CullDistance);
            ShadowCastingMode shadows = _settings.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int i = 0; i < _batches.Count; i++)
            {
                VariantBatch batch = _batches[i];
                Graphics.DrawMeshInstanced(
                    useLowLod ? batch.LowMesh : batch.HighMesh,
                    0,
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

                        int variantIndex = ChooseVariant(settings.Variants, cellX, cellZ, worldSeed, salt + 11);
                        if (variantIndex < 0)
                        {
                            continue;
                        }
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

        private static int ChooseVariant(List<DesertShrubVariantTuning> variants, int x, int z, int seed, int salt)
        {
            float total = 0f;
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] != null)
                {
                    total += Mathf.Max(0f, variants[i].SelectionWeight);
                }
            }
            if (total <= 0f)
            {
                return -1;
            }
            float choice = DuneVectorMath.HashRange(x, z, seed, salt, 0f, total);
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] == null)
                {
                    continue;
                }
                choice -= Mathf.Max(0f, variants[i].SelectionWeight);
                if (choice <= 0f)
                {
                    return i;
                }
            }
            return variants.Count - 1;
        }

        private static Mesh GetShrubMesh(DesertShrubVariantTuning variant, bool lowDetail)
        {
            int key = GetMeshKey(variant, lowDetail);
            if (!SharedMeshes.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = BuildShrubMesh(variant, lowDetail);
                SharedMeshes[key] = mesh;
            }
            return mesh;
        }

        private static int GetMeshKey(DesertShrubVariantTuning variant, bool lowDetail)
        {
            unchecked
            {
                int hash = lowDetail ? 486187739 : 16777619;
                hash = (hash * 31) + variant.Height.GetHashCode();
                hash = (hash * 31) + variant.Width.GetHashCode();
                hash = (hash * 31) + variant.BranchCount;
                hash = (hash * 31) + variant.BranchStartHeight.GetHashCode();
                hash = (hash * 31) + variant.BranchUpwardBias.GetHashCode();
                return hash;
            }
        }

        private static Mesh BuildShrubMesh(DesertShrubVariantTuning variant, bool lowDetail)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            float height = Mathf.Max(0.1f, variant.Height);
            float width = Mathf.Max(0.1f, variant.Width);
            int branchCount = lowDetail ? Mathf.Min(3, variant.BranchCount) : variant.BranchCount;
            int sides = lowDetail ? 4 : 6;
            AddTaperedBranch(
                vertices,
                triangles,
                Vector3.zero,
                Vector3.up * (height * 0.82f),
                width * 0.045f,
                width * 0.022f,
                sides);
            AddFacetedClump(
                vertices,
                triangles,
                Vector3.up * (height * 0.82f),
                new Vector3(width * 0.18f, height * 0.16f, width * 0.16f),
                sides);

            for (int i = 0; i < branchCount; i++)
            {
                float normalized = (i + 0.5f) / Mathf.Max(1, branchCount);
                float angle = (normalized * Mathf.PI * 2f) + (i * 0.37f);
                float startHeight = height * Mathf.Lerp(variant.BranchStartHeight, 0.6f, normalized * 0.48f);
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 start = Vector3.up * startHeight;
                Vector3 end = start + (radial * width * Mathf.Lerp(0.34f, 0.5f, normalized)) +
                    (Vector3.up * height * variant.BranchUpwardBias * Mathf.Lerp(0.38f, 0.7f, normalized));
                AddTaperedBranch(vertices, triangles, start, end, width * 0.038f, width * 0.014f, sides);
                AddFacetedClump(
                    vertices,
                    triangles,
                    end,
                    new Vector3(width * 0.145f, height * 0.13f, width * 0.12f),
                    sides);
                if (!lowDetail)
                {
                    Vector3 sideClump = Vector3.Lerp(start, end, 0.72f) +
                        (Vector3.up * height * 0.035f) -
                        (radial * width * 0.025f);
                    AddFacetedClump(
                        vertices,
                        triangles,
                        sideClump,
                        new Vector3(width * 0.1f, height * 0.085f, width * 0.09f),
                        sides);
                }
            }

            Mesh mesh = new Mesh { name = $"Desert Shrub {variant.Name} {(lowDetail ? "LOD1" : "LOD0")}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void AddTaperedBranch(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 start,
            Vector3 end,
            float startRadius,
            float endRadius,
            int sides)
        {
            Vector3 direction = (end - start).normalized;
            Vector3 tangent = Vector3.Cross(direction, Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
            int baseIndex = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector3 radial = (tangent * Mathf.Cos(angle)) + (bitangent * Mathf.Sin(angle));
                vertices.Add(start + (radial * startRadius));
                vertices.Add(end + (radial * endRadius));
            }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int a = baseIndex + (i * 2);
                int b = baseIndex + (next * 2);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(b + 1);
                triangles.Add(a + 1);
            }
        }

        private static void AddFacetedClump(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 center,
            Vector3 radius,
            int sides)
        {
            int top = vertices.Count;
            vertices.Add(center + (Vector3.up * radius.y));
            int bottom = vertices.Count;
            vertices.Add(center - (Vector3.up * radius.y));
            int firstRing = vertices.Count;
            float[] ringHeights = { -0.42f, 0f, 0.42f };
            float[] ringWidths = { 0.7f, 1f, 0.76f };
            for (int ring = 0; ring < ringHeights.Length; ring++)
            {
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    vertices.Add(center + new Vector3(
                        Mathf.Cos(angle) * radius.x * ringWidths[ring],
                        radius.y * ringHeights[ring],
                        Mathf.Sin(angle) * radius.z * ringWidths[ring]));
                }
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                triangles.Add(bottom);
                triangles.Add(firstRing + i);
                triangles.Add(firstRing + next);
            }

            for (int ring = 0; ring < ringHeights.Length - 1; ring++)
            {
                int lower = firstRing + (ring * sides);
                int upper = lower + sides;
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    triangles.Add(lower + i);
                    triangles.Add(lower + next);
                    triangles.Add(upper + i);
                    triangles.Add(lower + next);
                    triangles.Add(upper + next);
                    triangles.Add(upper + i);
                }
            }

            int topRing = firstRing + ((ringHeights.Length - 1) * sides);
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                triangles.Add(top);
                triangles.Add(topRing + next);
                triangles.Add(topRing + i);
            }
        }
    }
}
