using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public enum RouteFormationType
    {
        HeadOn,
        CrossAttack,
        Pursuit,
        VerticalAttack,
        FlyThroughAssault,
    }

    public enum FormationEnemyState
    {
        FormationApproach,
        AttackPass,
        Break,
        Reposition,
        SecondAttackPass,
    }

    public sealed class DuneVectorEncounterVolume
    {
        public LogicalPosition LogicalCenter;
        public float Radius;
        public RouteFormationType Formation;
        public float LowAltitude;
        public float HighAltitude;
        public float BreakOffDistance;
        public bool Triggered;
    }

    [DefaultExecutionOrder(1250)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorRouteEncounterDirector : MonoBehaviour
    {
        private readonly List<DuneVectorEncounterVolume> _volumes = new List<DuneVectorEncounterVolume>();
        private readonly List<DuneVectorFormationEnemy> _enemies = new List<DuneVectorFormationEnemy>();

        private DroneCharacterController _player;
        private DroneHealth _health;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private RouteEncounterTuning _settings;
        private DuneVectorCourierGame _courierGame;
        private CourierContract _contract;
        private float _pursuitTimer;
        private int _waveIndex;

        public IReadOnlyList<DuneVectorEncounterVolume> ActiveVolumes => _volumes;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            RouteEncounterTuning settings,
            DuneVectorCourierGame courierGame)
        {
            _player = player;
            _health = health;
            _world = world;
            _materials = materials;
            _wallet = wallet;
            _settings = settings;
            _courierGame = courierGame;
            _world.WorldShifted += HandleWorldShift;
        }

        public void BeginContract(CourierContract contract)
        {
            EndContract();
            _contract = contract;
            _waveIndex = 0;
            BuildRouteVolumes(contract);
            _pursuitTimer = NextInterval(contract.Seed);
        }

        public void EndContract()
        {
            _contract = null;
            _volumes.Clear();
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                {
                    Destroy(_enemies[i].gameObject);
                }
            }
            _enemies.Clear();
        }

        private void Update()
        {
            if (_contract == null || _courierGame == null || !_courierGame.IsCarryingCargo ||
                _health == null || _health.IsDead || _settings == null || !_settings.Enabled)
            {
                return;
            }
            PruneEnemies();
            LogicalPosition playerLogical = _world.LogicalPlayerPosition;
            for (int i = 0; i < _volumes.Count; i++)
            {
                DuneVectorEncounterVolume volume = _volumes[i];
                if (volume.Triggered)
                {
                    continue;
                }
                double dx = volume.LogicalCenter.X - playerLogical.X;
                double dz = volume.LogicalCenter.Z - playerLogical.Z;
                if ((dx * dx) + (dz * dz) <= volume.Radius * volume.Radius && CanStartWave())
                {
                    volume.Triggered = true;
                    SpawnFormation(volume.Formation);
                    break;
                }
            }

            if (_contract.Has(CourierContractModifier.HighValue))
            {
                _pursuitTimer -= Time.deltaTime;
                if (_pursuitTimer <= 0f && CanStartWave())
                {
                    SpawnFormation(RouteFormationType.Pursuit);
                    _pursuitTimer = NextInterval(_contract.Seed + _waveIndex);
                }
            }
        }

        private bool CanStartWave()
        {
            if (_enemies.Count > 0 || _courierGame.ActiveObjective == null)
            {
                return false;
            }
            return Vector3.Distance(_player.WorldCenter, _courierGame.ActiveObjective.position) >= _settings.MinimumObjectiveDistance;
        }

        private void BuildRouteVolumes(CourierContract contract)
        {
            LogicalPosition from = contract.PickupPosition;
            int sequence = 0;
            for (int leg = 0; leg < contract.DeliveryPositions.Count; leg++)
            {
                LogicalPosition to = contract.DeliveryPositions[leg];
                int count = Mathf.Max(1, _settings.VolumesPerRouteLeg);
                for (int i = 0; i < count; i++)
                {
                    float t = (i + 1f) / (count + 1f);
                    t += DuneVectorMath.HashRange(contract.Seed, sequence, _world.WorldSeed, 8101, -0.08f, 0.08f);
                    t = Mathf.Clamp(t, 0.12f, 0.88f);
                    _volumes.Add(new DuneVectorEncounterVolume
                    {
                        LogicalCenter = new LogicalPosition(
                            from.X + ((to.X - from.X) * t),
                            from.Z + ((to.Z - from.Z) * t)),
                        Radius = _settings.EncounterVolumeRadius,
                        Formation = (RouteFormationType)(sequence % 5),
                        LowAltitude = _settings.LowAltitude,
                        HighAltitude = _settings.HighAltitude,
                        BreakOffDistance = _settings.BreakOffDistance,
                    });
                    sequence++;
                }
                from = to;
            }
        }

        private void SpawnFormation(RouteFormationType formation)
        {
            _waveIndex++;
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 travelForward = Vector3.ProjectOnPlane(_player.Motor.BaseVelocity, Vector3.up);
            if (travelForward.sqrMagnitude < 1f)
            {
                travelForward = Vector3.ProjectOnPlane(_player.Motor.CharacterForward, Vector3.up);
            }
            if (travelForward.sqrMagnitude < 0.001f)
            {
                travelForward = Vector3.forward;
            }
            travelForward.Normalize();
            Vector3 travelRight = Vector3.Cross(Vector3.up, travelForward).normalized;
            int count = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(_settings.MinimumFormationSize, _settings.MaximumFormationSize,
                    Mathf.Clamp01((_contract.EncounterIntensity - 1f) * 0.8f))),
                _settings.MinimumFormationSize,
                _settings.MaximumFormationSize);

            for (int i = 0; i < count; i++)
            {
                float centered = i - ((count - 1) * 0.5f);
                float altitude = formation == RouteFormationType.VerticalAttack
                    ? Mathf.Lerp(_settings.LowAltitude, _settings.HighAltitude, i / Mathf.Max(1f, count - 1f))
                    : _settings.MediumAltitude + ((i % 2) * 4f);
                Vector3 spawnOffset;
                switch (formation)
                {
                    case RouteFormationType.CrossAttack:
                        spawnOffset = (travelRight * _settings.SpawnDistance) + (travelForward * centered * _settings.FormationSpacing);
                        break;
                    case RouteFormationType.Pursuit:
                        spawnOffset = (-travelForward * _settings.SpawnDistance) + (travelRight * centered * _settings.FormationSpacing);
                        break;
                    case RouteFormationType.VerticalAttack:
                        spawnOffset = (travelForward * _settings.SpawnDistance) + (travelRight * centered * _settings.FormationSpacing * 0.7f);
                        break;
                    case RouteFormationType.FlyThroughAssault:
                        spawnOffset = (travelForward * (_settings.SpawnDistance + ((i % 2) * _settings.FormationSpacing * 2f))) +
                            (travelRight * centered * _settings.FormationSpacing * 1.3f);
                        break;
                    default:
                        spawnOffset = (travelForward * _settings.SpawnDistance) + (travelRight * centered * _settings.FormationSpacing);
                        break;
                }
                Vector3 spawn = playerPosition + spawnOffset;
                float terrain = _world.SampleHeightAtLocal(spawn.x, spawn.z);
                spawn.y = Mathf.Max(playerPosition.y + altitude * 0.35f, terrain + altitude);
                Vector3 passTarget = playerPosition - (spawnOffset.normalized * (_settings.SpawnDistance * 0.45f));
                if (formation == RouteFormationType.CrossAttack)
                {
                    passTarget = playerPosition - (travelRight * _settings.SpawnDistance * 0.55f) + (travelForward * centered * 3f);
                }
                Vector3 breakTarget = passTarget + ((passTarget - spawn).normalized * _settings.BreakOffDistance) + Vector3.up * (i % 2 == 0 ? 12f : -5f);
                Vector3 reposition = playerPosition - spawnOffset + Vector3.up * 10f;

                GameObject enemyObject = new GameObject($"{formation} Formation Enemy {_waveIndex:00}-{i + 1:00}");
                enemyObject.transform.SetParent(transform, true);
                enemyObject.transform.position = spawn;
                DuneVectorFormationEnemy enemy = enemyObject.AddComponent<DuneVectorFormationEnemy>();
                bool secondPass = DuneVectorMath.Hash01(_waveIndex, i, _world.WorldSeed, 8117) <= _settings.SecondPassChance;
                enemy.Initialize(
                    _player,
                    _health,
                    _materials,
                    _wallet,
                    _settings,
                    formation,
                    passTarget,
                    breakTarget,
                    reposition,
                    secondPass);
                _enemies.Add(enemy);
            }
        }

        private float NextInterval(int seed)
        {
            float interval = DuneVectorMath.HashRange(seed, _waveIndex, _world.WorldSeed, 8123,
                _settings.MinimumEncounterInterval, _settings.MaximumEncounterInterval);
            if (_contract != null && _contract.Has(CourierContractModifier.HighValue))
            {
                interval *= _settings.HighValueIntervalMultiplier;
            }
            return interval;
        }

        private void PruneEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                if (_enemies[i] == null)
                {
                    _enemies.RemoveAt(i);
                }
            }
        }

        private void HandleWorldShift(Vector3 shift)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i]?.ApplyWorldShift(shift);
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

    [DisallowMultipleComponent]
    public sealed class DuneVectorFormationEnemy : MonoBehaviour
    {
        public FormationEnemyState State { get; private set; }

        private DroneCharacterController _player;
        private DroneHealth _health;
        private RouteEncounterTuning _settings;
        private Vector3 _passTarget;
        private Vector3 _breakTarget;
        private Vector3 _repositionTarget;
        private bool _allowSecondPass;
        private int _completedPasses;
        private float _stateTime;
        private float _shotTimer;
        private float _shotTelegraphTimer;
        private float _shotVisualTimer;
        private Vector3 _telegraphedPoint;
        private LineRenderer _shotLine;
        private bool _contactDamageApplied;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            RouteEncounterTuning settings,
            RouteFormationType formation,
            Vector3 passTarget,
            Vector3 breakTarget,
            Vector3 repositionTarget,
            bool allowSecondPass)
        {
            _player = player;
            _health = health;
            _settings = settings;
            _passTarget = passTarget;
            _breakTarget = breakTarget;
            _repositionTarget = repositionTarget;
            _allowSecondPass = allowSecondPass;
            DuneVectorVisuals.CreateFlyingEnemyVisual(transform, materials, settings.EnemyVisualScale);
            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.EnemyHealth);
            EnemyCombatTarget target = gameObject.AddComponent<EnemyCombatTarget>();
            target.Initialize(enemyHealth, settings.EnemyVisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, wallet, settings.EnemyGoldReward);
            _shotLine = gameObject.AddComponent<LineRenderer>();
            _shotLine.sharedMaterial = materials.EnemyCore;
            _shotLine.positionCount = 2;
            _shotLine.useWorldSpace = true;
            _shotLine.startWidth = settings.ShotStartWidth;
            _shotLine.endWidth = settings.ShotEndWidth;
            _shotLine.enabled = false;
            _shotTimer = settings.ShotInterval * 0.5f;
            SetState(FormationEnemyState.FormationApproach);
        }

        private void Update()
        {
            if (_player == null || _health == null || _health.IsDead)
            {
                return;
            }
            float deltaTime = Time.deltaTime;
            _stateTime += deltaTime;
            switch (State)
            {
                case FormationEnemyState.FormationApproach:
                    MoveTo(_passTarget, _settings.ApproachSpeed, deltaTime);
                    if (Vector3.Distance(transform.position, _passTarget) < _settings.SpawnDistance * 0.45f)
                    {
                        SetState(FormationEnemyState.AttackPass);
                    }
                    break;
                case FormationEnemyState.AttackPass:
                case FormationEnemyState.SecondAttackPass:
                    UpdateAttackPass(deltaTime);
                    break;
                case FormationEnemyState.Break:
                    MoveTo(_breakTarget, _settings.BreakSpeed, deltaTime);
                    if (Vector3.Distance(transform.position, _breakTarget) <= 2f)
                    {
                        if (_allowSecondPass && _completedPasses < _settings.MaximumAttackPasses)
                        {
                            SetState(FormationEnemyState.Reposition);
                        }
                        else
                        {
                            Destroy(gameObject);
                        }
                    }
                    break;
                case FormationEnemyState.Reposition:
                    MoveTo(_repositionTarget, _settings.BreakSpeed, deltaTime);
                    if (Vector3.Distance(transform.position, _repositionTarget) <= 3f || _stateTime >= _settings.RepositionDelay + 2f)
                    {
                        _passTarget = _player.WorldCenter + (_player.Motor.CharacterForward * 12f);
                        _breakTarget = _passTarget + ((_passTarget - transform.position).normalized * _settings.BreakOffDistance);
                        SetState(FormationEnemyState.SecondAttackPass);
                    }
                    break;
            }
            UpdateShot(deltaTime);
        }

        private void UpdateAttackPass(float deltaTime)
        {
            MoveTo(_passTarget, _settings.AttackPassSpeed, deltaTime);
            float playerDistance = Vector3.Distance(transform.position, _player.WorldCenter);
            if (!_contactDamageApplied && playerDistance <= _settings.ContactRadius)
            {
                _contactDamageApplied = true;
                _health.TakeDamage(_settings.ContactDamage);
            }
            if (Vector3.Distance(transform.position, _passTarget) <= 2f)
            {
                _completedPasses++;
                SetState(FormationEnemyState.Break);
            }
        }

        private void UpdateShot(float deltaTime)
        {
            if (State != FormationEnemyState.AttackPass && State != FormationEnemyState.SecondAttackPass)
            {
                _shotLine.enabled = false;
                return;
            }
            if (_shotVisualTimer > 0f)
            {
                _shotVisualTimer -= deltaTime;
                _shotLine.enabled = true;
                _shotLine.SetPosition(0, transform.position);
                _shotLine.SetPosition(1, _telegraphedPoint);
                return;
            }
            if (_shotTelegraphTimer > 0f)
            {
                _shotTelegraphTimer -= deltaTime;
                _shotLine.enabled = true;
                _shotLine.SetPosition(0, transform.position);
                _shotLine.SetPosition(1, _telegraphedPoint);
                if (_shotTelegraphTimer <= 0f)
                {
                    if (Vector3.Distance(_player.WorldCenter, _telegraphedPoint) <= _settings.ShotHitRadius)
                    {
                        _health.TakeDamage(_settings.ShotDamage);
                    }
                    _shotTimer = _settings.ShotInterval;
                    _shotVisualTimer = _settings.ShotVisualDuration;
                }
                return;
            }
            _shotTimer -= deltaTime;
            if (_shotTimer <= 0f)
            {
                float prediction = _settings.ShotTelegraphDuration;
                _telegraphedPoint = _player.WorldCenter + (_player.Motor.BaseVelocity * prediction);
                _shotTelegraphTimer = _settings.ShotTelegraphDuration;
            }
        }

        private void MoveTo(Vector3 target, float speed, float deltaTime)
        {
            Vector3 direction = target - transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    DuneVectorMath.Sharpness(_settings.TurnSharpness, deltaTime));
            }
            transform.position = Vector3.MoveTowards(transform.position, target, speed * deltaTime);
        }

        private void SetState(FormationEnemyState state)
        {
            State = state;
            _stateTime = 0f;
            _contactDamageApplied = false;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
            _passTarget += shift;
            _breakTarget += shift;
            _repositionTarget += shift;
            _telegraphedPoint += shift;
        }
    }
}
