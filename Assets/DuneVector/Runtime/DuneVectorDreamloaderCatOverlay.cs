using System;
using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorDreamloaderCatOverlay : MonoBehaviour
    {
        private DuneVectorAudioManager _audio;
        private MusicReactiveSkyTuning _settings;
        private Texture2D[] _frames = Array.Empty<Texture2D>();
        private bool _loadAttempted;

        public void Initialize(DuneVectorAudioManager audio, MusicReactiveSkyTuning settings)
        {
            _audio = audio;
            _settings = settings;
            enabled = _audio != null && _settings != null && _settings.DreamloaderCatOverlayEnabled;
        }

        private void OnGUI()
        {
            if (_audio == null
                || _settings == null
                || !_settings.DreamloaderCatOverlayEnabled
                || _audio.VisualizerMode == MusicVisualizerMode.Off
                || _audio.MusicVolume <= 0f
                || Event.current.type != EventType.Repaint
                || DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }

            MusicTimelineState timeline = _audio.TimelineState;
            int timelineMilliseconds = timeline.TimelinePositionMilliseconds;
            if (!TryResolveActiveSectionStart(timelineMilliseconds, out int startMilliseconds))
            {
                return;
            }

            if (!IsDreamloaderTrack())
            {
                return;
            }

            EnsureFramesLoaded();
            if (_frames.Length == 0)
            {
                return;
            }

            float secondsSinceStart = (timelineMilliseconds - startMilliseconds) / 1000f;
            float frameRate = Mathf.Max(0.01f, _settings.DreamloaderCatFramesPerSecond);
            float speed = Mathf.Max(0.01f, _settings.DreamloaderCatLoopSpeedMultiplier);
            int frameIndex = Mathf.FloorToInt(secondsSinceStart * frameRate * speed) % _frames.Length;
            Texture2D frame = _frames[frameIndex];
            if (frame == null)
            {
                return;
            }

            float width = frame.width;
            float height = frame.height;
            float padding = Mathf.Max(0f, _settings.DreamloaderCatHorizontalPadding);
            float y = (Screen.height - height) * 0.5f;
            Rect leftRect = new Rect(padding, y, width, height);
            Rect rightRect = new Rect(Screen.width - padding - width, y, width, height);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_settings.DreamloaderCatAlpha));
            GUI.DrawTexture(leftRect, frame, ScaleMode.StretchToFill, true);
            GUI.DrawTextureWithTexCoords(rightRect, frame, new Rect(1f, 0f, -1f, 1f), true);
            GUI.color = previousColor;
        }

        private bool IsDreamloaderTrack()
        {
            string match = _settings.DreamloaderCatTrackName;
            if (string.IsNullOrWhiteSpace(match))
            {
                return true;
            }

            MusicPlaylistTrack track = _audio.ActiveMusicTrack;
            MusicVisualTrackProfile profile = _audio.ActiveMusicTrackProfile;
            return MatchesEventName(track != null ? track.FmodEventPath : null, match)
                || MatchesEventName(profile != null ? profile.FmodEventPath : null, match);
        }

        private bool TryResolveActiveSectionStart(int timelineMilliseconds, out int startMilliseconds)
        {
            if (IsInSection(
                    timelineMilliseconds,
                    _settings.DreamloaderCatSection1StartTimelineMilliseconds,
                    _settings.DreamloaderCatSection1EndTimelineMilliseconds,
                    out startMilliseconds))
            {
                return true;
            }

            return IsInSection(
                timelineMilliseconds,
                _settings.DreamloaderCatSection2StartTimelineMilliseconds,
                _settings.DreamloaderCatSection2EndTimelineMilliseconds,
                out startMilliseconds);
        }

        private static bool IsInSection(
            int timelineMilliseconds,
            int authoredStartMilliseconds,
            int authoredEndMilliseconds,
            out int startMilliseconds)
        {
            startMilliseconds = Mathf.Max(0, authoredStartMilliseconds);
            int endMilliseconds = Mathf.Max(0, authoredEndMilliseconds);
            return endMilliseconds > startMilliseconds
                && timelineMilliseconds >= startMilliseconds
                && timelineMilliseconds <= endMilliseconds;
        }

        private void EnsureFramesLoaded()
        {
            if (_loadAttempted)
            {
                return;
            }

            _loadAttempted = true;
            string path = string.IsNullOrWhiteSpace(_settings.DreamloaderCatResourcesPath)
                ? "MusicVisuals/DreamloaderCat"
                : _settings.DreamloaderCatResourcesPath.Trim();
            _frames = Resources.LoadAll<Texture2D>(path);
            Array.Sort(_frames, (left, right) => string.CompareOrdinal(left.name, right.name));
        }

        private static bool MatchesEventName(string eventPath, string match)
        {
            if (string.IsNullOrWhiteSpace(eventPath) || string.IsNullOrWhiteSpace(match))
            {
                return false;
            }

            string value = eventPath.Trim();
            int separator = value.LastIndexOf('/');
            if (separator >= 0 && separator + 1 < value.Length)
            {
                value = value.Substring(separator + 1);
            }
            return string.Equals(value, match.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
