using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Shared presentation for the short-lived "restored" banners (stamina, health). Holds the
    /// text at full opacity, then pops, rises and fades it out, drawing an outline and a drop
    /// shadow underneath so it stays legible over bright sky and sand.
    /// </summary>
    public struct HudRestoreNotificationStyle
    {
        public float HoldFraction;
        public float Rise;
        public float PopScale;
        public float PopFraction;
        public float OutlineThickness;
        public Color OutlineColor;
        public Vector2 ShadowOffset;
        public Color ShadowColor;
        public float ShadowLifetimeFraction;
        public Color TextColor;
    }

    public static class HudRestoreNotification
    {
        /// <summary>
        /// Draws <paramref name="text"/> centred in <paramref name="rect"/> for a banner that is
        /// <paramref name="life01"/> of the way through its lifetime.
        /// </summary>
        public static void Draw(
            GUIStyle guiStyle,
            Rect rect,
            string text,
            float life01,
            in HudRestoreNotificationStyle style)
        {
            life01 = Mathf.Clamp01(life01);
            float hold = Mathf.Clamp01(style.HoldFraction);
            float fade = life01 <= hold
                ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (life01 - hold) / Mathf.Max(0.0001f, 1f - hold));
            if (fade <= 0f)
            {
                return;
            }

            rect.y -= style.Rise * EaseOut(life01);

            float popFraction = Mathf.Clamp01(style.PopFraction);
            float pop = popFraction <= 0f
                ? 1f
                : Mathf.Lerp(
                    Mathf.Max(1f, style.PopScale),
                    1f,
                    EaseOut(Mathf.Clamp01(life01 / popFraction)));

            Matrix4x4 previousMatrix = GUI.matrix;
            if (!Mathf.Approximately(pop, 1f))
            {
                GUIUtility.ScaleAroundPivot(
                    new Vector2(pop, pop),
                    new Vector2(rect.x + (rect.width * 0.5f), rect.y + (rect.height * 0.5f)));
            }

            if (style.ShadowOffset.sqrMagnitude > 0f)
            {
                // The shadow is retired early so the text finishes fading on its own.
                float shadowLife = Mathf.Clamp01(style.ShadowLifetimeFraction);
                float shadowFade = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((life01 - hold) / Mathf.Max(0.0001f, shadowLife - hold)));
                DrawText(
                    guiStyle,
                    new Rect(
                        rect.x + style.ShadowOffset.x,
                        rect.y + style.ShadowOffset.y,
                        rect.width,
                        rect.height),
                    text,
                    ScaleAlpha(style.ShadowColor, Mathf.Min(fade, shadowFade)));
            }

            DrawOutlined(
                guiStyle,
                rect,
                text,
                ScaleAlpha(style.TextColor, fade),
                style.OutlineThickness,
                ScaleAlpha(style.OutlineColor, fade));
            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Draws <paramref name="text"/> with an even outline ringing it, so HUD copy stays legible
        /// over bright sky and sand without needing a backing plate.
        /// </summary>
        public static void DrawOutlined(
            GUIStyle guiStyle,
            Rect rect,
            string text,
            Color textColor,
            float outlineThickness,
            Color outlineColor)
        {
            float outline = Mathf.Max(0f, outlineThickness);
            if (outline > 0f && outlineColor.a > 0f)
            {
                float diagonal = outline * 0.7071f;
                DrawOffset(guiStyle, rect, text, outlineColor, outline, 0f);
                DrawOffset(guiStyle, rect, text, outlineColor, -outline, 0f);
                DrawOffset(guiStyle, rect, text, outlineColor, 0f, outline);
                DrawOffset(guiStyle, rect, text, outlineColor, 0f, -outline);
                DrawOffset(guiStyle, rect, text, outlineColor, diagonal, diagonal);
                DrawOffset(guiStyle, rect, text, outlineColor, diagonal, -diagonal);
                DrawOffset(guiStyle, rect, text, outlineColor, -diagonal, diagonal);
                DrawOffset(guiStyle, rect, text, outlineColor, -diagonal, -diagonal);
            }

            DrawText(guiStyle, rect, text, textColor);
        }

        private static void DrawOffset(
            GUIStyle guiStyle,
            Rect rect,
            string text,
            Color color,
            float offsetX,
            float offsetY)
        {
            DrawText(
                guiStyle,
                new Rect(rect.x + offsetX, rect.y + offsetY, rect.width, rect.height),
                text,
                color);
        }

        private static void DrawText(GUIStyle guiStyle, Rect rect, string text, Color color)
        {
            if (color.a <= 0f)
            {
                return;
            }

            guiStyle.normal.textColor = color;
            GUI.Label(rect, text, guiStyle);
        }

        private static float EaseOut(float t)
        {
            float inverse = 1f - Mathf.Clamp01(t);
            return 1f - (inverse * inverse * inverse);
        }

        private static Color ScaleAlpha(Color color, float alphaScale)
        {
            color.a *= alphaScale;
            return color;
        }
    }
}
