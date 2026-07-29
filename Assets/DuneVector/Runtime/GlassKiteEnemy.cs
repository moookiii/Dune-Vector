using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum GlassKiteState
    {
        Patrol,
        Telegraph,
        AttackRun,
        Recovery,
        Crashing,
        Wreck,
    }

    [DisallowMultipleComponent]
    public sealed class GlassKiteEnemy : MonoBehaviour
    {
        private readonly Transform[] _wingJoints = new Transform[4];
        private readonly EnemyCombatTarget[] _jointTargets = new EnemyCombatTarget[4];
        private readonly Renderer[] _jointLights = new Renderer[4];
        private readonly bool[] _destroyedWings = new bool[4];
        private readonly List<Renderer> _attackSeams = new List<Renderer>();

        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private GlassKiteTuning _settings;
        private Transform _cachedTransform;
        private Transform _visual;
        private Material _bodyMaterial;
        private Material _lightMaterial;
        private Material _seamMaterial;
        private Material _wakeMaterial;
        private Vector3 _flightDirection;
        private Vector3 _committedCrossingPoint;
        private Vector3 _committedDirection;
        private float _stateTime;
        private float _attackCooldown;
        private float _patrolPhase;
        private float _attackLight;
        private float _altitudeLoss;
        private float _firstDestroyedSide;
        private int _destroyedJoints;
        private bool _wakeReleased;
        private bool _wreckInitialized;

        public GlassKiteState CurrentState { get; private set; }
        public int DestroyedJointCount => _destroyedJoints;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            GlassKiteTuning settings,
            int identity)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _cachedTransform = transform;
            _patrolPhase = identity * 1.618f;
            _flightDirection = Quaternion.Euler(0f, Mathf.Repeat(identity * 137.5f, 360f), 0f) * Vector3.forward;
            _attackCooldown = settings.AttackCooldown * Mathf.Repeat((identity * 0.41f) + 0.35f, 1f);

            CreateMaterials();
            BuildVisual();
            SetState(GlassKiteState.Patrol);
            DuneVectorPhotographableMarker.Register(
                gameObject,
                DuneVectorCompendiumSubjectIds.GlassKite,
                PhotographableSubjectCategory.Enemy,
                new Bounds(Vector3.zero, new Vector3(settings.WingSpan, settings.BodyScale.y * 5f, settings.BodyScale.z * 2f)));
        }

        private void Update()
        {
            if (_settings == null || _world == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _stateTime += deltaTime;
            if (CurrentState != GlassKiteState.Crashing && CurrentState != GlassKiteState.Wreck)
            {
                _attackCooldown = Mathf.Max(
                    0f,
                    _attackCooldown - (deltaTime * DuneVectorContractRisk.EnemyAttackRateMultiplier));
            }
            if (_destroyedJoints >= 3
                && CurrentState != GlassKiteState.Crashing
                && CurrentState != GlassKiteState.Wreck)
            {
                _altitudeLoss += _settings.ThirdJointDescentRate * deltaTime;
            }

            switch (CurrentState)
            {
                case GlassKiteState.Patrol:
                    UpdatePatrol(deltaTime);
                    break;
                case GlassKiteState.Telegraph:
                    UpdateTelegraph(deltaTime);
                    break;
                case GlassKiteState.AttackRun:
                    UpdateAttackRun(deltaTime);
                    break;
                case GlassKiteState.Recovery:
                    UpdateRecovery(deltaTime);
                    break;
                case GlassKiteState.Crashing:
                    UpdateCrash(deltaTime);
                    break;
            }

            UpdatePresentation(deltaTime);
        }

        private void UpdatePatrol(float deltaTime)
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                return;
            }

            Vector3 playerPosition = _player.WorldCenter;
            Vector3 horizontalOffset = Vector3.ProjectOnPlane(playerPosition - _cachedTransform.position, Vector3.up);
            float repositionDistance = _settings.RepositionDistance;
            if (horizontalOffset.sqrMagnitude > repositionDistance * repositionDistance)
            {
                RepositionNearPlayer(playerPosition);
                horizontalOffset = Vector3.ProjectOnPlane(playerPosition - _cachedTransform.position, Vector3.up);
            }

            Vector3 desiredDirection = _flightDirection;
            float routeRadius = _settings.PatrolRouteRadius;
            if (horizontalOffset.sqrMagnitude > routeRadius * routeRadius)
            {
                desiredDirection = horizontalOffset.normalized;
            }
            else
            {
                float routeYaw = Mathf.Sin((Time.time * _settings.PatrolSpeed / Mathf.Max(1f, routeRadius)) + _patrolPhase);
                desiredDirection = Quaternion.Euler(0f, routeYaw * _settings.TurnRate * deltaTime, 0f) * _flightDirection;
            }

            RotateFlightDirection(desiredDirection, _settings.TurnRate, deltaTime);
            MoveAtSpeed(_settings.PatrolSpeed, GetDesiredPatrolAltitude(), deltaTime);

            float terrainHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            float playerClearance = playerPosition.y - terrainHeight;
            bool playerEnteredBand = playerClearance >= _settings.AggressionMinimumTerrainClearance
                && Mathf.Abs(_cachedTransform.position.y - playerPosition.y) <= _settings.AggressionAltitudeBand;
            if (_attackCooldown <= 0f && playerEnteredBand)
            {
                BeginTelegraph();
            }
        }

        private void BeginTelegraph()
        {
            PredictCrossing();
            _wakeReleased = false;
            SetState(GlassKiteState.Telegraph);
        }

        private void UpdateTelegraph(float deltaTime)
        {
            if (_player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                SetState(GlassKiteState.Patrol);
                return;
            }

            float commitTime = _settings.TelegraphDuration * _settings.PredictionCommitFraction;
            if (_stateTime <= commitTime)
            {
                PredictCrossing();
            }

            Vector3 toCrossing = _committedCrossingPoint - _cachedTransform.position;
            if (toCrossing.sqrMagnitude > 0.001f)
            {
                RotateFlightDirection(toCrossing.normalized, GetCurrentTurnRate(), deltaTime);
            }
            MoveAtSpeed(_settings.PatrolSpeed, _cachedTransform.position.y, deltaTime);

            if (_stateTime >= _settings.TelegraphDuration)
            {
                _committedDirection = (_committedCrossingPoint - _cachedTransform.position).normalized;
                if (_committedDirection.sqrMagnitude <= 0.001f)
                {
                    _committedDirection = _flightDirection;
                }
                _flightDirection = _committedDirection;
                SetState(GlassKiteState.AttackRun);
            }
        }

        private void PredictCrossing()
        {
            Vector3 playerPosition = _player.WorldCenter;
            Vector3 playerVelocity = _player.Motor != null ? _player.Motor.Velocity : Vector3.zero;
            float attackSpeed = Mathf.Max(1f, GetCurrentAttackSpeed());
            float predictionTime = Mathf.Clamp(
                Vector3.Distance(_cachedTransform.position, playerPosition) / attackSpeed,
                _settings.MinimumPredictionTime,
                _settings.MaximumPredictionTime);
            _committedCrossingPoint = playerPosition + (playerVelocity * predictionTime);

            if (_destroyedJoints >= 2)
            {
                float sign = Mathf.Sin(_patrolPhase + Time.time) >= 0f ? 1f : -1f;
                _committedCrossingPoint += _cachedTransform.right * (_settings.SecondJointAccuracyPenalty * sign);
            }
        }

        private void UpdateAttackRun(float deltaTime)
        {
            Vector3 before = _committedCrossingPoint - _cachedTransform.position;
            _cachedTransform.position += _committedDirection
                * GetCurrentAttackSpeed()
                * DuneVectorContractRisk.EnemySpeedMultiplier
                * deltaTime;
            _flightDirection = _committedDirection;
            Vector3 after = _committedCrossingPoint - _cachedTransform.position;

            bool crossed = Vector3.Dot(before, _committedDirection) > 0f
                && Vector3.Dot(after, _committedDirection) <= 0f;
            bool closeEnough = after.sqrMagnitude <= _settings.AttackCommitDistance * _settings.AttackCommitDistance;
            if (!_wakeReleased && (crossed || closeEnough))
            {
                ReleaseRazorWake(_committedCrossingPoint);
                _wakeReleased = true;
            }

            if (_wakeReleased
                && Vector3.Dot(_cachedTransform.position - _committedCrossingPoint, _committedDirection)
                >= _settings.AttackExitDistance)
            {
                SetState(GlassKiteState.Recovery);
                SetJointTargetability(true);
            }
        }

        private void UpdateRecovery(float deltaTime)
        {
            float desiredAltitude = GetDesiredPatrolAltitude();
            MoveAtSpeed(_settings.RecoverySpeed, desiredAltitude, deltaTime);
            if (_stateTime >= _settings.RecoveryDuration)
            {
                SetJointTargetability(false);
                _attackCooldown = _settings.AttackCooldown;
                SetState(GlassKiteState.Patrol);
            }
        }

        private void ReleaseRazorWake(Vector3 crossingPoint)
        {
            GameObject wakeObject = new GameObject("Glass Kite Razor Wake");
            wakeObject.transform.SetParent(transform.parent, true);
            GlassKiteRazorWake wake = wakeObject.AddComponent<GlassKiteRazorWake>();
            wake.Initialize(
                _player,
                _playerHealth,
                _settings,
                crossingPoint,
                _committedDirection,
                Vector3.up,
                _firstDestroyedSide,
                _wakeMaterial);
        }

        private void UpdateCrash(float deltaTime)
        {
            Vector3 position = _cachedTransform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(_flightDirection, Vector3.up).normalized;
            if (horizontal.sqrMagnitude <= 0.001f)
            {
                horizontal = _cachedTransform.forward;
            }

            horizontal = Quaternion.Euler(0f, _settings.CrashSpiralDegreesPerSecond * deltaTime, 0f) * horizontal;
            _flightDirection = horizontal;
            position += horizontal * _settings.CrashForwardSpeed * deltaTime;
            position.y -= _settings.CrashDescentSpeed * deltaTime;
            _cachedTransform.position = position;
            _cachedTransform.Rotate(Vector3.forward, _settings.CrashRollDegreesPerSecond * deltaTime, Space.Self);

            float terrainHeight = _world.SampleHeightAtLocal(position.x, position.z);
            if (position.y <= terrainHeight + _settings.CrashTerrainClearance)
            {
                _cachedTransform.position = new Vector3(position.x, terrainHeight + _settings.CrashTerrainClearance, position.z);
                CreateWreckLandmark();
            }
        }

        private void CreateWreckLandmark()
        {
            if (_wreckInitialized)
            {
                return;
            }
            _wreckInitialized = true;
            SetState(GlassKiteState.Wreck);
            enabled = false;
            SetJointTargetability(false);

            GlassKiteCrashLandmark wreck = gameObject.AddComponent<GlassKiteCrashLandmark>();
            wreck.Initialize(_player, _settings);
            CreateSmokeColumn();
        }

        private void CreateSmokeColumn()
        {
            GameObject smokeObject = new GameObject("Glass Kite Crash Smoke");
            smokeObject.transform.SetParent(_cachedTransform, false);
            smokeObject.transform.rotation = Quaternion.identity;
            ParticleSystem smoke = smokeObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = smoke.main;
            main.loop = true;
            main.startLifetime = _settings.SmokeLifetime;
            main.startSpeed = _settings.SmokeHeight / Mathf.Max(1f, _settings.SmokeLifetime);
            main.startSize = _settings.SmokeParticleSize;
            main.startColor = new Color(_settings.BodyColor.r, _settings.BodyColor.g, _settings.BodyColor.b, 0.72f);
            main.maxParticles = _settings.SmokeMaximumParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = _settings.SmokeMaximumParticles / Mathf.Max(1f, _settings.SmokeLifetime);
            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = _settings.BodyScale.x;
            shape.angle = _settings.WingSweepDegrees;
            ParticleSystemRenderer renderer = smoke.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _wakeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            smoke.Play();
        }

        private void HandleJointDestroyed(int jointIndex)
        {
            if (jointIndex < 0 || jointIndex >= _jointTargets.Length || _jointTargets[jointIndex] == null)
            {
                return;
            }

            _jointTargets[jointIndex] = null;
            _destroyedWings[jointIndex] = true;
            if (_destroyedJoints == 0)
            {
                _firstDestroyedSide = jointIndex < 2 ? -1f : 1f;
            }
            _destroyedJoints++;
            if (_jointLights[jointIndex] != null)
            {
                _jointLights[jointIndex].enabled = false;
            }

            if (_destroyedJoints >= 4)
            {
                SetJointTargetability(false);
                SetState(GlassKiteState.Crashing);
            }
        }

        private void SetJointTargetability(bool targetable)
        {
            for (int i = 0; i < _jointTargets.Length; i++)
            {
                if (_jointTargets[i] != null)
                {
                    _jointTargets[i].SetTargetable(targetable);
                }
            }
        }

        private void UpdatePresentation(float deltaTime)
        {
            if (_visual == null || CurrentState == GlassKiteState.Wreck)
            {
                return;
            }

            float targetAttackLight = CurrentState == GlassKiteState.Telegraph ? 1f : 0f;
            _attackLight = Mathf.MoveTowards(
                _attackLight,
                targetAttackLight,
                deltaTime / Mathf.Max(0.01f, _settings.TelegraphDuration));
            int illuminatedSeams = Mathf.CeilToInt(_attackSeams.Count * _attackLight);
            for (int i = 0; i < _attackSeams.Count; i++)
            {
                if (_attackSeams[i] != null)
                {
                    _attackSeams[i].enabled = i < illuminatedSeams;
                }
            }

            bool exposeJoints = CurrentState == GlassKiteState.Recovery;
            for (int i = 0; i < _wingJoints.Length; i++)
            {
                if (_wingJoints[i] == null)
                {
                    continue;
                }
                float flutter = _destroyedWings[i]
                    ? Mathf.Sin((Time.time * _settings.DamagedWingFlutterSpeed) + i) * _settings.DamagedWingFlutterDegrees
                    : 0f;
                float attackFold = CurrentState == GlassKiteState.Telegraph
                    ? _attackLight * (i % 2 == 0 ? _settings.WingSweepDegrees : -_settings.WingSweepDegrees)
                    : 0f;
                float destroyedFold = _destroyedWings[i]
                    ? _settings.DestroyedWingFoldDegrees * (i < 2 ? -1f : 1f)
                    : 0f;
                _wingJoints[i].localRotation = Quaternion.Euler(
                    flutter,
                    attackFold,
                    destroyedFold + (flutter * 0.35f));
                if (_jointLights[i] != null)
                {
                    _jointLights[i].transform.localScale = Vector3.one
                        * _settings.JointVisualScale
                        * (exposeJoints ? _settings.JointExposedScale : 1f);
                }
            }

            if (CurrentState != GlassKiteState.Crashing)
            {
                float damageRoll = _destroyedJoints >= 1 ? _settings.FirstJointRollDegrees : 0f;
                float bankProgress = CurrentState == GlassKiteState.Telegraph
                    ? Mathf.Sin(Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _settings.TelegraphDuration)) * Mathf.PI)
                    : 0f;
                float roll = damageRoll + (_settings.MaximumBankAngle * bankProgress);
                Quaternion targetRotation = Quaternion.LookRotation(_flightDirection, Vector3.up)
                    * Quaternion.Euler(0f, 0f, roll);
                _cachedTransform.rotation = Quaternion.RotateTowards(
                    _cachedTransform.rotation,
                    targetRotation,
                    GetCurrentTurnRate() * deltaTime);
            }
        }

        private void MoveAtSpeed(float speed, float desiredAltitude, float deltaTime)
        {
            Vector3 position = _cachedTransform.position;
            position += _flightDirection.normalized
                * speed
                * DuneVectorContractRisk.EnemySpeedMultiplier
                * deltaTime;
            float altitudeRate = _destroyedJoints >= 3 ? _settings.ThirdJointDescentRate : speed;
            position.y = Mathf.MoveTowards(position.y, desiredAltitude, altitudeRate * deltaTime);
            _cachedTransform.position = position;
        }

        private float GetDesiredPatrolAltitude()
        {
            float terrainHeight = _world.SampleHeightAtLocal(_cachedTransform.position.x, _cachedTransform.position.z);
            float variation = Mathf.Sin(_patrolPhase) * _settings.PatrolAltitudeVariation;
            float desired = terrainHeight + _settings.PatrolAltitude + variation;
            if (_destroyedJoints >= 3)
            {
                desired -= _altitudeLoss;
            }
            return desired;
        }

        private float GetCurrentAttackSpeed()
        {
            return _destroyedJoints >= 2 ? _settings.DamagedAttackSpeed : _settings.AttackSpeed;
        }

        private float GetCurrentTurnRate()
        {
            return _destroyedJoints >= 2 ? _settings.DamagedTurnRate : _settings.TurnRate;
        }

        private void RotateFlightDirection(Vector3 desired, float degreesPerSecond, float deltaTime)
        {
            if (desired.sqrMagnitude <= 0.001f)
            {
                return;
            }
            _flightDirection = Vector3.RotateTowards(
                _flightDirection.normalized,
                desired.normalized,
                degreesPerSecond * Mathf.Deg2Rad * deltaTime,
                0f).normalized;
        }

        private void RepositionNearPlayer(Vector3 playerPosition)
        {
            float angle = Mathf.Repeat((_patrolPhase * Mathf.Rad2Deg) + (Time.time * _settings.TurnRate), 360f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward
                * Mathf.Lerp(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance, 0.5f);
            Vector3 position = playerPosition + offset;
            position.y = _world.SampleHeightAtLocal(position.x, position.z)
                + _settings.PatrolAltitude
                + (Mathf.Sin(_patrolPhase) * _settings.PatrolAltitudeVariation);
            _cachedTransform.position = position;
            _flightDirection = Vector3.ProjectOnPlane(playerPosition - position, Vector3.up).normalized;
            _attackCooldown = _settings.AttackCooldown;
        }

        private void SetState(GlassKiteState state)
        {
            CurrentState = state;
            _stateTime = 0f;
        }

        private void CreateMaterials()
        {
            _bodyMaterial = CreateRuntimeMaterial(_materials.DroneDark, "Glass Kite Body", _settings.BodyColor, Color.black);
            Color emittedJoint = _settings.JointLightColor * _settings.AttackEmissionIntensity;
            _lightMaterial = CreateRuntimeMaterial(
                _materials.LightningWarning,
                "Glass Kite Joint Light",
                _settings.JointLightColor,
                emittedJoint);
            _seamMaterial = CreateRuntimeMaterial(
                _materials.LightningWarning,
                "Glass Kite Attack Seams",
                _settings.AttackSeamColor,
                _settings.AttackSeamColor * _settings.AttackEmissionIntensity);
            _wakeMaterial = CreateRuntimeMaterial(
                _materials.LightningWarning,
                "Glass Kite Wake",
                _settings.WakeColor,
                _settings.WakeColor);
        }

        private void BuildVisual()
        {
            GameObject visualObject = new GameObject("Glass Kite Visual");
            _visual = visualObject.transform;
            _visual.SetParent(_cachedTransform, false);

            CreatePart("Central Body", _visual, Vector3.zero, _settings.BodyScale, Quaternion.identity, _bodyMaterial);
            CreatePart(
                "Forward Keel",
                _visual,
                Vector3.forward * (_settings.BodyScale.z * 0.55f),
                new Vector3(_settings.BodyScale.x * 0.55f, _settings.BodyScale.y, _settings.BodyScale.z * 0.36f),
                Quaternion.Euler(0f, 45f, 0f),
                _bodyMaterial);

            float halfSpanPerWing = (_settings.WingSpan - _settings.BodyScale.x) * 0.5f;
            int segments = Mathf.Max(1, _settings.WingSegmentsPerJoint);
            float segmentLength = halfSpanPerWing / segments;
            Renderer[,] orderedSeams = new Renderer[segments, 4];
            for (int i = 0; i < 4; i++)
            {
                float side = i < 2 ? -1f : 1f;
                float fore = i % 2 == 0 ? -1f : 1f;
                Vector3 jointPosition = new Vector3(
                    side * _settings.BodyScale.x * 0.48f,
                    0f,
                    fore * _settings.BodyScale.z * 0.23f);

                GameObject jointObject = new GameObject($"Wing Joint Visual {i + 1}");
                Transform joint = jointObject.transform;
                joint.SetParent(_visual, false);
                joint.localPosition = jointPosition;
                joint.localRotation = Quaternion.Euler(0f, -fore * side * _settings.WingSweepDegrees, 0f);
                _wingJoints[i] = joint;

                Transform light = CreatePart(
                    $"Warm Joint Light {i + 1}",
                    joint,
                    Vector3.zero,
                    Vector3.one * _settings.JointVisualScale,
                    Quaternion.identity,
                    _lightMaterial,
                    PrimitiveType.Sphere);
                _jointLights[i] = light.GetComponent<Renderer>();

                for (int segment = 0; segment < segments; segment++)
                {
                    float taper = 1f - (segment / (float)(segments + 1));
                    Vector3 localPosition = new Vector3(side * segmentLength * (segment + 0.5f), 0f, 0f);
                    Transform wing = CreatePart(
                        $"Wing {i + 1} Section {segment + 1}",
                        joint,
                        localPosition,
                        new Vector3(segmentLength * 1.04f, _settings.WingThickness, _settings.WingChord * taper),
                        Quaternion.identity,
                        _bodyMaterial);
                    Transform seam = CreatePart(
                        $"Attack Seam {i + 1}-{segment + 1}",
                        wing,
                        Vector3.down * ((_settings.WingThickness + _settings.SeamThickness) * 0.5f),
                        new Vector3(segmentLength * 0.84f, _settings.SeamThickness, _settings.SeamWidth),
                        Quaternion.identity,
                        _seamMaterial);
                    Renderer seamRenderer = seam.GetComponent<Renderer>();
                    seamRenderer.enabled = false;
                    orderedSeams[segment, i] = seamRenderer;
                }

                GameObject targetObject = new GameObject($"Glass Kite Wing Joint Target {i + 1}");
                targetObject.transform.SetParent(_visual, false);
                targetObject.transform.localPosition = jointPosition;
                EnemyHealth health = targetObject.AddComponent<EnemyHealth>();
                health.Initialize(_settings.JointMaximumHealth);
                EnemyCombatTarget target = targetObject.AddComponent<EnemyCombatTarget>();
                target.Initialize(health, _settings.JointTargetRadius);
                target.SetTargetable(false);
                int capturedIndex = i;
                health.Died += () => HandleJointDestroyed(capturedIndex);
                _jointTargets[i] = target;
            }

            for (int segment = 0; segment < segments; segment++)
            {
                for (int wing = 0; wing < 4; wing++)
                {
                    _attackSeams.Add(orderedSeams[segment, wing]);
                }
            }
        }

        private static Transform CreatePart(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            PrimitiveType primitive = PrimitiveType.Cube)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return part.transform;
        }

        private static Material CreateRuntimeMaterial(
            Material source,
            string name,
            Color baseColor,
            Color emission)
        {
            Material material = new Material(source) { name = name };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", baseColor);
            }
            if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", emission);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
            }
            return material;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            if (_cachedTransform != null)
            {
                _cachedTransform.position += shift;
            }
            _committedCrossingPoint += shift;
        }

        private void OnDestroy()
        {
            if (_bodyMaterial != null)
            {
                Destroy(_bodyMaterial);
            }
            if (_lightMaterial != null)
            {
                Destroy(_lightMaterial);
            }
            if (_seamMaterial != null)
            {
                Destroy(_seamMaterial);
            }
            if (_wakeMaterial != null)
            {
                Destroy(_wakeMaterial);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class GlassKiteRazorWake : MonoBehaviour
    {
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private GlassKiteTuning _settings;
        private Vector3 _center;
        private Vector3 _forward;
        private Vector3 _right;
        private Vector3 _up;
        private LineRenderer[] _sheets;
        private LineRenderer[] _vortices;
        private float _age;
        private float _asymmetrySide;
        private bool _consumed;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            GlassKiteTuning settings,
            Vector3 center,
            Vector3 forward,
            Vector3 up,
            float asymmetrySide,
            Material material)
        {
            _player = player;
            _playerHealth = playerHealth;
            _settings = settings;
            _center = center;
            _forward = forward.normalized;
            _right = Vector3.Cross(up, _forward).normalized;
            _up = Vector3.Cross(_forward, _right).normalized;
            _asymmetrySide = asymmetrySide;
            transform.position = center;

            int count = Mathf.Max(2, settings.WakeSheetCount);
            _sheets = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                _sheets[i] = CreateLine($"Turbulent Wake Sheet {i + 1}", material, settings.WakeVisualSegments);
            }
            _vortices = new[]
            {
                CreateLine("Left Wingtip Vortex", material, settings.WakeVisualSegments),
                CreateLine("Right Wingtip Vortex", material, settings.WakeVisualSegments),
            };
            CreateDebrisParticles(material);
            UpdateLines();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _settings.WakeLifetime)
            {
                Destroy(gameObject);
                return;
            }
            UpdateLines();
            TryAffectPlayer();
        }

        private void UpdateLines()
        {
            float life01 = Mathf.Clamp01(_age / Mathf.Max(0.01f, _settings.WakeLifetime));
            float opacity = _settings.WakeStartOpacity * (1f - life01);
            Color color = _settings.WakeColor;
            color.a = opacity;

            int count = _sheets.Length;
            for (int sheet = 0; sheet < count; sheet++)
            {
                LineRenderer line = _sheets[sheet];
                float centeredSheet = sheet - ((count - 1) * 0.5f);
                float vertical = centeredSheet * _settings.WakeSheetVerticalSpacing;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float across01 = i / (float)(line.positionCount - 1);
                    float across = Mathf.Lerp(-_settings.WakeHalfWidth, _settings.WakeHalfWidth, across01);
                    if (_asymmetrySide != 0f && Mathf.Sign(across) == _asymmetrySide)
                    {
                        across *= 1f - _settings.FirstJointWakeAsymmetry;
                    }
                    float turbulence = Mathf.Sin(
                        (across * _settings.WakeTurbulenceFrequency)
                        + (_age * _settings.WakeParticleSpeed)
                        + sheet) * _settings.WakeTurbulenceAmplitude;
                    line.SetPosition(i, _center + (_right * across) + (_up * (vertical + turbulence)));
                }
                line.startColor = color;
                line.endColor = color;
            }

            for (int sideIndex = 0; sideIndex < _vortices.Length; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                float sideStrength = _asymmetrySide != 0f && side == _asymmetrySide
                    ? 1f - _settings.FirstJointWakeAsymmetry
                    : 1f;
                LineRenderer line = _vortices[sideIndex];
                for (int i = 0; i < line.positionCount; i++)
                {
                    float trail01 = i / (float)(line.positionCount - 1);
                    float inset = (_settings.WakeVortexInsetSpeed * _age) + (_settings.WakeHalfWidth * 0.2f * trail01);
                    float curl = Mathf.Sin(trail01 * Mathf.PI)
                        * Mathf.Tan(_settings.WakeCurlDegrees * Mathf.Deg2Rad)
                        * _settings.WakeHalfHeight;
                    Vector3 point = _center
                        + (_right * side * (_settings.WakeHalfWidth - inset) * sideStrength)
                        - (_forward * trail01 * _settings.WakeHalfWidth)
                        + (_up * curl * side * sideStrength);
                    line.SetPosition(i, point);
                }
                line.startColor = color;
                line.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private void TryAffectPlayer()
        {
            if (_consumed || _player == null || _playerHealth == null || _playerHealth.IsDead)
            {
                return;
            }

            Vector3 relative = _player.WorldCenter - _center;
            float forwardDistance = Mathf.Abs(Vector3.Dot(relative, _forward));
            float across = Vector3.Dot(relative, _right);
            float vertical = Vector3.Dot(relative, _up);
            float halfWidth = _settings.WakeHalfWidth;
            if (_asymmetrySide != 0f && Mathf.Sign(across) == _asymmetrySide)
            {
                halfWidth *= 1f - _settings.FirstJointWakeAsymmetry;
            }
            if (forwardDistance > _settings.WakeCollisionThickness
                || Mathf.Abs(across) > halfWidth
                || Mathf.Abs(vertical) > _settings.WakeHalfHeight)
            {
                return;
            }

            _consumed = true;
            _playerHealth.TakeDamage(
                _settings.WakeDamage * DuneVectorContractRisk.EnemyDamageMultiplier,
                "Glass Kite razor wake",
                _settings.WakeDeathMessage);
            float side = across >= 0f ? 1f : -1f;
            Vector3 disruption = (_right * side * _settings.WakeSideImpulse)
                - (_forward * _settings.WakeDirectionDisruption);
            _player.ApplyExternalImpulse(disruption);
        }

        private LineRenderer CreateLine(string name, Material material, int segments)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.positionCount = Mathf.Max(2, segments);
            line.startWidth = _settings.WakeLineWidth;
            line.endWidth = _settings.WakeLineWidth;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            return line;
        }

        private void CreateDebrisParticles(Material material)
        {
            if (_settings.WakeParticleCount <= 0)
            {
                return;
            }

            GameObject particlesObject = new GameObject("Sheared Atmospheric Debris");
            particlesObject.transform.SetParent(transform, false);
            particlesObject.transform.rotation = Quaternion.LookRotation(_forward, _up);
            ParticleSystem particles = particlesObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.startLifetime = _settings.WakeLifetime;
            main.startSpeed = _settings.WakeParticleSpeed;
            main.startSize = _settings.WakeParticleSize;
            main.startColor = _settings.WakeParticleColor;
            main.maxParticles = _settings.WakeParticleCount;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                _settings.WakeHalfWidth * 2f,
                _settings.WakeHalfHeight * 2f,
                _settings.WakeCollisionThickness);
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            particles.Emit(_settings.WakeParticleCount);
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _center += shift;
            transform.position += shift;
        }
    }

    [DisallowMultipleComponent]
    public sealed class GlassKiteCrashLandmark : MonoBehaviour
    {
        private DroneCharacterController _player;
        private GlassKiteTuning _settings;
        private float _age;
        private bool _salvaged;

        public bool IsSalvaged => _salvaged;

        public void Initialize(DroneCharacterController player, GlassKiteTuning settings)
        {
            _player = player;
            _settings = settings;
            gameObject.name = "Glass Kite Crash Landmark";
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _settings.WreckLifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (_salvaged || _player == null)
            {
                return;
            }

            float radius = _settings.WreckDiscoveryRadius;
            if ((_player.WorldCenter - transform.position).sqrMagnitude > radius * radius)
            {
                return;
            }

            _salvaged = true;
            DroneGoldWallet wallet = _player.GetComponent<DroneGoldWallet>();
            wallet?.AddGold(_settings.WreckGoldReward);
            Debug.Log(
                $"Glass Kite wreck salvaged: {_settings.WreckGoldReward} gold and rare Kite materials recovered.",
                this);
        }
    }

    [DefaultExecutionOrder(1375)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorGlassKiteDirector : MonoBehaviour
    {
        private readonly List<GlassKiteEnemy> _enemies = new List<GlassKiteEnemy>();
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private GlassKiteTuning _settings;

        public void Initialize(
            DroneCharacterController player,
            DroneHealth playerHealth,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            GlassKiteTuning settings)
        {
            _player = player;
            _playerHealth = playerHealth;
            _world = world;
            _materials = materials;
            _settings = settings;
            _world.WorldShifted += HandleWorldShift;
            SpawnEnemies();
        }

        private void SpawnEnemies()
        {
            System.Random random = new System.Random(unchecked(_world.EnemySpawnSeed ^ 0x6b41f25));
            int count = Mathf.Max(1, _settings.EnemyCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                float distance = Mathf.Lerp(
                    _settings.MinimumSpawnDistance,
                    Mathf.Max(_settings.MinimumSpawnDistance, _settings.MaximumSpawnDistance),
                    (float)random.NextDouble());
                Vector3 playerPosition = _player.WorldCenter;
                Vector3 spawnPosition = playerPosition + new Vector3(
                    Mathf.Cos(angle) * distance,
                    0f,
                    Mathf.Sin(angle) * distance);
                float variation = Mathf.Lerp(
                    -_settings.PatrolAltitudeVariation,
                    _settings.PatrolAltitudeVariation,
                    (float)random.NextDouble());
                spawnPosition.y = _world.SampleHeightAtLocal(spawnPosition.x, spawnPosition.z)
                    + _settings.PatrolAltitude
                    + variation;

                GameObject enemyObject = new GameObject($"Glass Kite {i + 1:00}");
                enemyObject.transform.SetParent(transform, true);
                enemyObject.transform.position = spawnPosition;
                GlassKiteEnemy enemy = enemyObject.AddComponent<GlassKiteEnemy>();
                enemy.Initialize(_player, _playerHealth, _world, _materials, _settings, i + 1);
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
            GlassKiteRazorWake[] wakes = GetComponentsInChildren<GlassKiteRazorWake>(true);
            for (int i = 0; i < wakes.Length; i++)
            {
                wakes[i].enabled = active;
            }
            GlassKiteCrashLandmark[] wrecks = GetComponentsInChildren<GlassKiteCrashLandmark>(true);
            for (int i = 0; i < wrecks.Length; i++)
            {
                wrecks[i].enabled = active;
            }
        }

        private void HandleWorldShift(Vector3 shift)
        {
            GlassKiteRazorWake[] wakes = GetComponentsInChildren<GlassKiteRazorWake>(true);
            for (int i = 0; i < wakes.Length; i++)
            {
                wakes[i].ApplyWorldShift(shift);
            }
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
