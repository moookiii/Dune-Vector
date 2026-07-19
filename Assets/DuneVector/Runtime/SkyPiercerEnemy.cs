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
            _hoverPhase = identity * 1.73f;
            _visual = DuneVectorVisuals.CreateFlyingEnemyVisual(transform, materials, settings.VisualScale);
            _core = _visual.Find("Recessed Core");
            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.MaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, settings.VisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, player != null ? player.GetComponent<DroneGoldWallet>() : null, settings.GoldReward);
            _hoverAnchor = transform.position;
            SetState(SkyPiercerState.IdleFloating);
            _attackCooldown = settings.AttackCooldown * Mathf.Repeat((identity * 0.37f) + 0.3f, 1f);
        }

        private void Update()
        {
            if (_player == null || _world == null || _playerHealth == null || _playerHealth.IsDead)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _stateTime += deltaTime;
            _attackCooldown = Mathf.Max(0f, _attackCooldown - deltaTime);

            bool canReposition = CurrentState != SkyPiercerState.AttackDive
                && CurrentState != SkyPiercerState.StuckInGround;
            if (canReposition && Vector3.Distance(transform.position, _player.WorldCenter) > _settings.RepositionDistance)
            {
                RepositionNearPlayer();
            }

            switch (CurrentState)
            {
                case SkyPiercerState.IdleFloating:
                    UpdateIdle(deltaTime);
                    break;
                case SkyPiercerState.ChasingPlayer:
                    UpdateChase(deltaTime);
                    break;
                case SkyPiercerState.AttackDive:
                    UpdateDive(deltaTime);
                    break;
                case SkyPiercerState.StuckInGround:
                    UpdateStuck();
                    break;
                case SkyPiercerState.ReturnToSky:
                    UpdateReturn(deltaTime);
                    break;
            }

            UpdatePresentation(deltaTime);
        }

        private void UpdateIdle(float deltaTime)
        {
            Vector3 target = _hoverAnchor;
            target.y += Mathf.Sin((Time.time * 1.15f) + _hoverPhase) * _settings.HoverAmplitude;
            transform.position = Vector3.Lerp(
                transform.position,
                target,
                DuneVectorMath.Sharpness(2.4f, deltaTime));

            if (Vector3.Distance(transform.position, _player.WorldCenter) <= _settings.DetectionRange)
            {
                SetState(SkyPiercerState.ChasingPlayer);
            }
        }

        private void UpdateChase(float deltaTime)
        {
            Vector3 playerPosition = _player.WorldCenter;
            float terrainHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            Vector3 target = playerPosition;
            target.y = Mathf.Max(terrainHeight + _settings.HoverHeight, playerPosition.y + 7f);
            transform.position = Vector3.MoveTowards(transform.position, target, _settings.FollowSpeed * deltaTime);

            Vector2 horizontalOffset = new Vector2(
                transform.position.x - playerPosition.x,
                transform.position.z - playerPosition.z);
            if (_attackCooldown <= 0f && horizontalOffset.magnitude <= _settings.AttackAlignmentDistance)
            {
                BeginDive(playerPosition);
            }
        }

        private void BeginDive(Vector3 playerPosition)
        {
            float terrainHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            float stuckCenterHeight = terrainHeight + (_settings.VisualScale * 1.05f);
            _strikePoint = new Vector3(transform.position.x, stuckCenterHeight, transform.position.z);
            SetState(SkyPiercerState.AttackDive);
        }

        private void UpdateDive(float deltaTime)
        {
            Vector3 position = transform.position;
            position.y = Mathf.MoveTowards(position.y, _strikePoint.y, _settings.AttackSpeed * deltaTime);
            transform.position = position;
            if (Mathf.Abs(position.y - _strikePoint.y) <= 0.01f)
            {
                ResolveImpact();
            }
        }

        private void ResolveImpact()
        {
            if (Vector3.Distance(_player.WorldCenter, _strikePoint) <= _settings.ImpactRadius)
            {
                _playerHealth.TakeDamage(_settings.ImpactDamage);
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

        private void UpdateReturn(float deltaTime)
        {
            float terrainHeight = _world.SampleHeightAtLocal(transform.position.x, transform.position.z);
            Vector3 target = new Vector3(
                transform.position.x,
                terrainHeight + _settings.HoverHeight,
                transform.position.z);
            transform.position = Vector3.MoveTowards(transform.position, target, _settings.ReturnSpeed * deltaTime);
            if (Vector3.Distance(transform.position, target) <= 0.05f)
            {
                _hoverAnchor = target;
                _attackCooldown = _settings.AttackCooldown;
                SetState(Vector3.Distance(transform.position, _player.WorldCenter) <= _settings.DetectionRange
                    ? SkyPiercerState.ChasingPlayer
                    : SkyPiercerState.IdleFloating);
            }
        }

        private void UpdatePresentation(float deltaTime)
        {
            Vector3 toPlayer = Vector3.ProjectOnPlane(_player.WorldCenter - transform.position, Vector3.up);
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
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

        private void RepositionNearPlayer()
        {
            float angle = ((_identity * 137.5f) + (Time.time * 9f)) * Mathf.Deg2Rad;
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                Mathf.Repeat((_identity * 0.413f) + 0.27f, 1f));
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 position = playerPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            position.y = _world.SampleHeightAtLocal(position.x, position.z) + _settings.HoverHeight;
            transform.position = position;
            _hoverAnchor = position;
            _attackCooldown = _settings.AttackCooldown;
            SetState(SkyPiercerState.IdleFloating);
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
            _hoverAnchor += shift;
            _strikePoint += shift;
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
        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private FlyingEnemyTuning _settings;
        private double _lastOriginX;
        private double _lastOriginZ;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            FlyingEnemyTuning settings)
        {
            _player = player;
            _world = world;
            _materials = materials;
            _settings = settings;
            _lastOriginX = world.OriginOffsetX;
            _lastOriginZ = world.OriginOffsetZ;

            System.Random random = new System.Random(unchecked(world.WorldSeed ^ 0x51f2a9d));
            int count = Mathf.Max(1, settings.EnemyCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float distance = Mathf.Lerp(
                    settings.MinimumSpawnDistance,
                    Mathf.Max(settings.MinimumSpawnDistance, settings.MaximumSpawnDistance),
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
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].enabled = active;
                }
            }
        }

        private void LateUpdate()
        {
            if (_world == null)
            {
                return;
            }

            double shiftX = _world.OriginOffsetX - _lastOriginX;
            double shiftZ = _world.OriginOffsetZ - _lastOriginZ;
            if (System.Math.Abs(shiftX) > 0.001 || System.Math.Abs(shiftZ) > 0.001)
            {
                Vector3 shift = new Vector3((float)-shiftX, 0f, (float)-shiftZ);
                for (int i = 0; i < _enemies.Count; i++)
                {
                    if (_enemies[i] != null)
                    {
                        _enemies[i].ApplyWorldShift(shift);
                    }
                }
            }
            _lastOriginX = _world.OriginOffsetX;
            _lastOriginZ = _world.OriginOffsetZ;
        }
    }
}
