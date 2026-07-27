using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuneVector
{
    [Serializable]
    public sealed class PhotographRecord
    {
        public string PhotographId;
        public string SubjectId;
        public int SubjectCategory;
        public long CaptureSequence;
        public long CaptureUtcTicks;
        public bool IsValidSubjectPhotograph;
        public string ImagePath;
    }

    [Serializable]
    public sealed class SubjectDocumentationRecord
    {
        public string SubjectId;
        public int SubjectCategory;
        public bool Documented;
        public bool IsNew;
        public string CanonicalPhotographId;
    }

    [Serializable]
    internal sealed class PhotographySaveData
    {
        public long NextCaptureSequence = 1;
        public List<PhotographRecord> Photographs = new List<PhotographRecord>();
        public List<SubjectDocumentationRecord> Documentation = new List<SubjectDocumentationRecord>();
    }

    internal sealed class DuneVectorPhotographStorage : IDisposable
    {
        private const string SaveFileName = "dune_vector_photography.dat";
        private const string ImageFolderName = "DuneVectorPhotographs";

        private readonly string _savePath;
        private readonly string _imageDirectory;
        private readonly PhotographyTuning _settings;
        private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private PhotographySaveData _data;

        public IReadOnlyList<PhotographRecord> Photographs => _data.Photographs;
        public bool HasNewDocumentation
        {
            get
            {
                for (int i = 0; i < _data.Documentation.Count; i++)
                {
                    if (_data.Documentation[i].IsNew &&
                        _data.Documentation[i].SubjectCategory ==
                        (int)PhotographableSubjectCategory.Glyph)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        public int DocumentedGlyphCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _data.Documentation.Count; i++)
                {
                    SubjectDocumentationRecord record = _data.Documentation[i];
                    if (record.Documented && record.SubjectCategory == (int)PhotographableSubjectCategory.Glyph) count++;
                }
                return count;
            }
        }

        public DuneVectorPhotographStorage(PhotographyTuning settings)
        {
            _settings = settings;
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            _imageDirectory = Path.Combine(Application.persistentDataPath, ImageFolderName);
            Directory.CreateDirectory(_imageDirectory);
            Load();
        }

        public PhotographRecord Store(Texture2D image, string subjectId, PhotographableSubjectCategory category, bool valid)
        {
            while (_data.Photographs.Count >= Mathf.Max(1, _settings.MaximumGalleryPhotographs))
            {
                Delete(_data.Photographs[0].PhotographId);
            }

            long sequence = Math.Max(1, _data.NextCaptureSequence++);
            string photographId = $"photo-{sequence:00000000}";
            string imagePath = Path.Combine(_imageDirectory, photographId + ".jpg");
            byte[] bytes = image.EncodeToJPG(Mathf.Clamp(_settings.JpegQuality, 1, 100));
            string temporaryImagePath = imagePath + ".tmp";
            File.WriteAllBytes(temporaryImagePath, bytes);
            File.Move(temporaryImagePath, imagePath);
            PhotographRecord record = new PhotographRecord
            {
                PhotographId = photographId,
                SubjectId = valid ? subjectId : string.Empty,
                SubjectCategory = (int)category,
                CaptureSequence = sequence,
                CaptureUtcTicks = DateTime.UtcNow.Ticks,
                IsValidSubjectPhotograph = valid,
                ImagePath = imagePath,
            };
            _data.Photographs.Add(record);
            Save();
            return record;
        }

        public bool IsDocumented(string subjectId)
        {
            SubjectDocumentationRecord record = FindDocumentation(subjectId);
            return record != null && record.Documented;
        }

        public bool IsNew(string subjectId)
        {
            SubjectDocumentationRecord record = FindDocumentation(subjectId);
            return record != null && record.IsNew;
        }

        public void Document(string subjectId, PhotographableSubjectCategory category, string photographId)
        {
            SubjectDocumentationRecord documentation = FindDocumentation(subjectId);
            if (documentation == null)
            {
                documentation = new SubjectDocumentationRecord
                {
                    SubjectId = subjectId,
                    SubjectCategory = (int)category,
                };
                _data.Documentation.Add(documentation);
            }
            bool firstRegistration = !documentation.Documented;
            documentation.Documented = true;
            documentation.IsNew |= firstRegistration;
            documentation.CanonicalPhotographId = photographId;
            Save();
        }

        public void ClearNew(string subjectId)
        {
            SubjectDocumentationRecord documentation = FindDocumentation(subjectId);
            if (documentation == null || !documentation.IsNew) return;
            documentation.IsNew = false;
            Save();
        }

        public Texture2D GetCanonicalTexture(string subjectId)
        {
            SubjectDocumentationRecord documentation = FindDocumentation(subjectId);
            return documentation != null ? GetTexture(documentation.CanonicalPhotographId) : null;
        }

        public PhotographRecord GetPhotograph(string photographId)
        {
            if (string.IsNullOrEmpty(photographId)) return null;
            for (int i = 0; i < _data.Photographs.Count; i++)
            {
                if (string.Equals(_data.Photographs[i].PhotographId, photographId, StringComparison.Ordinal))
                {
                    return _data.Photographs[i];
                }
            }
            return null;
        }

        public Texture2D GetTexture(string photographId)
        {
            PhotographRecord record = GetPhotograph(photographId);
            if (record == null || string.IsNullOrEmpty(record.ImagePath) || !File.Exists(record.ImagePath)) return null;
            if (_textureCache.TryGetValue(photographId, out Texture2D cached) && cached != null) return cached;
            byte[] bytes = File.ReadAllBytes(record.ImagePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false)
            {
                name = $"Archived Photograph {photographId}",
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.LoadImage(bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            _textureCache[photographId] = texture;
            return texture;
        }

        public bool Delete(string photographId)
        {
            PhotographRecord record = GetPhotograph(photographId);
            if (record == null) return false;
            string safeRoot = Path.GetFullPath(_imageDirectory) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(record.ImagePath ?? string.Empty);
            if (fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            if (_textureCache.TryGetValue(photographId, out Texture2D texture))
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                _textureCache.Remove(photographId);
            }
            _data.Photographs.Remove(record);
            for (int i = 0; i < _data.Documentation.Count; i++)
            {
                if (string.Equals(_data.Documentation[i].CanonicalPhotographId, photographId, StringComparison.Ordinal))
                {
                    _data.Documentation[i].CanonicalPhotographId = string.Empty;
                }
            }
            Save();
            return true;
        }

        private SubjectDocumentationRecord FindDocumentation(string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId)) return null;
            for (int i = 0; i < _data.Documentation.Count; i++)
            {
                if (string.Equals(_data.Documentation[i].SubjectId, subjectId, StringComparison.Ordinal))
                {
                    return _data.Documentation[i];
                }
            }
            return null;
        }

        private void Load()
        {
            _data = new PhotographySaveData();
            bool metadataLoaded = true;
            if (File.Exists(_savePath))
            {
                try
                {
                    PhotographySaveData loaded = JsonUtility.FromJson<PhotographySaveData>(File.ReadAllText(_savePath));
                    if (loaded != null)
                    {
                        _data = loaded;
                    }
                    else
                    {
                        metadataLoaded = false;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Photography archive could not be loaded: {exception.Message}");
                    metadataLoaded = false;
                }
            }
            _data.Photographs ??= new List<PhotographRecord>();
            _data.Documentation ??= new List<SubjectDocumentationRecord>();
            _data.NextCaptureSequence = Math.Max(1, _data.NextCaptureSequence);
            for (int i = _data.Photographs.Count - 1; i >= 0; i--)
            {
                if (_data.Photographs[i] == null || !File.Exists(_data.Photographs[i].ImagePath))
                {
                    _data.Photographs.RemoveAt(i);
                }
            }
            if (metadataLoaded)
            {
                CleanupOrphans();
            }
            else
            {
                RecoverPhotographsFromImages();
            }
            Save();
        }

        private void RecoverPhotographsFromImages()
        {
            string[] files = Directory.GetFiles(_imageDirectory, "*.jpg", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                long sequence = _data.NextCaptureSequence++;
                _data.Photographs.Add(new PhotographRecord
                {
                    PhotographId = $"recovered-{sequence:00000000}",
                    SubjectId = string.Empty,
                    SubjectCategory = (int)PhotographableSubjectCategory.Glyph,
                    CaptureSequence = sequence,
                    CaptureUtcTicks = File.GetLastWriteTimeUtc(files[i]).Ticks,
                    IsValidSubjectPhotograph = false,
                    ImagePath = files[i],
                });
            }
        }

        private void CleanupOrphans()
        {
            HashSet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _data.Photographs.Count; i++)
            {
                referenced.Add(Path.GetFullPath(_data.Photographs[i].ImagePath));
            }
            string[] files = Directory.GetFiles(_imageDirectory, "*.jpg", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                if (!referenced.Contains(Path.GetFullPath(files[i]))) File.Delete(files[i]);
            }
        }

        private void Save()
        {
            string temporaryPath = _savePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(_data, true));
            File.Copy(temporaryPath, _savePath, true);
            File.Delete(temporaryPath);
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textureCache.Values)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            _textureCache.Clear();
        }
    }

    internal readonly struct PhotographableSubject
    {
        public readonly string SubjectId;
        public readonly string DisplayName;
        public readonly PhotographableSubjectCategory Category;
        public readonly DesertAtlasSiteDefinition AtlasSite;
        public readonly GeoglyphArtworkPlacement Artwork;
        public readonly DuneVectorPhotographableMarker Marker;

        public PhotographableSubject(DesertAtlasSiteDefinition site, GeoglyphArtworkPlacement artwork)
        {
            SubjectId = site.PersistentId;
            DisplayName = site.DisplayName;
            Category = PhotographableSubjectCategory.Glyph;
            AtlasSite = site;
            Artwork = artwork;
            Marker = null;
        }

        public PhotographableSubject(
            DuneVectorPhotographableMarker marker,
            string displayName)
        {
            SubjectId = marker != null ? marker.SubjectId : string.Empty;
            DisplayName = displayName;
            Category = marker != null ? marker.Category : PhotographableSubjectCategory.Misc;
            AtlasSite = null;
            Artwork = null;
            Marker = marker;
        }
    }

    internal readonly struct SubjectDetectionResult
    {
        public readonly bool HasSubject;
        public readonly bool IsValid;
        public readonly PhotographableSubject Subject;
        public readonly Rect ScreenBounds;
        public readonly float ScreenCoverage;
        public readonly float VisiblePercentage;

        public SubjectDetectionResult(bool hasSubject, bool valid, PhotographableSubject subject, Rect bounds, float coverage, float visible)
        {
            HasSubject = hasSubject;
            IsValid = valid;
            Subject = subject;
            ScreenBounds = bounds;
            ScreenCoverage = coverage;
            VisiblePercentage = visible;
        }
    }

    internal sealed class DuneVectorSubjectDetector
    {
        private readonly Camera _camera;
        private readonly DroneCharacterController _character;
        private readonly DesertWorldStreamer _world;
        private readonly GeoglyphSystemTuning _geoglyphs;
        private readonly DesertAtlasTuning _atlas;
        private readonly PhotographyTuning _settings;
        private readonly List<Vector3> _worldSamples = new List<Vector3>(40);
        private int _centerSampleIndex;

        public DuneVectorSubjectDetector(
            Camera camera,
            DroneCharacterController character,
            DesertWorldStreamer world,
            GeoglyphSystemTuning geoglyphs,
            DesertAtlasTuning atlas,
            PhotographyTuning settings)
        {
            _camera = camera;
            _character = character;
            _world = world;
            _geoglyphs = geoglyphs;
            _atlas = atlas;
            _settings = settings;
        }

        public SubjectDetectionResult Detect()
        {
            if (_camera == null)
            {
                return default;
            }

            bool found = false;
            PhotographableSubject bestSubject = default;
            Rect bestBounds = default;
            float bestCenterPriority = float.PositiveInfinity;
            float bestCoverage = -1f;
            bool allowGlyphSubjects = _character == null || !_character.IsStableGrounded;
            if (allowGlyphSubjects &&
                _world != null &&
                _geoglyphs?.Placements != null &&
                _atlas?.Sites != null)
            {
                for (int siteIndex = 0; siteIndex < _atlas.Sites.Count; siteIndex++)
                {
                    DesertAtlasSiteDefinition site = _atlas.Sites[siteIndex];
                    if (site == null || string.IsNullOrWhiteSpace(site.PersistentId)) continue;
                    GeoglyphArtworkPlacement artwork = FindArtwork(site);
                    if (artwork == null) continue;
                    BuildSamples(site, artwork);
                    if (!TryProjectBounds(out Rect bounds, out float coverage, out float priority)) continue;
                    if (!IsBetterCandidate(coverage, priority, bestCoverage, bestCenterPriority)) continue;
                    bestCenterPriority = priority;
                    bestBounds = bounds;
                    bestCoverage = coverage;
                    bestSubject = new PhotographableSubject(site, artwork);
                    found = true;
                }
            }

            foreach (DuneVectorPhotographableMarker marker in DuneVectorPhotographableMarker.ActiveMarkers)
            {
                if (marker == null ||
                    !TryResolveDisplayName(marker.SubjectId, out string displayName) ||
                    !marker.TryGetScreenBounds(
                        _camera,
                        out Rect markerBounds,
                        out float markerCoverage))
                {
                    continue;
                }

                float centerPriority = Vector2.Distance(
                    markerBounds.center,
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) /
                    Mathf.Max(1f, Screen.height);
                if (!IsBetterCandidate(
                        markerCoverage,
                        centerPriority,
                        bestCoverage,
                        bestCenterPriority))
                {
                    continue;
                }
                bestCenterPriority = centerPriority;
                bestBounds = markerBounds;
                bestCoverage = markerCoverage;
                bestSubject = new PhotographableSubject(marker, displayName);
                found = true;
            }
            if (!found) return default;

            bool fullyFramed = bestBounds.xMin >= Screen.width * _settings.ViewportEdgePadding &&
                bestBounds.xMax <= Screen.width * (1f - _settings.ViewportEdgePadding) &&
                bestBounds.yMin >= Screen.height * _settings.ViewportEdgePadding &&
                bestBounds.yMax <= Screen.height * (1f - _settings.ViewportEdgePadding);

            float visiblePercentage;
            bool valid;
            if (bestSubject.Category == PhotographableSubjectCategory.Glyph)
            {
                BuildSamples(bestSubject.AtlasSite, bestSubject.Artwork);
                visiblePercentage = CalculateVisiblePercentage(bestSubject.AtlasSite);
                DesertAtlasSiteDefinition definition = bestSubject.AtlasSite;
                float minimumCoverage = Mathf.Min(
                    definition.MinimumPhotoScreenCoverage,
                    definition.MaximumPhotoScreenCoverage);
                float maximumCoverage = Mathf.Max(
                    definition.MinimumPhotoScreenCoverage,
                    definition.MaximumPhotoScreenCoverage);
                float readableAngle = Vector3.Dot(_camera.transform.forward.normalized, Vector3.down);
                float requiredVisibility = definition.AllowPartialPhotoOcclusion
                    ? definition.RequiredPhotoVisiblePercentage
                    : 1f;
                valid = fullyFramed &&
                    bestCoverage >= minimumCoverage &&
                    bestCoverage <= maximumCoverage &&
                    readableAngle >= definition.MinimumPhotoReadableAngle &&
                    visiblePercentage >= requiredVisibility;
            }
            else
            {
                visiblePercentage = bestSubject.Marker != null
                    ? bestSubject.Marker.CalculateVisiblePercentage(_camera, _settings)
                    : 0f;
                float minimumCoverage = Mathf.Min(
                    _settings.CompendiumMinimumPhotoScreenCoverage,
                    _settings.CompendiumMaximumPhotoScreenCoverage);
                float maximumCoverage = Mathf.Max(
                    _settings.CompendiumMinimumPhotoScreenCoverage,
                    _settings.CompendiumMaximumPhotoScreenCoverage);
                valid = fullyFramed &&
                    bestCoverage >= minimumCoverage &&
                    bestCoverage <= maximumCoverage &&
                    visiblePercentage >= _settings.CompendiumRequiredVisiblePercentage;
            }
            return new SubjectDetectionResult(true, valid, bestSubject, bestBounds, bestCoverage, visiblePercentage);
        }

        private bool TryResolveDisplayName(string subjectId, out string displayName)
        {
            if (_settings?.CompendiumEntries != null)
            {
                for (int i = 0; i < _settings.CompendiumEntries.Count; i++)
                {
                    CompendiumEntryDefinition definition = _settings.CompendiumEntries[i];
                    if (definition != null &&
                        string.Equals(definition.SubjectId, subjectId, StringComparison.Ordinal))
                    {
                        displayName = definition.DisplayName;
                        return true;
                    }
                }
            }
            displayName = string.Empty;
            return false;
        }

        private static bool IsBetterCandidate(
            float coverage,
            float centerPriority,
            float bestCoverage,
            float bestCenterPriority)
        {
            const float coverageTieTolerance = 0.0001f;
            return coverage > bestCoverage + coverageTieTolerance ||
                (Mathf.Abs(coverage - bestCoverage) <= coverageTieTolerance &&
                 centerPriority < bestCenterPriority);
        }

        private GeoglyphArtworkPlacement FindArtwork(DesertAtlasSiteDefinition site)
        {
            GeoglyphArtworkPlacement closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < _geoglyphs.Placements.Count; i++)
            {
                GeoglyphArtworkPlacement candidate = _geoglyphs.Placements[i];
                if (candidate == null) continue;
                float distance = (candidate.WorldCenter - site.WorldPosition).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private void BuildSamples(DesertAtlasSiteDefinition site, GeoglyphArtworkPlacement artwork)
        {
            _worldSamples.Clear();
            Vector2 contentCenter = artwork.MaskContentCenter;
            Vector2 contentScale = artwork.MaskContentSize;
            if (contentScale.x <= 0f || contentScale.y <= 0f)
            {
                contentCenter = new Vector2(0.5f, 0.5f);
                contentScale = Vector2.one;
            }

            Vector2 size = Vector2.Scale(artwork.WorldSize, contentScale) *
                Mathf.Max(0.1f, site.PhotoCaptureRegionScale);
            // The geoglyph shader converts world deltas into artwork space with a
            // clockwise X/Z rotation. Unity's positive Y quaternion uses that same
            // direction for local-to-world, so invert the authored angle here to
            // reconstruct the shader's artwork footprint in world space.
            Quaternion rotation = Quaternion.Euler(0f, -artwork.RotationDegrees, 0f);
            Vector2 normalizedCenterOffset = contentCenter - new Vector2(0.5f, 0.5f);
            Vector3 centerOffset = rotation * new Vector3(
                normalizedCenterOffset.x * artwork.WorldSize.x,
                0f,
                normalizedCenterOffset.y * artwork.WorldSize.y);
            double contentCenterX = artwork.WorldCenter.x + centerOffset.x;
            double contentCenterZ = artwork.WorldCenter.y + centerOffset.z;
            float regionScale = Mathf.Max(0.1f, site.PhotoCaptureRegionScale);
            if (artwork.MaskCaptureBoundary != null && artwork.MaskCaptureBoundary.Count >= 3)
            {
                _centerSampleIndex = 0;
                AddWorldSample(contentCenterX, contentCenterZ);
                for (int i = 0; i < artwork.MaskCaptureBoundary.Count; i++)
                {
                    Vector2 uv = contentCenter +
                        ((artwork.MaskCaptureBoundary[i] - contentCenter) * regionScale);
                    Vector3 offset = rotation * new Vector3(
                        (uv.x - 0.5f) * artwork.WorldSize.x,
                        0f,
                        (uv.y - 0.5f) * artwork.WorldSize.y);
                    AddWorldSample(
                        artwork.WorldCenter.x + offset.x,
                        artwork.WorldCenter.y + offset.z);
                }
                return;
            }

            _centerSampleIndex = 4;
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector3 offset = rotation * new Vector3(size.x * 0.5f * x, 0f, size.y * 0.5f * z);
                    double logicalX = contentCenterX + offset.x;
                    double logicalZ = contentCenterZ + offset.z;
                    AddWorldSample(logicalX, logicalZ);
                }
            }
        }

        private void AddWorldSample(double logicalX, double logicalZ)
        {
            Vector3 local = _world.LogicalToLocal(logicalX, 0f, logicalZ);
            local.y = _world.SampleHeightAtLocal(local.x, local.z) + _settings.CaptureHeightOffset;
            _worldSamples.Add(local);
        }

        private bool TryProjectBounds(out Rect bounds, out float coverage, out float priority)
        {
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int frontSampleCount = 0;
            bool centerInFront = false;
            for (int i = 0; i < _worldSamples.Count; i++)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(_worldSamples[i]);
                if (viewport.z <= _camera.nearClipPlane)
                {
                    continue;
                }

                frontSampleCount++;
                centerInFront |= i == _centerSampleIndex;
                float x = viewport.x * Screen.width;
                float y = (1f - viewport.y) * Screen.height;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
            if (!centerInFront || frontSampleCount < 3)
            {
                bounds = default;
                coverage = 0f;
                priority = float.PositiveInfinity;
                return false;
            }

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            Rect screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Rect intersection = Intersect(bounds, screenRect);
            bool intersects = intersection.width > 0f && intersection.height > 0f;
            coverage = intersects ? (intersection.width * intersection.height) / Mathf.Max(1f, Screen.width * Screen.height) : 0f;
            priority = Vector2.Distance(bounds.center, screenRect.center) / Mathf.Max(1f, Screen.height);
            return intersects;
        }

        private float CalculateVisiblePercentage(DesertAtlasSiteDefinition definition)
        {
            int visible = 0;
            Vector3 origin = _camera.transform.position;
            for (int i = 0; i < _worldSamples.Count; i++)
            {
                Vector3 direction = _worldSamples[i] - origin;
                float distance = direction.magnitude;
                bool blocked = distance > _settings.OcclusionRayEndTolerance &&
                    Physics.Raycast(
                        origin,
                        direction / Mathf.Max(0.001f, distance),
                        distance - _settings.OcclusionRayEndTolerance,
                        _settings.OcclusionLayers,
                        QueryTriggerInteraction.Ignore);
                if (!blocked) visible++;
            }
            return _worldSamples.Count > 0 ? visible / (float)_worldSamples.Count : 0f;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : default;
        }
    }

    internal sealed class DuneVectorGalleryView
    {
        private readonly DuneVectorPhotographStorage _storage;
        private readonly PhotographyTuning _settings;
        private Vector2 _scroll;
        private string _selectedPhotographId;
        private bool _confirmDelete;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _buttonStyle;

        public DuneVectorGalleryView(DuneVectorPhotographStorage storage, PhotographyTuning settings)
        {
            _storage = storage;
            _settings = settings;
        }

        public bool Draw()
        {
            EnsureStyles();
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), _settings.GalleryBackdropColor);
            float scale = Mathf.Clamp(
                Mathf.Min(Screen.width / _settings.GalleryReferenceWidth, Screen.height / _settings.GalleryReferenceHeight),
                Mathf.Min(_settings.GalleryMinimumScale, _settings.GalleryMaximumScale),
                Mathf.Max(_settings.GalleryMinimumScale, _settings.GalleryMaximumScale));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float virtualWidth = Screen.width / scale;
            float virtualHeight = Screen.height / scale;
            Rect panel = new Rect(
                (virtualWidth - _settings.GalleryPanelWidth) * 0.5f,
                (virtualHeight - _settings.GalleryPanelHeight) * 0.5f,
                _settings.GalleryPanelWidth,
                _settings.GalleryPanelHeight);
            DrawRect(panel, _settings.GalleryPanelColor);
            DrawBorder(panel, _settings.GalleryAccentColor, _settings.FrameThickness);
            if (!string.IsNullOrEmpty(_selectedPhotographId))
            {
                DrawViewer(panel);
            }
            else
            {
                DrawGrid(panel);
            }
            GUI.matrix = previous;
            return false;
        }

        private void DrawGrid(Rect panel)
        {
            float padding = _settings.GalleryPadding;
            GUI.Label(new Rect(panel.x + padding, panel.y + padding, panel.width - (padding * 2f), _settings.GalleryHeaderHeight),
                string.Format(_settings.GalleryCountFormat, _settings.GalleryTitle, _storage.Photographs.Count), _titleStyle);
            if (GUI.Button(new Rect(panel.xMax - padding - _settings.GalleryThumbnailWidth, panel.y + padding,
                    _settings.GalleryThumbnailWidth, _settings.GalleryButtonHeight), _settings.GalleryDoneButton, _buttonStyle))
            {
                DuneVectorPhotographySystem.RequestCloseGallery();
            }
            Rect viewport = new Rect(panel.x + padding, panel.y + padding + _settings.GalleryHeaderHeight,
                panel.width - (padding * 2f), panel.height - (padding * 2f) - _settings.GalleryHeaderHeight);
            if (_storage.Photographs.Count == 0)
            {
                GUI.Label(viewport, _settings.GalleryEmptyText, _bodyStyle);
                return;
            }
            int columns = Mathf.Max(2, _settings.GalleryColumns);
            float cellWidth = (viewport.width - ((columns - 1) * _settings.GalleryGap)) / columns;
            float imageHeight = cellWidth * (_settings.GalleryThumbnailHeight / Mathf.Max(1f, _settings.GalleryThumbnailWidth));
            float cellHeight = imageHeight + _settings.SubjectLabelHeight + _settings.GalleryGap;
            int rows = Mathf.CeilToInt(_storage.Photographs.Count / (float)columns);
            Rect content = new Rect(0f, 0f, viewport.width - _settings.GalleryGap, rows * cellHeight);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content);
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / Mathf.Max(1f, cellHeight)) - 1);
            int lastVisibleRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_scroll.y + viewport.height) / Mathf.Max(1f, cellHeight)) + 1);
            for (int row = firstVisibleRow; row <= lastVisibleRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int displayIndex = (row * columns) + column;
                    if (displayIndex >= _storage.Photographs.Count) break;
                    int photographIndex = _storage.Photographs.Count - 1 - displayIndex;
                    PhotographRecord record = _storage.Photographs[photographIndex];
                    Rect cell = new Rect(column * (cellWidth + _settings.GalleryGap), row * cellHeight, cellWidth, imageHeight);
                    Texture2D texture = _storage.GetTexture(record.PhotographId);
                    if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                    {
                        _selectedPhotographId = record.PhotographId;
                        _confirmDelete = false;
                    }
                    Color cellBorder = cell.Contains(Event.current.mousePosition)
                        ? _settings.GallerySelectionColor
                        : _settings.GalleryAccentColor;
                    DrawBorder(cell, cellBorder, _settings.FrameThickness);
                    if (texture != null) GUI.DrawTexture(cell, texture, ScaleMode.ScaleToFit, false);
                    string label = record.IsValidSubjectPhotograph
                        ? _settings.GalleryDocumentedLabel
                        : string.Format(_settings.GalleryPhotoLabelFormat, record.CaptureSequence);
                    GUI.Label(new Rect(cell.x, cell.yMax, cell.width, _settings.SubjectLabelHeight), label, _bodyStyle);
                }
            }
            GUI.EndScrollView();
        }

        private void DrawViewer(Rect panel)
        {
            PhotographRecord record = _storage.GetPhotograph(_selectedPhotographId);
            if (record == null)
            {
                _selectedPhotographId = null;
                return;
            }
            float padding = _settings.GalleryPadding;
            Texture2D texture = _storage.GetTexture(record.PhotographId);
            Rect imageRect = new Rect(panel.x + padding, panel.y + padding,
                panel.width - (padding * 2f), panel.height - (padding * 3f) - _settings.GalleryButtonHeight);
            if (texture != null) GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, false);
            float buttonWidth = (imageRect.width - _settings.GalleryGap) * 0.5f;
            Rect done = new Rect(imageRect.x, imageRect.yMax + padding, buttonWidth, _settings.GalleryButtonHeight);
            Rect delete = new Rect(done.xMax + _settings.GalleryGap, done.y, buttonWidth, _settings.GalleryButtonHeight);
            if (!_confirmDelete)
            {
                if (GUI.Button(done, _settings.GalleryDoneButton, _buttonStyle)) _selectedPhotographId = null;
                Color previous = GUI.color;
                GUI.color = _settings.GalleryDangerColor;
                if (GUI.Button(delete, _settings.GalleryDeleteButton, _buttonStyle)) _confirmDelete = true;
                GUI.color = previous;
                return;
            }
            Rect confirmation = new Rect(panel.center.x - (_settings.IdentificationPanelWidth * 0.5f),
                panel.center.y - (_settings.IdentificationPanelHeight * 0.5f),
                _settings.IdentificationPanelWidth, _settings.IdentificationPanelHeight);
            DrawRect(confirmation, _settings.IdentificationPanelColor);
            DrawBorder(confirmation, _settings.GalleryDangerColor, _settings.FrameThickness);
            GUI.Label(new Rect(confirmation.x + padding, confirmation.y + padding,
                confirmation.width - (padding * 2f), _settings.SubjectLabelHeight * 2f), _settings.DeleteConfirmation, _bodyStyle);
            Rect confirmDelete = new Rect(confirmation.x + padding, confirmation.yMax - padding - _settings.GalleryButtonHeight,
                (confirmation.width - (padding * 3f)) * 0.5f, _settings.GalleryButtonHeight);
            Rect cancel = new Rect(confirmDelete.xMax + padding, confirmDelete.y, confirmDelete.width, confirmDelete.height);
            if (GUI.Button(confirmDelete, _settings.GalleryDeleteButton, _buttonStyle))
            {
                _storage.Delete(record.PhotographId);
                _selectedPhotographId = null;
                _confirmDelete = false;
            }
            if (GUI.Button(cancel, _settings.DeleteCancelButton, _buttonStyle)) _confirmDelete = false;
        }

        private void EnsureStyles()
        {
            _titleStyle ??= CreateStyle(_settings.GalleryTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.GalleryTextColor);
            _bodyStyle ??= CreateStyle(_settings.GalleryBodyFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.GalleryTextColor);
            _buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = _settings.GalleryBodyFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private static GUIStyle CreateStyle(int size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = anchor,
                normal = { textColor = color },
            };
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }

    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorPhotographySystem : MonoBehaviour
    {
        private enum CameraPresentationState
        {
            Live,
            Identified,
            ReplacePrompt,
        }

        public static DuneVectorPhotographySystem Active { get; private set; }
        public static bool IsCameraModeActive => Active != null && Active._cameraModeActive;
        public static bool RequiresGlyphDocumentation => Active != null && Active._settings != null && Active._settings.Enabled;
        public PhotographyTuning Tuning => _settings;
        private static bool _closeGalleryRequested;
        private static bool _closeCompendiumRequested;

        private DronePlayer _player;
        private DroneCameraController _cameraController;
        private Camera _camera;
        private PhotographyTuning _settings;
        private DuneVectorPhotographStorage _storage;
        private DuneVectorSubjectDetector _detector;
        private DuneVectorGalleryView _gallery;
        private DuneVectorCompendiumView _compendium;
        private SubjectDetectionResult _detection;
        private Color _animatedAccentColor;
        private Rect _animatedBounds;
        private bool _hasAnimatedBounds;
        private bool _previousHasSubject;
        private float _targetStateBlend;
        private bool _cameraModeActive;
        private float _baseFieldOfView;
        private float _zoom = 1f;
        private float _targetZoom = 1f;
        private float _nextValidationTime;
        private float _hudEnteredAt;
        private float _targetAcquiredAt;
        private float _lastZoomInputAt;
        private float _captureStartedAt;
        private float _captureHoldUntil;
        private float _shutterUntil;
        private float _hudScale = 1f;
        private float _hudWidth;
        private float _hudHeight;
        private float _presentationUntil;
        private float _timeScaleBeforeIdentification = 1f;
        private bool _identificationPauseActive;
        private CameraPresentationState _presentationState;
        private Texture2D _capturedTexture;
        private Texture2D _captureHoldTexture;
        private FilmGrain _cameraFilmGrain;
        private bool _cameraFilmGrainApplied;
        private bool _filmGrainActiveBeforeCamera;
        private bool _filmGrainTypeOverrideBeforeCamera;
        private FilmGrainLookup _filmGrainTypeBeforeCamera;
        private bool _filmGrainIntensityOverrideBeforeCamera;
        private float _filmGrainIntensityBeforeCamera;
        private bool _filmGrainResponseOverrideBeforeCamera;
        private float _filmGrainResponseBeforeCamera;
        private bool _filmGrainTextureOverrideBeforeCamera;
        private Texture _filmGrainTextureBeforeCamera;
        private PhotographRecord _pendingPhotograph;
        private PhotographableSubject _pendingSubject;
        private GUIStyle _subjectStyle;
        private GUIStyle _targetStatusStyle;
        private GUIStyle _modeLabelStyle;
        private GUIStyle _metadataStyle;
        private GUIStyle _metadataRightStyle;
        private GUIStyle _commandStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _comparisonLabelStyle;
        private GUIStyle _identificationTitleStyle;
        private GUIStyle _identificationNameStyle;
        private GUIStyle _buttonStyle;
        private readonly List<Renderer> _hiddenPlayerRenderers = new List<Renderer>();
        private readonly List<bool> _hiddenPlayerRendererStates = new List<bool>();

        public void Initialize(
            DronePlayer player,
            DroneCameraController cameraController,
            DesertWorldStreamer world,
            GeoglyphSystemTuning geoglyphs,
            DesertAtlasTuning atlas,
            PhotographyTuning settings)
        {
            _player = player;
            _cameraController = cameraController;
            _camera = cameraController != null ? cameraController.Camera : null;
            _settings = settings ?? new PhotographyTuning();
            _storage = new DuneVectorPhotographStorage(_settings);
            _detector = new DuneVectorSubjectDetector(
                _camera,
                player != null ? player.Character : null,
                world,
                geoglyphs,
                atlas,
                _settings);
            _gallery = new DuneVectorGalleryView(_storage, _settings);
            _compendium = new DuneVectorCompendiumView(_storage, _settings, atlas);
            Active = this;
        }

        public static bool IsGlyphDocumented(string glyphId)
        {
            return Active != null && Active._storage != null && Active._storage.IsDocumented(glyphId);
        }

        public static bool IsGlyphNew(string glyphId)
        {
            return Active != null && Active._storage != null && Active._storage.IsNew(glyphId);
        }

        public static bool HasNewGlyphs => Active != null && Active._storage != null && Active._storage.HasNewDocumentation;
        public static int DocumentedGlyphCount => Active != null && Active._storage != null
            ? Active._storage.DocumentedGlyphCount
            : 0;

        public static Texture2D GetGlyphAtlasTexture(string glyphId)
        {
            return Active != null && Active._storage != null ? Active._storage.GetCanonicalTexture(glyphId) : null;
        }

        public static void MarkGlyphViewed(string glyphId)
        {
            Active?._storage?.ClearNew(glyphId);
        }

        public static void RequestCloseGallery()
        {
            _closeGalleryRequested = true;
        }

        public bool DrawGallery()
        {
            _closeGalleryRequested = false;
            _gallery?.Draw();
            return _closeGalleryRequested;
        }

        public static void RequestCloseCompendium()
        {
            _closeCompendiumRequested = true;
        }

        public bool DrawCompendium()
        {
            _closeCompendiumRequested = false;
            _compendium?.Draw();
            return _closeCompendiumRequested;
        }

        private void Update()
        {
            if (_settings == null || !_settings.Enabled || _camera == null || _player == null) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            bool paused = DuneVectorBootstrap.Instance != null && DuneVectorBootstrap.Instance.PauseMenu != null &&
                DuneVectorBootstrap.Instance.PauseMenu.IsPaused;
            if (!_cameraModeActive)
            {
                if (!paused && mouse != null && mouse.rightButton.wasPressedThisFrame &&
                    !DuneVectorCourierGame.IsGameplayHudSuppressed)
                {
                    EnterCameraMode();
                }
                return;
            }

            if (_captureHoldTexture != null && Time.unscaledTime >= _captureHoldUntil)
            {
                ReleaseCaptureHoldTexture();
            }

            if (_presentationState == CameraPresentationState.Live &&
                ((mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                 (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)))
            {
                ExitCameraMode();
                return;
            }
            if (_presentationState != CameraPresentationState.Live)
            {
                if (_presentationState == CameraPresentationState.Identified && Time.unscaledTime >= _presentationUntil)
                {
                    ReturnToLiveCamera();
                }
                return;
            }

            Vector2 look = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f;
            _cameraController.UpdateWithInput(Time.unscaledDeltaTime, look, 0f);
            _player.SetDisabledMovementRotation(_camera.transform.rotation);
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _lastZoomInputAt = Time.unscaledTime;
            }
            float minimumZoom = Mathf.Clamp(_settings.MinimumZoom, 0.25f, 1f);
            _targetZoom = Mathf.Clamp(
                _targetZoom + (scroll * _settings.ZoomStep),
                minimumZoom,
                Mathf.Max(1f, _settings.MaximumZoom));
            _zoom = Mathf.Lerp(_zoom, _targetZoom, DuneVectorMath.Sharpness(_settings.ZoomSharpness, Time.unscaledDeltaTime));
            _camera.fieldOfView = Mathf.Clamp(_baseFieldOfView / Mathf.Max(0.01f, _zoom), 1f, 179f);

            if (Time.unscaledTime >= _nextValidationTime)
            {
                _nextValidationTime = Time.unscaledTime + Mathf.Max(0.01f, _settings.ValidationInterval);
                _detection = _detector.Detect();
                if (_detection.HasSubject && !_previousHasSubject)
                {
                    _targetAcquiredAt = Time.unscaledTime;
                    _hasAnimatedBounds = false;
                }
                _previousHasSubject = _detection.HasSubject;
            }

            float targetState = _detection.HasSubject && _detection.IsValid ? 1f : 0f;
            _targetStateBlend = Mathf.Lerp(
                _targetStateBlend,
                targetState,
                DuneVectorMath.Sharpness(_settings.TargetStateSharpness, Time.unscaledDeltaTime));
            Color targetAccent = !_detection.HasSubject
                ? _settings.NeutralColor
                : Color.Lerp(_settings.InvalidColor, _settings.ValidColor, _targetStateBlend);
            _animatedAccentColor = Color.Lerp(
                _animatedAccentColor,
                targetAccent,
                DuneVectorMath.Sharpness(_settings.AccentColorSharpness, Time.unscaledDeltaTime));
            if (_detection.HasSubject)
            {
                float hudScale = GetHudScale();
                Rect padded = ClampToViewfinder(
                    Expand(_detection.ScreenBounds, _settings.TargetBracketPadding * hudScale));
                float blend = DuneVectorMath.Sharpness(_settings.BracketSharpness, Time.unscaledDeltaTime);
                _animatedBounds = _hasAnimatedBounds ? Lerp(_animatedBounds, padded, blend) : padded;
                _hasAnimatedBounds = true;
            }
            else
            {
                _hasAnimatedBounds = false;
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                CapturePhotograph();
            }
        }

        private void EnterCameraMode()
        {
            _cameraModeActive = true;
            _presentationState = CameraPresentationState.Live;
            _baseFieldOfView = _camera.fieldOfView;
            _zoom = _targetZoom = 1f;
            _player.SetInputEnabled(false);
            _player.SetDisabledFlightStopEnabled(true);
            _cameraController.SetPhotographyMode(true, _settings.CameraDistance, _settings.CameraHeight);
            HidePlayerRenderers();
            EnableCameraFilmGrain();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _detection = default;
            _animatedAccentColor = _settings.NeutralColor;
            _targetStateBlend = 0f;
            _previousHasSubject = false;
            _hasAnimatedBounds = false;
            _hudEnteredAt = Time.unscaledTime;
            _lastZoomInputAt = float.NegativeInfinity;
            _nextValidationTime = 0f;
        }

        private void ExitCameraMode()
        {
            EndIdentificationPause();
            _cameraModeActive = false;
            _camera.fieldOfView = _baseFieldOfView;
            _cameraController.SetPhotographyMode(false, _settings.CameraDistance, _settings.CameraHeight);
            RestorePlayerRenderers();
            RestoreCameraFilmGrain();
            _player.SetDisabledFlightStopEnabled(false);
            _player.SetInputEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            ReleaseCapturedTexture();
            ReleaseCaptureHoldTexture();
            _pendingPhotograph = null;
            _detection = default;
        }

        private void CapturePhotograph()
        {
            Texture2D image = CaptureCameraImage();
            if (image == null) return;
            ReleaseCaptureHoldTexture();
            bool valid = _detection.HasSubject && _detection.IsValid;
            string subjectId = valid ? _detection.Subject.SubjectId : string.Empty;
            PhotographableSubjectCategory category = valid
                ? _detection.Subject.Category
                : PhotographableSubjectCategory.Glyph;
            PhotographRecord record = _storage.Store(image, subjectId, category, valid);
            _captureStartedAt = Time.unscaledTime;
            _shutterUntil = Time.unscaledTime + _settings.ShutterFlashDuration;
            _captureHoldUntil = Time.unscaledTime + _settings.CaptureHoldDuration;
            if (!valid)
            {
                _captureHoldTexture = image;
                return;
            }

            ReleaseCapturedTexture();
            _capturedTexture = image;
            _pendingPhotograph = record;
            _pendingSubject = _detection.Subject;
            if (!_storage.IsDocumented(subjectId))
            {
                _storage.Document(subjectId, category, record.PhotographId);
                if (category == PhotographableSubjectCategory.Glyph)
                {
                    DuneVectorDesertAtlas.TryCatalogPhotographedGlyph(subjectId);
                }
                _presentationState = CameraPresentationState.Identified;
                _presentationUntil = Time.unscaledTime + _settings.IdentificationHoldDuration;
                BeginIdentificationPause();
            }
            else
            {
                _presentationState = CameraPresentationState.ReplacePrompt;
                BeginIdentificationPause();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private Texture2D CaptureCameraImage()
        {
            int width = Mathf.Max(320, _settings.ImageWidth);
            int height = Mathf.Max(180, _settings.ImageHeight);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = _camera.targetTexture;
            Texture2D image = null;
            try
            {
                _camera.targetTexture = target;
                _camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                image.Apply(false, false);
                return image;
            }
            catch (Exception exception)
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                Debug.LogWarning($"Photograph capture failed: {exception.Message}");
                return null;
            }
            finally
            {
                _camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void ReturnToLiveCamera()
        {
            EndIdentificationPause();
            _presentationState = CameraPresentationState.Live;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            ReleaseCapturedTexture();
            _pendingPhotograph = null;
        }

        private void BeginIdentificationPause()
        {
            if (_identificationPauseActive) return;
            _timeScaleBeforeIdentification = Time.timeScale;
            _identificationPauseActive = true;
            Time.timeScale = 0f;
        }

        private void EndIdentificationPause()
        {
            if (!_identificationPauseActive) return;
            Time.timeScale = _timeScaleBeforeIdentification;
            _identificationPauseActive = false;
        }

        private void OnGUI()
        {
            if (!_cameraModeActive || _settings == null) return;
            GUI.depth = -2000;
            EnsureStyles();
            _hudScale = GetHudScale();
            _hudWidth = Screen.width / _hudScale;
            _hudHeight = Screen.height / _hudScale;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_hudScale, _hudScale, 1f));
            try
            {
                if (_presentationState == CameraPresentationState.Live)
                {
                    if (_captureHoldTexture != null && Time.unscaledTime < _captureHoldUntil)
                    {
                        GUI.DrawTexture(
                            new Rect(0f, 0f, _hudWidth, _hudHeight),
                            _captureHoldTexture,
                            ScaleMode.ScaleAndCrop,
                            false);
                    }
                    DrawSurfaceTextures();
                    DrawViewfinder();
                }
                else
                {
                    DrawCapturePresentation();
                }
                if (Time.unscaledTime < _shutterUntil)
                {
                    float duration = Mathf.Max(0.01f, _settings.ShutterFlashDuration);
                    float normalizedAge = Mathf.Clamp01((Time.unscaledTime - _captureStartedAt) / duration);
                    float alpha = Mathf.Sin(normalizedAge * Mathf.PI) * _settings.CaptureFlashOpacity;
                    Color flash = _settings.ShutterFlashColor;
                    flash.a *= alpha;
                    Rect screen = new Rect(0f, 0f, _hudWidth, _hudHeight);
                    if (_settings.CaptureFlashTexture != null)
                    {
                        DrawTexture(screen, _settings.CaptureFlashTexture, flash, ScaleMode.StretchToFill);
                    }
                    else
                    {
                        DrawRect(screen, flash);
                    }
                }
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawViewfinder()
        {
            float enter = EaseOutCubic(Mathf.Clamp01(
                (Time.unscaledTime - _hudEnteredAt) / Mathf.Max(0.01f, _settings.HudEnterDuration)));
            float slide = (1f - enter) * _settings.HudEnterSlideDistance;
            Color outerColor = WithAlpha(
                _settings.NeutralColor,
                _settings.NeutralColor.a * _settings.OuterFrameOpacity * enter);
            Rect frame = new Rect(
                _settings.ScreenMargin - slide,
                _settings.ScreenMargin - slide,
                _hudWidth - ((_settings.ScreenMargin - slide) * 2f),
                _hudHeight - ((_settings.ScreenMargin - slide) * 2f));
            DrawCorners(frame, _settings.FrameCornerLength, _settings.FrameThickness, outerColor);
            Rect crosshair = new Rect(_hudWidth * 0.5f - (_settings.CrosshairSize * 0.5f),
                _hudHeight * 0.5f - (_settings.CrosshairSize * 0.5f), _settings.CrosshairSize, _settings.CrosshairSize);
            DrawCrosshair(crosshair, WithAlpha(
                _settings.NeutralColor,
                _settings.NeutralColor.a * _settings.CrosshairOpacity * enter));

            float recoil = 0f;
            if (Time.unscaledTime < _shutterUntil)
            {
                float recoilProgress = Mathf.Clamp01(
                    (Time.unscaledTime - _captureStartedAt) / Mathf.Max(0.01f, _settings.ShutterFlashDuration));
                recoil = Mathf.Sin(recoilProgress * Mathf.PI) * _settings.CaptureUiRecoil;
            }

            if (_detection.HasSubject && _hasAnimatedBounds)
            {
                Rect animatedBounds = ToHudRect(_animatedBounds);
                float acquire = EaseOutCubic(Mathf.Clamp01(
                    (Time.unscaledTime - _targetAcquiredAt) / Mathf.Max(0.01f, _settings.TargetAcquireDuration)));
                float stateOffset = Mathf.Lerp(
                    _settings.InvalidBracketExpansion,
                    -_settings.ValidBracketInset,
                    _targetStateBlend);
                float acquisitionOffset = (1f - acquire) * _settings.TargetAcquireExpansion;
                Rect targetBounds = Expand(animatedBounds, stateOffset + acquisitionOffset - recoil);
                Color accent = WithAlpha(_animatedAccentColor, _animatedAccentColor.a * acquire * enter);
                DrawCorners(targetBounds, _settings.TargetBracketLength, _settings.TargetBracketThickness, accent);
                if (!_detection.IsValid)
                {
                    DrawFramingGuides(ToHudRect(_detection.ScreenBounds), frame, accent);
                }
                string subjectLabel = _storage.IsDocumented(_detection.Subject.SubjectId)
                    ? _detection.Subject.DisplayName
                    : _settings.UnknownSubjectLabel;
                float labelLeft = Mathf.Clamp(
                    targetBounds.x,
                    _settings.ScreenMargin,
                    _hudWidth - _settings.ScreenMargin - _settings.SubjectLabelWidth);
                float labelBlockHeight = _settings.SubjectLabelHeight + _settings.TargetStatusHeight;
                float labelTop = targetBounds.y - _settings.TargetLabelGap - labelBlockHeight;
                if (labelTop < _settings.ScreenMargin + _settings.SubjectLabelHeight)
                {
                    labelTop = targetBounds.yMax + _settings.TargetLabelGap;
                }
                labelTop = Mathf.Clamp(
                    labelTop,
                    _settings.ScreenMargin + _settings.SubjectLabelHeight,
                    _hudHeight - _settings.ScreenMargin - labelBlockHeight - _settings.CommandBarHeight);
                DrawRect(
                    new Rect(
                        labelLeft,
                        labelTop - _settings.FrameThickness,
                        _settings.TargetBracketLength,
                        _settings.FrameThickness),
                    accent);
                DrawLabel(
                    new Rect(labelLeft, labelTop, _settings.SubjectLabelWidth, _settings.SubjectLabelHeight),
                    subjectLabel,
                    _subjectStyle,
                    WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * acquire * enter),
                    true);
                string targetStatus = _detection.IsValid ? _settings.ValidStatus : _settings.InvalidStatus;
                DrawLabel(
                    new Rect(
                        labelLeft,
                        labelTop + _settings.SubjectLabelHeight,
                        _settings.SubjectLabelWidth,
                        _settings.TargetStatusHeight),
                    TrackText(targetStatus),
                    _targetStatusStyle,
                    accent,
                    true);
            }

            DrawLabel(
                new Rect(
                    _settings.ScreenMargin,
                    _settings.ScreenMargin + slide,
                    _hudWidth - (_settings.ScreenMargin * 2f),
                    _settings.SubjectLabelHeight),
                TrackText(_settings.CameraTitle),
                _modeLabelStyle,
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * enter),
                true);

            float bottomY = _hudHeight - _settings.ScreenMargin -
                _settings.CommandBarHeight - _settings.BottomInterfaceOffset;
            DrawCommandBar(bottomY, enter);
            DrawCornerMetadata(bottomY, enter);
        }

        private void DrawCapturePresentation()
        {
            DrawRect(new Rect(0f, 0f, _hudWidth, _hudHeight), _settings.GalleryBackdropColor);
            Rect imageRect = new Rect(0f, 0f, _hudWidth, _hudHeight);
            if (_capturedTexture != null) GUI.DrawTexture(imageRect, _capturedTexture, ScaleMode.ScaleAndCrop, false);
            DrawSurfaceTextures();
            if (_presentationState == CameraPresentationState.Identified)
            {
                float toastProgress = EaseOutCubic(Mathf.Clamp01(
                    1f - ((_presentationUntil - Time.unscaledTime) /
                        Mathf.Max(0.01f, _settings.IdentificationHoldDuration))));
                float toastWidth = Mathf.Min(
                    _settings.DocumentationToastWidth,
                    _hudWidth - (_settings.ScreenMargin * 2f));
                Rect toast = new Rect(
                    (_hudWidth - toastWidth) * 0.5f,
                    _hudHeight - _settings.DocumentationToastBottomOffset -
                        _settings.DocumentationToastHeight +
                        ((1f - toastProgress) * _settings.HudEnterSlideDistance),
                    toastWidth,
                    _settings.DocumentationToastHeight);
                DrawRect(toast, WithAlpha(
                    _settings.CommandBackdropColor,
                    _settings.CommandBackdropColor.a * toastProgress));
                DrawRect(
                    new Rect(toast.x, toast.y, toast.width, _settings.FrameThickness),
                    WithAlpha(_settings.ValidColor, _settings.ValidColor.a * toastProgress));
                DrawLabel(
                    new Rect(
                        toast.x + _settings.TargetBracketLength,
                        toast.y + _settings.TargetLabelGap,
                        toast.width - (_settings.TargetBracketLength * 2f),
                        _settings.SubjectLabelHeight),
                    TrackText(_settings.RegisteredText),
                    _targetStatusStyle,
                    WithAlpha(_settings.ValidColor, _settings.ValidColor.a * toastProgress),
                    true);
                DrawLabel(
                    new Rect(
                        toast.x + _settings.TargetBracketLength,
                        toast.y + _settings.SubjectLabelHeight,
                        toast.width - (_settings.TargetBracketLength * 2f),
                        _settings.SubjectLabelHeight),
                    _pendingSubject.DisplayName,
                    _subjectStyle,
                    WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * toastProgress),
                    true);
                return;
            }

            if (_presentationState == CameraPresentationState.ReplacePrompt)
            {
                DrawPhotographComparison();
            }
            Rect panel = new Rect((_hudWidth - _settings.IdentificationPanelWidth) * 0.5f,
                _hudHeight - _settings.IdentificationPanelHeight - _settings.ScreenMargin,
                _settings.IdentificationPanelWidth, _settings.IdentificationPanelHeight);
            DrawRect(panel, _settings.IdentificationPanelColor);
            DrawBorder(panel, _settings.ValidColor, _settings.FrameThickness);
            float padding = _settings.GalleryPadding;
            string heading = _presentationState == CameraPresentationState.ReplacePrompt
                ? _settings.AlreadyDocumentedText
                : _settings.IdentifiedTitle;
            GUI.Label(new Rect(panel.x + padding, panel.y + padding, panel.width - (padding * 2f),
                _settings.SubjectLabelHeight), heading, _identificationTitleStyle);
            GUI.Label(new Rect(panel.x + padding, panel.y + padding + _settings.SubjectLabelHeight,
                panel.width - (padding * 2f), _settings.SubjectLabelHeight), _pendingSubject.DisplayName, _identificationNameStyle);
            GUI.Label(new Rect(panel.x + padding, panel.y + padding + (_settings.SubjectLabelHeight * 2f),
                panel.width - (padding * 2f), _settings.SubjectLabelHeight), _settings.ReplacePrompt, _statusStyle);
            float buttonWidth = (panel.width - (padding * 3f)) * 0.5f;
            Rect replace = new Rect(panel.x + padding, panel.yMax - padding - _settings.GalleryButtonHeight,
                buttonWidth, _settings.GalleryButtonHeight);
            Rect keep = new Rect(replace.xMax + padding, replace.y, buttonWidth, replace.height);
            if (GUI.Button(replace, _settings.ReplaceButton, _buttonStyle))
            {
                _storage.Document(_pendingSubject.SubjectId, _pendingSubject.Category, _pendingPhotograph.PhotographId);
                ReturnToLiveCamera();
            }
            if (GUI.Button(keep, _settings.KeepButton, _buttonStyle)) ReturnToLiveCamera();
        }

        private void DrawPhotographComparison()
        {
            float cardWidth = _settings.ComparisonImageWidth + (_settings.ComparisonCardPadding * 2f);
            float cardHeight = _settings.ComparisonImageHeight + _settings.ComparisonLabelHeight +
                (_settings.ComparisonCardPadding * 3f);
            float totalWidth = (cardWidth * 2f) + _settings.ComparisonImageGap;
            float left = (_hudWidth - totalWidth) * 0.5f;
            float top = _settings.ScreenMargin;
            Rect currentCard = new Rect(left, top, cardWidth, cardHeight);
            Rect newCard = new Rect(currentCard.xMax + _settings.ComparisonImageGap, top, cardWidth, cardHeight);
            DrawComparisonCard(currentCard, _storage.GetCanonicalTexture(_pendingSubject.SubjectId),
                _settings.ComparisonCurrentLabel, _settings.NeutralColor);
            DrawComparisonCard(newCard, _capturedTexture, _settings.ComparisonNewLabel, _settings.ValidColor);
        }

        private void DrawComparisonCard(Rect card, Texture texture, string label, Color accent)
        {
            DrawRect(card, _settings.ComparisonCardColor);
            DrawBorder(card, accent, _settings.FrameThickness);

            float padding = _settings.ComparisonCardPadding;
            Rect imageRect = new Rect(
                card.x + padding,
                card.y + padding,
                _settings.ComparisonImageWidth,
                _settings.ComparisonImageHeight);
            DrawRect(imageRect, _settings.GalleryBackdropColor);
            DrawBorder(imageRect, accent, _settings.FrameThickness);
            if (texture != null) GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, false);

            Rect labelRect = new Rect(
                card.x + padding,
                imageRect.yMax + padding,
                _settings.ComparisonImageWidth,
                _settings.ComparisonLabelHeight);
            DrawRect(labelRect, _settings.ComparisonLabelPanelColor);
            DrawBorder(labelRect, accent, _settings.FrameThickness);
            GUI.Label(labelRect, label, _comparisonLabelStyle);
        }

        private void DrawSurfaceTextures()
        {
            if (!_settings.SurfaceTexturesEnabled) return;
            Rect screen = new Rect(0f, 0f, _hudWidth, _hudHeight);
            if (!_cameraFilmGrainApplied &&
                _settings.FilmGrainTexture != null &&
                _settings.FilmGrainOpacity > 0f)
            {
                DrawTexture(
                    screen,
                    _settings.FilmGrainTexture,
                    new Color(1f, 1f, 1f, _settings.FilmGrainOpacity),
                    ScaleMode.StretchToFill);
            }
            if (_settings.LensGlassTexture != null && _settings.LensGlassOpacity > 0f)
            {
                DrawTexture(
                    screen,
                    _settings.LensGlassTexture,
                    new Color(1f, 1f, 1f, _settings.LensGlassOpacity),
                    ScaleMode.StretchToFill);
            }
            if (_settings.VignetteTexture != null && _settings.VignetteOpacity > 0f)
            {
                DrawTexture(
                    screen,
                    _settings.VignetteTexture,
                    new Color(1f, 1f, 1f, _settings.VignetteOpacity),
                    ScaleMode.StretchToFill);
            }
        }

        private void EnableCameraFilmGrain()
        {
            RestoreCameraFilmGrain();
            if (!_settings.UseHdrpFilmGrain) return;

            Volume selectedVolume = null;
            float selectedPriority = float.NegativeInfinity;
            Volume[] volumes = FindObjectsByType<Volume>();
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume candidate = volumes[i];
                if (candidate == null ||
                    !candidate.enabled ||
                    !candidate.isGlobal ||
                    candidate.weight <= 0f ||
                    candidate.sharedProfile == null ||
                    candidate.priority < selectedPriority ||
                    !candidate.sharedProfile.TryGet(out FilmGrain _))
                {
                    continue;
                }
                selectedVolume = candidate;
                selectedPriority = candidate.priority;
            }
            if (selectedVolume == null ||
                selectedVolume.profile == null ||
                !selectedVolume.profile.TryGet(out _cameraFilmGrain))
            {
                return;
            }

            _filmGrainActiveBeforeCamera = _cameraFilmGrain.active;
            _filmGrainTypeOverrideBeforeCamera = _cameraFilmGrain.type.overrideState;
            _filmGrainTypeBeforeCamera = _cameraFilmGrain.type.value;
            _filmGrainIntensityOverrideBeforeCamera = _cameraFilmGrain.intensity.overrideState;
            _filmGrainIntensityBeforeCamera = _cameraFilmGrain.intensity.value;
            _filmGrainResponseOverrideBeforeCamera = _cameraFilmGrain.response.overrideState;
            _filmGrainResponseBeforeCamera = _cameraFilmGrain.response.value;
            _filmGrainTextureOverrideBeforeCamera = _cameraFilmGrain.texture.overrideState;
            _filmGrainTextureBeforeCamera = _cameraFilmGrain.texture.value;

            _cameraFilmGrain.active = true;
            _cameraFilmGrain.type.overrideState = true;
            bool useCustomTexture =
                _settings.UseCustomFilmGrainTexture &&
                _settings.FilmGrainTexture != null;
            _cameraFilmGrain.type.value = useCustomTexture
                ? FilmGrainLookup.Custom
                : (FilmGrainLookup)Mathf.Clamp(
                    (int)_settings.FilmGrainPreset,
                    (int)FilmGrainLookup.Thin1,
                    (int)FilmGrainLookup.Large02);
            _cameraFilmGrain.texture.overrideState = useCustomTexture;
            if (useCustomTexture)
            {
                _cameraFilmGrain.texture.value = _settings.FilmGrainTexture;
            }
            _cameraFilmGrain.intensity.overrideState = true;
            _cameraFilmGrain.intensity.value = Mathf.Clamp01(_settings.HdrpFilmGrainIntensity);
            _cameraFilmGrain.response.overrideState = true;
            _cameraFilmGrain.response.value = Mathf.Clamp01(_settings.HdrpFilmGrainResponse);
            _cameraFilmGrainApplied = true;
        }

        private void RestoreCameraFilmGrain()
        {
            if (!_cameraFilmGrainApplied || _cameraFilmGrain == null)
            {
                _cameraFilmGrain = null;
                _cameraFilmGrainApplied = false;
                return;
            }

            _cameraFilmGrain.active = _filmGrainActiveBeforeCamera;
            _cameraFilmGrain.type.overrideState = _filmGrainTypeOverrideBeforeCamera;
            _cameraFilmGrain.type.value = _filmGrainTypeBeforeCamera;
            _cameraFilmGrain.intensity.overrideState = _filmGrainIntensityOverrideBeforeCamera;
            _cameraFilmGrain.intensity.value = _filmGrainIntensityBeforeCamera;
            _cameraFilmGrain.response.overrideState = _filmGrainResponseOverrideBeforeCamera;
            _cameraFilmGrain.response.value = _filmGrainResponseBeforeCamera;
            _cameraFilmGrain.texture.overrideState = _filmGrainTextureOverrideBeforeCamera;
            _cameraFilmGrain.texture.value = _filmGrainTextureBeforeCamera;
            _cameraFilmGrain = null;
            _cameraFilmGrainApplied = false;
        }

        private void DrawCommandBar(float y, float enter)
        {
            float width = Mathf.Min(
                _settings.CommandBarWidth,
                _hudWidth - (_settings.ScreenMargin * 2f));
            Rect bar = new Rect(
                (_hudWidth - width) * 0.5f,
                y + ((1f - enter) * _settings.HudEnterSlideDistance),
                width,
                _settings.CommandBarHeight);
            DrawRect(bar, WithAlpha(
                _settings.CommandBackdropColor,
                _settings.CommandBackdropColor.a * enter));
            if (_settings.SurfaceTexturesEnabled &&
                _settings.TechnicalGridTexture != null &&
                _settings.TechnicalGridOpacity > 0f)
            {
                DrawTexture(
                    bar,
                    _settings.TechnicalGridTexture,
                    new Color(1f, 1f, 1f, _settings.TechnicalGridOpacity * enter),
                    ScaleMode.StretchToFill);
            }
            DrawRect(
                new Rect(bar.x, bar.y, bar.width, _settings.FrameThickness),
                WithAlpha(
                    _settings.NeutralColor,
                    _settings.NeutralColor.a * _settings.OuterFrameOpacity * enter));

            float groupWidth = bar.width / 3f;
            DrawCommandGroup(
                new Rect(bar.x, bar.y, groupWidth, bar.height),
                _settings.ExitKey,
                _settings.ExitAction,
                enter);
            DrawCommandGroup(
                new Rect(bar.x + groupWidth, bar.y, groupWidth, bar.height),
                _settings.CaptureKey,
                _settings.CaptureAction,
                enter);
            DrawCommandGroup(
                new Rect(bar.x + (groupWidth * 2f), bar.y, groupWidth, bar.height),
                _settings.ZoomKey,
                _settings.ZoomAction,
                enter);
        }

        private void DrawCommandGroup(Rect rect, string key, string action, float enter)
        {
            float contentWidth = _settings.CommandKeyWidth + _settings.TargetLabelGap +
                Mathf.Max(_settings.CommandKeyWidth, rect.width - _settings.CommandKeyWidth -
                    (_settings.TargetLabelGap * 2f));
            float left = rect.center.x - (contentWidth * 0.5f);
            Rect keyRect = new Rect(
                left,
                rect.center.y - (_settings.CommandKeyHeight * 0.5f),
                _settings.CommandKeyWidth,
                _settings.CommandKeyHeight);
            DrawRect(keyRect, WithAlpha(
                _settings.KeycapColor,
                _settings.KeycapColor.a * enter));
            DrawCorners(
                keyRect,
                Mathf.Min(_settings.TargetStatusHeight, _settings.CommandKeyWidth * 0.5f),
                _settings.FrameThickness,
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * enter));
            DrawLabel(
                keyRect,
                key,
                _keyStyle,
                WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * enter),
                false);
            DrawLabel(
                new Rect(
                    keyRect.xMax + _settings.TargetLabelGap,
                    rect.y,
                    rect.xMax - keyRect.xMax - _settings.TargetLabelGap,
                    rect.height),
                TrackText(action),
                _commandStyle,
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * enter),
                false);
        }

        private void DrawCornerMetadata(float y, float enter)
        {
            float metadataHeight = _settings.CommandBarHeight;
            if (_detection.HasSubject)
            {
                bool documented = _storage.IsDocumented(_detection.Subject.SubjectId);
                Color documentationColor = documented ? _settings.HudMutedColor : _animatedAccentColor;
                string documentation = documented
                    ? _settings.DocumentedLabel
                    : _settings.UndocumentedLabel;
                DrawLabel(
                    new Rect(
                        _settings.ScreenMargin,
                        y,
                        _settings.CornerMetadataWidth,
                        metadataHeight),
                    TrackText(documentation),
                    _metadataStyle,
                    WithAlpha(documentationColor, documentationColor.a * enter),
                    true);
            }

            float zoomElapsed = Time.unscaledTime - _lastZoomInputAt;
            float zoomActive = 1f - Mathf.Clamp01(
                zoomElapsed / Mathf.Max(0.01f, _settings.ZoomFeedbackDuration));
            float zoomOpacity = Mathf.Lerp(
                _settings.ZoomIdleOpacity,
                1f,
                EaseOutCubic(zoomActive));
            Rect right = new Rect(
                _hudWidth - _settings.ScreenMargin - _settings.CornerMetadataWidth,
                y,
                _settings.CornerMetadataWidth,
                metadataHeight);
            string zoomText = string.Format(_settings.ZoomFormat, _zoom);
            string countText = string.Format(
                _settings.PhotoCountFormat,
                _storage.Photographs.Count,
                _settings.MaximumGalleryPhotographs);
            DrawLabel(
                new Rect(right.x, right.y, right.width, right.height * 0.5f),
                zoomText,
                _metadataRightStyle,
                WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * zoomOpacity * enter),
                true);
            DrawLabel(
                new Rect(right.x, right.center.y, right.width, right.height * 0.5f),
                countText,
                _metadataRightStyle,
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * enter),
                true);
            float zoomRange = Mathf.Max(0.01f, _settings.MaximumZoom - _settings.MinimumZoom);
            float zoom01 = Mathf.Clamp01((_zoom - _settings.MinimumZoom) / zoomRange);
            float lineWidth = right.width * zoom01;
            DrawRect(
                new Rect(
                    right.xMax - lineWidth,
                    right.yMax - _settings.FrameThickness,
                    lineWidth,
                    _settings.FrameThickness),
                WithAlpha(
                    _settings.NeutralColor,
                    _settings.NeutralColor.a * zoomActive * enter));
        }

        private void EnsureStyles()
        {
            _subjectStyle ??= CreateStyle(
                _settings.SubjectLabelFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                _settings.HudSemiboldFont);
            _targetStatusStyle ??= CreateStyle(
                _settings.StatusFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                _settings.HudSemiboldFont);
            _modeLabelStyle ??= CreateStyle(
                _settings.ModeLabelFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                Color.white,
                _settings.HudRegularFont);
            _metadataStyle ??= CreateStyle(
                _settings.MetadataFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                _settings.HudRegularFont);
            _metadataRightStyle ??= CreateStyle(
                _settings.MetadataFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleRight,
                Color.white,
                _settings.HudRegularFont);
            _commandStyle ??= CreateStyle(
                _settings.CommandFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                _settings.HudRegularFont);
            _keyStyle ??= CreateStyle(
                _settings.CommandFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                Color.white,
                _settings.HudSemiboldFont);
            _statusStyle ??= CreateStyle(
                _settings.StatusFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.HudTextColor,
                _settings.HudSemiboldFont);
            _comparisonLabelStyle ??= CreateStyle(
                _settings.ComparisonLabelFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.ComparisonLabelColor,
                _settings.HudSemiboldFont);
            _identificationTitleStyle ??= CreateStyle(
                _settings.IdentificationTitleFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.HudTextColor,
                _settings.HudSemiboldFont);
            _identificationNameStyle ??= CreateStyle(
                _settings.IdentificationNameFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.HudTextColor,
                _settings.HudSemiboldFont);
            _buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = _settings.GalleryBodyFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                font = _settings.HudSemiboldFont,
            };
        }

        private static GUIStyle CreateStyle(
            int size,
            FontStyle style,
            TextAnchor anchor,
            Color color,
            Font font = null)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
                font = font,
                clipping = TextClipping.Clip,
                normal = { textColor = color },
            };
        }

        private void DrawCrosshair(Rect rect, Color color)
        {
            DrawRect(new Rect(rect.center.x - (_settings.CrosshairThickness * 0.5f), rect.y,
                _settings.CrosshairThickness, rect.height), color);
            DrawRect(new Rect(rect.x, rect.center.y - (_settings.CrosshairThickness * 0.5f),
                rect.width, _settings.CrosshairThickness), color);
        }

        private void DrawFramingGuides(Rect detectedBounds, Rect outerFrame, Color color)
        {
            float paddedLeft = _hudWidth * _settings.ViewportEdgePadding;
            float paddedRight = _hudWidth * (1f - _settings.ViewportEdgePadding);
            float paddedTop = _hudHeight * _settings.ViewportEdgePadding;
            float paddedBottom = _hudHeight * (1f - _settings.ViewportEdgePadding);
            float length = _settings.TargetBracketLength;
            float thickness = _settings.TargetBracketThickness;
            if (detectedBounds.xMin < paddedLeft)
            {
                float y = Mathf.Clamp(detectedBounds.center.y, outerFrame.y + length, outerFrame.yMax - length);
                DrawRect(new Rect(outerFrame.x, y - (length * 0.5f), thickness, length), color);
            }
            if (detectedBounds.xMax > paddedRight)
            {
                float y = Mathf.Clamp(detectedBounds.center.y, outerFrame.y + length, outerFrame.yMax - length);
                DrawRect(new Rect(outerFrame.xMax - thickness, y - (length * 0.5f), thickness, length), color);
            }
            if (detectedBounds.yMin < paddedTop)
            {
                float x = Mathf.Clamp(detectedBounds.center.x, outerFrame.x + length, outerFrame.xMax - length);
                DrawRect(new Rect(x - (length * 0.5f), outerFrame.y, length, thickness), color);
            }
            if (detectedBounds.yMax > paddedBottom)
            {
                float x = Mathf.Clamp(detectedBounds.center.x, outerFrame.x + length, outerFrame.xMax - length);
                DrawRect(new Rect(x - (length * 0.5f), outerFrame.yMax - thickness, length, thickness), color);
            }
        }

        private static void DrawCorners(Rect rect, float length, float thickness, Color color)
        {
            // Keep opposite corner arms separated when a detected subject becomes tiny on screen.
            // Horizontal and vertical arms need separate limits because the bounds can be very narrow
            // in only one dimension.
            float horizontalLength = Mathf.Min(length, Mathf.Max(0f, (rect.width - thickness) * 0.5f));
            float verticalLength = Mathf.Min(length, Mathf.Max(0f, (rect.height - thickness) * 0.5f));

            DrawRect(new Rect(rect.x, rect.y, horizontalLength, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, verticalLength), color);
            DrawRect(new Rect(rect.xMax - horizontalLength, rect.y, horizontalLength, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, verticalLength), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, horizontalLength, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - verticalLength, thickness, verticalLength), color);
            DrawRect(new Rect(rect.xMax - horizontalLength, rect.yMax - thickness, horizontalLength, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.yMax - verticalLength, thickness, verticalLength), color);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + (amount * 2f), rect.height + (amount * 2f));
        }

        private Rect ClampToViewfinder(Rect rect)
        {
            float scale = GetHudScale();
            float margin = _settings.ScreenMargin * scale;
            float minimumX = margin;
            float minimumY = margin + (_settings.SubjectLabelHeight * scale);
            float maximumX = Screen.width - margin;
            float maximumY = Screen.height - margin;
            float xMin = Mathf.Clamp(rect.xMin, minimumX, maximumX);
            float yMin = Mathf.Clamp(rect.yMin, minimumY, maximumY);
            float xMax = Mathf.Clamp(rect.xMax, minimumX, maximumX);
            float yMax = Mathf.Clamp(rect.yMax, minimumY, maximumY);
            return Rect.MinMaxRect(
                Mathf.Min(xMin, xMax),
                Mathf.Min(yMin, yMax),
                Mathf.Max(xMin, xMax),
                Mathf.Max(yMin, yMax));
        }

        private float GetHudScale()
        {
            float referenceHeight = Mathf.Max(1f, _settings.HudReferenceHeight);
            float minimumScale = Mathf.Min(_settings.HudMinimumScale, _settings.HudMaximumScale);
            float maximumScale = Mathf.Max(_settings.HudMinimumScale, _settings.HudMaximumScale);
            return Mathf.Clamp(Screen.height / referenceHeight, minimumScale, maximumScale);
        }

        private Rect ToHudRect(Rect physicalRect)
        {
            float scale = Mathf.Max(0.01f, _hudScale);
            return new Rect(
                physicalRect.x / scale,
                physicalRect.y / scale,
                physicalRect.width / scale,
                physicalRect.height / scale);
        }

        private static Rect Lerp(Rect from, Rect to, float t)
        {
            return new Rect(
                Mathf.Lerp(from.x, to.x, t),
                Mathf.Lerp(from.y, to.y, t),
                Mathf.Lerp(from.width, to.width, t),
                Mathf.Lerp(from.height, to.height, t));
        }

        private string TrackText(string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(_settings.TextTrackingSpacer))
            {
                return value;
            }

            System.Text.StringBuilder tracked = new System.Text.StringBuilder(
                value.Length * (1 + _settings.TextTrackingSpacer.Length));
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                tracked.Append(current);
                if (i >= value.Length - 1 ||
                    char.IsWhiteSpace(current) ||
                    char.IsWhiteSpace(value[i + 1]))
                {
                    continue;
                }
                tracked.Append(_settings.TextTrackingSpacer);
            }
            return tracked.ToString();
        }

        private void DrawLabel(
            Rect rect,
            string text,
            GUIStyle style,
            Color color,
            bool shadow)
        {
            Color previous = GUI.color;
            if (shadow && _settings.HudShadowColor.a > 0f)
            {
                GUI.color = WithAlpha(
                    _settings.HudShadowColor,
                    _settings.HudShadowColor.a * color.a);
                Rect shadowRect = new Rect(
                    rect.x + _settings.HudShadowOffset.x,
                    rect.y + _settings.HudShadowOffset.y,
                    rect.width,
                    rect.height);
                GUI.Label(shadowRect, text, style);
            }
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        private static void DrawTexture(Rect rect, Texture texture, Color color, ScaleMode scaleMode)
        {
            if (texture == null) return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture, scaleMode, true);
            GUI.color = previous;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - (inverse * inverse * inverse);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void ReleaseCapturedTexture()
        {
            if (_capturedTexture == null) return;
            UnityEngine.Object.Destroy(_capturedTexture);
            _capturedTexture = null;
        }

        private void ReleaseCaptureHoldTexture()
        {
            if (_captureHoldTexture == null) return;
            UnityEngine.Object.Destroy(_captureHoldTexture);
            _captureHoldTexture = null;
        }

        private void HidePlayerRenderers()
        {
            _hiddenPlayerRenderers.Clear();
            _hiddenPlayerRendererStates.Clear();
            if (_player.Character == null) return;
            Renderer[] renderers = _player.Character.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                _hiddenPlayerRenderers.Add(renderers[i]);
                _hiddenPlayerRendererStates.Add(renderers[i].enabled);
                renderers[i].enabled = false;
            }
        }

        private void RestorePlayerRenderers()
        {
            int count = Mathf.Min(_hiddenPlayerRenderers.Count, _hiddenPlayerRendererStates.Count);
            for (int i = 0; i < count; i++)
            {
                if (_hiddenPlayerRenderers[i] != null)
                {
                    _hiddenPlayerRenderers[i].enabled = _hiddenPlayerRendererStates[i];
                }
            }
            _hiddenPlayerRenderers.Clear();
            _hiddenPlayerRendererStates.Clear();
        }

        private void OnDestroy()
        {
            EndIdentificationPause();
            if (Active == this) Active = null;
            if (_cameraModeActive)
            {
                if (_camera != null) _camera.fieldOfView = _baseFieldOfView;
                _cameraController?.SetPhotographyMode(
                    false,
                    _settings != null ? _settings.CameraDistance : 0f,
                    _settings != null ? _settings.CameraHeight : 0f);
                RestorePlayerRenderers();
                _player?.SetDisabledFlightStopEnabled(false);
                _player?.SetInputEnabled(true);
            }
            ReleaseCapturedTexture();
            ReleaseCaptureHoldTexture();
            RestoreCameraFilmGrain();
            _compendium?.Dispose();
            _storage?.Dispose();
        }
    }
}
