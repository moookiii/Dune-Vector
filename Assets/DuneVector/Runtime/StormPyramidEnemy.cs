using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum StormPyramidState
    {
        IdleHovering,
        TrackingPlayer,
        ChargingAttack,
        FiringLightning,
        Cooldown,
        TriggeredWindUp,
        WarningTelegraph,
    }

    public enum StormLightningAttackType
    {
        GroundStrike,
        PlayerStrike,
    }

    public readonly struct StormLightningTarget
    {
        public readonly StormLightningAttackType Type;
        public readonly Vector3 Position;

        public StormLightningTarget(StormLightningAttackType type, Vector3 position)
        {
            Type = type;
            Position = position;
        }
    }

    public readonly struct StormPyramidThreatWarning
    {
        public readonly StormLightningAttackType Type;
        public readonly Vector3 TargetPosition;
        public readonly float SecondsRemaining;
        public readonly float ChargeNormalized;
        public readonly Vector3 ThreatOrigin;
        public readonly float ThreatRange;

        public StormPyramidThreatWarning(
            StormLightningAttackType type,
            Vector3 targetPosition,
            float secondsRemaining,
            float chargeNormalized,
            Vector3 threatOrigin = default,
            float threatRange = 0f)
        {
            Type = type;
            TargetPosition = targetPosition;
            SecondsRemaining = secondsRemaining;
            ChargeNormalized = chargeNormalized;
            ThreatOrigin = threatOrigin;
            ThreatRange = threatRange;
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidEnemy : MonoBehaviour
    {
        public StormPyramidState CurrentState { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private StormPyramidTuning _settings;
        private StormPyramidMovement _movement;
        private StormPyramidTargeting _targeting;
        private StormPyramidLightningAttack _lightning;
        private GroundExploderProximity _proximity;
        private DuneVectorMaterials _materials;
        private GroundExploderTuning _explosionSettings;
        private EnemyCombatTarget _combatTarget;
        private Transform _visual;
        private Transform _core;
        private Transform _counterRotator;
        private StormLightningTarget _trackedTarget;
        private float _stateTime;
        private float _attackTimer;
        private int _identity;
        private int _attackSequence;
        private bool _gameplayActive = true;
        private bool _proximityDetonationActive = true;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            StormPyramidTuning settings,
            GroundExploderTuning explosionSettings,
            int identity)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _settings = settings;
            _materials = materials;
            _explosionSettings = explosionSettings;
            _identity = identity;
            _attackSequence = 0;

            _visual = DuneVectorVisuals.CreateStormPyramidVisual(transform, materials, settings);
            _core = _visual.Find("Storm Core");
            _counterRotator = _visual.Find("Counter Rotator");
            Transform halo = _visual.Find("Charge Halo");
            Transform lightningOrigin = _visual.Find("Lightning Origin");

            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.MaximumHealth);
            _combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            _combatTarget.Initialize(enemyHealth, settings.VisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, player != null ? player.GetComponent<DroneGoldWallet>() : null, settings.GoldReward);

            _proximity = gameObject.AddComponent<GroundExploderProximity>();
            _proximity.BindTarget(player);

            _movement = gameObject.AddComponent<StormPyramidMovement>();
            _movement.Initialize(player, world, settings, identity);

            _targeting = gameObject.AddComponent<StormPyramidTargeting>();
            _targeting.Initialize(world);

            StormPyramidLightningDamage damage = gameObject.AddComponent<StormPyramidLightningDamage>();
            damage.Initialize(player, playerHealth);

            _lightning = gameObject.AddComponent<StormPyramidLightningAttack>();
            _lightning.Initialize(
                transform,
                lightningOrigin,
                _core,
                halo,
                materials,
                settings,
                damage,
                world,
                identity);

            _attackTimer = GetStaggeredAttackDelay();
            SetState(StormPyramidState.IdleHovering);
            DuneVectorPhotographableMarker.Register(
                gameObject,
                DuneVectorCompendiumSubjectIds.StormPyramid,
                PhotographableSubjectCategory.Enemy);
        }

        private void Update()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                _lightning?.CancelAttack();
                return;
            }

            float deltaTime = Time.deltaTime;
            if (!_gameplayActive)
            {
                _movement.Tick(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }

            // The triggered wind-up is a telegraph, not a cooldown, so risk never shortens it.
            _stateTime += deltaTime;

            if (CurrentState == StormPyramidState.TriggeredWindUp)
            {
                UpdateDetonationWindUp();
                UpdatePresentation(deltaTime);
                return;
            }

            if (_proximityDetonationActive && _proximity.IsTargetInside(
                _explosionSettings.DetectionRadius
                * Mathf.Max(0.1f, _settings.ProximityDetectionRadiusMultiplier)))
            {
                BeginDetonationWindUp();
                UpdatePresentation(deltaTime);
                return;
            }

            bool mayDrift = CurrentState == StormPyramidState.IdleHovering
                || CurrentState == StormPyramidState.Cooldown;
            if (mayDrift)
            {
                _movement.Tick(deltaTime);
            }

            bool mayReposition = CurrentState != StormPyramidState.WarningTelegraph
                && CurrentState != StormPyramidState.ChargingAttack
                && CurrentState != StormPyramidState.FiringLightning
                && CurrentState != StormPyramidState.TriggeredWindUp;
            if (mayReposition && _movement.HorizontalDistanceFromPlayer > ResolveRepositionDistance())
            {
                RepositionNearPlayer();
            }

            switch (CurrentState)
            {
                case StormPyramidState.IdleHovering:
                    UpdateIdle(deltaTime);
                    break;
                case StormPyramidState.TrackingPlayer:
                    BeginGroundStrike();
                    break;
                case StormPyramidState.WarningTelegraph:
                    UpdateWarningTelegraph(deltaTime);
                    break;
                case StormPyramidState.ChargingAttack:
                    UpdateCharging(deltaTime);
                    break;
                case StormPyramidState.FiringLightning:
                    UpdateFiring(deltaTime);
                    break;
                case StormPyramidState.Cooldown:
                    UpdateCooldown();
                    break;
                case StormPyramidState.TriggeredWindUp:
                    break;
            }

            UpdatePresentation(deltaTime);
        }

        private void UpdateIdle(float deltaTime)
        {
            _attackTimer = Mathf.Max(0f, _attackTimer - deltaTime);
            if (_attackTimer <= 0f)
            {
                BeginGroundStrike();
            }
        }

        private void BeginGroundStrike()
        {
            _trackedTarget = new StormLightningTarget(
                StormLightningAttackType.GroundStrike,
                _targeting.GetGroundPointBelow(transform.position));
            if (_lightning.BeginWarning(_trackedTarget))
            {
                SetState(StormPyramidState.WarningTelegraph);
                return;
            }

            _lightning.BeginCharge(_trackedTarget);
            SetState(StormPyramidState.ChargingAttack);
        }

        private void UpdateWarningTelegraph(float deltaTime)
        {
            FacePosition(_lightning.TargetPosition, 9f);
            if (_lightning.TickWarning(deltaTime))
            {
                _lightning.BeginCharge(_trackedTarget);
                SetState(StormPyramidState.ChargingAttack);
            }
        }

        private void UpdateCharging(float deltaTime)
        {
            FacePosition(_lightning.TargetPosition, 9f);
            if (_lightning.TickCharge(deltaTime))
            {
                _lightning.Fire();
                SetState(StormPyramidState.FiringLightning);
            }
        }

        private void UpdateFiring(float deltaTime)
        {
            if (_lightning.TickFiring(deltaTime))
            {
                SetState(StormPyramidState.Cooldown);
            }
        }

        private void UpdateCooldown()
        {
            if (_stateTime >= _settings.EvaluateCooldown(DuneVectorContractRisk.CurrentRisk))
            {
                _attackTimer = GetRecurringAttackDelay();
                SetState(StormPyramidState.IdleHovering);
            }
        }

        private void BeginDetonationWindUp()
        {
            _lightning.CancelAttack();
            SetState(StormPyramidState.TriggeredWindUp);
        }

        private void UpdateDetonationWindUp()
        {
            if (_stateTime < _explosionSettings.WindUpDuration)
            {
                return;
            }

            _combatTarget.SetTargetable(false);
            bool spawnedPrefabExplosion = SpawnPrefabExplosion();
            GroundExploderExplosionEffect.Spawn(
                transform.position,
                _player,
                _playerHealth,
                _materials,
                _explosionSettings,
                _settings.ProximityExplosionRadiusMultiplier,
                !spawnedPrefabExplosion);
            Destroy(gameObject);
        }

        private bool SpawnPrefabExplosion()
        {
            if (!_settings.UseExplosionPrefab)
            {
                return false;
            }

            bool spawnedPrimary = SpawnExplosionPrefab(
                _settings.ExplosionPrefab,
                _settings.ExplosionPrefabLocalPosition,
                _settings.ExplosionPrefabLocalEulerAngles,
                _settings.ExplosionPrefabLocalScale,
                _settings.ExplosionPrefabLifetime,
                "Storm Pyramid Explosion");
            bool spawnedAdditional = SpawnExplosionPrefab(
                _settings.AdditionalExplosionPrefab,
                _settings.AdditionalExplosionPrefabLocalPosition,
                _settings.AdditionalExplosionPrefabLocalEulerAngles,
                _settings.AdditionalExplosionPrefabLocalScale,
                _settings.AdditionalExplosionPrefabLifetime,
                "Storm Pyramid Additional Explosion");
            return spawnedPrimary || spawnedAdditional;
        }

        private bool SpawnExplosionPrefab(
            GameObject prefab,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale,
            float lifetime,
            string rootName)
        {
            if (prefab == null)
            {
                return false;
            }

            GameObject effectRoot = new GameObject(rootName);
            effectRoot.transform.position = transform.position;

            GameObject effect = Instantiate(prefab, effectRoot.transform, false);
            effect.name = prefab.name;
            effect.transform.localPosition = localPosition;
            effect.transform.localRotation = prefab.transform.localRotation
                * Quaternion.Euler(localEulerAngles);
            effect.transform.localScale = Vector3.Scale(
                prefab.transform.localScale,
                localScale);

            if (lifetime > 0f)
            {
                Destroy(effectRoot, lifetime);
            }

            return true;
        }

        private void UpdatePresentation(float deltaTime)
        {
            if (_visual != null)
            {
                float yaw = _settings.VisualRotationSpeed * deltaTime;
                _visual.Rotate(0f, yaw, 0f, Space.Self);
            }

            if (_counterRotator != null)
            {
                float counterYaw = _settings.CounterRotationSpeed * deltaTime;
                _counterRotator.Rotate(0f, counterYaw, 0f, Space.Self);
            }

            if (_core != null && CurrentState != StormPyramidState.ChargingAttack)
            {
                float pulse = 1f + (Mathf.Sin((Time.time * _settings.CorePulseSpeed) + _identity)
                    * _settings.CorePulseAmount);
                _core.localScale = _settings.CoreScale * pulse;
            }
        }

        private void FacePosition(Vector3 target, float sharpness)
        {
            Vector3 planarDirection = Vector3.ProjectOnPlane(target - transform.position, Vector3.up);
            if (planarDirection.sqrMagnitude < 0.001f)
            {
                return;
            }
            Quaternion targetRotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                DuneVectorMath.Sharpness(sharpness, Time.deltaTime));
        }

        private void RepositionNearPlayer()
        {
            _lightning.CancelAttack();
            _movement.RepositionNearPlayer();
            _attackTimer = GetStaggeredAttackDelay();
            SetState(StormPyramidState.IdleHovering);
        }

        private float ResolveRepositionDistance()
        {
            float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                _settings.MinimumSpawnDistance,
                _settings.DetectionRange,
                _world);
            return DuneVectorEnemyEngagementRing.ResolveRepositionDistance(
                _settings.RepositionDistance,
                DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                    minimumDistance,
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance));
        }

        public void SetGameplayActive(bool active)
        {
            _gameplayActive = active;
            _proximityDetonationActive = active;
            if (!active)
            {
                _lightning?.CancelAttack();
                _attackTimer = GetStaggeredAttackDelay();
                SetState(StormPyramidState.IdleHovering);
            }
            else
            {
                _attackTimer = Mathf.Min(_attackTimer, GetAttackInterval());
            }
        }

        private float GetAttackInterval()
        {
            if (_settings == null)
            {
                return 0f;
            }

            float riskProgress = Mathf.Clamp01(
                DuneVectorContractRisk.CurrentRisk /
                (float)Mathf.Max(1, _settings.AttackIntervalRiskCeiling));
            return Mathf.Max(0f, Mathf.Lerp(
                _settings.AttackInterval,
                _settings.AttackIntervalAtRiskCeiling,
                riskProgress));
        }

        private float GetStaggeredAttackDelay()
        {
            float minimumMultiplier = Mathf.Clamp01(_settings.MinimumInitialAttackDelayMultiplier);
            float identityPhase = Mathf.Repeat((_identity * 0.417f) + 0.21f, 1f);
            return GetAttackInterval() * Mathf.Lerp(minimumMultiplier, 1f, identityPhase);
        }

        private float GetRecurringAttackDelay()
        {
            float cadencePhase = EvaluateAttackCadencePhase(_identity, _attackSequence);
            _attackSequence++;
            return GetAttackInterval() +
                (Mathf.Max(0f, _settings.RecurringAttackCadenceSpread) * cadencePhase);
        }

        private static float EvaluateAttackCadencePhase(int identity, int attackSequence)
        {
            return Mathf.Repeat(
                (identity * 0.417f) + ((attackSequence + 1) * 0.6180339f) + 0.21f,
                1f);
        }

        private void SetState(StormPyramidState state)
        {
            CurrentState = state;
            _stateTime = 0f;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _movement.ApplyWorldShift(shift);
            _lightning.ApplyWorldShift(shift);
        }

        public bool TryGetThreatWarning(out StormPyramidThreatWarning warning)
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                warning = default;
                return false;
            }

            float chargeDuration = _lightning.GetChargeDuration(_lightning.TargetType);

            // The HUD must cover the whole reaction window, not just the charge. With a short
            // charge time the charge alone is far too brief to register.
            if (CurrentState == StormPyramidState.WarningTelegraph)
            {
                float leadTime = Mathf.Max(0f, _settings.GroundWarningLeadTime) + chargeDuration;
                float secondsRemaining = _lightning.WarningSecondsRemaining + chargeDuration;
                warning = new StormPyramidThreatWarning(
                    _lightning.TargetType,
                    _lightning.TargetPosition,
                    secondsRemaining,
                    Mathf.Clamp01(1f - (secondsRemaining / Mathf.Max(0.01f, leadTime))));
                return true;
            }

            if (CurrentState == StormPyramidState.ChargingAttack)
            {
                float elapsedCharge = Mathf.Max(0f, chargeDuration - _lightning.ChargeSecondsRemaining);
                warning = new StormPyramidThreatWarning(
                    _lightning.TargetType,
                    _lightning.TargetPosition,
                    _lightning.ChargeSecondsRemaining,
                    Mathf.Clamp01(elapsedCharge / Mathf.Max(0.01f, chargeDuration)));
                return true;
            }

            warning = default;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (_settings == null)
            {
                return;
            }
            Color detectionColor = _settings.WarningColor;
            detectionColor.a = 0.45f;
            Gizmos.color = detectionColor;
            Gizmos.DrawWireSphere(transform.position, _settings.DetectionRange);
            Color strikeColor = _settings.LightningColor;
            strikeColor.a = 0.7f;
            Gizmos.color = strikeColor;
            Gizmos.DrawWireSphere(
                _lightning != null ? _lightning.TargetPosition : transform.position,
                _settings.EvaluateStrikeRadius(DuneVectorContractRisk.CurrentRisk));
            if (_explosionSettings != null)
            {
                Gizmos.color = new Color(1f, 0.65f, 0.05f, 0.55f);
                Gizmos.DrawWireSphere(
                    transform.position,
                    _explosionSettings.DetectionRadius
                    * Mathf.Max(0.1f, _settings.ProximityDetectionRadiusMultiplier));
                Gizmos.color = new Color(1f, 0.12f, 0.02f, 0.65f);
                Gizmos.DrawWireSphere(
                    transform.position,
                    _explosionSettings.EvaluateExplosionRadius(DuneVectorContractRisk.CurrentRisk)
                    * Mathf.Max(0.1f, _settings.ProximityExplosionRadiusMultiplier));
            }
        }

        public void SetLightningOnlyActive()
        {
            _gameplayActive = true;
            _proximityDetonationActive = false;
            _attackTimer = Mathf.Min(_attackTimer, GetAttackInterval());
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidMovement : MonoBehaviour
    {
        public float HorizontalDistanceFromPlayer
        {
            get
            {
                if (_player == null)
                {
                    return 0f;
                }

                Vector3 offset = transform.position - _player.WorldCenter;
                return new Vector2(offset.x, offset.z).magnitude;
            }
        }

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private StormPyramidTuning _settings;
        private Vector3 _patrolCenter;
        private float _fixedAltitude;
        private float _phase;
        private float _patrolHeightNormalized;
        private int _identity;
        private int _repositionCount;

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            StormPyramidTuning settings,
            int identity)
        {
            _player = player;
            _world = world;
            _settings = settings;
            _identity = identity;
            _phase = identity * 1.713f;
            _patrolCenter = transform.position;
            _fixedAltitude = transform.position.y;
            float terrainHeight = world.SampleHeightAtLocal(transform.position.x, transform.position.z);
            float heightVariance = Mathf.Max(0f, settings.HoverHeightVariance);
            _patrolHeightNormalized = Mathf.InverseLerp(
                settings.HoverHeight - heightVariance,
                settings.HoverHeight + heightVariance,
                _fixedAltitude - terrainHeight);
        }

        public void Tick(float deltaTime)
        {
            float range = Mathf.Max(0f, _settings.PatrolDriftRange);
            float speed = Mathf.Max(0f, _settings.PatrolDriftSpeed) *
                DuneVectorContractRisk.EnemySpeedMultiplier;
            if (range <= 0.001f || speed <= 0.001f)
            {
                Vector3 stationary = transform.position;
                stationary.y = _fixedAltitude;
                transform.position = stationary;
                return;
            }

            _phase += (speed / Mathf.Max(1f, range)) * deltaTime;
            Vector3 target = _patrolCenter + new Vector3(
                Mathf.Sin(_phase) * range,
                0f,
                Mathf.Sin((_phase * 0.73f) + (_identity * 0.91f)) * range * 0.72f);
            target.y = _fixedAltitude;
            transform.position = Vector3.MoveTowards(transform.position, target, speed * deltaTime);
            Vector3 levelPosition = transform.position;
            levelPosition.y = _fixedAltitude;
            transform.position = levelPosition;
        }

        public void RepositionNearPlayer()
        {
            _repositionCount++;
            float seed = (_identity * 137.5f) + (_repositionCount * 83.7f);
            float angle = seed * Mathf.Deg2Rad;
            float distance01 = Mathf.Repeat((_identity * 0.413f) + (_repositionCount * 0.271f), 1f);
            float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                _settings.MinimumSpawnDistance,
                _settings.DetectionRange,
                _world);
            float distance = Mathf.Lerp(
                minimumDistance,
                DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                    minimumDistance,
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance),
                distance01);
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 position = playerPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            float terrainHeight = _world.SampleHeightAtLocal(position.x, position.z);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                _patrolHeightNormalized);
            position.y = terrainHeight + _settings.HoverHeight + heightVariation;
            transform.position = position;
            _patrolCenter = position;
            _fixedAltitude = position.y;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
            _patrolCenter += shift;
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidTargeting : MonoBehaviour
    {
        private DesertWorldStreamer _world;

        public void Initialize(DesertWorldStreamer world)
        {
            _world = world;
        }

        public Vector3 GetGroundPointBelow(Vector3 origin)
        {
            float groundHeight = _world.SampleHeightAtLocal(origin.x, origin.z);
            return new Vector3(origin.x, groundHeight + 0.08f, origin.z);
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidLightningDamage : MonoBehaviour
    {
        private DroneCharacterController _player;
        private DroneHealth _health;

        public void Initialize(DroneCharacterController player, DroneHealth health)
        {
            _player = player;
            _health = health;
        }

        public void ResolveStrike(
            Vector3 strikePoint,
            float strikeRadius,
            float damage,
            string damageSource,
            string deathMessage)
        {
            if (_player == null || _health == null || _health.IsDead || strikeRadius <= 0f || damage <= 0f)
            {
                return;
            }

            if (Vector3.Distance(_player.WorldCenter, strikePoint) <= strikeRadius)
            {
                _health.TakeDamage(
                    damage * DuneVectorContractRisk.EnemyDamageMultiplier,
                    damageSource,
                    deathMessage);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbMeterDrain : MonoBehaviour
    {
        private DroneCharacterController _player;
        private DroneHealth _health;
        private DroneStaminaSystem _stamina;
        private PlayerStrikeOrbTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            PlayerStrikeOrbTuning settings)
        {
            _player = player;
            _health = health;
            _settings = settings;
            _stamina = player != null ? player.GetComponent<DroneStaminaSystem>() : null;
        }

        public void ResolveStrike(Vector3 strikePoint, float strikeRadius)
        {
            if (_player == null || _health == null || _health.IsDead || strikeRadius <= 0f)
            {
                return;
            }

            if (Vector3.Distance(_player.WorldCenter, strikePoint) > strikeRadius)
            {
                return;
            }

            // Strike Rings deny airspace rather than deal damage. Burning the flight reserve drops
            // the drone toward the ground layer and the stamina hit stops it from immediately
            // sprinting clear of whatever is waiting there, which is the pressure the enemy is for.
            int risk = DuneVectorContractRisk.CurrentRisk;
            bool drained = false;
            if (_player.DrainFlightMeter(_settings.EvaluateFlightMeterDrain(risk)))
            {
                drained = true;
            }
            if (_stamina != null && _stamina.Drain(_settings.EvaluateStaminaDrain(risk)))
            {
                drained = true;
            }

            if (_settings.LightningDamage > 0f)
            {
                _health.TakeDamage(
                    _settings.LightningDamage * DuneVectorContractRisk.EnemyDamageMultiplier,
                    _settings.LightningDamageSource,
                    _settings.LightningDeathMessage);
            }

            if (drained &&
                !DuneTrainingRuntime.HeadlessPresentation &&
                !string.IsNullOrWhiteSpace(_settings.MeterDrainEvent))
            {
                RuntimeManager.PlayOneShot(_settings.MeterDrainEvent, _player.WorldCenter);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidLightningAttack : MonoBehaviour
    {
        public Vector3 TargetPosition => _target.Position;
        public StormLightningAttackType TargetType => _target.Type;
        public float ChargeNormalized => _charging
            ? Mathf.Clamp01(_timer / Mathf.Max(0.01f, _chargeDuration))
            : 0f;
        public float ChargeSecondsRemaining => _charging
            ? Mathf.Max(0f, _chargeDuration - _timer)
            : 0f;
        public bool IsWarning => _warning;
        public float WarningSecondsRemaining => _warning
            ? Mathf.Max(0f, _warningDuration - _timer)
            : 0f;

        private const int LightningSegments = 11;
        private readonly Vector3[] _lightningPositions = new Vector3[LightningSegments];
        private readonly List<Renderer> _warningRenderers = new List<Renderer>();
        private readonly List<Transform> _ionColumns = new List<Transform>();
        private MaterialPropertyBlock _warningBlock;
        private Transform _owner;
        private Transform _origin;
        private Transform _core;
        private Transform _halo;
        private Transform _marker;
        private Transform _impactFlash;
        private Transform _warningZone;
        private Transform _corePip;
        private Mesh _staticStampMesh;
        private Mesh _rotorStampMesh;
        private DesertWorldStreamer _world;
        private DuneVectorVisuals.GroundHeightSampler _worldSampler;
        private DuneVectorVisuals.GroundHeightSampler _drapeSampler;
        private readonly DuneVectorVisuals.StormWarningDrapeCache _drape =
            new DuneVectorVisuals.StormWarningDrapeCache();
        private Transform _groundImpactWave;
        private Transform _spawnedGroundImpact;
        private LineRenderer _chargeLine;
        private LineRenderer _warningBeam;
        private LineRenderer _lightningLine;
        private StormPyramidTuning _settings;
        private StormPyramidLightningDamage _damage;
        private StormLightningTarget _target;
        private float _timer;
        private float _chargeDuration;
        private float _warningDuration;
        private float _warningPulsePhase;
        private float _bezelSpinDegrees;
        private float _sigilSpinDegrees;
        private float _ionShimmerPhase;
        private float _strikeRadius;
        private float _strikeRadiusScale = 1f;
        private int _identity;
        private bool _warning;
        private bool _charging;
        private bool _firing;

        public void Initialize(
            Transform owner,
            Transform origin,
            Transform core,
            Transform halo,
            DuneVectorMaterials materials,
            StormPyramidTuning settings,
            StormPyramidLightningDamage damage,
            DesertWorldStreamer world,
            int identity)
        {
            _owner = owner;
            _world = world;
            // Both delegates are cached here: the stamp rebuild runs every frame a strike is
            // telegraphing, and allocating a fresh delegate per rebuild would garbage that path.
            _worldSampler = _world != null
                ? (DuneVectorVisuals.GroundHeightSampler)_world.SampleHeightAtLocal
                : null;
            _drapeSampler = _drape.Sample;
            _origin = origin != null ? origin : owner;
            _core = core;
            _halo = halo;
            _settings = settings;
            _damage = damage;
            _identity = identity;
            _marker = DuneVectorVisuals.CreateStormPyramidStrikeMarker(owner.parent, materials, settings);
            _impactFlash = _marker.Find("Strike Impact Flash");
            _warningZone = _marker.Find("Ground Warning Zone");
            _corePip = _warningZone != null ? _warningZone.Find("Sigil Core Pip") : null;
            _staticStampMesh = FindStampMesh("Warning Stamp Static");
            _rotorStampMesh = FindStampMesh("Warning Stamp Rotor");
            CacheIonColumns();
            CacheWarningRenderers();
            if (settings.GroundImpactPrefab == null)
            {
                _groundImpactWave = DuneVectorVisuals.CreateStormGroundImpactWave(
                    _marker,
                    materials,
                    settings.StrikeRadius,
                    settings.GroundImpactRingThickness,
                    settings.GroundImpactHeightOffset);
            }
            _chargeLine = CreateLine("Lightning Charge Telegraph", materials.LightningWarning, settings.ChargeTelegraphWidth);
            _warningBeam = CreateLine(
                "Strike Warning Beam",
                materials.LightningWarning,
                Mathf.Max(0.01f, settings.StrikeRadius * settings.GroundWarningBeamWidth));
            _lightningLine = CreateLine("Lightning Bolt", materials.Lightning, settings.LightningWidth);
            CancelAttack();
        }

        /// <summary>
        /// Paints the danger zone on the ground at its true final size before the strike starts
        /// charging. Returns false when the designer has disabled the lead time, in which case the
        /// caller should go straight to charging.
        /// </summary>
        public bool BeginWarning(StormLightningTarget target)
        {
            float leadTime = Mathf.Max(0f, _settings.GroundWarningLeadTime);
            if (leadTime <= 0f)
            {
                return false;
            }

            PrepareStrike(target);
            _warningDuration = leadTime;
            _warning = true;
            _charging = false;
            _chargeLine.enabled = false;
            PlayWarningAudio();
            UpdateWarningVisual(0f);
            return true;
        }

        public bool TickWarning(float deltaTime)
        {
            if (!_warning)
            {
                return false;
            }
            _timer += deltaTime;
            float warning01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _warningDuration));
            UpdateWarningVisual(warning01);
            return warning01 >= 1f;
        }

        public void BeginCharge(StormLightningTarget target)
        {
            // A warning phase already staged the marker at full size; re-preparing it here would
            // reset the danger zone the drone has been reading.
            if (!_warning || _target.Position != target.Position)
            {
                PrepareStrike(target);
            }

            _warning = false;
            _timer = 0f;
            _chargeDuration = GetChargeDuration(target.Type);
            _charging = true;
            _firing = false;
            _chargeLine.enabled = true;
            _lightningLine.enabled = false;
            UpdateChargeVisual(0f);
        }

        public bool TickCharge(float deltaTime)
        {
            if (!_charging)
            {
                return false;
            }
            _timer += deltaTime;
            float charge01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _chargeDuration));
            UpdateChargeVisual(charge01);
            return charge01 >= 1f;
        }

        public void Fire()
        {
            _warning = false;
            _charging = false;
            _firing = true;
            _timer = 0f;
            _chargeLine.enabled = false;
            _warningBeam.enabled = false;
            _lightningLine.enabled = true;
            _marker.localScale = Vector3.one * _strikeRadiusScale;
            SetWarningZoneActive(false);
            if (_groundImpactWave != null)
            {
                _groundImpactWave.gameObject.SetActive(true);
                _groundImpactWave.localScale = Vector3.one * _settings.GroundImpactStartScale;
            }
            SpawnGroundImpactPrefab();
            _damage.ResolveStrike(
                _target.Position,
                _strikeRadius,
                _settings.LightningDamage,
                "Storm Pyramid ground lightning",
                _settings.LightningDeathMessage);
            UpdateLightningVisual();
        }

        public bool TickFiring(float deltaTime)
        {
            if (!_firing)
            {
                return false;
            }
            _timer += deltaTime;
            float lightningDuration = Mathf.Max(0.01f, _settings.LightningVisualDuration);
            float lightningLife01 = Mathf.Clamp01(_timer / lightningDuration);
            if (_timer < lightningDuration)
            {
                UpdateLightningVisual();
            }
            else
            {
                _lightningLine.enabled = false;
            }
            if (_impactFlash != null)
            {
                float flash = Mathf.Sin(lightningLife01 * Mathf.PI)
                    * _settings.StrikeRadius
                    * _settings.GroundImpactFlashScaleMultiplier;
                _impactFlash.localScale = Vector3.one * flash;
            }

            float expansionDuration = Mathf.Max(0.01f, _settings.GroundImpactExpansionDuration);
            float expansion01 = Mathf.Clamp01(_timer / expansionDuration);
            if (_groundImpactWave != null)
            {
                float easedExpansion = Mathf.SmoothStep(
                    _settings.GroundImpactStartScale,
                    1f,
                    expansion01);
                _groundImpactWave.localScale = Vector3.one * easedExpansion;
            }

            float impactDuration = expansionDuration + Mathf.Max(0f, _settings.GroundImpactHoldDuration);
            if (_timer < Mathf.Max(lightningDuration, impactDuration))
            {
                return false;
            }

            CancelAttack();
            return true;
        }

        public void CancelAttack()
        {
            _warning = false;
            _charging = false;
            _firing = false;
            if (_chargeLine != null) _chargeLine.enabled = false;
            if (_warningBeam != null) _warningBeam.enabled = false;
            if (_lightningLine != null) _lightningLine.enabled = false;
            if (_marker != null) _marker.gameObject.SetActive(false);
            if (_groundImpactWave != null)
            {
                _groundImpactWave.gameObject.SetActive(false);
                _groundImpactWave.localScale = Vector3.zero;
            }
            if (_halo != null) _halo.localScale = Vector3.zero;
            if (_core != null) _core.localScale = _settings.CoreScale;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _target = new StormLightningTarget(_target.Type, _target.Position + shift);
            _drape.ApplyWorldShift(shift);
            if (_marker != null)
            {
                _marker.position += shift;
            }
            if (_spawnedGroundImpact != null)
            {
                _spawnedGroundImpact.position += shift;
            }
            if (_warning)
            {
                float warning01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _warningDuration));
                UpdateWarningVisual(warning01);
            }
            else if (_charging)
            {
                float charge01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _chargeDuration));
                UpdateChargeVisual(charge01);
            }
            else if (_firing)
            {
                UpdateLightningVisual();
            }
        }

        public float GetChargeDuration(StormLightningAttackType targetType)
        {
            return Mathf.Max(0.01f, _settings.ChargeTime);
        }

        private void PrepareStrike(StormLightningTarget target)
        {
            _target = target;
            _strikeRadius = _settings.EvaluateStrikeRadius(DuneVectorContractRisk.CurrentRisk);
            _strikeRadiusScale = _strikeRadius / Mathf.Max(0.1f, _settings.StrikeRadius);
            _timer = 0f;
            _warningPulsePhase = 0f;
            _bezelSpinDegrees = 0f;
            _sigilSpinDegrees = 0f;
            _ionShimmerPhase = 0f;
            _firing = false;
            _marker.gameObject.SetActive(true);
            _marker.position = target.Position;
            // The danger zone is staged at its true radius immediately. Growing it into place
            // would misreport the strike area for the whole time the drone can still react.
            _marker.localScale = Vector3.one * _strikeRadiusScale;
            StageWarningStamp();
            if (_impactFlash != null)
            {
                _impactFlash.localScale = Vector3.zero;
            }
            SetWarningZoneActive(true);
            if (_groundImpactWave != null)
            {
                _groundImpactWave.gameObject.SetActive(false);
                _groundImpactWave.localScale = Vector3.zero;
            }
            _lightningLine.enabled = false;
        }

        private void PlayWarningAudio()
        {
            if (DuneTrainingRuntime.HeadlessPresentation ||
                string.IsNullOrWhiteSpace(_settings.GroundWarningAudioEvent))
            {
                return;
            }
            RuntimeManager.PlayOneShot(_settings.GroundWarningAudioEvent, _target.Position);
        }

        private void UpdateWarningVisual(float warning01)
        {
            _marker.position = _target.Position;
            _marker.localScale = Vector3.one * _strikeRadiusScale;

            // The countdown ring collapses onto the danger zone edge exactly as the charge starts,
            // so the closing gap reads as the remaining reaction time from any viewing angle.
            float closingScale = Mathf.Lerp(
                Mathf.Max(1f, _settings.GroundWarningClosingRingStartMultiplier),
                1f,
                warning01);
            AnimateWarningSigil(warning01, closingScale);
            ApplyWarningPulse(warning01);
            UpdateWarningBeam(warning01);
        }

        private Mesh FindStampMesh(string surfaceName)
        {
            Transform surface = _warningZone != null ? _warningZone.Find(surfaceName) : null;
            MeshFilter filter = surface != null ? surface.GetComponent<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// Stamps the fixed line work onto the dunes under a freshly staged strike, and resizes the
        /// standing pieces to the risk-scaled radius. The stamp is generated in world metres so it
        /// can pin its vertices to real dune heights, so the zone has to shed the marker root's
        /// radius scale rather than inherit it.
        /// </summary>
        private void StageWarningStamp()
        {
            if (_warningZone == null)
            {
                return;
            }

            _warningZone.localScale = Vector3.one / Mathf.Max(0.0001f, _strikeRadiusScale);
            _drape.Rebuild(
                _target.Position,
                DuneVectorVisuals.ResolveStormWarningStampExtent(_strikeRadius, _settings),
                _settings.GroundWarningDrapeResolution,
                _worldSampler);
            if (_staticStampMesh != null)
            {
                DuneVectorVisuals.BuildStormWarningStaticStamp(
                    _staticStampMesh, _target.Position, _strikeRadius, _settings, _drapeSampler);
            }

            if (_corePip != null)
            {
                float pipWidth = _strikeRadius * Mathf.Max(0f, _settings.GroundWarningCorePipWidth);
                float pipHeight = _strikeRadius * Mathf.Max(0f, _settings.GroundWarningCorePipHeight);
                _corePip.localScale = new Vector3(
                    pipWidth * 0.5f,
                    pipHeight / DuneVectorVisuals.PyramidMeshApexHeight,
                    pipWidth * 0.5f);
                // Read the centre off the drape too, so the pip stands on the same surface the
                // stamp is pinned to rather than on wherever the strike point happened to land.
                float centreHeight = _drape.Sample(_target.Position.x, _target.Position.z);
                _corePip.localPosition = Vector3.up * (
                    (centreHeight - _target.Position.y)
                        + Mathf.Max(0f, _settings.GroundWarningHeightOffset));
            }
        }

        /// <summary>
        /// Regenerates the moving half of the stamp and walks the ion columns around with it. A
        /// mesh draped onto the dunes cannot be turned or scaled by its transform without peeling
        /// off the slope it is stamped to, so the rotation is baked into the geometry every frame
        /// instead. Only the pieces that actually move are in this mesh.
        /// </summary>
        private void AnimateWarningSigil(float urgency01, float closingRingScale)
        {
            float urgency = Mathf.Clamp01(urgency01);
            float deltaTime = Time.deltaTime;

            _bezelSpinDegrees += Mathf.Lerp(
                _settings.GroundWarningBezelSpinStart,
                _settings.GroundWarningBezelSpinEnd,
                urgency) * deltaTime;
            _sigilSpinDegrees += Mathf.Lerp(
                _settings.GroundWarningSigilSpinStart,
                _settings.GroundWarningSigilSpinEnd,
                urgency) * deltaTime;

            if (_rotorStampMesh != null)
            {
                DuneVectorVisuals.BuildStormWarningRotorStamp(
                    _rotorStampMesh,
                    _target.Position,
                    _strikeRadius,
                    _bezelSpinDegrees,
                    _sigilSpinDegrees,
                    closingRingScale,
                    _settings,
                    _drapeSampler);
            }

            if (_ionColumns.Count == 0)
            {
                return;
            }

            _ionShimmerPhase += deltaTime * Mathf.Max(0f, _settings.GroundWarningIonColumnShimmerSpeed);
            float columnHeight = _strikeRadius * Mathf.Lerp(
                Mathf.Max(0f, _settings.GroundWarningIonColumnHeightStart),
                Mathf.Max(0f, _settings.GroundWarningIonColumnHeightEnd),
                urgency);
            float columnWidth = Mathf.Max(0.004f, _strikeRadius * _settings.GroundWarningIonColumnWidth);
            float cornerRadius = DuneVectorVisuals.ResolveStormWarningSigilCornerRadius(
                _strikeRadius, _settings);
            float lift = Mathf.Max(0f, _settings.GroundWarningHeightOffset);
            float shimmer = Mathf.Clamp01(_settings.GroundWarningIonColumnShimmer);
            float phaseStep = (Mathf.PI * 2f) / _ionColumns.Count;
            for (int i = 0; i < _ionColumns.Count; i++)
            {
                Transform column = _ionColumns[i];
                if (column == null)
                {
                    continue;
                }

                // Half a slot of offset seats the columns on the sigil's vertices whenever the
                // column count matches its side count, which is the authored pairing.
                float yaw = _sigilSpinDegrees + ((360f / _ionColumns.Count) * (i + 0.5f));
                Quaternion placement = Quaternion.Euler(0f, yaw, 0f);
                Vector3 local = placement * (Vector3.forward * cornerRadius);
                float groundHeight = _drape.Sample(
                    _target.Position.x + local.x, _target.Position.z + local.z);
                local.y = (groundHeight - _target.Position.y) + lift;
                column.localPosition = local;
                column.localRotation = placement;

                float wave = Mathf.Sin(_ionShimmerPhase - (i * phaseStep) + _identity);
                float height = columnHeight * (1f + (shimmer * wave));
                column.localScale = new Vector3(
                    columnWidth * 0.5f,
                    Mathf.Max(0.0001f, height / DuneVectorVisuals.PyramidMeshApexHeight),
                    columnWidth * 0.5f);
            }
        }

        private void ApplyWarningPulse(float urgency01)
        {
            float pulseSpeed = Mathf.Lerp(
                Mathf.Max(0f, _settings.GroundWarningPulseSpeedStart),
                Mathf.Max(0f, _settings.GroundWarningPulseSpeedEnd),
                Mathf.Clamp01(urgency01));
            _warningPulsePhase += Time.deltaTime * pulseSpeed;
            float wave = 0.5f + (Mathf.Sin(_warningPulsePhase + _identity) * 0.5f);
            float brightness = Mathf.Lerp(
                Mathf.Clamp01(_settings.GroundWarningPulseDepth),
                1f,
                wave) * Mathf.Max(0f, _settings.GroundWarningBrightnessMultiplier);

            if (_warningRenderers.Count == 0)
            {
                return;
            }

            _warningBlock ??= new MaterialPropertyBlock();
            Color emission = _settings.WarningEmission * brightness;
            emission.a = 1f;
            _warningBlock.SetColor("_BaseColor", emission);
            for (int i = 0; i < _warningRenderers.Count; i++)
            {
                if (_warningRenderers[i] != null)
                {
                    _warningRenderers[i].SetPropertyBlock(_warningBlock);
                }
            }
        }

        private void UpdateWarningBeam(float urgency01)
        {
            float beamWidth = _strikeRadius * Mathf.Max(0f, _settings.GroundWarningBeamWidth);
            if (beamWidth <= 0f)
            {
                _warningBeam.enabled = false;
                return;
            }

            _warningBeam.enabled = true;
            _warningBeam.positionCount = 2;
            _warningBeam.SetPosition(0, _origin.position);
            _warningBeam.SetPosition(1, _target.Position);
            float taper = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(urgency01));
            _warningBeam.startWidth = beamWidth * taper;
            _warningBeam.endWidth = beamWidth * taper;
        }

        private void CacheWarningRenderers()
        {
            _warningRenderers.Clear();
            if (_warningZone != null)
            {
                _warningZone.GetComponentsInChildren(true, _warningRenderers);
            }
        }

        private void CacheIonColumns()
        {
            _ionColumns.Clear();
            Transform columns = _warningZone != null ? _warningZone.Find("Ion Columns") : null;
            if (columns == null)
            {
                return;
            }
            for (int i = 0; i < columns.childCount; i++)
            {
                _ionColumns.Add(columns.GetChild(i));
            }
        }

        private void SetWarningZoneActive(bool active)
        {
            if (_warningZone != null) _warningZone.gameObject.SetActive(active);
        }

        private void SpawnGroundImpactPrefab()
        {
            if (_settings.GroundImpactPrefab == null)
            {
                return;
            }

            GameObject effectRoot = new GameObject("Storm Pyramid Ground Impact");
            Transform effectRootTransform = effectRoot.transform;
            effectRootTransform.SetParent(_owner.parent, true);
            effectRootTransform.position = _target.Position;

            GameObject effect = UnityEngine.Object.Instantiate(
                _settings.GroundImpactPrefab,
                effectRootTransform,
                false);
            effect.name = _settings.GroundImpactPrefab.name;
            effect.transform.localPosition = _settings.GroundImpactPrefabLocalPosition;
            effect.transform.localRotation = _settings.GroundImpactPrefab.transform.localRotation
                * Quaternion.Euler(_settings.GroundImpactPrefabLocalEulerAngles);

            float radiusFitScale = _strikeRadius
                / Mathf.Max(0.01f, _settings.GroundImpactPrefabReferenceRadius);
            effect.transform.localScale = Vector3.Scale(
                _settings.GroundImpactPrefab.transform.localScale,
                _settings.GroundImpactPrefabScale) * radiusFitScale;
            _spawnedGroundImpact = effectRootTransform;

            if (_settings.GroundImpactPrefabLifetime > 0f)
            {
                UnityEngine.Object.Destroy(effectRoot, _settings.GroundImpactPrefabLifetime);
            }
        }

        private void UpdateChargeVisual(float charge01)
        {
            float pulse = 0.88f + (Mathf.Sin((Time.time * 12f) + _identity) * 0.12f);
            _marker.position = _target.Position;
            _marker.localScale = Vector3.one * _strikeRadiusScale;
            AnimateWarningSigil(1f, 1f);
            ApplyWarningPulse(1f);
            UpdateWarningBeam(1f);
            if (_halo != null)
            {
                _halo.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.15f, charge01) * pulse;
            }
            if (_core != null)
            {
                _core.localScale = _settings.CoreScale
                    * Mathf.Lerp(1f, _settings.CoreChargeScaleMultiplier, charge01)
                    * pulse;
            }

            _chargeLine.positionCount = 2;
            _chargeLine.SetPosition(0, _origin.position);
            _chargeLine.SetPosition(1, _target.Position);
            _chargeLine.startWidth = Mathf.Lerp(
                _settings.ChargeTelegraphWidth * 0.25f,
                _settings.ChargeTelegraphWidth,
                charge01) * pulse;
            _chargeLine.endWidth = _chargeLine.startWidth;
        }

        private void UpdateLightningVisual()
        {
            Vector3 start = _origin.position;
            Vector3 end = _target.Position;
            Vector3 direction = end - start;
            Vector3 axis = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.down;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
            Vector3 side = Vector3.Cross(axis, reference).normalized;
            Vector3 secondSide = Vector3.Cross(axis, side).normalized;
            float amplitude = Mathf.Min(2.2f, Mathf.Max(0.35f, direction.magnitude * 0.022f));

            for (int i = 0; i < LightningSegments; i++)
            {
                float along = i / (float)(LightningSegments - 1);
                Vector3 point = Vector3.Lerp(start, end, along);
                if (i > 0 && i < LightningSegments - 1)
                {
                    float envelope = Mathf.Sin(along * Mathf.PI);
                    float noiseA = Mathf.Sin((Time.time * 73f) + (_identity * 4.7f) + (i * 12.31f));
                    float noiseB = Mathf.Sin((Time.time * 91f) + (_identity * 7.1f) + (i * 8.17f));
                    point += ((side * noiseA) + (secondSide * noiseB)) * amplitude * envelope;
                }
                _lightningPositions[i] = point;
            }

            _lightningLine.positionCount = LightningSegments;
            _lightningLine.SetPositions(_lightningPositions);
        }

        private LineRenderer CreateLine(string objectName, Material material, float width)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(_owner, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.startWidth = width;
            line.endWidth = width * 0.65f;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 100;
            line.enabled = false;
            return line;
        }

        private void OnDestroy()
        {
            if (_marker != null)
            {
                Destroy(_marker.gameObject);
            }
            // The stamp meshes are generated per marker rather than shared out of the mesh cache,
            // so destroying the GameObject that renders them does not reclaim them.
            if (_staticStampMesh != null)
            {
                Destroy(_staticStampMesh);
                _staticStampMesh = null;
            }
            if (_rotorStampMesh != null)
            {
                Destroy(_rotorStampMesh);
                _rotorStampMesh = null;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbEnemy : MonoBehaviour
    {
        public StormPyramidState CurrentState { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private PlayerStrikeOrbTuning _settings;
        private PlayerStrikeOrbMovement _movement;
        private PlayerStrikeOrbTargeting _targeting;
        private PlayerStrikeOrbLightningAttack _lightning;
        private EnemyHealth _enemyHealth;
        private DuneVectorMaterials _materials;
        private Transform _visual;
        private Transform _flyThroughRing;
        private Vector3 _flyThroughCenterLocal;
        private Transform[] _orbPivots;
        private TrailRenderer[] _orbTrails;
        private Vector3 _trackedTarget;
        private float _stateTime;
        private float _attackTimer;
        private int _identity;
        private Vector3 _previousPlayerPosition;
        private Vector3 _previousOrbPosition;
        private Vector3 _previousRingNormal;
        private bool _hasPreviousPlayerPosition;
        private bool _flyThroughTriggered;
        private bool _facingLockedForClosePass;
        private bool _gameplayActive = true;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            PlayerStrikeOrbTuning settings,
            int identity)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _settings = settings;
            _materials = materials;
            _identity = identity;

            _visual = DuneVectorVisuals.CreatePlayerStrikeOrbVisual(transform, materials, settings);
            _flyThroughRing = _visual.Find("Superman Ring");
            CacheFlyThroughCenter();
            int orbitingOrbCount = settings.OrbitingOrbs != null
                ? settings.OrbitingOrbs.Length
                : 0;
            _orbPivots = new Transform[orbitingOrbCount];
            for (int i = 0; i < orbitingOrbCount; i++)
            {
                _orbPivots[i] = _visual.Find($"Orbiting Orb Pivot {i + 1}");
            }
            _orbTrails = _visual.GetComponentsInChildren<TrailRenderer>(true);
            Transform halo = _visual.Find("Charge Halo");
            Transform lightningOrigin = _visual.Find("Lightning Origin");

            _enemyHealth = gameObject.AddComponent<EnemyHealth>();
            _enemyHealth.Initialize(settings.MaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(_enemyHealth, settings.VisualScale * settings.RingRadius);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(
                _enemyHealth,
                player != null ? player.GetComponent<DroneGoldWallet>() : null,
                settings.GoldReward);

            _movement = gameObject.AddComponent<PlayerStrikeOrbMovement>();
            _movement.Initialize(player, world, settings, identity);
            _targeting = gameObject.AddComponent<PlayerStrikeOrbTargeting>();
            _targeting.Initialize(player, world, settings);

            PlayerStrikeOrbMeterDrain drain = gameObject.AddComponent<PlayerStrikeOrbMeterDrain>();
            drain.Initialize(player, playerHealth, settings);
            _lightning = gameObject.AddComponent<PlayerStrikeOrbLightningAttack>();
            _lightning.Initialize(
                transform,
                lightningOrigin,
                halo,
                materials,
                settings,
                drain,
                identity);

            _attackTimer = settings.AttackInterval * Mathf.Lerp(
                settings.MinimumInitialAttackDelayMultiplier,
                1f,
                Mathf.Repeat((identity * 0.371f) + 0.18f, 1f));
            if (player != null)
            {
                CaptureFlyThroughPose(player.WorldCenter);
            }
            _flyThroughTriggered = false;
            _facingLockedForClosePass = false;
            SetState(StormPyramidState.IdleHovering);
            DuneVectorPhotographableMarker.Register(
                gameObject,
                DuneVectorCompendiumSubjectIds.PlayerStrikeOrb,
                PhotographableSubjectCategory.Enemy);
        }

        private void Update()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                _lightning?.CancelAttack();
                return;
            }

            Vector3 playerPosition = _player.WorldCenter;
            if (_gameplayActive && TryDestroyFromFlyThrough(playerPosition))
            {
                return;
            }
            CaptureFlyThroughPose(playerPosition);

            float deltaTime = Time.deltaTime;
            if (!_gameplayActive)
            {
                _movement.Tick(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }

            _stateTime += deltaTime;
            bool mayDrift = CurrentState == StormPyramidState.IdleHovering
                || CurrentState == StormPyramidState.TrackingPlayer
                || CurrentState == StormPyramidState.Cooldown;
            if (mayDrift)
            {
                _movement.Tick(deltaTime);
            }

            bool mayReposition = CurrentState != StormPyramidState.ChargingAttack
                && CurrentState != StormPyramidState.FiringLightning;
            if (mayReposition && _movement.HorizontalDistanceFromPlayer > ResolveRepositionDistance())
            {
                RepositionNearPlayer();
            }

            switch (CurrentState)
            {
                case StormPyramidState.IdleHovering:
                    UpdateIdle(deltaTime);
                    break;
                case StormPyramidState.TrackingPlayer:
                    UpdateTracking();
                    break;
                case StormPyramidState.ChargingAttack:
                    UpdateCharging(deltaTime);
                    break;
                case StormPyramidState.FiringLightning:
                    UpdateFiring(deltaTime);
                    break;
                case StormPyramidState.Cooldown:
                    UpdateCooldown();
                    break;
            }

            UpdatePresentation(deltaTime);
        }

        private void UpdateIdle(float deltaTime)
        {
            _attackTimer = Mathf.Max(0f, _attackTimer -
                (deltaTime * DuneVectorContractRisk.EnemyAttackRateMultiplier));
            if (_attackTimer > 0f || !_targeting.CanTargetAirbornePlayer(transform.position))
            {
                return;
            }

            _trackedTarget = _targeting.GetPredictedPlayerPosition(_settings.ChargeTime);
            SetState(StormPyramidState.TrackingPlayer);
        }

        private void UpdateTracking()
        {
            if (!_targeting.CanTargetAirbornePlayer(transform.position))
            {
                AbortAttack();
                return;
            }

            _trackedTarget = _targeting.GetPredictedPlayerPosition(_settings.ChargeTime);
            FacePosition(_trackedTarget);
            if (_stateTime < _settings.TrackingDuration)
            {
                return;
            }

            _lightning.BeginCharge(_trackedTarget);
            DuneVectorAudioManager.Instance?.PlayStrikeRingLockAlert(
                _settings.LockOnAlertEvent,
                transform.position);
            SetState(StormPyramidState.ChargingAttack);
        }

        private void UpdateCharging(float deltaTime)
        {
            if (!_targeting.IsPlayerAirborne())
            {
                AbortAttack();
                return;
            }

            _lightning.UpdateTarget(_targeting.GetPredictedPlayerPosition(_lightning.ChargeSecondsRemaining));
            FacePosition(_lightning.TargetPosition);
            if (_lightning.TickCharge(deltaTime))
            {
                _lightning.UpdateTarget(_targeting.GetPredictedPlayerPosition(0f));
                _lightning.Fire();
                SetState(StormPyramidState.FiringLightning);
            }
        }

        private void UpdateFiring(float deltaTime)
        {
            if (_lightning.TickFiring(deltaTime))
            {
                SetState(StormPyramidState.Cooldown);
            }
        }

        private void UpdateCooldown()
        {
            if (_stateTime < _settings.Cooldown)
            {
                return;
            }

            _attackTimer = Mathf.Max(0.1f, _settings.AttackInterval);
            SetState(StormPyramidState.IdleHovering);
        }

        private void AbortAttack()
        {
            _lightning.CancelAttack();
            _attackTimer = Mathf.Max(0.1f, _settings.AttackInterval);
            SetState(StormPyramidState.IdleHovering);
        }

        private void UpdatePresentation(float deltaTime)
        {
            FacePosition(_player.WorldCenter);
            if (_visual != null)
            {
                _visual.Rotate(0f, 0f, _settings.RingRotationSpeed * deltaTime, Space.Self);
            }
            int orbitingOrbCount = _orbPivots != null ? _orbPivots.Length : 0;
            for (int i = 0; i < orbitingOrbCount; i++)
            {
                Transform pivot = _orbPivots[i];
                PlayerStrikeOrbSatelliteTuning orb = _settings.OrbitingOrbs[i];
                if (pivot != null && orb != null)
                {
                    pivot.Rotate(0f, 0f, orb.OrbitSpeed * deltaTime, Space.Self);
                }
            }
        }

        private void FacePosition(Vector3 target)
        {
            if (!_facingLockedForClosePass
                && _player != null
                && _player.CurrentMode == DroneTraversalMode.Flight)
            {
                float facingLockDistance = Mathf.Max(
                    0f,
                    _settings.FlyThroughFacingLockDistance);
                _facingLockedForClosePass =
                    (_player.WorldCenter - GetFlyThroughCenter()).sqrMagnitude
                    <= facingLockDistance * facingLockDistance;
            }
            if (_facingLockedForClosePass)
            {
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(
                target - transform.position,
                Vector3.up);
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                DuneVectorMath.Sharpness(_settings.FacingSharpness, Time.deltaTime));
        }

        private void RepositionNearPlayer()
        {
            _lightning.CancelAttack();
            _movement.RepositionNearPlayer();
            ClearOrbTrails();
            _facingLockedForClosePass = false;
            if (_player != null)
            {
                CaptureFlyThroughPose(_player.WorldCenter);
            }
            _attackTimer = Mathf.Max(0.1f, _settings.AttackInterval);
            SetState(StormPyramidState.IdleHovering);
        }

        private float ResolveRepositionDistance()
        {
            float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                _settings.MinimumSpawnDistance,
                _settings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk),
                _world);
            return DuneVectorEnemyEngagementRing.ResolveRepositionDistance(
                _settings.RepositionDistance,
                DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                    minimumDistance,
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance));
        }

        public void SetGameplayActive(bool active)
        {
            _gameplayActive = active;
            if (!active)
            {
                _lightning?.CancelAttack();
                _attackTimer = Mathf.Max(0.1f, _settings.AttackInterval);
                SetState(StormPyramidState.IdleHovering);
            }
            else
            {
                _attackTimer = Mathf.Min(
                    _attackTimer,
                    Mathf.Max(0.1f, _settings.AttackInterval));
            }

            if (_player != null)
            {
                CaptureFlyThroughPose(_player.WorldCenter);
            }
        }

        private bool TryDestroyFromFlyThrough(Vector3 playerPosition)
        {
            if (_flyThroughTriggered
                || !_hasPreviousPlayerPosition
                || _player.CurrentMode != DroneTraversalMode.Flight
                || _enemyHealth == null
                || _enemyHealth.IsDead)
            {
                return false;
            }

            float visibleOpeningRadius = Mathf.Max(
                0.1f,
                _settings.FlyThroughOpeningRadius * _settings.VisualScale);
            float triggerRadius = visibleOpeningRadius
                * Mathf.Clamp01(_settings.FlyThroughRadiusMultiplier);
            Vector3 currentOrbPosition = GetFlyThroughCenter();
            Vector3 previousRelativePosition =
                _previousPlayerPosition - _previousOrbPosition;
            Vector3 currentRelativePosition = playerPosition - currentOrbPosition;
            Vector3 relativeSegment =
                currentRelativePosition - previousRelativePosition;
            if (relativeSegment.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 currentRingNormal = GetRingNormal();
            float previousPlaneDistance = Vector3.Dot(
                previousRelativePosition,
                _previousRingNormal);
            float currentPlaneDistance = Vector3.Dot(
                currentRelativePosition,
                currentRingNormal);
            if (previousPlaneDistance * currentPlaneDistance > 0f)
            {
                return false;
            }

            float planeDistanceDelta = previousPlaneDistance - currentPlaneDistance;
            if (Mathf.Abs(planeDistanceDelta) <= Mathf.Epsilon)
            {
                return false;
            }

            float crossingTime = Mathf.Clamp01(previousPlaneDistance / planeDistanceDelta);
            Vector3 crossingPoint = previousRelativePosition
                + (relativeSegment * crossingTime);
            Vector3 crossingNormal = Vector3.Slerp(
                _previousRingNormal,
                currentRingNormal,
                crossingTime).normalized;
            Vector3 openingOffset = Vector3.ProjectOnPlane(
                crossingPoint,
                crossingNormal);
            if (openingOffset.sqrMagnitude > triggerRadius * triggerRadius)
            {
                return false;
            }

            _flyThroughTriggered = true;
            _lightning?.CancelAttack();
            DuneVectorPortalEvents.NotifyPlayerCrossed(
                currentOrbPosition,
                currentRingNormal,
                _player);
            DuneVectorVisuals.CreatePlayerStrikeOrbFlyThroughExplosion(
                currentOrbPosition,
                _flyThroughRing != null ? _flyThroughRing.rotation : transform.rotation,
                _materials,
                _settings);
            _enemyHealth.TakeDamage(float.MaxValue);
            return true;
        }

        private void CaptureFlyThroughPose(Vector3 playerPosition)
        {
            _previousPlayerPosition = playerPosition;
            _previousOrbPosition = GetFlyThroughCenter();
            _previousRingNormal = GetRingNormal();
            _hasPreviousPlayerPosition = true;
        }

        private Vector3 GetFlyThroughCenter()
        {
            return _flyThroughRing != null
                ? _flyThroughRing.TransformPoint(_flyThroughCenterLocal)
                : transform.position;
        }

        private void CacheFlyThroughCenter()
        {
            if (_flyThroughRing == null)
            {
                _flyThroughCenterLocal = Vector3.zero;
                return;
            }

            Renderer[] renderers = _flyThroughRing.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _flyThroughCenterLocal = Vector3.zero;
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            _flyThroughCenterLocal = _flyThroughRing.InverseTransformPoint(bounds.center);
        }

        private Vector3 GetRingNormal()
        {
            Vector3 ringNormal = _flyThroughRing != null
                ? _flyThroughRing.forward
                : _visual != null
                    ? _visual.forward
                    : transform.forward;
            return ringNormal.sqrMagnitude > Mathf.Epsilon
                ? ringNormal.normalized
                : Vector3.forward;
        }

        private void SetState(StormPyramidState state)
        {
            CurrentState = state;
            _stateTime = 0f;
        }

        public bool TryGetThreatWarning(out StormPyramidThreatWarning warning)
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                warning = default;
                return false;
            }

            if (CurrentState == StormPyramidState.TrackingPlayer)
            {
                float chargeDuration = Mathf.Max(0.01f, _settings.ChargeTime);
                float totalDuration = Mathf.Max(0.01f, _settings.TrackingDuration + chargeDuration);
                warning = new StormPyramidThreatWarning(
                    StormLightningAttackType.PlayerStrike,
                    _trackedTarget,
                    Mathf.Max(0f, _settings.TrackingDuration - _stateTime) + chargeDuration,
                    Mathf.Clamp01(_stateTime / totalDuration),
                    transform.position,
                    _settings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk));
                return true;
            }

            if (CurrentState == StormPyramidState.ChargingAttack)
            {
                float totalDuration = Mathf.Max(0.01f, _settings.TrackingDuration + _settings.ChargeTime);
                float elapsedCharge = Mathf.Max(0f, _settings.ChargeTime - _lightning.ChargeSecondsRemaining);
                warning = new StormPyramidThreatWarning(
                    StormLightningAttackType.PlayerStrike,
                    _lightning.TargetPosition,
                    _lightning.ChargeSecondsRemaining,
                    Mathf.Clamp01((_settings.TrackingDuration + elapsedCharge) / totalDuration),
                    transform.position,
                    _settings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk));
                return true;
            }

            warning = default;
            return false;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _movement.ApplyWorldShift(shift);
            ClearOrbTrails();
            _trackedTarget += shift;
            _lightning.ApplyWorldShift(shift);
            if (_hasPreviousPlayerPosition)
            {
                _previousPlayerPosition += shift;
                _previousOrbPosition += shift;
            }
        }

        private void ClearOrbTrails()
        {
            if (_orbTrails == null)
            {
                return;
            }

            for (int i = 0; i < _orbTrails.Length; i++)
            {
                _orbTrails[i]?.Clear();
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbFlyThroughExplosion : MonoBehaviour
    {
        private Transform _flash;
        private Transform[] _shockwaves;
        private Light _light;
        private PlayerStrikeOrbTuning _settings;
        private float _elapsed;

        public void Initialize(
            Transform flash,
            Transform[] shockwaves,
            Light explosionLight,
            PlayerStrikeOrbTuning settings)
        {
            _flash = flash;
            _shockwaves = shockwaves;
            _light = explosionLight;
            _settings = settings;
            _elapsed = 0f;

            if (!DuneTrainingRuntime.HeadlessPresentation &&
                !string.IsNullOrWhiteSpace(_settings.FlyThroughExplosionEvent))
            {
                RuntimeManager.PlayOneShot(_settings.FlyThroughExplosionEvent, transform.position);
            }
        }

        private void Update()
        {
            if (_settings == null)
            {
                Destroy(gameObject);
                return;
            }

            float duration = Mathf.Max(0.05f, _settings.FlyThroughExplosionDuration);
            float progress = Mathf.Clamp01(_elapsed / duration);
            float peakTime = Mathf.Clamp(_settings.FlyThroughFlashPeakTime, 0.05f, 0.95f);
            float flashProgress = progress <= peakTime
                ? progress / peakTime
                : 1f - ((progress - peakTime) / (1f - peakTime));
            float flashScale = Mathf.Lerp(
                _settings.FlyThroughFlashStartScale,
                _settings.FlyThroughFlashMaximumScale,
                Mathf.Sin(Mathf.Clamp01(flashProgress) * Mathf.PI * 0.5f));
            if (_flash != null)
            {
                _flash.localScale = Vector3.one * flashScale;
            }

            if (_shockwaves != null)
            {
                for (int i = 0; i < _shockwaves.Length; i++)
                {
                    Transform shockwave = _shockwaves[i];
                    if (shockwave == null)
                    {
                        continue;
                    }
                    float delay = (float)i / (_shockwaves.Length * 3f);
                    float waveProgress = Mathf.Clamp01((progress - delay) / Mathf.Max(0.01f, 1f - delay));
                    float radius = Mathf.Lerp(
                        _settings.FlyThroughShockwaveStartRadius,
                        _settings.FlyThroughShockwaveEndRadius,
                        1f - ((1f - waveProgress) * (1f - waveProgress)));
                    shockwave.localScale = Vector3.one * radius;
                }
            }

            if (_light != null)
            {
                _light.intensity = _settings.FlyThroughExplosionLightIntensity
                    * (1f - progress)
                    * (1f - progress);
                _light.range = _settings.FlyThroughExplosionLightRange;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbMovement : MonoBehaviour
    {
        public float HorizontalDistanceFromPlayer
        {
            get
            {
                if (_player == null)
                {
                    return 0f;
                }

                Vector3 offset = transform.position - _player.WorldCenter;
                return new Vector2(offset.x, offset.z).magnitude;
            }
        }

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private PlayerStrikeOrbTuning _settings;
        private Vector3 _patrolCenter;
        private float _fixedAltitude;
        private float _phase;
        private float _patrolHeightNormalized;
        private int _identity;
        private int _repositionCount;

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            PlayerStrikeOrbTuning settings,
            int identity)
        {
            _player = player;
            _world = world;
            _settings = settings;
            _identity = identity;
            _phase = identity * 1.421f;
            _patrolCenter = transform.position;
            _fixedAltitude = transform.position.y;
            float terrainHeight = world.SampleHeightAtLocal(transform.position.x, transform.position.z);
            float heightVariance = Mathf.Max(0f, settings.HoverHeightVariance);
            _patrolHeightNormalized = Mathf.InverseLerp(
                settings.HoverHeight - heightVariance,
                settings.HoverHeight + heightVariance,
                _fixedAltitude - terrainHeight);
        }

        public void Tick(float deltaTime)
        {
            float range = Mathf.Max(0f, _settings.PatrolDriftRange);
            float speed = Mathf.Max(0f, _settings.PatrolDriftSpeed) *
                DuneVectorContractRisk.EnemySpeedMultiplier;
            if (range > 0.001f && speed > 0.001f)
            {
                _phase += (speed / Mathf.Max(1f, range)) * deltaTime;
                Vector3 target = _patrolCenter + new Vector3(
                    Mathf.Sin(_phase) * range,
                    0f,
                    Mathf.Sin((_phase * 0.67f) + (_identity * 0.83f)) * range * 0.76f);
                target.y = _fixedAltitude;
                transform.position = Vector3.MoveTowards(transform.position, target, speed * deltaTime);
            }

            Vector3 levelPosition = transform.position;
            levelPosition.y = _fixedAltitude;
            transform.position = levelPosition;
        }

        public void RepositionNearPlayer()
        {
            _repositionCount++;
            float angle = ((_identity * 149.3f) + (_repositionCount * 79.1f)) * Mathf.Deg2Rad;
            float distance01 = Mathf.Repeat((_identity * 0.397f) + (_repositionCount * 0.283f), 1f);
            float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                _settings.MinimumSpawnDistance,
                _settings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk),
                _world);
            float distance = Mathf.Lerp(
                minimumDistance,
                DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                    minimumDistance,
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance),
                distance01);
            Vector3 position = _player.WorldCenter + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                _patrolHeightNormalized);
            position.y = _world.SampleHeightAtLocal(position.x, position.z)
                + _settings.HoverHeight
                + heightVariation;
            transform.position = position;
            _patrolCenter = position;
            _fixedAltitude = position.y;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
            _patrolCenter += shift;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbTargeting : MonoBehaviour
    {
        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private PlayerStrikeOrbTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            PlayerStrikeOrbTuning settings)
        {
            _player = player;
            _world = world;
            _settings = settings;
        }

        public bool CanTargetAirbornePlayer(Vector3 origin)
        {
            return IsPlayerAirborne()
                && Vector3.Distance(origin, _player.WorldCenter)
                    <= _settings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk);
        }

        public bool IsPlayerAirborne()
        {
            if (_player == null || _player.IsStableGrounded)
            {
                return false;
            }

            float groundHeight = _world.SampleHeightAtLocal(_player.WorldCenter.x, _player.WorldCenter.z);
            return _player.WorldCenter.y - groundHeight >= _settings.MinimumTargetHeightAboveGround;
        }

        public Vector3 GetPredictedPlayerPosition(float secondsUntilImpact)
        {
            Vector3 velocity = _player != null && _player.Motor != null
                ? _player.Motor.Velocity
                : Vector3.zero;
            float predictionTime = Mathf.Max(0f, secondsUntilImpact)
                * Mathf.Max(0f, _settings.PredictionTimeMultiplier);
            Vector3 prediction = velocity * predictionTime;
            prediction = Vector3.ClampMagnitude(
                prediction,
                Mathf.Max(0f, _settings.MaximumPredictionDistance));
            return _player.WorldCenter + prediction;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbLightningAttack : MonoBehaviour
    {
        public Vector3 TargetPosition => _targetPosition;
        public float ChargeSecondsRemaining => _charging
            ? Mathf.Max(0f, _settings.ChargeTime - _timer)
            : 0f;

        private const int LightningSegments = 11;
        private readonly Vector3[] _lightningPositions = new Vector3[LightningSegments];
        private Transform _owner;
        private Transform _origin;
        private Transform _halo;
        private Transform _marker;
        private Transform _impactFlash;
        private LineRenderer _chargeLine;
        private LineRenderer _lightningLine;
        private PlayerStrikeOrbTuning _settings;
        private PlayerStrikeOrbMeterDrain _drain;
        private Vector3 _targetPosition;
        private float _timer;
        private int _identity;
        private bool _charging;
        private bool _firing;

        public void Initialize(
            Transform owner,
            Transform origin,
            Transform halo,
            DuneVectorMaterials materials,
            PlayerStrikeOrbTuning settings,
            PlayerStrikeOrbMeterDrain drain,
            int identity)
        {
            _owner = owner;
            _origin = origin != null ? origin : owner;
            _halo = halo;
            _settings = settings;
            _drain = drain;
            _identity = identity;
            _marker = DuneVectorVisuals.CreateStormStrikeMarker(
                owner.parent,
                materials.PlayerStrikeOrbLightningWarning,
                materials.PlayerStrikeOrbLightning,
                settings.StrikeRadius);
            _impactFlash = _marker.Find("Strike Impact Flash");
            _chargeLine = CreateLine(
                "Player Strike Charge Telegraph",
                materials.PlayerStrikeOrbLightningWarning,
                settings.ChargeTelegraphWidth);
            _lightningLine = CreateLine(
                "Player Strike Lightning Bolt",
                materials.PlayerStrikeOrbLightning,
                settings.LightningWidth);
            CancelAttack();
        }

        public void BeginCharge(Vector3 targetPosition)
        {
            _targetPosition = targetPosition;
            _timer = 0f;
            _charging = true;
            _firing = false;
            _marker.gameObject.SetActive(true);
            _marker.position = targetPosition;
            _marker.localScale = Vector3.one * _settings.ChargeMarkerStartScale;
            if (_impactFlash != null)
            {
                _impactFlash.localScale = Vector3.zero;
            }
            _chargeLine.enabled = true;
            _lightningLine.enabled = false;
            UpdateChargeVisual(0f);
        }

        public void UpdateTarget(Vector3 targetPosition)
        {
            if (!_charging)
            {
                return;
            }

            _targetPosition = targetPosition;
        }

        public bool TickCharge(float deltaTime)
        {
            if (!_charging)
            {
                return false;
            }
            _timer += deltaTime;
            float charge01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _settings.ChargeTime));
            UpdateChargeVisual(charge01);
            return charge01 >= 1f;
        }

        public void Fire()
        {
            _charging = false;
            _firing = true;
            _timer = 0f;
            _chargeLine.enabled = false;
            _lightningLine.enabled = true;
            _drain.ResolveStrike(_targetPosition, _settings.StrikeRadius);
            UpdateLightningVisual();
        }

        public bool TickFiring(float deltaTime)
        {
            if (!_firing)
            {
                return false;
            }
            _timer += deltaTime;
            UpdateLightningVisual();
            float life01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _settings.LightningVisualDuration));
            if (_impactFlash != null)
            {
                float flash = Mathf.Sin(life01 * Mathf.PI)
                    * _settings.StrikeRadius
                    * _settings.ImpactFlashScaleMultiplier;
                _impactFlash.localScale = Vector3.one * flash;
            }
            if (life01 < 1f)
            {
                return false;
            }
            CancelAttack();
            return true;
        }

        public void CancelAttack()
        {
            _charging = false;
            _firing = false;
            if (_chargeLine != null) _chargeLine.enabled = false;
            if (_lightningLine != null) _lightningLine.enabled = false;
            if (_marker != null) _marker.gameObject.SetActive(false);
            if (_halo != null) _halo.localScale = Vector3.zero;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _targetPosition += shift;
            if (_marker != null)
            {
                _marker.position += shift;
            }
            if (_charging)
            {
                float charge01 = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _settings.ChargeTime));
                UpdateChargeVisual(charge01);
            }
            else if (_firing)
            {
                UpdateLightningVisual();
            }
        }

        private void UpdateChargeVisual(float charge01)
        {
            float pulse = 1f + (Mathf.Sin((Time.time * _settings.ChargePulseSpeed) + _identity)
                * _settings.ChargePulseAmount);
            _marker.position = _targetPosition;
            OrientMarkerTowardOrigin();
            _marker.localScale = Vector3.one
                * Mathf.Lerp(_settings.ChargeMarkerStartScale, 1f, charge01)
                * pulse;
            // Keep the strike orb's local warning halo hidden. The charge line below
            // remains visible so the orb-to-drone telegraph is preserved.
            if (_halo != null)
            {
                _halo.localScale = Vector3.zero;
            }
            _chargeLine.positionCount = 2;
            _chargeLine.SetPosition(0, _origin.position);
            _chargeLine.SetPosition(1, _targetPosition);
            _chargeLine.startWidth = Mathf.Lerp(
                _settings.ChargeTelegraphWidth * 0.25f,
                _settings.ChargeTelegraphWidth,
                charge01) * pulse;
            _chargeLine.endWidth = _chargeLine.startWidth;
        }

        private void OrientMarkerTowardOrigin()
        {
            Vector3 direction = _origin.position - _targetPosition;
            if (direction.sqrMagnitude > 0.001f)
            {
                _marker.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            }
        }

        private void UpdateLightningVisual()
        {
            Vector3 start = _origin.position;
            Vector3 direction = _targetPosition - start;
            Vector3 axis = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.down;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
            Vector3 side = Vector3.Cross(axis, reference).normalized;
            Vector3 secondSide = Vector3.Cross(axis, side).normalized;
            float amplitude = Mathf.Min(
                _settings.MaximumLightningJitter,
                Mathf.Max(
                    _settings.MinimumLightningJitter,
                    direction.magnitude * _settings.LightningJitterPerMeter));
            for (int i = 0; i < LightningSegments; i++)
            {
                float along = i / (float)(LightningSegments - 1);
                Vector3 point = Vector3.Lerp(start, _targetPosition, along);
                if (i > 0 && i < LightningSegments - 1)
                {
                    float envelope = Mathf.Sin(along * Mathf.PI);
                    float noiseA = Mathf.Sin((Time.time * 73f) + (_identity * 4.7f) + (i * 12.31f));
                    float noiseB = Mathf.Sin((Time.time * 91f) + (_identity * 7.1f) + (i * 8.17f));
                    point += ((side * noiseA) + (secondSide * noiseB)) * amplitude * envelope;
                }
                _lightningPositions[i] = point;
            }
            _lightningLine.positionCount = LightningSegments;
            _lightningLine.SetPositions(_lightningPositions);
        }

        private LineRenderer CreateLine(string objectName, Material material, float width)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(_owner, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.startWidth = width;
            line.endWidth = width * _settings.LightningEndWidthMultiplier;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 100;
            line.enabled = false;
            return line;
        }

        private void OnDestroy()
        {
            if (_marker != null)
            {
                Destroy(_marker.gameObject);
            }
        }
    }

    [DefaultExecutionOrder(1340)]
    [DisallowMultipleComponent]
    public sealed class StormPyramidThreatHUD : MonoBehaviour
    {
        private DroneCharacterController _player;
        private Camera _camera;
        private StormPyramidTuning _settings;
        private List<StormPyramidEnemy> _enemies;
        private List<PlayerStrikeOrbEnemy> _orbEnemies;
        private GUIStyle _titleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _arrowStyle;
        private float _styledScale = -1f;

        public void Initialize(
            DroneCharacterController player,
            Camera viewCamera,
            StormPyramidTuning settings,
            List<StormPyramidEnemy> enemies,
            List<PlayerStrikeOrbEnemy> orbEnemies)
        {
            _player = player;
            _camera = viewCamera;
            _settings = settings;
            _enemies = enemies;
            _orbEnemies = orbEnemies;
        }

        private void OnGUI()
        {
            // Draw-only overlay: it owns no controls and holds no event state, so the layout
            // pass would repeat every measurement for nothing. Only Repaint does work.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (_player == null || _settings == null || _enemies == null || _orbEnemies == null)
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            if (_camera == null || !TryGetMostUrgentThreat(out StormPyramidThreatWarning threat))
            {
                return;
            }

            int previousDepth = GUI.depth;
            GUI.depth = -1050;
            float scale = Mathf.Clamp(_settings.WarningHudScale, 0.6f, 2f);
            EnsureStyles(scale);
            float pulseSpeed = Mathf.Max(0f, _settings.WarningPulseSpeed);
            float pulse = 0.72f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.18f);
            Color warningColor = _settings.WarningColor;
            warningColor.a = Mathf.Clamp01(pulse);

            DrawScreenBorder(warningColor, scale);
            DrawWarningPanel(threat, warningColor, scale);
            DrawTargetMarker(threat, warningColor, scale);
            GUI.depth = previousDepth;
        }

        private bool TryGetMostUrgentThreat(out StormPyramidThreatWarning warning)
        {
            warning = default;
            float bestTime = float.MaxValue;
            float warningRange = Mathf.Max(1f, _settings.NearbyWarningRange);
            bool found = false;

            for (int i = 0; i < _enemies.Count; i++)
            {
                StormPyramidEnemy enemy = _enemies[i];
                if (enemy == null || !enemy.TryGetThreatWarning(out StormPyramidThreatWarning candidate))
                {
                    continue;
                }

                float targetDistance = Vector3.Distance(_player.WorldCenter, candidate.TargetPosition);
                bool threatensPlayer = candidate.Type == StormLightningAttackType.PlayerStrike
                    || targetDistance <= warningRange;
                if (!threatensPlayer || candidate.SecondsRemaining >= bestTime)
                {
                    continue;
                }

                warning = candidate;
                bestTime = candidate.SecondsRemaining;
                found = true;
            }

            for (int i = 0; i < _orbEnemies.Count; i++)
            {
                PlayerStrikeOrbEnemy enemy = _orbEnemies[i];
                if (enemy == null || !enemy.TryGetThreatWarning(out StormPyramidThreatWarning candidate) ||
                    candidate.SecondsRemaining >= bestTime)
                {
                    continue;
                }

                warning = candidate;
                bestTime = candidate.SecondsRemaining;
                found = true;
            }

            return found;
        }

        private void EnsureStyles(float scale)
        {
            if (_titleStyle != null && Mathf.Approximately(_styledScale, scale))
            {
                return;
            }

            _styledScale = scale;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(22f * scale),
                fontStyle = FontStyle.Bold,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            _detailStyle = new GUIStyle(_titleStyle)
            {
                fontSize = Mathf.RoundToInt(14f * scale),
                fontStyle = FontStyle.Normal,
            };
            _markerStyle = new GUIStyle(_titleStyle)
            {
                fontSize = Mathf.RoundToInt(16f * scale),
            };
            _arrowStyle = new GUIStyle(_titleStyle)
            {
                fontSize = Mathf.RoundToInt(30f * scale),
            };
        }

        private void DrawScreenBorder(Color warningColor, float scale)
        {
            float thickness = Mathf.Max(3f, 7f * scale);
            Color previousColor = GUI.color;
            GUI.color = warningColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, Screen.height - thickness, Screen.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, thickness, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - thickness, 0f, thickness, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawWarningPanel(StormPyramidThreatWarning threat, Color warningColor, float scale)
        {
            float panelWidth = 360f * scale;
            float panelHeight = 76f * scale;
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                152f * scale,
                panelWidth,
                panelHeight);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.035f, 0.055f, 0.9f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = warningColor;
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 4f * scale), Texture2D.whiteTexture);
            GUI.color = previousColor;

            string attackLabel = threat.Type == StormLightningAttackType.PlayerStrike
                ? "LIGHTNING LOCK"
                : "GROUND STRIKE NEARBY";
            GUI.Label(
                new Rect(panel.x, panel.y + (5f * scale), panel.width, 31f * scale),
                attackLabel,
                _titleStyle);
            GUI.Label(
                new Rect(panel.x, panel.y + (34f * scale), panel.width, 19f * scale),
                $"IMPACT IN {threat.SecondsRemaining:0.0} s",
                _detailStyle);

            Rect bar = new Rect(
                panel.x + (20f * scale),
                panel.yMax - (15f * scale),
                panel.width - (40f * scale),
                6f * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = warningColor;
            GUI.DrawTexture(
                new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(threat.ChargeNormalized), bar.height),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawTargetMarker(StormPyramidThreatWarning threat, Color warningColor, float scale)
        {
            Vector3 projected = _camera.WorldToScreenPoint(threat.TargetPosition);
            Vector2 screenPoint = new Vector2(projected.x, Screen.height - projected.y);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float padding = Mathf.Max(12f, _settings.WarningEdgePadding) * scale;
            bool onScreen = projected.z > 0f
                && screenPoint.x >= padding
                && screenPoint.x <= Screen.width - padding
                && screenPoint.y >= padding
                && screenPoint.y <= Screen.height - padding;
            Vector2 direction = screenPoint - screenCenter;
            Vector2 markerPosition = screenPoint;

            if (!onScreen)
            {
                if (projected.z <= 0f)
                {
                    direction = -direction;
                }
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = Vector2.up;
                }
                direction.Normalize();
                float horizontalScale = (screenCenter.x - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.x));
                float verticalScale = (screenCenter.y - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.y));
                markerPosition = screenCenter + (direction * Mathf.Min(horizontalScale, verticalScale));

                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
                Matrix4x4 previousMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, markerPosition);
                _arrowStyle.normal.textColor = warningColor;
                GUI.Label(
                    new Rect(markerPosition.x - (24f * scale), markerPosition.y - (24f * scale), 48f * scale, 48f * scale),
                    "▲",
                    _arrowStyle);
                GUI.matrix = previousMatrix;
            }

            float markerPulse = 1f + (Mathf.Sin(Time.unscaledTime * Mathf.Max(0f, _settings.WarningPulseSpeed)) * 0.12f);
            float markerSize = 42f * scale * markerPulse;
            Color previousColor = GUI.color;
            GUI.color = warningColor;
            Rect marker = new Rect(
                markerPosition.x - (markerSize * 0.5f),
                markerPosition.y - (markerSize * 0.5f),
                markerSize,
                markerSize);
            GUI.DrawTexture(new Rect(marker.x, marker.y, marker.width, 3f * scale), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.x, marker.yMax - (3f * scale), marker.width, 3f * scale), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.x, marker.y, 3f * scale, marker.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.xMax - (3f * scale), marker.y, 3f * scale, marker.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            bool isStrikeOrb = threat.Type == StormLightningAttackType.PlayerStrike
                && threat.ThreatRange > 0f;
            float distance = Vector3.Distance(
                _player.WorldCenter,
                isStrikeOrb ? threat.ThreatOrigin : threat.TargetPosition);
            string markerText = isStrikeOrb
                ? $"{distance:0} m"
                : $"STRIKE  {distance:0} m";
            _markerStyle.normal.textColor = warningColor;
            GUI.Label(
                new Rect(markerPosition.x - (75f * scale), markerPosition.y + (24f * scale), 150f * scale, 24f * scale),
                markerText,
                _markerStyle);
        }
    }

    [DefaultExecutionOrder(1350)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorStormPyramidDirector : MonoBehaviour
    {
        private readonly List<StormPyramidEnemy> _enemies = new List<StormPyramidEnemy>();
        private readonly List<StormPyramidEnemy> _baseEnemies = new List<StormPyramidEnemy>();
        private readonly List<StormPyramidEnemy> _contractEnemies = new List<StormPyramidEnemy>();
        private readonly List<StormPyramidEnemy> _riskEnemies = new List<StormPyramidEnemy>();
        private readonly List<PlayerStrikeOrbEnemy> _orbEnemies = new List<PlayerStrikeOrbEnemy>();
        private readonly List<PlayerStrikeOrbEnemy> _baseOrbEnemies = new List<PlayerStrikeOrbEnemy>();
        private readonly List<PlayerStrikeOrbEnemy> _riskOrbEnemies = new List<PlayerStrikeOrbEnemy>();
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private StormPyramidTuning _settings;
        private GroundExploderTuning _groundExploderSettings;
        private PlayerStrikeOrbTuning _orbSettings;
        private StormPyramidThreatHUD _warningHud;
        private bool _useDesertDeploymentSpawnDistance;
        private int _desertDeploymentSpawnIndex;
        private int _desertDeploymentSpawnCount;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            StormPyramidTuning settings,
            GroundExploderTuning groundExploderSettings,
            PlayerStrikeOrbTuning orbSettings)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _groundExploderSettings = groundExploderSettings;
            _orbSettings = orbSettings;
            _world.WorldShifted += HandleWorldShift;
            SpawnBaseEnemies();

            _warningHud = gameObject.AddComponent<StormPyramidThreatHUD>();
            _warningHud.Initialize(player, Camera.main, settings, _enemies, _orbEnemies);
        }

        private void SpawnBaseEnemies()
        {
            System.Random random = new System.Random(unchecked(_world.EnemySpawnSeed ^ 0x2749a31));
            if (_settings.Enabled)
            {
                int count = Mathf.Max(1, _settings.EnemyCount);
                for (int i = 0; i < count; i++)
                {
                    StormPyramidEnemy enemy = SpawnEnemy(
                        random,
                        $"Storm Pyramid {i + 1:00}",
                        i + 1,
                        GetSpawnHeightNormalized(i, count));
                    _baseEnemies.Add(enemy);
                }
            }

            if (_orbSettings.Enabled)
            {
                int count = Mathf.Max(1, _orbSettings.EnemyCount);
                for (int i = 0; i < count; i++)
                {
                    PlayerStrikeOrbEnemy enemy = SpawnOrbEnemy(
                        random,
                        $"Strike Orb {i + 1:00}",
                        10000 + i + 1,
                        GetSpawnHeightNormalized(i, count));
                    _baseOrbEnemies.Add(enemy);
                }
            }
        }

        private void RespawnBaseEnemies()
        {
            ClearEnemies(_baseEnemies, _enemies);
            ClearEnemies(_baseOrbEnemies, _orbEnemies);
            SpawnBaseEnemies();
        }

        public void SetContractBonusEnemies(int count, int seed)
        {
            ClearContractBonusEnemies();
            if (count <= 0 || _player == null || _playerHealth == null || _world == null ||
                _materials == null || _settings == null)
            {
                return;
            }
            System.Random random = new System.Random(unchecked(seed ^ _world.EnemySpawnSeed ^ 0x6f18d2b));
            for (int i = 0; i < count; i++)
            {
                StormPyramidEnemy enemy = SpawnEnemy(
                    random,
                    $"High-Value Storm Pyramid {i + 1:00}",
                    unchecked(seed + 1000 + i),
                    GetSpawnHeightNormalized(i, count));
                if (enemy != null)
                {
                    enemy.enabled = enabled;
                    _contractEnemies.Add(enemy);
                }
            }
        }

        public void ClearContractBonusEnemies()
        {
            for (int i = 0; i < _contractEnemies.Count; i++)
            {
                StormPyramidEnemy enemy = _contractEnemies[i];
                _enemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            _contractEnemies.Clear();
        }

        private StormPyramidEnemy SpawnEnemy(
            System.Random random,
            string objectName,
            int identity,
            float normalizedHeight)
        {
            int deploymentIndex = _desertDeploymentSpawnIndex;
            float angle = _useDesertDeploymentSpawnDistance
                ? GetDesertDeploymentAngleRadians(deploymentIndex)
                : (float)(random.NextDouble() * Mathf.PI * 2f);
            float distance01 = (float)random.NextDouble();
            float distance;
            if (_useDesertDeploymentSpawnDistance)
            {
                _desertDeploymentSpawnIndex++;
                distance = DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(
                    deploymentIndex,
                    _desertDeploymentSpawnCount,
                    _world);
            }
            else
            {
                float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                    _settings.MinimumSpawnDistance,
                    _settings.DetectionRange,
                    _world);
                distance = Mathf.Lerp(
                    minimumDistance,
                    DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                        minimumDistance,
                        _settings.MinimumSpawnDistance,
                        _settings.MaximumSpawnDistance),
                    distance01);
            }
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 spawnPosition = playerPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                Mathf.Clamp01(normalizedHeight));
            spawnPosition.y = _world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z)
                + _settings.HoverHeight
                + heightVariation;

            GameObject enemyObject = new GameObject(objectName);
            enemyObject.transform.SetParent(transform, true);
            enemyObject.transform.position = spawnPosition;
            StormPyramidEnemy enemy = enemyObject.AddComponent<StormPyramidEnemy>();
            enemy.Initialize(
                _player,
                _playerHealth,
                _world,
                _materials,
                _settings,
                _groundExploderSettings,
                identity);
            _enemies.Add(enemy);
            return enemy;
        }

        private PlayerStrikeOrbEnemy SpawnOrbEnemy(
            System.Random random,
            string objectName,
            int identity,
            float normalizedHeight)
        {
            int deploymentIndex = _desertDeploymentSpawnIndex;
            float angle = _useDesertDeploymentSpawnDistance
                ? GetDesertDeploymentAngleRadians(deploymentIndex)
                : (float)(random.NextDouble() * Mathf.PI * 2f);
            float distance01 = (float)random.NextDouble();
            float distance;
            if (_useDesertDeploymentSpawnDistance)
            {
                _desertDeploymentSpawnIndex++;
                distance = DuneVectorEnemyEngagementRing.ResolveDesertDeploymentDistance(
                    deploymentIndex,
                    _desertDeploymentSpawnCount,
                    _world);
            }
            else
            {
                float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                    _orbSettings.MinimumSpawnDistance,
                    _orbSettings.EvaluateDetectionRange(DuneVectorContractRisk.CurrentRisk),
                    _world);
                distance = Mathf.Lerp(
                    minimumDistance,
                    DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                        minimumDistance,
                        _orbSettings.MinimumSpawnDistance,
                        _orbSettings.MaximumSpawnDistance),
                    distance01);
            }
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 spawnPosition = playerPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_orbSettings.HoverHeightVariance,
                _orbSettings.HoverHeightVariance,
                Mathf.Clamp01(normalizedHeight));
            spawnPosition.y = _world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z)
                + _orbSettings.HoverHeight
                + heightVariation;

            GameObject enemyObject = new GameObject(objectName);
            enemyObject.transform.SetParent(transform, true);
            enemyObject.transform.position = spawnPosition;
            PlayerStrikeOrbEnemy enemy = enemyObject.AddComponent<PlayerStrikeOrbEnemy>();
            enemy.Initialize(_player, _playerHealth, _world, _materials, _orbSettings, identity);
            _orbEnemies.Add(enemy);
            return enemy;
        }

        public void SetGameplayActive(bool active)
        {
            enabled = active;
            if (active)
            {
                _useDesertDeploymentSpawnDistance = true;
                _desertDeploymentSpawnIndex = 0;
                _desertDeploymentSpawnCount = GetDesertDeploymentSpawnCount();
                try
                {
                    RespawnBaseEnemies();
                    SpawnRiskEnemies();
                }
                finally
                {
                    _useDesertDeploymentSpawnDistance = false;
                }
            }
            else
            {
                ClearRiskEnemies();
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].SetGameplayActive(active);
                }
            }
            for (int i = 0; i < _orbEnemies.Count; i++)
            {
                if (_orbEnemies[i] != null)
                {
                    _orbEnemies[i].SetGameplayActive(active);
                }
            }
            if (_warningHud != null)
            {
                _warningHud.enabled = active;
            }
        }

        private int GetDesertDeploymentSpawnCount()
        {
            int count = 0;
            float bonusMultiplier = Mathf.Max(0f, DuneVectorContractRisk.EnemySpawnMultiplier - 1f);
            if (_settings.Enabled)
            {
                int baseCount = Mathf.Max(1, _settings.EnemyCount);
                count += baseCount + Mathf.CeilToInt(baseCount * bonusMultiplier);
            }
            if (_orbSettings.Enabled)
            {
                int baseCount = Mathf.Max(1, _orbSettings.EnemyCount);
                count += baseCount + Mathf.CeilToInt(baseCount * bonusMultiplier);
            }
            return Mathf.Max(1, count);
        }

        private float GetDesertDeploymentAngleRadians(int index)
        {
            const float GoldenAngleDegrees = 137.50776f;
            float seedAngle = Mathf.Repeat(_world.EnemySpawnSeed * 0.6180339f, 360f);
            return (seedAngle + (index * GoldenAngleDegrees)) * Mathf.Deg2Rad;
        }

        public void SetHubLightningActive()
        {
            enabled = true;
            ClearRiskEnemies();
            _useDesertDeploymentSpawnDistance = true;
            _desertDeploymentSpawnIndex = 0;
            _desertDeploymentSpawnCount = GetHubDeploymentSpawnCount();
            try
            {
                RespawnBaseEnemies();
            }
            finally
            {
                _useDesertDeploymentSpawnDistance = false;
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].SetLightningOnlyActive();
                }
            }
            for (int i = 0; i < _orbEnemies.Count; i++)
            {
                if (_orbEnemies[i] != null)
                {
                    _orbEnemies[i].SetGameplayActive(false);
                }
            }
            if (_warningHud != null)
            {
                _warningHud.enabled = true;
            }
        }

        private int GetHubDeploymentSpawnCount()
        {
            int count = 0;
            if (_settings.Enabled)
            {
                count += Mathf.Max(1, _settings.EnemyCount);
            }
            if (_orbSettings.Enabled)
            {
                count += Mathf.Max(1, _orbSettings.EnemyCount);
            }
            return Mathf.Max(1, count);
        }

        private static void ClearEnemies<T>(List<T> source, List<T> allEnemies)
            where T : MonoBehaviour
        {
            for (int i = 0; i < source.Count; i++)
            {
                T enemy = source[i];
                allEnemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            source.Clear();
        }

        private void SpawnRiskEnemies()
        {
            ClearRiskEnemies();
            float bonusMultiplier = Mathf.Max(0f, DuneVectorContractRisk.EnemySpawnMultiplier - 1f);
            int multiplierSeed = Mathf.RoundToInt(DuneVectorContractRisk.EnemySpawnMultiplier * 1000f);
            System.Random random = new System.Random(unchecked(_world.EnemySpawnSeed ^ 0x38bde17 ^ multiplierSeed));

            if (_settings.Enabled)
            {
                int bonusCount = Mathf.CeilToInt(Mathf.Max(1, _settings.EnemyCount) * bonusMultiplier);
                for (int i = 0; i < bonusCount; i++)
                {
                    StormPyramidEnemy enemy = SpawnEnemy(
                        random,
                        $"Risk Storm Pyramid {i + 1:00}",
                        60000 + i + 1,
                        GetSpawnHeightNormalized(i, bonusCount));
                    enemy.enabled = enabled;
                    _riskEnemies.Add(enemy);
                }
            }

            if (_orbSettings.Enabled)
            {
                int bonusCount = Mathf.CeilToInt(Mathf.Max(1, _orbSettings.EnemyCount) * bonusMultiplier);
                for (int i = 0; i < bonusCount; i++)
                {
                    PlayerStrikeOrbEnemy enemy = SpawnOrbEnemy(
                        random,
                        $"Risk Strike Orb {i + 1:00}",
                        70000 + i + 1,
                        GetSpawnHeightNormalized(i, bonusCount));
                    enemy.enabled = enabled;
                    _riskOrbEnemies.Add(enemy);
                }
            }
        }

        private static float GetSpawnHeightNormalized(int index, int count)
        {
            return count <= 1 ? 0.5f : index / (float)(count - 1);
        }

        private void ClearRiskEnemies()
        {
            for (int i = 0; i < _riskEnemies.Count; i++)
            {
                StormPyramidEnemy enemy = _riskEnemies[i];
                _enemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            _riskEnemies.Clear();

            for (int i = 0; i < _riskOrbEnemies.Count; i++)
            {
                PlayerStrikeOrbEnemy enemy = _riskOrbEnemies[i];
                _orbEnemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            _riskOrbEnemies.Clear();
        }

        private void HandleWorldShift(Vector3 shift)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].ApplyWorldShift(shift);
                }
            }
            for (int i = 0; i < _orbEnemies.Count; i++)
            {
                if (_orbEnemies[i] != null)
                {
                    _orbEnemies[i].ApplyWorldShift(shift);
                }
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
