using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class DroneStaminaHUD : MonoBehaviour
    {
        private DroneCharacterController _drone;
        private DroneHealth _health;
        private Camera _camera;
        private DroneStaminaSystem _stamina;
        private StaminaBoostTuning _settings;
        private DroneStaminaState _previousState;
        private float _visibleAlpha;
        private float _fullIdleTime;
        private float _restoredFeedbackRemaining;
        private float _staminaRestored;
        private float _restoreNotificationUntil;
        private GUIStyle _restoreNotificationStyle;
        private Material _arcMaterial;
        private Vector2 _screenCenter;
        private bool _hasScreenCenter;
        private float _displayedStamina;
        private float _chipStamina;
        private float _chipHoldRemaining;
        private Color _displayedColor;
        private bool _hasDisplayedValues;

        public void Initialize(
            DroneCharacterController drone,
            Camera worldCamera,
            DroneStaminaSystem stamina,
            StaminaBoostTuning settings)
        {
            if (_stamina != null)
            {
                _stamina.Restored -= HandleStaminaRestored;
            }
            _drone = drone;
            _health = drone != null ? drone.GetComponent<DroneHealth>() : null;
            _camera = worldCamera;
            _stamina = stamina;
            _settings = settings;
            _previousState = stamina != null ? stamina.State : DroneStaminaState.Ready;
            _hasDisplayedValues = false;
            _chipHoldRemaining = 0f;
            if (_stamina != null)
            {
                _stamina.Restored += HandleStaminaRestored;
            }
        }

        private void Update()
        {
            if (_stamina == null || _settings == null)
            {
                return;
            }

            if (_previousState != DroneStaminaState.Ready && _stamina.State == DroneStaminaState.Ready)
            {
                _restoredFeedbackRemaining = Mathf.Max(0f, _settings.RestoredFeedbackDuration);
            }
            _previousState = _stamina.State;
            _restoredFeedbackRemaining = Mathf.Max(0f, _restoredFeedbackRemaining - Time.unscaledDeltaTime);

            bool fullAndIdle = _stamina.State == DroneStaminaState.Ready && _restoredFeedbackRemaining <= 0f;
            _fullIdleTime = fullAndIdle ? _fullIdleTime + Time.unscaledDeltaTime : 0f;
            float targetAlpha = fullAndIdle && _fullIdleTime >= _settings.FullIdleFadeDelay
                ? Mathf.Clamp01(_settings.FullIdleAlpha)
                : 1f;
            _visibleAlpha = Mathf.MoveTowards(
                _visibleAlpha,
                targetAlpha,
                Mathf.Max(0f, _settings.VisibilityFadeSpeed) * Time.unscaledDeltaTime);

            UpdateMeterReadout();
        }

        private void UpdateMeterReadout()
        {
            float stamina01 = Mathf.Clamp01(_stamina.NormalizedStamina);
            Color stateColor = GetStateColor(stamina01);
            if (!_hasDisplayedValues)
            {
                _displayedStamina = stamina01;
                _chipStamina = stamina01;
                _displayedColor = stateColor;
                _hasDisplayedValues = true;
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            _displayedStamina = Mathf.Lerp(
                _displayedStamina,
                stamina01,
                DuneVectorMath.Sharpness(_settings.MeterFillSharpness, deltaTime));
            _displayedColor = Color.Lerp(
                _displayedColor,
                stateColor,
                DuneVectorMath.Sharpness(_settings.MeterColorBlendSharpness, deltaTime));

            if (_chipStamina <= _displayedStamina)
            {
                _chipStamina = _displayedStamina;
                _chipHoldRemaining = Mathf.Max(0f, _settings.ChipTrailDelay);
            }
            else if (_chipHoldRemaining > 0f)
            {
                _chipHoldRemaining -= deltaTime;
            }
            else
            {
                _chipStamina = Mathf.MoveTowards(
                    _chipStamina,
                    _displayedStamina,
                    Mathf.Max(0f, _settings.ChipTrailCatchUpRate) * deltaTime);
            }
        }

        private void LateUpdate()
        {
            if (_drone == null || _camera == null || _settings == null)
            {
                _hasScreenCenter = false;
                return;
            }

            Vector3 screenPosition = _camera.WorldToScreenPoint(_drone.VisualWorldCenter);
            if (screenPosition.z <= 0f)
            {
                _hasScreenCenter = false;
                return;
            }

            float padding = Mathf.Max(0f, _settings.ScreenEdgePadding);
            float boostBlend = _drone.StaminaBoostBlend;
            Vector2 meterOffset = Vector2.Lerp(
                _settings.MeterScreenOffset,
                _settings.MeterMaximumSpeedScreenOffset,
                boostBlend);
            if (_drone.IsBoosting && !_drone.HasMovementInput)
            {
                Vector2 sprintInwardTravel =
                    _settings.MeterScreenOffset - _settings.MeterMaximumSpeedScreenOffset;
                meterOffset += sprintInwardTravel
                    * boostBlend
                    * Mathf.Max(0f, _settings.StationarySprintOutwardCompensation);
            }
            Vector2 targetCenter = new Vector2(
                Mathf.Clamp(screenPosition.x + meterOffset.x, padding, Screen.width - padding),
                Mathf.Clamp(Screen.height - screenPosition.y + meterOffset.y, padding, Screen.height - padding));
            if (!_hasScreenCenter)
            {
                _screenCenter = targetCenter;
            }
            else
            {
                _screenCenter = Vector2.Lerp(
                    _screenCenter,
                    targetCenter,
                    DuneVectorMath.Sharpness(_settings.MeterFollowSharpness, Time.unscaledDeltaTime));
            }
            _hasScreenCenter = true;
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed || (_health != null && _health.IsDead))
            {
                return;
            }
            if (_stamina == null || _settings == null)
            {
                return;
            }

            if (_hasScreenCenter && _visibleAlpha > 0f && Event.current.type == EventType.Repaint)
            {
                Vector2 center = _screenCenter;
                float stamina01 = Mathf.Clamp01(_displayedStamina);
                Color meterColor = ApplyMeterEmphasis(_displayedColor, stamina01);
                Color backgroundColor = _settings.MeterBackgroundColor;
                backgroundColor.a *= _visibleAlpha;
                meterColor.a *= _visibleAlpha;

                DrawBackgroundIcon(center, backgroundColor);
                if (EnsureArcMaterial())
                {
                    float totalDegrees = _settings.MeterArcDegrees;
                    float filledDegrees = totalDegrees * stamina01;
                    float filledStartDegrees = _settings.MeterArcStartDegrees
                        + (totalDegrees - filledDegrees);
                    if (_settings.ChipTrailEnabled && _chipStamina > stamina01)
                    {
                        float chipDegrees = totalDegrees * Mathf.Clamp01(_chipStamina);
                        Color chipColor = _settings.ChipTrailColor;
                        chipColor.a *= _visibleAlpha;
                        DrawContinuousArc(
                            center,
                            _settings.MeterArcStartDegrees + (totalDegrees - chipDegrees),
                            chipDegrees - filledDegrees,
                            chipColor);
                    }
                    DrawContinuousArc(center, filledStartDegrees, filledDegrees, meterColor);
                }
            }

            DrawRestoreNotification();
        }

        private void DrawRestoreNotification()
        {
            if (Time.unscaledTime >= _restoreNotificationUntil)
            {
                return;
            }

            _restoreNotificationStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _restoreNotificationStyle.fontSize = _settings.RestoreNotificationFontSize;
            float duration = Mathf.Max(0.1f, _settings.RestoreNotificationDuration);
            Color notificationColor = _settings.RestoreNotificationColor;
            notificationColor.a *= Mathf.Clamp01((_restoreNotificationUntil - Time.unscaledTime) / duration);
            _restoreNotificationStyle.normal.textColor = notificationColor;
            GUI.Label(
                new Rect(
                    0f,
                    _settings.RestoreNotificationTop,
                    Screen.width,
                    _settings.RestoreNotificationHeight),
                string.Format(
                    _settings.RestoreNotificationFormat,
                    Mathf.CeilToInt(Mathf.Max(0f, _staminaRestored))),
                _restoreNotificationStyle);
        }

        private void HandleStaminaRestored(float amount)
        {
            if (_settings == null)
            {
                return;
            }

            _staminaRestored = amount;
            _restoreNotificationUntil =
                Time.unscaledTime + Mathf.Max(0.1f, _settings.RestoreNotificationDuration);
            _restoredFeedbackRemaining = Mathf.Max(
                _restoredFeedbackRemaining,
                Mathf.Max(0f, _settings.RestoredFeedbackDuration));
        }

        private void DrawBackgroundIcon(Vector2 center, Color tint)
        {
            if (_settings.MeterBackgroundIcon == null || tint.a <= 0f)
            {
                return;
            }

            float size = Mathf.Max(1f, _settings.MeterBackgroundIconSize);
            Vector2 iconCenter = center + _settings.MeterBackgroundIconOffset;
            Rect destination = new Rect(
                iconCenter.x - (size * 0.5f),
                iconCenter.y - (size * 0.5f),
                size,
                size);
            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(destination, _settings.MeterBackgroundIcon, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
        }

        private Color ApplyMeterEmphasis(Color meterColor, float stamina01)
        {
            bool low = _stamina.State == DroneStaminaState.Exhausted
                || stamina01 <= _settings.LowStaminaThreshold;
            if (low && _settings.LowPulseStrength > 0f)
            {
                float pulse = (Mathf.Sin(Time.unscaledTime * _settings.LowPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                Color dimmed = meterColor * (1f - Mathf.Clamp01(_settings.LowPulseStrength));
                dimmed.a = meterColor.a;
                meterColor = Color.Lerp(meterColor, dimmed, pulse);
            }

            float restoreDuration = Mathf.Max(0.0001f, _settings.RestoredFeedbackDuration);
            float flash = Mathf.Clamp01(_restoredFeedbackRemaining / restoreDuration)
                * _settings.RestoreFlashStrength;
            if (flash > 0f)
            {
                Color flashColor = _settings.RestoreFlashColor;
                flashColor.a = meterColor.a;
                meterColor = Color.Lerp(meterColor, flashColor, flash);
            }
            return meterColor;
        }

        private Color GetStateColor(float stamina01)
        {
            if (_stamina.State == DroneStaminaState.Exhausted)
            {
                return _settings.EmptyColor;
            }
            if (_stamina.State == DroneStaminaState.Regenerating)
            {
                return _settings.RegeneratingColor;
            }
            if (stamina01 <= _settings.LowStaminaThreshold)
            {
                return _settings.LowColor;
            }
            return _stamina.State == DroneStaminaState.Boosting ? _settings.BoostingColor : _settings.ReadyColor;
        }

        private bool EnsureArcMaterial()
        {
            if (_arcMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }

            _arcMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _arcMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _arcMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _arcMaterial.SetInt("_Cull", (int)CullMode.Off);
            _arcMaterial.SetInt("_ZWrite", 0);
            return true;
        }

        private void DrawContinuousArc(
            Vector2 center,
            float startDegrees,
            float arcDegrees,
            Color color)
        {
            if (Mathf.Abs(arcDegrees) <= 0.001f || color.a <= 0f)
            {
                return;
            }

            int fullResolution = Mathf.Max(32, _settings.MeterArcResolution);
            int steps = Mathf.Max(1, Mathf.CeilToInt(fullResolution * (Mathf.Abs(arcDegrees) / 360f)));
            float radius = Mathf.Max(0f, _settings.MeterRadius);
            float halfThickness = Mathf.Max(0.5f, _settings.MeterThickness * 0.5f);

            _arcMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            for (int index = 0; index < steps; index++)
            {
                float angle0 = (startDegrees + (arcDegrees * (index / (float)steps))) * Mathf.Deg2Rad;
                float angle1 = (startDegrees + (arcDegrees * ((index + 1f) / steps))) * Mathf.Deg2Rad;
                Vector2 radial0 = new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0));
                Vector2 radial1 = new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1));

                GL.Vertex(center + (radial0 * (radius - halfThickness)));
                GL.Vertex(center + (radial0 * (radius + halfThickness)));
                GL.Vertex(center + (radial1 * (radius + halfThickness)));
                GL.Vertex(center + (radial1 * (radius - halfThickness)));
            }
            GL.End();

            Vector2 startRadial = new Vector2(
                Mathf.Cos(startDegrees * Mathf.Deg2Rad),
                Mathf.Sin(startDegrees * Mathf.Deg2Rad));
            float endAngle = (startDegrees + arcDegrees) * Mathf.Deg2Rad;
            Vector2 endRadial = new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle));
            int capSteps = Mathf.Max(8, fullResolution / 8);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            DrawRoundCap(center + (startRadial * radius), halfThickness, capSteps);
            DrawRoundCap(center + (endRadial * radius), halfThickness, capSteps);
            GL.End();
            GL.PopMatrix();
        }

        private static void DrawRoundCap(Vector2 center, float radius, int steps)
        {
            for (int index = 0; index < steps; index++)
            {
                float angle0 = (index / (float)steps) * Mathf.PI * 2f;
                float angle1 = ((index + 1f) / steps) * Mathf.PI * 2f;
                GL.Vertex(center);
                GL.Vertex(center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * radius);
                GL.Vertex(center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius);
            }
        }

        private void OnDestroy()
        {
            if (_stamina != null)
            {
                _stamina.Restored -= HandleStaminaRestored;
            }
            if (_arcMaterial != null)
            {
                Destroy(_arcMaterial);
            }
        }
    }
}
