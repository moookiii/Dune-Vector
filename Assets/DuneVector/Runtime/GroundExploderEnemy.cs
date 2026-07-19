using System;
using UnityEngine;

namespace DuneVector
{
    public enum GroundExploderState
    {
        Patrolling,
        TriggeredWindUp,
        Exploding,
        Dead,
    }

    [DisallowMultipleComponent]
    public sealed class GroundExploderEnemy : MonoBehaviour
    {
        private const float ExplosionPresentationDuration = 0.38f;

        public GroundExploderState CurrentState { get; private set; }

        private GroundExploderTuning _settings;
        private GroundExploderMovement _movement;
        private GroundExploderProximity _proximity;
        private GroundExploderDamage _damage;
        private GroundExploderVisual _visual;
        private EnemyCombatTarget _combatTarget;
        private EnemyGoldReward _goldReward;
        private DroneHealth _playerHealth;
        private float _stateTime;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DuneHeightField heightField,
            double chunkLogicalX,
            double chunkLogicalZ,
            float chunkSize,
            DuneVectorMaterials materials,
            GroundExploderTuning settings,
            int identity)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _movement = gameObject.AddComponent<GroundExploderMovement>();
            _proximity = gameObject.AddComponent<GroundExploderProximity>();
            _damage = gameObject.AddComponent<GroundExploderDamage>();
            _visual = gameObject.AddComponent<GroundExploderVisual>();

