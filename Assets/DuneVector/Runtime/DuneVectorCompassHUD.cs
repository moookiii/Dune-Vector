using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorCompassHUD : MonoBehaviour
    {
        private Camera _camera;
        private CompassHudTuning _settings;
        private GUIStyle _labelStyle;

        public void Initialize(Camera gameplayCamera, CompassHudTuning settings)
        {
            _camera = gameplayCamera;
            _settings = settings;
        }

        private void OnGUI()
        {
            if (_camera == null
                || _settings == null
                || !_settings.Enabled
                || DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }

            float scale = GetScale();
            float width = Mathf.Min(_settings.Width * scale, Screen.safeArea.width);
            float height = _settings.Height * scale;
            float left = Screen.safeArea.x + ((Screen.safeArea.width - width) * 0.5f);
            float top = (Screen.height - Screen.safeArea.yMax) + (_settings.TopMargin * scale);
            Rect compassRect = new Rect(left, top, width, height);

            DrawSolidRect(compassRect, _settings.PanelColor);
            GUI.BeginGroup(compassRect);
            DrawHeadingRibbon(new Rect(0f, 0f, width, height), scale);
            GUI.EndGroup();
        }

        private void DrawHeadingRibbon(Rect bounds, float scale)
        {
            float visibleDegrees = Mathf.Max(_settings.TickStepDegrees, _settings.VisibleDegrees);
            float step = Mathf.Max(1f, _settings.TickStepDegrees);
            float pixelsPerDegree = bounds.width / visibleDegrees;
            float heading = Mathf.Repeat(_camera.transform.eulerAngles.y, 360f);
            float halfRange = visibleDegrees * 0.5f;
            float firstHeading = Mathf.Floor((heading - halfRange) / step) * step;
            float lastHeading = heading + halfRange;
            float baselineY = bounds.height - (_settings.TickBottomMargin * scale);
            float baselineHeight = _settings.BaselineHeight * scale;

            DrawSolidRect(
                new Rect(0f, baselineY - (baselineHeight * 0.5f), bounds.width, baselineHeight),
                _settings.TickColor);

            int tickIndex = Mathf.RoundToInt(firstHeading / step);
            for (float tickHeading = firstHeading; tickHeading <= lastHeading + step; tickHeading += step, tickIndex++)
            {
                float wrappedHeading = Mathf.Repeat(tickHeading, 360f);
                float delta = Mathf.DeltaAngle(heading, wrappedHeading);
                float x = bounds.width * 0.5f + (delta * pixelsPerDegree);
                if (x < 0f || x > bounds.width)
                {
                    continue;
                }

                bool cardinal = Mathf.Approximately(Mathf.Repeat(wrappedHeading, 90f), 0f);
                bool major = cardinal || PositiveModulo(tickIndex, Mathf.Max(1, _settings.MajorTickEvery)) == 0;
                float tickHeight = (cardinal
                    ? _settings.CardinalTickHeight
                    : major ? _settings.MajorTickHeight : _settings.MinorTickHeight) * scale;
                Color tickColor = cardinal ? _settings.CardinalColor : _settings.TickColor;
                Rect tickRect = new Rect(
                    x - ((_settings.TickWidth * scale) * 0.5f),
                    baselineY - tickHeight,
                    _settings.TickWidth * scale,
                    tickHeight);
                DrawShadowedRect(tickRect, tickColor, scale);

                string label = GetHeadingLabel(wrappedHeading, cardinal);
                if (!string.IsNullOrEmpty(label))
                {
                    EnsureLabelStyle(scale, cardinal ? _settings.CardinalColor : _settings.TickColor);
                    Rect labelRect = new Rect(
                        x - ((_settings.LabelWidth * scale) * 0.5f),
                        baselineY - tickHeight - (_settings.LabelHeight * scale),
                        _settings.LabelWidth * scale,
                        _settings.LabelHeight * scale);
                    DrawShadowedLabel(labelRect, label, scale);
                }
            }

            float markerWidth = _settings.CenterMarkerWidth * scale;
            float markerHeight = _settings.CenterMarkerHeight * scale;
            Rect marker = new Rect(
                (bounds.width - markerWidth) * 0.5f,
                baselineY - markerHeight,
                markerWidth,
                markerHeight);
            DrawShadowedRect(marker, _settings.CenterMarkerColor, scale);
        }

        private string GetHeadingLabel(float heading, bool cardinal)
        {
            if (cardinal)
            {
                int quadrant = Mathf.RoundToInt(heading / 90f) % 4;
                return quadrant switch
                {
                    0 => _settings.NorthLabel,
                    1 => _settings.EastLabel,
                    2 => _settings.SouthLabel,
                    _ => _settings.WestLabel,
                };
            }

            if (!_settings.ShowIntercardinalLabels
                || !Mathf.Approximately(Mathf.Repeat(heading - 45f, 90f), 0f))
            {
                return string.Empty;
            }

            int intercardinal = Mathf.RoundToInt((heading - 45f) / 90f) % 4;
            return intercardinal switch
            {
                0 => _settings.NorthEastLabel,
                1 => _settings.SouthEastLabel,
                2 => _settings.SouthWestLabel,
                _ => _settings.NorthWestLabel,
            };
        }

        private void EnsureLabelStyle(float scale, Color color)
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _labelStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(_settings.LabelFontSize * scale));
            _labelStyle.normal.textColor = color;
        }

        private void DrawShadowedLabel(Rect rect, string text, float scale)
        {
            Color previousColor = GUI.color;
            Rect shadowRect = rect;
            shadowRect.position += _settings.ShadowOffset * scale;
            GUI.color = _settings.ShadowColor;
            GUI.Label(shadowRect, text, _labelStyle);
            GUI.color = Color.white;
            GUI.Label(rect, text, _labelStyle);
            GUI.color = previousColor;
        }

        private void DrawShadowedRect(Rect rect, Color color, float scale)
        {
            Rect shadowRect = rect;
            shadowRect.position += _settings.ShadowOffset * scale;
            DrawSolidRect(shadowRect, _settings.ShadowColor);
            DrawSolidRect(rect, color);
        }

        private float GetScale()
        {
            float minimumScale = Mathf.Min(_settings.MinimumScale, _settings.MaximumScale);
            float maximumScale = Mathf.Max(_settings.MinimumScale, _settings.MaximumScale);
            return Mathf.Clamp(
                Screen.height / Mathf.Max(1f, _settings.ReferenceHeight),
                minimumScale,
                maximumScale);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            return ((value % modulus) + modulus) % modulus;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
