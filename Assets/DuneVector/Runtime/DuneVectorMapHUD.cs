using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMapHUD : MonoBehaviour
    {
        private enum MapIconKind
        {
            Ring,
            Landmark,
            Geoglyph,
        }

        private readonly struct MapIconRecord
        {
            public readonly double X;
            public readonly double Z;
            public readonly MapIconKind Kind;
            public readonly TraversalRingType RingType;
            public readonly DuneLandmarkType LandmarkType;
            public readonly GeoglyphArtworkPlacement Artwork;

            public MapIconRecord(double x, double z, TraversalRingType ringType)
            {
                X = x;
                Z = z;
                Kind = MapIconKind.Ring;
                RingType = ringType;
                LandmarkType = default;
                Artwork = null;
            }

            public MapIconRecord(double x, double z, DuneLandmarkType landmarkType)
            {
                X = x;
                Z = z;
                Kind = MapIconKind.Landmark;
                RingType = default;
                LandmarkType = landmarkType;
                Artwork = null;
            }

            public MapIconRecord(GeoglyphArtworkPlacement artwork)
            {
                X = artwork.WorldCenter.x;
                Z = artwork.WorldCenter.y;
                Kind = MapIconKind.Geoglyph;
                RingType = default;
                LandmarkType = default;
                Artwork = artwork;
            }
        }

        public bool IsWorldMapVisible => _worldMapVisible;
        public bool IsMinimapVisible => _minimapVisible;
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

        private DroneCharacterController _drone;
        private DesertWorldStreamer _world;
        private BottomHudTuning _bottomHud;
        private MapHudTuning _settings;
        private GeoglyphSystemTuning _geoglyphs;
        private Texture2D _scanTexture;
        private Material _geoglyphMapMaterial;
        private Color[] _scanPixels;
        private readonly HashSet<long> _exploredCells = new HashSet<long>();
        private readonly List<MapIconRecord> _mapIcons = new List<MapIconRecord>();
        private readonly List<MapIconRecord> _upperFlightMapIcons = new List<MapIconRecord>();
        private readonly Dictionary<GeoglyphArtworkPlacement, Texture2D> _geoglyphMapTextures =
            new Dictionary<GeoglyphArtworkPlacement, Texture2D>();
        private readonly Queue<GeoglyphArtworkPlacement> _geoglyphTextureBuildQueue =
            new Queue<GeoglyphArtworkPlacement>();
        private readonly HashSet<GeoglyphArtworkPlacement> _queuedGeoglyphTextures =
            new HashSet<GeoglyphArtworkPlacement>();
        private readonly HashSet<GeoglyphArtworkPlacement> _exploredGeoglyphs =
            new HashSet<GeoglyphArtworkPlacement>();
        private GUIStyle _minimapTitleStyle;
        private GUIStyle _worldMapTitleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _ringIconStyle;
        private GUIStyle _landmarkIconStyle;
        private GUIStyle _ringIconShadowStyle;
        private GUIStyle _landmarkIconShadowStyle;
        private bool _worldMapVisible;
        private bool _minimapVisible;
        private double _lastScanX = double.PositiveInfinity;
        private double _lastScanZ = double.PositiveInfinity;
        private double _lastRevealX = double.PositiveInfinity;
        private double _lastRevealZ = double.PositiveInfinity;
        private float _textureWorldSize;
        private float _nextScanTime;
        private float _nextExplorationSaveTime;
        private float _nextIconRefreshTime;
        private bool _explorationDirty;
        private bool _forceScanRefresh;
        private bool _scanBuildActive;
        private int _scanBuildRow;
        private double _scanBuildCenterX;
        private double _scanBuildCenterZ;
        private float _scanBuildWorldSize;

        private const int ExplorationFileMagic = 0x44564D50;
        private const int ExplorationFileVersion = 2;

        public void Initialize(
            DroneCharacterController drone,
            DesertWorldStreamer world,
            BottomHudTuning bottomHud,
            MapHudTuning settings,
            GeoglyphSystemTuning geoglyphs)
        {
            _drone = drone;
            _world = world;
            _bottomHud = bottomHud;
            _settings = settings;
            _geoglyphs = geoglyphs;
            Shader geoglyphMapShader = Shader.Find("Hidden/DuneVector/Map Geoglyph Mask");
            if (geoglyphMapShader != null)
            {
                _geoglyphMapMaterial = new Material(geoglyphMapShader)
                {
                    name = "Dune Vector Map Geoglyph Mask - Runtime",
                    hideFlags = HideFlags.DontSave,
                };
            }
            _minimapVisible = settings != null && settings.MinimapVisibleByDefault;
            LoadExploration();
            RevealAroundPlayer(true);
            RefreshScan(true);
        }

        private void Update()
        {
            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            if (DuneVectorCourierGame.IsMapHudSuppressed)
            {
                _worldMapVisible = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (_settings.WorldMapKey != Key.None &&
                    keyboard[_settings.WorldMapKey].wasPressedThisFrame)
                {
                    _worldMapVisible = !_worldMapVisible;
                    _forceScanRefresh = true;
                }

                if (_settings.MinimapKey != Key.None &&
                    keyboard[_settings.MinimapKey].wasPressedThisFrame)
                {
                    _minimapVisible = !_minimapVisible;
                    _forceScanRefresh = true;
                }
            }

            RevealAroundPlayer(false);
            SaveExplorationIfDue();
            RefreshMapIconsIfDue();
            BuildQueuedGeoglyphTextures();

            if (_worldMapVisible || _minimapVisible)
            {
                RefreshScan(_forceScanRefresh);
                _forceScanRefresh = false;
                ProcessScanBuild();
            }
        }

        private void OnGUI()
        {
            if (_settings == null ||
                !_settings.Enabled ||
                _drone == null ||
                _world == null ||
                _scanTexture == null ||
                DuneVectorCourierGame.IsMapHudSuppressed)
            {
                return;
            }

            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            EnsureStyles();

            if (_worldMapVisible)
            {
                DrawWorldMap();
            }
            else if (_minimapVisible && _bottomHud != null)
            {
                DrawMinimap();
            }

            GUI.depth = previousDepth;
        }

        private void DrawMinimap()
        {
            float scale = DuneVectorBottomHud.GetScale(_bottomHud);
            Rect speedometer = DuneVectorBottomHud.GetPanelRect(
                _bottomHud,
                DuneVectorBottomHudPanel.Speed);
            float size = _settings.MinimapSize * scale;
            Rect safeArea = Screen.safeArea;
            float y = speedometer.y - (_settings.GapAboveSpeedometer * scale) - size;
            y = Mathf.Max(Screen.height - safeArea.yMax, y);
            Rect mapRect = new Rect(
                speedometer.x,
                y,
                size,
                size);

            DrawMapPanel(
                mapRect,
                _settings.MinimapWorldSize,
                _settings.MinimapTitle,
                false,
                scale);
        }

        private void DrawWorldMap()
        {
            Color overlay = _settings.OverlayColor;
            overlay.a *= _settings.OverlayOpacity;
            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), overlay);

            Rect safeArea = Screen.safeArea;
            float availableWidth = Mathf.Max(
                1f,
                safeArea.width - (_settings.WorldMapScreenPadding * 2f));
            float availableHeight = Mathf.Max(
                1f,
                safeArea.height - (_settings.WorldMapScreenPadding * 2f));
            float size = Mathf.Min(
                _settings.WorldMapMaximumSize,
                Mathf.Min(availableWidth, availableHeight));
            Rect mapRect = new Rect(
                safeArea.center.x - (size * 0.5f),
                (Screen.height - safeArea.yMax) + ((safeArea.height - size) * 0.5f),
                size,
                size);

            DrawMapPanel(
                mapRect,
                _settings.WorldMapWorldSize,
                _settings.WorldMapTitle,
                true,
                GetMapScale());
        }

        private void DrawMapPanel(
            Rect mapRect,
            float displayedWorldSize,
            string title,
            bool showDetails,
            float scale)
        {
            DrawSolidRect(mapRect, _settings.PanelColor);
            DrawBorder(
                mapRect,
                _settings.BorderColor,
                Mathf.Max(1f, _settings.BorderThickness * scale));

            float scanSize = mapRect.width *
                (_textureWorldSize / Mathf.Max(1f, displayedWorldSize));
            LogicalPosition currentCenter = _world.LogicalPlayerPosition;
            float pixelsPerWorldUnit = mapRect.width / Mathf.Max(1f, displayedWorldSize);
            bool hasCompletedScan =
                !double.IsInfinity(_lastScanX) &&
                !double.IsInfinity(_lastScanZ);
            float scanOffsetX = hasCompletedScan
                ? (float)(_lastScanX - currentCenter.X) * pixelsPerWorldUnit
                : 0f;
            float scanOffsetY = hasCompletedScan
                ? (float)(currentCenter.Z - _lastScanZ) * pixelsPerWorldUnit
                : 0f;
            Rect localScanRect = new Rect(
                ((mapRect.width - scanSize) * 0.5f) + scanOffsetX,
                ((mapRect.height - scanSize) * 0.5f) + scanOffsetY,
                scanSize,
                scanSize);

            GUI.BeginGroup(mapRect);
            GUI.DrawTexture(localScanRect, _scanTexture, ScaleMode.StretchToFill, false);
            DrawMapIcons(mapRect, displayedWorldSize, currentCenter, showDetails, scale);
            DrawDroneMarker(
                new Vector2(mapRect.width * 0.5f, mapRect.height * 0.5f),
                scale);

            float padding = _settings.ContentPadding * scale;
            float titleHeight = _settings.TitleHeight * scale;
            GUI.Label(
                new Rect(padding, padding, mapRect.width - (padding * 2f), titleHeight),
                title,
                showDetails ? _worldMapTitleStyle : _minimapTitleStyle);
            GUI.Label(
                new Rect(
                    (mapRect.width - titleHeight) * 0.5f,
                    showDetails ? padding + titleHeight : padding,
                    titleHeight,
                    titleHeight),
                _settings.NorthLabel,
                _detailStyle);

            if (showDetails)
            {
                string coordinates = string.Format(
                    _settings.CoordinateFormat,
                    currentCenter.X,
                    currentCenter.Z,
                    _settings.DroneRevealRadius);
                float detailWidth = mapRect.width - (padding * 2f);
                float splitX = detailWidth * _settings.DetailSplitFraction;
                GUI.Label(
                    new Rect(
                        padding,
                        mapRect.height - padding - titleHeight,
                        splitX,
                        titleHeight),
                    coordinates,
                    _detailStyle);
                GUI.Label(
                    new Rect(
                        padding + splitX,
                        mapRect.height - padding - titleHeight,
                        detailWidth - splitX,
                        titleHeight),
                    _settings.WorldMapHint,
                    _hintStyle);
            }

            GUI.EndGroup();
        }

        private void DrawMapIcons(
            Rect mapRect,
            float displayedWorldSize,
            LogicalPosition center,
            bool worldMap,
            float scale)
        {
            float iconScale = scale * (worldMap ? 1f : _settings.MinimapIconScale);
            float halfWorldSize = displayedWorldSize * 0.5f;
            Vector2 shadowOffset = _settings.IconShadowOffset * iconScale;
            UpdateIconStyles(iconScale);

            for (int index = 0; index < _mapIcons.Count; index++)
            {
                MapIconRecord icon = _mapIcons[index];
                bool isExplored = icon.Kind == MapIconKind.Geoglyph
                    ? !worldMap || _exploredGeoglyphs.Contains(icon.Artwork)
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
                if (Math.Abs(deltaX) > halfWorldSize + footprintHalfWidth ||
                    Math.Abs(deltaZ) > halfWorldSize + footprintHalfHeight)
                {
                    continue;
                }

                Vector2 position = new Vector2(
                    mapRect.width * (0.5f + ((float)deltaX / displayedWorldSize)),
                    mapRect.height * (0.5f - ((float)deltaZ / displayedWorldSize)));
                if (icon.Kind == MapIconKind.Geoglyph)
                {
                    DrawGeoglyphArtwork(
                        icon.Artwork,
                        position,
                        mapRect,
                        displayedWorldSize);
                    continue;
                }

                float recordScale =
                    icon.Kind == MapIconKind.Ring &&
                    icon.RingType == TraversalRingType.UpperFlight
                        ? _settings.UpperFlightIconScale
                        : 1f;
                float boxSize = _settings.IconBoxSize * iconScale * recordScale;
                Rect iconRect = new Rect(
                    position.x - (boxSize * 0.5f),
                    position.y - (boxSize * 0.5f),
                    boxSize,
                    boxSize);
                string glyph = GetIconGlyph(icon);
                if (icon.Kind == MapIconKind.Ring)
                {
                    _ringIconStyle.normal.textColor = GetRingIconColor(icon.RingType);
                    _ringIconStyle.fontSize = Mathf.Max(
                        8,
                        Mathf.RoundToInt(
                            _settings.RingIconFontSize * iconScale * recordScale));
                    _ringIconShadowStyle.fontSize = _ringIconStyle.fontSize;
                }
                GUI.Label(
                    new Rect(
                        iconRect.x + shadowOffset.x,
                        iconRect.y + shadowOffset.y,
                        iconRect.width,
                        iconRect.height),
                    glyph,
                    icon.Kind == MapIconKind.Ring
                        ? _ringIconShadowStyle
                        : _landmarkIconShadowStyle);
                GUI.Label(
                    iconRect,
                    glyph,
                    icon.Kind == MapIconKind.Ring
                        ? _ringIconStyle
                        : _landmarkIconStyle);
            }
        }

        private string GetIconGlyph(MapIconRecord icon)
        {
            if (icon.Kind == MapIconKind.Ring)
            {
                return _settings.RingIcon;
            }

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

        private Color GetRingIconColor(TraversalRingType ringType)
        {
            return ringType switch
            {
                TraversalRingType.GroundBoost => _settings.YellowRingColor,
                TraversalRingType.Coin => _settings.YellowRingColor,
                TraversalRingType.Flight => _settings.WhiteRingColor,
                TraversalRingType.Health => _settings.WhiteRingColor,
                _ => _settings.PurplePortalColor,
            };
        }

        private void DrawGeoglyphArtwork(
            GeoglyphArtworkPlacement artwork,
            Vector2 position,
            Rect mapRect,
            float displayedWorldSize)
        {
            if (artwork == null ||
                !_geoglyphMapTextures.TryGetValue(artwork, out Texture2D mapTexture) ||
                mapTexture == null)
            {
                return;
            }

            GetRotatedGeoglyphSize(artwork, out float rotatedWorldWidth, out float rotatedWorldHeight);
            float width = mapRect.width *
                (rotatedWorldWidth / displayedWorldSize);
            float height = mapRect.height *
                (rotatedWorldHeight / displayedWorldSize);
            Rect artworkRect = new Rect(
                position.x - (width * 0.5f),
                position.y - (height * 0.5f),
                width,
                height);

            GUI.DrawTexture(artworkRect, mapTexture, ScaleMode.StretchToFill, true);
        }

        private void UpdateIconStyles(float iconScale)
        {
            _ringIconStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.RingIconFontSize * iconScale));
            _ringIconShadowStyle.fontSize = _ringIconStyle.fontSize;
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
            _upperFlightMapIcons.Clear();
            _exploredGeoglyphs.Clear();

            if (_settings.ShowRings)
            {
                foreach (TraversalRing ring in TraversalRing.ActiveRings)
                {
                    if (ring == null)
                    {
                        continue;
                    }

                    Vector3 position = ring.transform.position;
                    MapIconRecord record = new MapIconRecord(
                        _world.OriginOffsetX + position.x,
                        _world.OriginOffsetZ + position.z,
                        ring.RingType);
                    if (ring.RingType == TraversalRingType.UpperFlight)
                    {
                        _upperFlightMapIcons.Add(record);
                    }
                    else
                    {
                        _mapIcons.Add(record);
                    }
                }

                _mapIcons.AddRange(_upperFlightMapIcons);
            }

            if (_settings.ShowLandmarks)
            {
                DuneVectorLandmarkDirector director =
                    DuneVectorBootstrap.Instance != null
                        ? DuneVectorBootstrap.Instance.LandmarkDirector
                        : null;
                if (director != null)
                {
                    foreach (DuneLandmarkPlacementRecord record in director.PlacementRecords.Values)
                    {
                        if (record != null)
                        {
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
                        if (landmark != null)
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
                        if (!_geoglyphMapTextures.ContainsKey(placement) &&
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
                _geoglyphMapTextures.ContainsKey(artwork) ||
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
                Texture2D texture = BuildGeoglyphMapTexture(artwork);
                if (texture != null)
                {
                    _geoglyphMapTextures[artwork] = texture;
                }
            }
        }

        private Texture2D BuildGeoglyphMapTexture(GeoglyphArtworkPlacement artwork)
        {
            if (artwork == null || artwork.Mask == null || _geoglyphMapMaterial == null)
            {
                return null;
            }

            int maximumResolution = Mathf.Clamp(
                _settings.GeoglyphMapTextureResolution,
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
            _geoglyphMapMaterial.SetColor("_Color", mapColor);
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
                name = $"Map Geoglyph - {artwork.Mask.name}",
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
                (!_worldMapVisible && !_minimapVisible && !force))
            {
                return;
            }

            LogicalPosition center = _world.LogicalPlayerPosition;
            float desiredWorldSize = _worldMapVisible
                ? Mathf.Max(1f, _settings.WorldMapWorldSize)
                : Mathf.Max(1f, _settings.MinimapWorldSize);
            force |= !Mathf.Approximately(_textureWorldSize, desiredWorldSize);
            if (_scanBuildActive)
            {
                if (force && !Mathf.Approximately(_scanBuildWorldSize, desiredWorldSize))
                {
                    BeginScanBuild(center, desiredWorldSize);
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

            BeginScanBuild(center, desiredWorldSize);
        }

        private void BeginScanBuild(LogicalPosition center, float desiredWorldSize)
        {
            EnsureTexture();
            _scanBuildCenterX = center.X;
            _scanBuildCenterZ = center.Z;
            _scanBuildWorldSize = desiredWorldSize;
            _scanBuildRow = 0;
            _scanBuildActive = true;
        }

        private void ProcessScanBuild()
        {
            if (!_scanBuildActive || _scanTexture == null)
            {
                return;
            }

            int resolution = _scanTexture.width;
            int rowsPerFrame = Mathf.Clamp(_settings.ScanRowsPerFrame, 1, resolution);
            int finalRow = Mathf.Min(resolution, _scanBuildRow + rowsPerFrame);
            float radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            float diameter = _scanBuildWorldSize;
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
                float offsetZ = (normalizedY - 0.5f) * diameter;
                for (int x = 0; x < resolution; x++)
                {
                    int index = (y * resolution) + x;
                    float normalizedX = (x + 0.5f) / resolution;
                    float offsetX = (normalizedX - 0.5f) * diameter;
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
                    float distance = Mathf.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ));
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

            _scanTexture.SetPixels(_scanPixels);
            _scanTexture.Apply(false, false);
            _lastScanX = _scanBuildCenterX;
            _lastScanZ = _scanBuildCenterZ;
            _textureWorldSize = _scanBuildWorldSize;
            _nextScanTime = Time.unscaledTime + _settings.ScanRefreshInterval;
            _scanBuildActive = false;
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

                    discoveredAny |= _exploredCells.Add(PackCell(cellX, cellZ));
                }
            }

            _lastRevealX = center.X;
            _lastRevealZ = center.Z;
            if (discoveredAny)
            {
                _explorationDirty = true;
            }
        }

        private bool IsExplored(double logicalX, double logicalZ)
        {
            double cellSize = Mathf.Max(1f, _settings.ExplorationCellSize);
            int cellX = Mathf.FloorToInt((float)(logicalX / cellSize));
            int cellZ = Mathf.FloorToInt((float)(logicalZ / cellSize));
            return _exploredCells.Contains(PackCell(cellX, cellZ));
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
                if (reader.ReadInt32() != ExplorationFileMagic ||
                    reader.ReadInt32() != ExplorationFileVersion)
                {
                    return;
                }

                float savedCellSize = reader.ReadSingle();
                if (!Mathf.Approximately(savedCellSize, _settings.ExplorationCellSize))
                {
                    return;
                }
                int count = reader.ReadInt32();
                for (int index = 0; index < count && stream.Position < stream.Length; index++)
                {
                    _exploredCells.Add(reader.ReadInt64());
                }
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"Unable to load map exploration: {exception.Message}");
            }
        }

        private void SaveExplorationIfDue()
        {
            if (!_explorationDirty || Time.unscaledTime < _nextExplorationSaveTime)
            {
                return;
            }
            SaveExploration();
        }

        private void SaveExploration()
        {
            if (!_explorationDirty || _settings == null)
            {
                return;
            }

            try
            {
                string path = GetExplorationPath();
                using FileStream stream = File.Create(path);
                using BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(ExplorationFileMagic);
                writer.Write(ExplorationFileVersion);
                writer.Write(_settings.ExplorationCellSize);
                writer.Write(_exploredCells.Count);
                foreach (long cell in _exploredCells)
                {
                    writer.Write(cell);
                }
                _explorationDirty = false;
                _nextExplorationSaveTime =
                    Time.unscaledTime + Mathf.Max(1f, _settings.ExplorationSaveInterval);
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"Unable to save map exploration: {exception.Message}");
                _nextExplorationSaveTime =
                    Time.unscaledTime + Mathf.Max(1f, _settings.ExplorationSaveInterval);
            }
        }

        private void EnsureTexture()
        {
            int resolution = Mathf.Clamp(_settings.ScanTextureResolution, 32, 512);
            if (_scanTexture != null && _scanTexture.width == resolution)
            {
                return;
            }

            if (_scanTexture != null)
            {
                Destroy(_scanTexture);
            }

            _scanTexture = new Texture2D(
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
            _scanPixels = new Color[resolution * resolution];
            for (int index = 0; index < _scanPixels.Length; index++)
            {
                _scanPixels[index] = _settings.UnexploredColor;
            }
            _scanTexture.SetPixels(_scanPixels);
            _scanTexture.Apply(false, false);
        }

        private void EnsureStyles()
        {
            _minimapTitleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperLeft);
            _worldMapTitleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperCenter);
            _detailStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperLeft);
            _hintStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperRight);
            _markerStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _ringIconStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _landmarkIconStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _ringIconShadowStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);
            _landmarkIconShadowStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);

            float scale = GetMapScale();
            _minimapTitleStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.MinimapTitleFontSize * scale));
            _minimapTitleStyle.normal.textColor = _settings.TitleColor;
            _worldMapTitleStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.WorldMapTitleFontSize * scale));
            _worldMapTitleStyle.normal.textColor = _settings.TitleColor;
            _detailStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DetailFontSize * scale));
            _detailStyle.normal.textColor = _settings.DetailColor;
            _hintStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DetailFontSize * scale));
            _hintStyle.normal.textColor = _settings.DetailColor;
            _markerStyle.fontSize = Mathf.Max(
                8,
                Mathf.RoundToInt(_settings.DroneMarkerFontSize * scale));
            _markerStyle.normal.textColor = _settings.DroneMarkerColor;
            _landmarkIconStyle.normal.textColor = _settings.LandmarkIconColor;
            _ringIconShadowStyle.normal.textColor = _settings.IconShadowColor;
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
            SaveExploration();
            if (_scanTexture != null)
            {
                Destroy(_scanTexture);
            }
            if (_geoglyphMapMaterial != null)
            {
                Destroy(_geoglyphMapMaterial);
            }
            foreach (Texture2D texture in _geoglyphMapTextures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            _geoglyphMapTextures.Clear();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveExploration();
            }
        }

        private void OnApplicationQuit()
        {
            SaveExploration();
        }
    }
}
