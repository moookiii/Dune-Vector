using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
            _data = new PhotographySaveData();
            if (DuneTrainingRuntime.Enabled)
            {
                return;
            }
            Directory.CreateDirectory(_imageDirectory);
            Load();
        }

        public PhotographRecord Store(Texture2D image, string subjectId, PhotographableSubjectCategory category, bool valid)
        {
            if (DuneTrainingRuntime.Enabled)
            {
                throw new InvalidOperationException("Photography storage is disabled during agent training.");
            }
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
            if (DuneTrainingRuntime.Enabled)
            {
                return;
            }
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
        private readonly Dictionary<string, GeoglyphArtworkPlacement> _artworkBySiteId =
            new Dictionary<string, GeoglyphArtworkPlacement>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _displayNameBySubjectId =
            new Dictionary<string, string>(StringComparer.Ordinal);
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
            BuildLookupCaches();
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
            float bestCoverage = -1f;
            float bestVisiblePercentage = 0f;
            float bestSelectionScore = float.NegativeInfinity;
            bool bestIsPreferredGlyph = false;
            bool allowGlyphSubjects = AllowsGlyphSubjects(
                _character != null,
                _character != null && _character.CurrentMode == DroneTraversalMode.Flight,
                _character != null && _character.IsStableGrounded);
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
                    if (!TryProjectBounds(
                            out Rect bounds,
                            out float coverage,
                            out float priority,
                            out float depth))
                    {
                        continue;
                    }
                    float selectionScore = CalculateSelectionScore(depth, coverage, priority);
                    bool candidateFullyFramed = IsFullyFramed(bounds);
                    bool isPreferredGlyph = _settings.PrioritizeFullyFramedAirborneGlyphs &&
                        candidateFullyFramed &&
                        Vector3.Dot(_camera.transform.forward.normalized, Vector3.down) >=
                            site.MinimumPhotoReadableAngle;
                    if (!ShouldReplaceSelection(
                            isPreferredGlyph,
                            selectionScore,
                            found,
                            bestIsPreferredGlyph,
                            bestSelectionScore))
                    {
                        continue;
                    }
                    float candidateVisiblePercentage = CalculateVisiblePercentage(site);
                    float candidateMinimumVisibility = isPreferredGlyph
                        ? GetRequiredGlyphVisibility(site)
                        : _settings.SubjectDetectionMinimumVisiblePercentage;
                    if (candidateVisiblePercentage < candidateMinimumVisibility)
                    {
                        continue;
                    }
                    bestBounds = bounds;
                    bestCoverage = coverage;
                    bestSelectionScore = selectionScore;
                    bestVisiblePercentage = candidateVisiblePercentage;
                    bestIsPreferredGlyph = isPreferredGlyph;
                    bestSubject = new PhotographableSubject(site, artwork);
                    found = true;
                }
            }

            Vector3 observerPosition = _character != null
                ? _character.transform.position
                : _camera.transform.position;
            foreach (DuneVectorPhotographableMarker marker in DuneVectorPhotographableMarker.ActiveMarkers)
            {
                if (marker == null ||
                    marker.IsSuppressedForObserver(observerPosition) ||
                    !TryResolveDisplayName(marker.SubjectId, out string displayName) ||
                    !marker.TryGetScreenBounds(
                        _camera,
                        out Rect markerBounds,
                        out float markerCoverage,
                        out float markerDepth))
                {
                    continue;
                }

                float centerPriority = Vector2.Distance(
                    markerBounds.center,
                    new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) /
                    Mathf.Max(1f, Screen.height);
                float selectionScore = CalculateSelectionScore(markerDepth, markerCoverage, centerPriority);
                if (!ShouldReplaceSelection(
                        false,
                        selectionScore,
                        found,
                        bestIsPreferredGlyph,
                        bestSelectionScore))
                {
                    continue;
                }
                float candidateVisiblePercentage = marker.CalculateVisiblePercentage(_camera, _settings);
                if (candidateVisiblePercentage < _settings.SubjectDetectionMinimumVisiblePercentage)
                {
                    continue;
                }
                bestBounds = markerBounds;
                bestCoverage = markerCoverage;
                bestSelectionScore = selectionScore;
                bestVisiblePercentage = candidateVisiblePercentage;
                bestIsPreferredGlyph = false;
                bestSubject = new PhotographableSubject(marker, displayName);
                found = true;
            }
            if (!found) return default;

            bool fullyFramed = IsFullyFramed(bestBounds);

            float visiblePercentage;
            bool valid;
            if (bestSubject.Category == PhotographableSubjectCategory.Glyph)
            {
                visiblePercentage = bestVisiblePercentage;
                DesertAtlasSiteDefinition definition = bestSubject.AtlasSite;
                float minimumCoverage = Mathf.Min(
                    definition.MinimumPhotoScreenCoverage,
                    definition.MaximumPhotoScreenCoverage);
                float maximumCoverage = Mathf.Max(
                    definition.MinimumPhotoScreenCoverage,
                    definition.MaximumPhotoScreenCoverage);
                float readableAngle = Vector3.Dot(_camera.transform.forward.normalized, Vector3.down);
                float requiredVisibility = GetRequiredGlyphVisibility(definition);
                valid = fullyFramed &&
                    bestCoverage >= minimumCoverage &&
                    bestCoverage <= maximumCoverage &&
                    readableAngle >= definition.MinimumPhotoReadableAngle &&
                    visiblePercentage >= requiredVisibility;
            }
            else
            {
                visiblePercentage = bestVisiblePercentage;
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
            return _displayNameBySubjectId.TryGetValue(subjectId ?? string.Empty, out displayName);
        }

        private void BuildLookupCaches()
        {
            _artworkBySiteId.Clear();
            if (_atlas?.Sites != null && _geoglyphs?.Placements != null)
            {
                for (int i = 0; i < _atlas.Sites.Count; i++)
                {
                    DesertAtlasSiteDefinition site = _atlas.Sites[i];
                    if (site == null || string.IsNullOrWhiteSpace(site.PersistentId)) continue;
                    GeoglyphArtworkPlacement artwork = FindClosestArtwork(site);
                    if (artwork != null) _artworkBySiteId[site.PersistentId] = artwork;
                }
            }

            _displayNameBySubjectId.Clear();
            if (_settings?.CompendiumEntries == null) return;
            for (int i = 0; i < _settings.CompendiumEntries.Count; i++)
            {
                CompendiumEntryDefinition definition = _settings.CompendiumEntries[i];
                if (definition == null || string.IsNullOrEmpty(definition.SubjectId)) continue;
                _displayNameBySubjectId[definition.SubjectId] = definition.DisplayName;
            }
        }

        private float CalculateSelectionScore(float depth, float coverage, float centerPriority)
        {
            float sizeScore = Mathf.Sqrt(Mathf.Clamp01(coverage));
            float foregroundScore = 1f / (1f + Mathf.Max(0f, depth) /
                Mathf.Max(0.1f, _settings.SubjectSelectionDepthReference));
            float centerScore = 1f - Mathf.Clamp01(centerPriority);
            return sizeScore * Mathf.Max(0f, _settings.SubjectSelectionSizeWeight) +
                foregroundScore * Mathf.Max(0f, _settings.SubjectSelectionForegroundWeight) +
                centerScore * Mathf.Max(0f, _settings.SubjectSelectionCenterWeight);
        }

        private static bool AllowsGlyphSubjects(
            bool hasCharacter,
            bool isInFlightMode,
            bool isStableGrounded)
        {
            // Kinematic grounding can remain stable for a frame while flight begins or while the
            // drone skims the sand. Flight mode is authoritative for photography: an actively
            // flying player must not lose the airborne glyph-priority tier to stale ground contact.
            return !hasCharacter || isInFlightMode || !isStableGrounded;
        }

        private float GetRequiredGlyphVisibility(DesertAtlasSiteDefinition definition)
        {
            return definition.AllowPartialPhotoOcclusion
                ? Mathf.Clamp01(definition.RequiredPhotoVisiblePercentage)
                : Mathf.Clamp01(_settings.GlyphRequiredVisiblePercentage);
        }

        private bool IsFullyFramed(Rect bounds)
        {
            return bounds.xMin >= Screen.width * _settings.ViewportEdgePadding &&
                bounds.xMax <= Screen.width * (1f - _settings.ViewportEdgePadding) &&
                bounds.yMin >= Screen.height * _settings.ViewportEdgePadding &&
                bounds.yMax <= Screen.height * (1f - _settings.ViewportEdgePadding);
        }

        private static bool ShouldReplaceSelection(
            bool candidateIsPreferredGlyph,
            float candidateScore,
            bool foundSelection,
            bool selectionIsPreferredGlyph,
            float selectionScore)
        {
            if (!foundSelection)
            {
                return true;
            }
            if (candidateIsPreferredGlyph != selectionIsPreferredGlyph)
            {
                return candidateIsPreferredGlyph;
            }
            return candidateScore > selectionScore;
        }

        private GeoglyphArtworkPlacement FindArtwork(DesertAtlasSiteDefinition site)
        {
            if (site == null || string.IsNullOrEmpty(site.PersistentId)) return null;
            _artworkBySiteId.TryGetValue(site.PersistentId, out GeoglyphArtworkPlacement artwork);
            return artwork;
        }

        private GeoglyphArtworkPlacement FindClosestArtwork(DesertAtlasSiteDefinition site)
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
            Vector2 contentCenter = artwork.UnityUvContentCenter;
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
                    Vector2 boundaryUv = GeoglyphArtworkPlacement.ImageUvToUnityUv(
                        artwork.MaskCaptureBoundary[i]);
                    Vector2 uv = contentCenter +
                        ((boundaryUv - contentCenter) * regionScale);
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

        private bool TryProjectBounds(
            out Rect bounds,
            out float coverage,
            out float priority,
            out float depth)
        {
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int frontSampleCount = 0;
            bool centerInFront = false;
            depth = float.PositiveInfinity;
            for (int i = 0; i < _worldSamples.Count; i++)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(_worldSamples[i]);
                if (viewport.z <= _camera.nearClipPlane)
                {
                    continue;
                }

                frontSampleCount++;
                centerInFront |= i == _centerSampleIndex;
                depth = Mathf.Min(depth, viewport.z);
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
                depth = float.PositiveInfinity;
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

    /// <summary>
    /// Immediate-mode photographic archive. Shares the HUD chrome vocabulary with the pause
    /// screen and shop: smoked-glass panel, accent rail, corner brackets and hand-drawn buttons.
    /// </summary>
    internal sealed class DuneVectorGalleryView
    {
        private const float CaptionTitleFraction = 0.55f;

        private readonly DuneVectorPhotographStorage _storage;
        private readonly PhotographyTuning _settings;
        private Vector2 _scroll;
        private string _selectedPhotographId;
        private bool _confirmDelete;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _cardTitleStyle;
        private GUIStyle _cardMetaStyle;
        private GUIStyle _emptyHintStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _hintRightStyle;
        private GUIStyle _modalTitleStyle;
        private GUIStyle _buttonStyle;

        /// <summary>Resolves a documented subject id to its compendium display name.</summary>
        public Func<string, string> SubjectNameResolver;

        public DuneVectorGalleryView(DuneVectorPhotographStorage storage, PhotographyTuning settings)
        {
            _storage = storage;
            _settings = settings;
        }

        private Color CardColor => Color.Lerp(_settings.GalleryPanelColor, Color.black, 0.45f);
        private Color HeaderColor => Color.Lerp(_settings.GalleryPanelColor, Color.black, 0.25f);
        private Color MattColor => new Color(0.01f, 0.015f, 0.02f, 1f);
        private Color DimTextColor => WithAlpha(_settings.GalleryTextColor, 0.55f);

        public bool Draw()
        {
            EnsureStyles();
            DuneVectorHudChrome.DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), _settings.GalleryBackdropColor);
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

            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            DuneVectorHudChrome.DrawSoftShadow(panel, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, 10f), 18f);
            DuneVectorHudChrome.DrawGlassPanel(
                panel,
                _settings.GalleryPanelColor,
                WithAlpha(_settings.GalleryAccentColor, 0.7f),
                thickness,
                1f);
            DuneVectorHudChrome.DrawCornerBrackets(
                panel,
                WithAlpha(_settings.GallerySelectionColor, 0.9f),
                26f,
                thickness);

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

        public bool CloseViewer()
        {
            if (_confirmDelete)
            {
                _confirmDelete = false;
                return true;
            }

            if (string.IsNullOrEmpty(_selectedPhotographId))
            {
                return false;
            }

            _selectedPhotographId = null;
            return true;
        }

        private void DrawGrid(Rect panel)
        {
            float padding = _settings.GalleryPadding;
            Rect header = DrawHeader(
                panel,
                string.Format(_settings.GalleryCountFormat, _settings.GalleryTitle, _storage.Photographs.Count),
                string.Format(
                    _settings.GallerySubtitleFormat,
                    _storage.Photographs.Count,
                    _storage.DocumentedGlyphCount,
                    Mathf.Max(1, _settings.MaximumGalleryPhotographs)));

            Rect footer = DrawFooter(panel, _settings.GalleryGridHint, string.Empty);
            Rect viewport = new Rect(
                panel.x + padding,
                header.yMax + padding,
                panel.width - (padding * 2f),
                Mathf.Max(1f, footer.yMin - header.yMax - (padding * 2f)));

            if (_storage.Photographs.Count == 0)
            {
                DrawEmptyState(viewport);
                return;
            }

            int columns = Mathf.Max(2, _settings.GalleryColumns);
            float gap = _settings.GalleryGap;
            float contentWidth = viewport.width - _settings.GalleryScrollbarWidth - gap;
            float cellWidth = (contentWidth - ((columns - 1) * gap)) / columns;
            float imageHeight = cellWidth * (_settings.GalleryThumbnailHeight / Mathf.Max(1f, _settings.GalleryThumbnailWidth));
            float captionHeight = _settings.SubjectLabelHeight * 1.6f;
            float cardHeight = imageHeight + captionHeight;
            float rowPitch = cardHeight + gap;
            int rows = Mathf.CeilToInt(_storage.Photographs.Count / (float)columns);
            Rect content = new Rect(0f, 0f, contentWidth, Mathf.Max(1f, (rows * rowPitch) - gap));

            // Inside the scroll view the mouse is reported in content space, so a pointer parked
            // over the header can land on a card rect; gate hover on the untransformed position.
            bool pointerInViewport = viewport.Contains(Event.current.mousePosition);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, true);
            int firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / Mathf.Max(1f, rowPitch)) - 1);
            int lastVisibleRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_scroll.y + viewport.height) / Mathf.Max(1f, rowPitch)) + 1);
            for (int row = firstVisibleRow; row <= lastVisibleRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int displayIndex = (row * columns) + column;
                    if (displayIndex >= _storage.Photographs.Count) break;
                    PhotographRecord record = _storage.Photographs[_storage.Photographs.Count - 1 - displayIndex];
                    Rect card = new Rect(column * (cellWidth + gap), row * rowPitch, cellWidth, cardHeight);
                    if (DrawCard(card, record, imageHeight, captionHeight, pointerInViewport))
                    {
                        _selectedPhotographId = record.PhotographId;
                        _confirmDelete = false;
                    }
                }
            }
            GUI.EndScrollView();
            DrawScrollEdgeFades(viewport, content.height);
        }

        /// <summary>Framed thumbnail with a caption plate; returns true when the card is clicked.</summary>
        private bool DrawCard(Rect card, PhotographRecord record, float imageHeight, float captionHeight, bool allowHover)
        {
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            bool hovered = allowHover && card.Contains(Event.current.mousePosition);
            Color accent = record.IsValidSubjectPhotograph
                ? _settings.GallerySelectionColor
                : _settings.GalleryAccentColor;

            if (hovered)
            {
                Color halo = accent;
                halo.a = 0.16f;
                DuneVectorHudChrome.DrawRect(new Rect(card.x - 4f, card.y - 4f, card.width + 8f, card.height + 8f), halo);
            }

            DuneVectorHudChrome.DrawRect(card, Color.Lerp(CardColor, accent, hovered ? 0.14f : 0f));
            Rect image = new Rect(card.x, card.y, card.width, imageHeight);
            DuneVectorHudChrome.DrawRect(image, MattColor);
            Texture2D texture = _storage.GetTexture(record.PhotographId);
            if (texture != null)
            {
                GUI.DrawTexture(image, texture, ScaleMode.ScaleToFit, false);
            }

            Rect caption = new Rect(card.x, image.yMax, card.width, captionHeight);
            DuneVectorHudChrome.DrawVerticalFade(caption, new Color(0f, 0f, 0f, 0.35f), true);
            if (record.IsValidSubjectPhotograph)
            {
                DuneVectorHudChrome.DrawRect(new Rect(caption.x, caption.y, thickness, caption.height), accent);
            }

            float inset = _settings.GalleryGap * 0.5f;
            float titleHeight = captionHeight * CaptionTitleFraction;
            Rect titleRect = new Rect(caption.x + inset + thickness, caption.y, caption.width - (inset * 2f), titleHeight);
            GUI.Label(titleRect, DescribePhotograph(record), _cardTitleStyle);
            Rect metaRect = new Rect(titleRect.x, caption.y + titleHeight, titleRect.width, captionHeight - titleHeight);
            GUI.Label(metaRect, DescribeCaptureTime(record), _cardMetaStyle);
            if (record.IsValidSubjectPhotograph)
            {
                Color previous = GUI.color;
                GUI.color = accent;
                GUI.Label(metaRect, _settings.GalleryDocumentedTag, _tagStyle);
                GUI.color = previous;
            }

            DuneVectorHudChrome.DrawBorder(card, WithAlpha(accent, hovered ? 1f : 0.45f), thickness);
            if (hovered)
            {
                DuneVectorHudChrome.DrawCornerBrackets(card, WithAlpha(_settings.GalleryTextColor, 0.85f), 16f, thickness);
            }
            return GUI.Button(card, GUIContent.none, GUIStyle.none);
        }

        private void DrawEmptyState(Rect viewport)
        {
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            Rect plate = new Rect(
                viewport.center.x - (_settings.IdentificationPanelWidth * 0.5f),
                viewport.center.y - (_settings.IdentificationPanelHeight * 0.5f),
                _settings.IdentificationPanelWidth,
                _settings.IdentificationPanelHeight);
            DuneVectorHudChrome.DrawRect(plate, CardColor);
            DuneVectorHudChrome.DrawBorder(plate, WithAlpha(_settings.GalleryAccentColor, 0.35f), thickness);
            DuneVectorHudChrome.DrawCornerBrackets(plate, WithAlpha(_settings.GalleryAccentColor, 0.6f), 18f, thickness);
            float line = _settings.SubjectLabelHeight;
            GUI.Label(
                new Rect(plate.x, plate.center.y - line, plate.width, line),
                _settings.GalleryEmptyText,
                _bodyStyle);
            GUI.Label(
                new Rect(plate.x, plate.center.y, plate.width, line),
                _settings.GalleryEmptyHint,
                _emptyHintStyle);
        }

        private void DrawViewer(Rect panel)
        {
            PhotographRecord record = _storage.GetPhotograph(_selectedPhotographId);
            if (record == null)
            {
                _selectedPhotographId = null;
                _confirmDelete = false;
                return;
            }

            int displayIndex = GetDisplayIndex(record.PhotographId);
            if (!_confirmDelete)
            {
                HandleViewerNavigationKeys(displayIndex);
            }

            float padding = _settings.GalleryPadding;
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            Rect header = DrawHeader(
                panel,
                DescribePhotograph(record),
                string.Format(_settings.GalleryCountFormat, _settings.GalleryTitle, _storage.Photographs.Count));
            Rect footer = DrawFooter(
                panel,
                _settings.GalleryViewerHint,
                string.Format(_settings.GalleryViewerCountFormat, displayIndex + 1, _storage.Photographs.Count));

            float actionHeight = _settings.GalleryButtonHeight;
            Rect frame = new Rect(
                panel.x + padding,
                header.yMax + padding,
                panel.width - (padding * 2f),
                Mathf.Max(1f, footer.yMin - header.yMax - (padding * 3f) - actionHeight));
            DuneVectorHudChrome.DrawRect(frame, MattColor);
            Texture2D texture = _storage.GetTexture(record.PhotographId);
            if (texture != null)
            {
                GUI.DrawTexture(frame, texture, ScaleMode.ScaleToFit, false);
            }
            DuneVectorHudChrome.DrawBorder(frame, WithAlpha(_settings.GalleryAccentColor, 0.5f), thickness);
            DuneVectorHudChrome.DrawCornerBrackets(frame, WithAlpha(_settings.GallerySelectionColor, 0.75f), 22f, thickness);

            Rect actions = new Rect(frame.x, frame.yMax + padding, frame.width, actionHeight);
            float buttonWidth = _settings.GalleryActionButtonWidth;
            float gap = _settings.GalleryGap;
            Rect done = new Rect(actions.xMax - buttonWidth, actions.y, buttonWidth, actionHeight);
            Rect delete = new Rect(done.x - gap - buttonWidth, actions.y, buttonWidth, actionHeight);
            Rect next = new Rect(delete.x - (gap * 2f) - buttonWidth, actions.y, buttonWidth, actionHeight);
            Rect earlier = new Rect(next.x - gap - buttonWidth, actions.y, buttonWidth, actionHeight);
            GUI.Label(
                new Rect(actions.x, actions.y, Mathf.Max(0f, earlier.x - actions.x - gap), actions.height),
                DescribeCaptureTime(record),
                _cardMetaStyle);

            bool interactive = !_confirmDelete;
            bool hasSiblings = _storage.Photographs.Count > 1;
            if (DrawChromeButton(earlier, _settings.GalleryPreviousButton, _settings.GalleryAccentColor, interactive && hasSiblings))
            {
                SelectByDisplayIndex(displayIndex - 1);
            }
            if (DrawChromeButton(next, _settings.GalleryNextButton, _settings.GalleryAccentColor, interactive && hasSiblings))
            {
                SelectByDisplayIndex(displayIndex + 1);
            }
            if (DrawChromeButton(delete, _settings.GalleryDeleteButton, _settings.GalleryDangerColor, interactive))
            {
                _confirmDelete = true;
            }
            if (DrawChromeButton(done, _settings.GalleryBackButton, _settings.GallerySelectionColor, interactive))
            {
                CloseViewer();
            }

            if (_confirmDelete)
            {
                DrawDeleteConfirmation(panel, record);
            }
        }

        private void DrawDeleteConfirmation(Rect panel, PhotographRecord record)
        {
            float padding = _settings.GalleryPadding;
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            DuneVectorHudChrome.DrawRect(panel, new Color(0f, 0f, 0f, 0.7f));

            Rect confirmation = new Rect(
                panel.center.x - (_settings.IdentificationPanelWidth * 0.5f),
                panel.center.y - (_settings.IdentificationPanelHeight * 0.5f),
                _settings.IdentificationPanelWidth,
                _settings.IdentificationPanelHeight);
            DuneVectorHudChrome.DrawSoftShadow(confirmation, new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, 8f), 14f);
            DuneVectorHudChrome.DrawGlassPanel(
                confirmation,
                _settings.IdentificationPanelColor,
                WithAlpha(_settings.GalleryDangerColor, 0.85f),
                thickness,
                1f);
            DuneVectorHudChrome.DrawCornerBrackets(confirmation, _settings.GalleryDangerColor, 18f, thickness);
            DuneVectorHudChrome.DrawRect(
                new Rect(confirmation.x, confirmation.y, confirmation.width, thickness * 1.5f),
                _settings.GalleryDangerColor);

            Color previousColor = GUI.color;
            GUI.color = _settings.GalleryDangerColor;
            GUI.Label(
                new Rect(confirmation.x + padding, confirmation.y + (padding * 0.6f), confirmation.width - (padding * 2f), _settings.SubjectLabelHeight),
                _settings.GalleryDeleteTitle,
                _modalTitleStyle);
            GUI.color = previousColor;
            GUI.Label(
                new Rect(confirmation.x + padding, confirmation.y + (padding * 0.6f) + _settings.SubjectLabelHeight,
                    confirmation.width - (padding * 2f), _settings.SubjectLabelHeight * 1.6f),
                _settings.DeleteConfirmation,
                _bodyStyle);

            float buttonWidth = (confirmation.width - (padding * 3f)) * 0.5f;
            Rect confirmDelete = new Rect(
                confirmation.x + padding,
                confirmation.yMax - padding - _settings.GalleryButtonHeight,
                buttonWidth,
                _settings.GalleryButtonHeight);
            Rect cancel = new Rect(confirmDelete.xMax + padding, confirmDelete.y, buttonWidth, confirmDelete.height);
            if (DrawChromeButton(confirmDelete, _settings.GalleryDeleteButton, _settings.GalleryDangerColor, true))
            {
                int displayIndex = GetDisplayIndex(record.PhotographId);
                _storage.Delete(record.PhotographId);
                _confirmDelete = false;
                _selectedPhotographId = null;
                SelectByDisplayIndex(displayIndex);
            }
            if (DrawChromeButton(cancel, _settings.DeleteCancelButton, _settings.GalleryAccentColor, true))
            {
                _confirmDelete = false;
            }
        }

        private Rect DrawHeader(Rect panel, string title, string subtitle)
        {
            float padding = _settings.GalleryPadding;
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            Rect header = new Rect(panel.x, panel.y, panel.width, _settings.GalleryHeaderHeight);
            DuneVectorHudChrome.DrawRect(header, HeaderColor);
            DuneVectorHudChrome.DrawVerticalFade(header, new Color(1f, 1f, 1f, 0.05f), true);
            DuneVectorHudChrome.DrawRect(new Rect(header.x, header.y, header.width, thickness * 1.5f), _settings.GalleryAccentColor);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(header.x, header.yMax - (thickness + 12f), header.width, 12f),
                WithAlpha(_settings.GalleryAccentColor, 0.25f),
                false);
            DuneVectorHudChrome.DrawRect(
                new Rect(header.x, header.yMax - thickness, header.width, thickness),
                WithAlpha(_settings.GalleryAccentColor, 0.6f));

            float titleHeight = _titleStyle.lineHeight;
            float subtitleHeight = _subtitleStyle.lineHeight;
            float block = titleHeight + subtitleHeight;
            float buttonWidth = _settings.GalleryActionButtonWidth;
            Rect titleRect = new Rect(
                header.x + padding,
                header.y + ((header.height - block) * 0.5f),
                header.width - (padding * 3f) - buttonWidth,
                titleHeight);
            DuneVectorHudChrome.DrawGlowLabel(
                titleRect,
                title,
                _titleStyle,
                _settings.GalleryTextColor,
                WithAlpha(_settings.GalleryAccentColor, 0.16f),
                2f,
                new Color(0f, 0f, 0f, 0.65f),
                new Vector2(2f, 2f));
            Color previous = GUI.color;
            GUI.color = DimTextColor;
            GUI.Label(new Rect(titleRect.x, titleRect.yMax, titleRect.width, subtitleHeight), subtitle, _subtitleStyle);
            GUI.color = previous;

            Rect done = new Rect(
                header.xMax - padding - buttonWidth,
                header.y + ((header.height - _settings.GalleryButtonHeight) * 0.5f),
                buttonWidth,
                _settings.GalleryButtonHeight);
            if (DrawChromeButton(done, _settings.GalleryDoneButton, _settings.GallerySelectionColor, !_confirmDelete))
            {
                DuneVectorPhotographySystem.RequestCloseGallery();
            }
            return header;
        }

        private Rect DrawFooter(Rect panel, string hint, string status)
        {
            float padding = _settings.GalleryPadding;
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            Rect footer = new Rect(
                panel.x,
                panel.yMax - _settings.GalleryFooterHeight,
                panel.width,
                _settings.GalleryFooterHeight);
            DuneVectorHudChrome.DrawRect(footer, HeaderColor);
            DuneVectorHudChrome.DrawRect(
                new Rect(footer.x, footer.y, footer.width, thickness),
                WithAlpha(_settings.GalleryAccentColor, 0.35f));

            Color previous = GUI.color;
            GUI.color = DimTextColor;
            GUI.Label(new Rect(footer.x + padding, footer.y, footer.width - (padding * 2f), footer.height), hint, _hintStyle);
            if (!string.IsNullOrEmpty(status))
            {
                GUI.color = WithAlpha(_settings.GalleryAccentColor, 0.85f);
                GUI.Label(new Rect(footer.x + padding, footer.y, footer.width - (padding * 2f), footer.height), status, _hintRightStyle);
            }
            GUI.color = previous;
            return footer;
        }

        /// <summary>Flat chrome button; painted by hand so it matches the panel instead of the default skin.</summary>
        private bool DrawChromeButton(Rect rect, string label, Color accent, bool enabled)
        {
            float thickness = Mathf.Max(1f, _settings.FrameThickness);
            bool hovered = enabled && rect.Contains(Event.current.mousePosition);
            Color body = enabled
                ? Color.Lerp(CardColor, accent, hovered ? 0.3f : 0.1f)
                : Color.Lerp(CardColor, Color.black, 0.3f);
            DuneVectorHudChrome.DrawRect(rect, body);
            DuneVectorHudChrome.DrawVerticalFade(rect, new Color(1f, 1f, 1f, hovered ? 0.12f : 0.05f), true);
            DuneVectorHudChrome.DrawBorder(rect, WithAlpha(accent, enabled ? (hovered ? 1f : 0.6f) : 0.2f), thickness);
            if (hovered)
            {
                DuneVectorHudChrome.DrawCornerBrackets(rect, WithAlpha(_settings.GalleryTextColor, 0.9f), 12f, thickness);
            }

            Color previous = GUI.color;
            GUI.color = enabled ? (hovered ? _settings.GalleryTextColor : WithAlpha(_settings.GalleryTextColor, 0.85f)) : DimTextColor;
            GUI.Label(rect, label, _buttonStyle);
            GUI.color = previous;
            return enabled && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private void DrawScrollEdgeFades(Rect viewport, float contentHeight)
        {
            float fade = 26f;
            if (_scroll.y > 1f)
            {
                DuneVectorHudChrome.DrawVerticalFade(
                    new Rect(viewport.x, viewport.y, viewport.width, fade),
                    WithAlpha(_settings.GalleryPanelColor, 0.95f),
                    true);
            }
            if (_scroll.y + viewport.height < contentHeight - 1f)
            {
                DuneVectorHudChrome.DrawVerticalFade(
                    new Rect(viewport.x, viewport.yMax - fade, viewport.width, fade),
                    WithAlpha(_settings.GalleryPanelColor, 0.95f),
                    false);
            }
        }

        private void HandleViewerNavigationKeys(int displayIndex)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (current.keyCode == KeyCode.LeftArrow)
            {
                SelectByDisplayIndex(displayIndex - 1);
                current.Use();
            }
            else if (current.keyCode == KeyCode.RightArrow)
            {
                SelectByDisplayIndex(displayIndex + 1);
                current.Use();
            }
        }

        /// <summary>Display order runs newest first, the reverse of the stored order.</summary>
        private int GetDisplayIndex(string photographId)
        {
            for (int i = 0; i < _storage.Photographs.Count; i++)
            {
                if (string.Equals(_storage.Photographs[i].PhotographId, photographId, StringComparison.Ordinal))
                {
                    return _storage.Photographs.Count - 1 - i;
                }
            }
            return 0;
        }

        private void SelectByDisplayIndex(int displayIndex)
        {
            int count = _storage.Photographs.Count;
            if (count == 0)
            {
                _selectedPhotographId = null;
                return;
            }

            displayIndex = Mathf.Clamp(displayIndex, 0, count - 1);
            _selectedPhotographId = _storage.Photographs[count - 1 - displayIndex].PhotographId;
        }

        private string DescribePhotograph(PhotographRecord record)
        {
            if (record.IsValidSubjectPhotograph && !string.IsNullOrEmpty(record.SubjectId))
            {
                string resolved = SubjectNameResolver?.Invoke(record.SubjectId);
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved.ToUpperInvariant();
                }
            }
            return record.IsValidSubjectPhotograph
                ? _settings.GalleryDocumentedLabel
                : string.Format(_settings.GalleryPhotoLabelFormat, record.CaptureSequence);
        }

        private string DescribeCaptureTime(PhotographRecord record)
        {
            if (record.CaptureUtcTicks <= 0L || record.CaptureUtcTicks > DateTime.MaxValue.Ticks)
            {
                return _settings.GalleryUnknownCaptureTime;
            }
            return new DateTime(record.CaptureUtcTicks, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString(_settings.GalleryCaptureTimeFormat, CultureInfo.InvariantCulture);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            int body = Mathf.Max(8, _settings.GalleryBodyFontSize);
            _titleStyle = CreateStyle(_settings.GalleryTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.GalleryTextColor);
            _subtitleStyle = CreateStyle(Mathf.Max(8, body - 2), FontStyle.Bold, TextAnchor.MiddleLeft, _settings.GalleryTextColor);
            _bodyStyle = CreateStyle(body, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.GalleryTextColor);
            _cardTitleStyle = CreateStyle(body, FontStyle.Bold, TextAnchor.LowerLeft, _settings.GalleryTextColor);
            _cardMetaStyle = CreateStyle(Mathf.Max(8, body - 3), FontStyle.Normal, TextAnchor.MiddleLeft, DimTextColor);
            _emptyHintStyle = CreateStyle(Mathf.Max(8, body - 2), FontStyle.Normal, TextAnchor.MiddleCenter, DimTextColor);
            _tagStyle = CreateStyle(Mathf.Max(8, body - 4), FontStyle.Bold, TextAnchor.MiddleRight, _settings.GalleryTextColor);
            _hintStyle = CreateStyle(Mathf.Max(8, body - 3), FontStyle.Normal, TextAnchor.MiddleLeft, _settings.GalleryTextColor);
            _hintRightStyle = CreateStyle(Mathf.Max(8, body - 3), FontStyle.Bold, TextAnchor.MiddleRight, _settings.GalleryTextColor);
            _modalTitleStyle = CreateStyle(Mathf.Max(8, body + 6), FontStyle.Bold, TextAnchor.MiddleLeft, _settings.GalleryTextColor);
            _buttonStyle = CreateStyle(body, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.GalleryTextColor);
        }

        private static GUIStyle CreateStyle(int size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = color },
            };
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
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
        private DesertAtlasTuning _atlasSettings;
        private DuneVectorPhotographStorage _storage;
        private DuneVectorSubjectDetector _detector;
        private DuneVectorGalleryView _gallery;
        private DuneVectorToolkitCompendiumView _compendium;
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
        private float _commandHintStartedAt = -1f;
        private bool _commandHintShown;
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
        private bool _identifiedGlyphAwaitingContinue;
        private bool _identifiedContinueArmed;
        private CameraPresentationState _presentationState;
        // Replace-prompt hit rects, in HUD space, published by the last draw so Update can resolve
        // clicks from the input system instead of relying on IMGUI events reaching this OnGUI.
        private Rect _replaceKeepRegion;
        private Rect _replaceReplaceRegion;
        private bool _replaceRegionsValid;
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
        private GUIStyle _identificationNameStyle;
        private GUIStyle _replaceEyebrowStyle;
        private GUIStyle _glyphDiscoveryHeaderStyle;
        private GUIStyle _glyphDiscoveryMetadataStyle;
        private GUIStyle _glyphDiscoveryIdentityStyle;
        private GUIStyle _glyphDiscoveryTitleStyle;
        private GUIStyle _glyphDiscoveryLoreStyle;
        private GUIStyle _glyphDiscoveryArchivedStyle;
        private GUIStyle _glyphDiscoveryContinueStyle;
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
            _atlasSettings = atlas;
            _storage = new DuneVectorPhotographStorage(_settings);
            _detector = new DuneVectorSubjectDetector(
                _camera,
                player != null ? player.Character : null,
                world,
                geoglyphs,
                atlas,
                _settings);
            _gallery = new DuneVectorGalleryView(_storage, _settings);
            _compendium = new DuneVectorToolkitCompendiumView(_storage, _settings, atlas);
            _gallery.SubjectNameResolver = subjectId =>
                _compendium.TryResolve(subjectId, out string displayName) ? displayName : string.Empty;
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

        public static void CancelCameraMode()
        {
            if (Active != null && Active._cameraModeActive)
            {
                Active.ExitCameraMode();
            }
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

        public bool CloseGalleryViewer()
        {
            return _gallery != null && _gallery.CloseViewer();
        }

        public static void RequestCloseCompendium()
        {
            _closeCompendiumRequested = true;
        }

        public void ShowCompendium()
        {
            _closeCompendiumRequested = false;
            _compendium?.Show();
        }

        public bool DrawCompendium()
        {
            _compendium?.Show();
            if (!_closeCompendiumRequested)
            {
                return false;
            }
            _closeCompendiumRequested = false;
            _compendium?.Hide();
            return true;
        }

        public bool CloseCompendiumViewer()
        {
            return _compendium != null && _compendium.CloseLightbox();
        }

        public void HideCompendium()
        {
            _compendium?.Hide();
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
                if (_presentationState == CameraPresentationState.Identified &&
                    _identifiedGlyphAwaitingContinue)
                {
                    if (!_identifiedContinueArmed)
                    {
                        _identifiedContinueArmed =
                            mouse == null || !mouse.leftButton.isPressed;
                    }
                    else if (
                        Time.unscaledTime >=
                            _captureStartedAt + _settings.GlyphDiscoveryContinueRevealDelay &&
                        mouse != null &&
                        mouse.leftButton.wasPressedThisFrame)
                    {
                        ReturnToLiveCamera();
                    }
                }
                else if (_presentationState == CameraPresentationState.Identified &&
                    Time.unscaledTime >= _presentationUntil)
                {
                    ReturnToLiveCamera();
                }
                else if (_presentationState == CameraPresentationState.ReplacePrompt)
                {
                    if (keyboard != null &&
                        (keyboard.enterKey.wasPressedThisFrame ||
                         keyboard.numpadEnterKey.wasPressedThisFrame))
                    {
                        ConfirmPhotographReplacement();
                    }
                    else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    {
                        ReturnToLiveCamera();
                    }
                    else if (_replaceRegionsValid &&
                        mouse != null &&
                        mouse.leftButton.wasPressedThisFrame)
                    {
                        Vector2 hudPointer = ScreenToHudPoint(mouse.position.ReadValue());
                        if (_replaceReplaceRegion.Contains(hudPointer))
                        {
                            ConfirmPhotographReplacement();
                        }
                        else if (_replaceKeepRegion.Contains(hudPointer))
                        {
                            ReturnToLiveCamera();
                        }
                    }
                }
                return;
            }

            // Capture the camera pose and portal billboards from the frame the player actually
            // composed. Applying this frame's mouse delta first moves the camera during Update,
            // while streamed portal billboards do not refit to that camera until LateUpdate. A
            // manual render in between therefore photographs portals from a mismatched pose.
            // Handling the shutter before camera motion preserves the last presented world frame
            // and keeps every portal identical to what was visible when the button was pressed.
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                CapturePhotograph();
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

        }

        private void LateUpdate()
        {
            if (_cameraModeActive && Application.isFocused)
            {
                ApplyCameraModeCursorState();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _cameraModeActive)
            {
                ApplyCameraModeCursorState();
            }
        }

        private void ApplyCameraModeCursorState()
        {
            bool needsPointer = _presentationState == CameraPresentationState.ReplacePrompt;
            Cursor.lockState = needsPointer ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = needsPointer;
        }

        private void EnterCameraMode()
        {
            _cameraModeActive = true;
            _presentationState = CameraPresentationState.Live;
            _baseFieldOfView = _camera.fieldOfView;
            _zoom = _targetZoom = 1f;
            _player.SetInputEnabled(false);
            _player.SetDisabledFlightStopEnabled(true);
            _cameraController.SetPhotographyMode(true, _settings.CameraDistance, _settings.CameraHeight, _settings.MinPitch, _settings.MaxPitch);
            HidePlayerRenderers();
            EnableCameraFilmGrain();
            ApplyCameraModeCursorState();
            _detection = default;
            _animatedAccentColor = _settings.NeutralColor;
            _targetStateBlend = 0f;
            _previousHasSubject = false;
            _hasAnimatedBounds = false;
            _hudEnteredAt = Time.unscaledTime;
            _lastZoomInputAt = float.NegativeInfinity;
            _nextValidationTime = 0f;
            if (!_settings.CommandHintFirstUseOnly || !_commandHintShown)
            {
                _commandHintShown = true;
                _commandHintStartedAt = Time.unscaledTime;
            }
            else
            {
                _commandHintStartedAt = -1f;
            }
        }

        private void ExitCameraMode()
        {
            EndIdentificationPause();
            _cameraModeActive = false;
            _replaceRegionsValid = false;
            _camera.fieldOfView = _baseFieldOfView;
            _cameraController.SetPhotographyMode(false, _settings.CameraDistance, _settings.CameraHeight, _settings.MinPitch, _settings.MaxPitch);
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
                _identifiedGlyphAwaitingContinue =
                    category == PhotographableSubjectCategory.Glyph;
                _identifiedContinueArmed = false;
                _presentationUntil = Time.unscaledTime + _settings.IdentificationHoldDuration;
                BeginIdentificationPause();
            }
            else
            {
                _presentationState = CameraPresentationState.ReplacePrompt;
                BeginIdentificationPause();
                ApplyCameraModeCursorState();
            }
        }

        private Texture2D CaptureCameraImage()
        {
            int width = Mathf.Max(320, _settings.ImageWidth);
            int height = Mathf.Max(180, _settings.ImageHeight);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = _camera.targetTexture;
            DuneVectorInstancedVisualGroup instancedSubject =
                _detection.HasSubject && _detection.Subject.Marker != null
                    ? _detection.Subject.Marker.GetComponent<DuneVectorInstancedVisualGroup>()
                    : null;
            Texture2D image = null;
            try
            {
                instancedSubject?.SetPhotographyRenderersEnabled(true);
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
                instancedSubject?.SetPhotographyRenderersEnabled(false);
                _camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void ReturnToLiveCamera()
        {
            EndIdentificationPause();
            _presentationState = CameraPresentationState.Live;
            ApplyCameraModeCursorState();
            ReleaseCapturedTexture();
            _pendingPhotograph = null;
            _identifiedGlyphAwaitingContinue = false;
            _identifiedContinueArmed = false;
            _replaceRegionsValid = false;
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
                    _hudHeight - _settings.ScreenMargin - labelBlockHeight - _settings.CornerMetadataHeight);
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

            float bandBottom = _hudHeight - _settings.ScreenMargin - _settings.BottomInterfaceOffset;
            DrawCornerMetadata(bandBottom - _settings.CornerMetadataHeight, enter);
            DrawCommandBar(bandBottom, enter);
        }

        private void DrawCapturePresentation()
        {
            DrawRect(new Rect(0f, 0f, _hudWidth, _hudHeight), _settings.GalleryBackdropColor);
            Rect imageRect = new Rect(0f, 0f, _hudWidth, _hudHeight);
            if (_capturedTexture != null) GUI.DrawTexture(imageRect, _capturedTexture, ScaleMode.ScaleAndCrop, false);
            DrawSurfaceTextures();
            if (_presentationState == CameraPresentationState.Identified)
            {
                if (_identifiedGlyphAwaitingContinue)
                {
                    DrawGlyphDiscoveryPresentation();
                    return;
                }

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
                DrawReplaceDecision();
            }
        }

        // The replace prompt is one composed sheet: dimmed capture, header, the two candidate
        // photographs, then the decision row. Hovering a command focuses the photograph it acts on
        // so the choice reads before it is made.
        private void DrawReplaceDecision()
        {
            float elapsed = Time.unscaledTime - _captureStartedAt;
            float progress = EaseOutCubic(Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, _settings.HudEnterDuration)));
            float slide = (1f - progress) * _settings.HudEnterSlideDistance;

            DrawRect(
                new Rect(0f, 0f, _hudWidth, _hudHeight),
                WithAlpha(
                    _settings.ReplaceDecisionBackdropColor,
                    _settings.ReplaceDecisionBackdropColor.a * progress));

            float padding = _settings.ReplaceDecisionPanelPadding;
            float gap = _settings.ReplaceDecisionSectionGap;
            float cardPadding = _settings.ComparisonCardPadding;
            float headerBlock = _settings.ReplaceDecisionEyebrowHeight + _settings.ReplaceDecisionNameHeight;
            float footerBlock = _settings.ReplaceDecisionPromptHeight + gap +
                _settings.ReplaceDecisionButtonHeight + _settings.ReplaceDecisionHintHeight;

            // Fit the pair of photographs to whichever axis runs out first.
            float widthBudget = _hudWidth - (_settings.ScreenMargin * 2f) - (padding * 2f) -
                _settings.ComparisonImageGap - (cardPadding * 4f);
            float heightBudget = _hudHeight - (_settings.ScreenMargin * 2f) - (padding * 2f) -
                headerBlock - footerBlock - (gap * 2f) - _settings.ComparisonLabelHeight -
                (cardPadding * 3f);
            float scale = Mathf.Clamp01(Mathf.Min(
                (widthBudget * 0.5f) / Mathf.Max(1f, _settings.ComparisonImageWidth),
                heightBudget / Mathf.Max(1f, _settings.ComparisonImageHeight)));
            float imageWidth = _settings.ComparisonImageWidth * scale;
            float imageHeight = _settings.ComparisonImageHeight * scale;
            float cardWidth = imageWidth + (cardPadding * 2f);
            float cardHeight = imageHeight + _settings.ComparisonLabelHeight + (cardPadding * 3f);

            float contentWidth = (cardWidth * 2f) + _settings.ComparisonImageGap;
            float panelWidth = contentWidth + (padding * 2f);
            float panelHeight = (padding * 2f) + headerBlock + gap + cardHeight + gap + footerBlock;
            Rect panel = new Rect(
                (_hudWidth - panelWidth) * 0.5f,
                ((_hudHeight - panelHeight) * 0.5f) + slide,
                panelWidth,
                panelHeight);

            DrawRect(
                panel,
                WithAlpha(
                    _settings.GlyphDiscoveryPanelColor,
                    _settings.GlyphDiscoveryPanelColor.a * progress));
            DrawBorder(
                panel,
                WithAlpha(
                    _settings.GlyphDiscoveryBorderColor,
                    _settings.GlyphDiscoveryBorderColor.a * progress),
                _settings.GlyphDiscoveryBorderThickness);
            DrawRect(
                new Rect(panel.x, panel.y, _settings.GlyphDiscoveryAccentWidth, panel.height),
                WithAlpha(_settings.ValidColor, _settings.ValidColor.a * progress));

            float contentX = panel.x + padding;
            float y = panel.y + padding;
            DrawLabel(
                new Rect(contentX, y, contentWidth, _settings.ReplaceDecisionEyebrowHeight),
                TrackText(_settings.AlreadyDocumentedText),
                _replaceEyebrowStyle,
                WithAlpha(_settings.ValidColor, _settings.ValidColor.a * progress),
                true);
            y += _settings.ReplaceDecisionEyebrowHeight;
            DrawLabel(
                new Rect(contentX, y, contentWidth, _settings.ReplaceDecisionNameHeight),
                _pendingSubject.DisplayName,
                _identificationNameStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryPrimaryTextColor,
                    _settings.GlyphDiscoveryPrimaryTextColor.a * progress),
                true);
            y += _settings.ReplaceDecisionNameHeight + gap;

            Rect currentCard = new Rect(contentX, y, cardWidth, cardHeight);
            Rect newCard = new Rect(
                currentCard.xMax + _settings.ComparisonImageGap,
                y,
                cardWidth,
                cardHeight);
            y += cardHeight + gap;

            Rect prompt = new Rect(contentX, y, contentWidth, _settings.ReplaceDecisionPromptHeight);
            y += _settings.ReplaceDecisionPromptHeight + gap;

            float buttonWidth = Mathf.Min(
                _settings.ReplaceDecisionButtonWidth,
                (contentWidth - _settings.ReplaceDecisionButtonGap) * 0.5f);
            float buttonsWidth = (buttonWidth * 2f) + _settings.ReplaceDecisionButtonGap;
            // Keep sits left under the CURRENT photograph, replace right under the NEW one.
            Rect keep = new Rect(
                panel.center.x - (buttonsWidth * 0.5f),
                y,
                buttonWidth,
                _settings.ReplaceDecisionButtonHeight);
            Rect replace = new Rect(
                keep.xMax + _settings.ReplaceDecisionButtonGap,
                y,
                buttonWidth,
                _settings.ReplaceDecisionButtonHeight);

            Vector2 pointer = Mouse.current != null
                ? ScreenToHudPoint(Mouse.current.position.ReadValue())
                : Event.current.mousePosition;
            bool replaceHovered = replace.Contains(pointer) || newCard.Contains(pointer);
            bool keepHovered = keep.Contains(pointer) || currentCard.Contains(pointer);

            DrawComparisonCard(
                currentCard,
                _storage.GetCanonicalTexture(_pendingSubject.SubjectId),
                _settings.ComparisonCurrentLabel,
                _settings.NeutralColor,
                keepHovered,
                imageWidth,
                imageHeight,
                progress);
            DrawComparisonCard(
                newCard,
                _capturedTexture,
                _settings.ComparisonNewLabel,
                _settings.ValidColor,
                replaceHovered,
                imageWidth,
                imageHeight,
                progress);

            DrawLabel(
                prompt,
                TrackText(_settings.ReplacePrompt),
                _statusStyle,
                WithAlpha(
                    _settings.GlyphDiscoverySecondaryTextColor,
                    _settings.GlyphDiscoverySecondaryTextColor.a * progress),
                true);

            bool replacePressed = DrawDecisionCommand(
                replace,
                _settings.ReplaceButton,
                _settings.ReplaceButtonHint,
                _settings.ValidColor,
                replaceHovered,
                elapsed,
                progress);
            bool keepPressed = DrawDecisionCommand(
                keep,
                _settings.KeepButton,
                _settings.KeepButtonHint,
                _settings.NeutralColor,
                keepHovered,
                elapsed,
                progress);

            // Each photograph and the command beneath it act as one target, resolved in Update from
            // the input system so a click lands even when IMGUI never sees the mouse event.
            _replaceKeepRegion = Union(currentCard, keep);
            _replaceReplaceRegion = Union(newCard, replace);
            _replaceRegionsValid = true;

            if (replacePressed)
            {
                ConfirmPhotographReplacement();
                return;
            }
            if (keepPressed)
            {
                ReturnToLiveCamera();
            }
        }

        private bool DrawDecisionCommand(
            Rect command,
            string label,
            string hint,
            Color accent,
            bool hovered,
            float elapsed,
            float progress)
        {
            Color fill = hovered
                ? _settings.GlyphDiscoveryCommandHoverColor
                : _settings.GlyphDiscoveryCommandColor;
            DrawRect(command, WithAlpha(fill, fill.a * progress));
            DrawBorder(
                command,
                WithAlpha(accent, accent.a * progress * (hovered ? 1f : 0.55f)),
                _settings.GlyphDiscoveryBorderThickness);
            if (hovered)
            {
                float sweepTravel = command.width + _settings.GlyphDiscoveryFocusSweepWidth;
                float sweepNormalized = Mathf.Repeat(
                    elapsed / Mathf.Max(0.01f, _settings.GlyphDiscoveryFocusSweepDuration),
                    1f);
                Rect sweep = Intersect(
                    new Rect(
                        command.x - _settings.GlyphDiscoveryFocusSweepWidth + (sweepTravel * sweepNormalized),
                        command.y,
                        _settings.GlyphDiscoveryFocusSweepWidth,
                        command.height),
                    command);
                if (sweep.width > 0f)
                {
                    DrawRect(
                        sweep,
                        WithAlpha(
                            _settings.GlyphDiscoveryFocusSweepColor,
                            _settings.GlyphDiscoveryFocusSweepColor.a * progress));
                }
            }
            DrawLabel(
                command,
                TrackText(label),
                _glyphDiscoveryContinueStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryPrimaryTextColor,
                    _settings.GlyphDiscoveryPrimaryTextColor.a * progress),
                true);
            DrawLabel(
                new Rect(command.x, command.yMax, command.width, _settings.ReplaceDecisionHintHeight),
                TrackText(hint),
                _glyphDiscoveryIdentityStyle,
                WithAlpha(
                    _settings.GlyphDiscoverySecondaryTextColor,
                    _settings.GlyphDiscoverySecondaryTextColor.a * progress * (hovered ? 1f : 0.7f)),
                true);
            return GUI.Button(command, GUIContent.none, GUIStyle.none);
        }

        private void ConfirmPhotographReplacement()
        {
            if (_pendingPhotograph != null)
            {
                _storage.Document(
                    _pendingSubject.SubjectId,
                    _pendingSubject.Category,
                    _pendingPhotograph.PhotographId);
            }
            ReturnToLiveCamera();
        }

        private void DrawGlyphDiscoveryPresentation()
        {
            float elapsed = Time.unscaledTime - _captureStartedAt;
            float progress = EaseOutCubic(Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, _settings.HudEnterDuration)));
            float panelWidth = Mathf.Min(
                _settings.GlyphDiscoveryPanelWidth,
                _hudWidth - (_settings.ScreenMargin * 2f));
            float panelHeight = Mathf.Min(
                _settings.GlyphDiscoveryPanelHeight,
                _hudHeight - (_settings.ScreenMargin * 2f));
            Rect panel = new Rect(
                (_hudWidth - panelWidth) * 0.5f,
                _hudHeight - _settings.GlyphDiscoveryPanelBottomOffset - panelHeight +
                    ((1f - progress) * _settings.HudEnterSlideDistance),
                panelWidth,
                panelHeight);
            DrawRect(
                Expand(panel, _settings.GlyphDiscoveryVignettePadding),
                WithAlpha(
                    _settings.GlyphDiscoveryVignetteColor,
                    _settings.GlyphDiscoveryVignetteColor.a * progress));
            DrawRect(
                panel,
                WithAlpha(
                    _settings.GlyphDiscoveryPanelColor,
                    _settings.GlyphDiscoveryPanelColor.a * progress));
            DrawBorder(
                panel,
                WithAlpha(
                    _settings.GlyphDiscoveryBorderColor,
                    _settings.GlyphDiscoveryBorderColor.a * progress),
                _settings.GlyphDiscoveryBorderThickness);
            DrawRect(
                new Rect(
                    panel.x,
                    panel.y,
                    _settings.GlyphDiscoveryAccentWidth,
                    panel.height),
                WithAlpha(
                    _settings.GlyphDiscoveryAccentColor,
                    _settings.GlyphDiscoveryAccentColor.a * progress));

            float padding = _settings.GlyphDiscoveryPanelPadding;
            float gap = _settings.GlyphDiscoveryElementGap;
            float identityWidth = Mathf.Min(
                _settings.GlyphDiscoveryIdentityWidth,
                Mathf.Max(0f, panel.width - (padding * 2f)));
            Rect identity = new Rect(
                panel.x + padding,
                panel.y + padding,
                identityWidth,
                Mathf.Max(0f, panel.height - (padding * 2f)));
            DrawRect(
                identity,
                WithAlpha(
                    _settings.GlyphDiscoveryRaisedColor,
                    _settings.GlyphDiscoveryRaisedColor.a * progress));
            DrawBorder(
                identity,
                WithAlpha(
                    _settings.GlyphDiscoveryBorderColor,
                    _settings.GlyphDiscoveryBorderColor.a * progress),
                _settings.GlyphDiscoveryBorderThickness);

            float thumbnailSize = Mathf.Min(
                _settings.GlyphDiscoveryThumbnailSize,
                Mathf.Min(identity.width, identity.height));
            Rect thumbnail = new Rect(
                identity.x + ((identity.width - thumbnailSize) * 0.5f),
                identity.y + gap,
                thumbnailSize,
                thumbnailSize);
            Texture2D thumbnailTexture = _pendingSubject.Artwork?.DiscoveryThumbnail != null
                ? _pendingSubject.Artwork.DiscoveryThumbnail
                : _pendingSubject.Artwork?.Mask;
            if (thumbnailTexture != null)
            {
                Color previousColor = GUI.color;
                GUI.color = WithAlpha(
                    _settings.GlyphDiscoveryAccentColor,
                    _settings.GlyphDiscoveryAccentColor.a * progress);
                GUI.DrawTexture(
                    thumbnail,
                    thumbnailTexture,
                    ScaleMode.ScaleToFit,
                    true);
                GUI.color = previousColor;
            }
            float scanNormalized = Mathf.Repeat(
                elapsed / Mathf.Max(0.01f, _settings.GlyphDiscoveryScanDuration),
                1f);
            DrawRect(
                new Rect(
                    thumbnail.x,
                    Mathf.Lerp(thumbnail.y, thumbnail.yMax, scanNormalized),
                    thumbnail.width,
                    _settings.GlyphDiscoveryScanLineHeight),
                WithAlpha(
                    _settings.GlyphDiscoveryAccentColor,
                    _settings.GlyphDiscoveryAccentColor.a * progress));

            int entryNumber = GetGlyphEntryNumber();
            DrawLabel(
                new Rect(
                    identity.x,
                    thumbnail.yMax + gap,
                    identity.width,
                    _settings.GlyphDiscoveryIdentityLabelHeight),
                string.Format(_settings.GlyphDiscoveryEntryFormat, entryNumber),
                _glyphDiscoveryIdentityStyle,
                WithAlpha(
                    _settings.GlyphDiscoverySecondaryTextColor,
                    _settings.GlyphDiscoverySecondaryTextColor.a * progress),
                true);

            float contentX = identity.xMax + _settings.GlyphDiscoveryIdentityGap;
            float contentWidth = Mathf.Max(0f, panel.xMax - padding - contentX);
            float y = panel.y + padding;
            DrawLabel(
                new Rect(
                    contentX,
                    y,
                    contentWidth,
                    _settings.GlyphDiscoveryHeaderHeight),
                TrackText(_settings.RegisteredText),
                _glyphDiscoveryHeaderStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryAccentColor,
                    _settings.GlyphDiscoveryAccentColor.a * progress),
                true);
            y += _settings.GlyphDiscoveryHeaderHeight + gap;
            DrawLabel(
                new Rect(
                    contentX,
                    y,
                    contentWidth,
                    _settings.GlyphDiscoveryMetadataHeight),
                string.Format(_settings.GlyphDiscoveryMetadataFormat, entryNumber),
                _glyphDiscoveryMetadataStyle,
                WithAlpha(
                    _settings.GlyphDiscoverySecondaryTextColor,
                    _settings.GlyphDiscoverySecondaryTextColor.a * progress),
                true);
            y += _settings.GlyphDiscoveryMetadataHeight + gap;
            DrawLabel(
                new Rect(
                    contentX,
                    y,
                    contentWidth,
                    _settings.GlyphDiscoveryNameHeight),
                _pendingSubject.DisplayName,
                _glyphDiscoveryTitleStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryPrimaryTextColor,
                    _settings.GlyphDiscoveryPrimaryTextColor.a * progress),
                true);
            y += _settings.GlyphDiscoveryNameHeight + gap;
            string lore = _pendingSubject.AtlasSite != null
                ? _pendingSubject.AtlasSite.Description
                : string.Empty;
            DrawLabel(
                new Rect(
                    contentX,
                    y,
                    contentWidth,
                    _settings.GlyphDiscoveryLoreHeight),
                lore,
                _glyphDiscoveryLoreStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryPrimaryTextColor,
                    _settings.GlyphDiscoveryPrimaryTextColor.a * progress),
                true);

            Rect footer = new Rect(
                contentX,
                panel.yMax - padding - _settings.GlyphDiscoveryFooterHeight,
                contentWidth,
                _settings.GlyphDiscoveryFooterHeight);
            DrawRect(
                new Rect(
                    footer.x,
                    footer.y,
                    footer.width,
                    _settings.GlyphDiscoveryBorderThickness),
                WithAlpha(
                    _settings.GlyphDiscoveryBorderColor,
                    _settings.GlyphDiscoveryBorderColor.a * progress));
            string continuePrompt = _settings.GlyphDiscoveryContinuePrompt;
            string trackedContinuePrompt = TrackText(continuePrompt);
            float continueTextWidth = _glyphDiscoveryContinueStyle
                .CalcSize(new GUIContent(trackedContinuePrompt)).x;
            float commandWidth = Mathf.Min(
                footer.width,
                Mathf.Max(
                    _settings.GlyphDiscoveryCommandWidth,
                    continueTextWidth + (_settings.GlyphDiscoveryCommandTextPadding * 2f)));
            DrawLabel(
                new Rect(
                    footer.x,
                    footer.y,
                    Mathf.Max(0f, footer.width - commandWidth - gap),
                    footer.height),
                TrackText(_settings.GlyphDiscoveryArchivedLabel),
                _glyphDiscoveryArchivedStyle,
                WithAlpha(
                    _settings.GlyphDiscoverySecondaryTextColor,
                    _settings.GlyphDiscoverySecondaryTextColor.a * progress),
                true);

            Rect command = new Rect(
                footer.xMax - commandWidth,
                footer.y + gap,
                commandWidth,
                Mathf.Max(0f, footer.height - gap));
            bool commandHovered = command.Contains(Event.current.mousePosition);
            Color commandColor = commandHovered
                ? _settings.GlyphDiscoveryCommandHoverColor
                : _settings.GlyphDiscoveryCommandColor;
            float continueProgress = Mathf.Clamp01(
                (elapsed - _settings.GlyphDiscoveryContinueRevealDelay) /
                Mathf.Max(0.01f, _settings.HudEnterDuration));
            DrawRect(
                command,
                WithAlpha(commandColor, commandColor.a * continueProgress));
            DrawBorder(
                command,
                WithAlpha(
                    _settings.GlyphDiscoveryAccentColor,
                    _settings.GlyphDiscoveryAccentColor.a * continueProgress),
                _settings.GlyphDiscoveryBorderThickness);
            float sweepTravel = command.width + _settings.GlyphDiscoveryFocusSweepWidth;
            float sweepNormalized = Mathf.Repeat(
                Mathf.Max(0f, elapsed - _settings.GlyphDiscoveryContinueRevealDelay) /
                Mathf.Max(0.01f, _settings.GlyphDiscoveryFocusSweepDuration),
                1f);
            Rect sweep = new Rect(
                command.x - _settings.GlyphDiscoveryFocusSweepWidth +
                    (sweepTravel * sweepNormalized),
                command.y,
                _settings.GlyphDiscoveryFocusSweepWidth,
                command.height);
            Rect clippedSweep = Intersect(sweep, command);
            if (clippedSweep.width > 0f)
            {
                DrawRect(
                    clippedSweep,
                    WithAlpha(
                        _settings.GlyphDiscoveryFocusSweepColor,
                        _settings.GlyphDiscoveryFocusSweepColor.a * continueProgress));
            }
            DrawLabel(
                command,
                trackedContinuePrompt,
                _glyphDiscoveryContinueStyle,
                WithAlpha(
                    _settings.GlyphDiscoveryPrimaryTextColor,
                    _settings.GlyphDiscoveryPrimaryTextColor.a * continueProgress),
                true);
        }

        private int GetGlyphEntryNumber()
        {
            if (_pendingSubject.AtlasSite == null || _atlasSettings?.Sites == null)
            {
                return 0;
            }

            for (int i = 0; i < _atlasSettings.Sites.Count; i++)
            {
                DesertAtlasSiteDefinition site = _atlasSettings.Sites[i];
                if (site != null &&
                    string.Equals(
                        site.PersistentId,
                        _pendingSubject.AtlasSite.PersistentId,
                        StringComparison.Ordinal))
                {
                    return i + 1;
                }
            }
            return 0;
        }

        private static Rect Intersect(Rect first, Rect second)
        {
            float left = Mathf.Max(first.xMin, second.xMin);
            float top = Mathf.Max(first.yMin, second.yMin);
            float right = Mathf.Min(first.xMax, second.xMax);
            float bottom = Mathf.Min(first.yMax, second.yMax);
            return Rect.MinMaxRect(left, top, Mathf.Max(left, right), Mathf.Max(top, bottom));
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        // Input system coordinates are bottom-left origin in real pixels; the HUD draws top-left
        // origin in scaled units.
        private Vector2 ScreenToHudPoint(Vector2 screenPoint)
        {
            float scale = Mathf.Max(0.0001f, _hudScale);
            return new Vector2(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
        }

        private void DrawComparisonCard(
            Rect card,
            Texture texture,
            string label,
            Color accent,
            bool focused,
            float imageWidth,
            float imageHeight,
            float progress)
        {
            float emphasis = focused ? 1f : _settings.ReplaceDecisionRestingAccent;
            Color cardAccent = WithAlpha(accent, accent.a * progress * emphasis);
            DrawRect(
                card,
                WithAlpha(
                    _settings.GlyphDiscoveryRaisedColor,
                    _settings.GlyphDiscoveryRaisedColor.a * progress));
            DrawBorder(card, cardAccent, _settings.GlyphDiscoveryBorderThickness);

            float padding = _settings.ComparisonCardPadding;
            Rect imageRect = new Rect(card.x + padding, card.y + padding, imageWidth, imageHeight);
            DrawRect(
                imageRect,
                WithAlpha(
                    _settings.GalleryBackdropColor,
                    _settings.GalleryBackdropColor.a * progress));
            if (texture != null)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, progress * Mathf.Lerp(0.78f, 1f, emphasis));
                GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleAndCrop, false);
                GUI.color = previous;
            }
            DrawCorners(
                imageRect,
                _settings.ReplaceDecisionCornerLength,
                _settings.FrameThickness,
                cardAccent);

            Rect labelRect = new Rect(
                card.x + padding,
                imageRect.yMax + padding,
                imageWidth,
                _settings.ComparisonLabelHeight);
            DrawRect(
                labelRect,
                WithAlpha(
                    _settings.ComparisonLabelPanelColor,
                    _settings.ComparisonLabelPanelColor.a * progress));
            DrawRect(
                new Rect(
                    labelRect.x,
                    labelRect.y,
                    labelRect.width,
                    _settings.GlyphDiscoveryBorderThickness),
                cardAccent);
            DrawLabel(
                labelRect,
                TrackText(label),
                _comparisonLabelStyle,
                WithAlpha(
                    focused ? accent : _settings.ComparisonLabelColor,
                    _settings.ComparisonLabelColor.a * progress * Mathf.Lerp(0.72f, 1f, emphasis)),
                true);
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

        // Control hints are onboarding, not permanent chrome: the bar auto-sizes to its content,
        // holds for CommandHintDuration on the first camera use, then fades out for good.
        private float GetCommandHintOpacity()
        {
            if (_commandHintStartedAt < 0f) return 0f;
            float elapsed = Time.unscaledTime - _commandHintStartedAt;
            if (elapsed <= _settings.CommandHintDuration) return 1f;
            float fade = Mathf.Max(0.05f, _settings.CommandHintFadeDuration);
            return 1f - Mathf.Clamp01((elapsed - _settings.CommandHintDuration) / fade);
        }

        private void DrawCommandBar(float bandBottom, float enter)
        {
            float opacity = GetCommandHintOpacity() * enter;
            if (opacity <= 0.001f) return;

            float gap = _settings.TargetLabelGap * 2f;
            float exitWidth = GetCommandGroupWidth(_settings.ExitAction, gap);
            float captureWidth = GetCommandGroupWidth(_settings.CaptureAction, gap);
            float zoomWidth = GetCommandGroupWidth(_settings.ZoomAction, gap);
            float content = exitWidth + captureWidth + zoomWidth + (_settings.CommandGroupGap * 2f);
            float maxWidth = Mathf.Min(
                _settings.CommandBarWidth,
                _hudWidth - (_settings.ScreenMargin * 2f));
            float width = Mathf.Min(content + (_settings.CommandBarPadding * 2f), maxWidth);
            Rect bar = new Rect(
                (_hudWidth - width) * 0.5f,
                bandBottom - _settings.CommandBarHeight +
                    ((1f - opacity) * _settings.HudEnterSlideDistance),
                width,
                _settings.CommandBarHeight);

            DrawRect(bar, WithAlpha(
                _settings.CommandBackdropColor,
                _settings.CommandBackdropColor.a * opacity));
            if (_settings.SurfaceTexturesEnabled &&
                _settings.TechnicalGridTexture != null &&
                _settings.TechnicalGridOpacity > 0f)
            {
                DrawTexture(
                    bar,
                    _settings.TechnicalGridTexture,
                    new Color(1f, 1f, 1f, _settings.TechnicalGridOpacity * opacity),
                    ScaleMode.StretchToFill);
            }
            DrawBorder(
                bar,
                WithAlpha(
                    _settings.HudMutedColor,
                    _settings.HudMutedColor.a * _settings.OuterFrameOpacity * opacity),
                _settings.FrameThickness);
            DrawCorners(
                bar,
                Mathf.Min(_settings.FrameCornerLength, bar.width * 0.25f),
                _settings.FrameThickness,
                WithAlpha(_settings.NeutralColor, _settings.NeutralColor.a * opacity));

            float scale = content > 0f
                ? Mathf.Min(1f, (bar.width - (_settings.CommandBarPadding * 2f)) / content)
                : 1f;
            float groupGap = _settings.CommandGroupGap * scale;
            float cursor = bar.center.x - ((content * scale) * 0.5f);
            cursor = DrawCommandGroup(
                bar, cursor, exitWidth * scale, _settings.ExitKey, _settings.ExitAction, opacity);
            DrawCommandSeparator(bar, cursor + (groupGap * 0.5f), opacity);
            cursor += groupGap;
            cursor = DrawCommandGroup(
                bar, cursor, captureWidth * scale, _settings.CaptureKey, _settings.CaptureAction, opacity);
            DrawCommandSeparator(bar, cursor + (groupGap * 0.5f), opacity);
            cursor += groupGap;
            DrawCommandGroup(
                bar, cursor, zoomWidth * scale, _settings.ZoomKey, _settings.ZoomAction, opacity);
        }

        private float GetCommandGroupWidth(string action, float gap)
        {
            float labelWidth = _commandStyle.CalcSize(new GUIContent(TrackText(action))).x;
            return _settings.CommandKeyWidth + gap + labelWidth;
        }

        private float DrawCommandGroup(
            Rect bar,
            float left,
            float width,
            string key,
            string action,
            float opacity)
        {
            Rect keyRect = new Rect(
                left,
                bar.center.y - (_settings.CommandKeyHeight * 0.5f),
                _settings.CommandKeyWidth,
                _settings.CommandKeyHeight);
            DrawRect(keyRect, WithAlpha(
                _settings.KeycapColor,
                _settings.KeycapColor.a * opacity));
            DrawBorder(
                keyRect,
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * 0.7f * opacity),
                _settings.FrameThickness);
            DrawCorners(
                keyRect,
                Mathf.Min(_settings.TargetStatusHeight, _settings.CommandKeyWidth * 0.4f),
                _settings.FrameThickness,
                WithAlpha(_settings.NeutralColor, _settings.NeutralColor.a * opacity));
            DrawLabel(
                keyRect,
                key,
                _keyStyle,
                WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * opacity),
                true);
            DrawLabel(
                new Rect(
                    keyRect.xMax + _settings.TargetLabelGap,
                    bar.y,
                    Mathf.Max(0f, left + width - keyRect.xMax - _settings.TargetLabelGap),
                    bar.height),
                TrackText(action),
                _commandStyle,
                WithAlpha(_settings.HudTextColor, _settings.HudTextColor.a * 0.78f * opacity),
                true);
            return left + width;
        }

        private void DrawCommandSeparator(Rect bar, float x, float opacity)
        {
            float height = bar.height * 0.34f;
            DrawRect(
                new Rect(
                    x - (_settings.FrameThickness * 0.5f),
                    bar.center.y - (height * 0.5f),
                    _settings.FrameThickness,
                    height),
                WithAlpha(_settings.HudMutedColor, _settings.HudMutedColor.a * 0.45f * opacity));
        }

        private void DrawCornerMetadata(float y, float enter)
        {
            float metadataHeight = _settings.CornerMetadataHeight;
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
                _settings.CommandKeyFontSize,
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
            _replaceEyebrowStyle ??= CreateStyle(
                _settings.GlyphDiscoveryHeaderFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.ValidColor,
                _settings.HudSemiboldFont);
            _identificationNameStyle ??= CreateStyle(
                _settings.IdentificationNameFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.HudTextColor,
                _settings.HudSemiboldFont);
            _glyphDiscoveryHeaderStyle ??= CreateStyle(
                _settings.GlyphDiscoveryHeaderFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                _settings.GlyphDiscoveryAccentColor,
                _settings.HudSemiboldFont);
            _glyphDiscoveryMetadataStyle ??= CreateStyle(
                _settings.GlyphDiscoveryMetadataFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                _settings.GlyphDiscoverySecondaryTextColor,
                _settings.HudRegularFont);
            _glyphDiscoveryIdentityStyle ??= CreateStyle(
                _settings.GlyphDiscoveryMetadataFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.GlyphDiscoverySecondaryTextColor,
                _settings.HudRegularFont);
            _glyphDiscoveryTitleStyle ??= CreateStyle(
                _settings.GlyphDiscoveryTitleFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                _settings.GlyphDiscoveryPrimaryTextColor,
                _settings.HudSemiboldFont);
            _glyphDiscoveryLoreStyle ??= CreateStyle(
                _settings.GlyphDiscoveryLoreFontSize,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                _settings.GlyphDiscoveryPrimaryTextColor,
                _settings.HudRegularFont);
            _glyphDiscoveryLoreStyle.wordWrap = true;
            _glyphDiscoveryArchivedStyle ??= CreateStyle(
                _settings.GlyphDiscoveryMetadataFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                _settings.GlyphDiscoverySecondaryTextColor,
                _settings.HudRegularFont);
            _glyphDiscoveryContinueStyle ??= CreateStyle(
                _settings.GlyphDiscoveryContinueFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.GlyphDiscoveryPrimaryTextColor,
                _settings.HudSemiboldFont);
            _glyphDiscoveryContinueStyle.wordWrap = false;
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
                if (i >= value.Length - 1)
                {
                    continue;
                }
                tracked.Append(_settings.TextTrackingSpacer);
                // Letter tracking closes to roughly a space's width, so word breaks need extra
                // padding or the words read as one run.
                if (char.IsWhiteSpace(current) || char.IsWhiteSpace(value[i + 1]))
                {
                    tracked.Append(_settings.TextTrackingSpacer);
                    tracked.Append(_settings.TextTrackingSpacer);
                }
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
                    _settings != null ? _settings.CameraHeight : 0f,
                    _settings != null ? _settings.MinPitch : -85f,
                    _settings != null ? _settings.MaxPitch : 85f);
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
