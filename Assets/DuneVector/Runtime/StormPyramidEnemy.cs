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
        private StormPyramidTuning _settings;
        private StormPyramidMovement _movement;
        private StormPyramidTargeting _targeting;
        private StormPyramidLightningAttack _lightning;
        private Transform _visual;
        private Transform _core;
        private Transform _counterRotator;
        private StormLightningTarget _trackedTarget;
        private float _stateTime;
        private float _attackTimer;
        private int _identity;
        private bool _gameplayActive = true;

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
                identity);

            _attackTimer = GetAttackInterval() * Mathf.Lerp(
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
            if (!_gameplayActive)
            {
                _movement.Tick(deltaTime);
                UpdatePresentation(deltaTime);
                return;
            }

            _stateTime += deltaTime;

            bool mayDrift = CurrentState == StormPyramidState.IdleHovering
                || CurrentState == StormPyramidState.Cooldown;
            if (mayDrift)
            {
                _movement.Tick(deltaTime);
            }

            bool mayReposition = CurrentState != StormPyramidState.ChargingAttack
                && CurrentState != StormPyramidState.FiringLightning;
            if (mayReposition && _movement.HorizontalDistanceFromPlayer > _settings.RepositionDistance)
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
            _lightning.BeginCharge(_trackedTarget);
            SetState(StormPyramidState.ChargingAttack);
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
                _attackTimer = GetAttackInterval();
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
            _attackTimer = GetAttackInterval();
            SetState(StormPyramidState.IdleHovering);
        }

        public void SetGameplayActive(bool active)
        {
            _gameplayActive = active;
            if (!active)
            {
                _lightning?.CancelAttack();
                _attackTimer = GetAttackInterval();
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

            if (CurrentState == StormPyramidState.ChargingAttack)
            {
                float chargeDuration = _lightning.GetChargeDuration(_lightning.TargetType);
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
        private Transform _outerWarningRing;
        private Transform _innerWarningRing;
        private Transform _groundImpactWave;
        private LineRenderer _chargeLine;
        private LineRenderer _lightningLine;
        private StormPyramidTuning _settings;
        private StormPyramidLightningDamage _damage;
        private StormLightningTarget _target;
        private float _timer;
        private float _chargeDuration;
        private float _strikeRadius;
        private float _strikeRadiusScale = 1f;
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
            _outerWarningRing = _marker.Find("Outer Warning Ring");
            _innerWarningRing = _marker.Find("Inner Warning Ring");
            _groundImpactWave = DuneVectorVisuals.CreateStormGroundImpactWave(
                _marker,
                materials,
                settings.StrikeRadius,
                settings.GroundImpactRingThickness,
                settings.GroundImpactHeightOffset);
            _chargeLine = CreateLine("Lightning Charge Telegraph", materials.LightningWarning, settings.ChargeTelegraphWidth);
            _lightningLine = CreateLine("Lightning Bolt", materials.Lightning, settings.LightningWidth);
            CancelAttack();
        }

        public void BeginCharge(StormLightningTarget target)
        {
            _target = target;
            _strikeRadius = _settings.EvaluateStrikeRadius(DuneVectorContractRisk.CurrentRisk);
            _strikeRadiusScale = _strikeRadius / Mathf.Max(0.1f, _settings.StrikeRadius);
            _timer = 0f;
            _chargeDuration = GetChargeDuration(target.Type);
            _charging = true;
            _firing = false;
            _marker.gameObject.SetActive(true);
            _marker.position = target.Position;
            _marker.localScale = Vector3.one * (0.25f * _strikeRadiusScale);
            if (_impactFlash != null)
            {
                _impactFlash.localScale = Vector3.zero;
            }
            SetWarningRingsActive(true);
            if (_groundImpactWave != null)
            {
                _groundImpactWave.gameObject.SetActive(false);
                _groundImpactWave.localScale = Vector3.zero;
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
            _marker.localScale = Vector3.one * _strikeRadiusScale;
            SetWarningRingsActive(false);
            if (_groundImpactWave != null)
            {
                _groundImpactWave.gameObject.SetActive(true);
                _groundImpactWave.localScale = Vector3.one * _settings.GroundImpactStartScale;
            }
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
            _charging = false;
            _firing = false;
            if (_chargeLine != null) _chargeLine.enabled = false;
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
            return Mathf.Max(0.01f, _settings.ChargeTime);
        }

        private void SetWarningRingsActive(bool active)
        {
            if (_outerWarningRing != null) _outerWarningRing.gameObject.SetActive(active);
            if (_innerWarningRing != null) _innerWarningRing.gameObject.SetActive(active);
        }

        private void UpdateChargeVisual(float charge01)
        {
            float pulse = 0.88f + (Mathf.Sin((Time.time * 12f) + _identity) * 0.12f);
            _marker.position = _target.Position;
            _marker.localScale = Vector3.one
                * Mathf.Lerp(0.25f, 1f, charge01)
                * pulse
                * _strikeRadiusScale;
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
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerStrikeOrbEnemy : MonoBehaviour
    {
        public StormPyramidState CurrentState { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private PlayerStrikeOrbTuning _settings;
        private PlayerStrikeOrbMovement _movement;
        private PlayerStrikeOrbTargeting _targeting;
        private PlayerStrikeOrbLightningAttack _lightning;
        private EnemyHealth _enemyHealth;
        private DuneVectorMaterials _materials;
        private Transform _visual;
        private Transform[] _orbPivots;
        private Vector3 _trackedTarget;
        private float _stateTime;
        private float _attackTimer;
        private int _identity;
        private Vector3 _previousPlayerPosition;
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
            _settings = settings;
            _materials = materials;
            _identity = identity;

            _visual = DuneVectorVisuals.CreatePlayerStrikeOrbVisual(transform, materials, settings);
            int orbitingOrbCount = settings.OrbitingOrbs != null
                ? settings.OrbitingOrbs.Length
                : 0;
            _orbPivots = new Transform[orbitingOrbCount];
            for (int i = 0; i < orbitingOrbCount; i++)
            {
                _orbPivots[i] = _visual.Find($"Orbiting Orb Pivot {i + 1}");
            }
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

            StormPyramidLightningDamage damage = gameObject.AddComponent<StormPyramidLightningDamage>();
            damage.Initialize(player, playerHealth);
            _lightning = gameObject.AddComponent<PlayerStrikeOrbLightningAttack>();
            _lightning.Initialize(
                transform,
                lightningOrigin,
                halo,
                materials,
                settings,
                damage,
                identity);

            _attackTimer = settings.AttackInterval * Mathf.Lerp(
                settings.MinimumInitialAttackDelayMultiplier,
                1f,
                Mathf.Repeat((identity * 0.371f) + 0.18f, 1f));
            _previousPlayerPosition = player != null ? player.WorldCenter : Vector3.zero;
            _hasPreviousPlayerPosition = player != null;
            _flyThroughTriggered = false;
            _facingLockedForClosePass = false;
            SetState(StormPyramidState.IdleHovering);
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
            _previousPlayerPosition = playerPosition;
            _hasPreviousPlayerPosition = true;

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
            if (mayReposition && _movement.HorizontalDistanceFromPlayer > _settings.RepositionDistance)
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
                    (_player.WorldCenter - transform.position).sqrMagnitude
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
            _facingLockedForClosePass = false;
            _attackTimer = Mathf.Max(0.1f, _settings.AttackInterval);
            SetState(StormPyramidState.IdleHovering);
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
                _previousPlayerPosition = _player.WorldCenter;
                _hasPreviousPlayerPosition = true;
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
            Vector3 segment = playerPosition - _previousPlayerPosition;
            float segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return false;
            }

            float previousDistanceSquared = (_previousPlayerPosition - transform.position).sqrMagnitude;
            if (previousDistanceSquared <= triggerRadius * triggerRadius)
            {
                return false;
            }

            float closestTime = Mathf.Clamp01(
                Vector3.Dot(transform.position - _previousPlayerPosition, segment) / segmentLengthSquared);
            Vector3 closestPoint = _previousPlayerPosition + (segment * closestTime);
            if ((closestPoint - transform.position).sqrMagnitude > triggerRadius * triggerRadius)
            {
                return false;
            }

            _flyThroughTriggered = true;
            _lightning?.CancelAttack();
            DuneVectorVisuals.CreatePlayerStrikeOrbFlyThroughExplosion(
                transform.position,
                transform.rotation,
                _materials,
                _settings);
            _enemyHealth.TakeDamage(float.MaxValue);
            return true;
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
            _trackedTarget += shift;
            _lightning.ApplyWorldShift(shift);
            if (_hasPreviousPlayerPosition)
            {
                _previousPlayerPosition += shift;
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
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                distance01);
            Vector3 position = _player.WorldCenter + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                Mathf.Repeat((_identity * 0.593f) + (_repositionCount * 0.331f), 1f));
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
        private StormPyramidLightningDamage _damage;
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
            StormPyramidLightningDamage damage,
            int identity)
        {
            _owner = owner;
            _origin = origin != null ? origin : owner;
            _halo = halo;
            _settings = settings;
            _damage = damage;
            _identity = identity;
            _marker = DuneVectorVisuals.CreateStormStrikeMarker(owner.parent, materials, settings.StrikeRadius);
            _impactFlash = _marker.Find("Strike Impact Flash");
            _chargeLine = CreateLine("Player Strike Charge Telegraph", materials.LightningWarning, settings.ChargeTelegraphWidth);
            _lightningLine = CreateLine("Player Strike Lightning Bolt", materials.Lightning, settings.LightningWidth);
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
            _damage.ResolveStrike(
                _targetPosition,
                _settings.StrikeRadius,
                _settings.LightningDamage,
                "Strike Orb lightning",
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
            if (_halo != null)
            {
                _halo.localScale = Vector3.one
                    * Mathf.Lerp(_settings.ChargeHaloStartScale, _settings.ChargeHaloEndScale, charge01)
                    * pulse;
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
                ? distance > threat.ThreatRange
                    ? "CLEAR"
                    : $"{distance:0} m"
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
        private readonly List<StormPyramidEnemy> _contractEnemies = new List<StormPyramidEnemy>();
        private readonly List<StormPyramidEnemy> _riskEnemies = new List<StormPyramidEnemy>();
        private readonly List<PlayerStrikeOrbEnemy> _orbEnemies = new List<PlayerStrikeOrbEnemy>();
        private readonly List<PlayerStrikeOrbEnemy> _riskOrbEnemies = new List<PlayerStrikeOrbEnemy>();
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private StormPyramidTuning _settings;
        private PlayerStrikeOrbTuning _orbSettings;
        private StormPyramidThreatHUD _warningHud;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            StormPyramidTuning settings,
            PlayerStrikeOrbTuning orbSettings)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _orbSettings = orbSettings;
            _world.WorldShifted += HandleWorldShift;
            System.Random random = new System.Random(unchecked(world.EnemySpawnSeed ^ 0x2749a31));
            if (settings.Enabled)
            {
                int count = Mathf.Max(1, settings.EnemyCount);
                for (int i = 0; i < count; i++)
                {
                    SpawnEnemy(random, $"Storm Pyramid {i + 1:00}", i + 1);
                }
            }

            if (orbSettings.Enabled)
            {
                int count = Mathf.Max(1, orbSettings.EnemyCount);
                for (int i = 0; i < count; i++)
                {
                    SpawnOrbEnemy(random, $"Strike Orb {i + 1:00}", 10000 + i + 1);
                }
            }

            _warningHud = gameObject.AddComponent<StormPyramidThreatHUD>();
            _warningHud.Initialize(player, Camera.main, settings, _enemies, _orbEnemies);
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
                    unchecked(seed + 1000 + i));
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

        private StormPyramidEnemy SpawnEnemy(System.Random random, string objectName, int identity)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                (float)random.NextDouble());
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 spawnPosition = playerPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_settings.HoverHeightVariance,
                _settings.HoverHeightVariance,
                (float)random.NextDouble());
            spawnPosition.y = _world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z)
                + _settings.HoverHeight
                + heightVariation;

            GameObject enemyObject = new GameObject(objectName);
            enemyObject.transform.SetParent(transform, true);
            enemyObject.transform.position = spawnPosition;
            StormPyramidEnemy enemy = enemyObject.AddComponent<StormPyramidEnemy>();
            enemy.Initialize(_player, _playerHealth, _world, _materials, _settings, identity);
            _enemies.Add(enemy);
            return enemy;
        }

        private PlayerStrikeOrbEnemy SpawnOrbEnemy(System.Random random, string objectName, int identity)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float distance = Mathf.Lerp(
                _orbSettings.MinimumSpawnDistance,
                Mathf.Max(_orbSettings.MinimumSpawnDistance, _orbSettings.MaximumSpawnDistance),
                (float)random.NextDouble());
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 spawnPosition = playerPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float heightVariation = Mathf.Lerp(
                -_orbSettings.HoverHeightVariance,
                _orbSettings.HoverHeightVariance,
                (float)random.NextDouble());
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
                SpawnRiskEnemies();
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
                        60000 + i + 1);
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
                        70000 + i + 1);
                    enemy.enabled = enabled;
                    _riskOrbEnemies.Add(enemy);
                }
            }
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
