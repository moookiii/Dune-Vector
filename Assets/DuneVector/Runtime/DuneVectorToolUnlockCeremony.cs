using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    /// <summary>Field tools the courier earns from contract milestones.</summary>
    public enum DuneVectorToolUnlockId
    {
        Compass = 0,
        AtlasFinder = 1,
    }

    /// <summary>
    /// Which field tools the courier currently owns. The HUD surfaces these gate are built
    /// before the courier game exists, so they read the grant from here instead of holding a
    /// reference to saved progress. Everything starts granted so scenes that never run
    /// contracts - the title screen and headless training - keep their full HUD.
    /// </summary>
    public static class DuneVectorToolUnlocks
    {
        private const int ToolCount = 2;
        private static readonly bool[] Granted = CreateGrantedDefaults();
        private static readonly int[] RequiredContracts = new int[ToolCount];

        public static bool IsUnlocked(DuneVectorToolUnlockId tool)
        {
            return Granted[(int)tool];
        }

        /// <summary>Completed contracts the tool costs, for locked-state copy.</summary>
        public static int GetRequiredContracts(DuneVectorToolUnlockId tool)
        {
            return RequiredContracts[(int)tool];
        }

        public static void Configure(DuneVectorToolUnlockId tool, int requiredContracts, bool granted)
        {
            RequiredContracts[(int)tool] = Mathf.Max(0, requiredContracts);
            Granted[(int)tool] = granted;
        }

        public static void Grant(DuneVectorToolUnlockId tool)
        {
            Granted[(int)tool] = true;
        }

        // Statics survive a play-mode restart whenever domain reloading is disabled, so a run
        // that locked the gates would otherwise hand that state to the next run.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            for (int index = 0; index < ToolCount; index++)
            {
                Granted[index] = true;
                RequiredContracts[index] = 0;
            }
        }

        private static bool[] CreateGrantedDefaults()
        {
            bool[] granted = new bool[ToolCount];
            for (int index = 0; index < ToolCount; index++)
            {
                granted[index] = true;
            }
            return granted;
        }
    }

    /// <summary>
    /// Full-screen award card played in the hub when a contract milestone grants a field tool.
    /// The card cannot be clicked away: the courier holds click, space or enter until the
    /// authorization meter fills, and only then is the tool granted and its HUD revealed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuneVectorToolUnlockCeremony : MonoBehaviour
    {
        private ToolUnlockCeremonyTuning _tuning;
        private DronePlayer _playerInput;
        private ToolUnlockCeremonyEntryTuning _entry;
        private DuneVectorToolUnlockId _tool;
        private Action<DuneVectorToolUnlockId> _confirmed;
        private Action _closed;
        private string _kickerText = string.Empty;
        private string _holdPromptText = string.Empty;
        private string _holdActivePromptText = string.Empty;
        private float _openedAt;
        private float _confirmedAt = -1f;
        private int _openedFrame;
        private bool _awaitingRelease;
        private float _hold01;
        private float _ringDegrees;
        private float _styledScale = -1f;
        private GUIStyle _kickerStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _footnoteStyle;
        private GUIStyle _promptStyle;

        public bool IsOpen { get; private set; }

        public void Initialize(ToolUnlockCeremonyTuning tuning, DronePlayer playerInput)
        {
            _tuning = tuning ?? new ToolUnlockCeremonyTuning();
            _tuning.EnsureInitialized();
            _playerInput = playerInput;
        }

        /// <summary>
        /// Opens the card for <paramref name="tool"/>. <paramref name="confirmed"/> fires the
        /// instant the hold completes so the tool's HUD is already live behind the fade-out;
        /// <paramref name="closed"/> fires once the card is gone.
        /// </summary>
        public bool Open(
            DuneVectorToolUnlockId tool,
            Action<DuneVectorToolUnlockId> confirmed,
            Action closed)
        {
            if (IsOpen || _tuning == null || !_tuning.Enabled)
            {
                return false;
            }

            ToolUnlockCeremonyEntryTuning entry = _tuning.Resolve(tool);
            if (entry == null)
            {
                return false;
            }

            _tool = tool;
            _entry = entry;
            _confirmed = confirmed;
            _closed = closed;
            _kickerText = Letterspace(entry.Kicker, _tuning.KickerLetterSpacing);
            _holdPromptText = Letterspace(_tuning.HoldPrompt, _tuning.PromptLetterSpacing);
            _holdActivePromptText = Letterspace(_tuning.HoldActivePrompt, _tuning.PromptLetterSpacing);
            _openedAt = Time.unscaledTime;
            _openedFrame = Time.frameCount;
            _confirmedAt = -1f;
            _hold01 = 0f;
            _ringDegrees = 0f;
            _awaitingRelease = true;
            IsOpen = true;
            _playerInput?.SetInputEnabled(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return true;
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            if (_confirmedAt < 0f)
            {
                AdvanceHold(delta);
            }
            else if (Time.unscaledTime >= _confirmedAt +
                     Mathf.Max(0f, _tuning.ConfirmFlashDuration) +
                     Mathf.Max(0.01f, _tuning.CloseFadeDuration))
            {
                Close();
                return;
            }

            float speed = _tuning.ReticleDegreesPerSecond *
                Mathf.Lerp(1f, Mathf.Max(1f, _tuning.ReticleHoldSpeedMultiplier), _hold01);
            _ringDegrees = Mathf.Repeat(_ringDegrees + (delta * speed), 360f);
        }

        private void AdvanceHold(float delta)
        {
            bool held = IsConfirmHeld();
            bool inputAllowed = Time.frameCount != _openedFrame &&
                Time.unscaledTime >= _openedAt + Mathf.Max(0f, _tuning.OpenInputDelay);

            // The press that dismissed whatever ran before this card must not carry into it.
            if (_awaitingRelease)
            {
                if (inputAllowed && !held)
                {
                    _awaitingRelease = false;
                }
                held = false;
            }
            else if (!inputAllowed)
            {
                held = false;
            }

            _hold01 = held
                ? Mathf.Min(1f, _hold01 + (delta / Mathf.Max(0.05f, _tuning.HoldSeconds)))
                : Mathf.Max(0f, _hold01 - (delta * Mathf.Max(0f, _tuning.HoldReleaseDecayPerSecond)));

            if (_hold01 >= 1f)
            {
                Confirm();
            }
        }

        private void Confirm()
        {
            _hold01 = 1f;
            _confirmedAt = Time.unscaledTime;
            Action<DuneVectorToolUnlockId> callback = _confirmed;
            _confirmed = null;
            callback?.Invoke(_tool);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            _entry = null;
            _playerInput?.SetInputEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Action callback = _closed;
            _closed = null;
            callback?.Invoke();
        }

        private void OnDestroy()
        {
            if (IsOpen)
            {
                IsOpen = false;
                _playerInput?.SetInputEnabled(true);
            }
        }

        private static bool IsConfirmHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.isPressed ||
                 keyboard.enterKey.isPressed ||
                 keyboard.numpadEnterKey.isPressed))
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonSouth.isPressed;
        }

        private void OnGUI()
        {
            // Draw-only overlay: it owns no controls, so the layout pass would repeat every
            // measurement for nothing. Only Repaint does work.
            if (Event.current.type != EventType.Repaint || !IsOpen || _entry == null)
            {
                return;
            }

            float scale = CalculateScale();
            EnsureStyles(scale);

            float reveal = EaseOut(Mathf.Clamp01(
                (Time.unscaledTime - _openedAt) / Mathf.Max(0.01f, _tuning.OpenFadeDuration)));
            float alpha = reveal;
            float flash = 0f;
            if (_confirmedAt >= 0f)
            {
                float since = Time.unscaledTime - _confirmedAt;
                float flashDuration = Mathf.Max(0.01f, _tuning.ConfirmFlashDuration);
                flash = 1f - Mathf.Clamp01(since / flashDuration);
                flash *= flash;
                alpha = 1f - Mathf.Clamp01(
                    (since - flashDuration) / Mathf.Max(0.01f, _tuning.CloseFadeDuration));
            }

            if (alpha <= 0f)
            {
                return;
            }

            GUI.depth = _tuning.GuiDepth;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            DuneVectorHudChrome.DrawRect(screen, Fade(_tuning.BackdropColor, alpha));
            DrawBackdropVignette(screen, scale, alpha);

            float panelWidth = Mathf.Min(_tuning.PanelWidth * scale, Screen.width - (24f * scale));
            float panelHeight = Mathf.Min(_tuning.PanelHeight * scale, Screen.height - (24f * scale));
            Rect panel = new Rect(
                Mathf.Round((Screen.width - panelWidth) * 0.5f),
                Mathf.Round((Screen.height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);

            Matrix4x4 previousMatrix = GUI.matrix;
            float entrance = Mathf.Lerp(Mathf.Clamp(_tuning.EntranceScale, 0.5f, 1f), 1f, reveal);
            if (!Mathf.Approximately(entrance, 1f))
            {
                GUIUtility.ScaleAroundPivot(new Vector2(entrance, entrance), panel.center);
            }
            DrawCard(panel, scale, alpha, reveal);
            GUI.matrix = previousMatrix;

            if (flash > 0f)
            {
                DuneVectorHudChrome.DrawRect(
                    screen,
                    Fade(_entry.AccentColor, flash * _tuning.ConfirmFlashStrength * alpha));
            }
        }

        /// <summary>Darkens the screen corners so the card is the only lit thing left.</summary>
        private void DrawBackdropVignette(Rect screen, float scale, float alpha)
        {
            Color vignette = Fade(_tuning.VignetteColor, alpha);
            float band = _tuning.VignetteBandSize * scale;
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(screen.x, screen.y, screen.width, band), vignette, true);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(screen.x, screen.yMax - band, screen.width, band), vignette, false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(screen.x, screen.y, band, screen.height), vignette, true);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(screen.xMax - band, screen.y, band, screen.height), vignette, false);
        }

        private void DrawCard(Rect panel, float scale, float alpha, float reveal)
        {
            Color accent = _entry.AccentColor;
            float border = Mathf.Max(1f, _tuning.BorderThickness * scale);

            DuneVectorHudChrome.DrawSoftShadow(
                panel,
                Fade(_tuning.ShadowColor, alpha),
                new Vector2(0f, _tuning.ShadowOffset * scale),
                _tuning.ShadowSpread * scale);

            // Hand-rolled glass instead of the shared helper: every layer has to carry the
            // card's fade, and the helper's sheen and depth washes are fixed opacity.
            DuneVectorHudChrome.DrawRect(panel, Fade(_tuning.PanelColor, alpha));
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(panel.x, panel.y, panel.width, panel.height * 0.55f),
                Fade(_tuning.PanelSheenColor, alpha),
                true);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(panel.x, panel.center.y, panel.width, panel.height * 0.5f),
                Fade(_tuning.PanelDepthColor, alpha),
                false);

            Rect header = new Rect(panel.x, panel.y, panel.width, _tuning.HeaderHeight * scale);
            float accentBarHeight = Mathf.Max(1f, _tuning.AccentBarHeight * scale);
            DuneVectorHudChrome.DrawRect(
                new Rect(panel.x, panel.y + accentBarHeight, panel.width, header.height - accentBarHeight),
                Fade(_tuning.HeaderColor, alpha));
            DuneVectorHudChrome.DrawRect(
                new Rect(panel.x, panel.y, panel.width, accentBarHeight),
                Fade(accent, alpha));
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(panel.x, panel.y + accentBarHeight, panel.width, _tuning.AccentGlowHeight * scale),
                Fade(accent, alpha * _tuning.AccentGlowOpacity),
                true);
            DuneVectorHudChrome.DrawRect(
                new Rect(panel.x, header.yMax - border, panel.width, border),
                Fade(_tuning.PanelBorderColor, alpha));
            DrawLabel(header, _kickerText, _kickerStyle, accent, alpha);

            float padding = _tuning.PanelPadding * scale;
            Rect content = new Rect(
                panel.x + padding,
                header.yMax + padding,
                panel.width - (padding * 2f),
                Mathf.Max(0f, panel.yMax - header.yMax - (padding * 2f)));

            float reticleRadius = _tuning.ReticleRadius * scale;
            Vector2 reticleCenter = new Vector2(content.center.x, content.y + reticleRadius);
            DrawReticle(reticleCenter, reticleRadius, scale, alpha, accent);

            float plateSize = _tuning.ImagePlateSize * scale;
            DrawImagePlate(
                new Rect(
                    reticleCenter.x - (plateSize * 0.5f),
                    reticleCenter.y - (plateSize * 0.5f),
                    plateSize,
                    plateSize),
                scale,
                alpha,
                accent);

            Rect titleRect = new Rect(
                content.x,
                reticleCenter.y + reticleRadius + (_tuning.TitleTopGap * scale),
                content.width,
                _tuning.TitleFontSize * 1.5f * scale);
            DuneVectorHudChrome.DrawGlowLabel(
                titleRect,
                _entry.Title,
                _titleStyle,
                Fade(_tuning.PrimaryTextColor, alpha),
                Fade(accent, alpha * _tuning.TitleGlowOpacity),
                _tuning.TitleGlowRadius * scale,
                Fade(_tuning.ShadowColor, alpha),
                new Vector2(0f, _tuning.TitleShadowOffset * scale));

            // The rule draws itself outward from the centre as the card settles in.
            float ruleWidth = _tuning.TitleRuleWidth * scale * reveal;
            float ruleHeight = Mathf.Max(1f, _tuning.TitleRuleHeight * scale);
            Rect rule = new Rect(
                content.center.x - (ruleWidth * 0.5f),
                titleRect.yMax + (_tuning.TitleRuleTopGap * scale),
                ruleWidth,
                ruleHeight);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(rule.x, rule.y, rule.width * 0.5f, rule.height), Fade(accent, alpha), false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(rule.center.x, rule.y, rule.width * 0.5f, rule.height), Fade(accent, alpha), true);

            float meterHeight = Mathf.Max(4f, _tuning.HoldMeterHeight * scale);
            float meterInsetX = _tuning.HoldMeterSideInset * scale;
            Rect meter = new Rect(
                content.x + meterInsetX,
                panel.yMax - (_tuning.HoldMeterBottomGap * scale) - meterHeight,
                Mathf.Max(0f, content.width - (meterInsetX * 2f)),
                meterHeight);

            float promptHeight = _tuning.PromptFontSize * 1.8f * scale;
            Rect promptRect = new Rect(
                content.x,
                meter.y - (_tuning.PromptBottomGap * scale) - promptHeight,
                content.width,
                promptHeight);

            float footnoteHeight = _tuning.FootnoteFontSize * 1.8f * scale;
            Rect footnoteRect = new Rect(
                content.x,
                promptRect.y - (_tuning.FootnoteBottomGap * scale) - footnoteHeight,
                content.width,
                footnoteHeight);

            float bodyInsetX = _tuning.BodySideInset * scale;
            Rect bodyRect = new Rect(
                content.x + bodyInsetX,
                rule.yMax + (_tuning.BodyTopGap * scale),
                Mathf.Max(0f, content.width - (bodyInsetX * 2f)),
                Mathf.Max(0f, footnoteRect.y - rule.yMax - (_tuning.BodyTopGap * scale)));
            DrawLabel(bodyRect, _entry.Body, _bodyStyle, _tuning.SecondaryTextColor, alpha);
            DrawLabel(footnoteRect, _entry.Footnote, _footnoteStyle, accent, alpha * _tuning.FootnoteOpacity);

            bool holding = _hold01 > 0.015f;
            float pulse = holding
                ? 1f
                : Mathf.Lerp(
                    Mathf.Clamp01(_tuning.PromptMinimumAlpha),
                    1f,
                    0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * _tuning.PromptPulseSpeed)));
            DrawLabel(
                promptRect,
                holding ? _holdActivePromptText : _holdPromptText,
                _promptStyle,
                holding ? accent : _tuning.PromptColor,
                alpha * pulse);

            DrawHoldMeter(meter, scale, alpha, accent);
        }

        /// <summary>
        /// Concentric survey reticle around the artwork. The outer ring doubles as the
        /// authorization readout: its ticks light up in order as the hold fills.
        /// </summary>
        private void DrawReticle(Vector2 center, float radius, float scale, float alpha, Color accent)
        {
            Color dim = Fade(_tuning.ReticleColor, alpha);
            Color hot = Fade(accent, alpha);
            float thickness = Mathf.Max(1f, _tuning.ReticleTickThickness * scale);

            DrawTickRing(
                center,
                radius,
                _tuning.ReticleOuterTickCount,
                _tuning.ReticleOuterTickLength * scale,
                thickness,
                _ringDegrees,
                dim,
                hot,
                _hold01,
                _tuning.ReticleFilledTickLengthMultiplier);
            DrawTickRing(
                center,
                radius - (_tuning.ReticleRingGap * scale),
                _tuning.ReticleInnerTickCount,
                _tuning.ReticleInnerTickLength * scale,
                Mathf.Max(1f, scale),
                -_ringDegrees * _tuning.ReticleInnerSpeedRatio,
                dim,
                hot,
                0f,
                1f);

            float spurRadius = radius + (_tuning.ReticleSpurGap * scale);
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                DrawRadialTick(
                    center,
                    spurRadius,
                    (quadrant * 90f) + 45f,
                    _tuning.ReticleSpurLength * scale,
                    thickness,
                    hot);
            }
        }

        private static void DrawTickRing(
            Vector2 center,
            float radius,
            int tickCount,
            float tickLength,
            float thickness,
            float rotationDegrees,
            Color color,
            Color filledColor,
            float fill01,
            float filledLengthMultiplier)
        {
            tickCount = Mathf.Max(1, tickCount);
            if (radius <= 0f || tickLength <= 0f)
            {
                return;
            }

            float step = 360f / tickCount;
            for (int index = 0; index < tickCount; index++)
            {
                bool filled = (index + 1f) / tickCount <= Mathf.Clamp01(fill01);
                DrawRadialTick(
                    center,
                    radius,
                    rotationDegrees + (index * step),
                    filled ? tickLength * filledLengthMultiplier : tickLength,
                    thickness,
                    filled ? filledColor : color);
            }
        }

        private static void DrawRadialTick(
            Vector2 center,
            float radius,
            float angleDegrees,
            float length,
            float thickness,
            Color color)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDegrees, center);
            DuneVectorHudChrome.DrawRect(
                new Rect(center.x - (thickness * 0.5f), center.y - radius - length, thickness, length),
                color);
            GUI.matrix = previousMatrix;
        }

        private void DrawImagePlate(Rect plate, float scale, float alpha, Color accent)
        {
            DuneVectorHudChrome.DrawRect(plate, Fade(_tuning.ImagePlateColor, alpha));
            if (_entry.Image != null)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(plate, _entry.Image, ScaleMode.ScaleToFit, true);
                GUI.color = previousColor;
            }

            // Survey sweep, clipped to the plate so the band never spills onto the card.
            float sweepHeight = Mathf.Max(1f, _tuning.ImageSweepHeight * scale);
            float sweep01 = Mathf.Repeat(
                (Time.unscaledTime - _openedAt) / Mathf.Max(0.05f, _tuning.ImageSweepSeconds), 1f);
            float sweepY = Mathf.Lerp(-sweepHeight, plate.height, sweep01);
            GUI.BeginGroup(plate);
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(0f, sweepY, plate.width, sweepHeight),
                Fade(accent, alpha * _tuning.ImageSweepOpacity),
                false);
            DuneVectorHudChrome.DrawRect(
                new Rect(0f, sweepY + sweepHeight - Mathf.Max(1f, scale), plate.width, Mathf.Max(1f, scale)),
                Fade(accent, alpha * _tuning.ImageSweepLineOpacity));
            GUI.EndGroup();

            DuneVectorHudChrome.DrawBorder(
                plate,
                Fade(_tuning.ImagePlateBorderColor, alpha),
                Mathf.Max(1f, _tuning.BorderThickness * scale));
            DuneVectorHudChrome.DrawCornerBrackets(
                plate,
                Fade(accent, alpha),
                _tuning.CornerBracketLength * scale,
                Mathf.Max(1f, _tuning.CornerBracketThickness * scale));
        }

        private void DrawHoldMeter(Rect track, float scale, float alpha, Color accent)
        {
            DuneVectorHudChrome.DrawRect(track, Fade(_tuning.HoldTrackColor, alpha));

            float inset = Mathf.Max(1f, _tuning.HoldMeterInset * scale);
            Rect bounds = new Rect(
                track.x + inset,
                track.y + inset,
                Mathf.Max(0f, track.width - (inset * 2f)),
                Mathf.Max(0f, track.height - (inset * 2f)));
            float fillWidth = bounds.width * Mathf.Clamp01(_hold01);
            if (fillWidth > 0.5f && bounds.height > 0f)
            {
                Rect fill = new Rect(bounds.x, bounds.y, fillWidth, bounds.height);
                DuneVectorHudChrome.DrawRect(fill, Fade(accent, alpha * _tuning.HoldFillBaseOpacity));
                DuneVectorHudChrome.DrawVerticalFade(fill, Fade(accent, alpha), true);

                float cap = Mathf.Max(1.5f, _tuning.HoldMeterCapWidth * scale);
                if (fill.width > cap)
                {
                    DuneVectorHudChrome.DrawRect(
                        new Rect(fill.xMax - cap, fill.y, cap, fill.height),
                        Fade(Color.Lerp(accent, Color.white, _tuning.HoldCapWhiteness), alpha));
                }

                DuneVectorHudChrome.DrawHorizontalFade(
                    new Rect(
                        fill.xMax,
                        track.y,
                        Mathf.Min(bounds.xMax - fill.xMax, _tuning.HoldBloomWidth * scale),
                        track.height),
                    Fade(accent, alpha * _tuning.HoldBloomOpacity),
                    true);
            }

            int segments = Mathf.Max(1, _tuning.HoldSegmentCount);
            float segmentWidth = Mathf.Max(1f, scale);
            for (int index = 1; index < segments; index++)
            {
                float x = track.x + (track.width * index / segments);
                DuneVectorHudChrome.DrawRect(
                    new Rect(x - (segmentWidth * 0.5f), track.y, segmentWidth, track.height),
                    Fade(_tuning.HoldSegmentColor, alpha));
            }

            DuneVectorHudChrome.DrawBorder(
                track,
                Fade(accent, alpha * _tuning.HoldTrackBorderOpacity),
                Mathf.Max(1f, scale));
        }

        private float CalculateScale()
        {
            float minimumScale = Mathf.Min(_tuning.MinimumScale, _tuning.MaximumScale);
            float maximumScale = Mathf.Max(_tuning.MinimumScale, _tuning.MaximumScale);
            return Mathf.Clamp(
                Mathf.Min(
                    Screen.width / Mathf.Max(1f, _tuning.ReferenceWidth),
                    Screen.height / Mathf.Max(1f, _tuning.ReferenceHeight)),
                minimumScale,
                maximumScale);
        }

        private void EnsureStyles(float scale)
        {
            if (_kickerStyle != null && Mathf.Abs(scale - _styledScale) < 0.001f)
            {
                return;
            }
            _styledScale = scale;

            _kickerStyle = CreateLabelStyle(
                _tuning.KickerFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
            _titleStyle = CreateLabelStyle(
                _tuning.TitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
            _bodyStyle = CreateLabelStyle(
                _tuning.BodyFontSize, FontStyle.Normal, TextAnchor.UpperCenter, true, scale);
            _footnoteStyle = CreateLabelStyle(
                _tuning.FootnoteFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
            _promptStyle = CreateLabelStyle(
                _tuning.PromptFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
        }

        private static GUIStyle CreateLabelStyle(
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            bool wordWrap,
            float scale)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale)),
                fontStyle = fontStyle,
                wordWrap = wordWrap,
                clipping = TextClipping.Clip,
                padding = new RectOffset(),
                margin = new RectOffset(),
            };
        }

        private static void DrawLabel(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            if (string.IsNullOrEmpty(text) || style == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = Fade(color, alpha);
            GUI.Label(rect, text, style);
            GUI.color = previousColor;
        }

        /// <summary>Widens the tracking of a short label by padding between its characters.</summary>
        private static string Letterspace(string text, int spaces)
        {
            if (string.IsNullOrEmpty(text) || spaces <= 0)
            {
                return text ?? string.Empty;
            }

            string gap = new string(' ', spaces);
            StringBuilder builder = new StringBuilder(text.Length * (spaces + 1));
            for (int index = 0; index < text.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(gap);
                }
                builder.Append(text[index]);
            }
            return builder.ToString();
        }

        private static Color Fade(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(alpha));
        }

        private static float EaseOut(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - (inverse * inverse * inverse);
        }
    }
}
