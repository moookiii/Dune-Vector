using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Streams multiresolution height tiles for the world map. Height samples are
    /// persisted independently from exploration so changing map colors or contour
    /// styling never invalidates the expensive terrain cache.
    /// </summary>
    internal sealed class DuneVectorWorldMapTileCache : IDisposable
    {
        private readonly struct TileKey : IEquatable<TileKey>
        {
            public readonly int Lod;
            public readonly int X;
            public readonly int Z;

            public TileKey(int lod, int x, int z)
            {
                Lod = lod;
                X = x;
                Z = z;
            }

            public bool Equals(TileKey other)
            {
                return Lod == other.Lod && X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is TileKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Lod;
                    hash = (hash * 397) ^ X;
                    return (hash * 397) ^ Z;
                }
            }
        }

        private sealed class RuntimeTile
        {
            public Texture2D HeightTexture;
            public Texture2D ExplorationTexture;
            public RenderTexture StyledTexture;
            public int ExplorationRevision = -1;
            public int LastUsedFrame;
        }

        private sealed class TileBuildResult
        {
            public TileKey Key;
            public float[] Heights;
            public long CacheDataOffset;
        }

        private sealed class TileBuildJob
        {
            public TileKey Key;
            public Task<TileBuildResult> Task;
        }

        private const int CacheFileMagic = 0x44565443;
        private const int CacheFileVersion = 1;

        private readonly DuneHeightField _heightField;
        private readonly MapHudTuning _settings;
        private readonly Func<double, double, bool> _isExplored;
        private readonly Func<int, int, int, bool> _isTileExplored;
        private readonly Dictionary<TileKey, long> _diskOffsets =
            new Dictionary<TileKey, long>();
        private readonly Dictionary<TileKey, RuntimeTile> _runtimeTiles =
            new Dictionary<TileKey, RuntimeTile>();
        private readonly List<TileKey> _pendingKeys = new List<TileKey>();
        private readonly List<TileKey> _styleRequestKeys = new List<TileKey>();
        private readonly List<TileBuildJob> _buildJobs = new List<TileBuildJob>();
        private readonly object _cacheWriteLock = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ManualResetEventSlim _processingGate = new ManualResetEventSlim(false);
        private readonly string _cachePath;
        private readonly Material _terrainMaterial;
        private int _explorationRevision;
        private int _minimumRuntimeTileCount;
        private volatile bool _processingEnabled;
        private bool _disposed;

        public bool IsAvailable => !_disposed && _terrainMaterial != null;

        public DuneVectorWorldMapTileCache(
            DuneHeightField heightField,
            MapHudTuning settings,
            Func<double, double, bool> isExplored,
            Func<int, int, int, bool> isTileExplored)
        {
            _heightField = heightField ?? throw new ArgumentNullException(nameof(heightField));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _isExplored = isExplored ?? throw new ArgumentNullException(nameof(isExplored));
            _isTileExplored =
                isTileExplored ?? throw new ArgumentNullException(nameof(isTileExplored));
            _cachePath = GetCachePath(settings);

            Shader shader = Shader.Find("Hidden/DuneVector/World Map Terrain Tile");
            if (shader != null)
            {
                _terrainMaterial = new Material(shader)
                {
                    name = "Dune Vector World Map Terrain Tile - Runtime",
                    hideFlags = HideFlags.DontSave,
                };
            }
            else
            {
                Debug.LogWarning(
                    "World-map terrain tile shader was not found. Falling back to the legacy map renderer.");
            }

            InitializeCacheIndex();
        }

        public void MarkExplorationChanged()
        {
            unchecked
            {
                _explorationRevision++;
            }
        }

        public void Prefetch(
            LogicalPosition center,
            float displayedWorldWidth,
            float displayedWorldHeight,
            float viewportPixelHeight)
        {
            if (!IsAvailable)
            {
                return;
            }

            float coarseFactor = Mathf.Clamp(
                _settings.WorldMapTerrainPrefetchCoarseFactor,
                1f,
                8f);
            List<TileKey> visibleKeys = GetVisibleKeys(
                center,
                displayedWorldWidth,
                displayedWorldHeight,
                viewportPixelHeight / coarseFactor);
            List<TileKey> detailedKeys = GetVisibleKeys(
                center,
                displayedWorldWidth,
                displayedWorldHeight,
                viewportPixelHeight);
            for (int index = 0; index < detailedKeys.Count; index++)
            {
                if (!visibleKeys.Contains(detailedKeys[index]))
                {
                    visibleKeys.Add(detailedKeys[index]);
                }
            }
            _minimumRuntimeTileCount = visibleKeys.Count;
            SetPendingKeys(visibleKeys, false);
        }

        public void Update()
        {
            if (!IsAvailable || !_processingEnabled)
            {
                return;
            }

            CompleteBuildJobs();
            RefreshRequestedStyles();
            StartPendingBuildJobs();
            TrimRuntimeCache();
        }

        public void SetProcessingEnabled(bool enabled)
        {
            _processingEnabled = enabled;
            if (enabled)
            {
                _processingGate.Set();
            }
            else
            {
                _processingGate.Reset();
                _styleRequestKeys.Clear();
            }
        }

        public bool Draw(
            Rect mapRect,
            LogicalPosition center,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            if (!IsAvailable || mapRect.width <= 0f || mapRect.height <= 0f)
            {
                return false;
            }

            List<TileKey> visibleKeys = GetVisibleKeys(
                center,
                displayedWorldWidth,
                displayedWorldHeight,
                mapRect.height);
            List<TileKey> requestedKeys = GetVisibleKeys(
                center,
                displayedWorldWidth,
                displayedWorldHeight,
                mapRect.height / Mathf.Clamp(
                    _settings.WorldMapTerrainPrefetchCoarseFactor,
                    1f,
                    8f));
            for (int index = 0; index < visibleKeys.Count; index++)
            {
                if (!requestedKeys.Contains(visibleKeys[index]))
                {
                    requestedKeys.Add(visibleKeys[index]);
                }
            }
            _minimumRuntimeTileCount = requestedKeys.Count;
            SetPendingKeys(requestedKeys, true);

            bool drewAny = false;
            for (int index = 0; index < visibleKeys.Count; index++)
            {
                TileKey key = visibleKeys[index];
                if (!_runtimeTiles.TryGetValue(key, out RuntimeTile tile))
                {
                    drewAny |= DrawBestAvailableParent(
                        key,
                        mapRect,
                        center,
                        displayedWorldWidth,
                        displayedWorldHeight);
                    continue;
                }

                if (tile.StyledTexture == null)
                {
                    continue;
                }

                tile.LastUsedFrame = Time.frameCount;
                Rect tileRect = GetTileScreenRect(
                    key,
                    mapRect,
                    center,
                    displayedWorldWidth,
                    displayedWorldHeight);
                GUI.DrawTexture(tileRect, tile.StyledTexture, ScaleMode.StretchToFill, false);
                drewAny = true;
            }

            return drewAny;
        }

        private bool DrawBestAvailableParent(
            TileKey childKey,
            Rect mapRect,
            LogicalPosition center,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            int maximumLod = Mathf.Max(0, _settings.WorldMapTerrainMaximumLod);
            for (int parentLod = childKey.Lod + 1; parentLod <= maximumLod; parentLod++)
            {
                int scale = 1 << Mathf.Min(30, parentLod - childKey.Lod);
                int parentX = FloorDivide(childKey.X, scale);
                int parentZ = FloorDivide(childKey.Z, scale);
                TileKey parentKey = new TileKey(parentLod, parentX, parentZ);
                if (!_runtimeTiles.TryGetValue(parentKey, out RuntimeTile parentTile))
                {
                    continue;
                }

                if (parentTile.StyledTexture == null)
                {
                    continue;
                }

                parentTile.LastUsedFrame = Time.frameCount;
                int localX = childKey.X - (parentX * scale);
                int localZ = childKey.Z - (parentZ * scale);
                float uvScale = 1f / scale;
                Rect uvRect = new Rect(
                    localX * uvScale,
                    localZ * uvScale,
                    uvScale,
                    uvScale);
                Rect childRect = GetTileScreenRect(
                    childKey,
                    mapRect,
                    center,
                    displayedWorldWidth,
                    displayedWorldHeight);
                GUI.DrawTextureWithTexCoords(
                    childRect,
                    parentTile.StyledTexture,
                    uvRect,
                    false);
                return true;
            }
            return false;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private List<TileKey> GetVisibleKeys(
            LogicalPosition center,
            float displayedWorldWidth,
            float displayedWorldHeight,
            float viewportPixelHeight)
        {
            int resolution = GetTileResolution();
            float worldUnitsPerPixel =
                displayedWorldHeight / Mathf.Max(1f, viewportPixelHeight);
            float targetTileWorldSize =
                worldUnitsPerPixel *
                resolution /
                Mathf.Max(0.25f, _settings.WorldMapTerrainTexelsPerScreenPixel);
            float baseTileWorldSize = GetBaseTileWorldSize();
            int lod = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Log(
                    Mathf.Max(1f, targetTileWorldSize / baseTileWorldSize),
                    2f)),
                0,
                Mathf.Max(0, _settings.WorldMapTerrainMaximumLod));
            double tileWorldSize = GetTileWorldSize(lod);
            double halfWidth = displayedWorldWidth * 0.5d;
            double halfHeight = displayedWorldHeight * 0.5d;
            int minimumX = (int)Math.Floor((center.X - halfWidth) / tileWorldSize);
            int maximumX = (int)Math.Floor((center.X + halfWidth) / tileWorldSize);
            int minimumZ = (int)Math.Floor((center.Z - halfHeight) / tileWorldSize);
            int maximumZ = (int)Math.Floor((center.Z + halfHeight) / tileWorldSize);

            List<TileKey> keys = new List<TileKey>(
                Math.Max(1, (maximumX - minimumX + 1) * (maximumZ - minimumZ + 1)));
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    if (_isTileExplored(lod, x, z))
                    {
                        keys.Add(new TileKey(lod, x, z));
                    }
                }
            }
            keys.Sort((left, right) =>
            {
                double leftX = ((left.X + 0.5d) * tileWorldSize) - center.X;
                double leftZ = ((left.Z + 0.5d) * tileWorldSize) - center.Z;
                double rightX = ((right.X + 0.5d) * tileWorldSize) - center.X;
                double rightZ = ((right.Z + 0.5d) * tileWorldSize) - center.Z;
                return ((leftX * leftX) + (leftZ * leftZ)).CompareTo(
                    (rightX * rightX) + (rightZ * rightZ));
            });
            return keys;
        }

        private void SetPendingKeys(List<TileKey> visibleKeys, bool requestStyles)
        {
            _pendingKeys.Clear();
            if (requestStyles)
            {
                _styleRequestKeys.Clear();
            }
            for (int index = 0; index < visibleKeys.Count; index++)
            {
                TileKey key = visibleKeys[index];
                if (requestStyles)
                {
                    _styleRequestKeys.Add(key);
                }
                if (_runtimeTiles.ContainsKey(key) || IsBuildActive(key))
                {
                    continue;
                }
                _pendingKeys.Add(key);
            }
        }

        private bool IsBuildActive(TileKey key)
        {
            for (int index = 0; index < _buildJobs.Count; index++)
            {
                if (_buildJobs[index].Key.Equals(key))
                {
                    return true;
                }
            }
            return false;
        }

        private void StartPendingBuildJobs()
        {
            int maximumConcurrentBuilds = Mathf.Clamp(
                _settings.WorldMapTerrainConcurrentBuilds,
                1,
                4);
            while (_buildJobs.Count < maximumConcurrentBuilds && _pendingKeys.Count > 0)
            {
                TileKey key = _pendingKeys[0];
                _pendingKeys.RemoveAt(0);
                if (_runtimeTiles.ContainsKey(key) || IsBuildActive(key))
                {
                    continue;
                }

                CancellationToken cancellationToken = _cancellation.Token;
                TileBuildJob job = new TileBuildJob
                {
                    Key = key,
                    Task = Task.Factory.StartNew(
                        () => LoadOrBuildTile(key, cancellationToken),
                        cancellationToken,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default),
                };
                _buildJobs.Add(job);
            }
        }

        private void CompleteBuildJobs()
        {
            for (int index = _buildJobs.Count - 1; index >= 0; index--)
            {
                TileBuildJob job = _buildJobs[index];
                if (!job.Task.IsCompleted)
                {
                    continue;
                }

                _buildJobs.RemoveAt(index);
                if (job.Task.IsCanceled)
                {
                    continue;
                }
                if (job.Task.IsFaulted)
                {
                    Debug.LogWarning(
                        $"Unable to build world-map terrain tile " +
                        $"LOD {job.Key.Lod} ({job.Key.X}, {job.Key.Z}): " +
                        $"{job.Task.Exception?.GetBaseException().Message}");
                    continue;
                }

                TileBuildResult result = job.Task.Result;
                if (result == null || result.Heights == null)
                {
                    continue;
                }
                lock (_cacheWriteLock)
                {
                    _diskOffsets[result.Key] = result.CacheDataOffset;
                }
                CreateRuntimeTile(result.Key, result.Heights);
                if (_runtimeTiles.TryGetValue(result.Key, out RuntimeTile runtimeTile))
                {
                    EnsureStyledTexture(result.Key, runtimeTile);
                }
            }
        }

        private void RefreshRequestedStyles()
        {
            int refreshLimit = Mathf.Clamp(
                _settings.WorldMapTerrainStyleRefreshesPerFrame,
                1,
                8);
            int refreshed = 0;
            for (int index = 0;
                index < _styleRequestKeys.Count && refreshed < refreshLimit;
                index++)
            {
                TileKey key = _styleRequestKeys[index];
                if (!_runtimeTiles.TryGetValue(key, out RuntimeTile tile) ||
                    tile.ExplorationRevision == _explorationRevision)
                {
                    continue;
                }
                EnsureStyledTexture(key, tile);
                refreshed++;
            }
        }

        private TileBuildResult LoadOrBuildTile(
            TileKey key,
            CancellationToken cancellationToken)
        {
            WaitForProcessing(cancellationToken);
            long dataOffset;
            lock (_cacheWriteLock)
            {
                _diskOffsets.TryGetValue(key, out dataOffset);
            }
            if (dataOffset > 0L)
            {
                float[] cachedHeights = ReadCachedHeights(dataOffset, cancellationToken);
                if (cachedHeights != null)
                {
                    return new TileBuildResult
                    {
                        Key = key,
                        Heights = cachedHeights,
                        CacheDataOffset = dataOffset,
                    };
                }
            }

            int resolution = GetTileResolution();
            float[] heights = new float[resolution * resolution];
            double tileWorldSize = GetTileWorldSize(key.Lod);
            double minimumX = key.X * tileWorldSize;
            double minimumZ = key.Z * tileWorldSize;
            double sampleStep = tileWorldSize / Math.Max(1, resolution - 1);
            for (int y = 0; y < resolution; y++)
            {
                WaitForProcessing(cancellationToken);
                double logicalZ = minimumZ + (y * sampleStep);
                int rowOffset = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    heights[rowOffset + x] = (float)_heightField.SampleHeight(
                        minimumX + (x * sampleStep),
                        logicalZ);
                }
            }

            long writtenOffset = AppendTileToCache(key, heights, cancellationToken);
            return new TileBuildResult
            {
                Key = key,
                Heights = heights,
                CacheDataOffset = writtenOffset,
            };
        }

        private void CreateRuntimeTile(TileKey key, float[] heights)
        {
            int resolution = GetTileResolution();
            Texture2D heightTexture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RFloat,
                false,
                true)
            {
                name = $"World Map Height LOD {key.Lod} ({key.X}, {key.Z})",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            heightTexture.SetPixelData(heights, 0);
            heightTexture.Apply(false, true);
            _runtimeTiles[key] = new RuntimeTile
            {
                HeightTexture = heightTexture,
                LastUsedFrame = Time.frameCount,
            };
        }

        private void EnsureStyledTexture(TileKey key, RuntimeTile tile)
        {
            if (tile.ExplorationRevision == _explorationRevision &&
                tile.StyledTexture != null)
            {
                return;
            }

            int resolution = GetTileResolution();
            byte[] maskPixels = new byte[resolution * resolution];
            double tileWorldSize = GetTileWorldSize(key.Lod);
            double minimumX = key.X * tileWorldSize;
            double minimumZ = key.Z * tileWorldSize;
            double sampleStep = tileWorldSize / Math.Max(1, resolution - 1);
            for (int y = 0; y < resolution; y++)
            {
                double logicalZ = minimumZ + (y * sampleStep);
                int rowOffset = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    maskPixels[rowOffset + x] = _isExplored(
                        minimumX + (x * sampleStep),
                        logicalZ)
                            ? byte.MaxValue
                            : byte.MinValue;
                }
            }

            if (tile.ExplorationTexture == null)
            {
                tile.ExplorationTexture = new Texture2D(
                    resolution,
                    resolution,
                    TextureFormat.R8,
                    false,
                    true)
                {
                    name = $"World Map Exploration LOD {key.Lod} ({key.X}, {key.Z})",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave,
                };
            }
            tile.ExplorationTexture.SetPixelData(maskPixels, 0);
            tile.ExplorationTexture.Apply(false, false);

            if (tile.StyledTexture == null)
            {
                tile.StyledTexture = new RenderTexture(
                    resolution,
                    resolution,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = $"World Map Styled Terrain LOD {key.Lod} ({key.X}, {key.Z})",
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = true,
                    autoGenerateMips = false,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave,
                };
                tile.StyledTexture.Create();
            }

            ApplyMaterialSettings(tile.ExplorationTexture);
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                Graphics.Blit(tile.HeightTexture, tile.StyledTexture, _terrainMaterial);
                tile.StyledTexture.GenerateMips();
            }
            finally
            {
                RenderTexture.active = previousTarget;
            }
            tile.ExplorationRevision = _explorationRevision;
        }

        private void ApplyMaterialSettings(Texture explorationTexture)
        {
            _terrainMaterial.SetTexture("_ExplorationTex", explorationTexture);
            _terrainMaterial.SetColor("_UnexploredColor", _settings.UnexploredColor);
            _terrainMaterial.SetColor("_TerrainLowColor", _settings.TerrainLowColor);
            _terrainMaterial.SetColor("_TerrainHighColor", _settings.TerrainHighColor);
            _terrainMaterial.SetColor("_ContourColor", _settings.ContourColor);
            _terrainMaterial.SetFloat("_TerrainHeightMinimum", _settings.TerrainHeightMinimum);
            _terrainMaterial.SetFloat("_TerrainHeightMaximum", _settings.TerrainHeightMaximum);
            _terrainMaterial.SetFloat("_HeightContrast", _settings.HeightContrast);
            _terrainMaterial.SetFloat("_ContourSpacing", _settings.ContourSpacing);
            _terrainMaterial.SetFloat("_ContourThickness", _settings.ContourThickness);
            _terrainMaterial.SetFloat("_ContourStrength", _settings.ContourStrength);
            _terrainMaterial.SetFloat(
                "_ContourAntialiasPixels",
                _settings.WorldMapContourAntialiasPixels);
            _terrainMaterial.SetFloat(
                "_ExplorationEdgeSoftness",
                _settings.WorldMapExplorationEdgeSoftness);
        }

        private Rect GetTileScreenRect(
            TileKey key,
            Rect mapRect,
            LogicalPosition center,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            double tileWorldSize = GetTileWorldSize(key.Lod);
            double minimumX = key.X * tileWorldSize;
            double maximumZ = (key.Z + 1d) * tileWorldSize;
            float pixelsPerWorldX = mapRect.width / Mathf.Max(1f, displayedWorldWidth);
            float pixelsPerWorldZ = mapRect.height / Mathf.Max(1f, displayedWorldHeight);
            float minimumScreenX =
                (mapRect.width * 0.5f) +
                ((float)(minimumX - center.X) * pixelsPerWorldX);
            float minimumScreenY =
                (mapRect.height * 0.5f) -
                ((float)(maximumZ - center.Z) * pixelsPerWorldZ);
            float maximumScreenX =
                minimumScreenX + ((float)tileWorldSize * pixelsPerWorldX);
            float maximumScreenY =
                minimumScreenY + ((float)tileWorldSize * pixelsPerWorldZ);

            // Floor leading edges and ceil trailing edges so adjacent tiles
            // share at least one covered pixel instead of exposing the black
            // map background when their world boundary lands between pixels.
            return Rect.MinMaxRect(
                Mathf.Floor(minimumScreenX),
                Mathf.Floor(minimumScreenY),
                Mathf.Ceil(maximumScreenX),
                Mathf.Ceil(maximumScreenY));
        }

        private void TrimRuntimeCache()
        {
            int limit = Mathf.Max(
                Mathf.Max(4, _settings.WorldMapTerrainMemoryTileLimit),
                _minimumRuntimeTileCount);
            while (_runtimeTiles.Count > limit)
            {
                TileKey oldestKey = default;
                RuntimeTile oldestTile = null;
                foreach (KeyValuePair<TileKey, RuntimeTile> pair in _runtimeTiles)
                {
                    if (oldestTile == null ||
                        pair.Value.LastUsedFrame < oldestTile.LastUsedFrame)
                    {
                        oldestKey = pair.Key;
                        oldestTile = pair.Value;
                    }
                }
                if (oldestTile == null)
                {
                    return;
                }
                DestroyRuntimeTile(oldestTile);
                _runtimeTiles.Remove(oldestKey);
            }
        }

        private void InitializeCacheIndex()
        {
            try
            {
                if (!File.Exists(_cachePath) || !TryReadCacheIndex())
                {
                    CreateEmptyCacheFile();
                }
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"Unable to initialize world-map terrain cache: {exception.Message}");
            }
        }

        private bool TryReadCacheIndex()
        {
            using FileStream stream = File.Open(_cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);
            if (stream.Length < sizeof(int) * 4 + sizeof(float))
            {
                return false;
            }
            if (reader.ReadInt32() != CacheFileMagic ||
                reader.ReadInt32() != CacheFileVersion ||
                reader.ReadInt32() != GetTileResolution() ||
                !Mathf.Approximately(reader.ReadSingle(), GetBaseTileWorldSize()) ||
                reader.ReadInt32() != GetHeightFieldSignature())
            {
                return false;
            }

            int expectedCount = GetTileResolution() * GetTileResolution();
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < (sizeof(int) * 4))
                {
                    return false;
                }
                TileKey key = new TileKey(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                int count = reader.ReadInt32();
                if (count != expectedCount ||
                    stream.Length - stream.Position < count * (long)sizeof(float))
                {
                    return false;
                }
                _diskOffsets[key] = stream.Position;
                stream.Seek(count * (long)sizeof(float), SeekOrigin.Current);
            }
            return true;
        }

        private void CreateEmptyCacheFile()
        {
            _diskOffsets.Clear();
            string directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            using FileStream stream = File.Create(_cachePath);
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(CacheFileMagic);
            writer.Write(CacheFileVersion);
            writer.Write(GetTileResolution());
            writer.Write(GetBaseTileWorldSize());
            writer.Write(GetHeightFieldSignature());
        }

        private float[] ReadCachedHeights(
            long dataOffset,
            CancellationToken cancellationToken)
        {
            int count = GetTileResolution() * GetTileResolution();
            float[] heights = new float[count];
            try
            {
                using FileStream stream = File.Open(
                    _cachePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                using BinaryReader reader = new BinaryReader(stream);
                stream.Seek(dataOffset, SeekOrigin.Begin);
                for (int index = 0; index < count; index++)
                {
                    if ((index & 4095) == 0)
                    {
                        WaitForProcessing(cancellationToken);
                    }
                    heights[index] = reader.ReadSingle();
                }
                return heights;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private long AppendTileToCache(
            TileKey key,
            float[] heights,
            CancellationToken cancellationToken)
        {
            lock (_cacheWriteLock)
            {
                WaitForProcessing(cancellationToken);
                using FileStream stream = File.Open(
                    _cachePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                using BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(key.Lod);
                writer.Write(key.X);
                writer.Write(key.Z);
                writer.Write(heights.Length);
                long dataOffset = stream.Position;
                for (int index = 0; index < heights.Length; index++)
                {
                    writer.Write(heights[index]);
                }
                return dataOffset;
            }
        }

        private void WaitForProcessing(CancellationToken cancellationToken)
        {
            _processingGate.Wait(cancellationToken);
        }

        private int GetTileResolution()
        {
            return Mathf.Clamp(_settings.WorldMapTerrainTileResolution, 64, 512);
        }

        private float GetBaseTileWorldSize()
        {
            return Mathf.Max(32f, _settings.WorldMapTerrainBaseTileWorldSize);
        }

        private double GetTileWorldSize(int lod)
        {
            return GetBaseTileWorldSize() * Math.Pow(2d, lod);
        }

        private int GetHeightFieldSignature()
        {
            DuneFieldSettings field = _heightField.Settings;
            unchecked
            {
                int hash = field.WorldSeed;
                hash = AddHash(hash, field.BaseHeight);
                hash = AddHash(hash, field.HeightMultiplier);
                hash = AddHash(hash, field.MajorScale);
                hash = AddHash(hash, field.MajorAmplitude);
                hash = (hash * 397) ^ field.MajorOctaves;
                hash = AddHash(hash, field.MajorPersistence);
                hash = AddHash(hash, field.MajorLacunarity);
                hash = AddHash(hash, field.BroadBowlStrength);
                hash = AddHash(hash, field.DuneScale);
                hash = AddHash(hash, field.DuneAmplitude);
                hash = AddHash(hash, field.WindDirection.x);
                hash = AddHash(hash, field.WindDirection.y);
                hash = AddHash(hash, field.DuneWarp);
                hash = (hash * 397) ^ field.WarpOctaves;
                hash = AddHash(hash, field.PrimaryRidgeWeight);
                hash = AddHash(hash, field.RidgeHarmonicWeight);
                hash = AddHash(hash, field.RidgeHarmonicFrequency);
                hash = AddHash(hash, field.RidgeHarmonicPhase);
                hash = AddHash(hash, field.CrestVariationStrength);
                hash = AddHash(hash, field.SecondaryScale);
                hash = AddHash(hash, field.SecondaryAmplitude);
                hash = (hash * 397) ^ field.SecondaryOctaves;
                hash = AddHash(hash, field.SecondaryPersistence);
                hash = AddHash(hash, field.SecondaryLacunarity);
                hash = AddHash(hash, field.DetailScale);
                hash = AddHash(hash, field.DetailAmplitude);
                hash = (hash * 397) ^ field.DetailOctaves;
                hash = AddHash(hash, field.DetailPersistence);
                return AddHash(hash, field.DetailLacunarity);
            }
        }

        private static int AddHash(int hash, float value)
        {
            return unchecked((hash * 397) ^ BitConverter.SingleToInt32Bits(value));
        }

        private static string GetCachePath(MapHudTuning settings)
        {
            string fileName = string.IsNullOrWhiteSpace(settings.WorldMapTerrainCacheFileName)
                ? "DuneVectorWorldMapTerrainCache.dat"
                : Path.GetFileName(settings.WorldMapTerrainCacheFileName);
            if (!fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".dat";
            }
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        private static void DestroyRuntimeTile(RuntimeTile tile)
        {
            if (tile.HeightTexture != null)
            {
                UnityEngine.Object.Destroy(tile.HeightTexture);
            }
            if (tile.ExplorationTexture != null)
            {
                UnityEngine.Object.Destroy(tile.ExplorationTexture);
            }
            if (tile.StyledTexture != null)
            {
                tile.StyledTexture.Release();
                UnityEngine.Object.Destroy(tile.StyledTexture);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cancellation.Cancel();
            _processingGate.Set();
            foreach (RuntimeTile tile in _runtimeTiles.Values)
            {
                DestroyRuntimeTile(tile);
            }
            _runtimeTiles.Clear();
            if (_terrainMaterial != null)
            {
                UnityEngine.Object.Destroy(_terrainMaterial);
            }
        }
    }
}
