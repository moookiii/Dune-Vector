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
                            chipColor,
                            false);
                    }
                    DrawContinuousArc(center, filledStartDegrees, filledDegrees, meterColor, true);
                }
            }

            DrawRestoreNotification();
        }

        private void DrawRestoreNotification()
        {
            float remaining = _restoreNotificationUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                return;
            }

            _restoreNotificationStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
            _restoreNotificationStyle.fontSize = _settings.RestoreNotificationFontSize;

            float duration = Mathf.Max(0.1f, _settings.RestoreNotificationDuration);
            float life01 = Mathf.Clamp01(1f - (remaining / duration));
            float hold = Mathf.Clamp01(_settings.RestoreNotificationHoldFraction);
            float fade = life01 <= hold
                ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (life01 - hold) / Mathf.Max(0.0001f, 1f - hold));

            float rise = _settings.RestoreNotificationRise * EaseOut(life01);
            Rect rect = new Rect(
                0f,
                _settings.RestoreNotificationTop - rise,
                Screen.width,
                _settings.RestoreNotificationHeight);
            string text = string.Format(
                _settings.RestoreNotificationFormat,
                Mathf.CeilToInt(Mathf.Max(0f, _staminaRestored)));

            float popFraction = Mathf.Clamp01(_settings.RestoreNotificationPopFraction);
            float pop = popFraction <= 0f
                ? 1f
                : Mathf.Lerp(
                    Mathf.Max(1f, _settings.RestoreNotificationPopScale),
                    1f,
                    EaseOut(Mathf.Clamp01(life01 / popFraction)));

            Matrix4x4 previousMatrix = GUI.matrix;
            if (!Mathf.Approximately(pop, 1f))
            {
                GUIUtility.ScaleAroundPivot(
                    new Vector2(pop, pop),
                    new Vector2(rect.width * 0.5f, rect.y + (rect.height * 0.5f)));
            }

            Vector2 shadowOffset = _settings.RestoreNotificationShadowOffset;
            if (shadowOffset.sqrMagnitude > 0f)
            {
                // The shadow is retired early so the text finishes fading on its own.
                float shadowLife = Mathf.Clamp01(_settings.RestoreNotificationShadowLifetimeFraction);
                float shadowFade = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((life01 - hold) / Mathf.Max(0.0001f, shadowLife - hold)));
                DrawNotificationText(
                    text,
                    new Rect(rect.x + shadowOffset.x, rect.y + shadowOffset.y, rect.width, rect.height),
                    ScaleAlpha(_settings.RestoreNotificationShadowColor, Mathf.Min(fade, shadowFade)));
            }

            float outline = Mathf.Max(0f, _settings.RestoreNotificationOutlineThickness);
            if (outline > 0f)
            {
                Color outlineColor = ScaleAlpha(_settings.RestoreNotificationOutlineColor, fade);
                float diagonal = outline * 0.7071f;
                DrawNotificationOutlinePass(text, rect, outlineColor, outline, 0f);
                DrawNotificationOutlinePass(text, rect, outlineColor, -outline, 0f);
                DrawNotificationOutlinePass(text, rect, outlineColor, 0f, outline);
                DrawNotificationOutlinePass(text, rect, outlineColor, 0f, -outline);
                DrawNotificationOutlinePass(text, rect, outlineColor, diagonal, diagonal);
                DrawNotificationOutlinePass(text, rect, outlineColor, diagonal, -diagonal);
                DrawNotificationOutlinePass(text, rect, outlineColor, -diagonal, diagonal);
                DrawNotificationOutlinePass(text, rect, outlineColor, -diagonal, -diagonal);
            }

            DrawNotificationText(text, rect, ScaleAlpha(_settings.RestoreNotificationColor, fade));
            GUI.matrix = previousMatrix;
        }

        private void DrawNotificationOutlinePass(
            string text,
            Rect rect,
            Color color,
            float offsetX,
            float offsetY)
        {
            DrawNotificationText(
                text,
                new Rect(rect.x + offsetX, rect.y + offsetY, rect.width, rect.height),
                color);
        }

        private void DrawNotificationText(string text, Rect rect, Color color)
        {
            if (color.a <= 0f)
            {
                return;
            }

            _restoreNotificationStyle.normal.textColor = color;
            GUI.Label(rect, text, _restoreNotificationStyle);
        }

        private static float EaseOut(float t)
        {
            float inverse = 1f - Mathf.Clamp01(t);
            return 1f - (inverse * inverse * inverse);
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
            Color color,
            bool leadingTip)
        {
            if (Mathf.Abs(arcDegrees) <= 0.001f || color.a <= 0f)
            {
                return;
            }

            int fullResolution = Mathf.Max(32, _settings.MeterArcResolution);
            int steps = Mathf.Max(1, Mathf.CeilToInt(fullResolution * (Mathf.Abs(arcDegrees) / 360f)));
            int capSteps = Mathf.Max(8, fullResolution / 8);
            float radius = Mathf.Max(0f, _settings.MeterRadius);
            float halfThickness = Mathf.Max(0.5f, _settings.MeterThickness * 0.5f);
            float feather = Mathf.Max(0f, _settings.MeterEdgeFeather);

            // The arc drains from its low-degree end, so that end is the live leading tip.
            Color tipColor = color;
            Color tailColor = color * (1f - Mathf.Clamp01(_settings.MeterGradientStrength));
            tailColor.a = color.a;

            _arcMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.QUADS);

            if (_settings.MeterGlowOpacity > 0f && _settings.MeterGlowThicknessMultiplier > 1f)
            {
                EmitHalo(
                    center,
                    startDegrees,
                    arcDegrees,
                    steps,
                    capSteps,
                    radius,
                    halfThickness * _settings.MeterGlowThicknessMultiplier,
                    ScaleAlpha(tipColor, _settings.MeterGlowOpacity),
                    ScaleAlpha(tailColor, _settings.MeterGlowOpacity));
            }

            EmitRibbon(
                center,
                startDegrees,
                arcDegrees,
                steps,
                capSteps,
                radius,
                halfThickness,
                feather,
                tipColor,
                tailColor,
                1f);

            if (leadingTip && _settings.MeterTipHighlightStrength > 0f)
            {
                Color highlight = Color.Lerp(
                    tipColor,
                    _settings.MeterTipHighlightColor,
                    Mathf.Clamp01(_settings.MeterTipHighlightStrength));
                highlight.a = tipColor.a;
                Vector2 tip = center + Radial(startDegrees) * radius;
                EmitDisc(
                    tip,
                    halfThickness * Mathf.Max(0f, _settings.MeterTipHighlightScale),
                    feather,
                    highlight,
                    capSteps);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Emits a soft halo centred on the arc line: opaque along the arc itself and fading to
        /// nothing <paramref name="glowHalfThickness"/> pixels to either side.
        /// </summary>
        private static void EmitHalo(
            Vector2 center,
            float startDegrees,
            float arcDegrees,
            int steps,
            int capSteps,
            float radius,
            float glowHalfThickness,
            Color startColor,
            Color endColor)
        {
            EmitBand(
                center,
                startDegrees,
                arcDegrees,
                steps,
                radius,
                radius + glowHalfThickness,
                1f,
                0f,
                startColor,
                endColor);
            EmitBand(
                center,
                startDegrees,
                arcDegrees,
                steps,
                Mathf.Max(0f, radius - glowHalfThickness),
                radius,
                0f,
                1f,
                startColor,
                endColor);
            EmitDisc(center + (Radial(startDegrees) * radius), 0f, glowHalfThickness, startColor, capSteps);
            EmitDisc(center + (Radial(startDegrees + arcDegrees) * radius), 0f, glowHalfThickness, endColor, capSteps);
        }

        /// <summary>
        /// Emits a rounded arc ribbon whose radial edges fade out over <paramref name="feather"/>
        /// pixels, with <paramref name="coreAlpha"/> controlling the opacity of the solid middle
        /// band. A zero core alpha produces a pure halo.
        /// </summary>
        private static void EmitRibbon(
            Vector2 center,
            float startDegrees,
            float arcDegrees,
            int steps,
            int capSteps,
            float radius,
            float halfThickness,
            float feather,
            Color startColor,
            Color endColor,
            float coreAlpha)
        {
            float inner = radius - halfThickness;
            float outer = radius + halfThickness;
            EmitBand(center, startDegrees, arcDegrees, steps, inner, outer, coreAlpha, coreAlpha, startColor, endColor);
            if (feather > 0f)
            {
                EmitBand(center, startDegrees, arcDegrees, steps, outer, outer + feather, coreAlpha, 0f, startColor, endColor);
                EmitBand(
                    center,
                    startDegrees,
                    arcDegrees,
                    steps,
                    Mathf.Max(0f, inner - feather),
                    inner,
                    0f,
                    coreAlpha,
                    startColor,
                    endColor);
            }

            EmitDisc(center + (Radial(startDegrees) * radius), halfThickness, feather, ScaleAlpha(startColor, coreAlpha), capSteps);
            EmitDisc(
                center + (Radial(startDegrees + arcDegrees) * radius),
                halfThickness,
                feather,
                ScaleAlpha(endColor, coreAlpha),
                capSteps);
        }

        private static void EmitBand(
            Vector2 center,
            float startDegrees,
            float arcDegrees,
            int steps,
            float innerRadius,
            float outerRadius,
            float innerAlpha,
            float outerAlpha,
            Color startColor,
            Color endColor)
        {
            for (int index = 0; index < steps; index++)
            {
                float t0 = index / (float)steps;
                float t1 = (index + 1f) / steps;
                Vector2 radial0 = Radial(startDegrees + (arcDegrees * t0));
                Vector2 radial1 = Radial(startDegrees + (arcDegrees * t1));
                Color color0 = Color.Lerp(startColor, endColor, t0);
                Color color1 = Color.Lerp(startColor, endColor, t1);

                GL.Color(ScaleAlpha(color0, innerAlpha));
                GL.Vertex(center + (radial0 * innerRadius));
                GL.Color(ScaleAlpha(color0, outerAlpha));
                GL.Vertex(center + (radial0 * outerRadius));
                GL.Color(ScaleAlpha(color1, outerAlpha));
                GL.Vertex(center + (radial1 * outerRadius));
                GL.Color(ScaleAlpha(color1, innerAlpha));
                GL.Vertex(center + (radial1 * innerRadius));
            }
        }

        private static void EmitDisc(Vector2 center, float radius, float feather, Color color, int steps)
        {
            if (color.a <= 0f)
            {
                return;
            }

            Color edgeColor = ScaleAlpha(color, 0f);
            for (int index = 0; index < steps; index++)
            {
                Vector2 radial0 = Radial((index / (float)steps) * 360f);
                Vector2 radial1 = Radial(((index + 1f) / steps) * 360f);

                GL.Color(color);
                GL.Vertex(center);
                GL.Vertex(center);
                GL.Vertex(center + (radial0 * radius));
                GL.Vertex(center + (radial1 * radius));

                if (feather <= 0f)
                {
                    continue;
                }

                GL.Color(color);
                GL.Vertex(center + (radial0 * radius));
                GL.Color(edgeColor);
                GL.Vertex(center + (radial0 * (radius + feather)));
                GL.Vertex(center + (radial1 * (radius + feather)));
                GL.Color(color);
                GL.Vertex(center + (radial1 * radius));
            }
        }

        private static Vector2 Radial(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static Color ScaleAlpha(Color color, float alphaScale)
        {
            color.a *= alphaScale;
            return color;
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
