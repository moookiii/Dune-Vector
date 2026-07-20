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

        public static float GetRewardMultiplier(CourierContractTuning settings, int risk)
        {
            int additionalRisk = Mathf.Max(0, risk - 1);
            return 1f + (additionalRisk * Mathf.Max(0f, settings.RiskRewardMultiplierPerTier));
        }

        public static void Configure(CourierContractTuning settings, int risk)
        {
            int additionalRisk = Mathf.Max(0, risk - 1);
            EnemyHealthMultiplier = 1f +
                (additionalRisk * Mathf.Max(0f, settings.RiskEnemyHealthMultiplierPerTier));
            EnemySpeedMultiplier = 1f +
                (additionalRisk * Mathf.Max(0f, settings.RiskEnemySpeedMultiplierPerTier));
            EnemyDamageMultiplier = 1f +
                (additionalRisk * Mathf.Max(0f, settings.RiskEnemyDamageMultiplierPerTier));
            EnemyAttackRateMultiplier = 1f +
                (additionalRisk * Mathf.Max(0f, settings.RiskEnemyAttackRateMultiplierPerTier));
            EnemySpawnMultiplier = Mathf.Max(1f,
                risk >= 3
                    ? settings.RiskThreeEnemySpawnMultiplier
                    : risk == 2
                        ? settings.RiskTwoEnemySpawnMultiplier
                        : settings.RiskOneEnemySpawnMultiplier);
        }

        public static void Reset()
        {
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
            Retreating,
        }

        private sealed class SandAmbusher
        {
            public GameObject Root;
            public GameObject WarningMarker;
            public EnemyCombatTarget CombatTarget;
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
        private DuneVectorMaterials _materials;
        private CourierContractTuning _settings;
        private System.Random _random;
        private int _risk;
        private float _spawnTimer;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            CourierContractTuning settings)
        {
            _player = player;
            _health = health;
            _world = world;
            _materials = materials;
            _settings = settings;
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

            _random = new System.Random(unchecked(seed ^ (_risk * 7919) ^ _world.WorldSeed));
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
                if (_ambushers[i].WarningMarker != null)
                {
                    Destroy(_ambushers[i].WarningMarker);
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
                    if (ambusher.WarningMarker != null)
                    {
                        Destroy(ambusher.WarningMarker);
                    }
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
            if (ambusher.WarningMarker != null)
            {
                float pulse = 0.82f +
                    (Mathf.Sin(Time.time * Mathf.Max(0f, _settings.SandAmbusherWarningPulseSpeed)) * 0.18f);
                float radius = Mathf.Max(0.1f, _settings.SandAmbusherWarningRadius) * pulse;
                ambusher.WarningMarker.transform.localScale = new Vector3(radius, 0.035f, radius);
            }

            float warningDuration = Mathf.Max(0f, _settings.SandAmbusherWarningDuration) /
                Mathf.Max(0.1f, DuneVectorContractRisk.EnemyAttackRateMultiplier);
            if (ambusher.StateTime < warningDuration)
            {
                return;
            }

            if (ambusher.WarningMarker != null)
            {
                Destroy(ambusher.WarningMarker);
                ambusher.WarningMarker = null;
            }

            Vector3 prediction = _player.Motor.BaseVelocity *
                Mathf.Max(0f, _settings.SandAmbusherTargetPredictionTime);
            Vector3 attackTarget = _player.WorldCenter + prediction;
            Vector3 attackDirection = (attackTarget - ambusher.BuriedPosition).normalized;
            ambusher.AttackEnd = attackTarget +
                (attackDirection * Mathf.Max(0f, _settings.SandAmbusherAttackOvershoot));
            ambusher.Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, attackDirection);
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
                ambusher.DamagedPlayer = true;
                float damage = (Mathf.Max(0f, _settings.SandAmbusherBaseDamage) +
                    (Mathf.Max(0, _risk - 1) * Mathf.Max(0f, _settings.SandAmbusherDamagePerRisk))) *
                    DuneVectorContractRisk.EnemyDamageMultiplier;
                _health.TakeDamage(
                    damage,
                    $"Risk {_risk} sand ambusher",
                    _settings.SandAmbusherDeathMessage);
            }

            if (next == ambusher.AttackEnd ||
                ambusher.StateTime >= Mathf.Max(0.1f, _settings.SandAmbusherMaximumAttackDuration))
            {
                ambusher.CombatTarget?.SetTargetable(false);
                ambusher.State = AmbushState.Retreating;
                ambusher.StateTime = 0f;
            }
        }

        private bool TickRetreat(SandAmbusher ambusher, float deltaTime)
        {
            float speed = Mathf.Max(0.1f, _settings.SandAmbusherRetreatSpeed);
            ambusher.Root.transform.position = Vector3.MoveTowards(
                ambusher.Root.transform.position,
                ambusher.BuriedPosition,
                speed * deltaTime);
            if (ambusher.Root.transform.position != ambusher.BuriedPosition)
            {
                return false;
            }

            Destroy(ambusher.Root);
            return true;
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

            GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            proxy.name = "Temporary Sand Ambusher Proxy";
            proxy.transform.SetParent(root.transform, false);
            float proxyRadius = Mathf.Max(0.1f, _settings.SandAmbusherPlaceholderRadius);
            proxy.transform.localScale = new Vector3(proxyRadius, proxyRadius * 2f, proxyRadius);
            proxy.GetComponent<Renderer>().sharedMaterial = _materials.GroundEnemyBody;
            Collider proxyCollider = proxy.GetComponent<Collider>();
            if (proxyCollider != null)
            {
                Destroy(proxyCollider);
            }

            EnemyHealth enemyHealth = root.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(Mathf.Max(0.1f, _settings.SandAmbusherHealth));
            EnemyCombatTarget combatTarget = root.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius));
            combatTarget.SetTargetable(false);

            GameObject warningMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            warningMarker.name = $"Risk {_risk} Sand Ambusher Warning";
            warningMarker.transform.SetParent(transform, true);
            warningMarker.transform.position = new Vector3(target.x, terrainHeight + 0.12f, target.z);
            float warningRadius = Mathf.Max(0.1f, _settings.SandAmbusherWarningRadius);
            warningMarker.transform.localScale = new Vector3(warningRadius, 0.035f, warningRadius);
            warningMarker.GetComponent<Renderer>().sharedMaterial = _materials.GroundEnemyWarning;
            Collider warningCollider = warningMarker.GetComponent<Collider>();
            if (warningCollider != null)
            {
                Destroy(warningCollider);
            }
            _ambushers.Add(new SandAmbusher
            {
                Root = root,
                WarningMarker = warningMarker,
                CombatTarget = combatTarget,
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
                if (ambusher.WarningMarker != null)
                {
                    ambusher.WarningMarker.transform.position += shift;
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

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
