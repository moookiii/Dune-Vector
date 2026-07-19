using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class EnemyHealth : MonoBehaviour
    {
        public float MaximumHealth { get; private set; }
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        public event Action<float> Damaged;
        public event Action Died;

        public void Initialize(float maximumHealth)
        {
            MaximumHealth = Mathf.Max(Mathf.Epsilon, maximumHealth);
            CurrentHealth = MaximumHealth;
            IsDead = false;
        }

        public bool TakeDamage(float damage)
        {
            if (IsDead || damage <= 0f)
            {
                return false;
            }

            damage /= Mathf.Max(0.1f, DuneVectorContractRisk.EnemyHealthMultiplier);

            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            Damaged?.Invoke(previousHealth - CurrentHealth);
            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
                Destroy(gameObject);
            }
            return CurrentHealth < previousHealth;
        }
    }

    [DisallowMultipleComponent]
    public sealed class EnemyCombatTarget : MonoBehaviour
    {
        private static readonly List<EnemyCombatTarget> Targets = new List<EnemyCombatTarget>();

        public Vector3 AimPoint => transform.position;
        public Vector3 Velocity { get; private set; }
        public float CollisionRadius { get; private set; }
        public bool IsValid => _targetable && isActiveAndEnabled && _health != null && !_health.IsDead;
        public static IReadOnlyList<EnemyCombatTarget> ActiveTargets => Targets;

        private EnemyHealth _health;
        private Vector3 _previousPosition;
        private bool _hasPreviousPosition;
        private bool _targetable = true;

        public void Initialize(EnemyHealth health, float collisionRadius)
        {
            _health = health;
            CollisionRadius = Mathf.Max(Mathf.Epsilon, collisionRadius);
            _previousPosition = transform.position;
            _hasPreviousPosition = true;
        }

        public void SetTargetable(bool targetable)
        {
            _targetable = targetable;
        }

        public bool ApplyDamage(float damage)
        {
            return IsValid && _health.TakeDamage(damage);
        }

        private void OnEnable()
        {
            if (!Targets.Contains(this))
            {
                Targets.Add(this);
            }
            _previousPosition = transform.position;
            _hasPreviousPosition = true;
        }

        private void LateUpdate()
        {
            Vector3 position = transform.position;
            Velocity = _hasPreviousPosition && Time.deltaTime > 0f
                ? (position - _previousPosition) / Time.deltaTime
                : Vector3.zero;
            _previousPosition = position;
            _hasPreviousPosition = true;
        }

        private void OnDisable()
        {
            Targets.Remove(this);
            Velocity = Vector3.zero;
        }

        public static bool TryFindHit(
            Vector3 segmentStart,
            Vector3 segmentEnd,
            float projectileRadius,
            out EnemyCombatTarget hitTarget,
            out Vector3 hitPoint,
            out float hitDistance)
        {
            hitTarget = null;
            hitPoint = segmentEnd;
            hitDistance = float.PositiveInfinity;
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLength = segment.magnitude;
            float segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return false;
            }

            for (int i = Targets.Count - 1; i >= 0; i--)
            {
                EnemyCombatTarget candidate = Targets[i];
                if (candidate == null)
                {
                    Targets.RemoveAt(i);
                    continue;
                }
                if (!candidate.IsValid)
                {
                    continue;
                }

                float segment01 = Mathf.Clamp01(Vector3.Dot(candidate.AimPoint - segmentStart, segment) / segmentLengthSquared);
                Vector3 closestPoint = segmentStart + (segment * segment01);
                float combinedRadius = projectileRadius + candidate.CollisionRadius;
                if ((candidate.AimPoint - closestPoint).sqrMagnitude > combinedRadius * combinedRadius)
                {
                    continue;
                }

                float distance = segmentLength * segment01;
                if (distance < hitDistance)
                {
                    hitTarget = candidate;
                    hitPoint = closestPoint;
                    hitDistance = distance;
                }
            }
            return hitTarget != null;
        }
    }

    [DefaultExecutionOrder(230)]
    [DisallowMultipleComponent]
    public sealed class DroneTargetDetector : MonoBehaviour
    {
        public EnemyCombatTarget SelectedTarget { get; private set; }

        private Camera _camera;
        private EnergyLauncherTuning _settings;
        private DuneVectorCourierGame _courierGame;
        private float _scanTimer;
        private float _outsideTargetAreaTime;

        public void Initialize(
            Camera camera,
            EnergyLauncherTuning settings,
            DuneVectorCourierGame courierGame = null)
        {
            _camera = camera;
            _settings = settings;
            _courierGame = courierGame;
            _scanTimer = 0f;
        }

        private void LateUpdate()
        {
            if (_camera == null || _settings == null || !_settings.Enabled
                || (_courierGame != null && _courierGame.State == CourierRunState.Hub))
            {
                SelectTarget(null);
                return;
            }

            ValidateCurrentTarget(Time.deltaTime);
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f)
            {
                return;
            }

            _scanTimer = Mathf.Max(0.01f, _settings.TargetScanInterval);
            ScanForBestTarget();
        }

        public bool IsStrictlyEligible(EnemyCombatTarget target)
        {
            if (target == null || !target.IsValid || _camera == null || _settings == null)
            {
                return false;
            }

            Vector3 toTarget = target.AimPoint - _camera.transform.position;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon || distance > _settings.LockRange)
            {
                return false;
            }

            Vector3 direction = toTarget / distance;
            float forwardDot = Vector3.Dot(_camera.transform.forward, direction);
            if (forwardDot <= 0f)
            {
                return false;
            }

            float minimumDot = Mathf.Cos(_settings.LockConeAngle * 0.5f * Mathf.Deg2Rad);
            return forwardDot >= minimumDot;
        }

        private void ValidateCurrentTarget(float deltaTime)
        {
            if (SelectedTarget == null || !SelectedTarget.IsValid)
            {
                SelectTarget(null);
                return;
            }

            Vector3 toTarget = SelectedTarget.AimPoint - _camera.transform.position;
            if (toTarget.sqrMagnitude <= Mathf.Epsilon
                || Vector3.Dot(_camera.transform.forward, toTarget.normalized) <= 0f)
            {
                SelectTarget(null);
                return;
            }

            if (IsStrictlyEligible(SelectedTarget))
            {
                _outsideTargetAreaTime = 0f;
                return;
            }

            _outsideTargetAreaTime += deltaTime;
            if (_outsideTargetAreaTime >= _settings.TargetLossTolerance)
            {
                SelectTarget(null);
            }
        }

        private void ScanForBestTarget()
        {
            EnemyCombatTarget bestTarget = null;
            float bestScore = float.PositiveInfinity;
            IReadOnlyList<EnemyCombatTarget> targets = EnemyCombatTarget.ActiveTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                EnemyCombatTarget candidate = targets[i];
                if (!IsStrictlyEligible(candidate))
                {
                    continue;
                }

                float score = GetViewCenterScore(candidate);
                if (score < bestScore)
                {
                    bestTarget = candidate;
                    bestScore = score;
                }
            }

            if (SelectedTarget != null)
            {
                if (!IsStrictlyEligible(SelectedTarget))
                {
                    return;
                }

                float currentScore = GetViewCenterScore(SelectedTarget);
                if (bestTarget == null || bestTarget == SelectedTarget
                    || bestScore + _settings.TargetSwitchAdvantage >= currentScore)
                {
                    return;
                }
            }

            SelectTarget(bestTarget);
        }

        private float GetViewCenterScore(EnemyCombatTarget target)
        {
            Vector3 toTarget = target.AimPoint - _camera.transform.position;
            float distance = toTarget.magnitude;
            float angle = Vector3.Angle(_camera.transform.forward, toTarget);
            float halfCone = Mathf.Max(Mathf.Epsilon, _settings.LockConeAngle * 0.5f);
            float centeredScore = angle / halfCone;
            float distanceScore = distance / Mathf.Max(Mathf.Epsilon, _settings.LockRange);
            return centeredScore + (distanceScore * _settings.DistanceScoreWeight);
        }

        private void SelectTarget(EnemyCombatTarget target)
        {
            if (SelectedTarget == target)
            {
                return;
            }
            SelectedTarget = target;
            _outsideTargetAreaTime = 0f;
        }
    }

    public enum DroneLockOnState
    {
        None,
        TargetDetected,
        Locking,
        Locked,
    }

    [DefaultExecutionOrder(240)]
    [DisallowMultipleComponent]
    public sealed class DroneLockOnController : MonoBehaviour
    {
        public DroneLockOnState State { get; private set; }
        public EnemyCombatTarget Target { get; private set; }
        public event Action<DroneLockOnState> StateChanged;
        public float AcquisitionProgress => State switch
        {
            DroneLockOnState.Locked => 1f,
            DroneLockOnState.Locking => Mathf.Clamp01(_stateTime / Mathf.Max(Mathf.Epsilon, CurrentAcquisitionTime)),
            _ => 0f,
        };

        private DroneTargetDetector _detector;
        private EnergyLauncherTuning _settings;
        private DronePermanentUpgradeSystem _upgrades;
        private float _stateTime;
        private float CurrentAcquisitionTime => _upgrades != null
            ? _upgrades.GetCurrentValue(DroneUpgradeId.LockOnSpeed)
            : _settings != null ? _settings.AcquisitionTime : 0f;

        public void Initialize(
            DroneTargetDetector detector,
            EnergyLauncherTuning settings,
            DronePermanentUpgradeSystem upgrades = null)
        {
            _detector = detector;
            _settings = settings;
            _upgrades = upgrades;
            SetState(DroneLockOnState.None, null);
        }

        private void LateUpdate()
        {
            EnemyCombatTarget detectedTarget = _detector != null ? _detector.SelectedTarget : null;
            if (detectedTarget == null || !detectedTarget.IsValid)
            {
                SetState(DroneLockOnState.None, null);
                return;
            }

            if (detectedTarget != Target)
            {
                SetState(DroneLockOnState.TargetDetected, detectedTarget);
                return;
            }

            _stateTime += Time.deltaTime;
            if (State == DroneLockOnState.TargetDetected && _stateTime >= _settings.TargetDetectedDuration)
            {
                SetState(DroneLockOnState.Locking, Target);
            }
            else if (State == DroneLockOnState.Locking && _stateTime >= CurrentAcquisitionTime)
            {
                SetState(DroneLockOnState.Locked, Target);
            }
        }

        private void SetState(DroneLockOnState state, EnemyCombatTarget target)
        {
            if (State == state && Target == target)
            {
                return;
            }
            State = state;
            Target = target;
            _stateTime = 0f;
            StateChanged?.Invoke(State);
        }
    }

    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class DroneEnergyLauncher : MonoBehaviour
    {
        public bool CanFire => _cooldownRemaining <= 0f;
        public event System.Action Fired;

        private DroneCharacterController _drone;
        private Camera _camera;
        private DesertWorldStreamer _world;
        private DuneVectorAudioManager _audioManager;
        private DroneLockOnController _lockController;
        private EnergyLauncherTuning _settings;
        private DronePermanentUpgradeSystem _upgrades;
        private Material _energyMaterial;
        private float _cooldownRemaining;
        private bool _fireRequested;
        private float _environmentalCooldownMultiplier = 1f;

        public void Initialize(
            DroneCharacterController drone,
            Camera camera,
            DesertWorldStreamer world,
            DuneVectorAudioManager audioManager,
            DroneLockOnController lockController,
            EnergyLauncherTuning settings,
            DronePermanentUpgradeSystem upgrades = null)
        {
            _drone = drone;
            _camera = camera;
            _world = world;
            _audioManager = audioManager;
            _lockController = lockController;
            _settings = settings;
            _upgrades = upgrades;
            _energyMaterial = CreateEnergyMaterial(settings);
            if (_upgrades != null)
            {
                _upgrades.UpgradePurchased += HandleUpgradePurchased;
            }
        }

        public void RequestFire()
        {
            _fireRequested = true;
        }

        public void SetEnvironmentalCooldownMultiplier(float multiplier)
        {
            _environmentalCooldownMultiplier = Mathf.Max(1f, multiplier);
        }

        private void LateUpdate()
        {
            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - Time.deltaTime);
            if (!_fireRequested)
            {
                return;
            }

            _fireRequested = false;
            TryFire();
        }

        private bool TryFire()
        {
            if (!CanFire || _drone == null || _camera == null || _settings == null || !_settings.Enabled)
            {
                return false;
            }

            Transform cameraTransform = _camera.transform;
            Vector3 muzzlePosition = _drone.WorldCenter + cameraTransform.TransformVector(_settings.MuzzleOffset);
            Vector3 launchDirection = cameraTransform.forward.normalized;
            EnemyCombatTarget homingTarget = _lockController != null
                && _lockController.State == DroneLockOnState.Locked
                && _lockController.Target != null
                && _lockController.Target.IsValid
                    ? _lockController.Target
                    : null;

            GameObject projectileObject = new GameObject(homingTarget != null
                ? "Locked Homing Energy Shot"
                : "Forward Energy Shot");
            projectileObject.transform.SetParent(transform, true);
            projectileObject.transform.SetPositionAndRotation(
                muzzlePosition,
                Quaternion.LookRotation(launchDirection, cameraTransform.up));
            HomingEnergyProjectile projectile = projectileObject.AddComponent<HomingEnergyProjectile>();
            projectile.Initialize(
                launchDirection,
                homingTarget,
                _drone.transform,
                _world,
                _energyMaterial,
                _settings,
                GetCurrentDamage());

            EnergyPulseFeedback.Spawn(
                muzzlePosition,
                _energyMaterial,
                _settings.LaunchFlashScale,
                _settings.LaunchFlashDuration,
                "Energy Launch Flash");
            _audioManager?.PlayDroneFire(muzzlePosition);
            _cooldownRemaining = GetCurrentCooldown();
            Fired?.Invoke();
            return true;
        }

        private float GetCurrentDamage()
        {
            return _upgrades != null
                ? _upgrades.GetCurrentValue(DroneUpgradeId.EnergyShotDamage)
                : _settings.Damage;
        }

        private float GetCurrentCooldown()
        {
            float baseCooldown = _upgrades != null
                ? _upgrades.GetCurrentValue(DroneUpgradeId.EnergyShotCooldown)
                : _settings.FireCooldown;
            return baseCooldown * _environmentalCooldownMultiplier;
        }

        private void HandleUpgradePurchased(DroneUpgradeId id, int purchasedTier, int goldCost)
        {
            if (id == DroneUpgradeId.EnergyShotCooldown)
            {
                _cooldownRemaining = Mathf.Min(_cooldownRemaining, GetCurrentCooldown());
            }
        }

        private static Material CreateEnergyMaterial(EnergyLauncherTuning settings)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                shader = Shader.Find("HDRP/Unlit");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("The energy launcher requires an HDRP-compatible shader.");
            }

            Material material = new Material(shader) { name = "Drone Energy Launcher - Runtime" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", settings.ProjectileColor);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", settings.ProjectileColor);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", settings.ProjectileEmission);
            if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            return material;
        }

        private void OnDestroy()
        {
            if (_upgrades != null)
            {
                _upgrades.UpgradePurchased -= HandleUpgradePurchased;
            }
            if (_energyMaterial != null)
            {
                Destroy(_energyMaterial);
            }
        }
    }

    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent]
    public sealed class HomingEnergyProjectile : MonoBehaviour
    {
        private const int MaximumCollisionHits = 16;
        private readonly RaycastHit[] _collisionHits = new RaycastHit[MaximumCollisionHits];

        private EnemyCombatTarget _target;
        private Transform _owner;
        private DesertWorldStreamer _world;
        private Material _material;
        private EnergyLauncherTuning _settings;
        private float _damage;
        private TrailRenderer _trail;
        private Vector3 _velocity;
        private float _age;

        public void Initialize(
            Vector3 direction,
            EnemyCombatTarget target,
            Transform owner,
            DesertWorldStreamer world,
            Material material,
            EnergyLauncherTuning settings,
            float damage)
        {
            _target = target;
            _owner = owner;
            _world = world;
            _material = material;
            _settings = settings;
            _damage = Mathf.Max(0f, damage);
            _velocity = direction.normalized * settings.ProjectileSpeed;
            CreateVisual();
            if (_world != null)
            {
                _world.WorldShifted += HandleWorldShift;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            _age += deltaTime;
            if (_age >= _settings.ProjectileLifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (_target != null && _target.IsValid)
            {
                Vector3 toTarget = _target.AimPoint - transform.position;
                float travelTime = Mathf.Min(
                    _settings.LeadPredictionTime,
                    toTarget.magnitude / Mathf.Max(Mathf.Epsilon, _settings.ProjectileSpeed));
                Vector3 targetVelocity = Vector3.ClampMagnitude(_target.Velocity, _settings.MaximumLeadSpeed);
                Vector3 predictedPoint = _target.AimPoint + (targetVelocity * travelTime);
                Vector3 desiredDirection = predictedPoint - transform.position;
                if (desiredDirection.sqrMagnitude > Mathf.Epsilon)
                {
                    float maximumTurn = _settings.HomingTurnStrength * Mathf.Deg2Rad * deltaTime;
                    Vector3 steeredDirection = Vector3.RotateTowards(
                        _velocity.normalized,
                        desiredDirection.normalized,
                        maximumTurn,
                        0f);
                    _velocity = steeredDirection * _settings.ProjectileSpeed;
                }
            }
            else
            {
                _target = null;
            }

            Vector3 start = transform.position;
            Vector3 end = start + (_velocity * deltaTime);
            if (TryResolveCollision(start, end, out EnemyCombatTarget hitTarget, out Vector3 hitPoint))
            {
                hitTarget?.ApplyDamage(_damage);
                EnergyPulseFeedback.Spawn(
                    hitPoint,
                    _material,
                    _settings.ImpactFlashScale,
                    _settings.ImpactFlashDuration,
                    hitTarget != null ? "Energy Hit Flash" : "Energy Impact Flash");
                Destroy(gameObject);
                return;
            }

            transform.position = end;
            if (_velocity.sqrMagnitude > Mathf.Epsilon)
            {
                transform.rotation = Quaternion.LookRotation(_velocity.normalized, transform.up);
            }
        }

        private bool TryResolveCollision(
            Vector3 start,
            Vector3 end,
            out EnemyCombatTarget hitTarget,
            out Vector3 hitPoint)
        {
            hitTarget = null;
            hitPoint = end;
            float closestDistance = float.PositiveInfinity;
            if (EnemyCombatTarget.TryFindHit(
                    start,
                    end,
                    _settings.ProjectileHitRadius,
                    out EnemyCombatTarget registryTarget,
                    out Vector3 registryPoint,
                    out float registryDistance))
            {
                hitTarget = registryTarget;
                hitPoint = registryPoint;
                closestDistance = registryDistance;
            }

            Vector3 segment = end - start;
            float segmentLength = segment.magnitude;
            if (segmentLength <= Mathf.Epsilon)
            {
                return hitTarget != null;
            }

            int hitCount = Physics.SphereCastNonAlloc(
                start,
                _settings.ProjectileHitRadius,
                segment / segmentLength,
                _collisionHits,
                segmentLength,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _collisionHits[i];
                if (hit.collider == null || IsOwnerCollider(hit.collider))
                {
                    continue;
                }

                EnemyCombatTarget colliderTarget = hit.collider.GetComponentInParent<EnemyCombatTarget>();
                if (colliderTarget != null && !colliderTarget.IsValid)
                {
                    continue;
                }
                if (hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                hitTarget = colliderTarget;
                hitPoint = hit.point;
            }
            return closestDistance < float.PositiveInfinity;
        }

        private bool IsOwnerCollider(Collider collider)
        {
            return _owner != null
                && (collider.transform == _owner || collider.transform.IsChildOf(_owner));
        }

        private void CreateVisual()
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Energy Core";
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * _settings.ProjectileScale;
            Collider collider = core.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            MeshRenderer renderer = core.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = _material;
            _trail.time = _settings.TrailDuration;
            _trail.startWidth = _settings.TrailStartWidth;
            _trail.endWidth = 0f;
            _trail.minVertexDistance = _settings.TrailMinimumVertexDistance;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.emitting = true;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            transform.position += shift;
            _trail?.Clear();
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
    public sealed class EnergyPulseFeedback : MonoBehaviour
    {
        private float _duration;
        private float _age;
        private float _maximumScale;

        public static void Spawn(
            Vector3 position,
            Material material,
            float maximumScale,
            float duration,
            string objectName)
        {
            GameObject pulseObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pulseObject.name = objectName;
            pulseObject.transform.position = position;
            Collider collider = pulseObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            MeshRenderer renderer = pulseObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EnergyPulseFeedback pulse = pulseObject.AddComponent<EnergyPulseFeedback>();
            pulse._maximumScale = maximumScale;
            pulse._duration = Mathf.Max(Mathf.Epsilon, duration);
            pulseObject.transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float progress = Mathf.Clamp01(_age / _duration);
            float pulse = Mathf.Sin(progress * Mathf.PI);
            transform.localScale = Vector3.one * (_maximumScale * pulse);
            if (_age >= _duration)
            {
                Destroy(gameObject);
            }
        }
    }

    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class DroneLockOnHUD : MonoBehaviour
    {
        private Camera _camera;
        private DroneLockOnController _lockController;
        private EnergyLauncherTuning _settings;
        private GUIStyle _statusStyle;
        private GUIStyle _distanceStyle;

        public void Initialize(
            Camera camera,
            DroneLockOnController lockController,
            EnergyLauncherTuning settings)
        {
            _camera = camera;
            _lockController = lockController;
            _settings = settings;
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (_camera == null || _lockController == null || _settings == null || !_settings.Enabled)
            {
                return;
            }

            float scale = Screen.height / Mathf.Max(Mathf.Epsilon, _settings.HudReferenceHeight);
            DrawCenterReticle(scale);
            EnemyCombatTarget target = _lockController.Target;
            if (target == null || !target.IsValid || _lockController.State == DroneLockOnState.None)
            {
                return;
            }

            Vector3 screenPosition = _camera.WorldToScreenPoint(target.AimPoint);
            if (screenPosition.z <= 0f)
            {
                return;
            }

            Vector2 center = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            float progress = _lockController.AcquisitionProgress;
            float size = Mathf.Lerp(
                _settings.TargetDetectedReticleSize,
                _settings.LockedReticleSize,
                progress) * scale;
            if (_lockController.State == DroneLockOnState.Locked)
            {
                size += Mathf.Sin(Time.unscaledTime * _settings.ReticlePulseSpeed)
                    * _settings.LockedPulseAmount * scale;
            }

            Color color = GetStateColor();
            DrawTargetBrackets(
                center,
                size,
                _settings.TargetBracketLength * scale,
                _settings.ReticleLineThickness * scale,
                color);
            if (_lockController.State == DroneLockOnState.Locked)
            {
                DrawRect(
                    new Rect(
                        center.x - (_settings.LockedConfirmationSize * scale * 0.5f),
                        center.y - (_settings.LockedConfirmationSize * scale * 0.5f),
                        _settings.LockedConfirmationSize * scale,
                        _settings.LockedConfirmationSize * scale),
                    color);
            }
            DrawTargetLabels(center, size, scale, color, target);
        }

        private void DrawCenterReticle(float scale)
        {
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float size = _settings.CenterReticleSize * scale;
            float gap = _settings.CenterReticleGap * scale;
            float thickness = _settings.ReticleLineThickness * scale;
            float armLength = Mathf.Max(0f, (size - gap) * 0.5f);
            DrawRect(new Rect(center.x - gap * 0.5f - armLength, center.y - thickness * 0.5f, armLength, thickness), _settings.CenterReticleColor);
            DrawRect(new Rect(center.x + gap * 0.5f, center.y - thickness * 0.5f, armLength, thickness), _settings.CenterReticleColor);
            DrawRect(new Rect(center.x - thickness * 0.5f, center.y - gap * 0.5f - armLength, thickness, armLength), _settings.CenterReticleColor);
            DrawRect(new Rect(center.x - thickness * 0.5f, center.y + gap * 0.5f, thickness, armLength), _settings.CenterReticleColor);
        }

        private static void DrawTargetBrackets(
            Vector2 center,
            float size,
            float bracketLength,
            float thickness,
            Color color)
        {
            float half = size * 0.5f;
            float left = center.x - half;
            float right = center.x + half;
            float top = center.y - half;
            float bottom = center.y + half;

            DrawRect(new Rect(left, top, bracketLength, thickness), color);
            DrawRect(new Rect(left, top, thickness, bracketLength), color);
            DrawRect(new Rect(right - bracketLength, top, bracketLength, thickness), color);
            DrawRect(new Rect(right - thickness, top, thickness, bracketLength), color);
            DrawRect(new Rect(left, bottom - thickness, bracketLength, thickness), color);
            DrawRect(new Rect(left, bottom - bracketLength, thickness, bracketLength), color);
            DrawRect(new Rect(right - bracketLength, bottom - thickness, bracketLength, thickness), color);
            DrawRect(new Rect(right - thickness, bottom - bracketLength, thickness, bracketLength), color);
        }

        private void DrawTargetLabels(
            Vector2 center,
            float reticleSize,
            float scale,
            Color color,
            EnemyCombatTarget target)
        {
            EnsureStyles(scale);
            _statusStyle.normal.textColor = color;
            _distanceStyle.normal.textColor = color;
            float labelWidth = _settings.HudLabelWidth * scale;
            float labelHeight = _settings.HudLabelHeight * scale;
            float labelX = center.x - (labelWidth * 0.5f);
            string status = _lockController.State switch
            {
                DroneLockOnState.TargetDetected => "TARGET DETECTED",
                DroneLockOnState.Locking => $"LOCKING {Mathf.RoundToInt(_lockController.AcquisitionProgress * 100f):00}%",
                DroneLockOnState.Locked => "LOCKED",
                _ => string.Empty,
            };
            GUI.Label(
                new Rect(
                    labelX,
                    center.y - (reticleSize * 0.5f) - (_settings.TargetStatusOffset * scale) - labelHeight,
                    labelWidth,
                    labelHeight),
                status,
                _statusStyle);

            float distance = Vector3.Distance(_camera.transform.position, target.AimPoint);
            GUI.Label(
                new Rect(
                    labelX,
                    center.y + (reticleSize * 0.5f) + (_settings.TargetDistanceOffset * scale),
                    labelWidth,
                    labelHeight),
                $"{distance:0} m",
                _distanceStyle);
        }

        private void EnsureStyles(float scale)
        {
            _statusStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            _distanceStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
            };
            _statusStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(_settings.TargetStatusFontSize * scale));
            _distanceStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(_settings.TargetDistanceFontSize * scale));
        }

        private Color GetStateColor()
        {
            return _lockController.State switch
            {
                DroneLockOnState.TargetDetected => _settings.TargetDetectedColor,
                DroneLockOnState.Locking => _settings.LockingColor,
                DroneLockOnState.Locked => _settings.LockedColor,
                _ => Color.clear,
            };
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
