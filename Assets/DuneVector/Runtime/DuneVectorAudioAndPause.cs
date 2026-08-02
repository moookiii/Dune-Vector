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

        public static DuneVectorAudioManager Instance { get; private set; }

        [Serializable]
        private sealed class AudioPreferencesData
        {
            public int Version = 1;
            public float MusicVolume;
            public float SoundEffectsVolume;
        }

        public float MusicVolume { get; private set; }
        public float SoundEffectsVolume { get; private set; }

        public bool TryGetMusicChannelGroup(out FMOD.ChannelGroup channelGroup)
        {
            channelGroup = default;
            return _musicInstance.isValid()
                && _musicInstance.getChannelGroup(out channelGroup) == FMOD.RESULT.OK
                && channelGroup.hasHandle();
        }

        private AudioTuning _settings;
        private EventInstance _musicInstance;
        private EventInstance _flightBoostInstance;
        private bool _flightBoostFadingOut;
        private bool _flightBoostNeedsRandomSeek;
        private float _flightBoostVolume;
        private Bus _masterBus;
        private Bus _musicBus;
        private Bus _soundEffectsBus;
        private bool _hasMasterBus;
        private bool _hasMusicBus;
        private bool _hasSoundEffectsBus;
        private DroneHealth _health;
        private DroneCharacterController _drone;
        private DroneLockOnController _lockOnController;
        private float _masterFullVolume = 1f;
        private float _masterCurrentVolume = 1f;
        private float _masterTargetVolume = 1f;
        private string _preferencesPath;
        private bool _preferencesDirty;
        private bool _initialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        public void Initialize(
            AudioTuning settings,
            DroneHealth health,
            DroneCharacterController drone)
        {
            _settings = settings;
            if (_settings == null)
            {
                Debug.LogError("Dune Vector audio requires Audio Tuning in the Runtime Settings asset.", this);
                enabled = false;
                return;
            }

            BindHealth(health);
            BindDrone(drone);
            if (_initialized)
            {
                enabled = true;
                return;
            }

            _initialized = true;
            _preferencesPath = Path.Combine(Application.persistentDataPath, AudioPreferencesFileName);
            LoadStoredVolumes();

            _hasMasterBus = TryGetBus(_settings.MasterBusPath, out _masterBus);
            if (_hasMasterBus && _masterBus.getVolume(out _masterFullVolume) == FMOD.RESULT.OK)
            {
                _masterCurrentVolume = _masterFullVolume;
                _masterTargetVolume = _masterFullVolume;
            }
            _hasMusicBus = TryGetBus(_settings.MusicBusPath, out _musicBus);
            _hasSoundEffectsBus = TryGetBus(_settings.SoundEffectsBusPath, out _soundEffectsBus);
            ApplyMixerVolumes();
            StartBackgroundMusic();
        }

        private void BindHealth(DroneHealth health)
        {
            if (_health != null)
            {
                _health.Damaged -= HandleDroneDamaged;
            }

            _health = health;
            if (_health != null)
            {
                _health.Damaged += HandleDroneDamaged;
            }
        }

        private void BindDrone(DroneCharacterController drone)
        {
            if (_drone != drone)
            {
                ReleaseFlightBoostAudio();
            }
            _drone = drone;
        }

        private void Update()
        {
            UpdatePauseDucking();
            UpdateFlightBoostAudio();
            if (_musicInstance.isValid()
                && _musicInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK
                && playbackState == PLAYBACK_STATE.STOPPED)
            {
                _musicInstance.start();
            }
        }

        private void UpdateFlightBoostAudio()
        {
            bool shouldPlay = _drone != null
                && _drone.CurrentMode == DroneTraversalMode.Flight
                && _drone.IsBoosting;
            if (!shouldPlay)
            {
                UpdateFlightBoostFadeOut();
                return;
            }

            if (!_flightBoostInstance.isValid())
            {
                StartFlightBoostAudio();
                return;
            }

            if (_flightBoostFadingOut)
            {
                _flightBoostFadingOut = false;
            }

            UpdateFlightBoostFadeIn();
            TryRandomizeFlightBoostPlaybackPosition();
            if (_flightBoostInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK
                && playbackState == PLAYBACK_STATE.STOPPED)
            {
                _flightBoostInstance.setTimelinePosition(0);
                _flightBoostInstance.start();
            }
        }

        private void StartFlightBoostAudio()
        {
            if (string.IsNullOrWhiteSpace(_settings.DroneFlightBoostEvent))
            {
                return;
            }

            try
            {
                _flightBoostInstance = RuntimeManager.CreateInstance(_settings.DroneFlightBoostEvent);
                RuntimeManager.AttachInstanceToGameObject(_flightBoostInstance, _drone.gameObject);
                if (_flightBoostInstance.getDescription(out EventDescription description) == FMOD.RESULT.OK
                    && description.getLength(out int lengthMilliseconds) == FMOD.RESULT.OK
                    && lengthMilliseconds > 1)
                {
                    _flightBoostInstance.setTimelinePosition(UnityEngine.Random.Range(0, lengthMilliseconds));
                }
                _flightBoostFadingOut = false;
                _flightBoostVolume = 0f;
                _flightBoostInstance.setVolume(_flightBoostVolume);
                _flightBoostInstance.start();
                _flightBoostNeedsRandomSeek = true;
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"FMOD flight boost event '{_settings.DroneFlightBoostEvent}' was not found. {exception.Message}",
                    this);
                ReleaseFlightBoostAudio();
            }
        }

        private void TryRandomizeFlightBoostPlaybackPosition()
        {
            if (!_flightBoostNeedsRandomSeek
                || _flightBoostInstance.getChannelGroup(out FMOD.ChannelGroup channelGroup) != FMOD.RESULT.OK
                || channelGroup.getNumChannels(out int channelCount) != FMOD.RESULT.OK)
            {
                return;
            }

            for (int i = 0; i < channelCount; i++)
            {
                if (channelGroup.getChannel(i, out FMOD.Channel channel) != FMOD.RESULT.OK
                    || channel.getCurrentSound(out FMOD.Sound sound) != FMOD.RESULT.OK
                    || sound.getLength(out uint lengthMilliseconds, FMOD.TIMEUNIT.MS) != FMOD.RESULT.OK
                    || lengthMilliseconds <= 1)
                {
                    continue;
                }

                uint randomPosition = (uint)UnityEngine.Random.Range(
                    0,
                    (int)Math.Min(lengthMilliseconds, int.MaxValue));
                if (channel.setPosition(randomPosition, FMOD.TIMEUNIT.MS) == FMOD.RESULT.OK)
                {
                    _flightBoostNeedsRandomSeek = false;
                    return;
                }
            }
        }

        private void UpdateFlightBoostFadeIn()
        {
            float duration = Mathf.Max(0f, _settings.DroneFlightBoostFadeInDuration);
            _flightBoostVolume = duration <= Mathf.Epsilon
                ? 1f
                : Mathf.MoveTowards(
                    _flightBoostVolume,
                    1f,
                    Time.unscaledDeltaTime / duration);
            _flightBoostInstance.setVolume(_flightBoostVolume);
        }

        private void UpdateFlightBoostFadeOut()
        {
            if (!_flightBoostInstance.isValid())
            {
                _flightBoostFadingOut = false;
                _flightBoostVolume = 0f;
                return;
            }

            float duration = Mathf.Max(0f, _settings.DroneFlightBoostFadeOutDuration);
            if (duration <= Mathf.Epsilon)
            {
                ReleaseFlightBoostAudio();
                return;
            }

            _flightBoostFadingOut = true;
            _flightBoostVolume = Mathf.MoveTowards(
                _flightBoostVolume,
                0f,
                Time.unscaledDeltaTime / duration);
            _flightBoostInstance.setVolume(_flightBoostVolume);
            if (_flightBoostVolume <= Mathf.Epsilon)
            {
                ReleaseFlightBoostAudio();
            }
        }

        private void ReleaseFlightBoostAudio()
        {
            _flightBoostFadingOut = false;
            _flightBoostNeedsRandomSeek = false;
            _flightBoostVolume = 0f;
            if (!_flightBoostInstance.isValid())
            {
                return;
            }

            _flightBoostInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _flightBoostInstance.release();
            _flightBoostInstance.clearHandle();
        }

        public void SetPausedDucking(bool paused)
        {
            float pausedMultiplier = Mathf.Clamp01(_settings.PausedVolumeMultiplier);
            _masterTargetVolume = _masterFullVolume * (paused ? pausedMultiplier : 1f);
            if (_settings.PauseFadeDuration <= 0f)
            {
                ApplyMasterVolume(_masterTargetVolume);
            }
        }

        public void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            if (_hasMusicBus && _musicBus.isValid())
            {
                ApplyBusVolumeAndMute(_musicBus, MusicVolume);
            }
            else if (_musicInstance.isValid())
            {
                ApplyMusicInstanceVolumeAndMute();
            }
            _preferencesDirty = true;
        }

        public void SetSoundEffectsVolume(float volume)
        {
            SoundEffectsVolume = Mathf.Clamp01(volume);
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                ApplyBusVolumeAndMute(_soundEffectsBus, SoundEffectsVolume);
            }
            _preferencesDirty = true;
        }

        public void PlayDroneFire(Vector3 position)
        {
            PlayConfiguredOneShot(_settings != null ? _settings.DroneFireEvent : null, position, "drone-fire");
        }

        public void PlayFlightRingSwoosh(Vector3 position)
        {
            PlayConfiguredOneShot(_settings != null ? _settings.FlightRingSwooshEvent : null, position, "flight-ring swoosh");
        }

        public void PlayDeliveryRing(Vector3 position)
        {
            PlayConfiguredOneShot(_settings != null ? _settings.DeliveryRingEvent : null, position, "delivery ring");
        }

        public void BindLockOnController(DroneLockOnController lockOnController)
        {
            if (_lockOnController != null)
            {
                _lockOnController.StateChanged -= HandleLockOnStateChanged;
            }

            _lockOnController = lockOnController;
            if (_lockOnController != null)
            {
                _lockOnController.StateChanged += HandleLockOnStateChanged;
            }
        }

        private void HandleLockOnStateChanged(DroneLockOnState state)
        {
            string eventPath = state switch
            {
                DroneLockOnState.TargetDetected => _settings.LockOnEvent,
                DroneLockOnState.Locked => _settings.LockOnFullEvent,
                _ => null,
            };
            Vector3 position = _health != null ? _health.transform.position : transform.position;
            PlayConfiguredOneShot(eventPath, position, "lock-on");
        }

        private void PlayConfiguredOneShot(string eventPath, Vector3 position, string label)
        {
            if (IsMuted(SoundEffectsVolume) || string.IsNullOrWhiteSpace(eventPath))
            {
                return;
            }

            try
            {
                RuntimeManager.PlayOneShot(eventPath, position);
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"FMOD {label} event '{eventPath}' was not found. {exception.Message}",
                    this);
            }
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
                if (!_hasMusicBus && IsMuted(MusicVolume))
                {
                    _musicInstance.setPaused(true);
                }
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
                ApplyBusVolumeAndMute(_musicBus, MusicVolume);
            }
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                ApplyBusVolumeAndMute(_soundEffectsBus, SoundEffectsVolume);
            }
        }

        private void ApplyMusicInstanceVolumeAndMute()
        {
            bool muted = IsMuted(MusicVolume);
            if (muted)
            {
                _musicInstance.setPaused(true);
                _musicInstance.setVolume(0f);
                return;
            }

            _musicInstance.setVolume(MusicVolume);
            _musicInstance.setPaused(false);
        }

        private static void ApplyBusVolumeAndMute(Bus bus, float volume)
        {
            bool muted = IsMuted(volume);
            if (muted)
            {
                bus.setMute(true);
                bus.setVolume(0f);
                return;
            }

            bus.setVolume(volume);
            bus.setMute(false);
        }

        private static bool IsMuted(float volume)
        {
            return volume <= Mathf.Epsilon;
        }

        private void UpdatePauseDucking()
        {
            if (!_hasMasterBus || !_masterBus.isValid() || Mathf.Approximately(_masterCurrentVolume, _masterTargetVolume))
            {
                return;
            }

            float duration = Mathf.Max(0f, _settings.PauseFadeDuration);
            if (duration <= 0f)
            {
                ApplyMasterVolume(_masterTargetVolume);
                return;
            }

            float pausedVolume = _masterFullVolume * Mathf.Clamp01(_settings.PausedVolumeMultiplier);
            float fullFadeDistance = Mathf.Abs(_masterFullVolume - pausedVolume);
            float maximumDelta = fullFadeDistance * (Time.unscaledDeltaTime / duration);
            ApplyMasterVolume(Mathf.MoveTowards(_masterCurrentVolume, _masterTargetVolume, maximumDelta));
        }

        private void ApplyMasterVolume(float volume)
        {
            _masterCurrentVolume = volume;
            if (_hasMasterBus && _masterBus.isValid())
            {
                _masterBus.setVolume(_masterCurrentVolume);
            }
        }

        private void HandleDroneDamaged(float appliedDamage)
        {
            if (IsMuted(SoundEffectsVolume)
                || appliedDamage <= 0f
                || string.IsNullOrWhiteSpace(_settings.DroneDamageEvent))
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
            if (Instance == this)
            {
                Instance = null;
            }
            if (_lockOnController != null)
            {
                _lockOnController.StateChanged -= HandleLockOnStateChanged;
            }
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
            ReleaseFlightBoostAudio();
            if (_hasMasterBus && _masterBus.isValid())
            {
                _masterBus.setVolume(_masterFullVolume);
            }
            FlushPreferences();
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorPauseMenu : MonoBehaviour
    {
        public bool IsPaused { get; private set; }
        public bool IsCompendiumOpen => IsPaused && _showCompendium;

        private DronePlayer _player;
        private DroneHealth _health;
        private DuneVectorAudioManager _audio;
        private DroneGoldWallet _wallet;
        private DronePermanentUpgradeSystem _upgrades;
        private PauseMenuVisualTuning _visuals;
        private DuneVectorUpgradeShopView _shopView;
        private DuneVectorCourierGame _courierGame;
        private DuneVectorPhotographySystem _photography;
        private bool _showShop;
        private bool _showGallery;
        private bool _showCompendium;
        private bool _showControls;
        private float _controlsFade;

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
            DroneGoldWallet wallet,
            DronePermanentUpgradeSystem upgrades,
            PauseMenuVisualTuning visuals,
            UpgradeShopVisualTuning shopVisuals)
        {
            _player = player;
            _health = health;
            _audio = audio;
            _wallet = wallet;
            _upgrades = upgrades;
            _visuals = visuals;
            _shopView = new DuneVectorUpgradeShopView(_upgrades, _wallet, shopVisuals);
            if (_health != null)
            {
                _health.Died += HandleDeath;
            }
        }

        public void BindCourierGame(DuneVectorCourierGame courierGame)
        {
            _courierGame = courierGame;
        }

        public void BindPhotography(DuneVectorPhotographySystem photography)
        {
            _photography = photography;
        }

        private void Update()
        {
            UpdateControlsFade();
            if (!IsPaused || !_showCompendium)
            {
                _photography?.HideCompendium();
            }

            if (DuneVectorPhotographySystem.IsCameraModeActive)
            {
                return;
            }
            if (_courierGame != null &&
                (_courierGame.IsTerminalOpen || _courierGame.IsDeliveryMessageOpen))
            {
                return;
            }
            if (DuneVectorMapHUD.ShouldSuppressPauseMenuInput)
            {
                return;
            }
            if ((_health == null || !_health.IsDead) &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (IsPaused && (_showControls || _controlsFade > 0f))
                {
                    _showControls = false;
                }
                else if (IsPaused && _showShop)
                {
                    _showShop = false;
                }
                else if (IsPaused && _showGallery)
                {
                    if (_photography == null || !_photography.CloseGalleryViewer())
                    {
                        _showGallery = false;
                    }
                }
                else if (IsPaused && _showCompendium)
                {
                    _showCompendium = false;
                }
                else
                {
                    SetPaused(!IsPaused);
                }
            }
        }

        private void SetPaused(bool paused)
        {
            if (paused && _health != null && _health.IsDead)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused || DuneVectorMapHUD.IsWorldMapPausingGameplay
                ? 0f
                : 1f;
            _player?.SetInputEnabled(!paused);
            bool keepCursorFree = paused || DuneVectorMapHUD.IsWorldMapOpen;
            Cursor.lockState = keepCursorFree
                ? CursorLockMode.None
                : CursorLockMode.Locked;
            Cursor.visible = keepCursorFree;
            _audio?.SetPausedDucking(paused);
            if (!paused)
            {
                _showShop = false;
                _showGallery = false;
                _showCompendium = false;
                _showControls = false;
                _controlsFade = 0f;
                _audio?.FlushPreferences();
            }
        }

        private void UpdateControlsFade()
        {
            float target = IsPaused && _showControls ? 1f : 0f;
            float duration = _visuals != null ? Mathf.Max(0f, _visuals.ControlsFadeDuration) : 0f;
            _controlsFade = duration <= 0f
                ? target
                : Mathf.MoveTowards(_controlsFade, target, Time.unscaledDeltaTime / duration);
        }

        private void HandleDeath()
        {
            IsPaused = false;
            _showShop = false;
            _showGallery = false;
            _showCompendium = false;
            _showControls = false;
            _controlsFade = 0f;
            _audio?.SetPausedDucking(false);
            _player?.SetInputEnabled(false);
        }

        private void OnGUI()
        {
            if (!IsPaused || (_health != null && _health.IsDead) || _visuals == null)
            {
                return;
            }

            // UI Toolkit renders before IMGUI. Return before drawing the pause-menu
            // dimmer so it cannot darken or cover the compendium panel.
            if (_showCompendium)
            {
                if (_photography == null || _photography.DrawCompendium())
                {
                    _showCompendium = false;
                }
                return;
            }

            GUI.depth = -1000;
            float scale = CalculateScale();
            EnsureStyles(scale);

            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), _visuals.OverlayColor);

            if (_showControls || _controlsFade > 0f)
            {
                DrawControlsScreen();
                return;
            }

            if (_showShop)
            {
                if (_shopView == null || _shopView.Draw())
                {
                    _showShop = false;
                }
                return;
            }
            if (_showGallery)
            {
                if (_photography == null || _photography.DrawGallery())
                {
                    _showGallery = false;
                }
                return;
            }
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

            string galleryButtonLabel = _photography != null && _photography.Tuning != null
                ? _photography.Tuning.PauseMenuButtonLabel
                : string.Empty;
            if (GUI.Button(new Rect(content.x, y, content.width, buttonHeight), galleryButtonLabel, _secondaryButtonStyle))
            {
                _showGallery = true;
            }
            y += buttonHeight + gap;

            string compendiumButtonLabel = _photography != null && _photography.Tuning != null
                ? _photography.Tuning.CompendiumPauseMenuButtonLabel
                : string.Empty;
            if (GUI.Button(
                    new Rect(content.x, y, content.width, buttonHeight),
                    compendiumButtonLabel,
                    _secondaryButtonStyle))
            {
                _showCompendium = true;
                _photography?.ShowCompendium();
            }
            y += buttonHeight + gap;

            if (GUI.Button(new Rect(content.x, y, content.width, buttonHeight), "UPGRADE SHOP", _secondaryButtonStyle))
            {
                _showShop = true;
                _shopView?.Open();
            }
            y += buttonHeight + gap;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _courierGame != null && _courierGame.State != CourierRunState.Hub;
            if (GUI.Button(new Rect(content.x, y, content.width, buttonHeight), "RETURN TO HUB", _secondaryButtonStyle))
            {
                SetPaused(false);
                _courierGame?.RequestReturnToHub();
            }
            GUI.enabled = previousEnabled;
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
            y += buttonHeight + gap;

            if (GUI.Button(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.ControlsButtonLabel,
                    _secondaryButtonStyle))
            {
                _showControls = true;
            }
            float hintHeight = _hintStyle.lineHeight;
            GUI.Label(
                new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight),
                "ESC  /  RETURN TO THE DESERT",
                _hintStyle);
        }

        private void DrawControlsScreen()
        {
            float alpha = Mathf.Clamp01(_controlsFade);
            Color background = _visuals.ControlsBackgroundColor;
            background.a *= alpha;
            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), background);

            Texture2D controlsImage = _visuals.ControlsImage;
            if (controlsImage == null || controlsImage.width <= 0 || controlsImage.height <= 0)
            {
                return;
            }

            float imageHeight = Screen.width * ((float)controlsImage.height / controlsImage.width);
            Rect imageRect = new Rect(
                0f,
                (Screen.height - imageHeight) * 0.5f,
                Screen.width,
                imageHeight);
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(imageRect, controlsImage, ScaleMode.StretchToFill, false);
            GUI.color = previousColor;
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
            SetPaused(false);
            if (_courierGame != null)
            {
                _health?.RestoreHealth(_health.MaximumHealth);
                _player?.GetComponent<DroneCharacterController>()?.RestoreStaminaToFull();
                _courierGame.RestartAtHub();
                return;
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void QuitGame()
        {
            _audio?.SetPausedDucking(false);
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
                _audio?.SetPausedDucking(false);
                Time.timeScale = DuneVectorMapHUD.IsWorldMapPausingGameplay ? 0f : 1f;
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
            _shopView?.Dispose();
            _shopView = null;
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
