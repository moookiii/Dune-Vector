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
            // This overlay only draws; it owns no controls and mutates no state. Running the
            // layout pass would repeat every measurement for nothing, so only Repaint does work.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // The ribbon is a field tool the courier is awarded, not standard equipment, so it
            // stays off the screen entirely until its hub award card has been held through.
            if (_camera == null
                || _settings == null
                || !_settings.Enabled
                || !DuneVectorToolUnlocks.IsUnlocked(DuneVectorToolUnlockId.Compass)
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

            DrawPanelBackdrop(compassRect, scale);
            GUI.BeginGroup(compassRect);
            DrawHeadingRibbon(new Rect(0f, 0f, width, height), scale);
            GUI.EndGroup();
        }

        /// <summary>
        /// The ribbon has no hard frame: the smoked backing and its rules bloom out from the
        /// center marker and dissolve before they reach the screen edges.
        /// </summary>
        private void DrawPanelBackdrop(Rect rect, float scale)
        {
            float half = rect.width * 0.5f;
            Rect leftHalf = new Rect(rect.x, rect.y, half, rect.height);
            Rect rightHalf = new Rect(rect.center.x, rect.y, half, rect.height);

            DuneVectorHudChrome.DrawHorizontalFade(leftHalf, _settings.PanelColor, false);
            DuneVectorHudChrome.DrawHorizontalFade(rightHalf, _settings.PanelColor, true);

            Color sheen = new Color(0.55f, 0.85f, 1f, 0.05f);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(rect.x, rect.y, rect.width, rect.height * 0.55f),
                sheen,
                true);

            float ruleHeight = Mathf.Max(1f, scale);
            Color rule = _settings.CenterMarkerColor;
            rule.a *= 0.35f;
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(leftHalf.x, rect.y, leftHalf.width, ruleHeight),
                rule,
                false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(rightHalf.x, rect.y, rightHalf.width, ruleHeight),
                rule,
                true);
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

            float half = bounds.width * 0.5f;
            Rect baseline = new Rect(0f, baselineY - (baselineHeight * 0.5f), bounds.width, baselineHeight);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(baseline.x, baseline.y, half, baseline.height),
                _settings.TickColor,
                false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(half, baseline.y, half, baseline.height),
                _settings.TickColor,
                true);

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
                float edgeFade = EvaluateEdgeFade(x, bounds.width);
                Color tickColor = cardinal ? _settings.CardinalColor : _settings.TickColor;
                tickColor.a *= edgeFade * (major ? 1f : 0.7f);
                Rect tickRect = new Rect(
                    x - ((_settings.TickWidth * scale) * 0.5f),
                    baselineY - tickHeight,
                    _settings.TickWidth * scale,
                    tickHeight);
                DrawShadowedRect(tickRect, tickColor, scale, edgeFade);

                string label = GetHeadingLabel(wrappedHeading, cardinal);
                if (!string.IsNullOrEmpty(label) && edgeFade > 0.01f)
                {
                    Color labelColor = cardinal ? _settings.CardinalColor : _settings.TickColor;
                    labelColor.a *= edgeFade;
                    EnsureLabelStyle(scale, labelColor);
                    Rect labelRect = new Rect(
                        x - ((_settings.LabelWidth * scale) * 0.5f),
                        baselineY - tickHeight - (_settings.LabelHeight * scale),
                        _settings.LabelWidth * scale,
                        _settings.LabelHeight * scale);
                    if (cardinal)
                    {
                        // Cardinals get a soft pill so they stay readable over bright sky.
                        Color plate = _settings.PanelColor;
                        plate.a *= 0.85f * edgeFade;
                        float plateWidth = _settings.LabelFontSize * scale * (label.Length + 1.1f) * 0.62f;
                        DuneVectorHudChrome.DrawRect(
                            new Rect(
                                labelRect.center.x - (plateWidth * 0.5f),
                                labelRect.y + (labelRect.height * 0.16f),
                                plateWidth,
                                labelRect.height * 0.68f),
                            plate);
                    }
                    DrawShadowedLabel(labelRect, label, scale, edgeFade);
                }
            }

            DrawCenterMarker(bounds, baselineY, scale);
        }

        /// <summary>Downward chevron over a needle, so the read point is unmistakable.</summary>
        private void DrawCenterMarker(Rect bounds, float baselineY, float scale)
        {
            float markerWidth = _settings.CenterMarkerWidth * scale;
            float markerHeight = _settings.CenterMarkerHeight * scale;
            float centerX = bounds.width * 0.5f;

            Color glow = _settings.CenterMarkerColor;
            glow.a *= 0.28f;
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(centerX - (28f * scale), baselineY - markerHeight, 28f * scale, markerHeight),
                glow,
                false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(centerX, baselineY - markerHeight, 28f * scale, markerHeight),
                glow,
                true);

            Rect needle = new Rect(
                centerX - (markerWidth * 0.5f),
                baselineY - markerHeight,
                markerWidth,
                markerHeight);
            DrawShadowedRect(needle, _settings.CenterMarkerColor, scale, 1f);

            float chevronWidth = Mathf.Max(6f, 9f * scale);
            float chevronHeight = Mathf.Max(4f, 7f * scale);
            float rowHeight = Mathf.Max(1f, scale);
            int rows = Mathf.Max(2, Mathf.RoundToInt(chevronHeight / rowHeight));
            float chevronTop = baselineY - markerHeight - chevronHeight - (2f * scale);
            for (int row = 0; row < rows; row++)
            {
                float t = row / (float)(rows - 1);
                float rowWidth = Mathf.Lerp(chevronWidth, rowHeight, t);
                DuneVectorHudChrome.DrawRect(
                    new Rect(
                        centerX - (rowWidth * 0.5f),
                        chevronTop + (row * rowHeight),
                        rowWidth,
                        rowHeight),
                    _settings.CenterMarkerColor);
            }
        }

        /// <summary>Ticks dissolve toward the ribbon edges instead of being cut off mid-stroke.</summary>
        private static float EvaluateEdgeFade(float x, float width)
        {
            float distance = Mathf.Abs(x - (width * 0.5f)) / Mathf.Max(1f, width * 0.5f);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, distance));
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

        private void DrawShadowedLabel(Rect rect, string text, float scale, float opacity)
        {
            Color shadow = _settings.ShadowColor;
            shadow.a *= opacity;
            DuneVectorHudChrome.DrawLabel(
                rect,
                text,
                _labelStyle,
                Color.white,
                shadow,
                _settings.ShadowOffset * scale);
        }

        private void DrawShadowedRect(Rect rect, Color color, float scale, float opacity)
        {
            Rect shadowRect = rect;
            shadowRect.position += _settings.ShadowOffset * scale;
            Color shadow = _settings.ShadowColor;
            shadow.a *= opacity;
            DrawSolidRect(shadowRect, shadow);
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
