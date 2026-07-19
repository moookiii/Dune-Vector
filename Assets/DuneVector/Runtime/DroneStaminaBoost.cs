using UnityEngine;

namespace DuneVector
{
    public enum DroneStaminaState
    {
        Ready,
        Boosting,
        Regenerating,
        Exhausted,
    }

    [DisallowMultipleComponent]
    public sealed class DroneStaminaSystem : MonoBehaviour
    {
        public DroneStaminaState State { get; private set; } = DroneStaminaState.Ready;
        public float CurrentStamina { get; private set; }
        public float MaximumStamina { get; private set; }
        public float NormalizedStamina => MaximumStamina > 0f
            ? Mathf.Clamp01(CurrentStamina / MaximumStamina)
            : 0f;
        public bool IsBoosting => State == DroneStaminaState.Boosting;
        public bool IsExhausted => State == DroneStaminaState.Exhausted;

        private StaminaBoostTuning _settings;
        private float _regenDelayRemaining;
        private float _environmentalDrainMultiplier = 1f;

        public void Initialize(StaminaBoostTuning settings)
        {
            _settings = settings;
            MaximumStamina = settings != null ? Mathf.Max(0.01f, settings.MaxStamina) : 0f;
            CurrentStamina = MaximumStamina;
            State = DroneStaminaState.Ready;
            _regenDelayRemaining = 0f;
        }

        public void SetMaximumStamina(float maximumStamina, bool restoreAddedCapacity)
        {
            float previousMaximum = MaximumStamina;
            float nextMaximum = Mathf.Max(0.01f, maximumStamina);
            if (Mathf.Approximately(previousMaximum, nextMaximum))
            {
                return;
            }

            float addedCapacity = Mathf.Max(0f, nextMaximum - previousMaximum);
            MaximumStamina = nextMaximum;
            CurrentStamina = restoreAddedCapacity
                ? Mathf.Min(MaximumStamina, CurrentStamina + addedCapacity)
                : Mathf.Min(CurrentStamina, MaximumStamina);
        }

        public void SetEnvironmentalDrainMultiplier(float multiplier)
        {
            _environmentalDrainMultiplier = Mathf.Max(1f, multiplier);
        }

        public void Tick(bool boostHeld, float deltaTime)
        {
            if (_settings == null)
            {
                return;
            }

            float maximum = Mathf.Max(0.01f, MaximumStamina);
            float elapsed = Mathf.Max(0f, deltaTime);

            if (State == DroneStaminaState.Exhausted)
            {
                Recharge(maximum, elapsed, true);
                return;
            }

            if (boostHeld && CurrentStamina > 0f)
            {
                State = DroneStaminaState.Boosting;
                _regenDelayRemaining = Mathf.Max(0f, _settings.RegenDelay);
                CurrentStamina = Mathf.Max(
                    0f,
                    CurrentStamina - (Mathf.Max(0f, _settings.DrainRate) * _environmentalDrainMultiplier * elapsed));
                if (CurrentStamina <= 0f)
                {
                    CurrentStamina = 0f;
                    State = DroneStaminaState.Exhausted;
                }
                return;
            }

            if (CurrentStamina >= maximum)
            {
                CurrentStamina = maximum;
                State = DroneStaminaState.Ready;
                return;
            }

            State = DroneStaminaState.Regenerating;
            Recharge(maximum, elapsed, false);
        }

        private void Recharge(float maximum, float deltaTime, bool exhausted)
        {
            if (_regenDelayRemaining > 0f)
            {
                _regenDelayRemaining = Mathf.Max(0f, _regenDelayRemaining - deltaTime);
                return;
            }

            CurrentStamina = Mathf.Min(
                maximum,
                CurrentStamina + (Mathf.Max(
                    0f,
                    exhausted ? _settings.ExhaustedRegenRate : _settings.RegenRate) * deltaTime));
            if (CurrentStamina >= maximum)
            {
                CurrentStamina = maximum;
                State = DroneStaminaState.Ready;
            }
            else
            {
                State = exhausted ? DroneStaminaState.Exhausted : DroneStaminaState.Regenerating;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneBoostSpeedModifier : MonoBehaviour
    {
        public float BoostBlend { get; private set; }
        public float BoostMaximumSpeed { get; private set; }
        public float CurrentResponse => _settings == null
            ? 0f
            : Mathf.Lerp(_settings.BoostDeceleration, _settings.BoostAcceleration, BoostBlend);

        private StaminaBoostTuning _settings;

        public void Initialize(StaminaBoostTuning settings)
        {
            _settings = settings;
            BoostMaximumSpeed = settings != null ? Mathf.Max(0f, settings.BoostMaximumSpeed) : 0f;
            BoostBlend = 0f;
        }

        public void SetBoostMaximumSpeed(float maximumSpeed)
        {
            BoostMaximumSpeed = Mathf.Max(0f, maximumSpeed);
        }

        public void Tick(bool boosting, float deltaTime)
        {
            if (_settings == null)
            {
                BoostBlend = 0f;
                return;
            }

            float rate = boosting ? _settings.BoostAcceleration : _settings.BoostDeceleration;
            BoostBlend = Mathf.MoveTowards(BoostBlend, boosting ? 1f : 0f, Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
        }

        public float ModifyTargetSpeed(float normalTargetSpeed)
        {
            if (_settings == null || BoostBlend <= 0f)
            {
                return normalTargetSpeed;
            }

            float boostedSpeed = normalTargetSpeed * Mathf.Max(1f, _settings.BoostSpeedMultiplier);
            if (BoostMaximumSpeed > 0f)
            {
                boostedSpeed = Mathf.Min(boostedSpeed, BoostMaximumSpeed);
            }
            return Mathf.Lerp(normalTargetSpeed, boostedSpeed, BoostBlend);
        }
    }
}
