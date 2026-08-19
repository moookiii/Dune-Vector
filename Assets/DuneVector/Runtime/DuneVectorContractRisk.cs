using System.Collections.Generic;
using FMODUnity;
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

            // Each axis carries its own authored curve. A single shared multiplier reads as one
            // difficulty number but lands multiplicatively: damage, cadence, and population
            // together squared the incoming pressure and made high risk lethal on arithmetic
            // rather than on anything the player could route around.
            EnemyHealthMultiplier = EvaluateAxis(
                settings.RiskEnemyHealthMultiplierAtRankOne,
                settings.RiskEnemyHealthMultiplierAtMaximumRank,
                rankProgress);
            EnemySpeedMultiplier = EvaluateAxis(
                settings.RiskEnemySpeedMultiplierAtRankOne,
                settings.RiskEnemySpeedMultiplierAtMaximumRank,
                rankProgress);
            EnemyDamageMultiplier = EvaluateAxis(
                settings.RiskEnemyDamageMultiplierAtRankOne,
                settings.RiskEnemyDamageMultiplierAtMaximumRank,
                rankProgress);
            EnemyAttackRateMultiplier = EvaluateAxis(
                settings.RiskEnemyAttackRateMultiplierAtRankOne,
                settings.RiskEnemyAttackRateMultiplierAtMaximumRank,
                rankProgress);
            EnemySpawnMultiplier = EvaluateAxis(
                settings.RiskEnemySpawnMultiplierAtRankOne,
                settings.RiskEnemySpawnMultiplierAtMaximumRank,
                rankProgress);
        }

        private static float EvaluateAxis(float atRankOne, float atMaximumRank, float rankProgress)
        {
            float start = Mathf.Max(1f, atRankOne);
            float end = Mathf.Max(start, atMaximumRank);
            return Mathf.Lerp(start, end, rankProgress);
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
            public Vector3 DiveDirection;
            public float StateTime;
            public bool DamagedPlayer;
            public AmbushState State;
        }

        private const int InterceptSolverIterations = 4;

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
            // The cracking-ground telegraph is the player's whole read on this attack, so its
            // length is authored per risk rather than falling out of attack rate scaling. Risk
            // shortens it toward an authored floor while the spawn ring keeps the dodge available.
            float warningDuration = GetWarningDuration();
            float warningProgress = warningDuration > 0f
                ? Mathf.Clamp01(ambusher.StateTime / warningDuration)
                : 1f;
            float spawnDepth = Mathf.Max(0.1f, _settings.SandAmbusherSpawnBuriedDepth);
            float finalDepth = Mathf.Max(0.1f, _settings.SandAmbusherBuriedDepth);
            float currentDepth = Mathf.Lerp(spawnDepth, finalDepth, warningProgress);
            ambusher.Root.transform.position = ambusher.BuriedPosition +
                (Vector3.down * (currentDepth - finalDepth));
            if (ambusher.CombatTarget != null)
            {
                ambusher.CombatTarget.transform.localPosition = Vector3.up * currentDepth;
            }
            ambusher.Emergence?.TickWarning(warningProgress);
            TryDamagePlayerOnBuriedContact(ambusher);
            if (ambusher.StateTime < warningDuration)
            {
                return;
            }

            Vector3 attackTarget = SolveInterceptPoint(ambusher.BuriedPosition);
            Vector3 attackOffset = attackTarget - ambusher.BuriedPosition;
            Vector3 attackDirection = ApplyMinimumElevation(
                attackOffset.normalized,
                _settings.SandAmbusherGroundedMinimumAttackAngle);
            float attackDistance = attackOffset.magnitude +
                Mathf.Max(0f, _settings.SandAmbusherAttackOvershoot);
            ambusher.AttackEnd = ambusher.BuriedPosition + (attackDirection * attackDistance);
            ambusher.Root.transform.rotation = Quaternion.FromToRotation(Vector3.up, attackDirection);
            if (ambusher.CombatTarget != null)
            {
                ambusher.CombatTarget.transform.localPosition = Vector3.zero;
            }
            ambusher.Emergence?.Burst();
            ambusher.Visual?.BeginEmergence();
            if (!DuneTrainingRuntime.HeadlessPresentation &&
                !string.IsNullOrWhiteSpace(_settings.SandAmbusherEmergenceEvent))
            {
                Vector3 emergencePosition = ambusher.Emergence != null
                    ? ambusher.Emergence.transform.position
                    : ambusher.Root.transform.position;
                RuntimeManager.PlayOneShot(_settings.SandAmbusherEmergenceEvent, emergencePosition);
            }
            ambusher.CombatTarget?.SetTargetable(true);
            ambusher.State = AmbushState.Attacking;
            ambusher.StateTime = 0f;
            ambusher.Visual?.PlayAttackAnimation();
        }

        private void TickAttack(SandAmbusher ambusher, float deltaTime)
        {
            Vector3 previous = ambusher.Root.transform.position;
            Vector3 next = Vector3.MoveTowards(previous, ambusher.AttackEnd, GetAttackSpeed() * deltaTime);
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
            bool attackAnimationComplete = ambusher.Visual != null && ambusher.Visual.HasAttackAnimator
                ? ambusher.Visual.IsAttackAnimationComplete()
                : ambusher.StateTime >= Mathf.Max(0f, _settings.SandAmbusherExposedDuration);
            if (!attackAnimationComplete)
            {
                return;
            }

            Vector3 attackDirection = ambusher.Root.transform.up;
            ambusher.DiveDirection = new Vector3(
                attackDirection.x,
                -Mathf.Abs(attackDirection.y),
                attackDirection.z).normalized;
            ambusher.Visual?.BeginRetreat();
            ambusher.State = AmbushState.Retreating;
            ambusher.StateTime = 0f;
        }

        private bool TickRetreat(SandAmbusher ambusher, float deltaTime)
        {
            float speed = GetRetreatSpeed();
            ambusher.Root.transform.position += ambusher.DiveDirection * speed * deltaTime;
            TryDamagePlayerOnBodyContact(ambusher);
            if (ambusher.Visual != null && !ambusher.Visual.IsFullyBelowTerrain(_world))
            {
                return false;
            }

            ambusher.CombatTarget?.SetTargetable(false);
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

        private void TryDamagePlayerOnBuriedContact(SandAmbusher ambusher)
        {
            if (ambusher.DamagedPlayer || ambusher.CombatTarget == null)
            {
                return;
            }

            float collisionRadius = Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius) +
                Mathf.Max(0.1f, _settings.SandAmbusherPlayerCollisionRadius);
            if (Vector3.Distance(_player.WorldCenter, ambusher.CombatTarget.AimPoint) <= collisionRadius)
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
            if (!TryFindSpawnTarget(out Vector3 target))
            {
                return;
            }

            float terrainHeight = _world.SampleHeightAtLocal(target.x, target.z);
            Vector3 buriedPosition = new Vector3(
                target.x,
                terrainHeight - Mathf.Max(0.1f, _settings.SandAmbusherBuriedDepth),
                target.z);

            GameObject root = new GameObject($"Risk {_risk} Sand Ambusher");
            root.transform.SetParent(transform, true);
            float spawnDepth = Mathf.Max(0.1f, _settings.SandAmbusherSpawnBuriedDepth);
            float finalDepth = Mathf.Max(0.1f, _settings.SandAmbusherBuriedDepth);
            root.transform.position = buriedPosition + (Vector3.down * (spawnDepth - finalDepth));

            int visualSeed = _random.Next();
            DuneVectorSandAmbusherVisual visual = root.AddComponent<DuneVectorSandAmbusherVisual>();
            visual.Initialize(_settings, _palette, visualSeed);
            DuneVectorPhotographableMarker.Register(
                root,
                DuneVectorCompendiumSubjectIds.SandAmbusher,
                PhotographableSubjectCategory.Enemy);

            EnemyHealth enemyHealth = root.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(Mathf.Max(0.1f, _settings.SandAmbusherHealth));
            GameObject combatTargetObject = new GameObject("Buried Sand Ambusher Hurtbox and Beam Target");
            combatTargetObject.transform.SetParent(root.transform, false);
            combatTargetObject.transform.localPosition = Vector3.up * spawnDepth;
            EnemyCombatTarget combatTarget = combatTargetObject.AddComponent<EnemyCombatTarget>();
            combatTarget.Initialize(enemyHealth, Mathf.Max(0.1f, _settings.SandAmbusherCollisionRadius));
            combatTarget.SetTargetable(true);

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

        private bool TryFindSpawnTarget(out Vector3 target)
        {
            float riskProgress = Mathf.Clamp01(
                _risk / (float)Mathf.Max(1, _settings.SandAmbusherTargetOffsetRiskCeiling));
            float minimumOffset = Mathf.Max(0f, Mathf.Lerp(
                _settings.SandAmbusherMinimumTargetOffset,
                _settings.SandAmbusherMinimumTargetOffsetAtRiskCeiling,
                riskProgress));
            float maximumOffset = Mathf.Max(minimumOffset, Mathf.Lerp(
                _settings.SandAmbusherMaximumTargetOffset,
                _settings.SandAmbusherMaximumTargetOffsetAtRiskCeiling,
                riskProgress));
            // An ambusher cannot chase: it is slower than a boosting drone and it fires along a
            // fixed line. It only ever threatens by being buried where the drone is going to be.
            // The lead therefore has to cover the whole delay before it arrives, which is the
            // telegraph plus the time it needs to climb from its burial depth to the drone's
            // altitude. Leading by a fixed span instead left it erupting into empty sand behind
            // a drone at flight speed, so straight-line flight never had to react to it at all.
            float leadProgress = Mathf.Clamp01(
                _risk / (float)Mathf.Max(1, _settings.SandAmbusherInterceptLeadRiskCeiling));
            float leadMultiplier = Mathf.Max(0f, Mathf.Lerp(
                _settings.SandAmbusherInterceptLeadMultiplier,
                _settings.SandAmbusherInterceptLeadMultiplierAtRiskCeiling,
                leadProgress));
            float leadTime = (GetWarningDuration() + EstimateRiseTime()) * leadMultiplier;
            Vector3 prediction = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(_player.Motor.BaseVelocity, Vector3.up) * leadTime,
                Mathf.Max(0f, _settings.SandAmbusherMaximumInterceptLeadDistance));
            Vector3 origin = _player.WorldCenter + prediction;

            // Ambushers erupt in a ring around the drone rather than underneath it, so the crack
            // telegraph always names a direction to move away from. Separation keeps a dense risk
            // tier from stacking several eruptions onto the same escape route.
            int attempts = Mathf.Clamp(_settings.SandAmbusherSeparationAttempts, 1, 24);
            for (int i = 0; i < attempts; i++)
            {
                float angle = RandomRange(0f, Mathf.PI * 2f);
                float offset = RandomRange(minimumOffset, maximumOffset);
                target = origin + new Vector3(
                    Mathf.Cos(angle) * offset,
                    0f,
                    Mathf.Sin(angle) * offset);
                if (IsSeparatedFromActiveAmbushers(target))
                {
                    return true;
                }
            }

            target = Vector3.zero;
            return false;
        }

        private bool IsSeparatedFromActiveAmbushers(Vector3 target)
        {
            float separation = Mathf.Max(0f, _settings.SandAmbusherMinimumSeparation);
            if (separation <= 0f)
            {
                return true;
            }

            float separationSquared = separation * separation;
            for (int i = 0; i < _ambushers.Count; i++)
            {
                Vector3 buried = _ambushers[i].BuriedPosition;
                float horizontalX = buried.x - target.x;
                float horizontalZ = buried.z - target.z;
                if ((horizontalX * horizontalX) + (horizontalZ * horizontalZ) < separationSquared)
                {
                    return false;
                }
            }

            return true;
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

        private float GetAttackSpeed()
        {
            return Mathf.Max(0.1f, _settings.SandAmbusherAttackSpeed) *
                DuneVectorContractRisk.EnemySpeedMultiplier;
        }

        private float EstimateRiseTime()
        {
            float terrainHeight = _world.SampleHeightAtLocal(
                _player.WorldCenter.x,
                _player.WorldCenter.z);
            float altitude = Mathf.Max(0f, _player.WorldCenter.y - terrainHeight);
            float depth = Mathf.Max(0.1f, _settings.SandAmbusherBuriedDepth);
            return (depth + altitude) / GetAttackSpeed();
        }

        private Vector3 SolveInterceptPoint(Vector3 buriedPosition)
        {
            float speed = GetAttackSpeed();
            float maximumDuration = Mathf.Max(0.1f, _settings.SandAmbusherMaximumAttackDuration);
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 playerVelocity = _player.Motor.BaseVelocity;

            // Seeding from a straight vertical rise keeps this an ambush rather than a pursuit.
            // Solving the unconstrained intercept instead runs away to a stern chase whenever the
            // drone is faster than the ambusher, which aims it almost flat and guarantees a miss.
            float time = Mathf.Clamp(
                (playerPosition.y - buriedPosition.y) / speed,
                0f,
                maximumDuration);
            for (int i = 0; i < InterceptSolverIterations; i++)
            {
                Vector3 candidate = playerPosition + (playerVelocity * time);
                float nextTime = Vector3.Distance(candidate, buriedPosition) / speed;
                if (nextTime > maximumDuration)
                {
                    break;
                }
                time = nextTime;
            }

            return playerPosition + (playerVelocity * time);
        }

        private float GetWarningDuration()
        {
            float riskProgress = Mathf.Clamp01(
                _risk / (float)Mathf.Max(1, _settings.SandAmbusherWarningDurationRiskCeiling));
            return Mathf.Max(0f, Mathf.Lerp(
                _settings.SandAmbusherWarningDuration,
                _settings.SandAmbusherWarningDurationAtRiskCeiling,
                riskProgress));
        }

        private float GetRetreatSpeed()
        {
            float riskProgress = Mathf.Clamp01(
                _risk / (float)Mathf.Max(1, _settings.SandAmbusherRetreatSpeedRiskCeiling));
            return Mathf.Max(0.1f, Mathf.Lerp(
                _settings.SandAmbusherRetreatSpeed,
                _settings.SandAmbusherRetreatSpeedAtRiskCeiling,
                riskProgress));
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
