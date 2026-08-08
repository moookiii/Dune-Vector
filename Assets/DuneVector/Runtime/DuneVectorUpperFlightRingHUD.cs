using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorUpperFlightRingHUD : MonoBehaviour
    {
        private DesertWorldStreamer _world;
        private RingTuning _settings;
        private GUIStyle _titleStyle;
        private GUIStyle _statusStyle;
        private float _unlockedAt = -1f;

        public void Initialize(DesertWorldStreamer world, RingTuning settings)
        {
            _world = world;
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

            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (!TryGetVisiblePanelRect(out Rect panel))
            {
                return;
            }

            float referenceHeight = Mathf.Max(1f, _settings.UpperFlightRingHudReferenceHeight);
            float minimumScale = Mathf.Min(
                _settings.UpperFlightRingHudMinimumScale,
                _settings.UpperFlightRingHudMaximumScale);
            float maximumScale = Mathf.Max(
                _settings.UpperFlightRingHudMinimumScale,
                _settings.UpperFlightRingHudMaximumScale);
            float scale = Mathf.Clamp(Screen.height / referenceHeight, minimumScale, maximumScale);
            EnsureStyles(scale);

            int requiredPasses = _world.UpperFlightRingRequiredPasses;
            int completedPasses = Mathf.Min(_world.ActivatedFlightRingCount, requiredPasses);
            Color accentColor = _world.IsUpperFlightRingUnlocked
                ? _settings.UpperFlightRingHudUnlockedColor
                : _settings.UpperFlightRingHudAccentColor;

            DuneVectorHudChrome.DrawSoftShadow(
                panel,
                _settings.UpperFlightRingHudShadowColor,
                _settings.UpperFlightRingHudShadowOffset * scale,
                5f * scale);

            Color border = accentColor;
            border.a *= 0.45f;
            DuneVectorHudChrome.DrawGlassPanel(
                panel,
                _settings.UpperFlightRingHudPanelColor,
                border,
                Mathf.Max(1f, scale),
                scale);

            float accentWidth = _settings.UpperFlightRingHudAccentWidth * scale;
            DuneVectorHudChrome.DrawAccentRail(panel, accentColor, accentWidth, 34f * scale);
            Color bracket = accentColor;
            bracket.a *= 0.6f;
            DuneVectorHudChrome.DrawCornerBrackets(panel, bracket, 12f * scale, Mathf.Max(1f, scale));

            float padding = _settings.UpperFlightRingHudPadding * scale;
            float contentX = panel.x + accentWidth + padding;
            float contentWidth = panel.width - accentWidth - (padding * 2f);
            float titleHeight = _settings.UpperFlightRingHudTitleFontSize * scale * 1.5f;
            Vector2 textShadow = new Vector2(1f, 1f) * scale;
            Color textShadowColor = new Color(0f, 0f, 0f, 0.6f);
            DuneVectorHudChrome.DrawLabel(
                new Rect(contentX, panel.y + padding, contentWidth, titleHeight),
                _settings.UpperFlightRingHudTitle,
                _titleStyle,
                Color.white,
                textShadowColor,
                textShadow);

            string status = _world.IsUpperFlightRingUnlocked
                ? _settings.UpperFlightRingHudUnlockedLabel
                : $"{_settings.UpperFlightRingHudProgressLabel}  {completedPasses} / {requiredPasses}";
            _statusStyle.normal.textColor = _world.IsUpperFlightRingUnlocked
                ? _settings.UpperFlightRingHudUnlockedColor
                : _settings.UpperFlightRingHudStatusColor;
            float statusY = panel.y + padding + titleHeight;
            float barHeight = _settings.UpperFlightRingHudProgressBarHeight * scale;
            float statusHeight = Mathf.Max(1f, panel.yMax - padding - barHeight - statusY - padding);
            DuneVectorHudChrome.DrawLabel(
                new Rect(contentX, statusY, contentWidth, statusHeight),
                status,
                _statusStyle,
                Color.white,
                textShadowColor,
                textShadow);

            Rect track = new Rect(
                contentX,
                panel.yMax - padding - barHeight,
                contentWidth,
                barHeight);
            float progress = completedPasses / (float)requiredPasses;
            DuneVectorHudChrome.DrawMeter(
                track,
                progress,
                accentColor,
                _settings.UpperFlightRingHudTrackColor,
                Mathf.Max(1f, scale),
                Mathf.Max(1, requiredPasses / 10),
                new Color(0f, 0f, 0f, 0.28f),
                Mathf.Max(1f, scale),
                scale);
        }

        public bool TryGetVisiblePanelRect(out Rect panel)
        {
            panel = default;
            if (_world == null || _settings == null || !_settings.ShowUpperFlightRingHud)
            {
                return false;
            }

            if (_world.IsUpperFlightRingUnlocked)
            {
                if (_unlockedAt < 0f)
                {
                    _unlockedAt = Time.unscaledTime;
                }

                if (Time.unscaledTime - _unlockedAt >= Mathf.Max(0f, _settings.UpperFlightRingHudUnlockedDuration))
                {
                    return false;
                }
            }
            else
            {
                _unlockedAt = -1f;
            }

            float referenceHeight = Mathf.Max(1f, _settings.UpperFlightRingHudReferenceHeight);
            float minimumScale = Mathf.Min(
                _settings.UpperFlightRingHudMinimumScale,
                _settings.UpperFlightRingHudMaximumScale);
            float maximumScale = Mathf.Max(
                _settings.UpperFlightRingHudMinimumScale,
                _settings.UpperFlightRingHudMaximumScale);
            float scale = Mathf.Clamp(Screen.height / referenceHeight, minimumScale, maximumScale);

            float width = _settings.UpperFlightRingHudWidth * scale;
            float height = _settings.UpperFlightRingHudHeight * scale;
            float topMargin = _settings.UpperFlightRingHudTopMargin * scale;
            float rightMargin = _settings.UpperFlightRingHudRightMargin * scale;
            float belowGoldPanel = _settings.GoldHudTopMargin
                + _settings.GoldHudHeight
                + (_settings.UpperFlightRingHudGoldGap * scale);
            topMargin = Mathf.Max(topMargin, belowGoldPanel);
            panel = new Rect(Screen.width - rightMargin - width, topMargin, width, height);
            return true;
        }

        private void EnsureStyles(float scale)
        {
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            _statusStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            _titleStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(_settings.UpperFlightRingHudTitleFontSize * scale));
            _titleStyle.normal.textColor = _settings.UpperFlightRingHudTitleColor;
            _statusStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(_settings.UpperFlightRingHudStatusFontSize * scale));
        }
    }
}
