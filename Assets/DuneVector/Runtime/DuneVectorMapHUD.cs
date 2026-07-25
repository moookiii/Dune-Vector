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
        public bool IsWorldMapVisible => _worldMapVisible;
        public bool IsMinimapVisible => _minimapVisible;

        private DroneCharacterController _drone;
        private DesertWorldStreamer _world;
        private BottomHudTuning _bottomHud;
        private MapHudTuning _settings;
        private Texture2D _scanTexture;
        private Color[] _scanPixels;
        private float[] _heightSamples;
        private readonly HashSet<long> _exploredCells = new HashSet<long>();
        private GUIStyle _minimapTitleStyle;
        private GUIStyle _worldMapTitleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _markerStyle;
        private bool _worldMapVisible;
        private bool _minimapVisible;
        private double _lastScanX = double.PositiveInfinity;
        private double _lastScanZ = double.PositiveInfinity;
        private double _lastRevealX = double.PositiveInfinity;
        private double _lastRevealZ = double.PositiveInfinity;
        private float _textureWorldSize;
        private float _nextScanTime;
        private float _nextExplorationSaveTime;
        private bool _explorationDirty;
        private bool _forceScanRefresh;

        private const int ExplorationFileMagic = 0x44564D50;
        private const int ExplorationFileVersion = 2;

        public void Initialize(
            DroneCharacterController drone,
            DesertWorldStreamer world,
            BottomHudTuning bottomHud,
            MapHudTuning settings)
        {
            _drone = drone;
            _world = world;
            _bottomHud = bottomHud;
            _settings = settings;
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

            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
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

            if (_worldMapVisible || _minimapVisible)
            {
                RefreshScan(_forceScanRefresh);
                _forceScanRefresh = false;
            }
        }

        private void OnGUI()
        {
            if (_settings == null ||
                !_settings.Enabled ||
                _drone == null ||
                _world == null ||
                _scanTexture == null ||
                DuneVectorCourierGame.IsGameplayHudSuppressed)
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
            float scanOffsetX = (float)(_lastScanX - currentCenter.X) * pixelsPerWorldUnit;
            float scanOffsetY = (float)(currentCenter.Z - _lastScanZ) * pixelsPerWorldUnit;
            Rect localScanRect = new Rect(
                ((mapRect.width - scanSize) * 0.5f) + scanOffsetX,
                ((mapRect.height - scanSize) * 0.5f) + scanOffsetY,
                scanSize,
                scanSize);

            GUI.BeginGroup(mapRect);
            GUI.DrawTexture(localScanRect, _scanTexture, ScaleMode.StretchToFill, false);
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
            double dx = center.X - _lastScanX;
            double dz = center.Z - _lastScanZ;
            double movementThreshold = _settings.ScanRefreshMovement;
            bool movedEnough = (dx * dx) + (dz * dz) >= movementThreshold * movementThreshold;
            if (!force && (Time.unscaledTime < _nextScanTime || !movedEnough))
            {
                return;
            }

            EnsureTexture();
            int resolution = _scanTexture.width;
            float radius = Mathf.Max(1f, _settings.DroneRevealRadius);
            float diameter = desiredWorldSize;

            for (int y = 0; y < resolution; y++)
            {
                float normalizedY = (y + 0.5f) / resolution;
                float offsetZ = (normalizedY - 0.5f) * diameter;
                for (int x = 0; x < resolution; x++)
                {
                    int index = (y * resolution) + x;
                    float normalizedX = (x + 0.5f) / resolution;
                    float offsetX = (normalizedX - 0.5f) * diameter;
                    double logicalX = center.X + offsetX;
                    double logicalZ = center.Z + offsetZ;
                    if (!IsExplored(logicalX, logicalZ))
                    {
                        _heightSamples[index] = float.NaN;
                        continue;
                    }

                    float height = (float)_world.HeightField.SampleHeight(
                        logicalX,
                        logicalZ);
                    _heightSamples[index] = height;
                }
            }

            float minimumHeight = Mathf.Min(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float maximumHeight = Mathf.Max(
                _settings.TerrainHeightMinimum,
                _settings.TerrainHeightMaximum);
            float heightRange = Mathf.Max(Mathf.Epsilon, maximumHeight - minimumHeight);
            float contourSpacing = Mathf.Max(0.01f, _settings.ContourSpacing);
            for (int y = 0; y < resolution; y++)
            {
                float normalizedY = (y + 0.5f) / resolution;
                float offsetZ = (normalizedY - 0.5f) * diameter;
                for (int x = 0; x < resolution; x++)
                {
                    int index = (y * resolution) + x;
                    float height = _heightSamples[index];
                    if (float.IsNaN(height))
                    {
                        _scanPixels[index] = _settings.UnexploredColor;
                        continue;
                    }

                    float normalizedX = (x + 0.5f) / resolution;
                    float offsetX = (normalizedX - 0.5f) * diameter;
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

            _scanTexture.SetPixels(_scanPixels);
            _scanTexture.Apply(false, false);
            _lastScanX = center.X;
            _lastScanZ = center.Z;
            _textureWorldSize = desiredWorldSize;
            _nextScanTime = Time.unscaledTime + _settings.ScanRefreshInterval;
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
            int resolution = Mathf.Clamp(_settings.ScanTextureResolution, 32, 256);
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
            _heightSamples = new float[resolution * resolution];
        }

        private void EnsureStyles()
        {
            _minimapTitleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperLeft);
            _worldMapTitleStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperCenter);
            _detailStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperLeft);
            _hintStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.UpperRight);
            _markerStyle ??= CreateStyle(FontStyle.Bold, TextAnchor.MiddleCenter);

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
