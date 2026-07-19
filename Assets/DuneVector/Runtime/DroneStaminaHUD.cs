using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DroneStaminaHUD : MonoBehaviour
    {
        private DroneCharacterController _drone;
        private Camera _camera;
        private DroneStaminaSystem _stamina;
        private StaminaBoostTuning _settings;
        private GUIStyle _labelStyle;
        private DroneStaminaState _previousState;
        private float _visibleAlpha;
        private float _fullIdleTime;
        private float _restoredFeedbackRemaining;

        public void Initialize(
            DroneCharacterController drone,
            Camera worldCamera,
            DroneStaminaSystem stamina,
            StaminaBoostTuning settings)
        {
            _drone = drone;
            _camera = worldCamera;
            _stamina = stamina;
            _settings = settings;
            _previousState = stamina != null ? stamina.State : DroneStaminaState.Ready;
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
        }

        private void OnGUI()
        {
            if (_drone == null || _camera == null || _stamina == null || _settings == null || _visibleAlpha <= 0f)
            {
                return;
            }

            Vector3 viewportPosition = _camera.WorldToScreenPoint(_drone.WorldCenter);
            if (viewportPosition.z <= 0f)
            {
                return;
            }

            float padding = Mathf.Max(0f, _settings.ScreenEdgePadding);
            Vector2 center = new Vector2(
                Mathf.Clamp(viewportPosition.x + _settings.MeterScreenOffset.x, padding, Screen.width - padding),
                Mathf.Clamp(Screen.height - viewportPosition.y + _settings.MeterScreenOffset.y, padding, Screen.height - padding));

            int segments = Mathf.Max(1, _settings.MeterSegments);
            float stamina01 = _stamina.NormalizedStamina;
            float filledSegments = stamina01 * segments;
            Color meterColor = GetMeterColor(stamina01);
            float pulse = GetPulse();

            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            for (int index = 0; index < segments; index++)
            {
                float segment01 = segments > 1 ? index / (float)(segments - 1) : 0f;
                float angle = _settings.MeterArcStartDegrees + (_settings.MeterArcDegrees * segment01);
                float radians = angle * Mathf.Deg2Rad;
                Vector2 radial = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                Vector2 segmentCenter = center + (radial * _settings.MeterRadius * pulse);
                float segmentLength = Mathf.Max(
                    _settings.MeterThickness,
                    (Mathf.Deg2Rad * _settings.MeterArcDegrees * _settings.MeterRadius / segments) * 0.72f);
                Rect segmentRect = new Rect(
                    segmentCenter.x - (segmentLength * 0.5f),
                    segmentCenter.y - (_settings.MeterThickness * 0.5f),
                    segmentLength,
                    _settings.MeterThickness);

                GUIUtility.RotateAroundPivot(angle + 90f, segmentCenter);
                Color color = index < filledSegments ? meterColor : _settings.MeterBackgroundColor;
                color.a *= _visibleAlpha;
                GUI.color = color;
                GUI.DrawTexture(segmentRect, Texture2D.whiteTexture);
                GUI.matrix = previousMatrix;
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;

            string label = GetLabel(stamina01);
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            EnsureLabelStyle();
            Color labelColor = meterColor;
            labelColor.a *= _visibleAlpha;
            _labelStyle.normal.textColor = labelColor;
            Rect labelRect = new Rect(
                center.x + _settings.MeterLabelOffset.x,
                center.y + _settings.MeterLabelOffset.y,
                _settings.MeterLabelWidth,
                _settings.MeterLabelHeight);
            GUI.Label(labelRect, label, _labelStyle);
        }

        private Color GetMeterColor(float stamina01)
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

        private string GetLabel(float stamina01)
        {
            if (_restoredFeedbackRemaining > 0f)
            {
                return _settings.RestoredLabel;
            }
            if (_stamina.State == DroneStaminaState.Exhausted)
            {
                return _settings.EmptyLabel;
            }
            if (_stamina.State == DroneStaminaState.Regenerating)
            {
                return _settings.RegeneratingLabel;
            }
            if (stamina01 <= _settings.LowStaminaThreshold)
            {
                return _settings.LowLabel;
            }
            return string.Empty;
        }

        private float GetPulse()
        {
            bool pulse = _stamina.State == DroneStaminaState.Exhausted
                || _stamina.NormalizedStamina <= _settings.LowStaminaThreshold
                || _restoredFeedbackRemaining > 0f;
            if (!pulse)
            {
                return 1f;
            }
            return 1f + (Mathf.Sin(Time.unscaledTime * _settings.FeedbackPulseSpeed) * _settings.FeedbackPulseAmount);
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyle != null)
            {
                return;
            }
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Max(8, _settings.MeterLabelFontSize),
                clipping = TextClipping.Overflow,
            };
        }
    }
}
