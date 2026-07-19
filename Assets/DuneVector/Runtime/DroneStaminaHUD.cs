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
        private Material _arcMaterial;
        private Vector2 _screenCenter;
        private bool _hasScreenCenter;

        public void Initialize(
            DroneCharacterController drone,
            Camera worldCamera,
            DroneStaminaSystem stamina,
            StaminaBoostTuning settings)
        {
            _drone = drone;
            _health = drone != null ? drone.GetComponent<DroneHealth>() : null;
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

        private void LateUpdate()
        {
            if (_drone == null || _camera == null || _settings == null)
            {
                _hasScreenCenter = false;
                return;
            }

            Vector3 screenPosition = _camera.WorldToScreenPoint(_drone.WorldCenter);
            if (screenPosition.z <= 0f)
            {
                _hasScreenCenter = false;
                return;
            }

            float padding = Mathf.Max(0f, _settings.ScreenEdgePadding);
            Vector2 targetCenter = new Vector2(
                Mathf.Clamp(screenPosition.x + _settings.MeterScreenOffset.x, padding, Screen.width - padding),
                Mathf.Clamp(Screen.height - screenPosition.y + _settings.MeterScreenOffset.y, padding, Screen.height - padding));
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
            if (_stamina == null || _settings == null || !_hasScreenCenter || _visibleAlpha <= 0f)
            {
                return;
            }

            Vector2 center = _screenCenter;

            float stamina01 = _stamina.NormalizedStamina;
            Color meterColor = GetMeterColor(stamina01);

            if (Event.current.type == EventType.Repaint)
            {
                Color backgroundColor = _settings.MeterBackgroundColor;
                backgroundColor.a *= _visibleAlpha;
                meterColor.a *= _visibleAlpha;

                DrawBackgroundIcon(center, backgroundColor);
                if (!EnsureArcMaterial())
                {
                    return;
                }

                float filledDegrees = _settings.MeterArcDegrees * stamina01;
                float filledStartDegrees = _settings.MeterArcStartDegrees
                    + (_settings.MeterArcDegrees - filledDegrees);
                DrawContinuousArc(center, filledStartDegrees, filledDegrees, meterColor);
            }
        }

        private void DrawBackgroundIcon(Vector2 center, Color tint)
        {
            if (_settings.MeterBackgroundIcon == null || tint.a <= 0f)
            {
                return;
            }

            float size = Mathf.Max(1f, _settings.MeterBackgroundIconSize);
            Rect destination = new Rect(
                center.x - (size * 0.5f),
                center.y - (size * 0.5f),
                size,
                size);
            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(destination, _settings.MeterBackgroundIcon, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
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
            if (_arcMaterial != null)
            {
                Destroy(_arcMaterial);
            }
        }
    }
}
