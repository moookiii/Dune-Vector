using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum VesperPilgrimState
    {
        ChasingPlayer,
        Reflected,
    }

    [DisallowMultipleComponent]
    public sealed class VesperKiteEnemy : MonoBehaviour
    {
        private readonly List<VesperPilgrimAttack> _activePilgrims =
            new List<VesperPilgrimAttack>();

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private VesperKiteTuning _settings;
        private EnemyHealth _enemyHealth;
        private Transform _visual;
        private Transform _leftWing;
        private Transform _rightWing;
        private Transform _core;
        private Transform _cachedTransform;
        private float _patrolAngle;
        private float _patrolAltitude;
        private float _attackCooldown;
        private float _windUpRemaining;
        private float _presentationPhase;
        private int _identity;
        private bool _isWindingUp;

        public int ActivePilgrimCount
        {
            get
            {
                PrunePilgrims();
                return _activePilgrims.Count;
            }
        }

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            VesperKiteTuning settings,
            int identity,
            float patrolAltitude)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _identity = identity;
            _cachedTransform = transform;
            _patrolAltitude = Mathf.Clamp(
                patrolAltitude,
                Mathf.Min(settings.MinimumAltitude, settings.MaximumAltitude),
                Mathf.Max(settings.MinimumAltitude, settings.MaximumAltitude));
            _patrolAngle = Mathf.Repeat(identity * 137.50776f, 360f);
            _presentationPhase = Mathf.Repeat(identity * 0.6180339f, 1f) * Mathf.PI * 2f;
            _attackCooldown = settings.AttackInterval *
                Mathf.Lerp(
                    Mathf.Min(
                        settings.MinimumInitialAttackDelayMultiplier,
                        settings.MaximumInitialAttackDelayMultiplier),
                    Mathf.Max(
                        settings.MinimumInitialAttackDelayMultiplier,
                        settings.MaximumInitialAttackDelayMultiplier),
                    Mathf.Repeat(identity * 0.381966f, 1f));

            _visual = DuneVectorVisuals.CreateVesperKiteVisual(
                _cachedTransform,
                materials,
                settings);
            _leftWing = _visual.Find("Left Wing");
            _rightWing = _visual.Find("Right Wing");
            _core = _visual.Find("Vesper Core");

            _enemyHealth = gameObject.AddComponent<EnemyHealth>();
            _enemyHealth.Initialize(settings.MaximumHealth);
            EnemyCombatTarget combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(_enemyHealth, settings.CollisionRadius);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(
                _enemyHealth,
                player != null ? player.GetComponent<DroneGoldWallet>() : null,
                settings.GoldReward);
            DuneVectorPhotographableMarker.Register(
                gameObject,
                DuneVectorCompendiumSubjectIds.VesperKite,
                PhotographableSubjectCategory.Enemy);
        }

        private void Update()
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead ||
                _world == null || _settings == null || _enemyHealth == null || _enemyHealth.IsDead)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 playerPosition = _player.WorldCenter;
            UpdatePatrol(deltaTime, playerPosition);
            UpdateAttack(deltaTime, playerPosition);
            UpdatePresentation(deltaTime, playerPosition);
        }

        private void UpdatePatrol(float deltaTime, Vector3 playerPosition)
        {
            Vector2 horizontalDelta = new Vector2(
                _cachedTransform.position.x - playerPosition.x,
                _cachedTransform.position.z - playerPosition.z);
            float repositionDistance = Mathf.Max(
                _settings.RepositionDistance,
                _settings.PatrolOrbitRadius);
            if (horizontalDelta.sqrMagnitude > repositionDistance * repositionDistance)
            {
                RepositionNearPlayer(playerPosition);
            }

            _patrolAngle = Mathf.Repeat(
                _patrolAngle + (_settings.PatrolAngularSpeed * deltaTime),
                360f);
            float radians = _patrolAngle * Mathf.Deg2Rad;
            Vector3 targetPosition = playerPosition + new Vector3(
                Mathf.Cos(radians) * _settings.PatrolOrbitRadius,
                0f,
                Mathf.Sin(radians) * _settings.PatrolOrbitRadius);
            float terrainHeight = _world.SampleHeightAtLocal(
                targetPosition.x,
                targetPosition.z);
            float hoverOffset = Mathf.Sin(
                (Time.time * _settings.HoverFrequency * Mathf.PI * 2f) +
                _presentationPhase) * _settings.HoverAmplitude;
            targetPosition.y = terrainHeight + _patrolAltitude + hoverOffset;
            _cachedTransform.position = Vector3.MoveTowards(
                _cachedTransform.position,
                targetPosition,
                _settings.PatrolSpeed *
                DuneVectorContractRisk.EnemySpeedMultiplier *
                deltaTime);
        }

        private void UpdateAttack(float deltaTime, Vector3 playerPosition)
        {
            PrunePilgrims();
            _attackCooldown = Mathf.Max(
                0f,
                _attackCooldown -
                (deltaTime * DuneVectorContractRisk.EnemyAttackRateMultiplier));

            if (_isWindingUp)
            {
                _windUpRemaining = Mathf.Max(0f, _windUpRemaining - deltaTime);
                if (_windUpRemaining <= 0f)
                {
                    _isWindingUp = false;
                    SpawnProcession();
                    _attackCooldown = _settings.AttackInterval;
                }
                return;
            }

            int maximumActive = Mathf.Max(1, _settings.MaximumActivePilgrims);
            if (_attackCooldown > 0f || _activePilgrims.Count >= maximumActive)
            {
                return;
            }

            float detectionRange = Mathf.Max(1f, _settings.DetectionRange);
            if ((_cachedTransform.position - playerPosition).sqrMagnitude >
                detectionRange * detectionRange)
            {
                return;
            }

            _isWindingUp = true;
            _windUpRemaining = Mathf.Max(0f, _settings.AttackWindUpDuration);
        }

        private void SpawnProcession()
        {
            if (_materials == null || _settings == null || _player == null)
            {
                return;
            }

            int available = Mathf.Max(
                0,
                Mathf.Max(1, _settings.MaximumActivePilgrims) -
                _activePilgrims.Count);
            int count = Mathf.Min(Mathf.Max(1, _settings.PilgrimsPerProcession), available);
            for (int i = 0; i < count; i++)
            {
                float degrees = ((360f * i) / count) + (_identity % count);
                float radians = degrees * Mathf.Deg2Rad;
                Vector3 localOffset = new Vector3(
                    Mathf.Cos(radians) * _settings.PilgrimSpawnRadius,
                    Mathf.Sin(radians) * _settings.PilgrimSpawnRadius,
                    _settings.PilgrimSpawnForwardOffset);
                Vector3 spawnPosition = _cachedTransform.TransformPoint(localOffset);

                GameObject pilgrimObject = new GameObject(
                    $"Redshift Pilgrim {_identity:00}-{i + 1:00}");
                pilgrimObject.transform.SetParent(_cachedTransform.parent, true);
                pilgrimObject.transform.position = spawnPosition;
                VesperPilgrimAttack pilgrim =
                    pilgrimObject.AddComponent<VesperPilgrimAttack>();
                pilgrim.Initialize(
                    this,
                    _enemyHealth,
                    _player,
                    _playerHealth,
                    _materials,
                    _settings);
                _activePilgrims.Add(pilgrim);
            }
        }

        private void UpdatePresentation(float deltaTime, Vector3 playerPosition)
        {
            Vector3 toPlayer = Vector3.ProjectOnPlane(
                playerPosition - _cachedTransform.position,
                Vector3.up);
            if (toPlayer.sqrMagnitude > Mathf.Epsilon)
            {
                Quaternion targetRotation = Quaternion.LookRotation(
                    toPlayer.normalized,
                    Vector3.up);
                _cachedTransform.rotation = Quaternion.Slerp(
                    _cachedTransform.rotation,
                    targetRotation,
                    DuneVectorMath.Sharpness(_settings.FacingSharpness, deltaTime));
            }

            float wingPulse = 1f + (
                Mathf.Sin((Time.time * _settings.WingPulseSpeed) + _presentationPhase) *
                _settings.WingPulseAmount);
            if (_leftWing != null)
            {
                _leftWing.localScale = Vector3.Scale(
                    _settings.WingScale,
                    new Vector3(1f, wingPulse, 1f));
            }
            if (_rightWing != null)
            {
                _rightWing.localScale = Vector3.Scale(
                    _settings.WingScale,
                    new Vector3(1f, wingPulse, 1f));
            }

            if (_core != null)
            {
                float windUp01 = _isWindingUp && _settings.AttackWindUpDuration > Mathf.Epsilon
                    ? 1f - Mathf.Clamp01(_windUpRemaining / _settings.AttackWindUpDuration)
                    : 0f;
                float pulse = 1f + (
                    Mathf.Sin((Time.time * _settings.TetherPulseSpeed) + _presentationPhase) *
                    _settings.TetherPulseAmount *
                    windUp01);
                _core.localScale = _settings.CoreScale *
                    Mathf.Lerp(1f, _settings.CoreWindUpScale, windUp01) *
                    pulse;
            }
        }

        private void RepositionNearPlayer(Vector3 playerPosition)
        {
            float radians = _patrolAngle * Mathf.Deg2Rad;
            float distance = Mathf.Clamp(
                _settings.PatrolOrbitRadius,
                _settings.MinimumSpawnDistance,
                Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance));
            Vector3 position = playerPosition + new Vector3(
                Mathf.Cos(radians) * distance,
                0f,
                Mathf.Sin(radians) * distance);
            position.y = _world.SampleHeightAtLocal(position.x, position.z) + _patrolAltitude;
            _cachedTransform.position = position;
        }

        public void SetGameplayActive(bool active)
        {
            enabled = active;
            PrunePilgrims();
            for (int i = 0; i < _activePilgrims.Count; i++)
            {
                if (_activePilgrims[i] != null)
                {
                    _activePilgrims[i].SetGameplayActive(active);
                }
            }
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _cachedTransform.position += shift;
            PrunePilgrims();
            for (int i = 0; i < _activePilgrims.Count; i++)
            {
                if (_activePilgrims[i] != null)
                {
                    _activePilgrims[i].ApplyWorldShift(shift);
                }
            }
        }

        internal void NotifyPilgrimFinished(VesperPilgrimAttack pilgrim)
        {
            _activePilgrims.Remove(pilgrim);
        }

        private void PrunePilgrims()
        {
            for (int i = _activePilgrims.Count - 1; i >= 0; i--)
            {
                if (_activePilgrims[i] == null)
                {
                    _activePilgrims.RemoveAt(i);
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = _activePilgrims.Count - 1; i >= 0; i--)
            {
                if (_activePilgrims[i] != null)
                {
                    Destroy(_activePilgrims[i].gameObject);
                }
            }
            _activePilgrims.Clear();
        }
    }

    [DisallowMultipleComponent]
    public sealed class VesperPilgrimAttack : MonoBehaviour
    {
        public VesperPilgrimState CurrentState { get; private set; }
        public float CurrentSpeed => _speed;

        private VesperKiteEnemy _owner;
        private EnemyHealth _sourceHealth;
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DuneVectorMaterials _materials;
        private VesperKiteTuning _settings;
        private Transform _cachedTransform;
        private Transform _visual;
        private Transform _vowRing;
        private Renderer[] _renderers;
        private LineRenderer _tether;
        private Vector3 _direction;
        private float _speed;
        private float _reflectedDamage;
        private float _ringRotation;
        private bool _finished;

        public void Initialize(
            VesperKiteEnemy owner,
            EnemyHealth sourceHealth,
            DroneCharacterController player,
            DroneHealth playerHealth,
            DuneVectorMaterials materials,
            VesperKiteTuning settings)
        {
            _owner = owner;
            _sourceHealth = sourceHealth;
            _player = player;
            _playerHealth = playerHealth;
            _materials = materials;
            _settings = settings;
            _cachedTransform = transform;
            _speed = Mathf.Max(0.1f, settings.PilgrimInitialSpeed);
            CurrentState = VesperPilgrimState.ChasingPlayer;
            Vector3 toPlayer = player != null
                ? player.WorldCenter - _cachedTransform.position
                : _cachedTransform.forward;
            _direction = toPlayer.sqrMagnitude > Mathf.Epsilon
                ? toPlayer.normalized
                : _cachedTransform.forward;

            _visual = DuneVectorVisuals.CreateVesperPilgrimVisual(
                _cachedTransform,
                materials,
                settings);
            _vowRing = _visual.Find("Pilgrim Vow Ring");
            _renderers = _visual.GetComponentsInChildren<Renderer>(true);
            CreateTether();
            DuneVectorPortalEvents.PlayerCrossed += HandlePortalCrossing;
        }

        private void CreateTether()
        {
            _tether = gameObject.AddComponent<LineRenderer>();
            _tether.name = "Pilgrim Player Tether";
            _tether.sharedMaterial = _materials.VesperTether;
            _tether.useWorldSpace = true;
            _tether.positionCount = 2;
            _tether.startWidth = _settings.TetherWidth;
            _tether.endWidth = _settings.TetherWidth;
            _tether.numCapVertices = 3;
            _tether.numCornerVertices = 2;
            _tether.shadowCastingMode = ShadowCastingMode.Off;
            _tether.receiveShadows = false;
        }

        private void Update()
        {
            if (_finished || _settings == null || _owner == null ||
                _sourceHealth == null || _sourceHealth.IsDead)
            {
                Finish();
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 start = _cachedTransform.position;
            if (CurrentState == VesperPilgrimState.ChasingPlayer)
            {
                UpdateChase(deltaTime);
                Vector3 end = start + (_direction * _speed * deltaTime);
                _cachedTransform.position = end;
                if (_player == null || _playerHealth == null || _playerHealth.IsDead)
                {
                    Finish();
                    return;
                }

                if (SegmentTouchesSphere(
                    start,
                    end,
                    _player.WorldCenter,
                    _settings.PilgrimCollisionRadius))
                {
                    _playerHealth.TakeDamage(
                        _settings.PilgrimDamage *
                        DuneVectorContractRisk.EnemyDamageMultiplier,
                        "Vesper Kite Redshift Procession",
                        _settings.PilgrimDeathMessage);
                    Finish();
                    return;
                }
                UpdateTether();
            }
            else
            {
                UpdateReflection(deltaTime);
                Vector3 end = start + (_direction * _speed * deltaTime);
                _cachedTransform.position = end;
                if (_owner == null || _sourceHealth == null || _sourceHealth.IsDead)
                {
                    Finish();
                    return;
                }

                if (SegmentTouchesSphere(
                    start,
                    end,
                    _owner.transform.position,
                    _settings.ReflectedCollisionRadius))
                {
                    _sourceHealth.TakeDamage(_reflectedDamage);
                    Finish();
                    return;
                }
            }

            UpdatePresentation(deltaTime);
        }

        private void UpdateChase(float deltaTime)
        {
            float riskSpeedMultiplier = DuneVectorContractRisk.EnemySpeedMultiplier;
            float maximumSpeed = Mathf.Max(
                _settings.PilgrimInitialSpeed,
                _settings.PilgrimMaximumSpeed) * riskSpeedMultiplier;
            _speed = Mathf.MoveTowards(
                _speed,
                maximumSpeed,
                _settings.PilgrimAcceleration * riskSpeedMultiplier * deltaTime);
            if (_player == null)
            {
                return;
            }

            Vector3 desired = _player.WorldCenter - _cachedTransform.position;
            if (desired.sqrMagnitude > Mathf.Epsilon)
            {
                _direction = Vector3.RotateTowards(
                    _direction,
                    desired.normalized,
                    _settings.PilgrimTurnRate * Mathf.Deg2Rad * deltaTime,
                    0f).normalized;
            }
        }

        private void UpdateReflection(float deltaTime)
        {
            Vector3 desired = _owner.transform.position - _cachedTransform.position;
            if (desired.sqrMagnitude > Mathf.Epsilon)
            {
                _direction = Vector3.RotateTowards(
                    _direction,
                    desired.normalized,
                    _settings.ReflectedTurnRate * Mathf.Deg2Rad * deltaTime,
                    0f).normalized;
            }
        }

        private void UpdateTether()
        {
            if (_tether == null || _player == null)
            {
                return;
            }

            _tether.SetPosition(0, _cachedTransform.position);
            _tether.SetPosition(1, _player.WorldCenter);
            float pulse = 1f + (
                Mathf.Sin(Time.time * _settings.TetherPulseSpeed) *
                _settings.TetherPulseAmount);
            float width = _settings.TetherWidth * pulse;
            _tether.startWidth = width;
            _tether.endWidth = width;
        }

        private void UpdatePresentation(float deltaTime)
        {
            if (_direction.sqrMagnitude > Mathf.Epsilon)
            {
                _cachedTransform.rotation = Quaternion.LookRotation(
                    _direction,
                    Vector3.up);
            }
            if (_vowRing != null)
            {
                _ringRotation = Mathf.Repeat(
                    _ringRotation + (_settings.PilgrimRingRotationSpeed * deltaTime),
                    360f);
                _vowRing.localRotation = Quaternion.AngleAxis(
                    _ringRotation,
                    Vector3.forward);
            }
        }

        private void HandlePortalCrossing(DuneVectorPortalCrossing crossing)
        {
            if (_finished || CurrentState != VesperPilgrimState.ChasingPlayer)
            {
                return;
            }

            float riskSpeedMultiplier = DuneVectorContractRisk.EnemySpeedMultiplier;
            float initialSpeed = _settings.PilgrimInitialSpeed;
            float maximumSpeed = Mathf.Max(
                initialSpeed,
                _settings.PilgrimMaximumSpeed) * riskSpeedMultiplier;
            float speedProgress = Mathf.InverseLerp(initialSpeed, maximumSpeed, _speed);
            _reflectedDamage = _settings.ReflectedBaseDamage +
                (_settings.ReflectedMaximumBonusDamage * speedProgress);
            _cachedTransform.position = crossing.Position +
                (crossing.TravelDirection * _settings.PortalExitOffset);
            _direction = crossing.TravelDirection;
            _speed = Mathf.Max(
                _settings.PilgrimInitialSpeed,
                _speed * _settings.ReflectedSpeedMultiplier);
            CurrentState = VesperPilgrimState.Reflected;

            if (_tether != null)
            {
                _tether.enabled = false;
            }
            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _renderers[i].sharedMaterial = _materials.VesperPilgrimReflected;
                    }
                }
            }
        }

        public void SetGameplayActive(bool active)
        {
            enabled = active;
            if (_tether != null)
            {
                _tether.enabled = active &&
                    CurrentState == VesperPilgrimState.ChasingPlayer;
            }
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _cachedTransform.position += shift;
        }

        private static bool SegmentTouchesSphere(
            Vector3 start,
            Vector3 end,
            Vector3 center,
            float radius)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            float interpolation = lengthSquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / lengthSquared)
                : 0f;
            Vector3 closestPoint = start + (segment * interpolation);
            return (closestPoint - center).sqrMagnitude <= radius * radius;
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            DuneVectorPortalEvents.PlayerCrossed -= HandlePortalCrossing;
            if (_owner != null)
            {
                _owner.NotifyPilgrimFinished(this);
            }
        }
    }

    [DefaultExecutionOrder(1375)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorVesperKiteDirector : MonoBehaviour
    {
        private readonly List<VesperKiteEnemy> _enemies = new List<VesperKiteEnemy>();
        private readonly List<VesperKiteEnemy> _baseEnemies = new List<VesperKiteEnemy>();
        private readonly List<VesperKiteEnemy> _riskEnemies = new List<VesperKiteEnemy>();

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private VesperKiteTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            VesperKiteTuning settings)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _world.WorldShifted += HandleWorldShift;
            SpawnBaseEnemies();
        }

        public void SetGameplayActive(bool active)
        {
            enabled = active;
            if (active)
            {
                RespawnBaseEnemies();
                SpawnRiskEnemies();
            }
            else
            {
                ClearEnemies(_riskEnemies);
            }

            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    _enemies[i].SetGameplayActive(active);
                }
            }
        }

        private void SpawnBaseEnemies()
        {
            System.Random random = new System.Random(
                unchecked(_world.EnemySpawnSeed ^ 0x72f4b91));
            int count = Mathf.Max(1, _settings.EnemyCount);
            for (int i = 0; i < count; i++)
            {
                VesperKiteEnemy enemy = SpawnEnemy(
                    random,
                    $"Vesper Kite {i + 1:00}",
                    i + 1,
                    NormalizeIndex(i, count));
                _baseEnemies.Add(enemy);
            }
        }

        private void RespawnBaseEnemies()
        {
            ClearEnemies(_baseEnemies);
            SpawnBaseEnemies();
        }

        private void SpawnRiskEnemies()
        {
            ClearEnemies(_riskEnemies);
            float bonusMultiplier = Mathf.Max(
                0f,
                DuneVectorContractRisk.EnemySpawnMultiplier - 1f);
            int bonusCount = Mathf.CeilToInt(
                Mathf.Max(1, _settings.EnemyCount) * bonusMultiplier);
            if (bonusCount <= 0)
            {
                return;
            }

            int multiplierSeed = Mathf.RoundToInt(
                DuneVectorContractRisk.EnemySpawnMultiplier * 1000f);
            System.Random random = new System.Random(
                unchecked(_world.EnemySpawnSeed ^ 0x49c8e23 ^ multiplierSeed));
            for (int i = 0; i < bonusCount; i++)
            {
                VesperKiteEnemy enemy = SpawnEnemy(
                    random,
                    $"Risk Vesper Kite {i + 1:00}",
                    80000 + i + 1,
                    NormalizeIndex(i, bonusCount));
                enemy.SetGameplayActive(enabled);
                _riskEnemies.Add(enemy);
            }
        }

        private VesperKiteEnemy SpawnEnemy(
            System.Random random,
            string objectName,
            int identity,
            float altitudeProgress)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float distance = Mathf.Lerp(
                _settings.MinimumSpawnDistance,
                Mathf.Max(
                    _settings.MinimumSpawnDistance,
                    _settings.MaximumSpawnDistance),
                (float)random.NextDouble());
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 spawnPosition = playerPosition + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            float minimumAltitude = Mathf.Min(
                _settings.MinimumAltitude,
                _settings.MaximumAltitude);
            float maximumAltitude = Mathf.Max(
                _settings.MinimumAltitude,
                _settings.MaximumAltitude);
            float altitude = Mathf.Lerp(
                minimumAltitude,
                maximumAltitude,
                Mathf.Clamp01(altitudeProgress));
            spawnPosition.y = _world.SampleHeightAtLocal(
                spawnPosition.x,
                spawnPosition.z) + altitude;

            GameObject enemyObject = new GameObject(objectName);
            enemyObject.transform.SetParent(transform, true);
            enemyObject.transform.position = spawnPosition;
            VesperKiteEnemy enemy = enemyObject.AddComponent<VesperKiteEnemy>();
            enemy.Initialize(
                _player,
                _playerHealth,
                _world,
                _materials,
                _settings,
                identity,
                altitude);
            _enemies.Add(enemy);
            return enemy;
        }

        private void ClearEnemies(List<VesperKiteEnemy> enemies)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                VesperKiteEnemy enemy = enemies[i];
                _enemies.Remove(enemy);
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }
            enemies.Clear();
        }

        private static float NormalizeIndex(int index, int count)
        {
            return count <= 1 ? 0.5f : index / (float)(count - 1);
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
