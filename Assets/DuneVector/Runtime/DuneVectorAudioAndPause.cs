using System;
using System.IO;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorAudioManager : MonoBehaviour
    {
        private const string AudioPreferencesFileName = "DuneVectorAudio.dat";

        [Serializable]
        private sealed class AudioPreferencesData
        {
            public int Version = 1;
            public float MusicVolume;
            public float SoundEffectsVolume;
        }

        public float MusicVolume { get; private set; }
        public float SoundEffectsVolume { get; private set; }

        private AudioTuning _settings;
        private EventInstance _musicInstance;
        private Bus _musicBus;
        private Bus _soundEffectsBus;
        private bool _hasMusicBus;
        private bool _hasSoundEffectsBus;
        private DroneHealth _health;
        private string _preferencesPath;
        private bool _preferencesDirty;

        public void Initialize(AudioTuning settings, DroneHealth health)
        {
            _settings = settings;
            _health = health;
            if (_settings == null)
            {
                Debug.LogError("Dune Vector audio requires Audio Tuning in the Runtime Settings asset.", this);
                enabled = false;
                return;
            }

            _preferencesPath = Path.Combine(Application.persistentDataPath, AudioPreferencesFileName);
            LoadStoredVolumes();

            _hasMusicBus = TryGetBus(_settings.MusicBusPath, out _musicBus);
            _hasSoundEffectsBus = TryGetBus(_settings.SoundEffectsBusPath, out _soundEffectsBus);
            ApplyMixerVolumes();
            StartBackgroundMusic();
            if (_health != null)
            {
                _health.Damaged += HandleDroneDamaged;
            }
        }

        private void Update()
        {
            if (!_musicInstance.isValid())
            {
                return;
            }

            if (_musicInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK &&
                playbackState == PLAYBACK_STATE.STOPPED)
            {
                _musicInstance.start();
            }
        }

        public void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            if (_hasMusicBus && _musicBus.isValid())
            {
                _musicBus.setVolume(MusicVolume);
            }
            else if (_musicInstance.isValid())
            {
                _musicInstance.setVolume(MusicVolume);
            }
            _preferencesDirty = true;
        }

        public void SetSoundEffectsVolume(float volume)
        {
            SoundEffectsVolume = Mathf.Clamp01(volume);
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                _soundEffectsBus.setVolume(SoundEffectsVolume);
            }
            _preferencesDirty = true;
        }

        private void StartBackgroundMusic()
        {
            if (string.IsNullOrWhiteSpace(_settings.BackgroundMusicEvent))
            {
                return;
            }

            try
            {
                _musicInstance = RuntimeManager.CreateInstance(_settings.BackgroundMusicEvent);
                if (!_hasMusicBus)
                {
                    _musicInstance.setVolume(MusicVolume);
                }
                _musicInstance.start();
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogError(
                    $"FMOD background event '{_settings.BackgroundMusicEvent}' was not found. {exception.Message}",
                    this);
            }
        }

        private void ApplyMixerVolumes()
        {
            if (_hasMusicBus && _musicBus.isValid())
            {
                _musicBus.setVolume(MusicVolume);
            }
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                _soundEffectsBus.setVolume(SoundEffectsVolume);
            }
        }

        private void HandleDroneDamaged(float appliedDamage)
        {
            if (appliedDamage <= 0f || string.IsNullOrWhiteSpace(_settings.DroneDamageEvent))
            {
                return;
            }

            RuntimeManager.PlayOneShot(_settings.DroneDamageEvent, _health.transform.position);
        }

        private static bool TryGetBus(string path, out Bus bus)
        {
            bus = default;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                bus = RuntimeManager.GetBus(path);
                return bus.isValid();
            }
            catch (BusNotFoundException exception)
            {
                Debug.LogWarning($"FMOD mixer bus '{path}' was not found. {exception.Message}");
                return false;
            }
        }

        private void LoadStoredVolumes()
        {
            MusicVolume = Mathf.Clamp01(_settings.DefaultMusicVolume);
            SoundEffectsVolume = Mathf.Clamp01(_settings.DefaultSoundEffectsVolume);
            if (!_settings.PersistVolumeSettings || !File.Exists(_preferencesPath))
            {
                return;
            }

            try
            {
                AudioPreferencesData stored = JsonUtility.FromJson<AudioPreferencesData>(File.ReadAllText(_preferencesPath));
                if (stored != null && stored.Version == 1)
                {
                    MusicVolume = Mathf.Clamp01(stored.MusicVolume);
                    SoundEffectsVolume = Mathf.Clamp01(stored.SoundEffectsVolume);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read audio preferences from '{_preferencesPath}'. {exception.Message}", this);
            }
        }

        public void FlushPreferences()
        {
            if (!_preferencesDirty || _settings == null || !_settings.PersistVolumeSettings)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_preferencesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AudioPreferencesData stored = new AudioPreferencesData
                {
                    MusicVolume = MusicVolume,
                    SoundEffectsVolume = SoundEffectsVolume,
                };
                File.WriteAllText(_preferencesPath, JsonUtility.ToJson(stored));
                _preferencesDirty = false;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save audio preferences to '{_preferencesPath}'. {exception.Message}", this);
            }
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.Damaged -= HandleDroneDamaged;
            }
            if (_musicInstance.isValid())
            {
                _musicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _musicInstance.release();
                _musicInstance.clearHandle();
            }
            FlushPreferences();
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorPauseMenu : MonoBehaviour
    {
        public bool IsPaused { get; private set; }

        private DronePlayer _player;
        private DroneHealth _health;
        private DuneVectorAudioManager _audio;
        private PauseMenuVisualTuning _visuals;

        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _mixerLabelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _secondaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _sliderStyle;
        private GUIStyle _sliderThumbStyle;

        private Texture2D _transparentTexture;
        private Texture2D _primaryButtonTexture;
        private Texture2D _primaryButtonHoverTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonActiveTexture;
        private Texture2D _dangerButtonTexture;
        private Texture2D _dangerButtonHoverTexture;
        private Texture2D _sliderThumbTexture;
        private float _styledScale = -1f;

        public void Initialize(
            DronePlayer player,
            DroneHealth health,
            DuneVectorAudioManager audio,
            PauseMenuVisualTuning visuals)
        {
            _player = player;
            _health = health;
            _audio = audio;
            _visuals = visuals;
            if (_health != null)
            {
                _health.Died += HandleDeath;
            }
        }

        private void Update()
        {
            if ((_health == null || !_health.IsDead) &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetPaused(!IsPaused);
            }
        }

        private void SetPaused(bool paused)
        {
            if (paused && _health != null && _health.IsDead)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            _player?.SetInputEnabled(!paused);
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
            if (!paused)
            {
                _audio?.FlushPreferences();
            }
        }

        private void HandleDeath()
        {
            IsPaused = false;
            _player?.SetInputEnabled(false);
        }

        private void OnGUI()
        {
            if (!IsPaused || (_health != null && _health.IsDead) || _visuals == null)
            {
                return;
            }

            GUI.depth = -1000;
            float scale = CalculateScale();
            EnsureStyles(scale);

            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), _visuals.OverlayColor);

            float screenMargin = _visuals.ScreenMargin * scale;
            float panelWidth = Mathf.Min(_visuals.PanelWidth * scale, Screen.width - (screenMargin * 2f));
            float panelHeight = Mathf.Min(_visuals.PanelHeight * scale, Screen.height - (screenMargin * 2f));
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            float shadowOffset = _visuals.ShadowOffset * scale;
            DrawSolidRect(new Rect(panel.x + shadowOffset, panel.y + shadowOffset, panel.width, panel.height), _visuals.ShadowColor);
            DrawSolidRect(panel, _visuals.PanelColor);
            DrawBorder(panel, _visuals.PanelBorderColor, Mathf.Max(1f, scale * 2f));
            DrawSolidRect(
                new Rect(panel.x, panel.y, panel.width, Mathf.Max(1f, _visuals.AccentBarHeight * scale)),
                _visuals.AccentColor);

            float padding = _visuals.PanelPadding * scale;
            Rect content = new Rect(panel.x + padding, panel.y + padding, panel.width - (padding * 2f), panel.height - (padding * 2f));
            float gap = _visuals.ButtonGap * scale;
            float y = content.y;

            float titleHeight = _titleStyle.lineHeight;
            GUI.Label(new Rect(content.x, y, content.width, titleHeight), "PAUSED", _titleStyle);
            y += titleHeight;

            float subtitleHeight = _subtitleStyle.lineHeight;
            GUI.Label(new Rect(content.x, y, content.width, subtitleHeight), "DUNE VECTOR  /  SYSTEMS ON HOLD", _subtitleStyle);
            y += subtitleHeight + gap;

            DrawSolidRect(new Rect(content.x, y, content.width, Mathf.Max(1f, scale)), _visuals.DividerColor);
            y += gap * 1.5f;

            float sectionHeight = _sectionStyle.lineHeight;
            GUI.Label(new Rect(content.x, y, content.width, sectionHeight), "AUDIO MIXER", _sectionStyle);
            y += sectionHeight + gap;

            float sliderRowHeight = _visuals.SliderRowHeight * scale;
            DrawVolumeRow(
                new Rect(content.x, y, content.width, sliderRowHeight),
                "MUSIC",
                _audio != null ? _audio.MusicVolume : 0f,
                value => _audio?.SetMusicVolume(value),
                scale);
            y += sliderRowHeight;

            DrawVolumeRow(
                new Rect(content.x, y, content.width, sliderRowHeight),
                "SOUND EFFECTS",
                _audio != null ? _audio.SoundEffectsVolume : 0f,
                value => _audio?.SetSoundEffectsVolume(value),
                scale);
            y += sliderRowHeight + gap;

            float buttonHeight = _visuals.ButtonHeight * scale;
            if (GUI.Button(new Rect(content.x, y, content.width, buttonHeight), "RESUME FLIGHT", _primaryButtonStyle))
            {
                SetPaused(false);
            }
            y += buttonHeight + gap;

            float splitButtonWidth = (content.width - gap) * 0.5f;
            if (GUI.Button(new Rect(content.x, y, splitButtonWidth, buttonHeight), "RESTART RUN", _secondaryButtonStyle))
            {
                RestartRun();
            }
            if (GUI.Button(
                    new Rect(content.x + splitButtonWidth + gap, y, splitButtonWidth, buttonHeight),
                    "QUIT",
                    _dangerButtonStyle))
            {
                QuitGame();
            }

            float hintHeight = _hintStyle.lineHeight;
            GUI.Label(
                new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight),
                "ESC  /  RETURN TO THE DESERT",
                _hintStyle);
        }

        private void DrawVolumeRow(
            Rect area,
            string label,
            float value,
            Action<float> apply,
            float scale)
        {
            float labelHeight = Mathf.Max(_mixerLabelStyle.lineHeight, _valueStyle.lineHeight);
            GUI.Label(new Rect(area.x, area.y, area.width * 0.7f, labelHeight), label, _mixerLabelStyle);
            GUI.Label(
                new Rect(area.x + (area.width * 0.7f), area.y, area.width * 0.3f, labelHeight),
                $"{Mathf.RoundToInt(value * 100f):00}%",
                _valueStyle);

            float thumbWidth = _visuals.SliderThumbWidth * scale;
            float thumbHeight = _visuals.SliderThumbHeight * scale;
            float trackHeight = Mathf.Max(1f, _visuals.SliderTrackHeight * scale);
            float availableSliderHeight = Mathf.Max(thumbHeight, area.height - labelHeight);
            float sliderY = area.y + labelHeight + ((availableSliderHeight - thumbHeight) * 0.5f);
            Rect sliderRect = new Rect(area.x, sliderY, area.width, thumbHeight);
            Rect trackRect = new Rect(
                sliderRect.x + (thumbWidth * 0.5f),
                sliderRect.center.y - (trackHeight * 0.5f),
                Mathf.Max(1f, sliderRect.width - thumbWidth),
                trackHeight);

            DrawSolidRect(trackRect, _visuals.SliderTrackColor);
            DrawSolidRect(new Rect(trackRect.x, trackRect.y, trackRect.width * Mathf.Clamp01(value), trackRect.height), _visuals.SliderFillColor);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && _audio != null;
            float changedValue = GUI.HorizontalSlider(sliderRect, value, 0f, 1f, _sliderStyle, _sliderThumbStyle);
            GUI.enabled = wasEnabled;
            if (!Mathf.Approximately(changedValue, value))
            {
                apply?.Invoke(changedValue);
            }
        }

        private float CalculateScale()
        {
            float widthScale = Screen.width / Mathf.Max(1f, _visuals.ReferenceWidth);
            float heightScale = Screen.height / Mathf.Max(1f, _visuals.ReferenceHeight);
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), _visuals.MinimumScale, _visuals.MaximumScale);
        }

        private void EnsureStyles(float scale)
        {
            EnsureTextures();
            if (Mathf.Abs(scale - _styledScale) < 0.001f)
            {
                return;
            }
            _styledScale = scale;

            _titleStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.TitleFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.TitleColor);
            _subtitleStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SubtitleFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.SecondaryTextColor);
            _sectionStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SectionFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _visuals.AccentColor);
            _mixerLabelStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.MixerLabelFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _visuals.PrimaryTextColor);
            _valueStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.ValueFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                _visuals.TitleColor);
            _hintStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.HintFontSize * scale),
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _visuals.SecondaryTextColor);

            int buttonFontSize = Mathf.RoundToInt(_visuals.ButtonFontSize * scale);
            _primaryButtonStyle = CreateButtonStyle(
                buttonFontSize,
                _primaryButtonTexture,
                _primaryButtonHoverTexture,
                _buttonActiveTexture);
            _secondaryButtonStyle = CreateButtonStyle(
                buttonFontSize,
                _buttonTexture,
                _buttonHoverTexture,
                _buttonActiveTexture);
            _dangerButtonStyle = CreateButtonStyle(
                buttonFontSize,
                _dangerButtonTexture,
                _dangerButtonHoverTexture,
                _buttonActiveTexture);

            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = _visuals.SliderTrackHeight * scale,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            _sliderStyle.normal.background = _transparentTexture;
            _sliderStyle.hover.background = _transparentTexture;
            _sliderStyle.active.background = _transparentTexture;

            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = _visuals.SliderThumbWidth * scale,
                fixedHeight = _visuals.SliderThumbHeight * scale,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            _sliderThumbStyle.normal.background = _sliderThumbTexture;
            _sliderThumbStyle.hover.background = _primaryButtonHoverTexture;
            _sliderThumbStyle.active.background = _buttonActiveTexture;
        }

        private GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontSize = fontSize,
                fontStyle = fontStyle,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(),
                margin = new RectOffset(),
                normal = { textColor = color },
            };
        }

        private GUIStyle CreateButtonStyle(
            int fontSize,
            Texture2D normalBackground,
            Texture2D hoverBackground,
            Texture2D activeBackground)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                wordWrap = false,
                border = new RectOffset(),
                margin = new RectOffset(),
            };
            style.normal.background = normalBackground;
            style.normal.textColor = _visuals.PrimaryTextColor;
            style.hover.background = hoverBackground;
            style.hover.textColor = _visuals.PrimaryTextColor;
            style.active.background = activeBackground;
            style.active.textColor = _visuals.PrimaryTextColor;
            style.focused.background = hoverBackground;
            style.focused.textColor = _visuals.PrimaryTextColor;
            return style;
        }

        private void EnsureTextures()
        {
            if (_transparentTexture != null)
            {
                return;
            }

            _transparentTexture = CreateSolidTexture("Pause Transparent", Color.clear);
            _primaryButtonTexture = CreateSolidTexture("Pause Primary", _visuals.AccentColor);
            _primaryButtonHoverTexture = CreateSolidTexture("Pause Primary Hover", _visuals.SliderThumbColor);
            _buttonTexture = CreateSolidTexture("Pause Button", _visuals.ButtonColor);
            _buttonHoverTexture = CreateSolidTexture("Pause Button Hover", _visuals.ButtonHoverColor);
            _buttonActiveTexture = CreateSolidTexture("Pause Button Active", _visuals.ButtonActiveColor);
            _dangerButtonTexture = CreateSolidTexture("Pause Danger", _visuals.DangerButtonColor);
            _dangerButtonHoverTexture = CreateSolidTexture("Pause Danger Hover", _visuals.DangerButtonHoverColor);
            _sliderThumbTexture = CreateSolidTexture("Pause Slider Thumb", _visuals.SliderThumbColor);
        }

        private static Texture2D CreateSolidTexture(string textureName, Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void RestartRun()
        {
            _audio?.FlushPreferences();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void QuitGame()
        {
            _audio?.FlushPreferences();
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.Died -= HandleDeath;
            }
            if (IsPaused && (_health == null || !_health.IsDead))
            {
                Time.timeScale = 1f;
            }

            DestroyTexture(ref _transparentTexture);
            DestroyTexture(ref _primaryButtonTexture);
            DestroyTexture(ref _primaryButtonHoverTexture);
            DestroyTexture(ref _buttonTexture);
            DestroyTexture(ref _buttonHoverTexture);
            DestroyTexture(ref _buttonActiveTexture);
            DestroyTexture(ref _dangerButtonTexture);
            DestroyTexture(ref _dangerButtonHoverTexture);
            DestroyTexture(ref _sliderThumbTexture);
        }

        private void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
        }
    }
}
