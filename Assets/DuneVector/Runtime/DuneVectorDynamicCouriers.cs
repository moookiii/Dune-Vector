using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum DynamicCourierEventType
    {
        None,
        DistressedCourier,
        CourierRace,
        MovingConvoy,
    }

    internal enum DynamicCourierEventPhase
    {
        Inactive,
        Offered,
        Active,
        Result,
    }

    [DisallowMultipleComponent]
    public sealed class DynamicCourierAgent : MonoBehaviour
    {
        public CourierDroneFaction Faction { get; private set; }
        public bool IsAlive => _currentHealth > 0f;
        public bool ReachedDestination { get; private set; }
        public float CurrentHealth => _currentHealth;
        public float MaximumHealth => _maximumHealth;
        public float NormalizedHealth => _maximumHealth > 0f ? Mathf.Clamp01(_currentHealth / _maximumHealth) : 0f;

        private DesertWorldStreamer _world;
        private DynamicCourierTuning _settings;
        private Transform _cachedTransform;
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
            Transform package = DuneVectorVisuals.CreatePackageVisual(_cachedTransform, materials, scale);
            package.localPosition = localOffset;
        }

        public void ConfigureRoute(Vector3 destination, float speed, bool paused)
        {
            _destination = destination;
            _speed = Mathf.Max(0f, speed);
            _paused = paused;
            ReachedDestination = false;
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
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
            _shotTimer = Mathf.Max(0f, _shotTimer - deltaTime);
            Vector3 targetPosition = _target.transform.position;
            Vector3 orbitOffset = new Vector3(
                Mathf.Cos(_orbitPhase),
                0f,
                Mathf.Sin(_orbitPhase)) * _settings.AttackerOrbitRadius;
            Vector3 desiredPosition = targetPosition + orbitOffset + (Vector3.up * _settings.AttackerHeightOffset);
            _cachedTransform.position = Vector3.MoveTowards(
                _cachedTransform.position,
                desiredPosition,
                _settings.AttackerSpeed * deltaTime);

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
                _target.TakeDamage(_settings.AttackerShotDamage);
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
        private DynamicCourierEventPhase _phase;
        private DynamicCourierAgent _primaryCourier;
        private Vector3 _eventDestination;
        private Transform _objectiveTarget;
        private float _eventTimer;
        private bool _wasGameplayAvailable;
        private string _eventTitle;
        private string _eventStatus;
        private Color _eventColor;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _markerStyle;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DynamicCourierTuning settings,
            DuneVectorCourierGame courierGame)
        {
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

            bool gameplayAvailable = IsGameplayAvailable();
            if (!gameplayAvailable)
            {
                if (_wasGameplayAvailable)
                {
                    ClearEvent();
                    ClearAmbientCouriers();
                }
                _wasGameplayAvailable = false;
                return;
            }

            if (!_wasGameplayAvailable)
            {
                _wasGameplayAvailable = true;
                _eventTimer = _settings.InitialEventDelay;
            }

            PruneRuntimeObjects();
            UpdateAmbientCouriers(Time.deltaTime);
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
                case DynamicCourierEventType.DistressedCourier:
                    UpdateDistressEvent();
                    break;
                case DynamicCourierEventType.CourierRace:
                    UpdateRaceEvent();
                    break;
                case DynamicCourierEventType.MovingConvoy:
                    UpdateConvoyEvent();
                    break;
            }
        }

        private bool IsGameplayAvailable()
        {
            if (_courierGame == null)
            {
                return true;
            }
            return _courierGame.State == CourierRunState.FindPackage ||
                   _courierGame.State == CourierRunState.Delivering;
        }

        private void SpawnNextEvent()
        {
            float distressWeight = Mathf.Max(0f, _settings.DistressEventWeight);
            float raceWeight = Mathf.Max(0f, _settings.RaceEventWeight);
            float convoyWeight = Mathf.Max(0f, _settings.ConvoyEventWeight);
            float totalWeight = distressWeight + raceWeight + convoyWeight;
            if (totalWeight <= Mathf.Epsilon)
            {
                ScheduleNextEvent();
                return;
            }

            float selection = Random.value * totalWeight;
            if (selection < distressWeight)
            {
                SpawnDistressEvent();
            }
            else if (selection < distressWeight + raceWeight)
            {
                SpawnRaceEvent();
            }
            else
            {
                SpawnConvoyEvent();
            }
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

                flight.TurnaroundRemaining -= deltaTime;
                if (flight.TurnaroundRemaining > 0f)
                {
                    continue;
                }

                Vector3 destination = BuildAmbientDestination(flight.Courier.transform.position, flight.FlightHeight);
                flight.Courier.ConfigureRoute(destination, flight.CruiseSpeed, false);
                flight.TurnaroundRemaining = RandomRange(
                    _settings.AmbientMinimumTurnaroundDelay,
                    _settings.AmbientMaximumTurnaroundDelay);
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
            Vector3 destination = BuildAmbientDestination(start, flightHeight);
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
            courier.ConfigureRoute(destination, cruiseSpeed, false);
            _ambientCouriers.Add(new AmbientCourierFlight
            {
                Courier = courier,
                CruiseSpeed = cruiseSpeed,
                FlightHeight = flightHeight,
                TurnaroundRemaining = RandomRange(
                    _settings.AmbientMinimumTurnaroundDelay,
                    _settings.AmbientMaximumTurnaroundDelay),
            });
        }

        private Vector3 BuildAmbientDestination(Vector3 start, float flightHeight)
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

        private void SpawnDistressEvent()
        {
            ActiveEventType = DynamicCourierEventType.DistressedCourier;
            _phase = DynamicCourierEventPhase.Active;
            BuildRoute(out Vector3 start, out _eventDestination);
            _primaryCourier = SpawnCourier(
                "Distressed Neutral Courier",
                CourierDroneFaction.Neutral,
                start,
                _eventDestination,
                _settings.CruiseSpeed,
                _settings.DistressedStartingHealthFraction,
                false);
            _objectiveTarget = _primaryCourier.transform;
            SpawnAttackers(_primaryCourier, _settings.DistressAttackerCount);
            _eventTitle = "DISTRESS SIGNAL";
            _eventStatus = "Destroy the attackers, then escort the courier.";
            _eventColor = _settings.DistressHudColor;
        }

        private void UpdateDistressEvent()
        {
            if (_primaryCourier == null || !_primaryCourier.IsAlive)
            {
                FinishEvent(false, "DISTRESS SIGNAL LOST", 0);
                return;
            }

            if (_attackers.Count > 0)
            {
                _eventStatus = $"ATTACKERS  {_attackers.Count}  //  COURIER HULL  {Mathf.CeilToInt(_primaryCourier.NormalizedHealth * 100f)}%";
                return;
            }

            _eventStatus = "THREATS CLEARED  //  ESCORT COURIER";
            if (_primaryCourier.ReachedDestination)
            {
                FinishEvent(true, "COURIER RESCUED", _settings.DistressRescueReward);
            }
        }

        private void SpawnRaceEvent()
        {
            ActiveEventType = DynamicCourierEventType.CourierRace;
            _phase = DynamicCourierEventPhase.Offered;
            BuildRoute(out Vector3 start, out _eventDestination);
            _primaryCourier = SpawnCourier(
                "Rival Courier",
                CourierDroneFaction.Rival,
                start,
                _eventDestination,
                _settings.RivalRaceSpeed,
                1f,
                true);
            _objectiveTarget = _primaryCourier.transform;
            _eventTitle = "COURIER CHALLENGE AVAILABLE";
            _eventStatus = "Approach the rival to accept an open-route race.";
            _eventColor = _settings.RaceHudColor;
        }

        private void UpdateRaceEvent()
        {
            if (_primaryCourier == null)
            {
                FinishEvent(false, "RIVAL COURIER LOST", 0);
                return;
            }

            if (_phase == DynamicCourierEventPhase.Offered)
            {
                float distance = Vector3.Distance(_player.WorldCenter, _primaryCourier.transform.position);
                if (distance > _settings.OfferedEventDespawnDistance)
                {
                    ClearEvent();
                    ScheduleNextEvent();
                    return;
                }

                if (distance <= _settings.ChallengeAcceptDistance)
                {
                    _eventStatus = $"PRESS {_settings.ChallengeAcceptKey.ToString().ToUpperInvariant()} TO RACE";
                    if (Keyboard.current != null && _settings.ChallengeAcceptKey != Key.None)
                    {
                        var acceptControl = Keyboard.current[_settings.ChallengeAcceptKey];
                        if (acceptControl != null && acceptControl.wasPressedThisFrame)
                        {
                            _phase = DynamicCourierEventPhase.Active;
                            _primaryCourier.SetPaused(false);
                            _objectiveTarget = null;
                            _eventStatus = "FIRST COURIER TO THE RELAY WINS";
                        }
                    }
                }
                else
                {
                    _eventStatus = "Approach the rival to accept an open-route race.";
                }
                return;
            }

            _objectiveTarget = null;
            float playerDistance = HorizontalDistance(_player.WorldCenter, _eventDestination);
            if (playerDistance <= _settings.DestinationRadius)
            {
                FinishEvent(true, "COURIER CHALLENGE WON", _settings.RaceWinnerReward);
            }
            else if (_primaryCourier.ReachedDestination)
            {
                FinishEvent(false, "RIVAL REACHED THE RELAY FIRST", 0);
            }
            else
            {
                _eventStatus = $"RELAY  {Mathf.CeilToInt(playerDistance)}m  //  RIVAL  {Mathf.CeilToInt(HorizontalDistance(_primaryCourier.transform.position, _eventDestination))}m";
            }
        }

        private void SpawnConvoyEvent()
        {
            ActiveEventType = DynamicCourierEventType.MovingConvoy;
            _phase = DynamicCourierEventPhase.Active;
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

            _eventStatus = $"CARRIER HULL  {Mathf.CeilToInt(_primaryCourier.NormalizedHealth * 100f)}%  //  HOSTILES  {_attackers.Count}";
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
            _eventTimer = _settings.ResultDisplayDuration;
            _eventStatus = success && reward > 0 ? $"{result}  //  +{reward:N0} GOLD" : result;
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
                _ambientCouriers[i].Courier?.HandleWorldShift(worldShift);
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private void OnGUI()
        {
            if (_settings == null || _phase == DynamicCourierEventPhase.Inactive ||
                DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }

            EnsureGuiStyles();
            Rect panel = new Rect(_settings.HudLeft, _settings.HudTop, _settings.HudWidth, _settings.HudHeight);
            Color previousColor = GUI.color;
            GUI.color = _settings.HudPanelColor;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previousColor;

            _titleStyle.normal.textColor = _eventColor;
            Rect title = new Rect(
                panel.x + _settings.HudPadding,
                panel.y + _settings.HudPadding,
                panel.width - (_settings.HudPadding * 2f),
                _settings.HudTitleHeight);
            GUI.Label(title, _eventTitle, _titleStyle);
            Rect body = new Rect(
                title.x,
                title.yMax,
                title.width,
                Mathf.Max(_settings.HudLineHeight, panel.yMax - title.yMax - _settings.HudPadding));
            GUI.Label(body, _eventStatus, _bodyStyle);

            DrawObjectiveMarker();
        }

        public bool TryGetVisiblePanelRect(out Rect panel)
        {
            if (_settings != null && _phase != DynamicCourierEventPhase.Inactive &&
                !DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                panel = new Rect(_settings.HudLeft, _settings.HudTop, _settings.HudWidth, _settings.HudHeight);
                return true;
            }
            panel = default;
            return false;
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
            else if (ActiveEventType == DynamicCourierEventType.CourierRace && _phase == DynamicCourierEventPhase.Active)
            {
                targetPosition = _eventDestination;
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
