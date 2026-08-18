using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public enum SkyPiercerState
    {
        IdleFloating,
        ChasingPlayer,
        AttackDive,
        StuckInGround,
        ReturnToSky,
    }

    [DisallowMultipleComponent]
    public sealed class SkyPiercerEnemy : MonoBehaviour
    {
        public SkyPiercerState CurrentState { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private FlyingEnemyTuning _settings;
        private Transform _visual;
        private Transform _core;
        private Vector3 _hoverAnchor;
        private Vector3 _strikePoint;
        private float _stateTime;
        private float _attackCooldown;
        private float _hoverPhase;
        private int _identity;
        private Transform _cachedTransform;
        private bool _passiveDrift;
        private Vector3 _passiveAnchor;
        private float _passiveAngle;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            FlyingEnemyTuning settings,
            int identity)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _settings = settings;
            _identity = identity;
            _cachedTransform = transform;
            _hoverPhase = identity * 1.73f;
            _visual = DuneVectorVisuals.CreateFlyingEnemyVisual(_cachedTransform, materials, settings.VisualScale);
            _core = _visual.Find("Recessed Core");
            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.MaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, settings.VisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, player != null ? player.GetComponent<DroneGoldWallet>() : null, settings.GoldReward);
            _hoverAnchor = _cachedTransform.position;
            SetState(SkyPiercerState.IdleFloating);
            _attackCooldown = settings.AttackCooldown * Mathf.Repeat((identity * 0.37f) + 0.3f, 1f);
            DuneVectorPhotographableMarker.Register(
                gameObject,
                DuneVectorCompendiumSubjectIds.SkyPiercer,
                PhotographableSubjectCategory.Enemy);
        }

        public void SetPassiveDrift(bool passive)
        {
            if (_passiveDrift == passive)
            {
                return;
            }
            _passiveDrift = passive;
            if (passive)
            {
                BeginPassiveDrift();
            }
            else
            {
                _hoverAnchor = _cachedTransform != null ? _cachedTransform.position : _hoverAnchor;
                SetState(SkyPiercerState.IdleFloating);
            }
        }

        private void BeginPassiveDrift()
        {
            SetState(SkyPiercerState.IdleFloating);
            _attackCooldown = _settings.AttackCooldown;
            _passiveAngle = _identity * 137.5f;
            Vector3 center = _player != null ? _player.WorldCenter : _cachedTransform.position;
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                Mathf.Repeat((_identity * 0.413f) + 0.27f, 1f));
            float radians = _passiveAngle * Mathf.Deg2Rad;
            _passiveAnchor = center + new Vector3(Mathf.Cos(radians) * distance, 0f, Mathf.Sin(radians) * distance);
            _passiveAnchor.y = SampleGroundHeight(_passiveAnchor) + _settings.HubPassiveHoverHeight;
            _cachedTransform.position = _passiveAnchor;
            _hoverAnchor = _passiveAnchor;
        }

        private float SampleGroundHeight(Vector3 position)
        {
            return _world != null ? _world.SampleHeightAtLocal(position.x, position.z) : position.y;
        }

        private void UpdatePassiveDrift(float deltaTime)
        {
            _passiveAngle += _settings.HubPassiveDriftSpeed * deltaTime;
            float radians = _passiveAngle * Mathf.Deg2Rad;
            float radius = _settings.HubPassiveDriftRadius;
            Vector3 target = _passiveAnchor + new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius);
            target.y = SampleGroundHeight(target)
                + _settings.HubPassiveHoverHeight
                + (Mathf.Sin((Time.time * _settings.HubPassiveBobSpeed * Mathf.PI * 2f) + _hoverPhase)
                    * _settings.HubPassiveBobAmplitude);
            _cachedTransform.position = Vector3.Lerp(
                _cachedTransform.position,
                target,
                DuneVectorMath.Sharpness(2.4f, deltaTime));
            _cachedTransform.rotation = Quaternion.Euler(
                0f,
                _cachedTransform.rotation.eulerAngles.y + (_settings.HubPassiveYawSpeed * deltaTime),
                0f);

            if (_visual != null)
            {
                _visual.localPosition = Vector3.up * (Mathf.Sin((Time.time * 2.1f) + _hoverPhase) * 0.12f);
            }
            if (_core != null)
            {
                float pulse = 1f + (Mathf.Sin((Time.time * 5f) + _hoverPhase) * 0.12f);
                _core.localScale = new Vector3(0.36f, 0.42f, 0.1f) * pulse;
            }
        }

        private void Update()
        {
            if (_passiveDrift)
            {
                UpdatePassiveDrift(Time.deltaTime);
                return;
            }

            if (_player == null || _world == null || _playerHealth == null || _playerHealth.IsDead)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _stateTime += deltaTime;
            _attackCooldown = Mathf.Max(0f, _attackCooldown -
                (deltaTime * DuneVectorContractRisk.EnemyAttackRateMultiplier));
            Vector3 playerPosition = _player.WorldCenter;

            bool canReposition = CurrentState != SkyPiercerState.AttackDive
                && CurrentState != SkyPiercerState.StuckInGround;
            float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                _settings.MinimumSpawnDistance,
                _settings.DetectionRange,
                _world);
            float repositionDistance = DuneVectorEnemyEngagementRing.ResolveRepositionDistance(
                _settings.RepositionDistance,
                DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                    minimumDistance,
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance));
            if (canReposition
                && (_cachedTransform.position - playerPosition).sqrMagnitude > repositionDistance * repositionDistance)
            {
                RepositionNearPlayer(playerPosition);
            }

            switch (CurrentState)
            {
                case SkyPiercerState.IdleFloating:
                    UpdateIdle(deltaTime, playerPosition);
                    break;
                case SkyPiercerState.ChasingPlayer:
                    UpdateChase(deltaTime, playerPosition);
                    break;
                case SkyPiercerState.AttackDive:
                    UpdateDive(deltaTime);
                    break;
                case SkyPiercerState.StuckInGround:
                    UpdateStuck();
                    break;
                case SkyPiercerState.ReturnToSky:
                    UpdateReturn(deltaTime, playerPosition);
                    break;
            }

            UpdatePresentation(deltaTime, playerPosition);
        }

        private void UpdateIdle(float deltaTime, Vector3 playerPosition)
        {
            Vector3 target = _hoverAnchor;
            target.y += Mathf.Sin((Time.time * 1.15f) + _hoverPhase) * _settings.HoverAmplitude;
            _cachedTransform.position = Vector3.Lerp(
                _cachedTransform.position,
                target,
                DuneVectorMath.Sharpness(2.4f, deltaTime));

            float detectionRange = _settings.DetectionRange;
            if ((_cachedTransform.position - playerPosition).sqrMagnitude <= detectionRange * detectionRange)
            {
                SetState(SkyPiercerState.ChasingPlayer);
            }
        }

        private void UpdateChase(float deltaTime, Vector3 playerPosition)
        {
            float terrainHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            Vector3 target = playerPosition;
            target.y = Mathf.Max(terrainHeight + _settings.HoverHeight, playerPosition.y + 7f);
            _cachedTransform.position = Vector3.MoveTowards(
                _cachedTransform.position,
                target,
                _settings.EvaluateFollowSpeed(DuneVectorContractRisk.CurrentRisk) * deltaTime);

            Vector2 horizontalOffset = new Vector2(
                _cachedTransform.position.x - playerPosition.x,
                _cachedTransform.position.z - playerPosition.z);
            float alignmentDistance = _settings.AttackAlignmentDistance;
            if (_attackCooldown <= 0f && horizontalOffset.sqrMagnitude <= alignmentDistance * alignmentDistance)
            {
                BeginDive(playerPosition);
            }
        }

        private void BeginDive(Vector3 playerPosition)
        {
            float terrainHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            float stuckCenterHeight = terrainHeight
                + (_settings.VisualScale * _settings.StuckCenterHeightPerVisualScale)
                - _settings.AttackGroundPenetrationDepth;
            _strikePoint = new Vector3(_cachedTransform.position.x, stuckCenterHeight, _cachedTransform.position.z);
            SetState(SkyPiercerState.AttackDive);
        }

        private void UpdateDive(float deltaTime)
        {
            Vector3 position = _cachedTransform.position;
            position.y = Mathf.MoveTowards(
                position.y,
                _strikePoint.y,
                _settings.EvaluateAttackSpeed(DuneVectorContractRisk.CurrentRisk) * deltaTime);
            _cachedTransform.position = position;
            if (Mathf.Abs(position.y - _strikePoint.y) <= 0.01f)
            {
                ResolveImpact();
            }
        }

        private void ResolveImpact()
        {
            float impactRadius = _settings.ImpactRadius;
            if ((_player.WorldCenter - _strikePoint).sqrMagnitude <= impactRadius * impactRadius)
            {
                _playerHealth.TakeDamage(
                    _settings.ImpactDamage * DuneVectorContractRisk.EnemyDamageMultiplier,
                    "Sky Piecer impact",
                    _settings.ImpactDeathMessage);
            }
            SetState(SkyPiercerState.StuckInGround);
        }

        private void UpdateStuck()
        {
            if (_stateTime >= _settings.StuckDuration)
            {
                SetState(SkyPiercerState.ReturnToSky);
            }
        }

        private void UpdateReturn(float deltaTime, Vector3 playerPosition)
        {
            Vector3 currentPosition = _cachedTransform.position;
            float terrainHeight = _world.SampleHeightAtLocal(currentPosition.x, currentPosition.z);
            Vector3 target = new Vector3(
                currentPosition.x,
                terrainHeight + _settings.HoverHeight,
                currentPosition.z);
            _cachedTransform.position = Vector3.MoveTowards(
                currentPosition,
                target,
                _settings.ReturnSpeed * DuneVectorContractRisk.EnemySpeedMultiplier * deltaTime);
            if ((_cachedTransform.position - target).sqrMagnitude <= 0.0025f)
            {
                _hoverAnchor = target;
                _attackCooldown = _settings.AttackCooldown;
                float detectionRange = _settings.DetectionRange;
                SetState((_cachedTransform.position - playerPosition).sqrMagnitude <= detectionRange * detectionRange
                    ? SkyPiercerState.ChasingPlayer
                    : SkyPiercerState.IdleFloating);
            }
        }

        private void UpdatePresentation(float deltaTime, Vector3 playerPosition)
        {
            Vector3 toPlayer = Vector3.ProjectOnPlane(playerPosition - _cachedTransform.position, Vector3.up);
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                _cachedTransform.rotation = Quaternion.Slerp(
                    _cachedTransform.rotation,
                    targetRotation,
                    DuneVectorMath.Sharpness(7f, deltaTime));
            }

            if (_visual != null)
            {
                float visualHover = CurrentState == SkyPiercerState.IdleFloating || CurrentState == SkyPiercerState.ChasingPlayer
                    ? Mathf.Sin((Time.time * 2.1f) + _hoverPhase) * 0.12f
                    : 0f;
                _visual.localPosition = Vector3.up * visualHover;
            }
            if (_core != null)
            {
                float pulse = 1f + (Mathf.Sin((Time.time * 5f) + _hoverPhase) * 0.12f);
                if (CurrentState == SkyPiercerState.AttackDive)
                {
                    pulse += 0.28f;
                }
                _core.localScale = new Vector3(0.36f, 0.42f, 0.1f) * pulse;
            }
        }

        private void SetState(SkyPiercerState state)
        {
            CurrentState = state;
            _stateTime = 0f;
        }

        private void RepositionNearPlayer(Vector3 playerPosition)
        {
            float angle = ((_identity * 137.5f) + (Time.time * 9f)) * Mathf.Deg2Rad;
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
                Mathf.Repeat((_identity * 0.413f) + 0.27f, 1f));
            Vector3 position = playerPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            position.y = _world.SampleHeightAtLocal(position.x, position.z) + _settings.HoverHeight;
            _cachedTransform.position = position;
            _hoverAnchor = position;
            _attackCooldown = _settings.AttackCooldown;
            SetState(SkyPiercerState.IdleFloating);
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _cachedTransform.position += shift;
            _hoverAnchor += shift;
            _strikePoint += shift;
            _passiveAnchor += shift;
        }

        private void OnDrawGizmosSelected()
        {
            if (_settings == null)
            {
                return;
            }
            Gizmos.color = new Color(1f, 0.12f, 0.08f, 0.5f);
            Gizmos.DrawWireSphere(_strikePoint, _settings.ImpactRadius);
        }
    }

    [DefaultExecutionOrder(1300)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorEnemyDirector : MonoBehaviour
    {
        private readonly List<SkyPiercerEnemy> _enemies = new List<SkyPiercerEnemy>();
        private readonly List<SkyPiercerEnemy> _riskEnemies = new List<SkyPiercerEnemy>();
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private FlyingEnemyTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            FlyingEnemyTuning settings)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _world.WorldShifted += HandleWorldShift;

            System.Random random = new System.Random(unchecked(world.EnemySpawnSeed ^ 0x51f2a9d));
            int count = Mathf.Max(1, settings.EnemyCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float minimumDistance = DuneVectorEnemyEngagementRing.ResolveMinimumDistance(
                    settings.MinimumSpawnDistance,
                    settings.DetectionRange,
                    world);
                float distance = Mathf.Lerp(
                    minimumDistance,
                    DuneVectorEnemyEngagementRing.ResolveMaximumDistance(
                        minimumDistance,
                        settings.MinimumSpawnDistance,
                        settings.MaximumSpawnDistance),
                    (float)random.NextDouble());
                Vector3 playerPosition = player.WorldCenter;
                Vector3 spawnPosition = playerPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                spawnPosition.y = world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z) + settings.HoverHeight;

                GameObject enemyObject = new GameObject($"Sky Piercer {i + 1:00}");
                enemyObject.transform.SetParent(transform, true);
                enemyObject.transform.position = spawnPosition;
                SkyPiercerEnemy enemy = enemyObject.AddComponent<SkyPiercerEnemy>();
                enemy.Initialize(player, playerHealth, world, materials, settings, i + 1);
                _enemies.Add(enemy);
            }
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
            bool passiveDrift = !active && _settings != null && _settings.HubPassiveDriftEnabled;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].SetPassiveDrift(passiveDrift);
                    _enemies[i].enabled = active || passiveDrift;
                }
            }
        }

        private void SpawnRiskEnemies()
        {
            ClearRiskEnemies();
            int baseCount = Mathf.Max(1, _settings.EnemyCount);
            int bonusCount = Mathf.CeilToInt(
                baseCount * Mathf.Max(0f, DuneVectorContractRisk.EnemySpawnMultiplier - 1f));
            if (bonusCount <= 0)
            {
                return;
            }

            int multiplierSeed = Mathf.RoundToInt(DuneVectorContractRisk.EnemySpawnMultiplier * 1000f);
            System.Random random = new System.Random(unchecked(_world.EnemySpawnSeed ^ 0x4d31ac7 ^ multiplierSeed));
            for (int i = 0; i < bonusCount; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
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
                    (float)random.NextDouble());
                Vector3 playerPosition = _player.WorldCenter;
                Vector3 spawnPosition = playerPosition + new Vector3(
                    Mathf.Cos(angle) * distance,
                    0f,
                    Mathf.Sin(angle) * distance);
                spawnPosition.y = _world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z) +
                    _settings.HoverHeight;

                GameObject enemyObject = new GameObject($"Risk Sky Piercer {i + 1:00}");
                enemyObject.transform.SetParent(transform, true);
                enemyObject.transform.position = spawnPosition;
                SkyPiercerEnemy enemy = enemyObject.AddComponent<SkyPiercerEnemy>();
                enemy.Initialize(
                    _player,
                    _playerHealth,
                    _world,
                    _materials,
                    _settings,
                    50000 + i + 1);
                enemy.enabled = enabled;
                _enemies.Add(enemy);
                _riskEnemies.Add(enemy);
            }
        }

        private void ClearRiskEnemies()
        {
            for (int i = 0; i < _riskEnemies.Count; i++)
            {
                SkyPiercerEnemy enemy = _riskEnemies[i];
                _enemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            _riskEnemies.Clear();
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
