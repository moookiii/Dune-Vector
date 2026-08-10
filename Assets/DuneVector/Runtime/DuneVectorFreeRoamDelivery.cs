using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum FreeRoamDeliveryPhase
    {
        Inactive,
        Pickup,
        Deliver,
    }

    /// <summary>
    /// Escalating burst played where a free-roam delivery lands. The component owns every object
    /// it creates and removes itself once the longest ring has finished expanding.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuneVectorFreeRoamCompletionEffect : MonoBehaviour
    {
        private sealed class ExpandingRing
        {
            public Transform Root;
            public float Delay;
            public float StartRadius;
            public float EndRadius;
        }

        private readonly List<ExpandingRing> _rings = new List<ExpandingRing>();
        private FreeRoamDeliveryTuning _settings;
        private Material _material;
        private ParticleSystem _sparks;
        private float _elapsed;
        private float _lifetime;

        public void Initialize(
            FreeRoamDeliveryTuning settings,
            DuneVectorMaterials materials,
            Vector3 worldPosition,
            float zoneRadius,
            float tierProgress,
            Color tierColor)
        {
            _settings = settings;
            transform.position = worldPosition;

            float intensity = Mathf.Lerp(1f, Mathf.Max(1f, settings.CompletionIntensityAtHighestTier), tierProgress);
            Color emission = new Color(
                tierColor.r * intensity,
                tierColor.g * intensity,
                tierColor.b * intensity,
                1f);
            _material = new Material(materials.DroneAccent)
            {
                name = "Free Roam Delivery Completion",
            };
            if (_material.HasProperty("_BaseColor"))
            {
                _material.SetColor("_BaseColor", new Color(tierColor.r * 0.12f, tierColor.g * 0.12f, tierColor.b * 0.12f, 1f));
            }
            if (_material.HasProperty("_EmissionColor"))
            {
                _material.SetColor("_EmissionColor", emission);
                _material.EnableKeyword("_EMISSION");
            }

            int ringCount = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(
                settings.CompletionRingsAtLowestTier,
                settings.CompletionRingsAtHighestTier,
                tierProgress)));
            float startRadius = Mathf.Max(1f, zoneRadius * settings.CompletionRingStartRadiusFraction);
            float endRadius = Mathf.Max(startRadius + 1f, zoneRadius * settings.CompletionRingEndRadiusFraction);
            for (int i = 0; i < ringCount; i++)
            {
                float ringPhase = ringCount <= 1 ? 0f : i / (float)(ringCount - 1);
                Transform ringRoot = new GameObject($"Completion Ring {i + 1}").transform;
                ringRoot.SetParent(transform, false);
                BuildSegmentedRing(ringRoot, startRadius);
                _rings.Add(new ExpandingRing
                {
                    Root = ringRoot,
                    Delay = settings.CompletionRingStagger * i,
                    StartRadius = startRadius,
                    EndRadius = Mathf.Lerp(endRadius * 0.7f, endRadius, ringPhase),
                });
            }

            int sparkCount = Mathf.Max(0, Mathf.RoundToInt(Mathf.Lerp(
                settings.CompletionSparksAtLowestTier,
                settings.CompletionSparksAtHighestTier,
                tierProgress)));
            if (sparkCount > 0)
            {
                BuildSparkBurst(sparkCount);
            }

            _lifetime = Mathf.Max(
                settings.CompletionRingDuration + (settings.CompletionRingStagger * ringCount),
                settings.CompletionSparkLifetime) + 0.25f;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
        }

        private void BuildSegmentedRing(Transform ringRoot, float radius)
        {
            int segments = Mathf.Max(4, _settings.CompletionRingSegments);
            float thickness = Mathf.Max(0.05f, _settings.CompletionRingSegmentThickness);
            float height = Mathf.Max(0.05f, _settings.CompletionRingHeight);
            float segmentLength = (2f * Mathf.PI * radius) / segments * 0.72f;
            for (int i = 0; i < segments; i++)
            {
                float angle = (360f / segments) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Arc {i + 1:00}";
                segment.transform.SetParent(ringRoot, false);
                segment.transform.localPosition = direction * radius;
                segment.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                segment.transform.localScale = new Vector3(segmentLength, height, thickness);
                Renderer renderer = segment.GetComponent<Renderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Collider collider = segment.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
            }
        }

        private void BuildSparkBurst(int sparkCount)
        {
            GameObject sparkObject = new GameObject("Completion Sparks");
            sparkObject.transform.SetParent(transform, false);
            _sparks = sparkObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.1f, _settings.CompletionSparkLifetime);
            main.startLifetime = _settings.CompletionSparkLifetime;
            main.startSpeed = _settings.CompletionSparkSpeed;
            main.startSize = _settings.CompletionSparkSize;
            main.maxParticles = sparkCount;
            main.gravityModifier = 0.4f;

            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)sparkCount) });

            ParticleSystem.ShapeModule shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Lerp(80f, 12f, Mathf.Clamp01(_settings.CompletionSparkUpwardBias));
            shape.radius = 1.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystemRenderer particleRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = _material;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _sparks.Play();
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float duration = Mathf.Max(0.05f, _settings.CompletionRingDuration);
            for (int i = 0; i < _rings.Count; i++)
            {
                ExpandingRing ring = _rings[i];
                if (ring.Root == null)
                {
                    continue;
                }

                float progress = Mathf.Clamp01((_elapsed - ring.Delay) / duration);
                if (progress <= 0f)
                {
                    ring.Root.localScale = Vector3.zero;
                    continue;
                }

                // Fast out, slow in: the shockwave punches outward and then eases as it fades.
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                float radiusScale = Mathf.Lerp(1f, ring.EndRadius / Mathf.Max(0.01f, ring.StartRadius), eased);
                float fade = 1f - progress;
                ring.Root.localScale = new Vector3(radiusScale, fade, radiusScale);
            }

            if (_elapsed >= _lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }

    /// <summary>
    /// Repeating free-roam courier cycle. A hexagon zone is fitted over the landmark beside the
    /// deployment point, the drone collects a package there, and each drop-off seeds a new pickup
    /// an authored distance away. Consecutive deliveries raise a streak multiplier that resets on
    /// death, abandonment, or any return to the hub.
    /// </summary>
    [DefaultExecutionOrder(1130)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorFreeRoamDeliverySystem : MonoBehaviour
    {
        public FreeRoamDeliveryPhase Phase { get; private set; } = FreeRoamDeliveryPhase.Inactive;
        public int Streak { get; private set; }

        /// <summary>True while the current leg rolled the escalated route.</summary>
        public bool IsHardRoute { get; private set; }

        /// <summary>Contract risk the current leg applies to enemies.</summary>
        public int RouteRisk { get; private set; }
        public bool IsCarryingCargo => Phase == FreeRoamDeliveryPhase.Deliver && _package != null;
        public Transform ActiveObjective { get; private set; }
        public LogicalPosition ActiveObjectiveLogicalPosition { get; private set; }

        private readonly HashSet<string> _usedLandmarkIds = new HashSet<string>();
        private readonly DuneVectorObjectiveIndicator _objectiveIndicator = new DuneVectorObjectiveIndicator();

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private Camera _camera;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private DuneVectorLandmarkDirector _landmarks;
        private DuneVectorCourierGame _courierGame;
        private DuneVectorCourierProgress _progress;
        private DeliveryTuning _deliverySettings;
        private CourierContractTuning _contractSettings;
        private FreeRoamDeliveryTuning _settings;

        private JobTraversalRing _zoneRing;
        private Transform _package;
        private DuneVectorFreeRoamCompletionEffect _completionEffect;
        private System.Random _random;
        private LogicalPosition _zoneCenter;
        private double _pickupLogicalHeight;
        private float _zoneRadius;
        private float _streakPunchTimer;
        private int _lastRewardGold;
        private GUIStyle _multiplierStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _rewardStyle;

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DuneVectorLandmarkDirector landmarks,
            DuneVectorCourierGame courierGame,
            DuneVectorCourierProgress progress,
            DeliveryTuning deliverySettings,
            CourierContractTuning contractSettings,
            FreeRoamDeliveryTuning settings)
        {
            _player = player;
            _world = world;
            _camera = camera;
            _materials = materials;
            _wallet = wallet;
            _landmarks = landmarks;
            _courierGame = courierGame;
            _progress = progress;
            _deliverySettings = deliverySettings;
            _contractSettings = contractSettings;
            _settings = settings ?? new FreeRoamDeliveryTuning();
            _settings.EnsureInitialized();
            _random = new System.Random(unchecked(_world.WorldSeed ^ 0x5F3A21));
            _world.WorldShifted += HandleWorldShift;
        }

        /// <summary>
        /// Starts a fresh free-roam run. Every landmark used during the previous deployment is
        /// forgotten and the streak restarts at zero.
        /// </summary>
        public void BeginDeployment()
        {
            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            ClearRuntimeObjects();
            _usedLandmarkIds.Clear();
            Streak = 0;
            _streakPunchTimer = 0f;
            _lastRewardGold = 0;
            BeginPickupLeg(useNearestLandmark: true);
        }

        public void EndDeployment()
        {
            ClearRuntimeObjects();
            Phase = FreeRoamDeliveryPhase.Inactive;
            Streak = 0;
            _streakPunchTimer = 0f;
        }

        private void BeginPickupLeg(bool useNearestLandmark)
        {
            DestroyZoneRing();
            RollRouteEscalation();
            LogicalPosition origin = _world.LogicalPlayerPosition;
            DuneLandmarkPlacementRecord record = useNearestLandmark
                ? _landmarks.ResolveNearestWorldLandmarkOfAnyType(origin)
                : ResolveNextLandmark(origin);
            if (record == null)
            {
                Debug.LogWarning(
                    "Free roam could not resolve a pickup landmark. The delivery cycle is idle until the next deployment.",
                    this);
                Phase = FreeRoamDeliveryPhase.Inactive;
                return;
            }

            DuneVectorLandmarkInstance landmark = PinAndFitZone(record);
            if (landmark == null)
            {
                Phase = FreeRoamDeliveryPhase.Inactive;
                return;
            }

            Phase = FreeRoamDeliveryPhase.Pickup;
            _package = DuneVectorVisuals.CreatePackageVisual(
                transform,
                _materials,
                _contractSettings.ObjectivePackageScale);
            _package.name = "Free Roam Package";
            _package.position = landmark.ContractSocket.position;
            _zoneRing = CreateZoneRing("Free Roam Pickup Zone", landmark, true, HandlePickup);
            ActiveObjective = _package;
            ActiveObjectiveLogicalPosition = ToLogical(_package.position);
            _pickupLogicalHeight = _package.position.y;
            _courierGame.ShowStatusMessage(
                $"SIGNAL LOCKED — COLLECT CARGO AT {DescribeRoute(record)}",
                _settings.StatusMessageDuration);
        }

        private void HandlePickup()
        {
            if (Phase != FreeRoamDeliveryPhase.Pickup || _package == null)
            {
                return;
            }

            Transform carryParent = _player.DroneVisualRoot != null
                ? _player.DroneVisualRoot
                : _player.transform;
            _package.SetParent(carryParent, false);
            _package.localPosition = _contractSettings.CarriedPackageOffset;
            _package.localRotation = Quaternion.Euler(0f, 18f, 0f);
            BeginDeliveryLeg();
        }

        private void BeginDeliveryLeg()
        {
            DestroyZoneRing();
            DuneLandmarkPlacementRecord record = ResolveNextLandmark(_world.LogicalPlayerPosition);
            if (record == null)
            {
                Debug.LogWarning(
                    "Free roam could not resolve a drop-off landmark. The delivery cycle is idle until the next deployment.",
                    this);
                Phase = FreeRoamDeliveryPhase.Inactive;
                return;
            }

            DuneVectorLandmarkInstance landmark = PinAndFitZone(record);
            if (landmark == null)
            {
                Phase = FreeRoamDeliveryPhase.Inactive;
                return;
            }

            Phase = FreeRoamDeliveryPhase.Deliver;
            _zoneRing = CreateZoneRing("Free Roam Delivery Zone", landmark, false, HandleDelivery);
            ActiveObjective = _zoneRing.transform;
            ActiveObjectiveLogicalPosition = _zoneRing.LogicalPosition;
            _courierGame.ShowStatusMessage(
                $"CARGO SECURED — DELIVER TO {DescribeRoute(record)}",
                _settings.StatusMessageDuration);
        }

        private void HandleDelivery()
        {
            if (Phase != FreeRoamDeliveryPhase.Deliver)
            {
                return;
            }

            Vector3 deliveryPosition = _zoneRing != null
                ? _zoneRing.transform.position
                : _player.WorldCenter;
            Streak++;
            int reward = _settings.EvaluateDeliveryGold(Streak);
            _lastRewardGold = reward;
            _wallet?.AddGold(reward);
            _progress?.RecordFreeRoamDelivery(reward, Streak);
            _streakPunchTimer = _settings.StreakCounterPunchDuration;
            PlayCompletionEffect(deliveryPosition);
            DestroyPackage();
            FreeRoamStreakTier tier = _settings.EvaluateTier(Streak);
            _courierGame.ShowStatusMessage(
                $"DELIVERED  •  {Streak}x {tier.Label}  •  +{reward} GOLD",
                _settings.StatusMessageDuration);
            BeginPickupLeg(useNearestLandmark: false);
        }

        /// <summary>
        /// Dying, abandoning, or returning to the hub all break the run.
        /// </summary>
        public void NotifyStreakBroken()
        {
            if (Phase == FreeRoamDeliveryPhase.Inactive && Streak == 0)
            {
                return;
            }

            Streak = 0;
            _streakPunchTimer = 0f;
        }

        private DuneLandmarkPlacementRecord ResolveNextLandmark(LogicalPosition origin)
        {
            float legDistance = _settings.EvaluateLegDistance(Streak, IsHardRoute);
            List<DuneLandmarkType> preferredTypes = IsHardRoute ? _settings.DangerousLandmarkTypes : null;
            DuneLandmarkPlacementRecord record = _landmarks.ResolveRandomWorldLandmarkAtDistance(
                origin,
                legDistance,
                _settings.LegDistanceTolerance,
                _settings.LegDistanceWideningSteps,
                _random,
                _usedLandmarkIds,
                preferredTypes);
            if (record != null)
            {
                return record;
            }

            // Long runs eventually consume every nearby landmark. Forgetting the run's history is
            // better than stranding the courier with no destination at all.
            _usedLandmarkIds.Clear();
            return _landmarks.ResolveRandomWorldLandmarkAtDistance(
                origin,
                legDistance,
                _settings.LegDistanceTolerance,
                _settings.LegDistanceWideningSteps,
                _random,
                _usedLandmarkIds,
                preferredTypes);
        }

        /// <summary>
        /// Rolls the next leg's difficulty. The chance of a hard route and the risk applied to
        /// enemies both climb with the streak, so an unbroken run drifts toward longer routes
        /// through the authored dangerous landmarks without ever changing the default 100m feel
        /// at a low streak.
        /// </summary>
        private void RollRouteEscalation()
        {
            double roll = _random != null ? _random.NextDouble() : UnityEngine.Random.value;
            IsHardRoute = roll < _settings.EvaluateHardRouteChance(Streak);
            RouteRisk = _settings.EvaluateRouteRisk(Streak, IsHardRoute);
            DuneVectorContractRisk.Configure(_contractSettings, RouteRisk);
        }

        private string DescribeRoute(DuneLandmarkPlacementRecord record)
        {
            string landmark = DuneLandmarkNames.GetDisplayName(record.Type).ToUpperInvariant();
            return IsHardRoute && !string.IsNullOrEmpty(_settings.HardRoutePrefix)
                ? $"{_settings.HardRoutePrefix} — {landmark}"
                : landmark;
        }

        private DuneVectorLandmarkInstance PinAndFitZone(DuneLandmarkPlacementRecord record)
        {
            _landmarks.ClearContractLandmarks();
            DuneVectorLandmarkInstance landmark = _landmarks.PinWorldLandmark(record);
            if (landmark == null)
            {
                return null;
            }

            _usedLandmarkIds.Add(record.PersistentId);
            _zoneRadius = Mathf.Clamp(
                landmark.CalculateMeshHorizontalRadius() + _settings.ZoneMargin,
                _settings.MinimumZoneRadius,
                Mathf.Max(_settings.MinimumZoneRadius, _settings.MaximumZoneRadius));
            _zoneCenter = landmark.TryCalculateMeshBounds(out Bounds meshBounds)
                ? ToLogical(meshBounds.center)
                : landmark.LogicalPosition;
            return landmark;
        }

        private JobTraversalRing CreateZoneRing(
            string objectName,
            DuneVectorLandmarkInstance landmark,
            bool isPickup,
            Action crossed)
        {
            GameObject ringObject = new GameObject(objectName);
            ringObject.transform.SetParent(transform, false);
            JobTraversalRing ring = ringObject.AddComponent<JobTraversalRing>();
            FreeRoamDeliveryPhase requiredPhase = isPickup
                ? FreeRoamDeliveryPhase.Pickup
                : FreeRoamDeliveryPhase.Deliver;
            ring.Initialize(
                _player,
                _camera,
                _materials,
                _deliverySettings,
                isPickup,
                _zoneRadius,
                crossed,
                () => Phase == requiredPhase,
                !isPickup);
            // The zone sits on the terrain under the middle of the landmark's silhouette, not on
            // the landmark origin, which several archetypes place off to one side of their meshes.
            double groundHeight = _world.HeightField.SampleHeight(_zoneCenter.X, _zoneCenter.Z);
            double ringHeight = groundHeight + (isPickup
                ? _deliverySettings.PickupRingGroundOffset
                : _deliverySettings.DeliveryRingGroundOffset);
            ring.LogicalPosition = _zoneCenter;
            ring.LogicalHeight = ringHeight;
            ring.transform.position = _world.LogicalToLocal(
                _zoneCenter.X,
                ringHeight,
                _zoneCenter.Z);
            return ring;
        }

        private void PlayCompletionEffect(Vector3 worldPosition)
        {
            if (_completionEffect != null)
            {
                Destroy(_completionEffect.gameObject);
            }

            GameObject effectObject = new GameObject("Free Roam Delivery Completion Effect");
            effectObject.transform.SetParent(transform, false);
            _completionEffect = effectObject.AddComponent<DuneVectorFreeRoamCompletionEffect>();
            _completionEffect.Initialize(
                _settings,
                _materials,
                worldPosition,
                _zoneRadius,
                _settings.EvaluateTierProgress(Streak),
                _settings.EvaluateTier(Streak).Color);
            DuneVectorAudioManager.Instance?.PlayDeliveryRing(worldPosition);
        }

        private void Update()
        {
            if (_streakPunchTimer > 0f)
            {
                _streakPunchTimer = Mathf.Max(0f, _streakPunchTimer - Time.deltaTime);
            }
            if (Phase == FreeRoamDeliveryPhase.Pickup && _package != null)
            {
                _package.Rotate(0f, _contractSettings.PackageSpinSpeed * Time.deltaTime, 0f, Space.World);
            }
        }

        private void LateUpdate()
        {
            // Floating-origin rebases move scene transforms in large float-sized steps. Rebuild
            // the active objective from its immutable logical coordinate every frame so a missed,
            // duplicated, or precision-lost shift can never make the target recede from the drone.
            if (Phase == FreeRoamDeliveryPhase.Pickup && _package != null)
            {
                _package.position = _world.LogicalToLocal(
                    ActiveObjectiveLogicalPosition.X,
                    _pickupLogicalHeight,
                    ActiveObjectiveLogicalPosition.Z);
            }
            else if (Phase == FreeRoamDeliveryPhase.Deliver && _zoneRing != null)
            {
                _zoneRing.transform.position = _world.LogicalToLocal(
                    _zoneRing.LogicalPosition.X,
                    _zoneRing.LogicalHeight,
                    _zoneRing.LogicalPosition.Z);
            }
        }

        private void HandleWorldShift(Vector3 shift)
        {
            _zoneRing?.ApplyWorldShift(shift);
            // Unity's null-conditional operator does not use Object's destroyed-object null
            // semantics. The completion effect can destroy itself while this field still holds
            // its managed wrapper; invoking through ?. then throws and prevents every later
            // WorldShifted subscriber (including the hub and camera) from receiving the rebase.
            if (_completionEffect != null)
            {
                _completionEffect.ApplyWorldShift(shift);
            }
            if (Phase == FreeRoamDeliveryPhase.Pickup && _package != null)
            {
                _package.position += shift;
            }
        }

        private LogicalPosition ToLogical(Vector3 localPosition)
        {
            return new LogicalPosition(
                _world.OriginOffsetX + localPosition.x,
                _world.OriginOffsetZ + localPosition.z);
        }

        private void ClearRuntimeObjects()
        {
            DestroyZoneRing();
            DestroyPackage();
            if (_completionEffect != null)
            {
                Destroy(_completionEffect.gameObject);
            }
            _completionEffect = null;
            ActiveObjective = null;
        }

        private void DestroyZoneRing()
        {
            if (_zoneRing != null)
            {
                Destroy(_zoneRing.gameObject);
                _zoneRing = null;
            }
            ActiveObjective = null;
        }

        private void DestroyPackage()
        {
            if (_package != null)
            {
                Destroy(_package.gameObject);
                _package = null;
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint ||
                Phase == FreeRoamDeliveryPhase.Inactive ||
                DuneVectorCourierGame.IsGameplayHudSuppressed ||
                _courierGame == null ||
                _courierGame.State != CourierRunState.FreeRoam ||
                _courierGame.IsTerminalOpen)
            {
                return;
            }

            EnsureStyles();
            DrawObjectiveIndicator();
            DrawStreakCounter();
        }

        private void DrawObjectiveIndicator()
        {
            if (ActiveObjective == null || _camera == null)
            {
                return;
            }

            float distance = Vector3.Distance(_player.WorldCenter, ActiveObjective.position);
            _objectiveIndicator.Draw(
                _camera,
                ActiveObjective,
                Phase == FreeRoamDeliveryPhase.Pickup ? "PICKUP" : "DELIVER",
                distance,
                _deliverySettings);
        }

        private void DrawStreakCounter()
        {
            if (Streak <= 0)
            {
                return;
            }

            FreeRoamStreakTier tier = _settings.EvaluateTier(Mathf.Max(1, Streak));
            float punch = _settings.StreakCounterPunchDuration <= 0f
                ? 0f
                : Mathf.Clamp01(_streakPunchTimer / _settings.StreakCounterPunchDuration);
            float scale = 1f + (_settings.StreakCounterPunchScale * punch * punch);

            float centerX = Screen.width * _settings.StreakCounterAnchor.x;
            float centerY = Screen.height * _settings.StreakCounterAnchor.y;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(centerX, centerY));

            float edgePadding = Mathf.Max(0f, _settings.StreakCounterEdgePadding);
            float availableHalfWidth = Mathf.Max(
                1f,
                Mathf.Min(centerX - edgePadding, Screen.width - edgePadding - centerX));
            float width = Mathf.Min(
                Screen.width * Mathf.Clamp01(_settings.StreakCounterMaxWidthFraction),
                (availableHalfWidth * 2f) / Mathf.Max(1f, scale));
            Rect multiplierRect = new Rect(
                centerX - (width * 0.5f),
                centerY - (_settings.StreakMultiplierFontSize * 0.62f),
                width,
                _settings.StreakMultiplierFontSize * 1.3f);
            Rect labelRect = new Rect(
                multiplierRect.x,
                multiplierRect.yMax,
                width,
                _settings.StreakLabelFontSize * 1.5f);
            Rect rewardRect = new Rect(
                multiplierRect.x,
                multiplierRect.y - (_settings.StreakRewardFontSize * 1.6f),
                width,
                _settings.StreakRewardFontSize * 1.5f);

            _multiplierStyle.fontSize = _settings.StreakMultiplierFontSize;
            _labelStyle.fontSize = _settings.StreakLabelFontSize;
            _rewardStyle.fontSize = _settings.StreakRewardFontSize;

            string multiplierText = $"{Streak}x";
            string labelText = IsHardRoute && !string.IsNullOrEmpty(_settings.HardRoutePrefix)
                ? $"{tier.Label}  •  {_settings.EvaluateDeliveryGold(Streak)} GOLD PER DROP  •  {_settings.HardRoutePrefix}"
                : $"{tier.Label}  •  {_settings.EvaluateDeliveryGold(Streak)} GOLD PER DROP";
            DrawShadowedLabel(multiplierRect, multiplierText, _multiplierStyle, tier.Color);
            DrawShadowedLabel(labelRect, labelText, _labelStyle, _settings.StreakLabelColor);
            if (punch > 0f && _lastRewardGold > 0)
            {
                Color rewardColor = tier.Color;
                rewardColor.a = punch;
                DrawShadowedLabel(rewardRect, $"+{_lastRewardGold} GOLD", _rewardStyle, rewardColor);
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawShadowedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color shadowColor = _settings.StreakShadowColor;
            shadowColor.a *= color.a;
            style.normal.textColor = shadowColor;
            GUI.Label(
                new Rect(
                    rect.x + _settings.StreakShadowOffset.x,
                    rect.y + _settings.StreakShadowOffset.y,
                    rect.width,
                    rect.height),
                text,
                style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private void EnsureStyles()
        {
            if (_multiplierStyle != null)
            {
                return;
            }

            _multiplierStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _labelStyle = new GUIStyle(_multiplierStyle) { fontStyle = FontStyle.Bold };
            _rewardStyle = new GUIStyle(_multiplierStyle) { fontStyle = FontStyle.Bold };
        }
    }
}
