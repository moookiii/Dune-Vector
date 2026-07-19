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
        public float NormalizedStamina => _settings != null && _settings.MaxStamina > 0f
            ? Mathf.Clamp01(CurrentStamina / _settings.MaxStamina)
            : 0f;
        public bool IsBoosting => State == DroneStaminaState.Boosting;
        public bool IsExhausted => State == DroneStaminaState.Exhausted;

        private StaminaBoostTuning _settings;
        private float _regenDelayRemaining;

        public void Initialize(StaminaBoostTuning settings)
        {
            _settings = settings;
            CurrentStamina = settings != null ? Mathf.Max(0.01f, settings.MaxStamina) : 0f;
            State = DroneStaminaState.Ready;
            _regenDelayRemaining = 0f;
        }

        public void Tick(bool boostHeld, float deltaTime)
        {
            if (_settings == null)
            {
                return;
            }

            float maximum = Mathf.Max(0.01f, _settings.MaxStamina);
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
                CurrentStamina = Mathf.Max(0f, CurrentStamina - (Mathf.Max(0f, _settings.DrainRate) * elapsed));
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
                CurrentStamina + (Mathf.Max(0f, _settings.RegenRate) * deltaTime));
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
        public float CurrentResponse => _settings == null
            ? 0f
            : Mathf.Lerp(_settings.BoostDeceleration, _settings.BoostAcceleration, BoostBlend);

        private StaminaBoostTuning _settings;

        public void Initialize(StaminaBoostTuning settings)
        {
            _settings = settings;
            BoostBlend = 0f;
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
            if (_settings.BoostMaximumSpeed > 0f)
            {
                boostedSpeed = Mathf.Min(boostedSpeed, _settings.BoostMaximumSpeed);
            }
            return Mathf.Lerp(normalTargetSpeed, boostedSpeed, BoostBlend);
        }
    }
}
