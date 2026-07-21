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
            public int Version = 3;
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
            public ParticleSystem AmbientParticles;
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
        private CompassHudTuning _compassSettings;
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
        private Vector3 _vectorPassPreviousPosition;
        private bool _hasVectorPassPreviousPosition;
        private int _relayStage;
        private float _relayStageProgress;
        private float _challengeStartedAt;
        private bool _completionRewardClaimed;
        private string _statusText;
        private float _statusUntil;
        private float _discoveryPresentationStartedAt;
        private float _discoveryPresentationUntil;
        private Vector2 _terminalScroll;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudBodyStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _terminalTitleStyle;
        private GUIStyle _terminalBodyStyle;
        private GUIStyle _terminalMetaStyle;
        private GUIStyle _discoveryBannerStyle;
        private Texture2D _whiteTexture;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DuneVectorCourierProgress progress,
            DuneVectorCourierGame courierGame,
            DesertAtlasTuning settings,
            CompassHudTuning compassSettings)
        {
            _player = player;
            _health = health;
            _world = world;
            _materials = materials;
            _wallet = wallet;
            _progress = progress;
            _courierGame = courierGame;
            _settings = settings ?? new DesertAtlasTuning();
            _compassSettings = compassSettings ?? new CompassHudTuning();
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
                case DesertAtlasChallengeType.RelaySequence:
                    UpdateRelaySequenceChallenge(_nearestSite);
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
            _challengeStartedAt = Time.unscaledTime;
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
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 targetPosition = GetVectorPassTargetPosition(site);
            float targetDistance = Vector3.Distance(playerPosition, targetPosition);
            bool flightValid = _player.CurrentMode == DroneTraversalMode.Flight && _player.Speed >= site.MinimumSpeed;
            if (targetDistance > startRadius)
            {
                _vectorPassArmed = false;
                _scanProgress = 0f;
                _hasVectorPassPreviousPosition = false;
                return;
            }
            if (!_hasVectorPassPreviousPosition)
            {
                _vectorPassPreviousPosition = playerPosition;
                _hasVectorPassPreviousPosition = true;
            }
            if (!_vectorPassArmed && flightValid)
            {
                _vectorPassArmed = true;
            }
            if (!_vectorPassArmed || !flightValid)
            {
                _scanProgress = Mathf.Max(0f, _scanProgress - (_settings.VectorPassProgressDecayPerSecond * Time.deltaTime));
                _vectorPassPreviousPosition = playerPosition;
                return;
            }
            _scanProgress = Mathf.Max(_scanProgress, Mathf.InverseLerp(startRadius, finishRadius, targetDistance));
            Vector3 segment = playerPosition - _vectorPassPreviousPosition;
            float segmentLengthSquared = segment.sqrMagnitude;
            bool crossedCore = targetDistance <= finishRadius;
            if (!crossedCore && segmentLengthSquared > Mathf.Epsilon)
            {
                float interpolation = Mathf.Clamp01(
                    Vector3.Dot(targetPosition - _vectorPassPreviousPosition, segment) / segmentLengthSquared);
                Vector3 closestPoint = _vectorPassPreviousPosition + (segment * interpolation);
                crossedCore = Vector3.Distance(closestPoint, targetPosition) <= finishRadius;
            }
            _vectorPassPreviousPosition = playerPosition;
            if (crossedCore)
            {
                _scanProgress = 1f;
            }
        }

        private void UpdateRelaySequenceChallenge(DesertAtlasSiteDefinition site)
        {
            switch (_relayStage)
            {
                case 0:
                    UpdateRelaySynchronization(site);
                    break;
                case 1:
                    UpdateRelayVectorPass(site);
                    break;
                default:
                    UpdateRelayAltitudeHold(site);
                    break;
            }

            _scanProgress = Mathf.Clamp01((_relayStage + _relayStageProgress) / 3f);
        }

        private void UpdateRelaySynchronization(DesertAtlasSiteDefinition site)
        {
            Keyboard keyboard = Keyboard.current;
            bool held = keyboard != null && _settings.ScanKey != Key.None && keyboard[_settings.ScanKey].isPressed;
            if (!held || _nearestDistance > _settings.ScanRadius)
            {
                _relayStageProgress = Mathf.Max(
                    0f,
                    _relayStageProgress - (_settings.ScanProgressDecayPerSecond * Time.deltaTime));
                return;
            }

            _relayStageProgress = Mathf.Clamp01(
                _relayStageProgress + (Time.deltaTime / Mathf.Max(0.1f, site.RequiredAmount)));
            if (_relayStageProgress >= 1f)
            {
                AdvanceRelayStage();
            }
        }

        private void UpdateRelayVectorPass(DesertAtlasSiteDefinition site)
        {
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 targetPosition = GetVectorPassTargetPosition(site);
            float targetDistance = Vector3.Distance(playerPosition, targetPosition);
            bool flightValid = _player.CurrentMode == DroneTraversalMode.Flight && _player.Speed >= site.MinimumSpeed;
            if (!_hasVectorPassPreviousPosition)
            {
                _vectorPassPreviousPosition = playerPosition;
                _hasVectorPassPreviousPosition = true;
            }
            if (!_vectorPassArmed && flightValid && targetDistance >= _settings.RelayVectorArmRadius)
            {
                _vectorPassArmed = true;
            }
            if (!_vectorPassArmed || !flightValid)
            {
                _relayStageProgress = 0f;
                _vectorPassPreviousPosition = playerPosition;
                return;
            }

            float finishRadius = _settings.VectorPassFinishRadius;
            _relayStageProgress = Mathf.Max(
                _relayStageProgress,
                Mathf.InverseLerp(_settings.RelayVectorArmRadius, finishRadius, targetDistance));
            if (DidCrossTarget(_vectorPassPreviousPosition, playerPosition, targetPosition, finishRadius))
            {
                AdvanceRelayStage();
            }
            _vectorPassPreviousPosition = playerPosition;
        }

        private void UpdateRelayAltitudeHold(DesertAtlasSiteDefinition site)
        {
            Vector3 offset = _player.WorldCenter - GetSiteLocalPosition(site);
            float planarRadius = new Vector2(offset.x, offset.z).magnitude;
            float heightError = offset.y - site.TargetHeightAboveSignal;
            Vector3 velocity = _player.Motor != null ? _player.Motor.Velocity : Vector3.zero;
            bool valid = _player.CurrentMode == DroneTraversalMode.Flight &&
                planarRadius <= _settings.AltitudeHoldHorizontalRadius &&
                Mathf.Abs(heightError) <= site.HeightTolerance &&
                Mathf.Abs(velocity.y) <= _settings.AltitudeHoldMaximumVerticalSpeed;
            if (valid)
            {
                _relayStageProgress = Mathf.Clamp01(
                    _relayStageProgress + (Time.deltaTime / Mathf.Max(0.1f, site.SecondaryRequiredAmount)));
            }
            else
            {
                _relayStageProgress = Mathf.Max(
                    0f,
                    _relayStageProgress - (_settings.ScanProgressDecayPerSecond * Time.deltaTime));
            }
        }

        private void AdvanceRelayStage()
        {
            _relayStage++;
            _relayStageProgress = 0f;
            _vectorPassArmed = false;
            _hasVectorPassPreviousPosition = false;
            _statusText = FormatDesignerText(_settings.RelayStageAdvancedFormat, _relayStage);
            _statusUntil = Time.unscaledTime + _settings.ScanInterruptedStatusDuration;
        }

        private static bool DidCrossTarget(Vector3 start, Vector3 end, Vector3 target, float radius)
        {
            if (Vector3.Distance(end, target) <= radius)
            {
                return true;
            }
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return false;
            }
            float interpolation = Mathf.Clamp01(Vector3.Dot(target - start, segment) / lengthSquared);
            return Vector3.Distance(start + (segment * interpolation), target) <= radius;
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
            int baseReward = Mathf.Max(0, site.GoldReward);
            bool earnedMasteryBonus = site.BonusTimeLimit > 0f &&
                Time.unscaledTime - _challengeStartedAt <= site.BonusTimeLimit;
            int masteryReward = earnedMasteryBonus ? Mathf.Max(0, site.BonusGoldReward) : 0;
            _wallet?.AddGold(baseReward + masteryReward);
            _statusText = FormatDesignerText(
                _settings.DiscoveryStatusFormat,
                site.DisplayName,
                baseReward,
                masteryReward);
            _statusUntil = Time.unscaledTime + _settings.DiscoveryStatusDuration;
            _discoveryPresentationStartedAt = Time.unscaledTime;
            _discoveryPresentationUntil = Time.unscaledTime + _settings.DiscoveryPresentationDuration;
            if (_visuals.TryGetValue(site.PersistentId, out SiteVisual visual))
            {
                ApplyMaterial(visual.Root, _discoveredMaterial);
                EmitCompletionBurst(visual, site.SignalColor);
            }
            int milestoneInterval = Mathf.Max(1, _settings.MilestoneInterval);
            if (DiscoveredCount < TotalSiteCount && DiscoveredCount % milestoneInterval == 0)
            {
                int milestoneReward = Mathf.Max(0, _settings.MilestoneGoldReward);
                _wallet?.AddGold(milestoneReward);
                _statusText = FormatDesignerText(
                    _settings.MilestoneStatusFormat,
                    _statusText,
                    DiscoveredCount,
                    TotalSiteCount,
                    milestoneReward);
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
            created.AmbientParticles = CreateAmbientParticles(root, signalMaterial, site.SignalColor);
            if (_settings.SpawnChallengeFlightRing &&
                site.ChallengeType != DesertAtlasChallengeType.SignalLock && _world.Rings != null)
            {
                GameObject ringObject = new GameObject("Atlas Challenge Flight Ring");
                ringObject.transform.SetParent(root, false);
                ringObject.transform.localPosition = new Vector3(
                    0f,
                    site.ChallengeType == DesertAtlasChallengeType.VectorPass ||
                    site.ChallengeType == DesertAtlasChallengeType.RelaySequence
                        ? _settings.CoreHeight
                        : Mathf.Max(_settings.ChallengeFlightRingHeight, site.TargetHeightAboveSignal),
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

        private ParticleSystem CreateAmbientParticles(Transform parent, Material material, Color color)
        {
            GameObject particleObject = new GameObject("Signal Ambient Particles");
            particleObject.transform.SetParent(parent, false);
            particleObject.transform.localPosition = Vector3.up * _settings.CoreHeight;
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = _settings.AmbientParticleLifetime;
            main.startSpeed = _settings.AmbientParticleSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.AmbientParticleMinimumSize,
                Mathf.Max(_settings.AmbientParticleMinimumSize, _settings.AmbientParticleMaximumSize));
            main.startColor = color;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = _settings.AmbientParticleRate;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _settings.AmbientParticleRadius;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            particles.Play();
            return particles;
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
                if (visual.AmbientParticles != null)
                {
                    ParticleSystem.EmissionModule emission = visual.AmbientParticles.emission;
                    emission.rateOverTime = _settings.AmbientParticleRate *
                        (challengeActive ? _settings.ActiveChallengeParticleMultiplier : 1f);
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
                DesertAtlasChallengeType.RelaySequence => _settings.VectorPassStartRadius,
                _ => _settings.ScanRadius,
            };
        }

        private bool IsWithinChallengeActivation(DesertAtlasSiteDefinition site)
        {
            if (site == null) return false;
            if (site.ChallengeType == DesertAtlasChallengeType.VectorPass ||
                site.ChallengeType == DesertAtlasChallengeType.RelaySequence)
            {
                return Vector3.Distance(_player.WorldCenter, GetVectorPassTargetPosition(site)) <=
                    GetChallengeActivationRadius(site);
            }
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
                    if (_player.CurrentMode != DroneTraversalMode.Flight)
                    {
                        return WithTimedBonus(site, _settings.VectorPassNeedFlightText);
                    }
                    if (_player.Speed < site.MinimumSpeed)
                    {
                        return WithTimedBonus(site, FormatDesignerText(
                            _settings.VectorPassNeedSpeedFormat,
                            _player.Speed,
                            site.MinimumSpeed));
                    }
                    return WithTimedBonus(site, FormatDesignerText(
                        _settings.VectorPassProgressFormat,
                        _scanProgress * 100f,
                        _player.Speed));
                case DesertAtlasChallengeType.OrbitTrace:
                    return WithTimedBonus(site, FormatDesignerText(
                        _settings.OrbitProgressFormat,
                        _scanProgress * site.RequiredAmount,
                        site.RequiredAmount));
                case DesertAtlasChallengeType.AltitudeHold:
                    float heightError = _player.WorldCenter.y - GetSiteLocalPosition(site).y - site.TargetHeightAboveSignal;
                    return WithTimedBonus(site, FormatDesignerText(
                        _settings.AltitudeProgressFormat,
                        _scanProgress * site.RequiredAmount,
                        site.RequiredAmount,
                        heightError));
                case DesertAtlasChallengeType.RelaySequence:
                    return WithTimedBonus(site, GetRelayProgressText(site));
                default:
                    return WithTimedBonus(
                        site,
                        FormatDesignerText(_settings.SignalLockProgressFormat, _scanProgress * 100f));
            }
        }

        private string GetRelayProgressText(DesertAtlasSiteDefinition site)
        {
            return _relayStage switch
            {
                0 when _nearestDistance > _settings.ScanRadius => FormatDesignerText(
                    _settings.RelayStageOneApproachFormat,
                    _nearestDistance),
                0 => FormatDesignerText(_settings.RelayStageOneProgressFormat, _relayStageProgress * 100f),
                1 when !_vectorPassArmed => _settings.RelayStageTwoNeedArmText,
                1 => FormatDesignerText(_settings.RelayStageTwoProgressFormat, _relayStageProgress * 100f),
                _ => FormatDesignerText(
                    _settings.RelayStageThreeProgressFormat,
                    _relayStageProgress * site.SecondaryRequiredAmount,
                    site.SecondaryRequiredAmount),
            };
        }

        private string WithTimedBonus(DesertAtlasSiteDefinition site, string progressText)
        {
            if (site.BonusTimeLimit <= 0f || site.BonusGoldReward <= 0)
            {
                return progressText;
            }
            float remaining = Mathf.Max(0f, site.BonusTimeLimit - (Time.unscaledTime - _challengeStartedAt));
            return FormatDesignerText(
                _settings.TimedBonusProgressFormat,
                progressText,
                remaining,
                site.BonusGoldReward);
        }

        private Vector3 GetSiteLocalPosition(DesertAtlasSiteDefinition site)
        {
            float height = (float)_world.HeightField.SampleHeight(site.WorldPosition.x, site.WorldPosition.y) +
                _settings.HeightAboveTerrain;
            return _world.LogicalToLocal(site.WorldPosition.x, height, site.WorldPosition.y);
        }

        private Vector3 GetVectorPassTargetPosition(DesertAtlasSiteDefinition site)
        {
            return GetSiteLocalPosition(site) + (Vector3.up * _settings.CoreHeight);
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
                    string availability = site.IsFinalSignal
                        ? _settings.TerminalFinalSignalStatus
                        : _settings.TerminalAvailableStatus;
                    body = site.BonusTimeLimit > 0f && site.BonusGoldReward > 0
                        ? FormatDesignerText(
                            _settings.TerminalChallengeWithBonusFormat,
                            availability,
                            site.ChallengeInstruction,
                            site.BonusTimeLimit,
                            site.BonusGoldReward)
                        : FormatDesignerText(_settings.TerminalChallengeFormat, availability, site.ChallengeInstruction);
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
            float compassScale = GetCompassScale();
            float panelWidth = Mathf.Min(_settings.HudWidth, Screen.safeArea.width);
            float panelLeft = Screen.safeArea.x + ((Screen.safeArea.width - panelWidth) * 0.5f);
            float compassBottom = (Screen.height - Screen.safeArea.yMax) +
                ((_compassSettings.TopMargin + _compassSettings.Height) * compassScale);
            Rect panel = new Rect(
                panelLeft,
                compassBottom + (_settings.HudGapBelowCompass * compassScale),
                panelWidth,
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
            if (Time.unscaledTime < _discoveryPresentationUntil)
            {
                DrawDiscoveryPresentation();
            }
            else if (Time.unscaledTime < _statusUntil)
            {
                GUI.Label(new Rect(0f, Screen.height * _settings.StatusVerticalFraction, Screen.width,
                    _settings.StatusHeight), _statusText, _statusStyle);
            }
        }

        private void DrawDiscoveryPresentation()
        {
            float duration = Mathf.Max(0.01f, _settings.DiscoveryPresentationDuration);
            float elapsed = Time.unscaledTime - _discoveryPresentationStartedAt;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float flashDuration = Mathf.Max(0.01f, _settings.DiscoveryFlashDuration);
            if (elapsed < flashDuration)
            {
                Color flashColor = _settings.DiscoveryFlashColor;
                flashColor.a *= Mathf.Sin(Mathf.Clamp01(elapsed / flashDuration) * Mathf.PI);
                DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), flashColor);
            }

            float fade = 1f - Mathf.SmoothStep(0f, 1f, normalized);
            float slide = Mathf.Lerp(_settings.DiscoveryBannerSlideDistance, 0f, Mathf.SmoothStep(0f, 1f, normalized));
            float width = Mathf.Min(_settings.DiscoveryBannerWidth, Screen.safeArea.width);
            Rect banner = new Rect(
                Screen.safeArea.x + ((Screen.safeArea.width - width) * 0.5f),
                (Screen.height * _settings.DiscoveryBannerVerticalFraction) - slide,
                width,
                _settings.DiscoveryBannerHeight);
            Color panelColor = _settings.DiscoveryBannerColor;
            panelColor.a *= fade;
            Color accentColor = _settings.DiscoveryBannerAccentColor;
            accentColor.a *= fade;
            DrawRect(banner, panelColor);
            DrawRect(
                new Rect(banner.x, banner.y, banner.width, _settings.DiscoveryBannerAccentHeight),
                accentColor);
            Color previous = GUI.color;
            GUI.color = new Color(previous.r, previous.g, previous.b, fade);
            GUI.Label(banner, _statusText, _discoveryBannerStyle);
            GUI.color = previous;
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

        private float GetCompassScale()
        {
            float minimumScale = Mathf.Min(_compassSettings.MinimumScale, _compassSettings.MaximumScale);
            float maximumScale = Mathf.Max(_compassSettings.MinimumScale, _compassSettings.MaximumScale);
            return Mathf.Clamp(
                Screen.height / Mathf.Max(1f, _compassSettings.ReferenceHeight),
                minimumScale,
                maximumScale);
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
            _statusStyle ??= CreateStyle(_settings.HudTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _terminalTitleStyle ??= CreateStyle(_settings.TerminalTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.TerminalTextColor);
            _terminalBodyStyle ??= CreateStyle(_settings.TerminalBodyFontSize, FontStyle.Bold, TextAnchor.UpperLeft, _settings.TerminalTextColor);
            _terminalMetaStyle ??= CreateStyle(_settings.TerminalMetaFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _settings.TerminalMutedColor);
            _discoveryBannerStyle ??= CreateStyle(
                _settings.DiscoveryBannerFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.HudTextColor);
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
            _vectorPassPreviousPosition = Vector3.zero;
            _hasVectorPassPreviousPosition = false;
            _relayStage = 0;
            _relayStageProgress = 0f;
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
                _completionRewardClaimed = data.Version >= 3 && data.CompletionRewardClaimed;
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
