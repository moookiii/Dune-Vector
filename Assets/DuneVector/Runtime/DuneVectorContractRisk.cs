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
            EnemySpawnMultiplier = Mathf.Min(
                Mathf.Max(1f, settings.RiskMaximumEnemySpawnMultiplier),
                1f + (additionalRisk * Mathf.Max(0f, settings.RiskEnemySpawnMultiplierPerTier)));
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
    public sealed class DuneVectorFallingRuinHazard : MonoBehaviour
    {
        private sealed class FallingRuin
        {
            public GameObject Root;
            public GameObject WarningMarker;
            public Vector3 Velocity;
            public Vector3 RotationAxis;
            public float CollisionRadius;
            public float SettledRemaining;
            public bool Settled;
            public bool DamagedPlayer;
        }

        private readonly List<FallingRuin> _ruins = new List<FallingRuin>();
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
            if (_risk < Mathf.Max(1, _settings.FallingRuinMinimumRisk))
            {
                return;
            }

            _random = new System.Random(unchecked(seed ^ (_risk * 7919) ^ _world.WorldSeed));
            _spawnTimer = Mathf.Max(0f, _settings.FallingRuinInitialDelay);
            enabled = true;
        }

        public void EndContract()
        {
            enabled = false;
            _risk = 0;
            for (int i = 0; i < _ruins.Count; i++)
            {
                if (_ruins[i].Root != null)
                {
                    Destroy(_ruins[i].Root);
                }
                if (_ruins[i].WarningMarker != null)
                {
                    Destroy(_ruins[i].WarningMarker);
                }
            }
            _ruins.Clear();
        }

        private void Update()
        {
            if (_player == null || _health == null || _health.IsDead || _world == null || _settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _spawnTimer -= deltaTime;
            if (_spawnTimer <= 0f && _ruins.Count < Mathf.Max(1, _settings.FallingRuinMaximumActive))
            {
                SpawnRuin();
                _spawnTimer = NextSpawnInterval();
            }

            for (int i = _ruins.Count - 1; i >= 0; i--)
            {
                FallingRuin ruin = _ruins[i];
                if (ruin.Root == null)
                {
                    if (ruin.WarningMarker != null)
                    {
                        Destroy(ruin.WarningMarker);
                    }
                    _ruins.RemoveAt(i);
                    continue;
                }
                if (ruin.Settled)
                {
                    ruin.SettledRemaining -= deltaTime;
                    if (ruin.SettledRemaining <= 0f)
                    {
                        Destroy(ruin.Root);
                        _ruins.RemoveAt(i);
                    }
                    continue;
                }

                if (ruin.WarningMarker != null)
                {
                    float pulse = 0.82f +
                        (Mathf.Sin(Time.time * Mathf.Max(0f, _settings.FallingRuinWarningPulseSpeed)) * 0.18f);
                    float radius = Mathf.Max(0.1f, _settings.FallingRuinWarningRadius) * pulse;
                    ruin.WarningMarker.transform.localScale = new Vector3(radius, 0.035f, radius);
                }

                Vector3 previous = ruin.Root.transform.position;
                ruin.Velocity += Vector3.down * Mathf.Max(0f, _settings.FallingRuinGravity) * deltaTime;
                Vector3 next = previous + (ruin.Velocity * deltaTime);
                ruin.Root.transform.position = next;
                ruin.Root.transform.Rotate(
                    ruin.RotationAxis,
                    Mathf.Max(0f, _settings.FallingRuinRotationSpeed) * deltaTime,
                    Space.World);

                float playerRadius = ruin.CollisionRadius +
                    Mathf.Max(0.1f, _settings.FallingRuinPlayerCollisionRadius);
                if (!ruin.DamagedPlayer &&
                    DistanceToSegment(_player.WorldCenter, previous, next) <= playerRadius)
                {
                    ruin.DamagedPlayer = true;
                    float damage = Mathf.Max(0f, _settings.FallingRuinBaseDamage) +
                        (Mathf.Max(0, _risk - 1) * Mathf.Max(0f, _settings.FallingRuinDamagePerRisk));
                    _health.TakeDamage(
                        damage,
                        $"Risk {_risk} falling building debris",
                        "Crushed by falling building debris.");
                }

                float terrainHeight = _world.SampleHeightAtLocal(next.x, next.z);
                if (next.y - ruin.CollisionRadius <= terrainHeight)
                {
                    next.y = terrainHeight + (ruin.CollisionRadius * 0.35f);
                    ruin.Root.transform.position = next;
                    ruin.Settled = true;
                    ruin.SettledRemaining = Mathf.Max(0f, _settings.FallingRuinSettledLifetime);
                    if (ruin.WarningMarker != null)
                    {
                        Destroy(ruin.WarningMarker);
                        ruin.WarningMarker = null;
                    }
                }
            }
        }

        private void SpawnRuin()
        {
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float minimumOffset = Mathf.Max(0f, _settings.FallingRuinMinimumTargetOffset);
            float maximumOffset = Mathf.Max(minimumOffset, _settings.FallingRuinMaximumTargetOffset);
            float offset = RandomRange(minimumOffset, maximumOffset);
            Vector3 prediction = Vector3.ProjectOnPlane(_player.Motor.BaseVelocity, Vector3.up) *
                Mathf.Max(0f, _settings.FallingRuinTargetPredictionTime);
            Vector3 target = _player.WorldCenter + prediction +
                new Vector3(Mathf.Cos(angle) * offset, 0f, Mathf.Sin(angle) * offset);
            float terrainHeight = _world.SampleHeightAtLocal(target.x, target.z);

            GameObject root = new GameObject($"Risk {_risk} Falling Building Ruin");
            root.transform.SetParent(transform, true);
            root.transform.position = new Vector3(
                target.x,
                terrainHeight + Mathf.Max(1f, _settings.FallingRuinSpawnHeight),
                target.z);
            root.transform.rotation = Quaternion.Euler(
                RandomRange(0f, 360f),
                RandomRange(0f, 360f),
                RandomRange(0f, 360f));

            int minimumPieces = Mathf.Max(1, _settings.FallingRuinMinimumPieceCount);
            int maximumPieces = Mathf.Max(minimumPieces, _settings.FallingRuinMaximumPieceCount);
            int pieceCount = _random.Next(minimumPieces, maximumPieces + 1);
            float riskScale = 1f +
                (Mathf.Max(0, _risk - 1) * Mathf.Max(0f, _settings.FallingRuinScalePerRisk));
            float maximumPieceExtent = 0f;
            for (int i = 0; i < pieceCount; i++)
            {
                float scale = RandomRange(
                    Mathf.Max(0.1f, _settings.FallingRuinMinimumPieceScale),
                    Mathf.Max(_settings.FallingRuinMinimumPieceScale, _settings.FallingRuinMaximumPieceScale)) *
                    riskScale;
                Vector3 pieceScale = new Vector3(
                    scale * RandomRange(0.55f, 1.45f),
                    scale * RandomRange(0.45f, 1.8f),
                    scale * RandomRange(0.55f, 1.45f));
                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = $"Broken Structure Chunk {i + 1}";
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = RandomInsideSphere(scale * 0.85f);
                piece.transform.localRotation = Quaternion.Euler(
                    RandomRange(-35f, 35f),
                    RandomRange(0f, 360f),
                    RandomRange(-35f, 35f));
                piece.transform.localScale = pieceScale;
                piece.GetComponent<Renderer>().sharedMaterial = _materials.Sandstone;
                Collider collider = piece.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
                maximumPieceExtent = Mathf.Max(maximumPieceExtent,
                    piece.transform.localPosition.magnitude + (pieceScale.magnitude * 0.35f));
            }

            Vector2 drift = RandomInsideCircle(Mathf.Max(0f, _settings.FallingRuinHorizontalDrift));
            GameObject warningMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            warningMarker.name = $"Risk {_risk} Falling Ruin Impact Warning";
            warningMarker.transform.SetParent(transform, true);
            warningMarker.transform.position = new Vector3(target.x, terrainHeight + 0.12f, target.z);
            float warningRadius = Mathf.Max(0.1f, _settings.FallingRuinWarningRadius);
            warningMarker.transform.localScale = new Vector3(warningRadius, 0.035f, warningRadius);
            warningMarker.GetComponent<Renderer>().sharedMaterial = _materials.GroundEnemyWarning;
            Collider warningCollider = warningMarker.GetComponent<Collider>();
            if (warningCollider != null)
            {
                Destroy(warningCollider);
            }
            _ruins.Add(new FallingRuin
            {
                Root = root,
                WarningMarker = warningMarker,
                Velocity = new Vector3(drift.x, 0f, drift.y),
                RotationAxis = RandomInsideSphere(1f).normalized,
                CollisionRadius = Mathf.Max(0.5f, maximumPieceExtent),
            });
        }

        private float NextSpawnInterval()
        {
            float interval = Mathf.Max(0.1f, _settings.FallingRuinBaseInterval) -
                (Mathf.Max(0, _risk - _settings.FallingRuinMinimumRisk) *
                    Mathf.Max(0f, _settings.FallingRuinIntervalReductionPerRisk));
            interval = Mathf.Max(Mathf.Max(0.1f, _settings.FallingRuinMinimumInterval), interval);
            return interval * RandomRange(0.82f, 1.18f);
        }

        private void HandleWorldShift(Vector3 shift)
        {
            for (int i = 0; i < _ruins.Count; i++)
            {
                if (_ruins[i].Root != null)
                {
                    _ruins[i].Root.transform.position += shift;
                }
                if (_ruins[i].WarningMarker != null)
                {
                    _ruins[i].WarningMarker.transform.position += shift;
                }
            }
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
        }

        private Vector2 RandomInsideCircle(float radius)
        {
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(RandomRange(0f, 1f)) * radius;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        private Vector3 RandomInsideSphere(float radius)
        {
            float vertical = RandomRange(-1f, 1f);
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - (vertical * vertical)));
            float distance = Mathf.Pow(RandomRange(0f, 1f), 1f / 3f) * radius;
            return new Vector3(
                horizontal * Mathf.Cos(angle),
                vertical,
                horizontal * Mathf.Sin(angle)) * distance;
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
