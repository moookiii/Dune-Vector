using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Shared immediate-mode chrome for the gameplay HUD: glass panels, recessed meters,
    /// corner brackets and soft glows. Keeps every runtime HUD surface on one visual language.
    /// </summary>
    public static class DuneVectorHudChrome
    {
        private const int FadeResolution = 64;

        private static readonly GUIContent _glyphContent = new GUIContent();

        private static Texture2D _verticalFade;
        private static Texture2D _horizontalFade;

        private static Texture2D VerticalFade
        {
            get
            {
                if (_verticalFade == null)
                {
                    _verticalFade = CreateFade(1, FadeResolution);
                }
                return _verticalFade;
            }
        }

        private static Texture2D HorizontalFade
        {
            get
            {
                if (_horizontalFade == null)
                {
                    _horizontalFade = CreateFade(FadeResolution, 1);
                }
                return _horizontalFade;
            }
        }

        public static void DrawRect(Rect rect, Color color)
        {
            if (color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>Linear alpha ramp that reaches full strength at the top (or bottom) edge.</summary>
        public static void DrawVerticalFade(Rect rect, Color color, bool strongAtTop)
        {
            if (color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(
                rect,
                VerticalFade,
                strongAtTop ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 1f, 1f, -1f));
            GUI.color = previous;
        }

        /// <summary>Linear alpha ramp that reaches full strength at the left (or right) edge.</summary>
        public static void DrawHorizontalFade(Rect rect, Color color, bool strongAtLeft)
        {
            if (color.a <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(
                rect,
                HorizontalFade,
                strongAtLeft ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
            GUI.color = previous;
        }

        public static void DrawBorder(Rect rect, Color color, float thickness)
        {
            if (color.a <= 0f || thickness <= 0f)
            {
                return;
            }

            DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>Offset drop shadow with a few fading frames so panels lift off the sky.</summary>
        public static void DrawSoftShadow(Rect rect, Color color, Vector2 offset, float spread)
        {
            if (color.a <= 0f)
            {
                return;
            }

            Rect shadow = new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
            const int steps = 3;
            for (int index = steps; index >= 1; index--)
            {
                float grow = spread * index / steps;
                Color layer = color;
                layer.a *= 0.28f / index;
                DrawRect(
                    new Rect(shadow.x - grow, shadow.y - grow, shadow.width + (grow * 2f), shadow.height + (grow * 2f)),
                    layer);
            }
            DrawRect(shadow, color);
        }

        /// <summary>L-shaped tick marks at each corner; the signature detail of the HUD frame.</summary>
        public static void DrawCornerBrackets(Rect rect, Color color, float length, float thickness)
        {
            if (color.a <= 0f || length <= 0f || thickness <= 0f)
            {
                return;
            }

            length = Mathf.Min(length, Mathf.Min(rect.width, rect.height) * 0.45f);
            DrawRect(new Rect(rect.x, rect.y, length, thickness), color);
            DrawRect(new Rect(rect.x, rect.y, thickness, length), color);
            DrawRect(new Rect(rect.xMax - length, rect.y, length, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, length), color);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, length, thickness), color);
            DrawRect(new Rect(rect.x, rect.yMax - length, thickness, length), color);
            DrawRect(new Rect(rect.xMax - length, rect.yMax - thickness, length, thickness), color);
            DrawRect(new Rect(rect.xMax - thickness, rect.yMax - length, thickness, length), color);
        }

        /// <summary>
        /// Translucent smoked-glass body: darker toward the bottom, a thin sheen along the top,
        /// a hairline inner highlight and an outer border.
        /// </summary>
        public static void DrawGlassPanel(
            Rect rect,
            Color body,
            Color border,
            float borderThickness,
            float scale)
        {
            DrawRect(rect, body);
            DrawVerticalFade(
                new Rect(rect.x, rect.y, rect.width, rect.height * 0.5f),
                new Color(0.55f, 0.85f, 1f, 0.09f),
                true);
            DrawVerticalFade(
                new Rect(rect.x, rect.center.y, rect.width, rect.height * 0.5f),
                new Color(0f, 0.02f, 0.04f, 0.4f),
                false);

            float hairline = Mathf.Max(1f, scale);
            DrawRect(
                new Rect(rect.x + borderThickness, rect.y + borderThickness, rect.width - (borderThickness * 2f), hairline),
                new Color(1f, 1f, 1f, 0.12f));
            DrawBorder(rect, border, borderThickness);
        }

        /// <summary>Bright accent rail along the left edge with a glow that bleeds into the panel.</summary>
        public static void DrawAccentRail(Rect rect, Color accent, float width, float glowWidth)
        {
            Color glow = accent;
            glow.a *= 0.22f;
            DrawHorizontalFade(new Rect(rect.x + width, rect.y, glowWidth, rect.height), glow, true);

            Color outerGlow = accent;
            outerGlow.a *= 0.3f;
            DrawHorizontalFade(new Rect(rect.x - glowWidth * 0.5f, rect.y, glowWidth * 0.5f, rect.height), outerGlow, false);

            DrawRect(new Rect(rect.x, rect.y, width, rect.height), accent);
            DrawVerticalFade(
                new Rect(rect.x, rect.y, width, rect.height * 0.6f),
                new Color(1f, 1f, 1f, 0.35f),
                true);
        }

        /// <summary>
        /// Recessed meter: inset track with an inner shadow, gradient fill, a hot leading-edge cap
        /// that blooms into the empty track, and optional segment ticks.
        /// </summary>
        public static void DrawMeter(
            Rect track,
            float normalized,
            Color accent,
            Color trackColor,
            float inset,
            int divisions,
            Color divisionColor,
            float divisionWidth,
            float scale)
        {
            DrawRect(track, trackColor);
            DrawVerticalFade(
                new Rect(track.x, track.y, track.width, track.height * 0.65f),
                new Color(0f, 0f, 0f, 0.45f),
                true);

            Rect bounds = new Rect(
                track.x + inset,
                track.y + inset,
                Mathf.Max(0f, track.width - (inset * 2f)),
                Mathf.Max(0f, track.height - (inset * 2f)));
            float fillWidth = bounds.width * Mathf.Clamp01(normalized);
            if (fillWidth > 0.5f && bounds.height > 0f)
            {
                Rect fill = new Rect(bounds.x, bounds.y, fillWidth, bounds.height);
                Color deep = new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, accent.a);
                DrawRect(fill, deep);
                DrawVerticalFade(fill, accent, true);
                DrawVerticalFade(
                    new Rect(fill.x, fill.y, fill.width, fill.height * 0.45f),
                    new Color(1f, 1f, 1f, 0.3f),
                    true);

                float capWidth = Mathf.Max(1.5f, 2f * scale);
                if (fill.width > capWidth)
                {
                    DrawRect(
                        new Rect(fill.xMax - capWidth, fill.y, capWidth, fill.height),
                        Color.Lerp(accent, Color.white, 0.6f));
                }

                Color bloom = accent;
                bloom.a *= 0.35f;
                DrawHorizontalFade(
                    new Rect(fill.xMax, track.y, Mathf.Min(bounds.xMax - fill.xMax, 14f * scale), track.height),
                    bloom,
                    true);
            }

            if (divisions > 1)
            {
                float width = Mathf.Max(0.5f, divisionWidth);
                for (int index = 1; index < divisions; index++)
                {
                    float x = track.x + (track.width * index / divisions);
                    DrawRect(new Rect(x - (width * 0.5f), track.y, width, track.height), divisionColor);
                }
            }

            Color rim = accent;
            rim.a *= 0.35f;
            DrawBorder(track, rim, Mathf.Max(1f, scale));
        }

        public static void DrawLabel(
            Rect rect,
            string text,
            GUIStyle style,
            Color textColor,
            Color shadowColor,
            Vector2 shadowOffset)
        {
            Color previous = GUI.color;
            if (shadowColor.a > 0f)
            {
                GUI.color = shadowColor;
                GUI.Label(new Rect(rect.x + shadowOffset.x, rect.y + shadowOffset.y, rect.width, rect.height), text, style);
            }
            GUI.color = textColor;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        /// <summary>
        /// Headline text with a cheap four-tap halo. Reserved for the one readout a panel wants
        /// the eye to land on; overuse turns the HUD to mush.
        /// </summary>
        public static void DrawGlowLabel(
            Rect rect,
            string text,
            GUIStyle style,
            Color textColor,
            Color glowColor,
            float radius,
            Color shadowColor,
            Vector2 shadowOffset)
        {
            Color previous = GUI.color;
            if (shadowColor.a > 0f)
            {
                GUI.color = shadowColor;
                GUI.Label(new Rect(rect.x + shadowOffset.x, rect.y + shadowOffset.y, rect.width, rect.height), text, style);
            }

            if (glowColor.a > 0f && radius > 0f)
            {
                GUI.color = glowColor;
                GUI.Label(new Rect(rect.x - radius, rect.y, rect.width, rect.height), text, style);
                GUI.Label(new Rect(rect.x + radius, rect.y, rect.width, rect.height), text, style);
                GUI.Label(new Rect(rect.x, rect.y - radius, rect.width, rect.height), text, style);
                GUI.Label(new Rect(rect.x, rect.y + radius, rect.width, rect.height), text, style);
            }

            GUI.color = textColor;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        /// <summary>
        /// Headline text with letter spacing, an optional halo and a drop shadow. IMGUI has no
        /// tracking, so the string is laid out one glyph at a time; keep it to short headings.
        /// </summary>
        public static void DrawTrackedLabel(
            Rect rect,
            string text,
            GUIStyle style,
            float tracking,
            Color textColor,
            Color glowColor,
            float glowRadius,
            Color shadowColor,
            Vector2 shadowOffset)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (tracking <= 0.01f || text.Length < 2)
            {
                DrawGlowLabel(rect, text, style, textColor, glowColor, glowRadius, shadowColor, shadowOffset);
                return;
            }

            float[] widths = new float[text.Length];
            float totalWidth = tracking * (text.Length - 1);
            for (int index = 0; index < text.Length; index++)
            {
                _glyphContent.text = text[index].ToString();
                widths[index] = style.CalcSize(_glyphContent).x;
                totalWidth += widths[index];
            }

            TextAnchor previousAlignment = style.alignment;
            TextClipping previousClipping = style.clipping;
            Color previousColor = GUI.color;

            float startX = rect.x;
            if (previousAlignment == TextAnchor.MiddleCenter
                || previousAlignment == TextAnchor.UpperCenter
                || previousAlignment == TextAnchor.LowerCenter)
            {
                startX = rect.x + ((rect.width - totalWidth) * 0.5f);
            }
            else if (previousAlignment == TextAnchor.MiddleRight
                || previousAlignment == TextAnchor.UpperRight
                || previousAlignment == TextAnchor.LowerRight)
            {
                startX = rect.xMax - totalWidth;
            }

            // Left alignment is what keeps each glyph pinned to the advance we measured for it;
            // any centring here would re-centre every glyph inside its own one-character rect.
            style.alignment = previousAlignment == TextAnchor.UpperCenter
                || previousAlignment == TextAnchor.UpperLeft
                || previousAlignment == TextAnchor.UpperRight
                ? TextAnchor.UpperLeft
                : previousAlignment == TextAnchor.LowerCenter
                    || previousAlignment == TextAnchor.LowerLeft
                    || previousAlignment == TextAnchor.LowerRight
                    ? TextAnchor.LowerLeft
                    : TextAnchor.MiddleLeft;
            style.clipping = TextClipping.Overflow;

            if (shadowColor.a > 0f)
            {
                GUI.color = shadowColor;
                DrawGlyphRun(text, widths, style, tracking, startX + shadowOffset.x, rect.y + shadowOffset.y, rect.height);
            }

            if (glowColor.a > 0f && glowRadius > 0f)
            {
                GUI.color = glowColor;
                DrawGlyphRun(text, widths, style, tracking, startX - glowRadius, rect.y, rect.height);
                DrawGlyphRun(text, widths, style, tracking, startX + glowRadius, rect.y, rect.height);
                DrawGlyphRun(text, widths, style, tracking, startX, rect.y - glowRadius, rect.height);
                DrawGlyphRun(text, widths, style, tracking, startX, rect.y + glowRadius, rect.height);
            }

            GUI.color = textColor;
            DrawGlyphRun(text, widths, style, tracking, startX, rect.y, rect.height);

            GUI.color = previousColor;
            style.alignment = previousAlignment;
            style.clipping = previousClipping;
        }

        private static void DrawGlyphRun(
            string text,
            float[] widths,
            GUIStyle style,
            float tracking,
            float x,
            float y,
            float height)
        {
            for (int index = 0; index < text.Length; index++)
            {
                _glyphContent.text = text[index].ToString();
                // The extra pixel absorbs the rounding CalcSize does, so wide glyphs are not
                // clipped by their own rect.
                GUI.Label(new Rect(x, y, widths[index] + 1f, height), _glyphContent, style);
                x += widths[index] + tracking;
            }
        }

        private static Texture2D CreateFade(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false)
            {
                name = "DuneVectorHudFade",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // Column 0 is the left edge of the drawn rect and row 0 its bottom edge, so the
            // ramp reaches full strength at the right edge (horizontal) or top edge (vertical).
            bool horizontal = width > height;
            int count = Mathf.Max(width, height);
            for (int index = 0; index < count; index++)
            {
                Color pixel = new Color(1f, 1f, 1f, index / Mathf.Max(1f, count - 1f));
                if (horizontal)
                {
                    texture.SetPixel(index, 0, pixel);
                }
                else
                {
                    texture.SetPixel(0, index, pixel);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
