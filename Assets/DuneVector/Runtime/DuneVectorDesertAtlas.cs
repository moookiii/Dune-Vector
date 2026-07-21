using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorDesertAtlas : MonoBehaviour
    {
        private const string SaveFileName = "DuneVectorDesertAtlas.dat";

        [Serializable]
        private sealed class AtlasSaveData
        {
            public int Version = 2;
            public List<string> DiscoveredSiteIds = new List<string>();
            public bool CompletionRewardClaimed;
        }

        private sealed class SiteVisual
        {
            public string PersistentId;
            public Transform Root;
            public Transform Rings;
            public Vector3 CoreBaseScale;
            public Material SignalMaterial;
            public Color SignalColor;
            public Transform Beam;
            public Vector3 BeamBaseScale;
            public TraversalRing ChallengeFlightRing;
        }

        public bool IsUnlocked => _settings != null && _settings.Enabled && _progress != null &&
            _progress.CompletedDeliveries >= Mathf.Max(0, _settings.UnlockCompletedDeliveries);
        public int DiscoveredCount
        {
            get
            {
                if (_settings?.Sites == null) return 0;
                int count = 0;
                for (int i = 0; i < _settings.Sites.Count; i++)
                {
                    if (IsDiscovered(_settings.Sites[i])) count++;
                }
                return count;
            }
        }
        public int TotalSiteCount => _settings?.Sites?.Count ?? 0;

        private readonly HashSet<string> _discoveredIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, SiteVisual> _visuals = new Dictionary<string, SiteVisual>(StringComparer.Ordinal);
        private DroneCharacterController _player;
        private DroneHealth _health;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private DuneVectorCourierProgress _progress;
        private DuneVectorCourierGame _courierGame;
        private DesertAtlasTuning _settings;
        private Material _discoveredMaterial;
        private string _savePath;
        private DesertAtlasSiteDefinition _nearestSite;
        private float _nearestDistance;
        private float _scanProgress;
        private string _scanningSiteId;
        private float _orbitLastAngle;
        private float _orbitDirection;
        private bool _hasOrbitAngle;
        private bool _vectorPassArmed;
        private bool _completionRewardClaimed;
        private string _statusText;
        private float _statusUntil;
        private Vector2 _terminalScroll;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudBodyStyle;
        private GUIStyle _terminalTitleStyle;
        private GUIStyle _terminalBodyStyle;
        private GUIStyle _terminalMetaStyle;
        private Texture2D _whiteTexture;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DuneVectorCourierProgress progress,
            DuneVectorCourierGame courierGame,
            DesertAtlasTuning settings)
        {
            _player = player;
            _health = health;
            _world = world;
            _materials = materials;
            _wallet = wallet;
            _progress = progress;
            _courierGame = courierGame;
            _settings = settings ?? new DesertAtlasTuning();
            _settings.EnsureInitialized();
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            Load();
            _discoveredMaterial = CreateSignalMaterial(_materials.LandmarkMetal, _settings.DiscoveredColor);
            TryGrantCompletionReward(showStatus: false);
        }

        public bool IsDiscovered(DesertAtlasSiteDefinition site)
        {
            return site != null && !string.IsNullOrWhiteSpace(site.PersistentId) &&
                _discoveredIds.Contains(site.PersistentId);
        }

        public bool IsSiteAvailable(DesertAtlasSiteDefinition site)
        {
            return IsValidSite(site) && !IsDiscovered(site) &&
                DiscoveredCount >= Mathf.Max(0, site.RequiredDiscoveries);
        }

        public string GetTerminalPrompt()
        {
            if (IsUnlocked)
            {
                return _settings.TerminalNearbyPrompt;
            }
            int remaining = Mathf.Max(0, _settings.UnlockCompletedDeliveries - (_progress?.CompletedDeliveries ?? 0));
            return FormatDesignerText(_settings.LockedNearbyPromptFormat, remaining);
        }

        private void Update()
        {
            if (_settings == null || !_settings.Enabled || _player == null || _world == null)
            {
                return;
            }

            bool active = IsUnlocked && _courierGame != null && _courierGame.State == CourierRunState.FreeRoam;
            if (!active)
            {
                SetVisualsActive(false);
                ResetScan();
                return;
            }

            UpdateSites();
            UpdateScanning();
            AnimateVisuals();
        }

        private void UpdateSites()
        {
            _nearestSite = null;
            _nearestDistance = float.PositiveInfinity;
            Vector3 playerPosition = _player.WorldCenter;
            float spawnDistance = Mathf.Max(1f, _settings.SiteVisualSpawnDistance);
            float despawnDistance = Mathf.Max(spawnDistance, _settings.SiteVisualDespawnDistance);

            for (int i = 0; i < _settings.Sites.Count; i++)
            {
                DesertAtlasSiteDefinition site = _settings.Sites[i];
                if (!IsValidSite(site))
                {
                    continue;
                }
                Vector3 sitePosition = GetSiteLocalPosition(site);
                float distance = Vector3.Distance(playerPosition, sitePosition);
                bool discovered = IsDiscovered(site);
                bool available = IsSiteAvailable(site);
                if (available && distance < _nearestDistance)
                {
                    _nearestSite = site;
                    _nearestDistance = distance;
                }

                if ((available || discovered) && distance <= spawnDistance)
                {
                    SiteVisual visual = GetOrCreateVisual(site);
                    visual.Root.position = sitePosition;
                    visual.Root.gameObject.SetActive(true);
                }
                else if (distance >= despawnDistance && _visuals.TryGetValue(site.PersistentId, out SiteVisual visual))
                {
                    visual.Root.gameObject.SetActive(false);
                }
            }
        }

        private void UpdateScanning()
        {
            if (_nearestSite == null || !IsWithinChallengeActivation(_nearestSite))
            {
                if (!string.IsNullOrEmpty(_scanningSiteId) && _scanProgress > 0f)
                {
                    _statusText = _settings.ScanInterruptedText;
                    _statusUntil = Time.unscaledTime + _settings.ScanInterruptedStatusDuration;
                }
                DecayScan();
                return;
            }

            if (!string.Equals(_scanningSiteId, _nearestSite.PersistentId, StringComparison.Ordinal))
            {
                BeginChallenge(_nearestSite);
            }

            switch (_nearestSite.ChallengeType)
            {
                case DesertAtlasChallengeType.VectorPass:
                    UpdateVectorPassChallenge(_nearestSite);
                    break;
                case DesertAtlasChallengeType.OrbitTrace:
                    UpdateOrbitChallenge(_nearestSite);
                    break;
                case DesertAtlasChallengeType.AltitudeHold:
                    UpdateAltitudeHoldChallenge(_nearestSite);
                    break;
                default:
                    UpdateSignalLockChallenge(_nearestSite);
                    break;
            }
            if (_scanProgress >= 1f)
            {
                CompleteDiscovery(_nearestSite);
            }
        }

        private void BeginChallenge(DesertAtlasSiteDefinition site)
        {
            ResetScan();
            _scanningSiteId = site.PersistentId;
        }

        private void UpdateSignalLockChallenge(DesertAtlasSiteDefinition site)
        {
            Keyboard keyboard = Keyboard.current;
            bool held = keyboard != null && _settings.ScanKey != Key.None && keyboard[_settings.ScanKey].isPressed;
            if (!held || _nearestDistance > _settings.ScanRadius)
            {
                DecayScan();
                return;
            }
            _scanProgress = Mathf.Clamp01(_scanProgress +
                (Time.deltaTime / Mathf.Max(0.1f, site.RequiredAmount)));
        }

        private void UpdateOrbitChallenge(DesertAtlasSiteDefinition site)
        {
            Vector3 sitePosition = GetSiteLocalPosition(site);
            Vector3 offset = _player.WorldCenter - sitePosition;
            float planarRadius = new Vector2(offset.x, offset.z).magnitude;
            float heightError = Mathf.Abs(offset.y - site.TargetHeightAboveSignal);
            bool valid = _player.CurrentMode == DroneTraversalMode.Flight &&
                _player.Speed >= site.MinimumSpeed &&
                planarRadius >= _settings.OrbitMinimumRadius &&
                planarRadius <= _settings.OrbitMaximumRadius &&
                heightError <= site.HeightTolerance;
            float angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            if (!valid)
            {
                _hasOrbitAngle = false;
                _scanProgress = Mathf.Max(0f, _scanProgress - (_settings.OrbitProgressDecayPerSecond * Time.deltaTime));
                return;
            }
            if (!_hasOrbitAngle)
            {
                _orbitLastAngle = angle;
                _hasOrbitAngle = true;
                return;
            }
            float delta = Mathf.DeltaAngle(_orbitLastAngle, angle);
            _orbitLastAngle = angle;
            if (Mathf.Abs(delta) <= Mathf.Epsilon) return;
            if (Mathf.Approximately(_orbitDirection, 0f)) _orbitDirection = Mathf.Sign(delta);
            if (Mathf.Sign(delta) == Mathf.Sign(_orbitDirection))
            {
                _scanProgress = Mathf.Clamp01(_scanProgress +
                    (Mathf.Abs(delta) / Mathf.Max(1f, site.RequiredAmount)));
            }
            else
            {
                _scanProgress = Mathf.Max(0f, _scanProgress - (_settings.OrbitProgressDecayPerSecond * Time.deltaTime));
            }
        }

        private void UpdateAltitudeHoldChallenge(DesertAtlasSiteDefinition site)
        {
            Vector3 sitePosition = GetSiteLocalPosition(site);
            Vector3 offset = _player.WorldCenter - sitePosition;
            float planarRadius = new Vector2(offset.x, offset.z).magnitude;
            float heightError = offset.y - site.TargetHeightAboveSignal;
            Vector3 velocity = _player.Motor != null ? _player.Motor.Velocity : Vector3.zero;
            bool valid = _player.CurrentMode == DroneTraversalMode.Flight &&
                planarRadius <= _settings.AltitudeHoldHorizontalRadius &&
                Mathf.Abs(heightError) <= site.HeightTolerance &&
                Mathf.Abs(velocity.y) <= _settings.AltitudeHoldMaximumVerticalSpeed;
            if (valid)
            {
                _scanProgress = Mathf.Clamp01(_scanProgress +
                    (Time.deltaTime / Mathf.Max(0.1f, site.RequiredAmount)));
            }
            else
            {
                DecayScan();
            }
        }

        private void UpdateVectorPassChallenge(DesertAtlasSiteDefinition site)
        {
            float startRadius = Mathf.Max(_settings.VectorPassFinishRadius, _settings.VectorPassStartRadius);
            float finishRadius = Mathf.Min(startRadius, _settings.VectorPassFinishRadius);
            bool flightValid = _player.CurrentMode == DroneTraversalMode.Flight && _player.Speed >= site.MinimumSpeed;
            if (_nearestDistance > startRadius)
            {
                _vectorPassArmed = false;
                _scanProgress = 0f;
                return;
            }
            if (!_vectorPassArmed && flightValid && _nearestDistance > finishRadius)
            {
                _vectorPassArmed = true;
            }
            if (!_vectorPassArmed || !flightValid)
            {
                _scanProgress = Mathf.Max(0f, _scanProgress - (_settings.VectorPassProgressDecayPerSecond * Time.deltaTime));
                return;
            }
            _scanProgress = Mathf.Max(_scanProgress, Mathf.InverseLerp(startRadius, finishRadius, _nearestDistance));
            if (_nearestDistance <= finishRadius)
            {
                _scanProgress = 1f;
            }
        }

        private void DecayScan()
        {
            if (_scanProgress <= 0f)
            {
                _scanningSiteId = null;
                return;
            }
            _scanProgress = Mathf.Max(0f, _scanProgress - (_settings.ScanProgressDecayPerSecond * Time.deltaTime));
            if (_scanProgress <= 0f)
            {
                _scanningSiteId = null;
            }
        }

        private void CompleteDiscovery(DesertAtlasSiteDefinition site)
        {
            if (site == null || !_discoveredIds.Add(site.PersistentId))
            {
                ResetScan();
                return;
            }
            _wallet?.AddGold(Mathf.Max(0, site.GoldReward));
            _statusText = FormatDesignerText(_settings.DiscoveryStatusFormat, site.DisplayName, site.GoldReward);
            _statusUntil = Time.unscaledTime + _settings.DiscoveryStatusDuration;
            if (_visuals.TryGetValue(site.PersistentId, out SiteVisual visual))
            {
                ApplyMaterial(visual.Root, _discoveredMaterial);
                EmitCompletionBurst(visual, site.SignalColor);
            }
            bool completedAtlas = TryGrantCompletionReward(showStatus: true);
            if (!completedAtlas) Save();
            ResetScan();
        }

        private bool TryGrantCompletionReward(bool showStatus)
        {
            if (_completionRewardClaimed || TotalSiteCount <= 0 || DiscoveredCount < TotalSiteCount)
            {
                return false;
            }
            _completionRewardClaimed = true;
            int reward = Mathf.Max(0, _settings.AtlasCompletionGoldReward);
            _wallet?.AddGold(reward);
            Save();
            if (showStatus)
            {
                _statusText = FormatDesignerText(_settings.AtlasCompletionStatusFormat, reward);
                _statusUntil = Time.unscaledTime + _settings.DiscoveryStatusDuration;
            }
            return true;
        }

        private SiteVisual GetOrCreateVisual(DesertAtlasSiteDefinition site)
        {
            if (_visuals.TryGetValue(site.PersistentId, out SiteVisual existing))
            {
                return existing;
            }

            Transform root = new GameObject($"Atlas Signal - {site.DisplayName}").transform;
            root.SetParent(transform, true);
            Material signalMaterial = CreateSignalMaterial(_materials.LandmarkMetal, site.SignalColor);
            Material material = IsDiscovered(site) ? _discoveredMaterial : signalMaterial;
            CreatePart(PrimitiveType.Cylinder, "Signal Base", root, Vector3.up * (_settings.BaseHeight * 0.5f),
                new Vector3(_settings.BaseRadius * 2f, _settings.BaseHeight * 0.5f, _settings.BaseRadius * 2f), material);
            Transform core = CreatePart(PrimitiveType.Sphere, "Signal Core", root, Vector3.up * _settings.CoreHeight,
                Vector3.one * (_settings.CoreRadius * 2f), material);
            CreatePart(PrimitiveType.Cylinder, "Signal Mast", root, Vector3.up * (_settings.CoreHeight * 0.5f),
                new Vector3(_settings.CoreRadius * 0.4f, _settings.CoreHeight * 0.5f, _settings.CoreRadius * 0.4f), material);
            Transform beam = CreatePart(PrimitiveType.Cylinder, "Signal Sky Beam", root,
                Vector3.up * (_settings.SignalBeamHeight * 0.5f),
                new Vector3(_settings.SignalBeamRadius * 2f, _settings.SignalBeamHeight * 0.5f,
                    _settings.SignalBeamRadius * 2f), material);
            Transform rings = new GameObject("Signal Rings").transform;
            rings.SetParent(root, false);
            for (int ring = 0; ring < Mathf.Max(1, _settings.RingCount); ring++)
            {
                float y = _settings.CoreHeight + ((ring - ((_settings.RingCount - 1) * 0.5f)) * _settings.RingHeightSpacing);
                BuildSegmentedRing(rings, y, _settings.RingRadius + (ring * _settings.RingSegmentWidth), material, ring);
            }
            SiteVisual created = new SiteVisual
            {
                PersistentId = site.PersistentId,
                Root = root,
                Rings = rings,
                CoreBaseScale = core.localScale,
                SignalMaterial = signalMaterial,
                SignalColor = site.SignalColor,
                Beam = beam,
                BeamBaseScale = beam.localScale,
            };
            if (_settings.SpawnChallengeFlightRing &&
                site.ChallengeType != DesertAtlasChallengeType.SignalLock && _world.Rings != null)
            {
                GameObject ringObject = new GameObject("Atlas Challenge Flight Ring");
                ringObject.transform.SetParent(root, false);
                ringObject.transform.localPosition = new Vector3(
                    0f,
                    _settings.ChallengeFlightRingHeight,
                    -_settings.ChallengeFlightRingDistance);
                ringObject.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                TraversalRing flightRing = ringObject.AddComponent<TraversalRing>();
                flightRing.Initialize(
                    TraversalRingType.Flight,
                    _player,
                    _health,
                    _materials,
                    _settings.ChallengeFlightRingRadius,
                    _world.Rings,
                    $"atlas:{site.PersistentId}:flight");
                created.ChallengeFlightRing = flightRing;
            }
            _visuals.Add(site.PersistentId, created);
            return created;
        }

        private void BuildSegmentedRing(Transform parent, float height, float radius, Material material, int ringIndex)
        {
            int segments = Mathf.Max(3, _settings.RingSegmentCount);
            for (int i = 0; i < segments; i++)
            {
                float angle = (360f / segments) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                CreatePart(PrimitiveType.Cube, $"Ring {ringIndex + 1} Segment {i + 1}", parent,
                    (direction * radius) + (Vector3.up * height),
                    new Vector3(_settings.RingSegmentWidth, _settings.RingSegmentWidth, _settings.RingSegmentDepth),
                    material, Quaternion.Euler(0f, angle, 0f));
            }
        }

        private static Transform CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Quaternion? localRotation = null)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation ?? Quaternion.identity;
            part.transform.localScale = localScale;
            if (part.TryGetComponent(out Collider collider))
            {
                Destroy(collider);
            }
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }

        private void AnimateVisuals()
        {
            float pulse = 1f + (Mathf.Sin(Time.time * _settings.PulseSpeed) * _settings.PulseScaleAmount);
            float beamPulse = 1f + (Mathf.Sin(Time.time * _settings.PulseSpeed) * _settings.SignalBeamPulseAmount);
            foreach (SiteVisual visual in _visuals.Values)
            {
                if (!visual.Root.gameObject.activeSelf)
                {
                    continue;
                }
                bool challengeActive = string.Equals(_scanningSiteId, visual.PersistentId, StringComparison.Ordinal);
                float rotationMultiplier = challengeActive ? _settings.ActiveChallengeRotationMultiplier : 1f;
                visual.Rings.Rotate(0f, _settings.RingRotationSpeed * rotationMultiplier * Time.deltaTime, 0f, Space.Self);
                Transform core = visual.Root.Find("Signal Core");
                if (core != null)
                {
                    core.localScale = visual.CoreBaseScale * pulse;
                }
                if (visual.Beam != null)
                {
                    visual.Beam.localScale = new Vector3(
                        visual.BeamBaseScale.x * beamPulse,
                        visual.BeamBaseScale.y,
                        visual.BeamBaseScale.z * beamPulse);
                }
                if (visual.SignalMaterial != null && visual.SignalMaterial.HasProperty("_EmissiveColor"))
                {
                    float challengeEmission = challengeActive
                        ? Mathf.Lerp(1f, _settings.ActiveChallengeEmissionMultiplier, _scanProgress)
                        : 1f;
                    visual.SignalMaterial.SetColor(
                        "_EmissiveColor",
                        visual.SignalColor * (_settings.SignalEmissionMultiplier * challengeEmission));
                }
            }
        }

        private void EmitCompletionBurst(SiteVisual visual, Color color)
        {
            if (visual?.Root == null || _settings.CompletionBurstParticleCount <= 0)
            {
                return;
            }
            GameObject burstObject = new GameObject("Atlas Discovery Burst");
            burstObject.transform.position = visual.Root.position + (Vector3.up * _settings.CoreHeight);
            ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.1f, _settings.CompletionBurstLifetime);
            main.startLifetime = _settings.CompletionBurstLifetime;
            main.startSpeed = _settings.CompletionBurstSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.CompletionBurstMinimumSize,
                Mathf.Max(_settings.CompletionBurstMinimumSize, _settings.CompletionBurstMaximumSize));
            main.startColor = color;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _settings.CoreRadius;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = visual.SignalMaterial;
            particles.Emit(_settings.CompletionBurstParticleCount);
            particles.Play();
        }

        private float GetChallengeActivationRadius(DesertAtlasSiteDefinition site)
        {
            if (site == null) return _settings.ScanRadius;
            return site.ChallengeType switch
            {
                DesertAtlasChallengeType.VectorPass => _settings.VectorPassStartRadius,
                DesertAtlasChallengeType.OrbitTrace => _settings.OrbitMaximumRadius,
                DesertAtlasChallengeType.AltitudeHold => _settings.AltitudeHoldHorizontalRadius,
                _ => _settings.ScanRadius,
            };
        }

        private bool IsWithinChallengeActivation(DesertAtlasSiteDefinition site)
        {
            if (site == null) return false;
            if (site.ChallengeType == DesertAtlasChallengeType.OrbitTrace ||
                site.ChallengeType == DesertAtlasChallengeType.AltitudeHold)
            {
                Vector3 offset = _player.WorldCenter - GetSiteLocalPosition(site);
                float planarDistance = new Vector2(offset.x, offset.z).magnitude;
                return planarDistance <= GetChallengeActivationRadius(site);
            }
            return _nearestDistance <= GetChallengeActivationRadius(site);
        }

        private string GetChallengeProgressText(DesertAtlasSiteDefinition site)
        {
            if (site == null) return string.Empty;
            switch (site.ChallengeType)
            {
                case DesertAtlasChallengeType.VectorPass:
                    return FormatDesignerText(
                        _settings.VectorPassProgressFormat,
                        _scanProgress * 100f,
                        _player.Speed);
                case DesertAtlasChallengeType.OrbitTrace:
                    return FormatDesignerText(
                        _settings.OrbitProgressFormat,
                        _scanProgress * site.RequiredAmount,
                        site.RequiredAmount);
                case DesertAtlasChallengeType.AltitudeHold:
                    float heightError = _player.WorldCenter.y - GetSiteLocalPosition(site).y - site.TargetHeightAboveSignal;
                    return FormatDesignerText(
                        _settings.AltitudeProgressFormat,
                        _scanProgress * site.RequiredAmount,
                        site.RequiredAmount,
                        heightError);
                default:
                    return FormatDesignerText(_settings.SignalLockProgressFormat, _scanProgress * 100f);
            }
        }

        private Vector3 GetSiteLocalPosition(DesertAtlasSiteDefinition site)
        {
            float height = (float)_world.HeightField.SampleHeight(site.WorldPosition.x, site.WorldPosition.y) +
                _settings.HeightAboveTerrain;
            return _world.LogicalToLocal(site.WorldPosition.x, height, site.WorldPosition.y);
        }

        private static bool IsValidSite(DesertAtlasSiteDefinition site)
        {
            return site != null && !string.IsNullOrWhiteSpace(site.PersistentId);
        }

        public void DrawTerminal()
        {
            EnsureGui();
            GUI.depth = -1150;
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), _settings.TerminalBackdropColor);
            float scale = Mathf.Clamp(
                Mathf.Min(Screen.width / _settings.TerminalReferenceWidth, Screen.height / _settings.TerminalReferenceHeight),
                Mathf.Min(_settings.TerminalMinimumScale, _settings.TerminalMaximumScale),
                Mathf.Max(_settings.TerminalMinimumScale, _settings.TerminalMaximumScale));
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float virtualWidth = Screen.width / scale;
            float virtualHeight = Screen.height / scale;
            float panelWidth = Mathf.Min(_settings.TerminalPanelWidth, virtualWidth - (_settings.TerminalScreenMargin * 2f));
            float panelHeight = Mathf.Min(_settings.TerminalPanelHeight, virtualHeight - (_settings.TerminalScreenMargin * 2f));
            Rect panel = new Rect((virtualWidth - panelWidth) * 0.5f, (virtualHeight - panelHeight) * 0.5f, panelWidth, panelHeight);
            DrawRect(panel, _settings.TerminalPanelColor);
            DrawBorder(panel, _settings.TerminalBorderColor, _settings.TerminalBorderThickness);
            DrawRect(new Rect(panel.x, panel.y, panel.width, _settings.TerminalAccentBarHeight), _settings.TerminalAccentColor);
            float padding = _settings.TerminalPadding;
            GUI.Label(new Rect(panel.x + padding, panel.y + _settings.TerminalTitleTop,
                panel.width - (padding * 2f), _settings.TerminalTitleHeight), _settings.TerminalTitle, _terminalTitleStyle);
            GUI.Label(new Rect(panel.xMax - padding - _settings.TerminalCloseWidth, panel.y + _settings.TerminalTitleTop,
                _settings.TerminalCloseWidth, _settings.TerminalCloseHeight), _settings.TerminalClosePrompt, _terminalMetaStyle);

            if (!IsUnlocked)
            {
                int remaining = Mathf.Max(0, _settings.UnlockCompletedDeliveries - (_progress?.CompletedDeliveries ?? 0));
                GUI.Label(new Rect(panel.x + padding, panel.y + _settings.TerminalHeaderHeight,
                    panel.width - (padding * 2f), panel.height - _settings.TerminalHeaderHeight),
                    $"{_settings.TerminalLockedTitle}\n\n{FormatDesignerText(_settings.TerminalLockedBodyFormat, remaining, remaining == 1 ? string.Empty : "s")}",
                    _terminalBodyStyle);
                GUI.matrix = previousMatrix;
                return;
            }

            GUI.Label(new Rect(panel.x + padding, panel.y + _settings.TerminalProgressTop,
                panel.width - (padding * 2f), _settings.TerminalProgressHeight),
                FormatDesignerText(_settings.TerminalProgressFormat, DiscoveredCount, TotalSiteCount), _terminalMetaStyle);
            Rect viewport = new Rect(panel.x + padding, panel.y + _settings.TerminalHeaderHeight,
                panel.width - (padding * 2f), panel.height - _settings.TerminalHeaderHeight - _settings.TerminalFooterHeight);
            float contentHeight = Mathf.Max(viewport.height, _settings.Sites.Count * (_settings.TerminalEntryHeight + _settings.TerminalEntryGap));
            _terminalScroll = GUI.BeginScrollView(viewport, _terminalScroll, new Rect(0f, 0f, viewport.width - 18f, contentHeight));
            for (int i = 0; i < _settings.Sites.Count; i++)
            {
                DesertAtlasSiteDefinition site = _settings.Sites[i];
                Rect entry = new Rect(0f, i * (_settings.TerminalEntryHeight + _settings.TerminalEntryGap), viewport.width - 24f, _settings.TerminalEntryHeight);
                bool discovered = IsDiscovered(site);
                bool available = IsSiteAvailable(site);
                Color entryColor = _settings.TerminalEntryColor;
                if (discovered)
                {
                    entryColor = _settings.TerminalDiscoveredEntryColor;
                }
                else if (available)
                {
                    float pulse = (Mathf.Sin(Time.unscaledTime * _settings.TerminalAvailablePulseSpeed) + 1f) * 0.5f;
                    entryColor = Color.Lerp(
                        _settings.TerminalAvailableEntryColor,
                        _settings.TerminalAccentColor,
                        pulse * _settings.TerminalAvailablePulseAmount);
                }
                DrawRect(entry, entryColor);
                string title = discovered ? site.DisplayName : FormatDesignerText(_settings.TerminalUnknownSiteFormat, i + 1);
                string body;
                if (discovered)
                {
                    body = FormatDesignerText(
                        _settings.TerminalDiscoveredEntryFormat,
                        FormatDesignerText(_settings.TerminalDiscoveredStatus, site.GoldReward),
                        site.Description);
                }
                else if (available)
                {
                    body = FormatDesignerText(
                        _settings.TerminalChallengeFormat,
                        site.IsFinalSignal ? _settings.TerminalFinalSignalStatus : _settings.TerminalAvailableStatus,
                        site.ChallengeInstruction);
                }
                else
                {
                    int remaining = Mathf.Max(0, site.RequiredDiscoveries - DiscoveredCount);
                    body = FormatDesignerText(
                        _settings.TerminalStagedLockFormat,
                        remaining,
                        remaining == 1 ? string.Empty : "S");
                }
                float entryPadding = _settings.TerminalEntryPadding;
                GUI.Label(new Rect(entry.x + entryPadding, entry.y + _settings.TerminalEntryTitleTop,
                    entry.width - (entryPadding * 2f), _settings.TerminalEntryTitleHeight), title, _terminalBodyStyle);
                GUI.Label(new Rect(entry.x + entryPadding, entry.y + _settings.TerminalEntryDescriptionTop,
                    entry.width - (entryPadding * 2f), entry.height - _settings.TerminalEntryDescriptionTop), body, _terminalMetaStyle);
            }
            GUI.EndScrollView();
            GUI.matrix = previousMatrix;
        }

        private void OnGUI()
        {
            if (_settings == null || !IsUnlocked || _courierGame == null ||
                _courierGame.State != CourierRunState.FreeRoam || _courierGame.IsTerminalOpen)
            {
                return;
            }
            EnsureGui();
            Rect panel = new Rect(
                Screen.width - _settings.HudRightMargin - _settings.HudWidth,
                _settings.HudTop,
                _settings.HudWidth,
                _settings.HudHeight);
            DrawRect(panel, _settings.HudPanelColor);
            float padding = _settings.HudPadding;
            GUI.Label(new Rect(panel.x + padding, panel.y + _settings.HudTitleTop,
                panel.width - (padding * 2f), _settings.HudTitleHeight),
                FormatDesignerText(_settings.HudTitleFormat, DiscoveredCount, TotalSiteCount), _hudTitleStyle);
            string body;
            if (_nearestSite == null)
            {
                body = DiscoveredCount >= TotalSiteCount
                    ? _settings.HudAllDiscoveredText
                    : _settings.HudSignalsLockedText;
            }
            else if (IsWithinChallengeActivation(_nearestSite))
            {
                body = FormatDesignerText(
                    _settings.HudChallengeFormat,
                    _nearestSite.ChallengeInstruction,
                    GetChallengeProgressText(_nearestSite));
            }
            else
            {
                body = FormatDesignerText(_settings.HudNearestSignalFormat, _nearestDistance, GetBearingText(_nearestSite));
            }
            GUI.Label(new Rect(panel.x + padding, panel.y + _settings.HudBodyTop,
                panel.width - (padding * 2f), _settings.HudBodyHeight), body, _hudBodyStyle);
            if (_scanProgress > 0f ||
                (_nearestSite != null && IsWithinChallengeActivation(_nearestSite)))
            {
                Rect bar = new Rect(panel.x + padding, panel.yMax - padding - _settings.ScanBarHeight,
                    panel.width - (padding * 2f), _settings.ScanBarHeight);
                DrawRect(bar, _settings.ScanBarBackgroundColor);
                DrawRect(new Rect(bar.x, bar.y, bar.width * _scanProgress, bar.height), _settings.HudAccentColor);
            }
            if (Time.unscaledTime < _statusUntil)
            {
                GUI.Label(new Rect(0f, Screen.height * _settings.StatusVerticalFraction, Screen.width,
                    _settings.StatusHeight), _statusText, _hudTitleStyle);
            }
        }

        private string GetBearingText(DesertAtlasSiteDefinition site)
        {
            Vector3 direction = GetSiteLocalPosition(site) - _player.WorldCenter;
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = Mathf.RoundToInt(angle / 45f) % cardinals.Length;
            return cardinals[index];
        }

        private void EnsureGui()
        {
            if (_whiteTexture == null)
            {
                _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _whiteTexture.SetPixel(0, 0, Color.white);
                _whiteTexture.Apply(false, true);
            }
            _hudTitleStyle ??= CreateStyle(_settings.HudTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.HudTextColor);
            _hudBodyStyle ??= CreateStyle(_settings.HudBodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, _settings.HudMutedColor);
            _terminalTitleStyle ??= CreateStyle(_settings.TerminalTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.TerminalTextColor);
            _terminalBodyStyle ??= CreateStyle(_settings.TerminalBodyFontSize, FontStyle.Bold, TextAnchor.UpperLeft, _settings.TerminalTextColor);
            _terminalMetaStyle ??= CreateStyle(_settings.TerminalMetaFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _settings.TerminalMutedColor);
        }

        private static GUIStyle CreateStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = true,
            };
            style.normal.textColor = color;
            return style;
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _whiteTexture);
            GUI.color = previous;
        }

        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private Material CreateSignalMaterial(Material source, Color color)
        {
            Material material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color * _settings.SignalBaseColorMultiplier);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", color * _settings.SignalEmissionMultiplier);
            return material;
        }

        private static void ApplyMaterial(Transform root, Material material)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        private void SetVisualsActive(bool active)
        {
            foreach (SiteVisual visual in _visuals.Values)
            {
                visual.Root.gameObject.SetActive(active);
            }
        }

        private void ResetScan()
        {
            _scanProgress = 0f;
            _scanningSiteId = null;
            _orbitDirection = 0f;
            _orbitLastAngle = 0f;
            _hasOrbitAngle = false;
            _vectorPassArmed = false;
        }

        private void Load()
        {
            _discoveredIds.Clear();
            if (!File.Exists(_savePath))
            {
                Save();
                return;
            }
            try
            {
                AtlasSaveData data = JsonUtility.FromJson<AtlasSaveData>(File.ReadAllText(_savePath));
                if (data?.DiscoveredSiteIds == null) return;
                _completionRewardClaimed = data.CompletionRewardClaimed;
                for (int i = 0; i < data.DiscoveredSiteIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(data.DiscoveredSiteIds[i]))
                    {
                        _discoveredIds.Add(data.DiscoveredSiteIds[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load Desert Atlas save '{_savePath}': {exception.Message}", this);
            }
        }

        private void Save()
        {
            try
            {
                AtlasSaveData data = new AtlasSaveData
                {
                    DiscoveredSiteIds = new List<string>(_discoveredIds),
                    CompletionRewardClaimed = _completionRewardClaimed,
                };
                File.WriteAllText(_savePath, JsonUtility.ToJson(data));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save Desert Atlas progress to '{_savePath}': {exception.Message}", this);
            }
        }

        private static string FormatDesignerText(string format, params object[] arguments)
        {
            try
            {
                return string.Format(format ?? string.Empty, arguments);
            }
            catch (FormatException)
            {
                return format ?? string.Empty;
            }
        }

        private void OnDestroy()
        {
            foreach (SiteVisual visual in _visuals.Values)
            {
                if (visual.SignalMaterial != null) Destroy(visual.SignalMaterial);
            }
            if (_discoveredMaterial != null) Destroy(_discoveredMaterial);
            if (_whiteTexture != null) Destroy(_whiteTexture);
        }
    }
}
