using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMapHUD : MonoBehaviour
    {
        private enum MapIconKind
        {
            Landmark,
            Geoglyph,
        }

        private readonly struct MapIconRecord
        {
            public readonly double X;
            public readonly double Z;
            public readonly MapIconKind Kind;
            public readonly DuneLandmarkType LandmarkType;
            public readonly GeoglyphArtworkPlacement Artwork;

            public MapIconRecord(double x, double z, DuneLandmarkType landmarkType)
            {
                X = x;
                Z = z;
                Kind = MapIconKind.Landmark;
                LandmarkType = landmarkType;
                Artwork = null;
            }

            public MapIconRecord(GeoglyphArtworkPlacement artwork)
            {
                X = artwork.WorldCenter.x;
                Z = artwork.WorldCenter.y;
                Kind = MapIconKind.Geoglyph;
                LandmarkType = default;
                Artwork = artwork;
            }
        }

        public bool IsWorldMapVisible => _worldMapVisible;
        public static bool ShouldSuppressPauseMenuInput
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                DuneVectorMapHUD mapHud = bootstrap != null ? bootstrap.MapHUD : null;
                return (mapHud != null && mapHud._worldMapVisible) ||
                    _worldMapClosedFrame == Time.frameCount;
            }
        }
        public static bool IsWorldMapOpen
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                return bootstrap != null &&
                    bootstrap.MapHUD != null &&
                    bootstrap.MapHUD._worldMapVisible;
            }
        }

        private sealed class WorldAtlasBuildResult
        {
            public int Resolution;
            public Color32[] Pixels;
            public double CenterX;
            public double CenterZ;
            public float WorldWidth;
            public float WorldHeight;
        }

        private sealed class ExplorationSaveResult
        {
            public bool RewroteFile;
            public long[] Cells;
            public Exception Error;
        }

        private sealed class GeoglyphRevealTexture
        {
            public Texture2D SourceTexture;
            public Texture2D RevealedTexture;
            public byte[] SourceAlpha;
            public Color32[] RevealedPixels;
            public int ExplorationRevision = -1;
        }

        public static bool IsWorldMapPausingGameplay
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                return bootstrap != null &&
                    bootstrap.MapHUD != null &&
                    bootstrap.MapHUD._worldMapPausedGame;
            }
        }

        private DroneCharacterController _drone;
        private DesertWorldStreamer _world;
        private BottomHudTuning _bottomHud;
        private MapHudTuning _settings;
        private GeoglyphSystemTuning _geoglyphs;
        private DronePermanentUpgradeSystem _permanentUpgrades;
        private Texture2D _scanTexture;
        private Texture2D _worldAtlasTexture;
        private Texture2D _worldMapScanRingTexture;
        private Material _geoglyphMapMaterial;
        private DuneVectorWorldMapTileCache _worldMapTileCache;
        private DuneVectorWorldMapGUI _worldMapGui;
        private Color[] _scanPixels;
        private readonly DuneVectorExplorationCellGrid _exploredCells =
            new DuneVectorExplorationCellGrid();
        private readonly List<long> _pendingExplorationCells = new List<long>();
        private readonly HashSet<long> _exploredTerrainBaseTiles = new HashSet<long>();
        private readonly List<MapIconRecord> _mapIcons = new List<MapIconRecord>();
        private readonly Dictionary<GeoglyphArtworkPlacement, Texture2D> _geoglyphWorldMapTextures =
            new Dictionary<GeoglyphArtworkPlacement, Texture2D>();
        private readonly Dictionary<GeoglyphArtworkPlacement, GeoglyphRevealTexture>
            _geoglyphRevealTextures =
                new Dictionary<GeoglyphArtworkPlacement, GeoglyphRevealTexture>();
        private readonly Queue<GeoglyphArtworkPlacement> _geoglyphTextureBuildQueue =
            new Queue<GeoglyphArtworkPlacement>();
        private readonly HashSet<GeoglyphArtworkPlacement> _queuedGeoglyphTextures =
            new HashSet<GeoglyphArtworkPlacement>();
        private readonly HashSet<GeoglyphArtworkPlacement> _exploredGeoglyphs =
            new HashSet<GeoglyphArtworkPlacement>();
        private GUIStyle _worldMapTitleStyle;
        private GUIStyle _northStyle;
        private GUIStyle _worldMapDetailStyle;
        private GUIStyle _worldMapHintStyle;
        private GUIStyle _worldMapScaleStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _landmarkIconStyle;
        private GUIStyle _landmarkIconShadowStyle;
        private bool _worldMapVisible;
        private double _lastScanX = double.PositiveInfinity;
        private double _lastScanZ = double.PositiveInfinity;
        private double _lastRevealX = double.PositiveInfinity;
        private double _lastRevealZ = double.PositiveInfinity;
        private float _textureWorldWidth;
        private float _textureWorldHeight;
        private float _nextScanTime;
        private float _nextExplorationSaveTime;
        private float _nextIconRefreshTime;
        private bool _explorationDirty;
        private bool _forceScanRefresh;
        private bool _scanBuildActive;
        private bool _worldMapPausedGame;
        private bool _worldMapDragging;
        private bool _worldMapDragMoved;
        private int _explorationRevision;
        private static int _worldMapClosedFrame = -1;
        private int _scanBuildRow;
        private int _scanBuildResolution;
        private float _timeScaleBeforeWorldMap = 1f;
        private float _worldMapViewHeight;
        private float _nextWorldMapRefineTime;
        private Task<WorldAtlasBuildResult> _worldAtlasBuildTask;
        private Task<ExplorationSaveResult> _explorationSaveTask;
        private CancellationTokenSource _worldAtlasBuildCancellation;
        private bool _explorationNeedsRewrite;
        private double _worldAtlasCenterX;
        private double _worldAtlasCenterZ;
        private float _worldAtlasWorldWidth;
        private float _worldAtlasWorldHeight;
        private double _worldMapCenterX;
        private double _worldMapCenterZ;
        private Vector2 _worldMapDragStartPosition;
        private Vector2 _lastWorldMapDragPosition;
        private CursorLockMode _cursorLockBeforeWorldMap;
        private bool _cursorVisibleBeforeWorldMap;
        private double _scanBuildCenterX;
        private double _scanBuildCenterZ;
        private double _scanBuildDroneX;
        private double _scanBuildDroneZ;
        private float _scanBuildWorldWidth;
        private float _scanBuildWorldHeight;

        /// <summary>Safety valve so an extreme zoom cannot flood the graticule with gridlines.</summary>
        private const int MaximumGraticuleLines = 256;

        private const int ExplorationFileMagic = 0x44564D50;
        private const int ExplorationFileVersion = 3;
        private const int LegacyExplorationFileVersion = 2;

        public void Initialize(
            DroneCharacterController drone,
            DesertWorldStreamer world,
            BottomHudTuning bottomHud,
            MapHudTuning settings,
            GeoglyphSystemTuning geoglyphs,
            DronePermanentUpgradeSystem permanentUpgrades)
        {
            _drone = drone;
            _world = world;
            _bottomHud = bottomHud;
            _settings = settings;
            _geoglyphs = geoglyphs;
            _permanentUpgrades = permanentUpgrades;
            if (_permanentUpgrades != null)
            {
                _permanentUpgrades.AtlasGlyphBrushedMetalEnabledChanged +=
                    HandleAtlasGlyphBrushedMetalEnabledChanged;
            }
            _worldMapGui = GetComponent<DuneVectorWorldMapGUI>();
            if (_worldMapGui == null)
            {
                _worldMapGui = gameObject.AddComponent<DuneVectorWorldMapGUI>();
            }
            _worldMapGui.Owner = this;
            _worldMapGui.enabled = false;
            if (settings != null)
            {
                _pendingExplorationCells.Capacity = Mathf.Max(
                    _pendingExplorationCells.Capacity,
                    settings.ExplorationJournalBufferCapacity);
            }
            Shader geoglyphMapShader = Shader.Find("Hidden/DuneVector/Map Geoglyph Mask");
            if (geoglyphMapShader != null)
            {
                _geoglyphMapMaterial = new Material(geoglyphMapShader)
                {
                    name = "Dune Vector Map Geoglyph Mask - Runtime",
                    hideFlags = HideFlags.DontSave,
                };
            }
            LoadExploration();
            RebuildExploredTerrainBaseTiles();
            RevealAroundPlayer(true);
            if (settings != null &&
                settings.WorldMapTiledTerrainEnabled &&
                world != null &&
                world.HeightField != null)
            {
                _worldMapTileCache = new DuneVectorWorldMapTileCache(
                    world.HeightField,
                    settings,
                    IsExplored,
                    IsWorldMapTerrainTileExplored);
            }
        }

        private void Update()
        {
            CompleteWorldAtlasBuildIfReady();
            CompleteExplorationSaveIfReady();
            if (_settings == null || !_settings.Enabled)
            {
                SetWorldMapVisible(false);
                return;
            }

            if (DuneVectorCourierGame.IsMapHudSuppressed)
            {
                SetWorldMapVisible(false);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (_worldMapVisible && keyboard.escapeKey.wasPressedThisFrame)
                {
                    SetWorldMapVisible(false);
                    _worldMapClosedFrame = Time.frameCount;
                }
                else if (_settings.WorldMapKey != Key.None &&
                    keyboard[_settings.WorldMapKey].wasPressedThisFrame)
                {
                    SetWorldMapVisible(!_worldMapVisible);
                }

            }

            _worldMapTileCache?.Update();
            RevealAroundPlayer(false);
            SaveExplorationIfDue();
            if (_worldMapVisible)
            {
                RefreshMapIconsIfDue();
                BuildQueuedGeoglyphTextures();
            }

            bool legacyWorldMapTerrain =
                _worldMapVisible &&
                (_worldMapTileCache == null || !_worldMapTileCache.IsAvailable);
            if (legacyWorldMapTerrain)
            {
                bool waitingForNavigationToSettle =
                    legacyWorldMapTerrain &&
                    Time.unscaledTime < _nextWorldMapRefineTime;
                if (!waitingForNavigationToSettle)
                {
                    RefreshScan(_forceScanRefresh);
                    _forceScanRefresh = false;
                    ProcessScanBuild();
                }
            }
        }

        private void SetWorldMapVisible(bool visible)
        {
            if (_worldMapVisible == visible)
            {
                return;
            }

            _worldMapVisible = visible;
            _forceScanRefresh = true;
            if (_worldMapGui != null)
            {
                _worldMapGui.enabled = visible;
            }
            _worldMapTileCache?.SetProcessingEnabled(visible);
            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            bool pauseMenuIsOpen = bootstrap != null &&
                bootstrap.PauseMenu != null &&
                bootstrap.PauseMenu.IsPaused;
            if (visible)
            {
                _nextIconRefreshTime = 0f;
                LogicalPosition playerPosition = _world.LogicalPlayerPosition;
                _worldMapCenterX = playerPosition.X;
                _worldMapCenterZ = playerPosition.Z;
                _worldMapViewHeight = Mathf.Clamp(
                    _settings.WorldMapWorldSize,
                    _settings.WorldMapMinimumWorldSize,
                    _settings.WorldMapMaximumWorldSize);
                _worldMapTileCache?.Prefetch(
                    playerPosition,
                    _worldMapViewHeight *
                        Mathf.Max(1f, _settings.WorldMapPanelAspectRatio),
                    _worldMapViewHeight,
                    _settings.WorldMapTerrainPrefetchViewportPixels);
                if (_worldMapTileCache == null || !_worldMapTileCache.IsAvailable)
                {
                    StartWorldAtlasBuild();
                }
                _cursorLockBeforeWorldMap = Cursor.lockState;
                _cursorVisibleBeforeWorldMap = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                _worldMapDragging = false;
                _worldMapDragMoved = false;
                Cursor.lockState = pauseMenuIsOpen
                    ? CursorLockMode.None
                    : _cursorLockBeforeWorldMap;
                Cursor.visible = pauseMenuIsOpen || _cursorVisibleBeforeWorldMap;
            }

            if (visible && _settings != null && _settings.PauseGameWhenWorldMapOpen)
            {
                _timeScaleBeforeWorldMap = Time.timeScale;
                Time.timeScale = 0f;
                _worldMapPausedGame = true;
            }
            else if (!visible && _worldMapPausedGame)
            {
                Time.timeScale = pauseMenuIsOpen ? 0f : _timeScaleBeforeWorldMap;
                _worldMapPausedGame = false;
            }
        }

        internal void DrawWorldMapGUI()
        {
            if (_settings == null ||
                !_settings.Enabled ||
                _drone == null ||
                _world == null ||
                !_worldMapVisible ||
                DuneVectorCourierGame.IsMapHudSuppressed)
            {
                return;
            }

            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            EnsureStyles();

            DrawWorldMap();
            GUI.depth = previousDepth;
        }

        private void DrawWorldMap()
        {
            Rect screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Color overlay = _settings.OverlayColor;
            overlay.a *= _settings.OverlayOpacity;
            DrawSolidRect(screenRect, overlay);
            DrawWorldMapBackdrop(screenRect);

            Rect panelRect = GetWorldMapPanelRect();
            float scale = GetMapScale();
            float borderThickness = Mathf.Max(1f, _settings.BorderThickness * scale);
            float headerHeight = _settings.WorldMapHeaderHeight * scale;
            float footerHeight = _settings.WorldMapFooterHeight * scale;
            Color accent = _settings.WorldMapAccentColor;

            DuneVectorHudChrome.DrawSoftShadow(
                panelRect,
                new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0f, 6f * scale),
                _settings.WorldMapPanelShadowSpread * scale);
            DrawSolidRect(panelRect, _settings.PanelColor);
            DrawBorder(panelRect, _settings.BorderColor, borderThickness);

            Rect headerRect = new Rect(
                panelRect.x + borderThickness,
                panelRect.y + borderThickness,
                panelRect.width - (borderThickness * 2f),
                headerHeight);
            Rect footerRect = new Rect(
                panelRect.x + borderThickness,
                panelRect.yMax - borderThickness - footerHeight,
                panelRect.width - (borderThickness * 2f),
                footerHeight);
            DrawWorldMapChromeBar(headerRect, accent, borderThickness, scale, true);
            DrawWorldMapChromeBar(footerRect, accent, borderThickness, scale, false);

            Rect mapRect = new Rect(
                panelRect.x + borderThickness,
                headerRect.yMax,
                panelRect.width - (borderThickness * 2f),
                Mathf.Max(1f, footerRect.y - headerRect.yMax));
            float viewportAspect = mapRect.width / Mathf.Max(1f, mapRect.height);
            _worldMapViewHeight = Mathf.Clamp(
                _worldMapViewHeight > 0f
                    ? _worldMapViewHeight
                    : _settings.WorldMapWorldSize,
                _settings.WorldMapMinimumWorldSize,
                _settings.WorldMapMaximumWorldSize);
            float displayedWorldWidth = _worldMapViewHeight * viewportAspect;
            HandleWorldMapInput(mapRect, displayedWorldWidth, _worldMapViewHeight);
            displayedWorldWidth = _worldMapViewHeight * viewportAspect;
            LogicalPosition currentCenter = new LogicalPosition(
                _worldMapCenterX,
                _worldMapCenterZ);

            DrawMapPanel(
                mapRect,
                displayedWorldWidth,
                _worldMapViewHeight,
                currentCenter,
                scale);

            float labelPadding = _settings.ContentPadding * scale;
            float tickWidth = Mathf.Max(2f, 3f * scale);
            float tickHeight = headerRect.height * 0.42f;
            Rect titleTick = new Rect(
                headerRect.x + labelPadding,
                headerRect.center.y - (tickHeight * 0.5f),
                tickWidth,
                tickHeight);
            DrawSolidRect(titleTick, accent);
            Color tickGlow = accent;
            tickGlow.a *= 0.35f;
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(titleTick.xMax, titleTick.y, 14f * scale, titleTick.height),
                tickGlow,
                true);

            float titleX = titleTick.xMax + (labelPadding * 0.7f);
            DuneVectorHudChrome.DrawGlowLabel(
                new Rect(
                    titleX,
                    headerRect.y,
                    Mathf.Max(1f, headerRect.xMax - titleX - labelPadding),
                    headerRect.height),
                _settings.WorldMapTitle,
                _worldMapTitleStyle,
                Color.white,
                new Color(accent.r, accent.g, accent.b, 0.22f),
                Mathf.Max(1f, 1.5f * scale),
                new Color(0f, 0f, 0f, 0.75f),
                new Vector2(0f, 2f * scale));
            GUI.Label(
                new Rect(
                    headerRect.center.x - (headerHeight * 0.5f),
                    headerRect.y,
                    headerHeight,
                    headerRect.height),
                _settings.NorthLabel,
                _northStyle);

            LogicalPosition dronePosition = _world.LogicalPlayerPosition;
            float droneHeight = _drone != null ? _drone.transform.position.y : 0f;
            string coordinates = string.Format(
                _settings.CoordinateFormat,
                dronePosition.X,
                droneHeight,
                dronePosition.Z,
                _settings.DroneRevealRadius);
            float footerContentWidth = footerRect.width - (labelPadding * 2f);
            float splitX = footerContentWidth * _settings.DetailSplitFraction;
            Vector2 textShadow = new Vector2(0f, Mathf.Max(1f, 1.5f * scale));
            Color textShadowColor = new Color(0f, 0f, 0f, 0.7f);
            DuneVectorHudChrome.DrawLabel(
                new Rect(
                    footerRect.x + labelPadding,
                    footerRect.y,
                    splitX,
                    footerRect.height),
                coordinates,
                _worldMapDetailStyle,
                Color.white,
                textShadowColor,
                textShadow);
            DuneVectorHudChrome.DrawLabel(
                new Rect(
                    footerRect.x + labelPadding + splitX,
                    footerRect.y,
                    footerContentWidth - splitX,
                    footerRect.height),
                _settings.WorldMapHint,
                _worldMapHintStyle,
                new Color(1f, 1f, 1f, 0.62f),
                textShadowColor,
                textShadow);
        }

        /// <summary>
        /// Header and footer bar: smoked chrome with a top sheen, a bottom shade and a bright
        /// accent rule facing the map viewport.
        /// </summary>
        private void DrawWorldMapChromeBar(
            Rect rect,
            Color accent,
            float borderThickness,
            float scale,
            bool isHeader)
        {
            DrawSolidRect(rect, _settings.WorldMapChromeColor);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(rect.x, rect.y, rect.width, rect.height * 0.6f),
                new Color(0.55f, 0.85f, 1f, 0.07f),
                true);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(rect.x, rect.center.y, rect.width, rect.height * 0.5f),
                new Color(0f, 0.02f, 0.04f, 0.45f),
                false);

            float ruleThickness = Mathf.Max(1f, borderThickness * 2f);
            float ruleY = isHeader ? rect.yMax - ruleThickness : rect.y;
            DrawSolidRect(new Rect(rect.x, ruleY, rect.width, ruleThickness), accent);

            Color glow = accent;
            glow.a *= 0.28f;
            float glowHeight = 18f * scale;
            DuneVectorHudChrome.DrawVerticalFade(
                isHeader
                    ? new Rect(rect.x, ruleY - glowHeight, rect.width, glowHeight)
                    : new Rect(rect.x, ruleY + ruleThickness, rect.width, glowHeight),
                glow,
                !isHeader);
        }

        private void DrawWorldMapBackdrop(Rect screenRect)
        {
            Texture2D backdrop = _settings.WorldMapBackdropImage;
            if (backdrop != null && _settings.WorldMapBackdropOpacity > 0f)
            {
                Color previousColor = GUI.color;
                Color backdropColor = _settings.WorldMapBackdropTint;
                backdropColor.a *= _settings.WorldMapBackdropOpacity;
                GUI.color = backdropColor;
                GUI.DrawTexture(
                    screenRect,
                    backdrop,
                    ScaleMode.ScaleAndCrop,
                    true);
                GUI.color = previousColor;
            }

            // Darken the outer frame so the eye settles on the panel rather than the wallpaper.
            float vignette = Mathf.Clamp01(_settings.WorldMapBackdropVignette);
            if (vignette <= 0f)
            {
                return;
            }

            Color shade = new Color(0f, 0f, 0f, vignette);
            float horizontal = screenRect.width * 0.28f;
            float vertical = screenRect.height * 0.28f;
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(screenRect.x, screenRect.y, horizontal, screenRect.height),
                shade,
                true);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(screenRect.xMax - horizontal, screenRect.y, horizontal, screenRect.height),
                shade,
                false);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(screenRect.x, screenRect.y, screenRect.width, vertical),
                shade,
                true);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(screenRect.x, screenRect.yMax - vertical, screenRect.width, vertical),
                shade,
                false);
        }

        private void HandleWorldMapInput(
            Rect mapRect,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            Event currentEvent = Event.current;
            if (currentEvent == null)
            {
                return;
            }

            Vector2 mousePosition = currentEvent.mousePosition;
            bool pointerOverMap = mapRect.Contains(mousePosition);
            int panMouseButton = Mathf.Clamp(_settings.WorldMapPanMouseButton, 0, 2);
            int panControlId = GUIUtility.GetControlID(FocusType.Passive, mapRect);
            if (currentEvent.type == EventType.ScrollWheel && pointerOverMap)
            {
                float normalizedX = (mousePosition.x - mapRect.x) / mapRect.width;
                float normalizedY = (mousePosition.y - mapRect.y) / mapRect.height;
                double worldUnderCursorX =
                    _worldMapCenterX +
                    ((normalizedX - 0.5f) * displayedWorldWidth);
                double worldUnderCursorZ =
                    _worldMapCenterZ +
                    ((0.5f - normalizedY) * displayedWorldHeight);
                float zoomMultiplier = Mathf.Exp(
                    currentEvent.delta.y * _settings.WorldMapZoomScrollSensitivity);
                float newHeight = Mathf.Clamp(
                    displayedWorldHeight * zoomMultiplier,
                    _settings.WorldMapMinimumWorldSize,
                    _settings.WorldMapMaximumWorldSize);
                float viewportAspect = mapRect.width / Mathf.Max(1f, mapRect.height);
                float newWidth = newHeight * viewportAspect;
                _worldMapCenterX =
                    worldUnderCursorX - ((normalizedX - 0.5f) * newWidth);
                _worldMapCenterZ =
                    worldUnderCursorZ - ((0.5f - normalizedY) * newHeight);
                _worldMapViewHeight = newHeight;
                _forceScanRefresh = true;
                _nextWorldMapRefineTime =
                    Time.unscaledTime + _settings.WorldMapNavigationRefineDelay;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == panMouseButton &&
                pointerOverMap)
            {
                _worldMapDragging = true;
                _worldMapDragMoved = false;
                GUIUtility.hotControl = panControlId;
                _worldMapDragStartPosition = mousePosition;
                _lastWorldMapDragPosition = mousePosition;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag &&
                currentEvent.button == panMouseButton &&
                _worldMapDragging &&
                GUIUtility.hotControl == panControlId)
            {
                if (!_worldMapDragMoved)
                {
                    float dragThreshold = Mathf.Max(
                        0f,
                        _settings.WorldMapPanDragThreshold);
                    if ((mousePosition - _worldMapDragStartPosition).sqrMagnitude <
                        dragThreshold * dragThreshold)
                    {
                        currentEvent.Use();
                        return;
                    }

                    _worldMapDragMoved = true;
                    _lastWorldMapDragPosition = mousePosition;
                    currentEvent.Use();
                    return;
                }

                Vector2 delta = mousePosition - _lastWorldMapDragPosition;
                _worldMapCenterX -=
                    (delta.x / Mathf.Max(1f, mapRect.width)) * displayedWorldWidth;
                _worldMapCenterZ +=
                    (delta.y / Mathf.Max(1f, mapRect.height)) * displayedWorldHeight;
                _lastWorldMapDragPosition = mousePosition;
                _nextWorldMapRefineTime =
                    Time.unscaledTime + _settings.WorldMapNavigationRefineDelay;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp &&
                currentEvent.button == panMouseButton &&
                _worldMapDragging &&
                GUIUtility.hotControl == panControlId)
            {
                _worldMapDragging = false;
                GUIUtility.hotControl = 0;
                if (_worldMapDragMoved)
                {
                    _forceScanRefresh = true;
                    _nextWorldMapRefineTime =
                        Time.unscaledTime + _settings.WorldMapNavigationRefineDelay;
                }
                _worldMapDragMoved = false;
                currentEvent.Use();
            }
        }

        private void DrawMapPanel(
            Rect mapRect,
            float displayedWorldWidth,
            float displayedWorldHeight,
            LogicalPosition currentCenter,
            float scale)
        {
            bool tiledTerrainBackground =
                _worldMapTileCache != null &&
                _worldMapTileCache.IsAvailable;
            DrawSolidRect(
                mapRect,
                tiledTerrainBackground
                    ? _settings.UnexploredColor
                    : _settings.PanelColor);

            GUI.BeginGroup(mapRect);
            bool tiledWorldMap =
                _worldMapTileCache != null &&
                _worldMapTileCache.IsAvailable;
            if (!tiledWorldMap && _worldAtlasTexture != null)
            {
                DrawCachedMapTexture(
                    mapRect,
                    _worldAtlasTexture,
                    _worldAtlasCenterX,
                    _worldAtlasCenterZ,
                    _worldAtlasWorldWidth,
                    _worldAtlasWorldHeight,
                    currentCenter,
                    displayedWorldWidth,
                    displayedWorldHeight);
            }
            if (tiledWorldMap)
            {
                _worldMapTileCache.Draw(
                    mapRect,
                    currentCenter,
                    displayedWorldWidth,
                    displayedWorldHeight);
                DrawWorldMapScanRing(
                    mapRect,
                    currentCenter,
                    displayedWorldWidth,
                    displayedWorldHeight);
            }
            else
            {
                DrawCachedMapTexture(
                    mapRect,
                    _scanTexture,
                    _lastScanX,
                    _lastScanZ,
                    _textureWorldWidth,
                    _textureWorldHeight,
                    currentCenter,
                    displayedWorldWidth,
                    displayedWorldHeight);
            }
            DrawWorldMapGraticule(
                mapRect,
                displayedWorldWidth,
                displayedWorldHeight,
                currentCenter,
                scale);
            DrawMapIcons(
                mapRect,
                displayedWorldWidth,
                displayedWorldHeight,
                currentCenter,
                scale);
            LogicalPosition dronePosition = _world.LogicalPlayerPosition;
            DrawDroneMarker(
                new Vector2(
                    mapRect.width *
                    (0.5f + ((float)(dronePosition.X - currentCenter.X) /
                             displayedWorldWidth)),
                    mapRect.height *
                    (0.5f - ((float)(dronePosition.Z - currentCenter.Z) /
                             displayedWorldHeight))),
                scale);

            GUI.EndGroup();
            DrawWorldMapViewportVignette(mapRect, scale);
            DrawWorldMapScaleBar(mapRect, displayedWorldWidth, scale);
            DrawBorder(
                mapRect,
                _settings.BorderColor,
                Mathf.Max(1f, _settings.BorderThickness * scale));
        }

        /// <summary>
        /// World-aligned coordinate grid. Spacing snaps to a 1/2/5 sequence so the graticule keeps
        /// a readable density at every zoom level, with every fifth line drawn as a major gridline.
        /// </summary>
        private void DrawWorldMapGraticule(
            Rect mapRect,
            float displayedWorldWidth,
            float displayedWorldHeight,
            LogicalPosition currentCenter,
            float scale)
        {
            if (!_settings.ShowWorldMapGraticule || _settings.WorldMapGraticuleColor.a <= 0f)
            {
                return;
            }

            int divisions = Mathf.Clamp(_settings.WorldMapGraticuleDivisions, 2, 16);
            float spacing = ChooseGraticuleSpacing(displayedWorldHeight / divisions);
            if (spacing <= 0f)
            {
                return;
            }

            Color minor = _settings.WorldMapGraticuleColor;
            Color major = minor;
            major.a = Mathf.Min(1f, minor.a * 2.6f);
            float thickness = Mathf.Max(1f, scale);

            double left = currentCenter.X - (displayedWorldWidth * 0.5);
            double right = currentCenter.X + (displayedWorldWidth * 0.5);
            long firstColumn = (long)System.Math.Floor(left / spacing);
            long lastColumn = (long)System.Math.Ceiling(right / spacing);
            if (lastColumn - firstColumn <= MaximumGraticuleLines)
            {
                for (long index = firstColumn; index <= lastColumn; index++)
                {
                    float x = (float)((((index * spacing) - left) / displayedWorldWidth) * mapRect.width);
                    DrawSolidRect(
                        new Rect(x - (thickness * 0.5f), 0f, thickness, mapRect.height),
                        index % 5 == 0 ? major : minor);
                }
            }

            double bottom = currentCenter.Z - (displayedWorldHeight * 0.5);
            double top = currentCenter.Z + (displayedWorldHeight * 0.5);
            long firstRow = (long)System.Math.Floor(bottom / spacing);
            long lastRow = (long)System.Math.Ceiling(top / spacing);
            if (lastRow - firstRow <= MaximumGraticuleLines)
            {
                for (long index = firstRow; index <= lastRow; index++)
                {
                    float y = (float)(((top - (index * spacing)) / displayedWorldHeight) * mapRect.height);
                    DrawSolidRect(
                        new Rect(0f, y - (thickness * 0.5f), mapRect.width, thickness),
                        index % 5 == 0 ? major : minor);
                }
            }
        }

        /// <summary>Rounds a raw world-space interval up to the nearest 1, 2 or 5 times a power of ten.</summary>
        private static float ChooseGraticuleSpacing(float rawSpacing)
        {
            if (rawSpacing <= 0f || float.IsNaN(rawSpacing) || float.IsInfinity(rawSpacing))
            {
                return 0f;
            }

            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawSpacing)));
            float normalized = rawSpacing / magnitude;
            float step = normalized <= 1f ? 1f : normalized <= 2f ? 2f : normalized <= 5f ? 5f : 10f;
            return step * magnitude;
        }

        /// <summary>Inner shadow along the viewport edges so the terrain sits inside the frame.</summary>
        private void DrawWorldMapViewportVignette(Rect mapRect, float scale)
        {
            float depth = _settings.WorldMapViewportVignette * scale;
            if (depth <= 0f)
            {
                return;
            }

            depth = Mathf.Min(depth, Mathf.Min(mapRect.width, mapRect.height) * 0.35f);
            Color shade = new Color(0f, 0f, 0f, 0.55f);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(mapRect.x, mapRect.y, mapRect.width, depth),
                shade,
                true);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(mapRect.x, mapRect.yMax - depth, mapRect.width, depth),
                shade,
                false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(mapRect.x, mapRect.y, depth, mapRect.height),
                shade,
                true);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(mapRect.xMax - depth, mapRect.y, depth, mapRect.height),
                shade,
                false);
        }

        /// <summary>Zoom-aware distance bar tucked into the bottom-left corner of the viewport.</summary>
        private void DrawWorldMapScaleBar(Rect mapRect, float displayedWorldWidth, float scale)
        {
            if (!_settings.ShowWorldMapScaleBar || displayedWorldWidth <= 0f)
            {
                return;
            }

            float worldLength = ChooseGraticuleSpacing(displayedWorldWidth * 0.18f);
            float pixelsPerWorldUnit = mapRect.width / displayedWorldWidth;
            float barWidth = worldLength * pixelsPerWorldUnit;
            if (barWidth < 8f || barWidth > mapRect.width * 0.6f)
            {
                return;
            }

            float margin = _settings.ContentPadding * scale * 1.5f;
            float thickness = Mathf.Max(1f, 2f * scale);
            float capHeight = Mathf.Max(3f, 6f * scale);
            float barY = mapRect.yMax - margin;
            float barX = mapRect.x + margin;
            Color accent = _settings.WorldMapAccentColor;
            Color shadow = new Color(0f, 0f, 0f, 0.8f);

            DrawSolidRect(
                new Rect(barX, barY + thickness, barWidth, thickness),
                shadow);
            DrawSolidRect(new Rect(barX, barY, barWidth, thickness), accent);
            DrawSolidRect(new Rect(barX, barY - capHeight, thickness, capHeight + thickness), accent);
            DrawSolidRect(
                new Rect(barX + barWidth - thickness, barY - capHeight, thickness, capHeight + thickness),
                accent);

            string label = worldLength >= 1000f
                ? $"{worldLength / 1000f:0.#} km"
                : $"{worldLength:0} m";
            Rect labelRect = new Rect(
                barX,
                barY - capHeight - (_worldMapScaleStyle.fontSize * 1.6f),
                Mathf.Max(barWidth, 80f * scale),
                _worldMapScaleStyle.fontSize * 1.6f);
            DuneVectorHudChrome.DrawLabel(
                labelRect,
                label,
                _worldMapScaleStyle,
                Color.white,
                shadow,
                new Vector2(0f, Mathf.Max(1f, scale)));
        }

        private static void DrawCachedMapTexture(
            Rect mapRect,
            Texture texture,
            double textureCenterX,
            double textureCenterZ,
            float textureWorldWidth,
            float textureWorldHeight,
            LogicalPosition viewCenter,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            if (texture == null ||
                double.IsInfinity(textureCenterX) ||
                double.IsInfinity(textureCenterZ) ||
                textureWorldWidth <= 0f ||
                textureWorldHeight <= 0f)
            {
                return;
            }

            float horizontalPixelsPerWorldUnit =
                mapRect.width / Mathf.Max(1f, displayedWorldWidth);
            float verticalPixelsPerWorldUnit =
                mapRect.height / Mathf.Max(1f, displayedWorldHeight);
            float textureWidth = textureWorldWidth * horizontalPixelsPerWorldUnit;
            float textureHeight = textureWorldHeight * verticalPixelsPerWorldUnit;
            float offsetX =
                (float)(textureCenterX - viewCenter.X) * horizontalPixelsPerWorldUnit;
            float offsetY =
                (float)(viewCenter.Z - textureCenterZ) * verticalPixelsPerWorldUnit;
            Rect textureRect = new Rect(
                ((mapRect.width - textureWidth) * 0.5f) + offsetX,
                ((mapRect.height - textureHeight) * 0.5f) + offsetY,
                textureWidth,
                textureHeight);
            GUI.DrawTexture(textureRect, texture, ScaleMode.StretchToFill, false);
        }

        private void DrawWorldMapScanRing(
            Rect mapRect,
            LogicalPosition viewCenter,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            EnsureWorldMapScanRingTexture();
            if (_worldMapScanRingTexture == null)
            {
                return;
            }

            LogicalPosition dronePosition = _world.LogicalPlayerPosition;
            float radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            float diameter = radius * 2f;
            float horizontalPixelsPerWorldUnit =
                mapRect.width / Mathf.Max(1f, displayedWorldWidth);
            float verticalPixelsPerWorldUnit =
                mapRect.height / Mathf.Max(1f, displayedWorldHeight);
            Rect ringRect = new Rect(
                (mapRect.width * 0.5f) +
                    ((float)(dronePosition.X - viewCenter.X - radius) *
                     horizontalPixelsPerWorldUnit),
                (mapRect.height * 0.5f) -
                    ((float)(dronePosition.Z - viewCenter.Z + radius) *
                     verticalPixelsPerWorldUnit),
                diameter * horizontalPixelsPerWorldUnit,
                diameter * verticalPixelsPerWorldUnit);
            GUI.DrawTexture(ringRect, _worldMapScanRingTexture, ScaleMode.StretchToFill, true);
        }

        private void EnsureWorldMapScanRingTexture()
        {
            int resolution = Mathf.Clamp(_settings.ScanTextureResolution, 32, 512);
            if (_worldMapScanRingTexture != null &&
                _worldMapScanRingTexture.width == resolution)
            {
                return;
            }

            if (_worldMapScanRingTexture != null)
            {
                Destroy(_worldMapScanRingTexture);
            }

            _worldMapScanRingTexture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                true,
                false)
            {
                name = "Dune Vector World Map Scan Ring",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            Color32[] pixels = new Color32[resolution * resolution];
            float radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            float diameter = radius * 2f;
            float worldUnitsPerPixel = diameter / resolution;
            float halfThickness = Mathf.Max(0.01f, _settings.RadiusLineThickness);
            float edgeWidth = Mathf.Max(worldUnitsPerPixel, Mathf.Epsilon);
            Color ringColor = _settings.RadiusLineColor;
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = (((y + 0.5f) / resolution) - 0.5f) * diameter;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = (((x + 0.5f) / resolution) - 0.5f) * diameter;
                    float distanceToRing =
                        Mathf.Abs(Mathf.Sqrt((worldX * worldX) + (worldZ * worldZ)) - radius);
                    float coverage = 1f - Mathf.SmoothStep(
                        halfThickness,
                        halfThickness + edgeWidth,
                        distanceToRing);
                    Color pixel = ringColor;
                    pixel.a *= coverage;
                    pixels[(y * resolution) + x] = pixel;
                }
            }
            _worldMapScanRingTexture.SetPixels32(pixels);
            _worldMapScanRingTexture.Apply(true, true);
        }

        private void DrawMapIcons(
            Rect mapRect,
            float displayedWorldWidth,
            float displayedWorldHeight,
            LogicalPosition center,
            float scale)
        {
            float minimumZoomScale = Mathf.Clamp(
                _settings.LandmarkIconMinimumZoomScale,
                0.25f,
                1f);
            float maximumZoomScale = Mathf.Max(
                minimumZoomScale,
                _settings.LandmarkIconMaximumZoomScale);
            float zoomRatio =
                Mathf.Max(1f, _settings.WorldMapWorldSize) /
                Mathf.Max(1f, displayedWorldHeight);
            float landmarkZoomScale = Mathf.Clamp(
                Mathf.Pow(
                    zoomRatio,
                    Mathf.Max(0.1f, _settings.LandmarkIconZoomScaleExponent)),
                minimumZoomScale,
                maximumZoomScale);
            float iconScale = scale * landmarkZoomScale;
            float halfWorldWidth = displayedWorldWidth * 0.5f;
            float halfWorldHeight = displayedWorldHeight * 0.5f;
            Vector2 shadowOffset = _settings.IconShadowOffset * iconScale;
            UpdateIconStyles(iconScale);

            for (int index = 0; index < _mapIcons.Count; index++)
            {
                MapIconRecord icon = _mapIcons[index];
                bool isExplored = icon.Kind == MapIconKind.Geoglyph
                    ? _exploredGeoglyphs.Contains(icon.Artwork)
                    : IsExplored(icon.X, icon.Z);
                if (_settings.OnlyShowExploredIcons && !isExplored)
                {
                    continue;
                }

                double deltaX = icon.X - center.X;
                double deltaZ = icon.Z - center.Z;
                float footprintHalfWidth = 0f;
                float footprintHalfHeight = 0f;
                if (icon.Kind == MapIconKind.Geoglyph)
                {
                    GetRotatedGeoglyphSize(
                        icon.Artwork,
                        out float footprintWidth,
                        out float footprintHeight);
                    footprintHalfWidth = footprintWidth * 0.5f;
                    footprintHalfHeight = footprintHeight * 0.5f;
                }
                if (Math.Abs(deltaX) > halfWorldWidth + footprintHalfWidth ||
                    Math.Abs(deltaZ) > halfWorldHeight + footprintHalfHeight)
                {
                    continue;
                }

                Vector2 position = new Vector2(
                    mapRect.width * (0.5f + ((float)deltaX / displayedWorldWidth)),
                    mapRect.height * (0.5f - ((float)deltaZ / displayedWorldHeight)));
                if (icon.Kind == MapIconKind.Geoglyph)
                {
                    DrawGeoglyphArtwork(
                        icon.Artwork,
                        position,
                        mapRect,
                        displayedWorldWidth,
                        displayedWorldHeight);
                    continue;
                }

                float boxSize = _settings.IconBoxSize * iconScale;
                Rect iconRect = new Rect(
                    position.x - (boxSize * 0.5f),
                    position.y - (boxSize * 0.5f),
                    boxSize,
                    boxSize);
                Texture2D iconTexture = GetIconTexture(icon);
                if (iconTexture != null)
                {
                    Color previousColor = GUI.color;
                    Color shadowColor = _settings.IconShadowColor;
                    shadowColor.a *= previousColor.a;
                    GUI.color = shadowColor;
                    GUI.DrawTexture(
                        new Rect(
                            iconRect.x + shadowOffset.x,
                            iconRect.y + shadowOffset.y,
                            iconRect.width,
                            iconRect.height),
                        iconTexture,
                        ScaleMode.ScaleToFit,
                        true);
                    GUI.color = previousColor;
                    GUI.DrawTexture(
                        iconRect,
                        iconTexture,
                        ScaleMode.ScaleToFit,
                        true);
                    continue;
                }

                string glyph = GetIconGlyph(icon);
                GUI.Label(
                    new Rect(
                        iconRect.x + shadowOffset.x,
                        iconRect.y + shadowOffset.y,
                        iconRect.width,
                        iconRect.height),
                    glyph,
                    _landmarkIconShadowStyle);
                GUI.Label(
                    iconRect,
                    glyph,
                    _landmarkIconStyle);
            }
        }

        private Texture2D GetIconTexture(MapIconRecord icon)
        {
            return icon.LandmarkType switch
            {
                DuneLandmarkType.DesertRelayStation => _settings.RelayStationIconImage,
                DuneLandmarkType.CrashedCarrier => _settings.CrashedCarrierIconImage,
                DuneLandmarkType.RaiderBeacon => _settings.RaiderBeaconIconImage,
                DuneLandmarkType.AncientSpire => _settings.AncientSpireIconImage,
                DuneLandmarkType.SandExcavationSite => _settings.ExcavationSiteIconImage,
                DuneLandmarkType.FallenOrbitalArray => _settings.OrbitalArrayIconImage,
                DuneLandmarkType.DesertMegagate => _settings.DesertMegagateIconImage,
                DuneLandmarkType.WindHarvesterGraveyard => _settings.WindHarvesterIconImage,
                DuneLandmarkType.BuriedArcology => _settings.BuriedArcologyIconImage,
                DuneLandmarkType.SandRing => _settings.SandRingIconImage,
                _ => null,
            };
        }

        private string GetIconGlyph(MapIconRecord icon)
        {
            return icon.LandmarkType switch
            {
                DuneLandmarkType.DesertRelayStation => _settings.RelayStationIcon,
                DuneLandmarkType.CrashedCarrier => _settings.CrashedCarrierIcon,
                DuneLandmarkType.RaiderBeacon => _settings.RaiderBeaconIcon,
                DuneLandmarkType.AncientSpire => _settings.AncientSpireIcon,
                DuneLandmarkType.SandExcavationSite => _settings.ExcavationSiteIcon,
                DuneLandmarkType.FallenOrbitalArray => _settings.OrbitalArrayIcon,
                DuneLandmarkType.DesertMegagate => _settings.DesertMegagateIcon,
                DuneLandmarkType.WindHarvesterGraveyard => _settings.WindHarvesterIcon,
                DuneLandmarkType.BuriedArcology => _settings.BuriedArcologyIcon,
                _ => _settings.SandRingIcon,
            };
        }

        private void DrawGeoglyphArtwork(
            GeoglyphArtworkPlacement artwork,
            Vector2 position,
            Rect mapRect,
            float displayedWorldWidth,
            float displayedWorldHeight)
        {
            if (artwork == null ||
                !_geoglyphWorldMapTextures.TryGetValue(artwork, out Texture2D mapTexture) ||
                mapTexture == null)
            {
                return;
            }

            Texture2D revealedTexture = GetRevealedGeoglyphTexture(artwork, mapTexture);
            if (revealedTexture == null)
            {
                return;
            }

            GetRotatedGeoglyphSize(artwork, out float rotatedWorldWidth, out float rotatedWorldHeight);
            float width = mapRect.width *
                (rotatedWorldWidth / displayedWorldWidth);
            float height = mapRect.height *
                (rotatedWorldHeight / displayedWorldHeight);
            Rect artworkRect = new Rect(
                position.x - (width * 0.5f),
                position.y - (height * 0.5f),
                width,
                height);

            GUI.DrawTexture(artworkRect, revealedTexture, ScaleMode.StretchToFill, true);
        }

        private Texture2D GetRevealedGeoglyphTexture(
            GeoglyphArtworkPlacement artwork,
            Texture2D sourceTexture)
        {
            if (!_geoglyphRevealTextures.TryGetValue(
                    artwork,
                    out GeoglyphRevealTexture reveal) ||
                reveal.SourceTexture != sourceTexture)
            {
                if (reveal?.RevealedTexture != null)
                {
                    Destroy(reveal.RevealedTexture);
                }

                Color32[] sourcePixels = sourceTexture.GetPixels32();
                byte[] sourceAlpha = new byte[sourcePixels.Length];
                for (int index = 0; index < sourcePixels.Length; index++)
                {
                    sourceAlpha[index] = sourcePixels[index].a;
                }
                reveal = new GeoglyphRevealTexture
                {
                    SourceTexture = sourceTexture,
                    SourceAlpha = sourceAlpha,
                    RevealedPixels = sourcePixels,
                    RevealedTexture = new Texture2D(
                        sourceTexture.width,
                        sourceTexture.height,
                        TextureFormat.RGBA32,
                        false,
                        false)
                    {
                        name = $"Explored Map Geoglyph - {artwork.Mask.name}",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        anisoLevel = 0,
                        hideFlags = HideFlags.DontSave,
                    },
                };
                _geoglyphRevealTextures[artwork] = reveal;
            }

            if (reveal.ExplorationRevision == _explorationRevision)
            {
                return reveal.RevealedTexture;
            }

            GetRotatedGeoglyphSize(
                artwork,
                out float rotatedWorldWidth,
                out float rotatedWorldHeight);
            int width = sourceTexture.width;
            int height = sourceTexture.height;
            for (int y = 0; y < height; y++)
            {
                double logicalZ =
                    artwork.WorldCenter.y +
                    ((((y + 0.5d) / height) - 0.5d) * rotatedWorldHeight);
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = rowOffset + x;
                    Color32 pixel = reveal.RevealedPixels[pixelIndex];
                    double logicalX =
                        artwork.WorldCenter.x +
                        ((((x + 0.5d) / width) - 0.5d) * rotatedWorldWidth);
                    pixel.a = IsExplored(logicalX, logicalZ)
                        ? reveal.SourceAlpha[pixelIndex]
                        : byte.MinValue;
                    reveal.RevealedPixels[pixelIndex] = pixel;
                }
            }

            reveal.RevealedTexture.SetPixels32(reveal.RevealedPixels);
            reveal.RevealedTexture.Apply(false, false);
            reveal.ExplorationRevision = _explorationRevision;
            return reveal.RevealedTexture;
        }

        private void UpdateIconStyles(float iconScale)
        {
            _landmarkIconStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.LandmarkIconFontSize * iconScale));
            _landmarkIconShadowStyle.fontSize = _landmarkIconStyle.fontSize;
        }

        private void RefreshMapIconsIfDue()
        {
            if (_settings == null || Time.unscaledTime < _nextIconRefreshTime)
            {
                return;
            }

            _nextIconRefreshTime =
                Time.unscaledTime + Mathf.Max(0.1f, _settings.IconRefreshInterval);
            _mapIcons.Clear();
            _exploredGeoglyphs.Clear();

            if (_settings.ShowLandmarks)
            {
                DuneVectorLandmarkDirector director =
                    DuneVectorBootstrap.Instance != null
                        ? DuneVectorBootstrap.Instance.LandmarkDirector
                        : null;
                if (director != null)
                {
                    HashSet<string> mappedLandmarkIds = new HashSet<string>();
                    foreach (DuneLandmarkPlacementRecord record in director.PlacementRecords.Values)
                    {
                        if (record != null)
                        {
                            mappedLandmarkIds.Add(record.PersistentId);
                            _mapIcons.Add(new MapIconRecord(
                                record.LogicalPosition.X,
                                record.LogicalPosition.Z,
                                record.Type));
                        }
                    }

                    IReadOnlyList<DuneVectorLandmarkInstance> contractLandmarks =
                        director.ContractLandmarks;
                    for (int index = 0; index < contractLandmarks.Count; index++)
                    {
                        DuneVectorLandmarkInstance landmark = contractLandmarks[index];
                        DuneLandmarkPlacementRecord placement = landmark != null
                            ? landmark.PlacementRecord
                            : null;
                        if (landmark != null &&
                            (placement == null || mappedLandmarkIds.Add(placement.PersistentId)))
                        {
                            _mapIcons.Add(new MapIconRecord(
                                landmark.LogicalPosition.X,
                                landmark.LogicalPosition.Z,
                                landmark.Type));
                        }
                    }
                }
            }

            if (_settings.ShowGeoglyphs && _geoglyphs != null && _geoglyphs.Enabled)
            {
                List<GeoglyphArtworkPlacement> pendingTextureBuilds =
                    new List<GeoglyphArtworkPlacement>();
                for (int index = 0; index < _geoglyphs.Placements.Count; index++)
                {
                    GeoglyphArtworkPlacement placement = _geoglyphs.Placements[index];
                    if (placement != null && placement.Mask != null)
                    {
                        _mapIcons.Add(new MapIconRecord(placement));
                        if (IsGeoglyphExplored(placement))
                        {
                            _exploredGeoglyphs.Add(placement);
                        }
                        if (!_geoglyphWorldMapTextures.ContainsKey(placement) &&
                            !_queuedGeoglyphTextures.Contains(placement))
                        {
                            pendingTextureBuilds.Add(placement);
                        }
                    }
                }

                LogicalPosition playerPosition = _world.LogicalPlayerPosition;
                pendingTextureBuilds.Sort((left, right) =>
                    GetSquaredDistance(left, playerPosition).CompareTo(
                        GetSquaredDistance(right, playerPosition)));
                for (int index = 0; index < pendingTextureBuilds.Count; index++)
                {
                    QueueGeoglyphTextureBuild(pendingTextureBuilds[index]);
                }
            }
        }

        private static double GetSquaredDistance(
            GeoglyphArtworkPlacement artwork,
            LogicalPosition position)
        {
            double deltaX = artwork.WorldCenter.x - position.X;
            double deltaZ = artwork.WorldCenter.y - position.Z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private void QueueGeoglyphTextureBuild(GeoglyphArtworkPlacement artwork)
        {
            if (artwork == null ||
                artwork.Mask == null ||
                _geoglyphWorldMapTextures.ContainsKey(artwork) ||
                !_queuedGeoglyphTextures.Add(artwork))
            {
                return;
            }
            _geoglyphTextureBuildQueue.Enqueue(artwork);
        }

        private void BuildQueuedGeoglyphTextures()
        {
            if (_geoglyphMapMaterial == null || _geoglyphTextureBuildQueue.Count == 0)
            {
                return;
            }

            int buildCount = Mathf.Clamp(
                _settings.GeoglyphTextureBuildsPerFrame,
                1,
                4);
            for (int index = 0;
                index < buildCount && _geoglyphTextureBuildQueue.Count > 0;
                index++)
            {
                GeoglyphArtworkPlacement artwork = _geoglyphTextureBuildQueue.Dequeue();
                _queuedGeoglyphTextures.Remove(artwork);
                Texture2D worldMapTexture = BuildGeoglyphMapTexture(
                    artwork,
                    _settings.GeoglyphMapTextureResolution,
                    "World Map");
                if (worldMapTexture != null)
                {
                    _geoglyphWorldMapTextures[artwork] = worldMapTexture;
                }
            }
        }

        private Texture2D BuildGeoglyphMapTexture(
            GeoglyphArtworkPlacement artwork,
            int requestedResolution,
            string variantName)
        {
            if (artwork == null || artwork.Mask == null || _geoglyphMapMaterial == null)
            {
                return null;
            }

            int maximumResolution = Mathf.Clamp(
                requestedResolution,
                64,
                512);
            GetRotatedGeoglyphSize(artwork, out float rotatedWorldWidth, out float rotatedWorldHeight);
            float aspect = rotatedWorldWidth / rotatedWorldHeight;
            int width = aspect >= 1f
                ? maximumResolution
                : Mathf.Max(1, Mathf.RoundToInt(maximumResolution * aspect));
            int height = aspect >= 1f
                ? Mathf.Max(1, Mathf.RoundToInt(maximumResolution / aspect))
                : maximumResolution;

            Color mapColor = _settings.GeoglyphMapColor;
            mapColor.a *= _settings.GeoglyphMapOpacity;
            Color haloColor = _settings.GeoglyphMapHaloColor;
            ConfigureGeoglyphMapSurface(ref mapColor, ref haloColor);
            _geoglyphMapMaterial.SetColor("_Color", mapColor);
            _geoglyphMapMaterial.SetColor(
                "_HaloColor",
                haloColor);
            _geoglyphMapMaterial.SetFloat(
                "_HaloWidthPixels",
                Mathf.Max(0f, _settings.GeoglyphMapHaloWidthPixels));
            _geoglyphMapMaterial.SetFloat(
                "_Threshold",
                Mathf.Clamp01(artwork.MaskThreshold));
            _geoglyphMapMaterial.SetFloat(
                "_Softness",
                Mathf.Max(0.0001f, artwork.EdgeSoftness));
            float rotationRadians = artwork.RotationDegrees * Mathf.Deg2Rad;
            _geoglyphMapMaterial.SetVector(
                "_RotationSinCos",
                new Vector4(
                    Mathf.Sin(rotationRadians),
                    Mathf.Cos(rotationRadians),
                    0f,
                    0f));
            _geoglyphMapMaterial.SetVector(
                "_OutputToSourceScale",
                new Vector4(
                    rotatedWorldWidth / Mathf.Max(0.01f, artwork.WorldSize.x),
                    rotatedWorldHeight / Mathf.Max(0.01f, artwork.WorldSize.y),
                    0f,
                    0f));

            RenderTexture target = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(artwork.Mask, target, _geoglyphMapMaterial);
            RenderTexture.active = target;
            Texture2D result = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                true)
            {
                name = $"Map Geoglyph {variantName} - {artwork.Mask.name}",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            result.Apply(true, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }

        private void ConfigureGeoglyphMapSurface(
            ref Color mapColor,
            ref Color haloColor)
        {
            AtlasGlyphMaterialUnlockTuning tuning =
                _permanentUpgrades?.AtlasGlyphMaterialTuning;
            string textureProperty = null;
            bool brushedMetalEnabled =
                tuning?.GlyphMaterial != null &&
                _permanentUpgrades.IsAtlasGlyphBrushedMetalEnabled &&
                TryGetMaterialTextureProperty(
                    tuning.GlyphMaterial,
                    out textureProperty);

            _geoglyphMapMaterial.SetFloat(
                "_SurfaceTextureEnabled",
                brushedMetalEnabled ? 1f : 0f);
            if (!brushedMetalEnabled)
            {
                return;
            }

            Texture surfaceTexture = tuning.GlyphMaterial.GetTexture(textureProperty);
            if (surfaceTexture == null)
            {
                _geoglyphMapMaterial.SetFloat("_SurfaceTextureEnabled", 0f);
                return;
            }

            Color materialColor = GetMaterialColor(
                tuning.GlyphMaterial,
                "_Color",
                "_BaseColor",
                Color.white);
            mapColor.r *= materialColor.r * tuning.GlyphMapColor.r;
            mapColor.g *= materialColor.g * tuning.GlyphMapColor.g;
            mapColor.b *= materialColor.b * tuning.GlyphMapColor.b;
            haloColor.r = tuning.GlyphMapColor.r;
            haloColor.g = tuning.GlyphMapColor.g;
            haloColor.b = tuning.GlyphMapColor.b;
            _geoglyphMapMaterial.SetTexture("_SurfaceTexture", surfaceTexture);
            _geoglyphMapMaterial.SetVector(
                "_SurfaceTextureTransform",
                new Vector4(
                    tuning.GlyphTextureTiling.x,
                    tuning.GlyphTextureTiling.y,
                    tuning.GlyphTextureOffset.x,
                    tuning.GlyphTextureOffset.y));
        }

        private static bool TryGetMaterialTextureProperty(
            Material material,
            out string textureProperty)
        {
            if (material != null && material.HasProperty("_BaseMap"))
            {
                textureProperty = "_BaseMap";
                return true;
            }
            if (material != null && material.HasProperty("_MainTex"))
            {
                textureProperty = "_MainTex";
                return true;
            }

            textureProperty = null;
            return false;
        }

        private static Color GetMaterialColor(
            Material material,
            string primaryProperty,
            string secondaryProperty,
            Color fallback)
        {
            if (material == null)
            {
                return fallback;
            }
            if (material.HasProperty(primaryProperty))
            {
                return material.GetColor(primaryProperty);
            }
            return material.HasProperty(secondaryProperty)
                ? material.GetColor(secondaryProperty)
                : fallback;
        }

        private void HandleAtlasGlyphBrushedMetalEnabledChanged(bool enabled)
        {
            ClearGeoglyphMapTextures();
            _nextIconRefreshTime = 0f;
        }

        private void ClearGeoglyphMapTextures()
        {
            _geoglyphTextureBuildQueue.Clear();
            _queuedGeoglyphTextures.Clear();
            foreach (Texture2D texture in _geoglyphWorldMapTextures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            _geoglyphWorldMapTextures.Clear();
            foreach (GeoglyphRevealTexture reveal in _geoglyphRevealTextures.Values)
            {
                if (reveal?.RevealedTexture != null)
                {
                    Destroy(reveal.RevealedTexture);
                }
            }
            _geoglyphRevealTextures.Clear();
        }

        private static void GetRotatedGeoglyphSize(
            GeoglyphArtworkPlacement artwork,
            out float width,
            out float height)
        {
            float sourceWidth = Mathf.Max(0.01f, artwork.WorldSize.x);
            float sourceHeight = Mathf.Max(0.01f, artwork.WorldSize.y);
            float rotationRadians = artwork.RotationDegrees * Mathf.Deg2Rad;
            float absoluteCosine = Mathf.Abs(Mathf.Cos(rotationRadians));
            float absoluteSine = Mathf.Abs(Mathf.Sin(rotationRadians));
            width = Mathf.Max(
                0.01f,
                (sourceWidth * absoluteCosine) + (sourceHeight * absoluteSine));
            height = Mathf.Max(
                0.01f,
                (sourceWidth * absoluteSine) + (sourceHeight * absoluteCosine));
        }

        private void DrawDroneMarker(Vector2 center, float scale)
        {
            float markerSize = _settings.DroneMarkerBoxSize * scale;
            Rect markerRect = new Rect(
                center.x - (markerSize * 0.5f),
                center.y - (markerSize * 0.5f),
                markerSize,
                markerSize);
            Vector3 forward = _drone.transform.forward;
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(heading, center);
            GUI.Label(markerRect, _settings.DroneGlyph, _markerStyle);
            GUI.matrix = previousMatrix;
        }

        private void RefreshScan(bool force)
        {
            if (_settings == null ||
                _world == null ||
                _world.HeightField == null ||
                !_worldMapVisible)
            {
                return;
            }

            LogicalPosition center = new LogicalPosition(
                _worldMapCenterX,
                _worldMapCenterZ);
            float desiredWorldHeight = Mathf.Clamp(
                _worldMapViewHeight,
                _settings.WorldMapMinimumWorldSize,
                _settings.WorldMapMaximumWorldSize);
            float desiredWorldWidth = desiredWorldHeight * GetWorldMapViewportAspect();
            force |=
                !Mathf.Approximately(_textureWorldWidth, desiredWorldWidth) ||
                !Mathf.Approximately(_textureWorldHeight, desiredWorldHeight);
            if (_scanBuildActive)
            {
                if (force &&
                    (!Mathf.Approximately(_scanBuildWorldWidth, desiredWorldWidth) ||
                     !Mathf.Approximately(_scanBuildWorldHeight, desiredWorldHeight) ||
                     Math.Abs(_scanBuildCenterX - center.X) > double.Epsilon ||
                     Math.Abs(_scanBuildCenterZ - center.Z) > double.Epsilon))
                {
                    BeginScanBuild(center, desiredWorldWidth, desiredWorldHeight);
                }
                return;
            }

            double dx = center.X - _lastScanX;
            double dz = center.Z - _lastScanZ;
            double movementThreshold = _settings.ScanRefreshMovement;
            bool movedEnough = (dx * dx) + (dz * dz) >= movementThreshold * movementThreshold;
            if (!force && (Time.unscaledTime < _nextScanTime || !movedEnough))
            {
                return;
            }

            BeginScanBuild(center, desiredWorldWidth, desiredWorldHeight);
        }

        private void BeginScanBuild(
            LogicalPosition center,
            float desiredWorldWidth,
            float desiredWorldHeight)
        {
            EnsureTexture();
            _scanBuildResolution = Mathf.Clamp(
                _settings.WorldMapScanTextureResolution,
                32,
                1024);
            int requiredPixelCount = _scanBuildResolution * _scanBuildResolution;
            if (_scanPixels == null || _scanPixels.Length != requiredPixelCount)
            {
                _scanPixels = new Color[requiredPixelCount];
            }
            _scanBuildCenterX = center.X;
            _scanBuildCenterZ = center.Z;
            LogicalPosition dronePosition = _world.LogicalPlayerPosition;
            _scanBuildDroneX = dronePosition.X;
            _scanBuildDroneZ = dronePosition.Z;
            _scanBuildWorldWidth = desiredWorldWidth;
            _scanBuildWorldHeight = desiredWorldHeight;
            _scanBuildRow = 0;
            _scanBuildActive = true;
        }

        private void ProcessScanBuild()
        {
            if (!_scanBuildActive || _scanTexture == null)
            {
                return;
            }

            int resolution = _scanBuildResolution;
            int rowsPerFrame = Mathf.Clamp(
                _settings.WorldMapScanRowsPerFrame,
                1,
                resolution);
            int finalRow = Mathf.Min(resolution, _scanBuildRow + rowsPerFrame);
            float radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            float minimumHeight = Mathf.Min(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float maximumHeight = Mathf.Max(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float heightRange = Mathf.Max(Mathf.Epsilon, maximumHeight - minimumHeight);
            float contourSpacing = Mathf.Max(0.01f, _settings.ContourSpacing);

            for (int y = _scanBuildRow; y < finalRow; y++)
            {
                float normalizedY = (y + 0.5f) / resolution;
                float offsetZ = (normalizedY - 0.5f) * _scanBuildWorldHeight;
                for (int x = 0; x < resolution; x++)
                {
                    int index = (y * resolution) + x;
                    float normalizedX = (x + 0.5f) / resolution;
                    float offsetX = (normalizedX - 0.5f) * _scanBuildWorldWidth;
                    double logicalX = _scanBuildCenterX + offsetX;
                    double logicalZ = _scanBuildCenterZ + offsetZ;
                    if (!IsExplored(logicalX, logicalZ))
                    {
                        _scanPixels[index] = _settings.UnexploredColor;
                        continue;
                    }

                    float height = (float)_world.HeightField.SampleHeight(
                        logicalX,
                        logicalZ);
                    float droneOffsetX = (float)(logicalX - _scanBuildDroneX);
                    float droneOffsetZ = (float)(logicalZ - _scanBuildDroneZ);
                    float distance = Mathf.Sqrt(
                        (droneOffsetX * droneOffsetX) +
                        (droneOffsetZ * droneOffsetZ));
                    if (Mathf.Abs(distance - radius) <= _settings.RadiusLineThickness)
                    {
                        _scanPixels[index] = _settings.RadiusLineColor;
                        continue;
                    }

                    float height01 = Mathf.Clamp01(
                        (((height - minimumHeight) / heightRange) - 0.5f) *
                        _settings.HeightContrast +
                        0.5f);
                    Color terrain = Color.Lerp(
                        _settings.TerrainLowColor,
                        _settings.TerrainHighColor,
                        height01);
                    float contourRemainder = Mathf.Repeat(Mathf.Abs(height), contourSpacing);
                    float contourDistance = Mathf.Min(
                        contourRemainder,
                        contourSpacing - contourRemainder);
                    if (contourDistance <= _settings.ContourThickness)
                    {
                        terrain = Color.Lerp(
                            terrain,
                            _settings.ContourColor,
                            _settings.ContourStrength);
                    }
                    _scanPixels[index] = terrain;
                }
            }

            _scanBuildRow = finalRow;
            if (_scanBuildRow < resolution)
            {
                return;
            }

            CommitCompletedScanTexture(resolution);
            _lastScanX = _scanBuildCenterX;
            _lastScanZ = _scanBuildCenterZ;
            _textureWorldWidth = _scanBuildWorldWidth;
            _textureWorldHeight = _scanBuildWorldHeight;
            _nextScanTime = Time.unscaledTime + _settings.ScanRefreshInterval;
            _scanBuildActive = false;
        }

        private void CommitCompletedScanTexture(int resolution)
        {
            if (_scanTexture != null && _scanTexture.width == resolution)
            {
                _scanTexture.SetPixels(_scanPixels);
                _scanTexture.Apply(false, false);
                return;
            }

            Texture2D completedTexture = CreateScanTexture(resolution);
            completedTexture.SetPixels(_scanPixels);
            completedTexture.Apply(false, false);
            Texture2D previousTexture = _scanTexture;
            _scanTexture = completedTexture;
            if (previousTexture != null)
            {
                Destroy(previousTexture);
            }
        }

        private float GetWorldMapViewportAspect()
        {
            Rect panelRect = GetWorldMapPanelRect();
            float scale = GetMapScale();
            float borderThickness = Mathf.Max(1f, _settings.BorderThickness * scale);
            float viewportWidth = Mathf.Max(
                1f,
                panelRect.width - (borderThickness * 2f));
            float viewportHeight = Mathf.Max(
                1f,
                panelRect.height -
                (borderThickness * 2f) -
                ((_settings.WorldMapHeaderHeight + _settings.WorldMapFooterHeight) * scale));
            return viewportWidth / viewportHeight;
        }

        private Rect GetWorldMapPanelRect()
        {
            Rect safeArea = Screen.safeArea;
            float requestedPadding = Mathf.Max(0f, _settings.WorldMapScreenPadding);
            float horizontalPadding = Mathf.Min(
                requestedPadding,
                Mathf.Max(0f, (safeArea.width - 1f) * 0.5f));
            float verticalPadding = Mathf.Min(
                requestedPadding,
                Mathf.Max(0f, (safeArea.height - 1f) * 0.5f));
            float availableWidth = Mathf.Max(
                1f,
                safeArea.width - (horizontalPadding * 2f));
            float availableHeight = Mathf.Max(
                1f,
                safeArea.height - (verticalPadding * 2f));

            if (_settings.WorldMapExpandToAvailableScreen)
            {
                return new Rect(
                    safeArea.xMin + horizontalPadding,
                    Screen.height - safeArea.yMax + verticalPadding,
                    availableWidth,
                    availableHeight);
            }

            float panelAspect = Mathf.Max(1f, _settings.WorldMapPanelAspectRatio);
            float panelHeight = Mathf.Min(
                _settings.WorldMapMaximumSize,
                Mathf.Min(availableHeight, availableWidth / panelAspect));
            float panelWidth = panelHeight * panelAspect;
            return new Rect(
                safeArea.center.x - (panelWidth * 0.5f),
                (Screen.height - safeArea.yMax) + ((safeArea.height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);
        }

        private void StartWorldAtlasBuild()
        {
            if (_settings == null ||
                !_settings.PrebuildWorldMapAtlasOnLoad ||
                _world == null ||
                _world.HeightField == null ||
                _worldAtlasBuildTask != null)
            {
                return;
            }

            float cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            HashSet<long> exploredSnapshot = new HashSet<long>(_exploredCells);
            LogicalPosition playerPosition = _world.LogicalPlayerPosition;
            double minimumX = playerPosition.X;
            double maximumX = playerPosition.X;
            double minimumZ = playerPosition.Z;
            double maximumZ = playerPosition.Z;
            foreach (long packedCell in exploredSnapshot)
            {
                int cellX = (int)(packedCell >> 32);
                int cellZ = unchecked((int)(uint)packedCell);
                minimumX = Math.Min(minimumX, cellX * (double)cellSize);
                maximumX = Math.Max(maximumX, (cellX + 1d) * cellSize);
                minimumZ = Math.Min(minimumZ, cellZ * (double)cellSize);
                maximumZ = Math.Max(maximumZ, (cellZ + 1d) * cellSize);
            }

            float margin = Mathf.Max(0f, _settings.WorldMapAtlasExplorationMargin);
            float viewportAspect = Mathf.Max(1f, GetWorldMapViewportAspect());
            float worldHeight = Mathf.Max(
                _settings.WorldMapMaximumWorldSize,
                (float)(maximumZ - minimumZ) + (margin * 2f));
            float worldWidth = worldHeight * viewportAspect;
            float requiredWidth = (float)(maximumX - minimumX) + (margin * 2f);
            if (requiredWidth > worldWidth)
            {
                worldWidth = requiredWidth;
                worldHeight = worldWidth / viewportAspect;
            }

            double centerX = (minimumX + maximumX) * 0.5d;
            double centerZ = (minimumZ + maximumZ) * 0.5d;
            int resolution = Mathf.Clamp(
                _settings.WorldMapAtlasTextureResolution,
                512,
                4096);
            DuneHeightField heightField = _world.HeightField;
            Color32 unexploredColor = _settings.UnexploredColor;
            Color32 terrainLowColor = _settings.TerrainLowColor;
            Color32 terrainHighColor = _settings.TerrainHighColor;
            Color32 contourColor = _settings.ContourColor;
            float minimumHeight = Mathf.Min(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float maximumHeight = Mathf.Max(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float heightContrast = _settings.HeightContrast;
            float contourSpacing = Mathf.Max(0.01f, _settings.ContourSpacing);
            float contourThickness = _settings.ContourThickness;
            float contourStrength = _settings.ContourStrength;

            _worldAtlasBuildCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _worldAtlasBuildCancellation.Token;
            _worldAtlasBuildTask = Task.Factory.StartNew(
                () => BuildWorldAtlas(
                    heightField,
                    exploredSnapshot,
                    cellSize,
                    resolution,
                    centerX,
                    centerZ,
                    worldWidth,
                    worldHeight,
                    unexploredColor,
                    terrainLowColor,
                    terrainHighColor,
                    contourColor,
                    minimumHeight,
                    maximumHeight,
                    heightContrast,
                    contourSpacing,
                    contourThickness,
                    contourStrength,
                    cancellationToken),
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private static WorldAtlasBuildResult BuildWorldAtlas(
            DuneHeightField heightField,
            HashSet<long> exploredCells,
            float cellSize,
            int resolution,
            double centerX,
            double centerZ,
            float worldWidth,
            float worldHeight,
            Color32 unexploredColor,
            Color32 terrainLowColor,
            Color32 terrainHighColor,
            Color32 contourColor,
            float minimumHeight,
            float maximumHeight,
            float heightContrast,
            float contourSpacing,
            float contourThickness,
            float contourStrength,
            CancellationToken cancellationToken)
        {
            Color32[] pixels = new Color32[resolution * resolution];
            float heightRange = Math.Max(float.Epsilon, maximumHeight - minimumHeight);
            double minimumWorldX = centerX - (worldWidth * 0.5d);
            double minimumWorldZ = centerZ - (worldHeight * 0.5d);
            for (int y = 0; y < resolution; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double logicalZ =
                    minimumWorldZ + (((y + 0.5d) / resolution) * worldHeight);
                int cellZ = (int)Math.Floor(logicalZ / cellSize);
                for (int x = 0; x < resolution; x++)
                {
                    int pixelIndex = (y * resolution) + x;
                    double logicalX =
                        minimumWorldX + (((x + 0.5d) / resolution) * worldWidth);
                    int cellX = (int)Math.Floor(logicalX / cellSize);
                    if (!exploredCells.Contains(PackCell(cellX, cellZ)))
                    {
                        pixels[pixelIndex] = unexploredColor;
                        continue;
                    }

                    float height = (float)heightField.SampleHeight(logicalX, logicalZ);
                    float height01 = Math.Clamp(
                        ((((height - minimumHeight) / heightRange) - 0.5f) *
                         heightContrast) +
                        0.5f,
                        0f,
                        1f);
                    Color32 terrain = LerpColor32(
                        terrainLowColor,
                        terrainHighColor,
                        height01);
                    float contourRemainder =
                        (float)(Math.Abs(height) % contourSpacing);
                    float contourDistance = Math.Min(
                        contourRemainder,
                        contourSpacing - contourRemainder);
                    if (contourDistance <= contourThickness)
                    {
                        terrain = LerpColor32(
                            terrain,
                            contourColor,
                            contourStrength);
                    }
                    pixels[pixelIndex] = terrain;
                }
            }

            return new WorldAtlasBuildResult
            {
                Resolution = resolution,
                Pixels = pixels,
                CenterX = centerX,
                CenterZ = centerZ,
                WorldWidth = worldWidth,
                WorldHeight = worldHeight,
            };
        }

        private static Color32 LerpColor32(Color32 from, Color32 to, float amount)
        {
            float clampedAmount = Math.Clamp(amount, 0f, 1f);
            return new Color32(
                (byte)Math.Round(from.r + ((to.r - from.r) * clampedAmount)),
                (byte)Math.Round(from.g + ((to.g - from.g) * clampedAmount)),
                (byte)Math.Round(from.b + ((to.b - from.b) * clampedAmount)),
                (byte)Math.Round(from.a + ((to.a - from.a) * clampedAmount)));
        }

        private void CompleteWorldAtlasBuildIfReady()
        {
            if (_worldAtlasBuildTask == null ||
                !_worldAtlasBuildTask.IsCompleted)
            {
                return;
            }

            Task<WorldAtlasBuildResult> completedTask = _worldAtlasBuildTask;
            _worldAtlasBuildTask = null;
            _worldAtlasBuildCancellation?.Dispose();
            _worldAtlasBuildCancellation = null;
            if (completedTask.IsCanceled)
            {
                return;
            }
            if (completedTask.IsFaulted)
            {
                Debug.LogWarning(
                    $"Unable to prebuild world map atlas: " +
                    $"{completedTask.Exception?.GetBaseException().Message}");
                return;
            }

            WorldAtlasBuildResult result = completedTask.Result;
            Texture2D atlasTexture = CreateScanTexture(result.Resolution);
            atlasTexture.name = "Dune Vector Prebuilt World Atlas";
            atlasTexture.SetPixels32(result.Pixels);
            atlasTexture.Apply(false, true);
            if (_worldAtlasTexture != null)
            {
                Destroy(_worldAtlasTexture);
            }
            _worldAtlasTexture = atlasTexture;
            _worldAtlasCenterX = result.CenterX;
            _worldAtlasCenterZ = result.CenterZ;
            _worldAtlasWorldWidth = result.WorldWidth;
            _worldAtlasWorldHeight = result.WorldHeight;
        }

        private void RevealAroundPlayer(bool force)
        {
            if (_settings == null || !_settings.Enabled || _world == null)
            {
                return;
            }

            LogicalPosition center = _world.LogicalPlayerPosition;
            double dx = center.X - _lastRevealX;
            double dz = center.Z - _lastRevealZ;
            double threshold = Mathf.Max(0.1f, _settings.ExplorationUpdateMovement);
            if (!force && ((dx * dx) + (dz * dz)) < threshold * threshold)
            {
                return;
            }

            double cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            double radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            bool hasPreviousReveal =
                !force &&
                !double.IsInfinity(_lastRevealX) &&
                !double.IsInfinity(_lastRevealZ);
            int minimumX = Mathf.FloorToInt((float)((center.X - radius) / cellSize));
            int maximumX = Mathf.FloorToInt((float)((center.X + radius) / cellSize));
            int minimumZ = Mathf.FloorToInt((float)((center.Z - radius) / cellSize));
            int maximumZ = Mathf.FloorToInt((float)((center.Z + radius) / cellSize));
            double radiusSquared = radius * radius;
            bool discoveredAny = false;

            for (int cellZ = minimumZ; cellZ <= maximumZ; cellZ++)
            {
                double sampleZ = (cellZ + 0.5d) * cellSize;
                double cellDz = sampleZ - center.Z;
                for (int cellX = minimumX; cellX <= maximumX; cellX++)
                {
                    double sampleX = (cellX + 0.5d) * cellSize;
                    double cellDx = sampleX - center.X;
                    if ((cellDx * cellDx) + (cellDz * cellDz) > radiusSquared)
                    {
                        continue;
                    }

                    if (hasPreviousReveal)
                    {
                        double previousDx = sampleX - _lastRevealX;
                        double previousDz = sampleZ - _lastRevealZ;
                        if ((previousDx * previousDx) + (previousDz * previousDz) <=
                            radiusSquared)
                        {
                            continue;
                        }
                    }

                    long packedCell = PackCell(cellX, cellZ);
                    if (_exploredCells.Add(packedCell))
                    {
                        discoveredAny = true;
                        if (!_explorationNeedsRewrite)
                        {
                            _pendingExplorationCells.Add(packedCell);
                        }
                        AddExploredTerrainBaseTile(sampleX, sampleZ);
                    }
                }
            }

            _lastRevealX = center.X;
            _lastRevealZ = center.Z;
            if (discoveredAny)
            {
                _explorationRevision++;
                _explorationDirty = true;
                _worldMapTileCache?.MarkExplorationChanged();
            }
        }

        private bool IsExplored(double logicalX, double logicalZ)
        {
            double cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            int cellX = Mathf.FloorToInt((float)(logicalX / cellSize));
            int cellZ = Mathf.FloorToInt((float)(logicalZ / cellSize));
            return _exploredCells.Contains(PackCell(cellX, cellZ));
        }

        private void RebuildExploredTerrainBaseTiles()
        {
            _exploredTerrainBaseTiles.Clear();
            if (_settings == null)
            {
                return;
            }

            double cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            foreach (long packedCell in _exploredCells)
            {
                int cellX = (int)(packedCell >> 32);
                int cellZ = unchecked((int)(uint)packedCell);
                AddExploredTerrainBaseTile(
                    (cellX + 0.5d) * cellSize,
                    (cellZ + 0.5d) * cellSize);
            }
        }

        private void AddExploredTerrainBaseTile(double logicalX, double logicalZ)
        {
            double tileSize = Mathf.Max(32f, _settings.WorldMapTerrainBaseTileWorldSize);
            int tileX = (int)Math.Floor(logicalX / tileSize);
            int tileZ = (int)Math.Floor(logicalZ / tileSize);
            _exploredTerrainBaseTiles.Add(PackCell(tileX, tileZ));
        }

        private bool IsWorldMapTerrainTileExplored(int lod, int tileX, int tileZ)
        {
            int baseTileSpan = 1 << Mathf.Clamp(lod, 0, 30);
            long minimumX = tileX * (long)baseTileSpan;
            long maximumX = minimumX + baseTileSpan;
            long minimumZ = tileZ * (long)baseTileSpan;
            long maximumZ = minimumZ + baseTileSpan;
            foreach (long packedTile in _exploredTerrainBaseTiles)
            {
                int exploredX = (int)(packedTile >> 32);
                int exploredZ = unchecked((int)(uint)packedTile);
                if (exploredX >= minimumX &&
                    exploredX < maximumX &&
                    exploredZ >= minimumZ &&
                    exploredZ < maximumZ)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsGeoglyphExplored(GeoglyphArtworkPlacement artwork)
        {
            if (artwork == null)
            {
                return false;
            }

            GetRotatedGeoglyphSize(artwork, out float width, out float height);
            double cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            int minimumX = Mathf.FloorToInt(
                (float)((artwork.WorldCenter.x - (width * 0.5f)) / cellSize));
            int maximumX = Mathf.FloorToInt(
                (float)((artwork.WorldCenter.x + (width * 0.5f)) / cellSize));
            int minimumZ = Mathf.FloorToInt(
                (float)((artwork.WorldCenter.y - (height * 0.5f)) / cellSize));
            int maximumZ = Mathf.FloorToInt(
                (float)((artwork.WorldCenter.y + (height * 0.5f)) / cellSize));

            for (int cellZ = minimumZ; cellZ <= maximumZ; cellZ++)
            {
                for (int cellX = minimumX; cellX <= maximumX; cellX++)
                {
                    if (_exploredCells.Contains(PackCell(cellX, cellZ)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static long PackCell(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        private string GetExplorationPath()
        {
            string fileName = string.IsNullOrWhiteSpace(_settings.ExplorationFileName)
                ? "DuneVectorMapExploration.dat"
                : Path.GetFileName(_settings.ExplorationFileName);
            if (!fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".dat";
            }
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        private void LoadExploration()
        {
            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            string path = GetExplorationPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                using BinaryReader reader = new BinaryReader(stream);
                if (reader.ReadInt32() != ExplorationFileMagic)
                {
                    _explorationNeedsRewrite = true;
                    _explorationDirty = true;
                    return;
                }

                int version = reader.ReadInt32();
                if (version != ExplorationFileVersion &&
                    version != LegacyExplorationFileVersion)
                {
                    _explorationNeedsRewrite = true;
                    _explorationDirty = true;
                    return;
                }
                float savedCellSize = Mathf.Max(1f, reader.ReadSingle());
                float currentCellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
                if (version == LegacyExplorationFileVersion)
                {
                    int count = reader.ReadInt32();
                    for (int index = 0;
                        index < count && stream.Length - stream.Position >= sizeof(long);
                        index++)
                    {
                        AddLoadedExplorationCell(
                            reader.ReadInt64(),
                            savedCellSize,
                            currentCellSize);
                    }
                }
                else
                {
                    while (stream.Length - stream.Position >= sizeof(long))
                    {
                        AddLoadedExplorationCell(
                            reader.ReadInt64(),
                            savedCellSize,
                            currentCellSize);
                    }
                }

                _explorationNeedsRewrite =
                    version != ExplorationFileVersion ||
                    !Mathf.Approximately(savedCellSize, currentCellSize);
                _explorationDirty = _explorationNeedsRewrite;
            }
            catch (IOException exception)
            {
                _explorationNeedsRewrite = true;
                _explorationDirty = true;
                Debug.LogWarning($"Unable to load map exploration: {exception.Message}");
            }
        }

        private void AddLoadedExplorationCell(
            long packedCell,
            float savedCellSize,
            float currentCellSize)
        {
            if (Mathf.Approximately(savedCellSize, currentCellSize))
            {
                _exploredCells.Add(packedCell);
                return;
            }

            int savedX = (int)(packedCell >> 32);
            int savedZ = unchecked((int)(uint)packedCell);
            double minimumX = savedX * (double)savedCellSize;
            double maximumX = (savedX + 1d) * savedCellSize;
            double minimumZ = savedZ * (double)savedCellSize;
            double maximumZ = (savedZ + 1d) * savedCellSize;
            int minimumCellX = (int)Math.Floor(minimumX / currentCellSize);
            int maximumCellX = (int)Math.Ceiling(maximumX / currentCellSize) - 1;
            int minimumCellZ = (int)Math.Floor(minimumZ / currentCellSize);
            int maximumCellZ = (int)Math.Ceiling(maximumZ / currentCellSize) - 1;
            for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
            {
                for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                {
                    _exploredCells.Add(PackCell(cellX, cellZ));
                }
            }
        }

        private void SaveExplorationIfDue()
        {
            CompleteExplorationSaveIfReady();
            if (!_explorationDirty || Time.unscaledTime < _nextExplorationSaveTime)
            {
                return;
            }
            if (_explorationNeedsRewrite && !_worldMapVisible)
            {
                return;
            }
            SaveExploration(false);
        }

        private void SaveExploration(bool flush)
        {
            if (!_explorationDirty || _settings == null)
            {
                if (flush && _explorationSaveTask != null)
                {
                    _explorationSaveTask.GetAwaiter().GetResult();
                    CompleteExplorationSaveIfReady();
                }
                return;
            }

            if (_explorationSaveTask != null)
            {
                if (!flush)
                {
                    return;
                }
                _explorationSaveTask.GetAwaiter().GetResult();
                CompleteExplorationSaveIfReady();
            }

            BeginExplorationSave();
            if (flush && _explorationSaveTask != null)
            {
                _explorationSaveTask.GetAwaiter().GetResult();
                CompleteExplorationSaveIfReady();
            }
        }

        private void BeginExplorationSave()
        {
            if (_explorationSaveTask != null || _settings == null)
            {
                return;
            }

            bool rewriteFile = _explorationNeedsRewrite;
            long[] cells;
            if (rewriteFile)
            {
                cells = new long[_exploredCells.Count];
                _exploredCells.CopyTo(cells);
                _pendingExplorationCells.Clear();
                _explorationNeedsRewrite = false;
            }
            else
            {
                if (_pendingExplorationCells.Count == 0)
                {
                    _explorationDirty = false;
                    return;
                }
                cells = _pendingExplorationCells.ToArray();
                _pendingExplorationCells.Clear();
            }

            string path = GetExplorationPath();
            float cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            _explorationDirty = false;
            _nextExplorationSaveTime =
                Time.unscaledTime + Mathf.Max(1f, _settings.ExplorationSaveInterval);
            _explorationSaveTask = Task.Run(
                () => PersistExploration(path, cellSize, cells, rewriteFile));
        }

        private void CompleteExplorationSaveIfReady()
        {
            if (_explorationSaveTask == null || !_explorationSaveTask.IsCompleted)
            {
                return;
            }

            ExplorationSaveResult result = _explorationSaveTask.GetAwaiter().GetResult();
            _explorationSaveTask = null;
            if (result.Error == null)
            {
                _explorationDirty =
                    _explorationNeedsRewrite || _pendingExplorationCells.Count > 0;
                return;
            }

            if (result.RewroteFile)
            {
                _explorationNeedsRewrite = true;
            }
            else if (result.Cells != null)
            {
                _pendingExplorationCells.AddRange(result.Cells);
            }
            _explorationDirty = true;
            _nextExplorationSaveTime =
                Time.unscaledTime + Mathf.Max(1f, _settings.ExplorationSaveInterval);
            Debug.LogWarning($"Unable to save map exploration: {result.Error.Message}");
        }

        private static ExplorationSaveResult PersistExploration(
            string path,
            float cellSize,
            long[] cells,
            bool rewriteFile)
        {
            try
            {
                if (rewriteFile || !File.Exists(path))
                {
                    WriteExplorationJournal(path, cellSize, cells);
                }
                else
                {
                    AppendExplorationJournal(path, cells);
                }
                return new ExplorationSaveResult
                {
                    RewroteFile = rewriteFile,
                    Cells = cells,
                };
            }
            catch (Exception exception)
            {
                return new ExplorationSaveResult
                {
                    RewroteFile = rewriteFile,
                    Cells = cells,
                    Error = exception,
                };
            }
        }

        private static void WriteExplorationJournal(
            string path,
            float cellSize,
            long[] cells)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string pendingPath = Path.Combine(
                directory ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(path)}.pending.dat");
            using (FileStream stream = File.Create(pendingPath))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(ExplorationFileMagic);
                writer.Write(ExplorationFileVersion);
                writer.Write(cellSize);
                for (int index = 0; index < cells.Length; index++)
                {
                    writer.Write(cells[index]);
                }
            }
            File.Copy(pendingPath, path, true);
            File.Delete(pendingPath);
        }

        private static void AppendExplorationJournal(string path, long[] cells)
        {
            using FileStream stream = File.Open(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            using BinaryWriter writer = new BinaryWriter(stream);
            for (int index = 0; index < cells.Length; index++)
            {
                writer.Write(cells[index]);
            }
        }

        private void EnsureTexture()
        {
            if (_scanTexture != null)
            {
                return;
            }

            int resolution = Mathf.Clamp(
                _settings.WorldMapScanTextureResolution,
                32,
                1024);
            _scanTexture = CreateScanTexture(resolution);
            Color[] initialPixels = new Color[resolution * resolution];
            for (int index = 0; index < initialPixels.Length; index++)
            {
                initialPixels[index] = _settings.UnexploredColor;
            }
            _scanTexture.SetPixels(initialPixels);
            _scanTexture.Apply(false, false);
        }

        private static Texture2D CreateScanTexture(int resolution)
        {
            return new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false)
            {
                name = "Dune Vector Runtime Drone Scan",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        private void EnsureStyles()
        {
            _worldMapTitleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleLeft);
            _northStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _worldMapDetailStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleLeft);
            _worldMapHintStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleRight);
            _worldMapScaleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.LowerLeft);
            _markerStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _landmarkIconStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _landmarkIconShadowStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);

            float scale = GetMapScale();
            _worldMapTitleStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.WorldMapTitleFontSize * scale));
            _worldMapTitleStyle.normal.textColor = _settings.TitleColor;
            _northStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DetailFontSize * scale));
            _northStyle.normal.textColor = _settings.TitleColor;
            _worldMapDetailStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.WorldMapFooterFontSize * scale));
            _worldMapDetailStyle.normal.textColor = _settings.DetailColor;
            _worldMapHintStyle.fontSize = _worldMapDetailStyle.fontSize;
            _worldMapHintStyle.normal.textColor = _settings.DetailColor;
            _worldMapScaleStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DetailFontSize * scale));
            _worldMapScaleStyle.normal.textColor = Color.white;
            _markerStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DroneMarkerFontSize * scale));
            _markerStyle.normal.textColor = _settings.DroneMarkerColor;
            _landmarkIconStyle.normal.textColor = _settings.LandmarkIconColor;
            _landmarkIconShadowStyle.normal.textColor = _settings.IconShadowColor;
            UpdateIconStyles(scale);
        }

        private float GetMapScale()
        {
            if (_bottomHud == null)
            {
                return 1f;
            }
            return DuneVectorBottomHud.GetScale(_bottomHud);
        }

        private static GUIStyle CreateStyle(FontStyle fontStyle, TextAnchor alignment)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontStyle = fontStyle,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void OnDestroy()
        {
            if (_permanentUpgrades != null)
            {
                _permanentUpgrades.AtlasGlyphBrushedMetalEnabledChanged -=
                    HandleAtlasGlyphBrushedMetalEnabledChanged;
            }
            _worldAtlasBuildCancellation?.Cancel();
            SetWorldMapVisible(false);
            SaveExploration(true);
            if (_scanTexture != null)
            {
                Destroy(_scanTexture);
            }
            if (_worldAtlasTexture != null)
            {
                Destroy(_worldAtlasTexture);
            }
            if (_worldMapScanRingTexture != null)
            {
                Destroy(_worldMapScanRingTexture);
            }
            _worldMapTileCache?.Dispose();
            _worldMapTileCache = null;
            if (_geoglyphMapMaterial != null)
            {
                Destroy(_geoglyphMapMaterial);
            }
            ClearGeoglyphMapTextures();
            if (_worldMapGui != null)
            {
                _worldMapGui.enabled = false;
                _worldMapGui.Owner = null;
                _worldMapGui = null;
            }
        }

        private void OnDisable()
        {
            SetWorldMapVisible(false);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveExploration(true);
            }
        }

        private void OnApplicationQuit()
        {
            SaveExploration(true);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWorldMapGUI : MonoBehaviour
    {
        internal DuneVectorMapHUD Owner { get; set; }

        private void OnGUI()
        {
            Owner?.DrawWorldMapGUI();
        }
    }
}