            Transform visualRoot = DuneVectorVisuals.CreateGroundExploderVisual(transform, materials, settings.VisualScale);
            _visual.Initialize(visualRoot, settings);
            _movement.Initialize(heightField, chunkLogicalX, chunkLogicalZ, chunkSize, settings, identity);
            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.MaximumHealth);
            _combatTarget = gameObject.AddComponent<EnemyCombatTarget>();
            _combatTarget.Initialize(enemyHealth, settings.VisualScale);
            _goldReward = gameObject.AddComponent<EnemyGoldReward>();
            _goldReward.Initialize(
                enemyHealth,
                player != null ? player.GetComponent<DroneGoldWallet>() : null,
                settings.GoldReward);
            BindTargets(player, playerHealth);
            SetState(GroundExploderState.Patrolling);
        }

        public void BindTargets(DroneCharacterController player, DroneHealth playerHealth)
        {
            _playerHealth = playerHealth;
            _goldReward?.BindWallet(player != null ? player.GetComponent<DroneGoldWallet>() : null);
            _proximity?.BindTarget(player);
            _damage?.BindTarget(player, playerHealth);
        }

        internal void Tick(float deltaTime)
        {
            if (_settings == null || (_playerHealth != null && _playerHealth.IsDead))
            {
                return;
            }

            _stateTime += deltaTime;
            switch (CurrentState)
            {
                case GroundExploderState.Patrolling:
                    float distanceMoved = _movement.TickPatrol(deltaTime);
                    _visual.TickPatrol(distanceMoved, deltaTime);
                    if (_proximity.IsTargetInside(_settings.DetectionRadius))
                    {
                        SetState(GroundExploderState.TriggeredWindUp);
                    }
                    break;

                case GroundExploderState.TriggeredWindUp:
                    _movement.TickStopped(deltaTime);
                    float windUp01 = Mathf.Clamp01(_stateTime / Mathf.Max(0.1f, _settings.WindUpDuration));
                    _visual.TickWindUp(windUp01, deltaTime);
                    if (_stateTime >= _settings.WindUpDuration)
                    {
                        Detonate();
                    }
                    break;

                case GroundExploderState.Exploding:
                    float explosion01 = Mathf.Clamp01(_stateTime / ExplosionPresentationDuration);
                    _visual.TickExplosion(explosion01, _settings.ExplosionRadius);
                    if (_stateTime >= ExplosionPresentationDuration)
                    {
                        SetState(GroundExploderState.Dead);
                    }
                    break;

                case GroundExploderState.Dead:
                    break;
            }
        }

        internal void FixedTick(float fixedDeltaTime)
        {
            _movement?.FixedTick(fixedDeltaTime);
        }

        private void Detonate()
        {
            SetState(GroundExploderState.Exploding);
            _damage.Detonate(transform.position, _settings.ExplosionRadius, _settings.MaximumDamage);
        }

        private void SetState(GroundExploderState state)
        {
            CurrentState = state;
            _stateTime = 0f;
            _combatTarget?.SetTargetable(
                state == GroundExploderState.Patrolling || state == GroundExploderState.TriggeredWindUp);
            switch (state)
            {
                case GroundExploderState.Patrolling:
                    _visual?.EnterPatrol();
                    break;
                case GroundExploderState.TriggeredWindUp:
                    _visual?.EnterWindUp();
                    break;
                case GroundExploderState.Exploding:
                    _visual?.EnterExplosion();
                    break;
                case GroundExploderState.Dead:
                    Destroy(gameObject);
                    break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_settings == null)
            {
                return;
            }
            Gizmos.color = new Color(1f, 0.65f, 0.05f, 0.55f);
            Gizmos.DrawWireSphere(transform.position, _settings.DetectionRadius);
            Gizmos.color = new Color(1f, 0.12f, 0.02f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, _settings.ExplosionRadius);
        }
    }

    [DisallowMultipleComponent]
    public sealed class GroundExploderMovement : MonoBehaviour
    {
        public float CurrentSpeed { get; private set; }

        private DuneHeightField _heightField;
        private GroundExploderTuning _settings;
        private System.Random _random;
        private Rigidbody _body;
        private Vector2 _patrolCenter;
        private Vector2 _patrolTarget;
        private Vector3 _lastMeasuredPosition;
        private double _chunkLogicalX;
        private double _chunkLogicalZ;
        private float _chunkSize;
        private float _groundClearance;
        private bool _hasTarget;
        private bool _patrolRequested;

        public void Initialize(
            DuneHeightField heightField,
            double chunkLogicalX,
            double chunkLogicalZ,
            float chunkSize,
            GroundExploderTuning settings,
            int identity)
        {
            _heightField = heightField;
            _chunkLogicalX = chunkLogicalX;
            _chunkLogicalZ = chunkLogicalZ;
            _chunkSize = chunkSize;
            _settings = settings;
            _random = new System.Random(identity);
            _patrolCenter = new Vector2(transform.localPosition.x, transform.localPosition.z);
            _groundClearance = Mathf.Max(0.2f, settings.VisualScale * 1.08f);
            SnapToSurface();

            SphereCollider bodyCollider = gameObject.AddComponent<SphereCollider>();
            bodyCollider.radius = settings.VisualScale * 1.05f;
            _body = gameObject.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _lastMeasuredPosition = _body.position;
            ChoosePatrolTarget();
        }

        public float TickPatrol(float deltaTime)
        {
            _patrolRequested = true;
            return MeasureDistance(deltaTime);
        }

        public void TickStopped(float deltaTime)
        {
            _patrolRequested = false;
            MeasureDistance(deltaTime);
        }

        internal void FixedTick(float fixedDeltaTime)
        {
            if (_body == null || _settings == null)
            {
                return;
            }

            Vector2 current = new Vector2(transform.localPosition.x, transform.localPosition.z);
            if (_patrolRequested && _settings.MovementSpeed > 0f)
            {
                if (!_hasTarget || (current - _patrolTarget).sqrMagnitude <= 1.21f)
                {
                    ChoosePatrolTarget();
                }

                Vector2 toTarget = _patrolTarget - current;
                Vector2 probe = current + (toTarget.sqrMagnitude > 0.001f ? toTarget.normalized * 1.6f : Vector2.zero);
                if (!IsValidSurface(probe))
                {
                    _hasTarget = false;
                    ChoosePatrolTarget();
                    toTarget = _patrolTarget - current;
                }

                if (toTarget.sqrMagnitude > 0.001f)
                {
                    Vector2 next = Vector2.MoveTowards(
                        current,
                        _patrolTarget,
                        _settings.MovementSpeed * fixedDeltaTime);
                    Vector3 surfaceNormal = SampleNormal(next);
                    Vector3 localPosition = new Vector3(
                        next.x,
                        SampleHeight(next) + _groundClearance,
                        next.y);
                    Vector3 worldPosition = transform.parent != null
                        ? transform.parent.TransformPoint(localPosition)
                        : localPosition;
                    Vector3 forward = Vector3.ProjectOnPlane(
                        new Vector3(toTarget.x, 0f, toTarget.y).normalized,
                        surfaceNormal).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(forward, surfaceNormal);
                    _body.MovePosition(worldPosition);
                    _body.MoveRotation(targetRotation);
                }
            }
        }

        private float MeasureDistance(float deltaTime)
        {
            if (_body == null || deltaTime <= 0f)
            {
                CurrentSpeed = 0f;
                return 0f;
            }

            Vector3 displacement = _body.position - _lastMeasuredPosition;
            Vector3 planarDisplacement = Vector3.ProjectOnPlane(displacement, Vector3.up);
            float distance = planarDisplacement.magnitude;
            CurrentSpeed = distance / deltaTime;
            _lastMeasuredPosition = _body.position;
            return distance;
        }

        private void ChoosePatrolTarget()
        {
            float radius = Mathf.Max(2f, _settings.PatrolRadius);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float angle = (float)(_random.NextDouble() * Mathf.PI * 2f);
                float distance = Mathf.Lerp(radius * 0.35f, radius, (float)_random.NextDouble());
                Vector2 candidate = _patrolCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                candidate.x = Mathf.Clamp(candidate.x, 4f, _chunkSize - 4f);
                candidate.y = Mathf.Clamp(candidate.y, 4f, _chunkSize - 4f);
                Vector2 midpoint = Vector2.Lerp(
                    new Vector2(transform.localPosition.x, transform.localPosition.z),
                    candidate,
                    0.5f);
                if (IsValidSurface(candidate) && IsValidSurface(midpoint))
                {
                    _patrolTarget = candidate;
                    _hasTarget = true;
                    return;
                }
            }
            _patrolTarget = _patrolCenter;
            _hasTarget = true;
        }

        private bool IsValidSurface(Vector2 local)
        {
            if (local.x < 3f || local.x > _chunkSize - 3f || local.y < 3f || local.y > _chunkSize - 3f)
            {
                return false;
            }
            return Vector3.Angle(SampleNormal(local), Vector3.up) <= _settings.MaximumGroundSlope;
        }

        private void SnapToSurface()
        {
            Vector2 local = new Vector2(transform.localPosition.x, transform.localPosition.z);
            transform.localPosition = new Vector3(local.x, SampleHeight(local) + _groundClearance, local.y);
            Vector3 normal = SampleNormal(local);
            Vector3 forward = Vector3.ProjectOnPlane(transform.localRotation * Vector3.forward, normal).normalized;
            transform.localRotation = Quaternion.LookRotation(forward, normal);
        }

        private float SampleHeight(Vector2 local)
        {
            return (float)_heightField.SampleHeight(_chunkLogicalX + local.x, _chunkLogicalZ + local.y);
        }

        private Vector3 SampleNormal(Vector2 local)
        {
            return _heightField.SampleNormal(_chunkLogicalX + local.x, _chunkLogicalZ + local.y);
        }
    }

    [DisallowMultipleComponent]
    public sealed class GroundExploderProximity : MonoBehaviour
    {
        private DroneCharacterController _target;

        public void BindTarget(DroneCharacterController target)
        {
            _target = target;
        }

        public bool IsTargetInside(float radius)
        {
            if (_target == null || radius <= 0f)
            {
                return false;
            }
            return (_target.WorldCenter - transform.position).sqrMagnitude <= radius * radius;
        }
    }

    [DisallowMultipleComponent]
    public sealed class GroundExploderDamage : MonoBehaviour
    {
        private DroneCharacterController _player;
        private DroneHealth _health;

        public void BindTarget(DroneCharacterController player, DroneHealth health)
        {
            _player = player;
            _health = health;
        }

        public void Detonate(Vector3 center, float radius, float maximumDamage)
        {
            if (_player == null || _health == null || _health.IsDead || radius <= 0f || maximumDamage <= 0f)
            {
                return;
            }

            float distance = Vector3.Distance(center, _player.WorldCenter);
            if (distance >= radius)
            {
                return;
            }

            float distance01 = Mathf.Clamp01(distance / radius);
            float damage = maximumDamage * (1f - distance01);
            _health.TakeDamage(damage, "Ground exploder blast");
        }
    }

    [DisallowMultipleComponent]
    public sealed class GroundExploderVisual : MonoBehaviour
    {
        private GroundExploderTuning _settings;
        private Transform _root;
        private Transform _wheel;
        private Quaternion _wheelBaseRotation;
        private Transform _beacon;
        private Transform[] _telegraphRings;
        private Transform _explosionFlash;
        private Renderer[] _renderers;
        private float _windUpTime;
        private float _wheelAngle;

        public void Initialize(Transform visualRoot, GroundExploderTuning settings)
        {
            _root = visualRoot;
            _settings = settings;
            _wheel = _root.Find("Spiked Hollow Wheel");
            _wheelBaseRotation = _wheel != null ? _wheel.localRotation : Quaternion.identity;
            _beacon = _root.Find("Warning Ring");
            _telegraphRings = new[]
            {
                _root.Find("Telegraph Ring 1"),
                _root.Find("Telegraph Ring 2"),
            };
            _explosionFlash = _root.Find("Explosion Flash");
            _renderers = _root.GetComponentsInChildren<Renderer>(true);
            EnterPatrol();
        }

        public void EnterPatrol()
        {
            _windUpTime = 0f;
            _root.localPosition = Vector3.zero;
            SetAllRenderers(true);
            if (_explosionFlash != null)
            {
                _explosionFlash.localScale = Vector3.zero;
            }
            for (int i = 0; i < _telegraphRings.Length; i++)
            {
                if (_telegraphRings[i] != null)
                {
                    _telegraphRings[i].localScale = Vector3.zero;
                }
            }
        }

        public void TickPatrol(float distanceMoved, float deltaTime)
        {
            float rollingRadius = Mathf.Max(0.05f, 1.08f * _settings.VisualScale);
            _wheelAngle = Mathf.Repeat(
                _wheelAngle + ((distanceMoved / rollingRadius) * Mathf.Rad2Deg),
                360f);
            if (_wheel != null)
            {
                _wheel.localRotation = _wheelBaseRotation * Quaternion.Euler(0f, 0f, _wheelAngle);
            }
            if (_beacon != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 3.5f) * 0.08f;
                _beacon.localScale = Vector3.one * pulse;
            }
            _root.localPosition = Vector3.zero;
        }

        public void EnterWindUp()
        {
            _windUpTime = 0f;
            if (_explosionFlash != null)
            {
                _explosionFlash.localScale = Vector3.zero;
            }
        }

        public void TickWindUp(float windUp01, float deltaTime)
        {
            _windUpTime += deltaTime;
            float pulseFrequency = Mathf.Lerp(4f, 16f, windUp01);
            float pulse = 0.5f + (Mathf.Sin(_windUpTime * pulseFrequency) * 0.5f);
            float warningStrength = Mathf.Lerp(0.75f, 1.45f, windUp01) * Mathf.Lerp(0.78f, 1f, pulse);

            if (_beacon != null)
            {
                _beacon.localScale = Vector3.one * warningStrength;
            }
            for (int i = 0; i < _telegraphRings.Length; i++)
            {
                Transform ring = _telegraphRings[i];
                if (ring == null)
                {
                    continue;
                }
                float baseRadius = 1.25f + (i * 0.32f);
                float targetScale = _settings.ExplosionRadius
                    / (baseRadius * Mathf.Max(0.01f, _settings.VisualScale));
                float stagger = Mathf.Clamp01((windUp01 * 1.12f) - (i * 0.08f));
                float growth = Mathf.SmoothStep(0f, 1f, stagger);
                float ringScale = Mathf.Lerp(0.3f, targetScale, growth) * Mathf.Lerp(0.9f, 1f, pulse);
                ring.localScale = Vector3.one * ringScale;
                ring.Rotate(Vector3.forward, (150f + (i * 55f)) * deltaTime, Space.Self);
            }

            // The powered legs are independent rigidbodies, so the warning animation
            // deliberately avoids moving their shared visual parent transform.
        }

        public void EnterExplosion()
        {
            _root.localPosition = Vector3.zero;
            SetAllRenderers(false);
            if (_explosionFlash != null)
            {
                Renderer flashRenderer = _explosionFlash.GetComponent<Renderer>();
                if (flashRenderer != null)
                {
                    flashRenderer.enabled = true;
                }
            }
        }

        public void TickExplosion(float explosion01, float explosionRadius)
        {
            if (_explosionFlash == null)
            {
                return;
            }
            float eased = 1f - Mathf.Pow(1f - explosion01, 3f);
            float worldDiameter = explosionRadius * 2f * eased;
            float rootScale = Mathf.Max(0.01f, _settings.VisualScale);
            _explosionFlash.localScale = Vector3.one * (worldDiameter / rootScale);
        }

        private void SetAllRenderers(bool enabled)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = enabled;
                }
            }
        }
    }
}
