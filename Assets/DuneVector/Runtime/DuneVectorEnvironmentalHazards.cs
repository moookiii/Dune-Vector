using System;
using UnityEngine;

namespace DuneVector
{
    public enum ElectricalStrikePhase
    {
        Idle,
        Buildup,
        TargetTelegraph,
    }

    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorEnvironmentalHazardSystem : MonoBehaviour
    {
        public ElectricalStrikePhase StrikePhase { get; private set; }
        public float StrikePhaseProgress { get; private set; }
        public Vector3 LightningTarget { get; private set; }
        public bool LightningTargetsAir { get; private set; }
        public bool IsElectricalInterferenceActive { get; private set; }
        public float HeatZoneIntensity { get; private set; }
        public float CurrentTemperature { get; private set; }
        public float NormalizedTemperature => _heatSettings == null
            ? 0f
            : Mathf.Clamp01(CurrentTemperature / Mathf.Max(1f, _heatSettings.MaximumTemperature));
        public float HeightAboveTerrain { get; private set; }
        public float AltitudeCoolingMultiplier { get; private set; } = 1f;

        public event Action<ElectricalStrikePhase> StrikePhaseChanged;
        public event Action<Vector3, bool> LightningTargetLocked;
        public event Action<Vector3, bool> LightningStruck;
        public event Action<float> TemperatureChanged;

        private DroneCharacterController _drone;
        private DroneHealth _health;
        private DroneStaminaSystem _stamina;
        private DroneEnergyLauncher _launcher;
        private DesertWorldStreamer _world;
        private DuneVectorWeatherController _weather;
        private DuneVectorCourierGame _courierGame;
        private ElectricalSandstormTuning _electricalSettings;
        private HeatZoneTuning _heatSettings;
        private System.Random _random;
        private float _phaseTimer;
        private float _phaseDuration;
        private float _strikeDelay;

        public void Initialize(
            DroneCharacterController drone,
            DroneHealth health,
            DroneStaminaSystem stamina,
            DroneEnergyLauncher launcher,
            DesertWorldStreamer world,
            DuneVectorWeatherController weather,
            DuneVectorCourierGame courierGame,
            EnvironmentalHazardTuning settings)
        {
            settings.EnsureInitialized();
            _drone = drone;
            _health = health;
            _stamina = stamina;
            _launcher = launcher;
            _world = world;
            _weather = weather;
            _courierGame = courierGame;
            _electricalSettings = settings.ElectricalSandstorms;
            _heatSettings = settings.HeatZones;
            _random = new System.Random(unchecked(world.WorldSeed ^ _electricalSettings.RandomSeedOffset));
            _strikeDelay = Mathf.Max(0f, _electricalSettings.InitialStrikeDelay);
            if (_launcher != null)
            {
                _launcher.Fired += HandleWeaponFired;
            }
            _world.WorldShifted += HandleWorldShift;
        }

        private void Update()
        {
            if (_drone == null || _world == null)
            {
                return;
            }

            UpdateHeat(Time.deltaTime);
            UpdateElectricalStorm(Time.deltaTime);
            ApplyMechanicalConsequences();
        }

        private void UpdateElectricalStorm(float deltaTime)
        {
            bool stormActive = _electricalSettings.Enabled && _weather != null &&
                _weather.CurrentStormIntensity >= _electricalSettings.MinimumStormIntensity;
            IsElectricalInterferenceActive = stormActive;
            if (!stormActive)
            {
                SetStrikePhase(ElectricalStrikePhase.Idle, 0f);
                _strikeDelay = Mathf.Max(0f, _electricalSettings.InitialStrikeDelay);
                return;
            }

            if (StrikePhase == ElectricalStrikePhase.Idle)
            {
                _strikeDelay -= deltaTime;
                if (_strikeDelay <= 0f)
                {
                    SetStrikePhase(ElectricalStrikePhase.Buildup, _electricalSettings.ElectricalBuildupDuration);
                }
                return;
            }

            _phaseTimer += deltaTime;
            StrikePhaseProgress = Mathf.Clamp01(_phaseTimer / Mathf.Max(0.01f, _phaseDuration));
            if (_phaseTimer < _phaseDuration)
            {
                return;
            }

            if (StrikePhase == ElectricalStrikePhase.Buildup)
            {
                LockLightningTarget();
                SetStrikePhase(ElectricalStrikePhase.TargetTelegraph, _electricalSettings.TargetTelegraphDuration);
                LightningTargetLocked?.Invoke(LightningTarget, LightningTargetsAir);
                return;
            }

            ResolveLightningStrike();
            SetStrikePhase(ElectricalStrikePhase.Idle, 0f);
            _strikeDelay = NextStrikeInterval();
        }

        private void LockLightningTarget()
        {
            Vector3 prediction = _drone.Motor.BaseVelocity * Mathf.Max(0f, _electricalSettings.TargetPredictionTime);
            prediction = Vector3.ClampMagnitude(prediction, Mathf.Max(0f, _electricalSettings.MaximumPredictionDistance));
            LightningTarget = _drone.WorldCenter + prediction;
            float terrainHeight = _world.SampleHeightAtLocal(LightningTarget.x, LightningTarget.z);
            LightningTargetsAir = LightningTarget.y - terrainHeight >= _electricalSettings.AirTargetMinimumHeight;
            if (!LightningTargetsAir)
            {
                LightningTarget = new Vector3(LightningTarget.x, terrainHeight, LightningTarget.z);
            }
        }

        private void ResolveLightningStrike()
        {
            bool hit = Vector3.Distance(_drone.WorldCenter, LightningTarget) <= _electricalSettings.StrikeRadius;
            if (hit && _health != null)
            {
                float damage = _electricalSettings.StrikeDamage;
                if (_courierGame != null && _courierGame.ActiveContract != null &&
                    _courierGame.ActiveContract.Has(CourierContractModifier.Hazardous))
                {
                    damage *= Mathf.Max(1f, _electricalSettings.HazardousCargoDamageMultiplier);
                }
                _health.TakeDamage(damage, "Electrical sandstorm lightning");
            }
            LightningStruck?.Invoke(LightningTarget, hit);
        }

        private float NextStrikeInterval()
        {
            float minimum = Mathf.Max(0.1f, _electricalSettings.MinimumStrikeInterval);
            float maximum = Mathf.Max(minimum, _electricalSettings.MaximumStrikeInterval);
            float interval = Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
            if (_courierGame != null && _courierGame.ActiveContract != null &&
                _courierGame.ActiveContract.Has(CourierContractModifier.HighValue))
            {
                interval *= Mathf.Clamp(_electricalSettings.HighValueStrikeIntervalMultiplier, 0.1f, 1f);
            }
            return interval;
        }

        private void UpdateHeat(float deltaTime)
        {
            if (!_heatSettings.Enabled)
            {
                HeatZoneIntensity = 0f;
                CurrentTemperature = 0f;
                return;
            }

            HeatZoneIntensity = SampleHeatZone(_world.LogicalPlayerPosition);
            float terrainHeight = _world.SampleHeightAtLocal(_drone.WorldCenter.x, _drone.WorldCenter.z);
            HeightAboveTerrain = Mathf.Max(0f, _drone.WorldCenter.y - terrainHeight);
            float altitudeRange = Mathf.Max(0.01f, _heatSettings.CoolingAltitudeFull - _heatSettings.CoolingAltitudeStart);
            float altitudeBlend = Mathf.Clamp01((HeightAboveTerrain - _heatSettings.CoolingAltitudeStart) / altitudeRange);
            AltitudeCoolingMultiplier = Mathf.Lerp(1f, Mathf.Max(1f, _heatSettings.HighAltitudeCoolingMultiplier), altitudeBlend);

            float heatGain = HeatZoneIntensity * Mathf.Max(0f, _heatSettings.ZoneHeatPerSecond);
            if (_stamina != null && _stamina.IsBoosting)
            {
                heatGain += Mathf.Max(0f, _heatSettings.BoostHeatPerSecond) *
                    Mathf.Lerp(1f, Mathf.Max(1f, _heatSettings.HotZoneBoostHeatMultiplier), HeatZoneIntensity);
            }
            float cooling = Mathf.Max(0f, _heatSettings.PassiveCoolingPerSecond) * AltitudeCoolingMultiplier;
            float previous = CurrentTemperature;
            CurrentTemperature = Mathf.Clamp(
                CurrentTemperature + ((heatGain - cooling) * Mathf.Max(0f, deltaTime)),
                0f,
                Mathf.Max(1f, _heatSettings.MaximumTemperature));
            if (!Mathf.Approximately(previous, CurrentTemperature))
            {
                TemperatureChanged?.Invoke(CurrentTemperature);
            }
        }

        private float SampleHeatZone(LogicalPosition position)
        {
            float cellSize = Mathf.Max(20f, _heatSettings.ZoneCellSize);
            int centerX = Mathf.FloorToInt((float)(position.X / cellSize));
            int centerZ = Mathf.FloorToInt((float)(position.Z / cellSize));
            int searchRadius = Mathf.CeilToInt(Mathf.Max(1f, _heatSettings.MaximumZoneRadius) / cellSize) + 1;
            float strongest = 0f;
            for (int z = centerZ - searchRadius; z <= centerZ + searchRadius; z++)
            {
                for (int x = centerX - searchRadius; x <= centerX + searchRadius; x++)
                {
                    if (DuneVectorMath.Hash01(x, z, _world.WorldSeed, _heatSettings.RandomSeedOffset) > _heatSettings.ZoneChance)
                    {
                        continue;
                    }
                    double centerLogicalX = (x + DuneVectorMath.Hash01(x, z, _world.WorldSeed, _heatSettings.RandomSeedOffset + 1)) * cellSize;
                    double centerLogicalZ = (z + DuneVectorMath.Hash01(x, z, _world.WorldSeed, _heatSettings.RandomSeedOffset + 2)) * cellSize;
                    float radius = DuneVectorMath.HashRange(
                        x, z, _world.WorldSeed, _heatSettings.RandomSeedOffset + 3,
                        Mathf.Max(1f, _heatSettings.MinimumZoneRadius),
                        Mathf.Max(_heatSettings.MinimumZoneRadius, _heatSettings.MaximumZoneRadius));
                    float distance = (float)Math.Sqrt(
                        ((position.X - centerLogicalX) * (position.X - centerLogicalX)) +
                        ((position.Z - centerLogicalZ) * (position.Z - centerLogicalZ)));
                    float edgeStart = radius * (1f - Mathf.Clamp01(_heatSettings.ZoneEdgeFalloff));
                    float influence = 1f - Mathf.InverseLerp(edgeStart, radius, distance);
                    strongest = Mathf.Max(strongest, influence);
                }
            }
            return strongest;
        }

        private void HandleWeaponFired()
        {
            if (_heatSettings == null || !_heatSettings.Enabled)
            {
                return;
            }
            float heat = Mathf.Max(0f, _heatSettings.WeaponHeatPerShot) *
                Mathf.Lerp(1f, Mathf.Max(1f, _heatSettings.HotZoneWeaponHeatMultiplier), HeatZoneIntensity);
            CurrentTemperature = Mathf.Min(Mathf.Max(1f, _heatSettings.MaximumTemperature), CurrentTemperature + heat);
            TemperatureChanged?.Invoke(CurrentTemperature);
        }

        private void ApplyMechanicalConsequences()
        {
            float threshold = Mathf.Clamp01(_heatSettings.ConsequenceTemperatureThreshold);
            float consequence = Mathf.InverseLerp(threshold, 1f, NormalizedTemperature);
            _stamina?.SetEnvironmentalDrainMultiplier(
                Mathf.Lerp(1f, Mathf.Max(1f, _heatSettings.MaximumBoostDrainMultiplier), consequence));
            float heatWeaponMultiplier = Mathf.Lerp(
                1f, Mathf.Max(1f, _heatSettings.MaximumWeaponCooldownMultiplier), consequence);
            float interferenceMultiplier = IsElectricalInterferenceActive
                ? Mathf.Max(1f, _electricalSettings.WeaponCooldownMultiplier)
                : 1f;
            _launcher?.SetEnvironmentalCooldownMultiplier(heatWeaponMultiplier * interferenceMultiplier);
        }

        private void SetStrikePhase(ElectricalStrikePhase phase, float duration)
        {
            if (StrikePhase == phase && (phase != ElectricalStrikePhase.Idle || _phaseTimer == 0f))
            {
                return;
            }
            StrikePhase = phase;
            StrikePhaseProgress = 0f;
            _phaseTimer = 0f;
            _phaseDuration = Mathf.Max(0f, duration);
            StrikePhaseChanged?.Invoke(phase);
        }

        private void HandleWorldShift(Vector3 shift)
        {
            LightningTarget += shift;
        }

        private void OnDestroy()
        {
            if (_launcher != null)
            {
                _launcher.Fired -= HandleWeaponFired;
            }
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
