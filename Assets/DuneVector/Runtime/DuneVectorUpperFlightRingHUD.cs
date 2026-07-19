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
            if (_world == null || _settings == null || !_settings.ShowUpperFlightRingHud)
            {
                return;
            }

            if (_world.IsUpperFlightRingUnlocked)
            {
                if (_unlockedAt < 0f)
                {
                    _unlockedAt = Time.unscaledTime;
                }

                if (Time.unscaledTime - _unlockedAt >= Mathf.Max(0f, _settings.UpperFlightRingHudUnlockedDuration))
                {
                    return;
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
            EnsureStyles(scale);

            float width = _settings.UpperFlightRingHudWidth * scale;
            float height = _settings.UpperFlightRingHudHeight * scale;
            float topMargin = _settings.UpperFlightRingHudTopMargin * scale;
            float rightMargin = _settings.UpperFlightRingHudRightMargin * scale;
            float belowGoldPanel = _settings.GoldHudTopMargin
                + _settings.GoldHudHeight
                + (_settings.UpperFlightRingHudGoldGap * scale);
            topMargin = Mathf.Max(topMargin, belowGoldPanel);
            Rect panel = new Rect(Screen.width - rightMargin - width, topMargin, width, height);
            DrawSolidRect(panel, _settings.UpperFlightRingHudPanelColor);

            float accentWidth = _settings.UpperFlightRingHudAccentWidth * scale;
            DrawSolidRect(
                new Rect(panel.x, panel.y, accentWidth, panel.height),
                _settings.UpperFlightRingHudAccentColor);

            float padding = _settings.UpperFlightRingHudPadding * scale;
            float contentX = panel.x + accentWidth + padding;
            float contentWidth = panel.width - accentWidth - (padding * 2f);
            float titleHeight = _settings.UpperFlightRingHudTitleFontSize * scale * 1.5f;
            GUI.Label(
                new Rect(contentX, panel.y + padding, contentWidth, titleHeight),
                _settings.UpperFlightRingHudTitle,
                _titleStyle);

            int requiredPasses = _world.UpperFlightRingRequiredPasses;
            int completedPasses = Mathf.Min(_world.ActivatedFlightRingCount, requiredPasses);
            string status = _world.IsUpperFlightRingUnlocked
                ? _settings.UpperFlightRingHudUnlockedLabel
                : $"{_settings.UpperFlightRingHudProgressLabel}  {completedPasses} / {requiredPasses}";
            _statusStyle.normal.textColor = _world.IsUpperFlightRingUnlocked
                ? _settings.UpperFlightRingHudUnlockedColor
                : _settings.UpperFlightRingHudStatusColor;
            float statusY = panel.y + padding + titleHeight;
            float barHeight = _settings.UpperFlightRingHudProgressBarHeight * scale;
            float statusHeight = Mathf.Max(1f, panel.yMax - padding - barHeight - statusY - padding);
            GUI.Label(new Rect(contentX, statusY, contentWidth, statusHeight), status, _statusStyle);

            Rect track = new Rect(
                contentX,
                panel.yMax - padding - barHeight,
                contentWidth,
                barHeight);
            DrawSolidRect(track, _settings.UpperFlightRingHudTrackColor);
            float progress = completedPasses / (float)requiredPasses;
            DrawSolidRect(
                new Rect(track.x, track.y, track.width * progress, track.height),
                _world.IsUpperFlightRingUnlocked
                    ? _settings.UpperFlightRingHudUnlockedColor
                    : _settings.UpperFlightRingHudAccentColor);
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

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
