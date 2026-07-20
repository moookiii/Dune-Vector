using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DroneHealth : MonoBehaviour
    {
        public float MaximumHealth { get; private set; } = 100f;
        public float CurrentHealth { get; private set; } = 100f;
        public float NormalizedHealth => MaximumHealth > 0f ? Mathf.Clamp01(CurrentHealth / MaximumHealth) : 0f;
        public bool IsDead { get; private set; }
        public bool IsDamageImmune { get; private set; }
        public string LastDamageSource { get; private set; } = "Unknown damage source";
        public string LastDeathMessage { get; private set; } = "Destroyed by an unknown damage source.";

        public event Action<float, float> HealthChanged;
        public event Action<float> Damaged;
        public event Action<float> Healed;
        public event Action Died;

        private float _damageInvulnerability;
        private float _nextDamageTime;

        public void Initialize(float maximumHealth, float damageInvulnerability)
        {
            MaximumHealth = Mathf.Max(1f, maximumHealth);
            CurrentHealth = MaximumHealth;
            _damageInvulnerability = Mathf.Max(0f, damageInvulnerability);
            IsDead = false;
            IsDamageImmune = false;
            LastDamageSource = "Unknown damage source";
            LastDeathMessage = "Destroyed by an unknown damage source.";
        }

        public void SetMaximumHealth(float maximumHealth, bool restoreAddedCapacity)
        {
            float previousMaximum = MaximumHealth;
            float nextMaximum = Mathf.Max(1f, maximumHealth);
            if (Mathf.Approximately(previousMaximum, nextMaximum))
            {
                return;
            }

            float addedCapacity = Mathf.Max(0f, nextMaximum - previousMaximum);
            MaximumHealth = nextMaximum;
            CurrentHealth = restoreAddedCapacity
                ? Mathf.Min(MaximumHealth, CurrentHealth + addedCapacity)
                : Mathf.Min(CurrentHealth, MaximumHealth);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);
        }

        public bool TakeDamage(float damage)
        {
            return TakeDamage(damage, "Unknown damage source");
        }

        public bool TakeDamage(float damage, string damageSource)
        {
            return TakeDamage(damage, damageSource, null);
        }

        public bool TakeDamage(float damage, string damageSource, string deathMessage)
        {
            if (IsDead || IsDamageImmune || damage <= 0f || Time.time < _nextDamageTime)
            {
                return false;
            }

            _nextDamageTime = Time.time + _damageInvulnerability;
            LastDamageSource = string.IsNullOrWhiteSpace(damageSource) ? "Unknown damage source" : damageSource;
            LastDeathMessage = string.IsNullOrWhiteSpace(deathMessage)
                ? $"Destroyed by {LastDamageSource}."
                : deathMessage;
            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);
            Damaged?.Invoke(previousHealth - CurrentHealth);
            if (CurrentHealth <= 0f)
            {
                IsDead = true;
                Debug.Log($"Player killed by {LastDamageSource} (final hit: {previousHealth - CurrentHealth:0.##} damage).", this);
                Died?.Invoke();
            }
            return true;
        }

        public void SetDamageImmune(bool immune)
        {
            IsDamageImmune = immune;
        }

        public bool RestoreHealth(float amount)
        {
            if (IsDead || amount <= 0f || CurrentHealth >= MaximumHealth)
            {
                return false;
            }

            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Min(MaximumHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);
            float restored = CurrentHealth - previousHealth;
            if (restored <= 0f)
            {
                return false;
            }

            Healed?.Invoke(restored);
            return true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneHealthHUD : MonoBehaviour
    {
        public DroneHealth Health;

        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _pickupStyle;
        private DroneHealth _observedHealth;
        private float _healthRestored;
        private float _feedbackUntil;

        private void OnDestroy()
        {
            if (_observedHealth != null)
            {
                _observedHealth.Healed -= HandleHealed;
            }
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (Health == null)
            {
                return;
            }

            ObserveHealth();

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white },
            };
            _valueStyle ??= new GUIStyle(_labelStyle)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Normal,
            };

            Rect panel = new Rect(Screen.width - 244f, Screen.height - 82f, 220f, 50f);
            GUI.Box(panel, GUIContent.none);
            Rect textRow = new Rect(panel.x + 12f, panel.y + 4f, panel.width - 24f, 21f);
            GUI.Label(new Rect(textRow.x, textRow.y, 72f, textRow.height), "HEALTH", _labelStyle);
            GUI.Label(
                new Rect(textRow.x + 76f, textRow.y, textRow.width - 76f, textRow.height),
                $"{Mathf.CeilToInt(Health.CurrentHealth)} / {Mathf.CeilToInt(Health.MaximumHealth)}",
                _valueStyle);

            Rect bar = new Rect(panel.x + 12f, panel.y + 29f, panel.width - 24f, 9f);
            GUI.Box(bar, GUIContent.none);
            Color oldColor = GUI.color;
            GUI.color = Color.Lerp(new Color(0.9f, 0.08f, 0.03f), new Color(0.05f, 0.9f, 0.72f), Health.NormalizedHealth);
            GUI.DrawTexture(
                new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * Health.NormalizedHealth, bar.height - 2f),
                Texture2D.whiteTexture);
            GUI.color = oldColor;

            RingTuning ringSettings = DuneVectorBootstrap.Instance != null
                ? DuneVectorBootstrap.Instance.Rings
                : null;
            if (ringSettings == null || Time.unscaledTime >= _feedbackUntil)
            {
                return;
            }

            _pickupStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _pickupStyle.fontSize = ringSettings.HealthPickupFeedbackFontSize;
            float duration = Mathf.Max(0.1f, ringSettings.HealthPickupFeedbackDuration);
            Color pickupColor = ringSettings.HealthPickupFeedbackColor;
            pickupColor.a *= Mathf.Clamp01((_feedbackUntil - Time.unscaledTime) / duration);
            _pickupStyle.normal.textColor = pickupColor;
            GUI.Label(
                new Rect(0f, ringSettings.HealthPickupFeedbackTop, Screen.width, ringSettings.HealthPickupFeedbackHeight),
                $"+{Mathf.CeilToInt(_healthRestored)} HEALTH",
                _pickupStyle);
        }

        private void ObserveHealth()
        {
            if (_observedHealth == Health)
            {
                return;
            }

            if (_observedHealth != null)
            {
                _observedHealth.Healed -= HandleHealed;
            }
            _observedHealth = Health;
            _observedHealth.Healed += HandleHealed;
        }

        private void HandleHealed(float amount)
        {
            RingTuning ringSettings = DuneVectorBootstrap.Instance != null
                ? DuneVectorBootstrap.Instance.Rings
                : null;
            if (ringSettings == null)
            {
                return;
            }

            _healthRestored = amount;
            _feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, ringSettings.HealthPickupFeedbackDuration);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorGameOverController : MonoBehaviour
    {
        public bool IsGameOver { get; private set; }

        private DroneHealth _health;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        public void Initialize(DroneHealth health)
        {
            _health = health;
            if (_health != null)
            {
                _health.Died += HandleDeath;
            }
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.Died -= HandleDeath;
            }
            if (IsGameOver)
            {
                Time.timeScale = 1f;
            }
        }

        private void HandleDeath()
        {
            IsGameOver = true;
            _health.GetComponent<DroneCharacterController>()?.SetHoverEnabled(false);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (!IsGameOver)
            {
                return;
            }

            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 38,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.35f, 0.18f) },
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                normal = { textColor = Color.white },
            };

            Color oldColor = GUI.color;
            GUI.color = new Color(0.015f, 0.01f, 0.025f, 0.88f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = oldColor;

            float width = Mathf.Min(480f, Screen.width - 40f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height * 0.5f) - 150f, width, 300f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 28f, panel.width - 40f, 55f), "RUN OVER", _titleStyle);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 84f, panel.width - 40f, 32f), "Your drone was destroyed.", _bodyStyle);
            GUI.Label(
                new Rect(panel.x + 20f, panel.y + 114f, panel.width - 40f, 30f),
                _health.LastDeathMessage,
                _bodyStyle);

            float buttonWidth = Mathf.Min(180f, panel.width - 48f);
            float buttonX = panel.x + ((panel.width - buttonWidth) * 0.5f);
            if (GUI.Button(new Rect(buttonX, panel.y + 154f, buttonWidth, 42f), "RESTART"))
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
            if (GUI.Button(new Rect(buttonX, panel.y + 210f, buttonWidth, 42f), "QUIT"))
            {
                Time.timeScale = 1f;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
