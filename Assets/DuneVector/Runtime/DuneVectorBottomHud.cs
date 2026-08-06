using UnityEngine;

namespace DuneVector
{
    public enum DuneVectorBottomHudPanel
    {
        Speed,
        FlightReserve,
        Health,
    }

    public static class DuneVectorBottomHud
    {
        private static GUIStyle _labelStyle;
        private static GUIStyle _valueStyle;

        public static Rect GetPanelRect(BottomHudTuning settings, DuneVectorBottomHudPanel panel)
        {
            float scale = GetScale(settings);
            Rect safeArea = Screen.safeArea;
            float width = GetPanelWidth(settings, panel) * scale;
            float height = settings.PanelHeight * scale;
            float sideMargin = settings.SideMargin * scale;
            float bottomMargin = settings.BottomMargin * scale;
            float y = Screen.height - safeArea.yMin - bottomMargin - height;

            float x;
            switch (panel)
            {
                case DuneVectorBottomHudPanel.Speed:
                    x = safeArea.xMin + sideMargin;
                    break;
                case DuneVectorBottomHudPanel.Health:
                    x = safeArea.xMax - sideMargin - width;
                    break;
                default:
                    x = safeArea.center.x - (width * 0.5f);
                    break;
            }

            return new Rect(x, y, width, height);
        }

        public static void DrawMeterPanel(
            Rect panel,
            string label,
            string value,
            float normalizedValue,
            Color accentColor,
            BottomHudTuning settings)
        {
            if (Event.current.type != EventType.Repaint)
            {
                EnsureStyles(settings);
                DrawLabels(panel, label, value, settings);
                return;
            }

            float scale = GetScale(settings);
            float border = Mathf.Max(1f, settings.BorderThickness * scale);
            float accentWidth = Mathf.Max(1f, settings.AccentWidth * scale);
            float topRuleHeight = Mathf.Max(1f, settings.TopRuleHeight * scale);
            float padding = settings.ContentPadding * scale;
            float meterHeight = Mathf.Max(1f, settings.MeterHeight * scale);
            float meterInset = Mathf.Max(0f, settings.MeterInset * scale);

            DuneVectorHudChrome.DrawSoftShadow(
                panel,
                settings.ShadowColor,
                settings.ShadowOffset * scale,
                5f * scale);

            Color borderColor = Color.Lerp(settings.BorderColor, accentColor, 0.32f);
            borderColor.a = settings.BorderColor.a;
            DuneVectorHudChrome.DrawGlassPanel(panel, settings.PanelColor, borderColor, border, scale);
            DuneVectorHudChrome.DrawAccentRail(panel, accentColor, accentWidth, 30f * scale);

            Color topRuleColor = accentColor;
            topRuleColor.a *= settings.TopRuleOpacity;
            DrawSolidRect(
                new Rect(panel.x + accentWidth, panel.y, panel.width - accentWidth, topRuleHeight),
                topRuleColor);
            Color topRuleGlow = accentColor;
            topRuleGlow.a *= settings.TopRuleOpacity * 0.4f;
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(panel.x + accentWidth, panel.y + topRuleHeight, panel.width - accentWidth, 10f * scale),
                topRuleGlow,
                true);

            Color bracketColor = accentColor;
            bracketColor.a *= 0.6f;
            DuneVectorHudChrome.DrawCornerBrackets(panel, bracketColor, 11f * scale, border);

            float meterY = panel.yMax - (settings.MeterBottomPadding * scale) - meterHeight;
            Rect meter = new Rect(
                panel.x + padding,
                meterY,
                panel.width - (padding * 2f),
                meterHeight);
            DuneVectorHudChrome.DrawMeter(
                meter,
                normalizedValue,
                accentColor,
                settings.TrackColor,
                meterInset,
                settings.MeterDivisionCount,
                settings.MeterDivisionColor,
                Mathf.Max(0.5f, settings.MeterDivisionWidth * scale),
                scale);

            EnsureStyles(settings);
            DrawLabels(panel, label, value, settings);
        }

        public static float GetScale(BottomHudTuning settings)
        {
            float widthScale = Screen.width / Mathf.Max(1f, settings.ReferenceWidth);
            float heightScale = Screen.height / Mathf.Max(1f, settings.ReferenceHeight);
            float minimum = Mathf.Min(settings.MinimumScale, settings.MaximumScale);
            float maximum = Mathf.Max(settings.MinimumScale, settings.MaximumScale);
            float scale = Mathf.Clamp(Mathf.Min(widthScale, heightScale), minimum, maximum);
            float requiredWidth =
                (settings.SideMargin * 2f)
                + settings.SpeedPanelWidth
                + settings.FlightPanelWidth
                + settings.HealthPanelWidth
                + (settings.MinimumPanelGap * 2f);
            float fitScale = Screen.safeArea.width / Mathf.Max(1f, requiredWidth);
            return Mathf.Min(scale, fitScale);
        }

        private static float GetPanelWidth(BottomHudTuning settings, DuneVectorBottomHudPanel panel)
        {
            switch (panel)
            {
                case DuneVectorBottomHudPanel.Speed:
                    return settings.SpeedPanelWidth;
                case DuneVectorBottomHudPanel.Health:
                    return settings.HealthPanelWidth;
                default:
                    return settings.FlightPanelWidth;
            }
        }

        private static void EnsureStyles(BottomHudTuning settings)
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _valueStyle ??= new GUIStyle(_labelStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };

            float scale = GetScale(settings);
            _labelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(settings.LabelFontSize * scale));
            _labelStyle.normal.textColor = settings.LabelColor;
            _valueStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(settings.ValueFontSize * scale));
            _valueStyle.normal.textColor = settings.ValueColor;
        }

        private static void DrawLabels(
            Rect panel,
            string label,
            string value,
            BottomHudTuning settings)
        {
            float scale = GetScale(settings);
            float padding = settings.ContentPadding * scale;
            float accentWidth = Mathf.Max(1f, settings.AccentWidth * scale);
            float rowHeight = settings.TextRowHeight * scale;
            Rect row = new Rect(
                panel.x + padding + accentWidth,
                panel.y + (settings.TextTopPadding * scale),
                panel.width - (padding * 2f) - accentWidth,
                rowHeight);
            float labelWidth = row.width * settings.LabelWidthFraction;
            Vector2 textShadow = new Vector2(1f, 1f) * scale;
            Color textShadowColor = new Color(0f, 0f, 0f, 0.55f);
            DuneVectorHudChrome.DrawLabel(
                new Rect(row.x, row.y, labelWidth, row.height),
                label,
                _labelStyle,
                Color.white,
                textShadowColor,
                textShadow);
            DuneVectorHudChrome.DrawLabel(
                new Rect(row.x + labelWidth, row.y, row.width - labelWidth, row.height),
                value,
                _valueStyle,
                Color.white,
                textShadowColor,
                textShadow);
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
