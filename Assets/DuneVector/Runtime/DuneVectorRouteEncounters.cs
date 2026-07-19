using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        private readonly List<DuneVectorFlyThroughGuide> _guides = new List<DuneVectorFlyThroughGuide>();

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
        private float _waveAnnouncementUntil;
        private string _waveAnnouncement;
        private GUIStyle _waveStyle;
        private Material _formationMaterial;
        private Material _shotMaterial;

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
            _formationMaterial = CreateEmissionMaterial(materials.EnemyCore, "Courier Formation Emission", settings.FormationEmission);
            _shotMaterial = CreateEmissionMaterial(materials.EnemyCore, "Courier Shot Emission", settings.ShotEmission);
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
            for (int i = 0; i < _guides.Count; i++)
            {
                if (_guides[i] != null)
                {
                    Destroy(_guides[i].gameObject);
                }
            }
            _guides.Clear();
            _waveAnnouncementUntil = 0f;
        }

        private void Update()
        {
            if (_contract == null || _courierGame == null || !_courierGame.IsCarryingCargo ||
                _health == null || _health.IsDead || _settings == null || !_settings.Enabled)
            {
                return;
            }
            PruneEnemies();
            PruneGuides();
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
            _waveAnnouncement = FormationTitle(formation);
            _waveAnnouncementUntil = Time.unscaledTime + _settings.WaveAnnouncementDuration;
            if (formation == RouteFormationType.FlyThroughAssault)
            {
                SpawnFlyThroughGuide(playerPosition, travelForward);
            }
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
                    _formationMaterial,
                    _shotMaterial,
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

        private void PruneGuides()
        {
            for (int i = _guides.Count - 1; i >= 0; i--)
            {
                if (_guides[i] == null)
                {
                    _guides.RemoveAt(i);
                }
            }
        }

        private void SpawnFlyThroughGuide(Vector3 playerPosition, Vector3 travelForward)
        {
            GameObject guideObject = new GameObject("Optional Fly-Through Assault Vector");
            guideObject.transform.SetParent(transform, true);
            DuneVectorFlyThroughGuide guide = guideObject.AddComponent<DuneVectorFlyThroughGuide>();
            guide.Initialize(playerPosition, travelForward, _formationMaterial, _settings);
            _guides.Add(guide);
        }

        private void HandleWorldShift(Vector3 shift)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i]?.ApplyWorldShift(shift);
            }
            for (int i = 0; i < _guides.Count; i++)
            {
                _guides[i]?.ApplyWorldShift(shift);
            }
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (Time.unscaledTime >= _waveAnnouncementUntil || string.IsNullOrEmpty(_waveAnnouncement))
            {
                return;
            }
            if (_waveStyle == null)
            {
                _waveStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = _settings.WaveAnnouncementFontSize,
                    normal = { textColor = _settings.WaveAnnouncementColor },
                };
            }
            float remaining = _waveAnnouncementUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remaining / Mathf.Max(0.01f, _settings.WaveAnnouncementDuration));
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, alpha));
            GUI.Label(new Rect(0f, _settings.WaveAnnouncementTop, Screen.width, 34f), _waveAnnouncement, _waveStyle);
            GUI.color = previous;
        }

        private static string FormationTitle(RouteFormationType formation)
        {
            switch (formation)
            {
                case RouteFormationType.CrossAttack: return "RAIDER CROSS-ATTACK";
                case RouteFormationType.Pursuit: return "PURSUIT WAVE INBOUND";
                case RouteFormationType.VerticalAttack: return "VERTICAL FORMATION INBOUND";
                case RouteFormationType.FlyThroughAssault: return "OPTIONAL ATTACK VECTOR OPEN";
                default: return "HEAD-ON FORMATION INBOUND";
            }
        }

        private static Material CreateEmissionMaterial(Material source, string materialName, Color emission)
        {
            Material material = new Material(source) { name = materialName };
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", emission);
            if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            return material;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            if (_formationMaterial != null) Destroy(_formationMaterial);
            if (_shotMaterial != null) Destroy(_shotMaterial);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorFlyThroughGuide : MonoBehaviour
    {
        private readonly List<Transform> _gates = new List<Transform>();
        private RouteEncounterTuning _settings;
        private float _remaining;

        public void Initialize(Vector3 origin, Vector3 forward, Material material, RouteEncounterTuning settings)
        {
            _settings = settings;
            _remaining = settings.FlyThroughGuideDuration;
            Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (horizontalForward.sqrMagnitude < 0.001f) horizontalForward = Vector3.forward;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(horizontalForward, Vector3.up);
            int gateCount = Mathf.Max(2, settings.FlyThroughGuideGateCount);
            for (int i = 0; i < gateCount; i++)
            {
                Transform gate = new GameObject($"Optional Vector Gate {i + 1}").transform;
                gate.SetParent(transform, false);
                gate.localPosition = Vector3.forward * ((i + 1) * settings.FlyThroughGuideGateSpacing);
                BuildGate(gate, material, settings.FlyThroughGuideGateRadius, settings.FlyThroughGuideGateThickness);
                _gates.Add(gate);
            }
        }

        private static void BuildGate(Transform gate, Material material, float radius, float thickness)
        {
            int sideCount = 8;
            for (int i = 0; i < sideCount; i++)
            {
                float angle = (360f / sideCount) * i;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * radius;
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Guide Segment {i + 1}";
                segment.transform.SetParent(gate, false);
                segment.transform.localPosition = position;
                segment.transform.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
                segment.transform.localScale = new Vector3(radius * 0.72f, thickness, thickness);
                Renderer renderer = segment.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Collider collider = segment.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            float lifeFade = Mathf.Clamp01(_remaining / Mathf.Max(0.01f, _settings.FlyThroughGuideDuration));
            for (int i = 0; i < _gates.Count; i++)
            {
                Transform gate = _gates[i];
                if (gate == null) continue;
                float phase = (Time.time * _settings.FlyThroughGuidePulseSpeed) + i;
                float pulse = 1f + (Mathf.Sin(phase) * _settings.FlyThroughGuidePulseAmount * lifeFade);
                gate.localScale = Vector3.one * pulse;
            }
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
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
            Material formationMaterial,
            Material shotMaterial,
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
            GameObject formationMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            formationMarker.name = "Formation Signal";
            formationMarker.transform.SetParent(transform, false);
            formationMarker.transform.localPosition = Vector3.up * (settings.EnemyVisualScale * 1.8f);
            formationMarker.transform.localScale = Vector3.one * (settings.EnemyVisualScale * 0.24f);
            Renderer markerRenderer = formationMarker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = formationMaterial;
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Collider markerCollider = formationMarker.GetComponent<Collider>();
            if (markerCollider != null) Destroy(markerCollider);
            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = formationMaterial;
            trail.time = settings.EnemyTrailDuration;
            trail.startWidth = settings.EnemyTrailStartWidth;
            trail.endWidth = settings.EnemyTrailEndWidth;
            trail.minVertexDistance = settings.EnemyTrailMinimumVertexDistance;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            EnemyHealth enemyHealth = gameObject.AddComponent<EnemyHealth>();
            enemyHealth.Initialize(settings.EnemyHealth);
            EnemyCombatTarget target = gameObject.AddComponent<EnemyCombatTarget>();
            target.Initialize(enemyHealth, settings.EnemyVisualScale);
            EnemyGoldReward goldReward = gameObject.AddComponent<EnemyGoldReward>();
            goldReward.Initialize(enemyHealth, wallet, settings.EnemyGoldReward);
            _shotLine = gameObject.AddComponent<LineRenderer>();
            _shotLine.sharedMaterial = shotMaterial;
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
                _shotLine.startWidth = _settings.ShotStartWidth;
                _shotLine.endWidth = _settings.ShotEndWidth;
                _shotLine.SetPosition(0, transform.position);
                _shotLine.SetPosition(1, _telegraphedPoint);
                return;
            }
            if (_shotTelegraphTimer > 0f)
            {
                _shotTelegraphTimer -= deltaTime;
                _shotLine.enabled = true;
                float pulse = Mathf.Lerp(
                    _settings.TelegraphMinimumWidthMultiplier,
                    1f,
                    Mathf.Abs(Mathf.Sin(Time.time * _settings.TelegraphPulseSpeed)));
                _shotLine.startWidth = _settings.ShotStartWidth * pulse;
                _shotLine.endWidth = _settings.ShotEndWidth * pulse;
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
