using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    // Stable IDs keep type-level discoveries consistent across streamed object instances.
    public static class DuneVectorCompendiumSubjectIds
    {
        public const string GroundExploder = "enemy:ground-exploder";
        public const string SkyPiercer = "enemy:sky-piercer";
        public const string StormPyramid = "enemy:storm-pyramid";
        public const string PlayerStrikeOrb = "enemy:player-strike-orb";
        public const string FormationEnemy = "enemy:formation";
        public const string SandAmbusher = "enemy:sand-ambusher";
        public const string Pyramid = "misc:pyramid";
        public const string Hub = "misc:hub";

        public static string ForLandmark(DuneLandmarkType type)
        {
            return $"landmark:{(int)type}";
        }

        public static string ForRing(TraversalRingType type)
        {
            return $"misc:ring:{(int)type}";
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorPhotographableMarker : MonoBehaviour
    {
        private static readonly HashSet<DuneVectorPhotographableMarker> ActiveMarkerSet =
            new HashSet<DuneVectorPhotographableMarker>();

        public static IReadOnlyCollection<DuneVectorPhotographableMarker> ActiveMarkers => ActiveMarkerSet;
        public string SubjectId { get; private set; }
        public PhotographableSubjectCategory Category { get; private set; }

        private readonly RaycastHit[] _occlusionHits = new RaycastHit[32];
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private float _nextRendererRefresh;
        private bool _hasCustomFramingBounds;
        private Bounds _customFramingBounds;

        public static DuneVectorPhotographableMarker Register(
            GameObject subjectRoot,
            string subjectId,
            PhotographableSubjectCategory category,
            Bounds? localFramingBounds = null)
        {
            if (subjectRoot == null || string.IsNullOrWhiteSpace(subjectId))
            {
                return null;
            }

            DuneVectorPhotographableMarker marker =
                subjectRoot.GetComponent<DuneVectorPhotographableMarker>() ??
                subjectRoot.AddComponent<DuneVectorPhotographableMarker>();
            marker.Initialize(subjectId, category, localFramingBounds);
            return marker;
        }

        public void Initialize(
            string subjectId,
            PhotographableSubjectCategory category,
            Bounds? localFramingBounds = null)
        {
            SubjectId = subjectId;
            Category = category;
            _hasCustomFramingBounds = localFramingBounds.HasValue;
            _customFramingBounds = localFramingBounds.GetValueOrDefault();
            RefreshRenderers();
        }

        private void OnEnable()
        {
            ActiveMarkerSet.Add(this);
        }

        private void OnDisable()
        {
            ActiveMarkerSet.Remove(this);
        }

        public bool TryGetScreenBounds(
            Camera camera,
            out Rect bounds,
            out float coverage)
        {
            bounds = default;
            coverage = 0f;
            if (camera == null || string.IsNullOrWhiteSpace(SubjectId))
            {
                return false;
            }

            if (_renderers == null || _renderers.Length == 0 || Time.unscaledTime >= _nextRendererRefresh)
            {
                RefreshRenderers();
            }

            if (_hasCustomFramingBounds)
            {
                return TryProjectBounds(
                    camera,
                    transform,
                    _customFramingBounds,
                    out bounds,
                    out coverage);
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool hasProjectedPoint = false;
            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer renderer = _renderers[rendererIndex];
                if (!IsFramingRenderer(renderer))
                {
                    continue;
                }

                Bounds localBounds = renderer.localBounds;
                Vector3 worldCenter = renderer.transform.TransformPoint(localBounds.center);
                Vector3 centerViewport = camera.WorldToViewportPoint(worldCenter);
                if (centerViewport.z <= camera.nearClipPlane)
                {
                    continue;
                }

                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localPoint = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 point = renderer.transform.TransformPoint(localPoint);
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (viewport.z <= camera.nearClipPlane)
                    {
                        continue;
                    }

                    float x = viewport.x * Screen.width;
                    float y = (1f - viewport.y) * Screen.height;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                    hasProjectedPoint = true;
                }
            }

            if (!hasProjectedPoint || maxX <= minX || maxY <= minY)
            {
                return false;
            }

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            Rect intersection = Intersect(bounds, new Rect(0f, 0f, Screen.width, Screen.height));
            coverage = intersection.width * intersection.height /
                Mathf.Max(1f, Screen.width * Screen.height);
            return intersection.width > 0f && intersection.height > 0f;
        }

        private static bool TryProjectBounds(
            Camera camera,
            Transform boundsTransform,
            Bounds localBounds,
            out Rect bounds,
            out float coverage)
        {
            bounds = default;
            coverage = 0f;
            if (boundsTransform == null || localBounds.size.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool hasProjectedPoint = false;
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localPoint = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 viewport = camera.WorldToViewportPoint(
                    boundsTransform.TransformPoint(localPoint));
                if (viewport.z <= camera.nearClipPlane)
                {
                    continue;
                }

                float x = viewport.x * Screen.width;
                float y = (1f - viewport.y) * Screen.height;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
                hasProjectedPoint = true;
            }

            if (!hasProjectedPoint || maxX <= minX || maxY <= minY)
            {
                return false;
            }

            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            Rect intersection = Intersect(bounds, new Rect(0f, 0f, Screen.width, Screen.height));
            coverage = intersection.width * intersection.height /
                Mathf.Max(1f, Screen.width * Screen.height);
            return intersection.width > 0f && intersection.height > 0f;
        }

        public float CalculateVisiblePercentage(Camera camera, PhotographyTuning settings)
        {
            if (camera == null || settings == null)
            {
                return 0f;
            }

            Vector3 origin = camera.transform.position;
            if (_hasCustomFramingBounds && !HasFramingRenderer())
            {
                Vector3 point = transform.TransformPoint(_customFramingBounds.center);
                Vector3 viewport = camera.WorldToViewportPoint(point);
                return viewport.z > camera.nearClipPlane &&
                    IsPointVisible(origin, point, settings)
                        ? 1f
                        : 0f;
            }

            int visible = 0;
            int sampleCount = 0;
            for (int rendererIndex = 0; rendererIndex < _renderers.Length; rendererIndex++)
            {
                Renderer renderer = _renderers[rendererIndex];
                if (!IsFramingRenderer(renderer) || renderer.bounds.size.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                Vector3 point = renderer.transform.TransformPoint(renderer.localBounds.center);
                Vector3 viewport = camera.WorldToViewportPoint(point);
                if (viewport.z <= camera.nearClipPlane)
                {
                    continue;
                }

                sampleCount++;
                if (IsPointVisible(origin, point, settings))
                {
                    visible++;
                }
            }
            return sampleCount > 0 ? visible / (float)sampleCount : 0f;
        }

        private bool IsPointVisible(Vector3 origin, Vector3 point, PhotographyTuning settings)
        {
            Vector3 direction = point - origin;
            float distance = direction.magnitude;
            if (distance <= settings.OcclusionRayEndTolerance)
            {
                return true;
            }

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction / Mathf.Max(0.001f, distance),
                _occlusionHits,
                distance,
                settings.OcclusionLayers,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Transform hitTransform = _occlusionHits[hitIndex].transform;
                if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
                {
                    continue;
                }
                if (_occlusionHits[hitIndex].distance < distance - settings.OcclusionRayEndTolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasFramingRenderer()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (IsFramingRenderer(_renderers[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _nextRendererRefresh = Time.unscaledTime + 1f;
        }

        private static bool IsFramingRenderer(Renderer renderer)
        {
            return renderer != null &&
                renderer.enabled &&
                renderer.gameObject.activeInHierarchy &&
                renderer is not TrailRenderer &&
                renderer is not LineRenderer &&
                renderer is not ParticleSystemRenderer;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax > xMin && yMax > yMin
                ? Rect.MinMaxRect(xMin, yMin, xMax, yMax)
                : default;
        }
    }

    internal readonly struct DuneVectorCompendiumEntry
    {
        public readonly string SubjectId;
        public readonly string DisplayName;
        public readonly PhotographableSubjectCategory Category;
        public readonly string Description;
        public readonly string DiscoveryLocation;
        public readonly string FieldNotes;

        public DuneVectorCompendiumEntry(
            string subjectId,
            string displayName,
            PhotographableSubjectCategory category,
            string description = "",
            string discoveryLocation = "",
            string fieldNotes = "")
        {
            SubjectId = subjectId;
            DisplayName = displayName;
            Category = category;
            Description = description;
            DiscoveryLocation = discoveryLocation;
            FieldNotes = fieldNotes;
        }
    }

    internal sealed class DuneVectorCompendiumView : IDisposable
    {
        private static readonly PhotographableSubjectCategory[] Tabs =
        {
            PhotographableSubjectCategory.Glyph,
            PhotographableSubjectCategory.Landmark,
            PhotographableSubjectCategory.Enemy,
            PhotographableSubjectCategory.Misc,
        };

        private readonly DuneVectorPhotographStorage _storage;
        private readonly PhotographyTuning _settings;
        private readonly List<DuneVectorCompendiumEntry> _entries = new List<DuneVectorCompendiumEntry>();
        private readonly Texture2D[] _tabIcons = new Texture2D[4];
        private Vector2 _scroll;
        private int _selectedTab;
        private string _selectedSubjectId;
        private GUIStyle _titleStyle;
        private GUIStyle _detailTitleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _unknownStyle;
        private GUIStyle _tabStyle;
        private GUIStyle _buttonStyle;

        public DuneVectorCompendiumView(
            DuneVectorPhotographStorage storage,
            PhotographyTuning settings,
            DesertAtlasTuning atlas)
        {
            _storage = storage;
            _settings = settings;
            _tabIcons[0] = settings?.CompendiumGlyphTabIcon;
            _tabIcons[1] = settings?.CompendiumLandmarkTabIcon;
            _tabIcons[2] = settings?.CompendiumEnemyTabIcon;
            _tabIcons[3] = settings?.CompendiumMiscTabIcon;
            if (atlas?.Sites != null)
            {
                for (int i = 0; i < atlas.Sites.Count; i++)
                {
                    DesertAtlasSiteDefinition site = atlas.Sites[i];
                    if (site == null || string.IsNullOrWhiteSpace(site.PersistentId))
                    {
                        continue;
                    }
                    _entries.Add(new DuneVectorCompendiumEntry(
                        site.PersistentId,
                        site.DisplayName,
                        PhotographableSubjectCategory.Glyph));
                }
            }
            if (settings?.CompendiumEntries != null)
            {
                for (int i = 0; i < settings.CompendiumEntries.Count; i++)
                {
                    CompendiumEntryDefinition definition = settings.CompendiumEntries[i];
                    if (definition == null || string.IsNullOrWhiteSpace(definition.SubjectId))
                    {
                        continue;
                    }
                    _entries.Add(new DuneVectorCompendiumEntry(
                        definition.SubjectId,
                        definition.DisplayName,
                        definition.Category));
                }
            }
        }

        public bool TryResolve(string subjectId, out string displayName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].SubjectId, subjectId, StringComparison.Ordinal))
                {
                    displayName = _entries[i].DisplayName;
                    return true;
                }
            }
            displayName = string.Empty;
            return false;
        }

        public void Draw()
        {
            EnsureStyles();
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), _settings.GalleryBackdropColor);
            float scale = Mathf.Clamp(
                Mathf.Min(
                    Screen.width / _settings.GalleryReferenceWidth,
                    Screen.height / _settings.GalleryReferenceHeight),
                Mathf.Min(_settings.GalleryMinimumScale, _settings.GalleryMaximumScale),
                Mathf.Max(_settings.GalleryMinimumScale, _settings.GalleryMaximumScale));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float virtualWidth = Screen.width / scale;
                float virtualHeight = Screen.height / scale;
                Rect panel = new Rect(
                    (virtualWidth - _settings.CompendiumPanelWidth) * 0.5f,
                    (virtualHeight - _settings.CompendiumPanelHeight) * 0.5f,
                    _settings.CompendiumPanelWidth,
                    _settings.CompendiumPanelHeight);
                DrawRect(panel, _settings.GalleryPanelColor);
                DrawBorder(
                    panel,
                    _settings.GalleryAccentColor,
                    _settings.CompendiumPanelBorderThickness);
                DrawContents(panel);
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void DrawContents(Rect panel)
        {
            float padding = _settings.GalleryPadding;
            GUI.Label(
                new Rect(
                    panel.x + padding,
                    panel.y + padding,
                    panel.width - (padding * 2f),
                    _settings.CompendiumHeaderHeight),
                _settings.CompendiumTitle,
                _titleStyle);
            if (GUI.Button(
                    new Rect(
                        panel.xMax - padding - _settings.GalleryThumbnailWidth,
                        panel.y + padding,
                        _settings.GalleryThumbnailWidth,
                        _settings.GalleryButtonHeight),
                    _settings.GalleryDoneButton,
                    _buttonStyle))
            {
                DuneVectorPhotographySystem.RequestCloseCompendium();
            }

            float tabY = panel.y + padding + _settings.CompendiumHeaderHeight;
            float tabWidth = (panel.width - (padding * 2f) -
                ((_tabIcons.Length - 1) * _settings.CompendiumGap)) / _tabIcons.Length;
            for (int i = 0; i < _tabIcons.Length; i++)
            {
                Rect tab = new Rect(
                    panel.x + padding + (i * (tabWidth + _settings.CompendiumGap)),
                    tabY,
                    tabWidth,
                    _settings.CompendiumTabHeight);
                bool selected = i == _selectedTab;
                bool hovered = tab.Contains(Event.current.mousePosition);
                DrawRect(tab, selected
                    ? _settings.CompendiumSelectedTabColor
                    : _settings.CompendiumTabColor);
                DrawBorder(
                    tab,
                    hovered
                        ? _settings.CompendiumHoverBorderColor
                        : selected
                            ? _settings.GallerySelectionColor
                            : _settings.GalleryAccentColor,
                    _settings.FrameThickness);
                if (GUI.Button(tab, GUIContent.none, GUIStyle.none))
                {
                    _selectedTab = i;
                    _scroll = Vector2.zero;
                    _selectedSubjectId = null;
                }
                float iconSize = Mathf.Min(_settings.CompendiumTabIconSize, tab.height);
                Rect icon = new Rect(
                    tab.x + _settings.CompendiumGap,
                    tab.center.y - (iconSize * 0.5f),
                    iconSize,
                    iconSize);
                if (_tabIcons[i] != null)
                {
                    Color previousColor = GUI.color;
                    GUI.color = _settings.CompendiumIconColor;
                    GUI.DrawTexture(icon, _tabIcons[i], ScaleMode.ScaleToFit, true);
                    GUI.color = previousColor;
                }
                GUI.Label(
                    new Rect(
                        icon.xMax + _settings.CompendiumGap,
                        tab.y,
                        tab.width - icon.width - (_settings.CompendiumGap * 3f),
                        tab.height),
                    GetTabLabel(i),
                    _tabStyle);
            }

            Rect viewport = new Rect(
                panel.x + padding,
                tabY + _settings.CompendiumTabHeight + _settings.CompendiumGap,
                panel.width - (padding * 2f),
                panel.yMax - padding -
                    (tabY + _settings.CompendiumTabHeight + _settings.CompendiumGap));
            DrawEntryGrid(viewport, Tabs[_selectedTab]);
        }

        private void DrawEntryGrid(Rect viewport, PhotographableSubjectCategory category)
        {
            int matchingCount = 0;
            DuneVectorCompendiumEntry firstMatchingEntry = default;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Category == category)
                {
                    if (matchingCount == 0)
                    {
                        firstMatchingEntry = _entries[i];
                    }
                    matchingCount++;
                }
            }
            if (string.IsNullOrEmpty(_selectedSubjectId) && matchingCount > 0)
            {
                _selectedSubjectId = firstMatchingEntry.SubjectId;
            }

            int columns = Mathf.Max(2, _settings.CompendiumColumns);
            float gap = _settings.CompendiumGap;
            float detailWidth = Mathf.Min(
                _settings.CompendiumDetailPanelWidth,
                viewport.width - _settings.CompendiumSlotWidth - gap);
            Rect gridViewport = new Rect(
                viewport.x,
                viewport.y,
                viewport.width - detailWidth - gap,
                viewport.height);
            Rect detailRect = new Rect(
                gridViewport.xMax + gap,
                viewport.y,
                detailWidth,
                viewport.height);
            float scrollContentWidth = Mathf.Max(
                1f,
                gridViewport.width - _settings.CompendiumScrollbarReserve);
            float cellWidth = (scrollContentWidth - ((columns - 1) * gap)) / columns;
            float slotHeight = cellWidth *
                (_settings.CompendiumSlotHeight / Mathf.Max(1f, _settings.CompendiumSlotWidth));
            float cellHeight = slotHeight + _settings.CompendiumSlotLabelHeight + gap;
            int rows = Mathf.CeilToInt(matchingCount / (float)columns);
            Rect content = new Rect(
                0f,
                0f,
                scrollContentWidth,
                Mathf.Max(gridViewport.height, rows * cellHeight));
            _scroll = GUI.BeginScrollView(gridViewport, _scroll, content);
            int visibleIndex = 0;
            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                DuneVectorCompendiumEntry entry = _entries[entryIndex];
                if (entry.Category != category)
                {
                    continue;
                }

                int row = visibleIndex / columns;
                int column = visibleIndex % columns;
                Rect slot = new Rect(
                    column * (cellWidth + gap),
                    row * cellHeight,
                    cellWidth,
                    slotHeight);
                bool documented = _storage.IsDocumented(entry.SubjectId);
                DrawRect(slot, _settings.CompendiumLockedColor);
                Texture2D texture = documented ? _storage.GetCanonicalTexture(entry.SubjectId) : null;
                if (texture != null)
                {
                    GUI.DrawTexture(slot, texture, ScaleMode.ScaleAndCrop, false);
                }
                else
                {
                    DrawRect(slot, _settings.CompendiumLockedOverlayColor);
                }
                if (GUI.Button(slot, GUIContent.none, GUIStyle.none))
                {
                    _selectedSubjectId = entry.SubjectId;
                }
                bool selected = string.Equals(
                    _selectedSubjectId,
                    entry.SubjectId,
                    StringComparison.Ordinal);
                bool hovered = slot.Contains(Event.current.mousePosition);
                DrawBorder(
                    slot,
                    hovered
                        ? _settings.CompendiumHoverBorderColor
                        : selected
                            ? _settings.CompendiumActiveAccentColor
                            : documented
                                ? _settings.GalleryAccentColor
                                : _settings.GallerySelectionColor,
                    selected
                        ? _settings.FrameThickness * 2f
                        : _settings.FrameThickness);
                string label = documented ? entry.DisplayName : _settings.CompendiumUnknownLabel;
                if (!documented)
                {
                    GUI.Label(slot, _settings.CompendiumUnknownLabel, _unknownStyle);
                }
                GUI.Label(
                    new Rect(slot.x, slot.yMax, slot.width, _settings.CompendiumSlotLabelHeight),
                    label,
                    _bodyStyle);
                visibleIndex++;
            }
            GUI.EndScrollView();
            DrawDetail(detailRect, category);
        }

        private void DrawDetail(Rect area, PhotographableSubjectCategory category)
        {
            DrawRect(area, _settings.CompendiumTabColor);
            DrawBorder(area, _settings.CompendiumActiveAccentColor, _settings.FrameThickness);
            DuneVectorCompendiumEntry selected = default;
            bool found = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Category == category &&
                    string.Equals(_entries[i].SubjectId, _selectedSubjectId, StringComparison.Ordinal))
                {
                    selected = _entries[i];
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return;
            }

            float padding = _settings.CompendiumGap;
            Rect image = new Rect(
                area.x + padding,
                area.y + padding,
                area.width - (padding * 2f),
                Mathf.Min(
                    _settings.CompendiumDetailImageHeight,
                    area.height -
                        _settings.CompendiumDetailTitleHeight -
                        _settings.CompendiumSlotLabelHeight -
                        (padding * 3f)));
            bool documented = _storage.IsDocumented(selected.SubjectId);
            Texture2D texture = documented ? _storage.GetCanonicalTexture(selected.SubjectId) : null;
            DrawRect(image, _settings.CompendiumLockedColor);
            if (texture != null)
            {
                GUI.DrawTexture(image, texture, ScaleMode.ScaleAndCrop, false);
            }
            else
            {
                DrawRect(image, _settings.CompendiumLockedOverlayColor);
                GUI.Label(image, _settings.CompendiumUnknownLabel, _unknownStyle);
            }
            DrawBorder(image, _settings.GalleryAccentColor, _settings.FrameThickness);

            string title = documented ? selected.DisplayName : _settings.CompendiumUnknownLabel;
            GUI.Label(
                new Rect(
                    image.x,
                    image.yMax + padding,
                    image.width,
                    _settings.CompendiumDetailTitleHeight),
                title,
                _detailTitleStyle);
            GUI.Label(
                new Rect(
                    image.x,
                    image.yMax + padding + _settings.CompendiumDetailTitleHeight,
                    image.width,
                    _settings.CompendiumSlotLabelHeight),
                documented ? GetTabLabel(_selectedTab) : _settings.CompendiumUnknownLabel,
                _tabStyle);
            DrawRect(
                new Rect(
                    image.x,
                    image.yMax + padding - _settings.FrameThickness,
                    Mathf.Min(image.width, _settings.CompendiumDetailTitleHeight),
                    _settings.FrameThickness),
                _settings.CompendiumActiveAccentColor);
        }

        private string GetTabLabel(int index)
        {
            return index switch
            {
                0 => _settings.CompendiumGlyphTabLabel,
                1 => _settings.CompendiumLandmarkTabLabel,
                2 => _settings.CompendiumEnemyTabLabel,
                _ => _settings.CompendiumMiscTabLabel,
            };
        }

        private void EnsureStyles()
        {
            _titleStyle ??= CreateStyle(
                _settings.GalleryTitleFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.GalleryTextColor);
            if (_detailTitleStyle == null)
            {
                _detailTitleStyle = CreateStyle(
                    _settings.CompendiumDetailTitleFontSize,
                    FontStyle.Bold,
                    TextAnchor.UpperLeft,
                    _settings.GalleryTextColor);
                _detailTitleStyle.wordWrap = true;
            }
            _bodyStyle ??= CreateStyle(
                _settings.GalleryBodyFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.GalleryTextColor);
            _unknownStyle ??= CreateStyle(
                _settings.CompendiumUnknownFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.GalleryTextColor);
            _tabStyle ??= CreateStyle(
                _settings.CompendiumTabFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.GalleryTextColor);
            _buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = _settings.GalleryBodyFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private static GUIStyle CreateStyle(
            int size,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = alignment,
                clipping = TextClipping.Clip,
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

        public void Dispose()
        {
            Array.Clear(_tabIcons, 0, _tabIcons.Length);
        }
    }
}
