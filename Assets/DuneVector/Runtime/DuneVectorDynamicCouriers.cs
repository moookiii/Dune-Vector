using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum DynamicCourierEventType
    {
        None,
        MovingConvoy,
    }

    internal enum DynamicCourierEventPhase
    {
        Inactive,
        Active,
        Result,
    }

    [DisallowMultipleComponent]
    public sealed class DynamicCourierAgent : MonoBehaviour
    {
        private static readonly HashSet<DynamicCourierAgent> ActiveAgentSet =
            new HashSet<DynamicCourierAgent>();

        public static IReadOnlyCollection<DynamicCourierAgent> ActiveAgents => ActiveAgentSet;
        public CourierDroneFaction Faction { get; private set; }
        public bool IsAlive => _currentHealth > 0f;
        public bool ReachedDestination { get; private set; }
        public float CurrentHealth => _currentHealth;
        public float MaximumHealth => _maximumHealth;
        public float NormalizedHealth => _maximumHealth > 0f ? Mathf.Clamp01(_currentHealth / _maximumHealth) : 0f;

        private DesertWorldStreamer _world;
        private DynamicCourierTuning _settings;
        private Transform _cachedTransform;
        private Transform _package;
        private Vector3 _destination;
        private float _speed;
        private float _maximumHealth;
        private float _currentHealth;
        private float _hoverPhase;
        private float _flightHeightAboveTerrain;
        private bool _paused;

        public void Initialize(
            CourierDroneFaction faction,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            DynamicCourierTuning settings,
            float startingHealthFraction,
            float hoverPhase)
        {
            Faction = faction;
            _world = world;
            _settings = settings;
            _cachedTransform = transform;
            _maximumHealth = Mathf.Max(Mathf.Epsilon, settings.MaximumCourierHealth);
            _currentHealth = _maximumHealth * Mathf.Clamp01(startingHealthFraction);
            _hoverPhase = hoverPhase;
            _flightHeightAboveTerrain = settings.FlightHeightAboveTerrain;
            DroneVisualTuning droneVisuals = DuneVectorBootstrap.Instance != null
                ? DuneVectorBootstrap.Instance.DroneVisuals
                : null;
            Transform visual = DuneVectorVisuals.CreateDroneVisual(_cachedTransform, materials, faction, droneVisuals);
            visual.localScale = Vector3.one * settings.VisualScale;
        }

        public void SetFlightHeightAboveTerrain(float height)
        {
            _flightHeightAboveTerrain = Mathf.Max(0f, height);
        }

        public void AttachPackage(DuneVectorMaterials materials, float scale, Vector3 localOffset)
        {
            _package = DuneVectorVisuals.CreatePackageVisual(_cachedTransform, materials, scale);
            _package.localPosition = localOffset;
        }

        /// <summary>
        /// Ambient couriers run the same pickup and drop-off cycle the player does, so their cargo
        /// is only visible on the leg between collecting it and delivering it.
        /// </summary>
        public void SetPackageVisible(bool visible)
        {
            if (_package != null)
            {
                _package.gameObject.SetActive(visible);
            }
        }

        public void ConfigureRoute(Vector3 destination, float speed, bool paused)
        {
            _destination = destination;
            _speed = Mathf.Max(0f, speed);
            _paused = paused;
            ReachedDestination = false;
        }

        public bool TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0f)
            {
                return false;
            }

            _currentHealth = Mathf.Max(0f, _currentHealth - damage);
            if (_currentHealth <= 0f)
            {
                Destroy(gameObject);
            }
            return true;
        }

        public void HandleWorldShift(Vector3 worldShift)
        {
            if (_cachedTransform != null)
            {
                _cachedTransform.position += worldShift;
            }
            _destination += worldShift;
        }

        private void OnEnable()
        {
            ActiveAgentSet.Add(this);
        }

        private void OnDisable()
        {
            ActiveAgentSet.Remove(this);
        }

        private void Update()
        {
            if (_world == null || _settings == null || !IsAlive || ReachedDestination)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 position = _cachedTransform.position;
            Vector3 horizontalToDestination = Vector3.ProjectOnPlane(_destination - position, Vector3.up);
            float destinationRadius = Mathf.Max(Mathf.Epsilon, _settings.DestinationRadius);
            if (horizontalToDestination.sqrMagnitude <= destinationRadius * destinationRadius)
            {
                ReachedDestination = true;
                _paused = true;
                return;
            }

            Vector3 horizontalDirection = horizontalToDestination.normalized;
            Vector3 next = _paused
                ? position
                : position + (horizontalDirection * (_speed * deltaTime));
            float hover = Mathf.Sin((Time.time * _settings.HoverFrequency) + _hoverPhase) * _settings.HoverAmplitude;
            float targetHeight = _world.SampleHeightAtLocal(next.x, next.z) + _flightHeightAboveTerrain + hover;
            next.y = Mathf.Lerp(position.y, targetHeight, DuneVectorMath.Sharpness(_settings.TurnSharpness, deltaTime));
            _cachedTransform.position = next;

            if (!_paused && horizontalDirection.sqrMagnitude > Mathf.Epsilon)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDirection, Vector3.up);
                _cachedTransform.rotation = Quaternion.Slerp(
                    _cachedTransform.rotation,
                    targetRotation,
                    DuneVectorMath.Sharpness(_settings.TurnSharpness, deltaTime));
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DynamicCourierAttacker : MonoBehaviour
    {
        private DynamicCourierAgent _target;
        private DynamicCourierTuning _settings;
        private DuneVectorMaterials _materials;
        private Transform _cachedTransform;
        private float _shotTimer;
        private float _orbitPhase;

        public void Initialize(
            DynamicCourierAgent target,
            DroneGoldWallet wallet,
            DuneVectorMaterials materials,
            DynamicCourierTuning settings,
            float orbitPhase)
        {
            _target = target;
            _settings = settings;
            _materials = materials;
            _cachedTransform = transform;
            _orbitPhase = orbitPhase;
            _shotTimer = settings.AttackerShotInterval;

            DuneVectorVisuals.CreateFlyingEnemyVisual(_cachedTransform, materials, settings.AttackerVisualScale);
            EnemyHealth health = gameObject.AddComponent<EnemyHealth>();
            health.Initialize(settings.AttackerMaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(health, settings.AttackerCollisionRadius);
            EnemyGoldReward reward = gameObject.AddComponent<EnemyGoldReward>();
            reward.Initialize(health, wallet, settings.AttackerGoldReward);
        }

        public void HandleWorldShift(Vector3 worldShift)
        {
            if (_cachedTransform != null)
            {
                _cachedTransform.position += worldShift;
            }
        }

        private void Update()
        {
            if (_target == null || !_target.IsAlive || _settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _shotTimer = Mathf.Max(0f, _shotTimer -
                (deltaTime * DuneVectorContractRisk.EnemyAttackRateMultiplier));
            Vector3 targetPosition = _target.transform.position;
            Vector3 orbitOffset = new Vector3(
                Mathf.Cos(_orbitPhase),
                0f,
                Mathf.Sin(_orbitPhase)) * _settings.AttackerOrbitRadius;
            Vector3 desiredPosition = targetPosition + orbitOffset + (Vector3.up * _settings.AttackerHeightOffset);
            _cachedTransform.position = Vector3.MoveTowards(
                _cachedTransform.position,
                desiredPosition,
                _settings.AttackerSpeed * DuneVectorContractRisk.EnemySpeedMultiplier * deltaTime);

            Vector3 toTarget = targetPosition - _cachedTransform.position;
            if (toTarget.sqrMagnitude > Mathf.Epsilon)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                _cachedTransform.rotation = Quaternion.Slerp(
                    _cachedTransform.rotation,
                    desiredRotation,
                    DuneVectorMath.Sharpness(_settings.AttackerTurnSharpness, deltaTime));
            }

            float shotRange = Mathf.Max(Mathf.Epsilon, _settings.AttackerShotRange);
            if (_shotTimer <= 0f && toTarget.sqrMagnitude <= shotRange * shotRange)
            {
                _shotTimer = _settings.AttackerShotInterval;
                _target.TakeDamage(
                    _settings.AttackerShotDamage * DuneVectorContractRisk.EnemyDamageMultiplier);
                CreateShotVisual(_cachedTransform.position, targetPosition);
            }
        }

        private void CreateShotVisual(Vector3 start, Vector3 end)
        {
            GameObject shotObject = new GameObject("Courier Event Attacker Shot");
            LineRenderer line = shotObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = _settings.AttackerShotStartWidth;
            line.endWidth = _settings.AttackerShotEndWidth;
            line.sharedMaterial = _materials.EnemyCore;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            Destroy(shotObject, _settings.AttackerShotVisualDuration);
        }
    }

    [DefaultExecutionOrder(1260)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorDynamicCourierDirector : MonoBehaviour
    {
        private sealed class AmbientCourierFlight
        {
            public DynamicCourierAgent Courier;
            public float CruiseSpeed;
            public float FlightHeight;
            public float TurnaroundRemaining;

            /// <summary>True while the courier is flying its cargo to a drop-off landmark.</summary>
            public bool CarryingCargo;

            /// <summary>Landmarks this courier has already used, mirroring the free-roam run history.</summary>
            public readonly HashSet<string> UsedLandmarkIds = new HashSet<string>();

            /// <summary>
            /// Remaining hops of the current leg. A leg is a single hop unless the straight line
            /// would cut through the hub, in which case bypass waypoints are queued ahead of the
            /// real destination.
            /// </summary>
            public readonly List<Vector3> PendingWaypoints = new List<Vector3>();
        }

        public DynamicCourierEventType ActiveEventType { get; private set; }

        private readonly List<DynamicCourierAgent> _couriers = new List<DynamicCourierAgent>();
        private readonly List<DynamicCourierAttacker> _attackers = new List<DynamicCourierAttacker>();
        private readonly List<AmbientCourierFlight> _ambientCouriers = new List<AmbientCourierFlight>();

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private Camera _camera;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private DynamicCourierTuning _settings;
        private DuneVectorCourierGame _courierGame;
        private DuneVectorLandmarkDirector _landmarks;
        private FreeRoamDeliveryTuning _freeRoamSettings;
        private System.Random _ambientRandom;
        private DynamicCourierEventPhase _phase;
        private DynamicCourierAgent _primaryCourier;
        private Vector3 _eventDestination;
        private Transform _objectiveTarget;
        private float _eventTimer;
        private bool _wasGameplayAvailable;
        private string _eventTitle;
        private string _eventStatus;
        private Color _eventColor;
        // Unscaled stamp of the last phase change; drives the panel slide-in and the swap flash
        // when the event resolves.
        private float _phaseChangedAt;
        private float _hullNormalized;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _meterLabelStyle;
        private GUIStyle _meterValueStyle;
        private GUIStyle _markerStyle;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DynamicCourierTuning settings,
            DuneVectorCourierGame courierGame,
            DuneVectorLandmarkDirector landmarks,
            FreeRoamDeliveryTuning freeRoamSettings)
        {
            _landmarks = landmarks;
            _freeRoamSettings = freeRoamSettings ?? new FreeRoamDeliveryTuning();
            _freeRoamSettings.EnsureInitialized();
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _camera = camera;
            _materials = materials;
            _wallet = wallet;
            _settings = settings;
            _courierGame = courierGame;
            _eventTimer = settings.InitialEventDelay;
            _world.WorldShifted += HandleWorldShift;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }

        private void Update()
        {
            if (_settings == null || !_settings.Enabled || _player == null || _world == null ||
                _playerHealth == null || _playerHealth.IsDead)
            {
                return;
            }

            bool ambientAvailable = IsAmbientAvailable();
            bool gameplayAvailable = IsGameplayAvailable();
            if (!gameplayAvailable && _wasGameplayAvailable)
            {
                ClearEvent();
            }

            if (gameplayAvailable && !_wasGameplayAvailable)
            {
                _eventTimer = _settings.InitialEventDelay;
            }

            _wasGameplayAvailable = gameplayAvailable;

            PruneRuntimeObjects();
            if (ambientAvailable)
            {
                UpdateAmbientCouriers(Time.deltaTime);
            }
            else if (_ambientCouriers.Count > 0)
            {
                ClearAmbientCouriers();
            }

            if (!gameplayAvailable)
            {
                return;
            }

            if (_phase == DynamicCourierEventPhase.Inactive)
            {
                _eventTimer -= Time.deltaTime;
                if (_eventTimer <= 0f)
                {
                    SpawnNextEvent();
                }
                return;
            }

            if (_phase == DynamicCourierEventPhase.Result)
            {
                _eventTimer -= Time.deltaTime;
                if (_eventTimer <= 0f)
                {
                    ClearEvent();
                    ScheduleNextEvent();
                }
                return;
            }

            switch (ActiveEventType)
            {
                case DynamicCourierEventType.MovingConvoy:
                    UpdateConvoyEvent();
                    break;
            }
        }

        private bool IsGameplayAvailable()
        {
            return IsAvailableInState(_settings.EventsDuringFreeRoam, _settings.EventsInHub);
        }

        private bool IsAmbientAvailable()
        {
            return IsAvailableInState(
                _settings.AmbientNeutralCouriersDuringFreeRoam,
                _settings.AmbientNeutralCouriersInHub);
        }

        private bool IsAvailableInState(bool allowedInFreeRoam, bool allowedInHub)
        {
            if (_courierGame == null)
            {
                return true;
            }
            switch (_courierGame.State)
            {
                case CourierRunState.FreeRoam:
                    return allowedInFreeRoam;
                case CourierRunState.Hub:
                    return allowedInHub;
                case CourierRunState.FindPackage:
                case CourierRunState.Delivering:
                    return true;
                default:
                    return false;
            }
        }

        private void SpawnNextEvent()
        {
            float convoyWeight = Mathf.Max(0f, _settings.ConvoyEventWeight);
            if (convoyWeight <= Mathf.Epsilon)
            {
                ScheduleNextEvent();
                return;
            }

            SpawnConvoyEvent();
        }

        private void UpdateAmbientCouriers(float deltaTime)
        {
            if (!_settings.AmbientNeutralCouriersEnabled || _settings.AmbientNeutralCourierCount <= 0)
            {
                ClearAmbientCouriers();
                return;
            }

            for (int i = _ambientCouriers.Count - 1; i >= 0; i--)
            {
                AmbientCourierFlight flight = _ambientCouriers[i];
                if (flight.Courier == null)
                {
                    _ambientCouriers.RemoveAt(i);
                    continue;
                }

                if (HorizontalDistance(_player.WorldCenter, flight.Courier.transform.position) > _settings.AmbientDespawnDistance)
                {
                    Destroy(flight.Courier.gameObject);
                    _ambientCouriers.RemoveAt(i);
                    continue;
                }

                if (!flight.Courier.ReachedDestination)
                {
                    continue;
                }

                // Bypass waypoints are mid-leg course changes, not arrivals, so the courier picks
                // the next one up without pausing or swapping its cargo state.
                if (flight.PendingWaypoints.Count > 0)
                {
                    AdvanceAmbientWaypoint(flight);
                    continue;
                }

                flight.TurnaroundRemaining -= deltaTime;
                if (flight.TurnaroundRemaining > 0f)
                {
                    continue;
                }

                BeginAmbientLeg(flight, flight.Courier.transform.position, advanceCycle: true);
            }

            int desiredCount = Mathf.Max(0, _settings.AmbientNeutralCourierCount);
            while (_ambientCouriers.Count > desiredCount)
            {
                int lastIndex = _ambientCouriers.Count - 1;
                AmbientCourierFlight flight = _ambientCouriers[lastIndex];
                if (flight.Courier != null)
                {
                    Destroy(flight.Courier.gameObject);
                }
                _ambientCouriers.RemoveAt(lastIndex);
            }
            while (_ambientCouriers.Count < desiredCount)
            {
                SpawnAmbientCourier(_ambientCouriers.Count);
            }
        }

        private void SpawnAmbientCourier(int index)
        {
            Vector3 playerPosition = _player.WorldCenter;
            float angle = Random.value * Mathf.PI * 2f;
            float distance = RandomRange(
                _settings.AmbientMinimumSpawnDistance,
                _settings.AmbientMaximumSpawnDistance);
            float flightHeight = RandomRange(
                _settings.AmbientMinimumFlightHeight,
                _settings.AmbientMaximumFlightHeight);
            Vector3 start = playerPosition + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
            start.y = _world.SampleHeightAtLocal(start.x, start.z) + flightHeight;
            float cruiseSpeed = RandomRange(
                _settings.AmbientMinimumCruiseSpeed,
                _settings.AmbientMaximumCruiseSpeed);

            GameObject courierObject = new GameObject($"Ambient Neutral Delivery Courier {index + 1:00}");
            courierObject.transform.SetParent(transform, true);
            courierObject.transform.position = start;
            DynamicCourierAgent courier = courierObject.AddComponent<DynamicCourierAgent>();
            courier.Initialize(
                CourierDroneFaction.Neutral,
                _world,
                _materials,
                _settings,
                1f,
                Random.value * Mathf.PI * 2f);
            courier.SetFlightHeightAboveTerrain(flightHeight);
            courier.AttachPackage(_materials, _settings.AmbientPackageScale, _settings.AmbientPackageOffset);
            AmbientCourierFlight flight = new AmbientCourierFlight
            {
                Courier = courier,
                CruiseSpeed = cruiseSpeed,
                FlightHeight = flightHeight,
            };
            // A fresh courier starts empty and flies to a pickup landmark, exactly as the player
            // does on the first leg of a free-roam deployment.
            flight.CarryingCargo = false;
            courier.SetPackageVisible(false);
            BeginAmbientLeg(flight, start, advanceCycle: false);
            _ambientCouriers.Add(flight);
        }

        /// <summary>
        /// Routes a courier to its next landmark. Legs alternate between collecting cargo and
        /// dropping it off, and use the same authored leg distance, tolerance, widening steps, and
        /// used-landmark history the player's free-roam cycle runs on.
        /// </summary>
        private void BeginAmbientLeg(AmbientCourierFlight flight, Vector3 origin, bool advanceCycle)
        {
            if (advanceCycle)
            {
                flight.CarryingCargo = !flight.CarryingCargo;
                flight.Courier.SetPackageVisible(flight.CarryingCargo);
            }

            Vector3 destination = ResolveAmbientLandmarkDestination(flight, origin)
                ?? BuildFallbackAmbientDestination(origin, flight.FlightHeight);
            flight.PendingWaypoints.Clear();
            BuildHubAvoidingPath(origin, destination, flight.FlightHeight, flight.PendingWaypoints);
            AdvanceAmbientWaypoint(flight);
            flight.TurnaroundRemaining = RandomRange(
                _settings.AmbientMinimumTurnaroundDelay,
                _settings.AmbientMaximumTurnaroundDelay);
        }

        /// <summary>Sends the courier to the next queued hop of its current leg.</summary>
        private void AdvanceAmbientWaypoint(AmbientCourierFlight flight)
        {
            if (flight.PendingWaypoints.Count <= 0)
            {
                return;
            }

            Vector3 next = flight.PendingWaypoints[0];
            flight.PendingWaypoints.RemoveAt(0);
            flight.Courier.ConfigureRoute(next, flight.CruiseSpeed, false);
        }

        /// <summary>
        /// Fills <paramref name="path"/> with the hops that carry a courier from
        /// <paramref name="origin"/> to <paramref name="destination"/> without entering the hub's
        /// no-fly ring. Ordinary routes that never come near the hub stay a single straight hop.
        /// </summary>
        private void BuildHubAvoidingPath(
            Vector3 origin,
            Vector3 destination,
            float flightHeight,
            List<Vector3> path)
        {
            float keepOut = _settings.AmbientAvoidHub
                ? Mathf.Max(0f, _settings.AmbientHubAvoidanceRadius) +
                  Mathf.Max(0f, _settings.AmbientHubAvoidanceClearance)
                : 0f;
            if (keepOut <= 0f)
            {
                path.Add(destination);
                return;
            }

            Vector3 hubLocal = _world.LogicalToLocal(
                DesertWorldStreamer.StartingLogicalPosition.x,
                0.0,
                DesertWorldStreamer.StartingLogicalPosition.y);
            Vector2 hub = new Vector2(hubLocal.x, hubLocal.z);

            // A destination inside the ring would drag the courier back in on its final approach,
            // so it is pulled out to the ring edge before any bypass is planned.
            Vector2 end = PushOutsideHubRing(new Vector2(destination.x, destination.z), hub, keepOut);

            // A courier that spawned over the hub leaves radially first; every later hop then
            // starts from clear air, which is what keeps the bypass geometry below valid.
            Vector2 start = new Vector2(origin.x, origin.z);
            if ((start - hub).sqrMagnitude < keepOut * keepOut)
            {
                start = PushOutsideHubRing(start, hub, keepOut);
                path.Add(ToFlightPoint(start, flightHeight));
            }

            Vector2 travel = end - start;
            float travelLength = travel.magnitude;
            if (travelLength > Mathf.Epsilon)
            {
                Vector2 forward = travel / travelLength;
                Vector2 side = new Vector2(-forward.y, forward.x);
                Vector2 toHub = hub - start;
                float along = Vector2.Dot(toHub, forward);
                float lateral = Vector2.Dot(toHub, side);
                // Only a line that clips the ring between the two ends needs help; both ends are
                // already outside it by this point.
                if (Mathf.Abs(lateral) < keepOut && along > 0f && along < travelLength)
                {
                    // Skirt the ring on the side the straight line already leans towards, so the
                    // detour reads as a lane change rather than a doubling back.
                    Vector2 lateralOffset = side * (lateral > 0f ? -keepOut : keepOut);
                    // Clamping the entry and exit offsets to the space actually available keeps
                    // both connecting hops outside the ring even on short legs.
                    Vector2 entry = hub + lateralOffset - (forward * Mathf.Min(keepOut, along));
                    Vector2 exit = hub + lateralOffset + (forward * Mathf.Min(keepOut, travelLength - along));
                    path.Add(ToFlightPoint(entry, flightHeight));
                    path.Add(ToFlightPoint(exit, flightHeight));
                }
            }

            path.Add(ToFlightPoint(end, flightHeight));
        }

        private static Vector2 PushOutsideHubRing(Vector2 planar, Vector2 hub, float keepOut)
        {
            Vector2 offset = planar - hub;
            float distance = offset.magnitude;
            if (distance >= keepOut)
            {
                return planar;
            }

            Vector2 direction = distance > Mathf.Epsilon ? offset / distance : Vector2.right;
            return hub + (direction * keepOut);
        }

        private Vector3 ToFlightPoint(Vector2 planar, float flightHeight)
        {
            return new Vector3(
                planar.x,
                _world.SampleHeightAtLocal(planar.x, planar.y) + flightHeight,
                planar.y);
        }

        private Vector3? ResolveAmbientLandmarkDestination(AmbientCourierFlight flight, Vector3 origin)
        {
            if (_landmarks == null || _freeRoamSettings == null || !_freeRoamSettings.Enabled)
            {
                return null;
            }

            _ambientRandom ??= new System.Random(unchecked(_world.WorldSeed ^ 0x2C71B4));
            LogicalPosition logicalOrigin = new LogicalPosition(
                _world.OriginOffsetX + origin.x,
                _world.OriginOffsetZ + origin.z);
            DuneLandmarkPlacementRecord record = _landmarks.ResolveRandomWorldLandmarkAtDistance(
                logicalOrigin,
                _freeRoamSettings.LegDistance,
                _freeRoamSettings.LegDistanceTolerance,
                _freeRoamSettings.LegDistanceWideningSteps,
                _ambientRandom,
                flight.UsedLandmarkIds);
            if (record == null)
            {
                // Long-lived couriers consume every landmark around them. Forgetting the history
                // keeps them running the cycle instead of drifting off on fallback headings.
                flight.UsedLandmarkIds.Clear();
                record = _landmarks.ResolveRandomWorldLandmarkAtDistance(
                    logicalOrigin,
                    _freeRoamSettings.LegDistance,
                    _freeRoamSettings.LegDistanceTolerance,
                    _freeRoamSettings.LegDistanceWideningSteps,
                    _ambientRandom,
                    flight.UsedLandmarkIds);
            }
            if (record == null)
            {
                return null;
            }

            flight.UsedLandmarkIds.Add(record.PersistentId);
            Vector3 destination = _world.LogicalToLocal(record.LogicalPosition.X, 0.0, record.LogicalPosition.Z);
            destination.y = _world.SampleHeightAtLocal(destination.x, destination.z) + flight.FlightHeight;
            return destination;
        }

        private Vector3 BuildFallbackAmbientDestination(Vector3 start, float flightHeight)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float distance = RandomRange(
                _settings.AmbientMinimumRouteDistance,
                _settings.AmbientMaximumRouteDistance);
            Vector3 destination = start + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
            destination.y = _world.SampleHeightAtLocal(destination.x, destination.z) + flightHeight;
            return destination;
        }

        private static float RandomRange(float a, float b)
        {
            return Random.Range(Mathf.Min(a, b), Mathf.Max(a, b));
        }

        private void SpawnConvoyEvent()
        {
            ActiveEventType = DynamicCourierEventType.MovingConvoy;
            _phase = DynamicCourierEventPhase.Active;
            _phaseChangedAt = Time.unscaledTime;
            _hullNormalized = 1f;
            BuildRoute(out Vector3 start, out _eventDestination);
            _primaryCourier = SpawnCourier(
                "Neutral Cargo Carrier",
                CourierDroneFaction.Neutral,
                start,
                _eventDestination,
                _settings.CruiseSpeed,
                1f,
                false);
            _objectiveTarget = _primaryCourier.transform;

            Vector3 forward = Vector3.ProjectOnPlane(_eventDestination - start, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < _settings.ConvoyEscortCount; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                float rank = (i / 2) + 1f;
                Vector3 offset = right * side * rank * _settings.ConvoyEscortSpacing;
                SpawnCourier(
                    $"Neutral Convoy Escort {i + 1}",
                    CourierDroneFaction.Neutral,
                    start + offset,
                    _eventDestination + offset,
                    _settings.CruiseSpeed,
                    1f,
                    false);
            }
            SpawnAttackers(_primaryCourier, _settings.ConvoyAttackerCount);
            _eventTitle = "CONVOY UNDER ATTACK";
            _eventStatus = "Protect the cargo carrier until it clears the route.";
            _eventColor = _settings.ConvoyHudColor;
        }

        private void UpdateConvoyEvent()
        {
            if (_primaryCourier == null || !_primaryCourier.IsAlive)
            {
                FinishEvent(false, "CARGO CARRIER DESTROYED", 0);
                return;
            }

            // The hull reads on the meter, so the status line carries what the meter cannot:
            // how many raiders are still up and how much route is left to survive.
            _hullNormalized = _primaryCourier.NormalizedHealth;
            _eventStatus = string.Format(
                _settings.HudActiveStatusFormat,
                _attackers.Count,
                HorizontalDistance(_primaryCourier.transform.position, _eventDestination));
            if (_primaryCourier.ReachedDestination)
            {
                float rewardFraction = Mathf.Lerp(
                    _settings.ConvoyMinimumRewardFraction,
                    1f,
                    _primaryCourier.NormalizedHealth);
                int reward = Mathf.RoundToInt(_settings.ConvoyMaximumReward * rewardFraction);
                FinishEvent(true, "CONVOY ESCORT COMPLETE", reward);
            }
        }

        private void BuildRoute(out Vector3 start, out Vector3 destination)
        {
            Vector3 playerPosition = _player.WorldCenter;
            float spawnAngle = Random.value * Mathf.PI * 2f;
            float spawnDistance = Random.Range(
                Mathf.Min(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance));
            Vector3 spawnDirection = new Vector3(Mathf.Cos(spawnAngle), 0f, Mathf.Sin(spawnAngle));
            start = playerPosition + (spawnDirection * spawnDistance);
            start.y = _world.SampleHeightAtLocal(start.x, start.z) + _settings.FlightHeightAboveTerrain;

            float routeTurn = Random.Range(-Mathf.PI, Mathf.PI);
            Vector3 routeDirection = Quaternion.AngleAxis(routeTurn * Mathf.Rad2Deg, Vector3.up) * spawnDirection;
            float routeDistance = Random.Range(
                Mathf.Min(_settings.MinimumRouteDistance, _settings.MaximumRouteDistance),
                Mathf.Max(_settings.MinimumRouteDistance, _settings.MaximumRouteDistance));
            destination = start + (routeDirection * routeDistance);
            destination.y = _world.SampleHeightAtLocal(destination.x, destination.z) + _settings.FlightHeightAboveTerrain;
        }

        private DynamicCourierAgent SpawnCourier(
            string objectName,
            CourierDroneFaction faction,
            Vector3 start,
            Vector3 destination,
            float speed,
            float healthFraction,
            bool paused)
        {
            GameObject courierObject = new GameObject(objectName);
            courierObject.transform.SetParent(transform, true);
            courierObject.transform.position = start;
            DynamicCourierAgent courier = courierObject.AddComponent<DynamicCourierAgent>();
            courier.Initialize(faction, _world, _materials, _settings, healthFraction, Random.value * Mathf.PI * 2f);
            courier.ConfigureRoute(destination, speed, paused);
            _couriers.Add(courier);
            return courier;
        }

        private void SpawnAttackers(DynamicCourierAgent target, int count)
        {
            count = Mathf.Max(1, Mathf.CeilToInt(
                count * DuneVectorContractRisk.EnemySpawnMultiplier));
            for (int i = 0; i < count; i++)
            {
                float phase = (Mathf.PI * 2f * i) / Mathf.Max(1, count);
                Vector3 offset = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase)) * _settings.AttackerOrbitRadius;
                GameObject attackerObject = new GameObject($"Courier Event Attacker {i + 1}");
                attackerObject.transform.SetParent(transform, true);
                attackerObject.transform.position = target.transform.position + offset + (Vector3.up * _settings.AttackerHeightOffset);
                DynamicCourierAttacker attacker = attackerObject.AddComponent<DynamicCourierAttacker>();
                attacker.Initialize(target, _wallet, _materials, _settings, phase);
                _attackers.Add(attacker);
            }
        }

        private void FinishEvent(bool success, string result, int reward)
        {
            if (_phase == DynamicCourierEventPhase.Result)
            {
                return;
            }

            if (success && reward > 0)
            {
                _wallet?.AddGold(reward);
            }
            _phase = DynamicCourierEventPhase.Result;
            _phaseChangedAt = Time.unscaledTime;
            _eventTimer = _settings.ResultDisplayDuration;
            // The outcome takes over the headline; leaving CONVOY UNDER ATTACK above a success
            // line read as a contradiction.
            _eventTitle = result;
            _eventStatus = success
                ? (reward > 0 ? string.Format(_settings.HudRewardStatusFormat, reward) : string.Empty)
                : _settings.HudFailureStatusLabel;
            _eventColor = success ? _settings.SuccessHudColor : _settings.FailureHudColor;
            _objectiveTarget = null;
            DestroyAttackers();
        }

        private void ScheduleNextEvent()
        {
            float minimum = Mathf.Min(_settings.MinimumEventInterval, _settings.MaximumEventInterval);
            float maximum = Mathf.Max(_settings.MinimumEventInterval, _settings.MaximumEventInterval);
            _eventTimer = Random.Range(minimum, maximum);
        }

        private void DestroyAttackers()
        {
            for (int i = 0; i < _attackers.Count; i++)
            {
                if (_attackers[i] != null)
                {
                    Destroy(_attackers[i].gameObject);
                }
            }
            _attackers.Clear();
        }

        private void PruneRuntimeObjects()
        {
            for (int i = _couriers.Count - 1; i >= 0; i--)
            {
                if (_couriers[i] == null)
                {
                    _couriers.RemoveAt(i);
                }
            }
            for (int i = _attackers.Count - 1; i >= 0; i--)
            {
                if (_attackers[i] == null)
                {
                    _attackers.RemoveAt(i);
                }
            }
        }

        private void ClearEvent()
        {
            for (int i = 0; i < _couriers.Count; i++)
            {
                if (_couriers[i] != null)
                {
                    Destroy(_couriers[i].gameObject);
                }
            }
            DestroyAttackers();
            _couriers.Clear();
            _primaryCourier = null;
            _objectiveTarget = null;
            ActiveEventType = DynamicCourierEventType.None;
            _phase = DynamicCourierEventPhase.Inactive;
            _eventTitle = string.Empty;
            _eventStatus = string.Empty;
        }

        private void ClearAmbientCouriers()
        {
            for (int i = 0; i < _ambientCouriers.Count; i++)
            {
                if (_ambientCouriers[i].Courier != null)
                {
                    Destroy(_ambientCouriers[i].Courier.gameObject);
                }
            }
            _ambientCouriers.Clear();
        }

        private void HandleWorldShift(Vector3 worldShift)
        {
            _eventDestination += worldShift;
            for (int i = 0; i < _couriers.Count; i++)
            {
                _couriers[i]?.HandleWorldShift(worldShift);
            }
            for (int i = 0; i < _attackers.Count; i++)
            {
                _attackers[i]?.HandleWorldShift(worldShift);
            }
            for (int i = 0; i < _ambientCouriers.Count; i++)
            {
                AmbientCourierFlight flight = _ambientCouriers[i];
                flight.Courier?.HandleWorldShift(worldShift);
                // Queued bypass hops are stored in local space, so they travel with the origin
                // rebase exactly as the courier's active destination does.
                for (int waypoint = 0; waypoint < flight.PendingWaypoints.Count; waypoint++)
                {
                    flight.PendingWaypoints[waypoint] += worldShift;
                }
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private void OnGUI()
        {
            // This overlay only draws; it owns no controls and mutates no state. Running the
            // layout pass would repeat every measurement for nothing, so only Repaint does work.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (_settings == null || _phase == DynamicCourierEventPhase.Inactive ||
                DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }

            EnsureGuiStyles();
            // GetHudPanelRect reports scaled screen space; draw at authored size under a uniform
            // GUI scale so the box and the text shrink together.
            float hudScale = HudScale;
            Rect scaledPanel = GetHudPanelRect();
            Matrix4x4 previousHudMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(hudScale, hudScale, 1f));
            Rect panel = new Rect(
                scaledPanel.x / hudScale,
                scaledPanel.y / hudScale,
                scaledPanel.width / hudScale,
                scaledPanel.height / hudScale);
            DrawEventPanel(panel);
            GUI.matrix = previousHudMatrix;

            DrawObjectiveMarker();
        }

        /// <summary>
        /// Draws the event overlay in authored space; the caller has already applied the HUD
        /// scale to <see cref="GUI.matrix"/>, so every measurement below is a raw tuning value.
        /// </summary>
        private void DrawEventPanel(Rect panel)
        {
            float sincePhase = Time.unscaledTime - _phaseChangedAt;
            float intro = _settings.HudIntroDuration <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sincePhase / _settings.HudIntroDuration));
            float outro = _phase == DynamicCourierEventPhase.Result && _settings.HudOutroDuration > 0f
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_eventTimer / _settings.HudOutroDuration))
                : 1f;
            float alpha = intro * outro;
            if (alpha <= 0.001f)
            {
                return;
            }

            // Slide in from the screen edge so a new event reads as arriving rather than popping.
            panel.x -= _settings.HudIntroSlideDistance * (1f - intro);

            Color accent = _eventColor;
            if (_phase == DynamicCourierEventPhase.Active && _settings.HudActivePulseSpeed > 0f)
            {
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * _settings.HudActivePulseSpeed));
                accent = Color.Lerp(
                    accent,
                    Color.white,
                    pulse * _settings.HudActivePulseAmount * 0.35f);
                accent.a *= Mathf.Lerp(1f - _settings.HudActivePulseAmount, 1f, pulse);
            }

            DuneVectorHudChrome.DrawSoftShadow(
                panel,
                Fade(_settings.HudShadowColor, alpha),
                _settings.HudShadowOffset,
                6f);

            Color border = Color.Lerp(_settings.HudBorderColor, _eventColor, 0.35f);
            border.a = _settings.HudBorderColor.a;
            DuneVectorHudChrome.DrawGlassPanel(
                panel,
                Fade(_settings.HudPanelColor, alpha),
                Fade(border, alpha),
                _settings.HudBorderThickness,
                1f);
            DuneVectorHudChrome.DrawAccentRail(
                panel,
                Fade(accent, alpha),
                _settings.HudAccentWidth,
                26f);

            Rect topRule = new Rect(
                panel.x + _settings.HudAccentWidth,
                panel.y,
                panel.width - _settings.HudAccentWidth,
                _settings.HudTopRuleHeight);
            DuneVectorHudChrome.DrawRect(topRule, Fade(accent, alpha * _settings.HudTopRuleOpacity));
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(topRule.x, topRule.yMax, topRule.width, 9f),
                Fade(accent, alpha * _settings.HudTopRuleOpacity * 0.4f),
                true);
            DuneVectorHudChrome.DrawCornerBrackets(
                panel,
                Fade(accent, alpha * 0.6f),
                _settings.HudCornerBracketLength,
                _settings.HudBorderThickness);

            float padding = _settings.HudPadding;
            float contentX = panel.x + padding + _settings.HudAccentWidth;
            float contentWidth = panel.width - (padding * 2f) - _settings.HudAccentWidth;
            Vector2 textShadowOffset = new Vector2(1f, 1f);
            Color textShadow = new Color(0f, 0f, 0f, 0.55f * alpha);

            Rect title = new Rect(contentX, panel.y + padding, contentWidth, _settings.HudTitleHeight);
            DuneVectorHudChrome.DrawGlowLabel(
                title,
                _eventTitle,
                _titleStyle,
                Fade(_eventColor, alpha),
                Fade(_eventColor, alpha * _settings.HudTitleGlowOpacity),
                _settings.HudTitleGlowRadius,
                textShadow,
                textShadowOffset);

            Rect body = new Rect(contentX, title.yMax, contentWidth, _settings.HudLineHeight);
            DuneVectorHudChrome.DrawLabel(
                body,
                _eventStatus,
                _bodyStyle,
                Fade(Color.white, alpha),
                textShadow,
                textShadowOffset);

            bool active = _phase == DynamicCourierEventPhase.Active;
            float meterY = panel.yMax - _settings.HudMeterBottomPadding - _settings.HudMeterHeight;
            if (active)
            {
                Rect meterLabel = new Rect(
                    contentX,
                    meterY - _settings.HudMeterLabelHeight,
                    contentWidth,
                    _settings.HudMeterLabelHeight);
                DuneVectorHudChrome.DrawLabel(
                    meterLabel,
                    _settings.HudHullMeterLabel,
                    _meterLabelStyle,
                    Fade(Color.white, alpha),
                    textShadow,
                    textShadowOffset);
                DuneVectorHudChrome.DrawLabel(
                    meterLabel,
                    string.Format(_settings.HudHullMeterFormat, Mathf.CeilToInt(_hullNormalized * 100f)),
                    _meterValueStyle,
                    Fade(Color.white, alpha),
                    textShadow,
                    textShadowOffset);
            }

            // Active: carrier hull. Result: the display timer draining, so the panel visibly
            // announces its own dismissal instead of vanishing mid-read.
            float normalized = active
                ? _hullNormalized
                : Mathf.Clamp01(_eventTimer / Mathf.Max(0.01f, _settings.ResultDisplayDuration));
            Color meterAccent = active
                ? Color.Lerp(_settings.FailureHudColor, accent, Mathf.Clamp01(_hullNormalized))
                : accent;
            DuneVectorHudChrome.DrawMeter(
                new Rect(contentX, meterY, contentWidth, _settings.HudMeterHeight),
                normalized,
                Fade(meterAccent, alpha),
                Fade(_settings.HudTrackColor, alpha),
                _settings.HudMeterInset,
                _settings.HudMeterDivisionCount,
                Fade(_settings.HudMeterDivisionColor, alpha),
                _settings.HudMeterDivisionWidth,
                1f);
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        public bool TryGetVisiblePanelRect(out Rect panel)
        {
            if (_settings != null && _phase != DynamicCourierEventPhase.Inactive &&
                !DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                panel = GetHudPanelRect();
                return true;
            }
            panel = default;
            return false;
        }

        private float HudScale => _settings == null ? 1f : Mathf.Clamp(_settings.HudScale, 0.4f, 2f);

        private Rect GetHudPanelRect()
        {
            float hudScale = HudScale;
            float width = _settings.HudWidth * hudScale;
            float panelTop = _settings.HudTop;
            if (DuneVectorDesertAtlas.TryGetVisibleHudRect(out Rect atlasPanel) &&
                _settings.HudLeft < atlasPanel.xMax &&
                _settings.HudLeft + width > atlasPanel.x)
            {
                panelTop = Mathf.Max(panelTop, atlasPanel.yMax + _settings.HudOtherPanelGap);
            }
            return new Rect(_settings.HudLeft, panelTop, width, _settings.HudHeight * hudScale);
        }

        private void EnsureGuiStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _settings.HudTitleFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = _settings.HudTextColor },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _settings.HudBodyFontSize,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = _settings.HudTextColor },
            };
            _meterLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _settings.HudMeterLabelFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = _settings.HudMutedColor },
            };
            _meterValueStyle = new GUIStyle(_meterLabelStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };
            _markerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _settings.ObjectiveMarkerFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = _settings.HudTextColor },
            };
        }

        private void DrawObjectiveMarker()
        {
            Vector3 targetPosition;
            if (_objectiveTarget != null)
            {
                targetPosition = _objectiveTarget.position;
            }
            else
            {
                return;
            }

            Vector3 screen = _camera.WorldToScreenPoint(targetPosition);
            if (screen.z < 0f)
            {
                screen.x = Screen.width - screen.x;
                screen.y = Screen.height - screen.y;
            }
            float guiY = Screen.height - screen.y;
            float halfMarker = _settings.ObjectiveMarkerSize * 0.5f;
            float padding = _settings.ObjectiveMarkerEdgePadding + halfMarker;
            float x = Mathf.Clamp(screen.x, padding, Screen.width - padding);
            float y = Mathf.Clamp(guiY, padding, Screen.height - padding);

            Color previousColor = GUI.color;
            GUI.color = _eventColor;
            GUI.DrawTexture(
                new Rect(x - halfMarker, y - halfMarker, _settings.ObjectiveMarkerSize, _settings.ObjectiveMarkerSize),
                Texture2D.whiteTexture);
            GUI.color = previousColor;

            float distance = Vector3.Distance(_player.WorldCenter, targetPosition);
            Rect label = new Rect(
                x - (_settings.ObjectiveMarkerLabelWidth * 0.5f),
                y + halfMarker,
                _settings.ObjectiveMarkerLabelWidth,
                _settings.ObjectiveMarkerLabelHeight);
            GUI.Label(label, $"{Mathf.CeilToInt(distance)}m", _markerStyle);
        }
    }
}
