using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public static class DuneVectorContractRisk
    {
        public static float EnemyHealthMultiplier { get; private set; } = 1f;
        public static float EnemySpeedMultiplier { get; private set; } = 1f;
        public static float EnemyDamageMultiplier { get; private set; } = 1f;
        public static float EnemyAttackRateMultiplier { get; private set; } = 1f;
        public static float EnemySpawnMultiplier { get; private set; } = 1f;
        public static int CurrentRisk { get; private set; }

        public static float GetRewardMultiplier(CourierContractTuning settings, int risk)
        {
            int additionalRisk = Mathf.Max(0, risk - 1);
            return 1f + (additionalRisk * Mathf.Max(0f, settings.RiskRewardMultiplierPerTier));
        }

        public static void Configure(CourierContractTuning settings, int risk)
        {
            int maximumRisk = Mathf.Max(1, settings.MaximumRisk);
            int clampedRisk = Mathf.Clamp(risk, 1, maximumRisk);
            CurrentRisk = Mathf.Clamp(risk, 0, maximumRisk);
            float rankProgress = maximumRisk > 1
                ? (clampedRisk - 1f) / (maximumRisk - 1f)
                : 0f;
            float startMultiplier = Mathf.Max(1f, settings.RiskEnemyMultiplierAtRankOne);
            float endMultiplier = Mathf.Max(startMultiplier, settings.RiskEnemyMultiplierAtMaximumRank);
            float enemyMultiplier = Mathf.Lerp(startMultiplier, endMultiplier, rankProgress);

            EnemyHealthMultiplier = enemyMultiplier;
            EnemySpeedMultiplier = enemyMultiplier;
            EnemyDamageMultiplier = enemyMultiplier;
            EnemyAttackRateMultiplier = enemyMultiplier;
            EnemySpawnMultiplier = enemyMultiplier;
        }

        public static void Reset()
        {
            CurrentRisk = 0;
            EnemyHealthMultiplier = 1f;
            EnemySpeedMultiplier = 1f;
            EnemyDamageMultiplier = 1f;
            EnemyAttackRateMultiplier = 1f;
            EnemySpawnMultiplier = 1f;
        }
    }

    [DefaultExecutionOrder(1230)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorSandAmbusherSystem : MonoBehaviour
    {
        private enum AmbushState
        {
            Buried,
            Attacking,
            Exposed,
            Retreating,
        }

        private sealed class SandAmbusher
        {
            public GameObject Root;
            public EnemyCombatTarget CombatTarget;
            public DuneVectorSandAmbusherVisual Visual;
            public DuneVectorSandAmbusherEmergence Emergence;
            public Vector3 BuriedPosition;
            public Vector3 AttackEnd;
            public float StateTime;
            public bool DamagedPlayer;
            public AmbushState State;
        }

        private readonly List<SandAmbusher> _ambushers = new List<SandAmbusher>();
        private DroneCharacterController _player;
        private DroneHealth _health;
        private DesertWorldStreamer _world;
        private CourierContractTuning _settings;
        private System.Random _random;
        private DuneVectorSandAmbusherPalette _palette;
        private int _risk;
        private float _spawnTimer;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            CourierContractTuning settings)
        {
            _player = player;
            _health = health;
            _world = world;
            _settings = settings;
            _palette = new DuneVectorSandAmbusherPalette(settings);
            _world.WorldShifted += HandleWorldShift;
            enabled = false;
        }

        public void BeginContract(int risk, int seed)
        {
            EndContract();
            _risk = Mathf.Max(1, risk);
            if (_risk < Mathf.Max(1, _settings.SandAmbusherMinimumRisk))
            {
                return;
            }

            _random = new System.Random(unchecked(seed ^ (_risk * 7919) ^ _world.EnemySpawnSeed));
            _spawnTimer = Mathf.Max(0f, _settings.SandAmbusherInitialDelay);
            enabled = true;
        }

        public void EndContract()
        {
            enabled = false;
            _risk = 0;
            for (int i = 0; i < _ambushers.Count; i++)
            {
                if (_ambushers[i].Root != null)
                {
                    Destroy(_ambushers[i].Root);
                }
                if (_ambushers[i].Emergence != null)
                {
                    Destroy(_ambushers[i].Emergence.gameObject);
                }
            }
            _ambushers.Clear();
        }

        private void Update()
        {
            if (_player == null || _health == null || _health.IsDead || _world == null || _settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _spawnTimer -= deltaTime;
            if (_spawnTimer <= 0f && _ambushers.Count < Mathf.Max(1, _settings.SandAmbusherMaximumActive))
            {
                SpawnAmbusher();
                _spawnTimer = NextSpawnInterval();
            }

            for (int i = _ambushers.Count - 1; i >= 0; i--)
            {
                SandAmbusher ambusher = _ambushers[i];
                if (ambusher.Root == null)
                {
                    _ambushers.RemoveAt(i);
                    continue;
                }

                ambusher.StateTime += deltaTime;
                switch (ambusher.State)
                {
                    case AmbushState.Buried:
                        TickBuried(ambusher);
                        break;
                    case AmbushState.Attacking:
                        TickAttack(ambusher, deltaTime);
                        break;
                    case AmbushState.Exposed:
                        TickExposed(ambusher);
                        break;
                    case AmbushState.Retreating:
                        if (TickRetreat(ambusher, deltaTime))
                        {
                            _ambushers.RemoveAt(i);
                        }
                        break;
                }
            }
        }

        private void TickBuried(SandAmbusher ambusher)
        {
            float warningDuration = Mathf.Max(0f, _settings.SandAmbusherWarningDuration) /
                Mathf.Max(0.1f, DuneVectorContractRisk.EnemyAttackRateMultiplier);
            ambusher.Emergence?.TickWarning(warningDuration > 0f
                ? Mathf.Clamp01(ambusher.StateTime / warningDuration)
                : 1f);
            if (ambusher.StateTime < warningDuration)
            {
                return;
            }

            Vector3 prediction = _player.Motor.BaseVelocity *
                Mathf.Max(0f, _settings.SandAmbusherTargetPredictionTime);
            Vector3 attackTarget = _player.WorldCenter + prediction;
            Vector3 attackDirection = (attackTarget - ambusher.BuriedPosition).normalized;
            if (_player.IsStableGrounded)
            {
                attackDirection = ApplyMinimumElevation(
                    attackDirection,
                    _settings.SandAmbusherGroundedMinimumAttackAngle);
            }
            ambusher.AttackEnd = attackTarget +
                (attackDirection * Mathf.Max(0f, _settings.SandAmbusherAttackOvershoot));
            ambusher.Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, attackDirection);
            ambusher.Emergence?.Burst();
            ambusher.Visual?.BeginEmergence();
            ambusher.CombatTarget?.SetTargetable(true);
            ambusher.State = AmbushState.Attacking;
            ambusher.StateTime = 0f;
        }

        private void TickAttack(SandAmbusher ambusher, float deltaTime)
        {
            Vector3 previous = ambusher.Root.transform.position;
            float speed = Mathf.Max(0.1f, _settings.SandAmbusherAttackSpeed) *
                DuneVectorContractRisk.EnemySpeedMultiplier;
            Vector3 next = Vector3.MoveTowards(previous, ambusher.AttackEnd, speed * deltaTime);
            ambusher.Root.transform.position = next;

            float collisionRadius = Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius) +
                Mathf.Max(0.1f, _settings.SandAmbusherPlayerCollisionRadius);
            if (!ambusher.DamagedPlayer &&
                DistanceToSegment(_player.WorldCenter, previous, next) <= collisionRadius)
            {
                DamagePlayer(ambusher);
            }

            if (next == ambusher.AttackEnd ||
                ambusher.StateTime >= Mathf.Max(0.1f, _settings.SandAmbusherMaximumAttackDuration))
            {
                ambusher.State = AmbushState.Exposed;
                ambusher.StateTime = 0f;
            }
        }

        private void TickExposed(SandAmbusher ambusher)
        {
            TryDamagePlayerOnBodyContact(ambusher);
            if (ambusher.StateTime < Mathf.Max(0f, _settings.SandAmbusherExposedDuration))
            {
                return;
            }
            ambusher.CombatTarget?.SetTargetable(false);
            ambusher.Visual?.BeginRetreat();
            ambusher.State = AmbushState.Retreating;
            ambusher.StateTime = 0f;
        }

        private bool TickRetreat(SandAmbusher ambusher, float deltaTime)
        {
            float speed = Mathf.Max(0.1f, _settings.SandAmbusherRetreatSpeed);
            ambusher.Root.transform.position = Vector3.MoveTowards(
                ambusher.Root.transform.position,
                ambusher.BuriedPosition,
                speed * deltaTime);
            TryDamagePlayerOnBodyContact(ambusher);
            if (ambusher.Root.transform.position != ambusher.BuriedPosition)
            {
                return false;
            }

            Destroy(ambusher.Root);
            return true;
        }

        private void TryDamagePlayerOnBodyContact(SandAmbusher ambusher)
        {
            if (ambusher.DamagedPlayer)
            {
                return;
            }

            float bodyLength = (Mathf.Max(3, _settings.SandAmbusherVisualSegmentCount) - 1) *
                Mathf.Max(0.1f, _settings.SandAmbusherSegmentSpacing);
            Vector3 head = ambusher.Root.transform.position;
            Vector3 tail = head - (ambusher.Root.transform.up * bodyLength);
            float collisionRadius = Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius) +
                Mathf.Max(0.1f, _settings.SandAmbusherPlayerCollisionRadius);
            if (DistanceToSegment(_player.WorldCenter, head, tail) <= collisionRadius)
            {
                DamagePlayer(ambusher);
            }
        }

        private void DamagePlayer(SandAmbusher ambusher)
        {
            ambusher.DamagedPlayer = true;
            float damage = (Mathf.Max(0f, _settings.SandAmbusherBaseDamage) +
                (Mathf.Max(0, _risk - 1) * Mathf.Max(0f, _settings.SandAmbusherDamagePerRisk))) *
                DuneVectorContractRisk.EnemyDamageMultiplier;
            _health.TakeDamage(
                damage,
                $"Risk {_risk} sand ambusher",
                _settings.SandAmbusherDeathMessage);
        }

        private void SpawnAmbusher()
        {
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float minimumOffset = Mathf.Max(0f, _settings.SandAmbusherMinimumTargetOffset);
            float maximumOffset = Mathf.Max(minimumOffset, _settings.SandAmbusherMaximumTargetOffset);
            float offset = RandomRange(minimumOffset, maximumOffset);
            Vector3 prediction = Vector3.ProjectOnPlane(_player.Motor.BaseVelocity, Vector3.up) *
                Mathf.Max(0f, _settings.SandAmbusherTargetPredictionTime);
            Vector3 target = _player.WorldCenter + prediction +
                new Vector3(Mathf.Cos(angle) * offset, 0f, Mathf.Sin(angle) * offset);
            float terrainHeight = _world.SampleHeightAtLocal(target.x, target.z);
            Vector3 buriedPosition = new Vector3(
                target.x,
                terrainHeight - Mathf.Max(0.1f, _settings.SandAmbusherBuriedDepth),
                target.z);

            GameObject root = new GameObject($"Risk {_risk} Sand Ambusher");
            root.transform.SetParent(transform, true);
            root.transform.position = buriedPosition;

            int visualSeed = _random.Next();
            DuneVectorSandAmbusherVisual visual = root.AddComponent<DuneVectorSandAmbusherVisual>();
            visual.Initialize(_settings, _palette, visualSeed);

            EnemyHealth enemyHealth = root.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(Mathf.Max(0.1f, _settings.SandAmbusherHealth));
            EnemyCombatTarget combatTarget = root.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius));
            combatTarget.SetTargetable(false);

            GameObject emergenceObject = new GameObject($"Risk {_risk} Terrain Rupture and Sand Displacement");
            emergenceObject.transform.SetParent(transform, true);
            emergenceObject.transform.position = new Vector3(target.x, terrainHeight, target.z);
            DuneVectorSandAmbusherEmergence emergence = emergenceObject.AddComponent<DuneVectorSandAmbusherEmergence>();
            emergence.Initialize(_settings, _world, _palette, visualSeed ^ 1229);
            _ambushers.Add(new SandAmbusher
            {
                Root = root,
                CombatTarget = combatTarget,
                Visual = visual,
                Emergence = emergence,
                BuriedPosition = buriedPosition,
                State = AmbushState.Buried,
            });
        }

        private float NextSpawnInterval()
        {
            float interval = Mathf.Max(0.1f, _settings.SandAmbusherBaseInterval) -
                (Mathf.Max(0, _risk - _settings.SandAmbusherMinimumRisk) *
                    Mathf.Max(0f, _settings.SandAmbusherIntervalReductionPerRisk));
            interval = Mathf.Max(Mathf.Max(0.1f, _settings.SandAmbusherMinimumInterval), interval);
            return interval * RandomRange(0.82f, 1.18f);
        }

        private void HandleWorldShift(Vector3 shift)
        {
            for (int i = 0; i < _ambushers.Count; i++)
            {
                SandAmbusher ambusher = _ambushers[i];
                if (ambusher.Root != null)
                {
                    ambusher.Root.transform.position += shift;
                }
                ambusher.BuriedPosition += shift;
                ambusher.AttackEnd += shift;
            }
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector3.Distance(point, start);
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return Vector3.Distance(point, start + (segment * t));
        }

        private static Vector3 ApplyMinimumElevation(Vector3 direction, float minimumAngle)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector3.up;
            }

            float clampedAngle = Mathf.Clamp(minimumAngle, 0f, 90f);
            float currentAngle = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (currentAngle >= clampedAngle)
            {
                return direction;
            }

            float angleRadians = clampedAngle * Mathf.Deg2Rad;
            return (horizontal.normalized * Mathf.Cos(angleRadians)) +
                (Vector3.up * Mathf.Sin(angleRadians));
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            _palette?.Dispose();
        }
    }
}
