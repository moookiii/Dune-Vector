using System.Collections.Generic;
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

        public StormPyramidThreatWarning(
            StormLightningAttackType type,
            Vector3 targetPosition,
            float secondsRemaining,
            float chargeNormalized)
        {
            Type = type;
            TargetPosition = targetPosition;
            SecondsRemaining = secondsRemaining;
            ChargeNormalized = chargeNormalized;
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidEnemy : MonoBehaviour
    {
        public StormPyramidState CurrentState { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private StormPyramidTuning _settings;
        private StormPyramidMovement _movement;
        private StormPyramidTargeting _targeting;
        private StormPyramidAttackSelector _attackSelector;
        private StormPyramidLightningAttack _lightning;
        private Transform _visual;
        private Transform _core;
        private Transform _counterRotator;
        private float _stateTime;
        private float _attackTimer;
        private int _identity;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            StormPyramidTuning settings,
            int identity)
        {
            _player = player;
            _playerHealth = playerHealth;
            _settings = settings;
            _identity = identity;

            _visual = DuneVectorVisuals.CreateStormPyramidVisual(transform, materials, settings);
            _core = _visual.Find("Storm Core");
            _counterRotator = _visual.Find("Counter Rotator");
            Transform halo = _visual.Find("Charge Halo");
            Transform lightningOrigin = _visual.Find("Lightning Origin");

            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.MaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, settings.VisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, player != null ? player.GetComponent<DroneGoldWallet>() : null, settings.GoldReward);

            _movement = gameObject.AddComponent<StormPyramidMovement>();
            _movement.Initialize(player, world, settings, identity);

            _targeting = gameObject.AddComponent<StormPyramidTargeting>();
            _targeting.Initialize(player, world, settings);

            _attackSelector = gameObject.AddComponent<StormPyramidAttackSelector>();
            _attackSelector.Initialize(_targeting);

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
                identity);

            _attackTimer = settings.PlayerStrikeAttackInterval * Mathf.Lerp(
                0.35f,
                1f,
                Mathf.Repeat((identity * 0.417f) + 0.21f, 1f));
            SetState(StormPyramidState.IdleHovering);
        }

        private void Update()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                _lightning?.CancelAttack();
                return;
            }

            float deltaTime = Time.deltaTime;
            _stateTime += deltaTime;

            bool mayDrift = CurrentState == StormPyramidState.IdleHovering
                || CurrentState == StormPyramidState.Cooldown;
            if (mayDrift)
            {
                _movement.Tick(deltaTime);
            }

            bool mayReposition = CurrentState != StormPyramidState.ChargingAttack
                && CurrentState != StormPyramidState.FiringLightning;
            if (mayReposition && _movement.DistanceFromPlayer > _settings.RepositionDistance)
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
            _attackTimer = Mathf.Max(0f, _attackTimer - deltaTime);
            if (_attackTimer <= 0f && CanBeginAttack())
            {
                SetState(StormPyramidState.TrackingPlayer);
            }
        }

        private void UpdateTracking()
        {
            if (!CanBeginAttack())
            {
                _attackTimer = GetAttackInterval(StormLightningAttackType.PlayerStrike);
                SetState(StormPyramidState.IdleHovering);
                return;
            }

            FacePosition(_player.WorldCenter, 6f);
            if (_stateTime < _settings.TrackingDuration)
            {
                return;
            }

            StormLightningTarget target = _attackSelector.SelectAttack(
                transform.position,
                CanChargePlayer());
            _lightning.BeginCharge(target);
            SetState(StormPyramidState.ChargingAttack);
        }

        private void UpdateCharging(float deltaTime)
        {
            if (_lightning.TargetType == StormLightningAttackType.PlayerStrike && !CanChargePlayer())
            {
                _lightning.CancelAttack();
                _attackTimer = GetAttackInterval(StormLightningAttackType.PlayerStrike);
                SetState(StormPyramidState.IdleHovering);
                return;
            }

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
            if (_stateTime >= _settings.Cooldown)
            {
                _attackTimer = GetAttackInterval(_lightning.TargetType);
                SetState(StormPyramidState.IdleHovering);
            }
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
            _attackTimer = _settings.PlayerStrikeAttackInterval;
            SetState(StormPyramidState.IdleHovering);
        }

        private float GetAttackInterval(StormLightningAttackType attackType)
        {
            float configuredInterval = attackType == StormLightningAttackType.PlayerStrike
                ? _settings.PlayerStrikeAttackInterval
                : _settings.AttackInterval;
            return Mathf.Max(0.1f, configuredInterval);
        }

        private bool CanChargePlayer()
        {
            return !_player.IsStableGrounded
                && _targeting.CanTargetPlayer(transform.position);
        }

        private bool CanBeginAttack()
        {
            return _player.IsStableGrounded || CanChargePlayer();
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

            if (CurrentState == StormPyramidState.TrackingPlayer)
            {
                StormLightningTarget target = _attackSelector.SelectAttack(
                    transform.position,
                    CanChargePlayer());
                float chargeDuration = _lightning.GetChargeDuration(target.Type);
                float totalWarningDuration = Mathf.Max(0.01f, _settings.TrackingDuration + chargeDuration);
                warning = new StormPyramidThreatWarning(
                    target.Type,
                    target.Position,
                    Mathf.Max(0f, _settings.TrackingDuration - _stateTime) + chargeDuration,
                    Mathf.Clamp01(_stateTime / totalWarningDuration));
                return true;
            }

            if (CurrentState == StormPyramidState.ChargingAttack)
            {
                float chargeDuration = _lightning.GetChargeDuration(_lightning.TargetType);
                float totalWarningDuration = Mathf.Max(0.01f, _settings.TrackingDuration + chargeDuration);
                float elapsedCharge = Mathf.Max(0f, chargeDuration - _lightning.ChargeSecondsRemaining);
                warning = new StormPyramidThreatWarning(
                    _lightning.TargetType,
                    _lightning.TargetPosition,
                    _lightning.ChargeSecondsRemaining,
                    Mathf.Clamp01((_settings.TrackingDuration + elapsedCharge) / totalWarningDuration));
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
            Gizmos.DrawWireSphere(_lightning != null ? _lightning.TargetPosition : transform.position, _settings.StrikeRadius);
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidMovement : MonoBehaviour
    {
        public float DistanceFromPlayer => _player != null
            ? Vector3.Distance(transform.position, _player.WorldCenter)
            : 0f;

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private StormPyramidTuning _settings;
        private Vector3 _patrolCenter;
        private float _fixedAltitude;
        private float _phase;
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
        }

        public void Tick(float deltaTime)
        {
            float range = Mathf.Max(0f, _settings.PatrolDriftRange);
            float speed = Mathf.Max(0f, _settings.PatrolDriftSpeed);
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
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                distance01);
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 position = playerPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            float terrainHeight = _world.SampleHeightAtLocal(position.x, position.z);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                Mathf.Repeat((_identity * 0.619f) + (_repositionCount * 0.347f), 1f));
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
        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private StormPyramidTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            StormPyramidTuning settings)
        {
            _player = player;
            _world = world;
            _settings = settings;
        }

        public bool CanTargetPlayer(Vector3 origin)
        {
            return _player != null
                && Vector3.Distance(origin, _player.WorldCenter) <= _settings.DetectionRange;
        }

        public Vector3 GetPlayerPosition()
        {
            return _player.WorldCenter;
        }

        public Vector3 GetGroundPointBelow(Vector3 origin)
        {
            float groundHeight = _world.SampleHeightAtLocal(origin.x, origin.z);
            return new Vector3(origin.x, groundHeight + 0.08f, origin.z);
        }
    }

    [DisallowMultipleComponent]
    public sealed class StormPyramidAttackSelector : MonoBehaviour
    {
        private StormPyramidTargeting _targeting;

        public void Initialize(StormPyramidTargeting targeting)
        {
            _targeting = targeting;
        }

        public StormLightningTarget SelectAttack(Vector3 origin, bool canTargetPlayer)
        {
            if (canTargetPlayer)
            {
                return new StormLightningTarget(
                    StormLightningAttackType.PlayerStrike,
                    _targeting.GetPlayerPosition());
            }

            return new StormLightningTarget(
                StormLightningAttackType.GroundStrike,
                _targeting.GetGroundPointBelow(origin));
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

        public void ResolvePlayerStrike(Vector3 attackOrigin, float attackRange, float damage)
        {
            if (_player == null || _health == null || _health.IsDead || attackRange <= 0f || damage <= 0f)
            {
                return;
            }

            if (Vector3.Distance(_player.WorldCenter, attackOrigin) <= attackRange)
            {
                _health.TakeDamage(damage);
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

        private const int LightningSegments = 11;
        private readonly Vector3[] _lightningPositions = new Vector3[LightningSegments];
        private Transform _owner;
        private Transform _origin;
        private Transform _core;
        private Transform _halo;
        private Transform _marker;
        private Transform _impactFlash;
        private LineRenderer _chargeLine;
        private LineRenderer _lightningLine;
        private StormPyramidTuning _settings;
        private StormPyramidLightningDamage _damage;
        private StormLightningTarget _target;
        private float _timer;
        private float _chargeDuration;
        private int _identity;
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
            int identity)
        {
            _owner = owner;
            _origin = origin != null ? origin : owner;
            _core = core;
            _halo = halo;
            _settings = settings;
            _damage = damage;
            _identity = identity;
            _marker = DuneVectorVisuals.CreateStormStrikeMarker(owner.parent, materials, settings.StrikeRadius);
            _impactFlash = _marker.Find("Strike Impact Flash");
            _chargeLine = CreateLine("Lightning Charge Telegraph", materials.LightningWarning, settings.ChargeTelegraphWidth);
            _lightningLine = CreateLine("Lightning Bolt", materials.Lightning, settings.LightningWidth);
            CancelAttack();
        }

        public void BeginCharge(StormLightningTarget target)
        {
            _target = target;
            _timer = 0f;
            _chargeDuration = GetChargeDuration(target.Type);
            _charging = true;
            _firing = false;
            _marker.gameObject.SetActive(true);
            _marker.position = target.Position;
            _marker.localScale = Vector3.one * 0.25f;
            if (_impactFlash != null)
            {
                _impactFlash.localScale = Vector3.zero;
            }
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
            _charging = false;
            _firing = true;
            _timer = 0f;
            _chargeLine.enabled = false;
            _lightningLine.enabled = true;
            _damage.ResolvePlayerStrike(_owner.position, _settings.DetectionRange, _settings.LightningDamage);
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
            float duration = Mathf.Max(0.01f, _settings.LightningVisualDuration);
            float life01 = Mathf.Clamp01(_timer / duration);
            if (_impactFlash != null)
            {
                float flash = Mathf.Sin(life01 * Mathf.PI) * _settings.StrikeRadius * 0.34f;
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
            if (_core != null) _core.localScale = _settings.CoreScale;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _target = new StormLightningTarget(_target.Type, _target.Position + shift);
            if (_marker != null)
            {
                _marker.position += shift;
            }
            if (_charging)
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
            float configuredDuration = targetType == StormLightningAttackType.PlayerStrike
                ? _settings.PlayerStrikeChargeTime
                : _settings.ChargeTime;
            return Mathf.Max(0.01f, configuredDuration);
        }

        private void UpdateChargeVisual(float charge01)
        {
            float pulse = 0.88f + (Mathf.Sin((Time.time * 12f) + _identity) * 0.12f);
            _marker.position = _target.Position;
            _marker.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, charge01) * pulse;
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
            line.startWidth = width;
            line.endWidth = width * 0.65f;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
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
        private GUIStyle _titleStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _markerStyle;
        private GUIStyle _arrowStyle;
        private float _styledScale = -1f;

        public void Initialize(
            DroneCharacterController player,
            Camera viewCamera,
            StormPyramidTuning settings,
            List<StormPyramidEnemy> enemies)
        {
            _player = player;
            _camera = viewCamera;
            _settings = settings;
            _enemies = enemies;
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (_player == null || _settings == null || _enemies == null)
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

            float scale = Mathf.Clamp(_settings.WarningHudScale, 0.6f, 2f);
            EnsureStyles(scale);
            float pulseSpeed = Mathf.Max(0f, _settings.WarningPulseSpeed);
            float pulse = 0.72f + (Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.18f);
            Color warningColor = _settings.WarningColor;
            warningColor.a = Mathf.Clamp01(pulse);

            DrawScreenBorder(warningColor, scale);
            DrawWarningPanel(threat, warningColor, scale);
            DrawTargetMarker(threat, warningColor, scale);
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

            float distance = Vector3.Distance(_player.WorldCenter, threat.TargetPosition);
            _markerStyle.normal.textColor = warningColor;
            GUI.Label(
                new Rect(markerPosition.x - (75f * scale), markerPosition.y + (24f * scale), 150f * scale, 24f * scale),
                $"STRIKE  {distance:0} m",
                _markerStyle);
        }
    }

    [DefaultExecutionOrder(1350)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorStormPyramidDirector : MonoBehaviour
    {
        private readonly List<StormPyramidEnemy> _enemies = new List<StormPyramidEnemy>();
        private DesertWorldStreamer _world;
        private StormPyramidThreatHUD _warningHud;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            StormPyramidTuning settings)
        {
            _world = world;
            _world.WorldShifted += HandleWorldShift;
            System.Random random = new System.Random(unchecked(world.WorldSeed ^ 0x2749a31));
            int count = Mathf.Max(1, settings.EnemyCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float distance = Mathf.Lerp(
                    settings.MinimumSpawnDistance,
                    Mathf.Max(settings.MinimumSpawnDistance, settings.MaximumSpawnDistance),
                    (float)random.NextDouble());
                Vector3 playerPosition = player.WorldCenter;
                Vector3 spawnPosition = playerPosition + new Vector3(
                    Mathf.Cos(angle) * distance,
                    0f,
                    Mathf.Sin(angle) * distance);
                float heightVariation = Mathf.Lerp(
                    -settings.HoverHeightVariance,
                    settings.HoverHeightVariance,
                    (float)random.NextDouble());
                spawnPosition.y = world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z)
                    + settings.HoverHeight
                    + heightVariation;

                GameObject enemyObject = new GameObject($"Storm Pyramid {i + 1:00}");
                enemyObject.transform.SetParent(transform, true);
                enemyObject.transform.position = spawnPosition;
                StormPyramidEnemy enemy = enemyObject.AddComponent<StormPyramidEnemy>();
                enemy.Initialize(player, playerHealth, world, materials, settings, i + 1);
                _enemies.Add(enemy);
            }

            _warningHud = gameObject.AddComponent<StormPyramidThreatHUD>();
            _warningHud.Initialize(player, Camera.main, settings, _enemies);
        }

        public void SetGameplayActive(bool active)
        {
            enabled = active;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].enabled = active;
                }
            }
            if (_warningHud != null)
            {
                _warningHud.enabled = active;
            }
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
