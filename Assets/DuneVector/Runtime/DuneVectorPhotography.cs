using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

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
                    if (_data.Documentation[i].IsNew) return true;
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

        public PhotographableSubject(DesertAtlasSiteDefinition site, GeoglyphArtworkPlacement artwork)
        {
            SubjectId = site.PersistentId;
            DisplayName = site.DisplayName;
            Category = PhotographableSubjectCategory.Glyph;
            AtlasSite = site;
            Artwork = artwork;
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
        private readonly DesertWorldStreamer _world;
        private readonly GeoglyphSystemTuning _geoglyphs;
        private readonly DesertAtlasTuning _atlas;
        private readonly PhotographyTuning _settings;
        private readonly List<Vector3> _worldSamples = new List<Vector3>(40);
        private int _centerSampleIndex;

        public DuneVectorSubjectDetector(Camera camera, DesertWorldStreamer world, GeoglyphSystemTuning geoglyphs, DesertAtlasTuning atlas, PhotographyTuning settings)
        {
            _camera = camera;
            _world = world;
            _geoglyphs = geoglyphs;
            _atlas = atlas;
            _settings = settings;
        }

        public SubjectDetectionResult Detect()
        {
            if (_camera == null || _world == null || _geoglyphs?.Placements == null || _atlas?.Sites == null)
            {
                return default;
            }

            bool found = false;
            PhotographableSubject bestSubject = default;
            Rect bestBounds = default;
            float bestPriority = float.PositiveInfinity;
            float bestCoverage = 0f;
            for (int siteIndex = 0; siteIndex < _atlas.Sites.Count; siteIndex++)
            {
                DesertAtlasSiteDefinition site = _atlas.Sites[siteIndex];
                if (site == null || string.IsNullOrWhiteSpace(site.PersistentId)) continue;
                GeoglyphArtworkPlacement artwork = FindArtwork(site);
                if (artwork == null) continue;
                BuildSamples(site, artwork);
                if (!TryProjectBounds(out Rect bounds, out float coverage, out float priority)) continue;
                if (priority >= bestPriority) continue;
                bestPriority = priority;
                bestBounds = bounds;
                bestCoverage = coverage;
                bestSubject = new PhotographableSubject(site, artwork);
                found = true;
            }
            if (!found) return default;

            BuildSamples(bestSubject.AtlasSite, bestSubject.Artwork);
            float visiblePercentage = CalculateVisiblePercentage(bestSubject.AtlasSite);
            DesertAtlasSiteDefinition definition = bestSubject.AtlasSite;
            float minimumCoverage = Mathf.Min(definition.MinimumPhotoScreenCoverage, definition.MaximumPhotoScreenCoverage);
            float maximumCoverage = Mathf.Max(definition.MinimumPhotoScreenCoverage, definition.MaximumPhotoScreenCoverage);
            float readableAngle = Vector3.Dot(_camera.transform.forward.normalized, Vector3.down);
            bool fullyFramed = bestBounds.xMin >= Screen.width * _settings.ViewportEdgePadding &&
                bestBounds.xMax <= Screen.width * (1f - _settings.ViewportEdgePadding) &&
                bestBounds.yMin >= Screen.height * _settings.ViewportEdgePadding &&
                bestBounds.yMax <= Screen.height * (1f - _settings.ViewportEdgePadding);
            float requiredVisibility = definition.AllowPartialPhotoOcclusion
                ? definition.RequiredPhotoVisiblePercentage
                : 1f;
            bool visibilityValid = visiblePercentage >= requiredVisibility;
            bool valid = fullyFramed && bestCoverage >= minimumCoverage && bestCoverage <= maximumCoverage &&
                readableAngle >= definition.MinimumPhotoReadableAngle && visibilityValid;
            return new SubjectDetectionResult(true, valid, bestSubject, bestBounds, bestCoverage, visiblePercentage);
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
                    int index = (row * columns) + column;
                    if (index >= _storage.Photographs.Count) break;
                    PhotographRecord record = _storage.Photographs[index];
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

        private DronePlayer _player;
        private DroneCameraController _cameraController;
        private Camera _camera;
        private PhotographyTuning _settings;
        private DuneVectorPhotographStorage _storage;
        private DuneVectorSubjectDetector _detector;
        private DuneVectorGalleryView _gallery;
        private SubjectDetectionResult _detection;
        private Color _animatedAccentColor;
        private Rect _animatedBounds;
        private bool _hasAnimatedBounds;
        private bool _cameraModeActive;
        private float _baseFieldOfView;
        private float _zoom = 1f;
        private float _targetZoom = 1f;
        private float _nextValidationTime;
        private float _shutterUntil;
        private float _presentationUntil;
        private float _timeScaleBeforeIdentification = 1f;
        private bool _identificationPauseActive;
        private CameraPresentationState _presentationState;
        private Texture2D _capturedTexture;
        private PhotographRecord _pendingPhotograph;
        private PhotographableSubject _pendingSubject;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
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
            _detector = new DuneVectorSubjectDetector(_camera, world, geoglyphs, atlas, _settings);
            _gallery = new DuneVectorGalleryView(_storage, _settings);
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
            }
            if (_detection.HasSubject)
            {
                Rect padded = ClampToViewfinder(
                    Expand(_detection.ScreenBounds, _settings.TargetBracketPadding));
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
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _detection = default;
            _animatedAccentColor = _settings.NeutralColor;
            _nextValidationTime = 0f;
        }

        private void ExitCameraMode()
        {
            EndIdentificationPause();
            _cameraModeActive = false;
            _camera.fieldOfView = _baseFieldOfView;
            _cameraController.SetPhotographyMode(false, _settings.CameraDistance, _settings.CameraHeight);
            RestorePlayerRenderers();
            _player.SetDisabledFlightStopEnabled(false);
            _player.SetInputEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            ReleaseCapturedTexture();
            _pendingPhotograph = null;
            _detection = default;
        }

        private void CapturePhotograph()
        {
            Texture2D image = CaptureCameraImage();
            if (image == null) return;
            bool valid = _detection.HasSubject && _detection.IsValid;
            string subjectId = valid ? _detection.Subject.SubjectId : string.Empty;
            PhotographableSubjectCategory category = valid
                ? _detection.Subject.Category
                : PhotographableSubjectCategory.Glyph;
            PhotographRecord record = _storage.Store(image, subjectId, category, valid);
            _shutterUntil = Time.unscaledTime + _settings.ShutterFlashDuration;
            if (!valid)
            {
                UnityEngine.Object.Destroy(image);
                return;
            }

            ReleaseCapturedTexture();
            _capturedTexture = image;
            _pendingPhotograph = record;
            _pendingSubject = _detection.Subject;
            if (!_storage.IsDocumented(subjectId))
            {
                _storage.Document(subjectId, category, record.PhotographId);
                _presentationState = CameraPresentationState.Identified;
                _presentationUntil = Time.unscaledTime + _settings.IdentificationHoldDuration;
                BeginIdentificationPause();
            }
            else
            {
                _presentationState = CameraPresentationState.ReplacePrompt;
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
            if (_presentationState == CameraPresentationState.Live)
            {
                DrawViewfinder();
            }
            else
            {
                DrawCapturePresentation();
            }
            if (Time.unscaledTime < _shutterUntil)
            {
                float alpha = Mathf.Clamp01((_shutterUntil - Time.unscaledTime) / Mathf.Max(0.01f, _settings.ShutterFlashDuration));
                Color flash = _settings.ShutterFlashColor;
                flash.a *= alpha;
                DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), flash);
            }
        }

        private void DrawViewfinder()
        {
            Color targetAccent = !_detection.HasSubject
                ? _settings.NeutralColor
                : _detection.IsValid ? _settings.ValidColor : _settings.InvalidColor;
            _animatedAccentColor = Color.Lerp(
                _animatedAccentColor,
                targetAccent,
                DuneVectorMath.Sharpness(_settings.BracketSharpness, Time.unscaledDeltaTime));
            Color accent = _animatedAccentColor;
            Rect frame = new Rect(_settings.ScreenMargin, _settings.ScreenMargin,
                Screen.width - (_settings.ScreenMargin * 2f), Screen.height - (_settings.ScreenMargin * 2f));
            DrawCorners(frame, _settings.FrameCornerLength, _settings.FrameThickness, accent);
            Rect crosshair = new Rect(Screen.width * 0.5f - (_settings.CrosshairSize * 0.5f),
                Screen.height * 0.5f - (_settings.CrosshairSize * 0.5f), _settings.CrosshairSize, _settings.CrosshairSize);
            DrawCrosshair(crosshair, accent);
            if (_detection.HasSubject && _hasAnimatedBounds)
            {
                DrawCorners(_animatedBounds, _settings.TargetBracketLength, _settings.TargetBracketThickness, accent);
                string subjectLabel = _storage.IsDocumented(_detection.Subject.SubjectId)
                    ? _detection.Subject.DisplayName
                    : _settings.UnknownSubjectLabel;
                float labelLeft = Mathf.Clamp(
                    _animatedBounds.center.x - (_settings.SubjectLabelWidth * 0.5f),
                    _settings.ScreenMargin,
                    Screen.width - _settings.ScreenMargin - _settings.SubjectLabelWidth);
                float titleClearance = _settings.ScreenMargin + _settings.SubjectLabelHeight;
                float labelTop = Mathf.Clamp(
                    _animatedBounds.center.y - (_settings.SubjectLabelHeight * 0.5f),
                    titleClearance,
                    Screen.height - _settings.ScreenMargin - _settings.SubjectLabelHeight);
                GUI.Label(new Rect(labelLeft, labelTop,
                    _settings.SubjectLabelWidth, _settings.SubjectLabelHeight), subjectLabel, _labelStyle);
            }
            string status = !_detection.HasSubject ? _settings.NeutralStatus : _detection.IsValid ? _settings.ValidStatus : _settings.InvalidStatus;
            GUI.Label(new Rect(_settings.ScreenMargin, _settings.ScreenMargin,
                Screen.width - (_settings.ScreenMargin * 2f), _settings.SubjectLabelHeight), _settings.CameraTitle, _statusStyle);
            GUI.Label(new Rect(_settings.ScreenMargin, Screen.height - _settings.ScreenMargin - _settings.SubjectLabelHeight,
                Screen.width - (_settings.ScreenMargin * 2f), _settings.SubjectLabelHeight),
                string.Format(_settings.StatusZoomFormat, status, _zoom), _statusStyle);
            GUI.Label(new Rect(_settings.ScreenMargin, Screen.height - (_settings.ScreenMargin * 2f) - _settings.SubjectLabelHeight,
                Screen.width - (_settings.ScreenMargin * 2f), _settings.SubjectLabelHeight), _settings.ExitHint, _statusStyle);
        }

        private void DrawCapturePresentation()
        {
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), _settings.GalleryBackdropColor);
            Rect imageRect = new Rect(0f, 0f, Screen.width, Screen.height);
            if (_capturedTexture != null) GUI.DrawTexture(imageRect, _capturedTexture, ScaleMode.ScaleToFit, false);
            if (_presentationState == CameraPresentationState.ReplacePrompt)
            {
                DrawPhotographComparison();
            }
            Rect panel = new Rect((Screen.width - _settings.IdentificationPanelWidth) * 0.5f,
                Screen.height - _settings.IdentificationPanelHeight - _settings.ScreenMargin,
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
            if (_presentationState == CameraPresentationState.Identified)
            {
                GUI.Label(new Rect(panel.x + padding, panel.yMax - padding - _settings.SubjectLabelHeight,
                    panel.width - (padding * 2f), _settings.SubjectLabelHeight), _settings.RegisteredText, _statusStyle);
                return;
            }
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
            float totalWidth = (_settings.ComparisonImageWidth * 2f) + _settings.ComparisonImageGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = _settings.ScreenMargin;
            Rect currentRect = new Rect(left, top, _settings.ComparisonImageWidth, _settings.ComparisonImageHeight);
            Rect newRect = new Rect(currentRect.xMax + _settings.ComparisonImageGap, top,
                _settings.ComparisonImageWidth, _settings.ComparisonImageHeight);
            Texture2D current = _storage.GetCanonicalTexture(_pendingSubject.SubjectId);
            DrawRect(currentRect, _settings.IdentificationPanelColor);
            DrawRect(newRect, _settings.IdentificationPanelColor);
            DrawBorder(currentRect, _settings.NeutralColor, _settings.FrameThickness);
            DrawBorder(newRect, _settings.ValidColor, _settings.FrameThickness);
            if (current != null) GUI.DrawTexture(currentRect, current, ScaleMode.ScaleToFit, false);
            if (_capturedTexture != null) GUI.DrawTexture(newRect, _capturedTexture, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(currentRect.x, currentRect.yMax, currentRect.width, _settings.SubjectLabelHeight),
                _settings.ComparisonCurrentLabel, _statusStyle);
            GUI.Label(new Rect(newRect.x, newRect.yMax, newRect.width, _settings.SubjectLabelHeight),
                _settings.ComparisonNewLabel, _statusStyle);
        }

        private void EnsureStyles()
        {
            _labelStyle ??= CreateStyle(_settings.SubjectLabelFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _statusStyle ??= CreateStyle(_settings.StatusFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _identificationTitleStyle ??= CreateStyle(_settings.IdentificationTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _identificationNameStyle ??= CreateStyle(_settings.IdentificationNameFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = _settings.GalleryBodyFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private static GUIStyle CreateStyle(int size, FontStyle style, TextAnchor anchor, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = anchor,
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

        private static void DrawCorners(Rect rect, float length, float thickness, Color color)
        {
            DrawRect(new Rect(rect.x, rect.y, length, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, length), color);
            DrawRect(new Rect(rect.xMax - length, rect.y, length, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, length), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, length, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - length, thickness, length), color);
            DrawRect(new Rect(rect.xMax - length, rect.yMax - thickness, length, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.yMax - length, thickness, length), color);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + (amount * 2f), rect.height + (amount * 2f));
        }

        private Rect ClampToViewfinder(Rect rect)
        {
            float minimumX = _settings.ScreenMargin;
            float minimumY = _settings.ScreenMargin + _settings.SubjectLabelHeight;
            float maximumX = Screen.width - _settings.ScreenMargin;
            float maximumY = Screen.height - _settings.ScreenMargin;
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

        private static Rect Lerp(Rect from, Rect to, float t)
        {
            return new Rect(
                Mathf.Lerp(from.x, to.x, t),
                Mathf.Lerp(from.y, to.y, t),
                Mathf.Lerp(from.width, to.width, t),
                Mathf.Lerp(from.height, to.height, t));
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
            _storage?.Dispose();
        }
    }
}
