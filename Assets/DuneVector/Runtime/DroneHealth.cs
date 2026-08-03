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
        public bool HasInfiniteHealth { get; private set; }
        public string LastDamageSource { get; private set; } = "Unknown damage source";
        public string LastDeathMessage { get; private set; } = "Destroyed by an unknown damage source.";

        public event Action<float, float> HealthChanged;
        public event Action<float> Damaged;
        public event Action<float> Healed;
        public event Action Died;

        private float _damageInvulnerability;
        private float _nextDamageTime;

        public void Initialize(float maximumHealth, float damageInvulnerability, bool infiniteHealth = false)
        {
            MaximumHealth = Mathf.Max(1f, maximumHealth);
            CurrentHealth = MaximumHealth;
            _damageInvulnerability = Mathf.Max(0f, damageInvulnerability);
            IsDead = false;
            IsDamageImmune = false;
            HasInfiniteHealth = infiniteHealth;
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
            if (IsDead || IsDamageImmune || HasInfiniteHealth || damage <= 0f || Time.time < _nextDamageTime)
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

        public void SetInfiniteHealth(bool enabled)
        {
            HasInfiniteHealth = enabled;
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

        public bool ReviveAtFullHealth()
        {
            if (!IsDead)
            {
                return false;
            }

            float previousHealth = CurrentHealth;
            IsDead = false;
            IsDamageImmune = false;
            CurrentHealth = MaximumHealth;
            _nextDamageTime = Time.time;
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);

            float restored = CurrentHealth - previousHealth;
            if (restored > 0f)
            {
                Healed?.Invoke(restored);
            }
            return true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneHealthHUD : MonoBehaviour
    {
        public DroneHealth Health;

        private BottomHudTuning _settings;
        private GUIStyle _pickupStyle;
        private DroneHealth _observedHealth;
        private float _healthRestored;
        private float _feedbackUntil;

        public void Initialize(DroneHealth health, BottomHudTuning settings)
        {
            Health = health;
            _settings = settings;
        }

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

            if (_settings != null)
            {
                float health01 = Health.NormalizedHealth;
                DuneVectorBottomHud.DrawMeterPanel(
                    DuneVectorBottomHud.GetPanelRect(_settings, DuneVectorBottomHudPanel.Health),
                    _settings.HealthLabel,
                    $"{Mathf.CeilToInt(Health.CurrentHealth)} / {Mathf.CeilToInt(Health.MaximumHealth)}",
                    health01,
                    Color.Lerp(_settings.HealthLowColor, _settings.HealthFullColor, health01),
                    _settings);
            }

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
                string.Format(
                    ringSettings.HealthPickupFeedbackFormat,
                    Mathf.CeilToInt(Mathf.Max(0f, _healthRestored))),
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
        private DuneVectorCourierGame _courierGame;
        private GameOverScreenTuning _settings;
        private PlayerStrikeOrbTuning _strikeOrbSettings;
        private VesperKiteTuning _vesperKiteSettings;
        private GUIStyle _eyebrowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _eliminationStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _secondaryButtonStyle;
        private GUIStyle _footerStyle;
        private GUIStyle _deathNoteLabelStyle;
        private GUIStyle _deathNoteMessageStyle;
        private GUIStyle _buttonHitStyle;
        private float _styledScale = -1f;
        private float _deathStartedAt;
        private string _activeDeathNoteLabel;
        private string _activeDeathNoteMessage;

        public void Initialize(
            DroneHealth health,
            GameOverScreenTuning settings,
            PlayerStrikeOrbTuning strikeOrbSettings,
            VesperKiteTuning vesperKiteSettings)
        {
            _health = health;
            _settings = settings;
            _strikeOrbSettings = strikeOrbSettings;
            _vesperKiteSettings = vesperKiteSettings;
            if (_health != null)
            {
                _health.Died += HandleDeath;
            }
        }

        public void BindCourierGame(DuneVectorCourierGame courierGame)
        {
            _courierGame = courierGame;
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
            DuneVectorPhotographySystem.CancelCameraMode();
            IsGameOver = true;
            _deathStartedAt = Time.unscaledTime;
            TryActivateDeathNote();
            _health.GetComponent<DroneCharacterController>()?.SetHoverEnabled(false);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void TryActivateDeathNote()
        {
            _activeDeathNoteLabel = null;
            _activeDeathNoteMessage = null;
            DuneVectorCourierProgress progress = _courierGame != null ? _courierGame.Progress : null;
            if (_settings == null || progress == null)
            {
                return;
            }

            if (_settings.ShowFirstStrikeOrbDeathNote &&
                !progress.StrikeOrbDeathNoteAcknowledged &&
                _strikeOrbSettings != null &&
                !string.IsNullOrEmpty(_strikeOrbSettings.LightningDamageSource) &&
                string.Equals(
                    _health.LastDamageSource,
                    _strikeOrbSettings.LightningDamageSource,
                    StringComparison.Ordinal))
            {
                _activeDeathNoteLabel = _settings.StrikeOrbNoteLabel;
                _activeDeathNoteMessage = _settings.StrikeOrbNoteMessage;
                progress.AcknowledgeStrikeOrbDeathNote();
                return;
            }

            if (_settings.ShowFirstVesperPilgrimDeathNote &&
                !progress.VesperPilgrimDeathNoteAcknowledged &&
                _vesperKiteSettings != null &&
                !string.IsNullOrEmpty(_vesperKiteSettings.PilgrimDamageSource) &&
                string.Equals(
                    _health.LastDamageSource,
                    _vesperKiteSettings.PilgrimDamageSource,
                    StringComparison.Ordinal))
            {
                _activeDeathNoteLabel = _settings.VesperPilgrimNoteLabel;
                _activeDeathNoteMessage = _settings.VesperPilgrimNoteMessage;
                progress.AcknowledgeVesperPilgrimDeathNote();
            }
        }

        private void OnGUI()
        {
            if (!IsGameOver)
            {
                return;
            }

            if (_settings == null)
            {
                return;
            }

            float scale = CalculateScale();
            EnsureStyles(scale);
            float entrance = Mathf.Clamp01(
                (Time.unscaledTime - _deathStartedAt) /
                Mathf.Max(Mathf.Epsilon, _settings.EntranceDuration));
            float easedEntrance = 1f - Mathf.Pow(1f - entrance, 3f);

            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            DrawSolidRect(screen, WithAlpha(_settings.OverlayColor, entrance));
            DrawScanlines(screen, entrance, scale);

            float screenMargin = _settings.ScreenMargin * scale;
            float panelWidth = Mathf.Min(_settings.PanelWidth * scale, Screen.width - (screenMargin * 2f));
            float panelHeight = Mathf.Min(_settings.PanelHeight * scale, Screen.height - (screenMargin * 2f));
            float entranceOffset = (1f - easedEntrance) * _settings.EntranceVerticalOffset * scale;
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                ((Screen.height - panelHeight) * 0.5f) + entranceOffset,
                panelWidth,
                panelHeight);

            float shadowOffset = _settings.ShadowOffset * scale;
            DrawSolidRect(
                new Rect(panel.x + shadowOffset, panel.y + shadowOffset, panel.width, panel.height),
                WithAlpha(_settings.ShadowColor, easedEntrance));
            DrawSolidRect(panel, WithAlpha(_settings.PanelColor, easedEntrance));

            float borderThickness = Mathf.Max(1f, _settings.BorderThickness * scale);
            DrawBorder(panel, WithAlpha(_settings.BorderColor, easedEntrance), borderThickness);
            DrawSolidRect(
                new Rect(panel.x, panel.y, panel.width, Mathf.Max(1f, _settings.AccentBarHeight * scale)),
                WithAlpha(_settings.AccentColor, easedEntrance));
            DrawCornerBrackets(panel, scale, easedEntrance);

            float padding = _settings.PanelPadding * scale;
            Rect content = new Rect(
                panel.x + padding,
                panel.y + padding,
                panel.width - (padding * 2f),
                panel.height - (padding * 2f));
            float y = content.y;

            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, easedEntrance);

            float eyebrowHeight = _settings.EyebrowHeight * scale;
            GUI.Label(new Rect(content.x, y, content.width, eyebrowHeight), _settings.Eyebrow, _eyebrowStyle);
            y += eyebrowHeight + (_settings.HeaderGap * scale);

            float titleHeight = _settings.TitleHeight * scale;
            GUI.Label(new Rect(content.x, y, content.width, titleHeight), _settings.Title, _titleStyle);
            y += titleHeight;

            float subtitleHeight = _settings.SubtitleHeight * scale;
            GUI.Label(new Rect(content.x, y, content.width, subtitleHeight), _settings.Subtitle, _subtitleStyle);
            y += subtitleHeight + (_settings.SectionGap * scale);

            float eliminationHeight = _settings.EliminationHeight * scale;
            GUI.Label(
                new Rect(content.x, y, content.width, eliminationHeight),
                _health.LastDeathMessage.ToUpperInvariant(),
                _eliminationStyle);
            y += eliminationHeight + (_settings.ActionGap * scale);
            GUI.color = previousGuiColor;

            Rect restartRect = new Rect(
                content.x,
                y,
                content.width,
                _settings.PrimaryButtonHeight * scale);
            bool restartHovered = restartRect.Contains(Event.current.mousePosition);
            DrawActionButton(
                restartRect,
                _settings.RestartButtonLabel,
                _primaryButtonStyle,
                restartHovered ? _settings.PrimaryButtonHoverColor : _settings.PrimaryButtonColor,
                _settings.PrimaryButtonTextColor,
                scale,
                easedEntrance,
                primary: true);
            bool restartClicked = GUI.Button(restartRect, GUIContent.none, _buttonHitStyle);
            y += restartRect.height + (_settings.ButtonGap * scale);

            Rect quitRect = new Rect(
                content.x,
                y,
                content.width,
                _settings.SecondaryButtonHeight * scale);
            bool quitHovered = quitRect.Contains(Event.current.mousePosition);
            DrawActionButton(
                quitRect,
                _settings.QuitButtonLabel,
                _secondaryButtonStyle,
                quitHovered ? _settings.SecondaryButtonHoverColor : _settings.SecondaryButtonColor,
                _settings.SecondaryTextColor,
                scale,
                easedEntrance,
                primary: false);
            bool quitClicked = GUI.Button(quitRect, GUIContent.none, _buttonHitStyle);
            y += quitRect.height + (_settings.FooterGap * scale);

            previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, easedEntrance);
            GUI.Label(
                new Rect(content.x, y, content.width, _settings.FooterHeight * scale),
                _settings.FooterHint,
                _footerStyle);
            GUI.color = previousGuiColor;

            if (!string.IsNullOrEmpty(_activeDeathNoteMessage))
            {
                DrawDeathNote(panel, screenMargin, scale);
            }

            Event currentEvent = Event.current;
            bool restartKeyPressed =
                currentEvent.type == EventType.KeyDown &&
                (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);
            if (restartClicked || restartKeyPressed)
            {
                if (restartKeyPressed)
                {
                    currentEvent.Use();
                }
                RestartAtHub();
                return;
            }
            if (quitClicked)
            {
                QuitGame();
            }
        }

        private float CalculateScale()
        {
            float widthScale = Screen.width / Mathf.Max(1f, _settings.ReferenceWidth);
            float heightScale = Screen.height / Mathf.Max(1f, _settings.ReferenceHeight);
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), _settings.MinimumScale, _settings.MaximumScale);
        }

        private void EnsureStyles(float scale)
        {
            if (Mathf.Abs(scale - _styledScale) < 0.001f)
            {
                return;
            }
            _styledScale = scale;
            _eyebrowStyle = CreateLabelStyle(
                _settings.EyebrowFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.SecondaryTextColor);
            _titleStyle = CreateLabelStyle(
                _settings.TitleFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.TitleColor);
            _subtitleStyle = CreateLabelStyle(
                _settings.SubtitleFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.PrimaryTextColor);
            _eliminationStyle = CreateLabelStyle(
                _settings.EliminationFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.PrimaryTextColor);
            _primaryButtonStyle = CreateLabelStyle(
                _settings.PrimaryButtonFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.PrimaryButtonTextColor);
            _secondaryButtonStyle = CreateLabelStyle(
                _settings.SecondaryButtonFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _settings.SecondaryTextColor);
            _footerStyle = CreateLabelStyle(
                _settings.FooterFontSize,
                scale,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _settings.SecondaryTextColor);
            _deathNoteLabelStyle = CreateLabelStyle(
                _settings.StrikeOrbNoteLabelFontSize,
                scale,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _settings.StrikeOrbNoteLabelColor);
            _deathNoteMessageStyle = CreateLabelStyle(
                _settings.StrikeOrbNoteMessageFontSize,
                scale,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                _settings.StrikeOrbNoteMessageColor,
                wordWrap: true);
            _buttonHitStyle = new GUIStyle(GUIStyle.none);
        }

        private GUIStyle CreateLabelStyle(
            int fontSize,
            float scale,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color,
            bool wordWrap = false)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = _settings.InterfaceFont,
                fontSize = Mathf.Max(1, Mathf.RoundToInt(fontSize * scale)),
                fontStyle = fontStyle,
                alignment = alignment,
                clipping = TextClipping.Clip,
                wordWrap = wordWrap,
                padding = new RectOffset(),
                margin = new RectOffset(),
                normal = { textColor = color },
            };
        }

        private void DrawDeathNote(Rect recoveryPanel, float screenMargin, float scale)
        {
            float elapsed = Time.unscaledTime - _deathStartedAt - _settings.StrikeOrbNoteEntranceDelay;
            float entrance = Mathf.Clamp01(
                elapsed / Mathf.Max(Mathf.Epsilon, _settings.StrikeOrbNoteEntranceDuration));
            float easedEntrance = 1f - Mathf.Pow(1f - entrance, 3f);
            if (easedEntrance <= 0f)
            {
                return;
            }

            float maximumWidth = Mathf.Max(1f, Screen.width - (screenMargin * 2f));
            float maximumHeight = Mathf.Max(1f, Screen.height - (screenMargin * 2f));
            float width = Mathf.Min(_settings.StrikeOrbNoteWidth * scale, maximumWidth);
            float height = Mathf.Min(_settings.StrikeOrbNoteHeight * scale, maximumHeight);
            float gap = _settings.StrikeOrbNotePanelGap * scale;
            float x = recoveryPanel.xMax + gap;
            float y = recoveryPanel.y + (_settings.StrikeOrbNoteVerticalOffset * scale);

            if (x + width > Screen.width - screenMargin)
            {
                float leftX = recoveryPanel.x - gap - width;
                if (leftX >= screenMargin)
                {
                    x = leftX;
                }
                else
                {
                    x = (Screen.width - width) * 0.5f;
                    float belowY = recoveryPanel.yMax + gap;
                    y = belowY + height <= Screen.height - screenMargin
                        ? belowY
                        : Screen.height - screenMargin - height;
                }
            }

            y = Mathf.Clamp(y, screenMargin, Screen.height - screenMargin - height);
            y += (1f - easedEntrance) * _settings.StrikeOrbNoteEntranceVerticalOffset * scale;
            Rect note = new Rect(x, y, width, height);
            float shadowOffset = _settings.ShadowOffset * scale;
            DrawSolidRect(
                new Rect(note.x + shadowOffset, note.y + shadowOffset, note.width, note.height),
                WithAlpha(_settings.ShadowColor, easedEntrance));
            DrawSolidRect(note, WithAlpha(_settings.StrikeOrbNotePanelColor, easedEntrance));
            DrawBorder(
                note,
                WithAlpha(_settings.StrikeOrbNoteBorderColor, easedEntrance),
                Mathf.Max(1f, _settings.BorderThickness * scale));
            DrawSolidRect(
                new Rect(
                    note.x,
                    note.y,
                    Mathf.Max(1f, _settings.StrikeOrbNoteAccentWidth * scale),
                    note.height),
                WithAlpha(_settings.StrikeOrbNoteAccentColor, easedEntrance));

            float padding = _settings.StrikeOrbNotePadding * scale;
            Rect content = new Rect(
                note.x + padding,
                note.y + padding,
                Mathf.Max(1f, note.width - (padding * 2f)),
                Mathf.Max(1f, note.height - (padding * 2f)));
            float labelHeight = _settings.StrikeOrbNoteLabelHeight * scale;
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, easedEntrance);
            GUI.Label(
                new Rect(content.x, content.y, content.width, labelHeight),
                _activeDeathNoteLabel,
                _deathNoteLabelStyle);
            GUI.color = previousGuiColor;

            float dividerY = content.yMax -
                Mathf.Max(1f, _settings.StrikeOrbNoteDividerThickness * scale);
            float messageY = content.y + labelHeight + (_settings.StrikeOrbNoteLabelGap * scale);
            DrawSolidRect(
                new Rect(
                    content.x,
                    dividerY,
                    content.width,
                    Mathf.Max(1f, _settings.StrikeOrbNoteDividerThickness * scale)),
                WithAlpha(_settings.StrikeOrbNoteBorderColor, easedEntrance));

            previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, easedEntrance);
            GUI.Label(
                new Rect(
                    content.x,
                    messageY,
                    content.width,
                    Mathf.Max(1f, dividerY - messageY)),
                _activeDeathNoteMessage,
                _deathNoteMessageStyle);
            GUI.color = previousGuiColor;
        }

        private void DrawActionButton(
            Rect rect,
            string label,
            GUIStyle labelStyle,
            Color backgroundColor,
            Color edgeColor,
            float scale,
            float alpha,
            bool primary)
        {
            DrawSolidRect(rect, WithAlpha(backgroundColor, alpha));
            DrawBorder(
                rect,
                WithAlpha(primary ? _settings.AccentSoftColor : _settings.SecondaryBorderColor, alpha),
                Mathf.Max(1f, _settings.BorderThickness * scale));
            DrawSolidRect(
                new Rect(rect.x, rect.y, Mathf.Max(1f, _settings.ButtonEdgeWidth * scale), rect.height),
                WithAlpha(edgeColor, alpha));
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, label, labelStyle);
            GUI.color = previousGuiColor;
        }

        private void DrawScanlines(Rect screen, float alpha, float scale)
        {
            float spacing = Mathf.Max(2f, _settings.ScanlineSpacing * scale);
            float thickness = Mathf.Max(1f, _settings.ScanlineThickness * scale);
            Color color = WithAlpha(_settings.ScanlineColor, alpha);
            for (float y = 0f; y < screen.height; y += spacing)
            {
                DrawSolidRect(new Rect(screen.x, screen.y + y, screen.width, thickness), color);
            }
        }

        private void DrawCornerBrackets(Rect panel, float scale, float alpha)
        {
            float length = _settings.CornerLength * scale;
            float thickness = Mathf.Max(1f, _settings.CornerThickness * scale);
            float pulsePhase = (Mathf.Sin(Time.unscaledTime * _settings.AccentPulseSpeed) + 1f) * 0.5f;
            float pulse = Mathf.Lerp(_settings.AccentPulseMinimum, _settings.AccentPulseMaximum, pulsePhase);
            Color color = WithAlpha(_settings.AccentColor, alpha * pulse);

            DrawSolidRect(new Rect(panel.x, panel.y, length, thickness), color);
            DrawSolidRect(new Rect(panel.x, panel.y, thickness, length), color);
            DrawSolidRect(new Rect(panel.xMax - length, panel.y, length, thickness), color);
            DrawSolidRect(new Rect(panel.xMax - thickness, panel.y, thickness, length), color);
            DrawSolidRect(new Rect(panel.x, panel.yMax - thickness, length, thickness), color);
            DrawSolidRect(new Rect(panel.x, panel.yMax - length, thickness, length), color);
            DrawSolidRect(new Rect(panel.xMax - length, panel.yMax - thickness, length, thickness), color);
            DrawSolidRect(new Rect(panel.xMax - thickness, panel.yMax - length, thickness, length), color);
        }

        private void RestartAtHub()
        {
            Time.timeScale = 1f;
            if (_courierGame != null && _health.ReviveAtFullHealth())
            {
                IsGameOver = false;
                DroneCharacterController drone = _health.GetComponent<DroneCharacterController>();
                drone?.SetHoverEnabled(true);
                _courierGame.RestartAtHub();
                drone?.PlayDeathRespawnEffect(_courierGame.HubFloorPosition);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static void QuitGame()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static Color WithAlpha(Color color, float multiplier)
        {
            color.a *= Mathf.Clamp01(multiplier);
            return color;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
