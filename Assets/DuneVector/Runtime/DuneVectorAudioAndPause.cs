using System;
using System.Collections.Generic;
using System.IO;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DuneVector
{
    public enum MusicVisualizerMode
    {
        All = 0,
        NoFlash = 1,
        Off = 2,
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorAudioManager : MonoBehaviour
    {
        private const string AudioPreferencesFileName = "DuneVectorAudio.dat";

        public static DuneVectorAudioManager Instance { get; private set; }

        [Serializable]
        private sealed class AudioPreferencesData
        {
            public int Version = 13;
            public float MusicVolume;
            public float SoundEffectsVolume;
            public float DialogueVolume;
            public bool MusicVisualizerEnabled = true;
            public int MusicVisualizerMode;
            public bool ChromaticAberrationEnabled = true;
            public bool LensDistortionEnabled = true;
            public bool CrtLinesEnabled = true;
            public bool FilmGrainEnabled = true;
            public bool VignetteEnabled = true;
            public bool BloomEnabled = true;
            public int AntiAliasingMode;
            public bool VisualizerFovEnabled;
            public int MusicVisualizerEffectMask;
        }

        public float MusicVolume { get; private set; }
        public float SoundEffectsVolume { get; private set; }
        public float DialogueVolume { get; private set; }
        public MusicVisualizerMode VisualizerMode { get; private set; } = MusicVisualizerMode.All;
        public bool ChromaticAberrationEnabled { get; private set; } = true;
        public bool LensDistortionEnabled { get; private set; } = true;
        public bool CrtLinesEnabled { get; private set; } = true;
        public bool FilmGrainEnabled { get; private set; } = true;
        public bool VignetteEnabled { get; private set; } = true;
        public bool BloomEnabled { get; private set; } = true;
        public DuneVectorCameraAntiAliasingMode AntiAliasingMode { get; private set; }
        public bool VisualizerFovEnabled { get; private set; }
        public MusicVisualEffectGroups VisualizerEffectMask { get; private set; }
        public event Action<MusicVisualizerMode> MusicVisualizerModeChanged;
        public event Action<MusicVisualEffectGroups> MusicVisualizerEffectsChanged;
        public event Action<bool> VisualizerFovEnabledChanged;
        public event Action<MusicPlaylistTrack> ActiveMusicTrackChanged;
        public event Action<bool> MusicPlaybackPausedChanged;
        public MusicTimelineState TimelineState => _timelineState;
        public MusicPlaylistTrack ActiveMusicTrack { get; private set; }
        public MusicVisualTrackProfile ActiveMusicTrackProfile => ActiveMusicTrack != null
            ? ActiveMusicTrack.VisualizerProfile
            : _musicReactiveSettings != null ? _musicReactiveSettings.TrackProfile : null;
        public MusicReactiveSkyTuning ActiveMusicReactiveSkySettings => ActiveMusicTrackProfile != null
            && ActiveMusicTrackProfile.ReactiveSkySettings != null
                ? ActiveMusicTrackProfile.ReactiveSkySettings
                : _musicReactiveSettings;
        public string ActiveMusicDisplayName => ActiveMusicTrack != null
            ? ActiveMusicTrack.DisplayName
            : string.Empty;
        public string MusicEventPath => ActiveMusicTrack != null
            ? ActiveMusicTrack.FmodEventPath
            : string.Empty;
        public bool IsMusicPlaybackPaused => _userMusicPaused;
        public int MusicEventLengthMilliseconds { get; private set; }
        public bool MusicTimelineCallbackRegistered { get; private set; }

        public bool TryGetMusicChannelGroup(out FMOD.ChannelGroup channelGroup)
        {
            channelGroup = default;
            return _musicInstance.isValid()
                && _musicInstance.getChannelGroup(out channelGroup) == FMOD.RESULT.OK
                && channelGroup.hasHandle();
        }

        private AudioTuning _settings;
        private MusicReactiveSkyTuning _musicReactiveSettings;
        private EventInstance _musicInstance;
        private MusicTimelineCallbackBridge _timelineBridge;
        private MusicTimelineState _timelineState;
        private uint _musicPlaybackGeneration;
        private MusicPlaylistTrack[] _musicPlaylist = Array.Empty<MusicPlaylistTrack>();
        private int[] _musicPlayOrder = Array.Empty<int>();
        private int _musicPlayOrderPosition;
        private bool _userMusicPaused;
        private bool _gameplayPaused;
        private int _lastPolledTimelinePosition;
        private bool _restartIssuedForStoppedState;
        private EventInstance _flightBoostInstance;
        private bool _flightBoostFadingOut;
        private bool _flightBoostNeedsRandomSeek;
        private float _flightBoostVolume;
        private Bus _masterBus;
        private Bus _musicBus;
        private Bus _soundEffectsBus;
        private Bus _dialogueBus;
        private bool _hasMasterBus;
        private bool _hasMusicBus;
        private bool _hasSoundEffectsBus;
        private bool _hasDialogueBus;
        private float _musicDuckMultiplier = 1f;
        private DroneHealth _health;
        private DroneCharacterController _drone;
        private DroneLockOnController _lockOnController;
        private float _masterFullVolume = 1f;
        private float _masterCurrentVolume = 1f;
        private float _masterTargetVolume = 1f;
        private string _preferencesPath;
        private bool _preferencesDirty;
        private bool _initialized;
        private DuneVectorCameraAntiAliasingMode _defaultAntiAliasingMode;

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
            MusicReactiveSkyTuning musicReactiveSettings,
            DroneHealth health,
            DroneCharacterController drone,
            DuneVectorCameraAntiAliasingMode defaultAntiAliasingMode)
        {
            _settings = settings;
            _musicReactiveSettings = musicReactiveSettings;
            _defaultAntiAliasingMode = defaultAntiAliasingMode;
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
            _hasDialogueBus = TryGetBus(_settings.DialogueBusPath, out _dialogueBus);
            ApplyMixerVolumes();
            InitializeMusicPlaylist();
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
            UpdateMusicTimelineState();
            if (_musicInstance.isValid()
                && _musicInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK)
            {
                if (playbackState == PLAYBACK_STATE.STOPPED && !_restartIssuedForStoppedState)
                {
                    _restartIssuedForStoppedState = true;
                    PlayNextMusicTrack();
                }
                else if (playbackState != PLAYBACK_STATE.STOPPED)
                {
                    _restartIssuedForStoppedState = false;
                }
            }
        }

        private void UpdateMusicTimelineState()
        {
            if (_timelineBridge == null)
            {
                _timelineState.IsValid = false;
                return;
            }

            _timelineBridge.Consume(ref _timelineState);
            _timelineState.IsValid = _musicInstance.isValid();
            if (!_timelineState.IsValid)
            {
                return;
            }

            if (_musicInstance.getTimelinePosition(out int position) == FMOD.RESULT.OK)
            {
                MusicReactiveSkyTuning activeReactiveSettings = ActiveMusicReactiveSkySettings;
                int jumpThreshold = activeReactiveSettings != null
                    ? Mathf.Max(250, activeReactiveSettings.TimelineJumpThresholdMilliseconds)
                    : 2000;
                int delta = position - _lastPolledTimelinePosition;
                if (delta < 0 || delta > jumpThreshold)
                {
                    _timelineState.DiscontinuousJump = true;
                    _timelineState.SeekGeneration++;
                }
                _timelineState.TimelinePositionMilliseconds = position;
                _lastPolledTimelinePosition = position;
            }
            if (_musicInstance.getPaused(out bool paused) == FMOD.RESULT.OK)
            {
                _timelineState.IsPaused = paused || _gameplayPaused;
            }
            if (_musicInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK)
            {
                _timelineState.IsPlaying = playbackState == PLAYBACK_STATE.PLAYING
                    || playbackState == PLAYBACK_STATE.STARTING
                    || playbackState == PLAYBACK_STATE.SUSTAINING;
            }
        }

        public bool SeekMusicTimeline(int timelineMilliseconds)
        {
            if (!_musicInstance.isValid())
            {
                return false;
            }
            int clamped = Mathf.Max(0, timelineMilliseconds);
            if (_musicInstance.setTimelinePosition(clamped) != FMOD.RESULT.OK)
            {
                return false;
            }
            _lastPolledTimelinePosition = clamped;
            _timelineState.TimelinePositionMilliseconds = clamped;
            _timelineState.SeekGeneration++;
            _timelineState.DiscontinuousJump = true;
            return true;
        }

        public bool RestartMusicTimeline()
        {
            if (!_musicInstance.isValid())
            {
                return false;
            }
            _musicPlaybackGeneration++;
            _timelineBridge?.SetGeneration(_musicPlaybackGeneration);
            _timelineState.PlaybackGeneration = _musicPlaybackGeneration;
            _timelineState.SeekGeneration++;
            _lastPolledTimelinePosition = 0;
            _restartIssuedForStoppedState = false;
            return _musicInstance.setTimelinePosition(0) == FMOD.RESULT.OK
                && _musicInstance.start() == FMOD.RESULT.OK;
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
            _gameplayPaused = paused;
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
                ApplyBusVolumeAndMute(_musicBus, EffectiveMusicVolume);
            }
            else if (_musicInstance.isValid())
            {
                ApplyMusicInstanceVolumeAndMute();
            }
            _preferencesDirty = true;
        }

        public void SetMusicDuckMultiplier(float multiplier)
        {
            _musicDuckMultiplier = Mathf.Clamp01(multiplier);
            if (_hasMusicBus && _musicBus.isValid())
            {
                ApplyBusVolumeAndMute(_musicBus, EffectiveMusicVolume);
            }
            else if (_musicInstance.isValid())
            {
                ApplyMusicInstanceVolumeAndMute();
            }
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

        public void SetDialogueVolume(float volume)
        {
            DialogueVolume = Mathf.Clamp01(volume);
            if (_hasDialogueBus && _dialogueBus.isValid())
            {
                ApplyBusVolumeAndMute(_dialogueBus, DialogueVolume);
            }
            _preferencesDirty = true;
        }

        public void SetMusicVisualizerMode(MusicVisualizerMode mode)
        {
            if (!Enum.IsDefined(typeof(MusicVisualizerMode), mode))
            {
                mode = MusicVisualizerMode.All;
            }
            if (VisualizerMode == mode)
            {
                return;
            }

            VisualizerMode = mode;
            _preferencesDirty = true;
            MusicVisualizerModeChanged?.Invoke(mode);
            FlushPreferences();
        }

        public bool IsMusicVisualizerEffectEnabled(MusicVisualEffectGroups effects)
        {
            return (VisualizerEffectMask & effects) == effects;
        }

        public void SetMusicVisualizerEffectEnabled(MusicVisualEffectGroups effects, bool enabled)
        {
            MusicVisualEffectGroups next = enabled
                ? VisualizerEffectMask | effects
                : VisualizerEffectMask & ~effects;
            next &= MusicVisualEffectGroups.All;
            if (VisualizerEffectMask == next)
            {
                return;
            }

            VisualizerEffectMask = next;
            _preferencesDirty = true;
            MusicVisualizerEffectsChanged?.Invoke(next);
            FlushPreferences();
        }

        public void SetChromaticAberrationEnabled(bool enabled)
        {
            if (ChromaticAberrationEnabled == enabled)
            {
                return;
            }

            ChromaticAberrationEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetLensDistortionEnabled(bool enabled)
        {
            if (LensDistortionEnabled == enabled)
            {
                return;
            }

            LensDistortionEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetCrtLinesEnabled(bool enabled)
        {
            if (CrtLinesEnabled == enabled)
            {
                return;
            }

            CrtLinesEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetFilmGrainEnabled(bool enabled)
        {
            if (FilmGrainEnabled == enabled)
            {
                return;
            }

            FilmGrainEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetVignetteEnabled(bool enabled)
        {
            if (VignetteEnabled == enabled)
            {
                return;
            }

            VignetteEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetBloomEnabled(bool enabled)
        {
            if (BloomEnabled == enabled)
            {
                return;
            }

            BloomEnabled = enabled;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetAntiAliasingMode(DuneVectorCameraAntiAliasingMode mode)
        {
            if (!Enum.IsDefined(typeof(DuneVectorCameraAntiAliasingMode), mode))
            {
                mode = DuneVectorCameraAntiAliasingMode.None;
            }
            if (AntiAliasingMode == mode)
            {
                return;
            }

            AntiAliasingMode = mode;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void SetVisualizerFovEnabled(bool enabled)
        {
            if (VisualizerFovEnabled == enabled)
            {
                return;
            }
            VisualizerFovEnabled = enabled;
            _preferencesDirty = true;
            VisualizerFovEnabledChanged?.Invoke(enabled);
            FlushPreferences();
        }

        public void ResetVideoSettingsToDefaults()
        {
            PauseMenuVisualTuning defaults = _settings != null ? _settings.PauseMenu : null;
            ChromaticAberrationEnabled = defaults != null && defaults.DefaultChromaticAberrationEnabled;
            LensDistortionEnabled = defaults != null && defaults.DefaultLensDistortionEnabled;
            CrtLinesEnabled = defaults == null || defaults.DefaultCrtLinesEnabled;
            FilmGrainEnabled = defaults != null && defaults.DefaultFilmGrainEnabled;
            VignetteEnabled = defaults != null && defaults.DefaultVignetteEnabled;
            BloomEnabled = defaults != null && defaults.DefaultBloomEnabled;
            AntiAliasingMode = _defaultAntiAliasingMode == DuneVectorCameraAntiAliasingMode.TemporalAntiAliasing
                ? DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing
                : _defaultAntiAliasingMode;
            _preferencesDirty = true;
            FlushPreferences();
        }

        public void ResetMusicVisualizerSettingsToDefaults()
        {
            PauseMenuVisualTuning defaults = _settings != null ? _settings.PauseMenu : null;
            MusicVisualizerMode nextMode = defaults == null || defaults.DefaultMusicVisualizerEnabled
                ? MusicVisualizerMode.All
                : MusicVisualizerMode.Off;
            MusicVisualEffectGroups nextMask = defaults != null
                ? defaults.BuildDefaultMusicVisualizerEffectMask()
                : MusicVisualEffectGroups.All;
            bool nextFov = defaults != null && defaults.DefaultVisualizerFovEnabled;
            bool modeChanged = VisualizerMode != nextMode;
            bool maskChanged = VisualizerEffectMask != nextMask;
            bool fovChanged = VisualizerFovEnabled != nextFov;
            VisualizerMode = nextMode;
            VisualizerEffectMask = nextMask;
            VisualizerFovEnabled = nextFov;
            _preferencesDirty = true;
            if (modeChanged)
            {
                MusicVisualizerModeChanged?.Invoke(nextMode);
            }
            if (maskChanged)
            {
                MusicVisualizerEffectsChanged?.Invoke(nextMask);
            }
            if (fovChanged)
            {
                VisualizerFovEnabledChanged?.Invoke(nextFov);
            }
            FlushPreferences();
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

        public void PlayVesperMissileAlert(Vector3 position)
        {
            PlayConfiguredOneShot(
                _settings != null ? _settings.VesperMissileAlertEvent : null,
                position,
                "vesper missile alert");
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

        public void PlayPreviousMusicTrack()
        {
            if (_musicPlayOrder.Length == 0)
            {
                return;
            }

            _musicPlayOrderPosition = (_musicPlayOrderPosition - 1 + _musicPlayOrder.Length)
                % _musicPlayOrder.Length;
            StartMusicTrackAtCurrentPosition();
        }

        public void ToggleMusicPlayback()
        {
            SetMusicPlaybackPaused(!_userMusicPaused);
        }

        public void SetMusicPlaybackPaused(bool paused)
        {
            if (_userMusicPaused == paused)
            {
                return;
            }
            _userMusicPaused = paused;
            if (_musicInstance.isValid())
            {
                bool shouldPauseInstance = paused || (!_hasMusicBus && IsMuted(EffectiveMusicVolume));
                _musicInstance.setPaused(shouldPauseInstance);
                _timelineState.IsPaused = shouldPauseInstance || _gameplayPaused;
            }
            MusicPlaybackPausedChanged?.Invoke(paused);
        }

        public void PlayNextMusicTrack()
        {
            if (_musicPlayOrder.Length == 0)
            {
                return;
            }

            _musicPlayOrderPosition++;
            if (_musicPlayOrderPosition >= _musicPlayOrder.Length)
            {
                int previousTrackIndex = _musicPlayOrder[_musicPlayOrder.Length - 1];
                ShuffleMusicPlayOrder(previousTrackIndex);
                _musicPlayOrderPosition = 0;
            }
            StartMusicTrackAtCurrentPosition();
        }

        private void InitializeMusicPlaylist()
        {
            List<MusicPlaylistTrack> validTracks = new List<MusicPlaylistTrack>();
            MusicPlaylistTrack[] authoredPlaylist = _settings.BackgroundMusicPlaylist;
            if (authoredPlaylist != null)
            {
                for (int i = 0; i < authoredPlaylist.Length; i++)
                {
                    MusicPlaylistTrack track = authoredPlaylist[i];
                    if (track != null && !string.IsNullOrWhiteSpace(track.FmodEventPath))
                    {
                        validTracks.Add(track);
                    }
                }
            }

            if (validTracks.Count == 0 && !string.IsNullOrWhiteSpace(_settings.BackgroundMusicEvent))
            {
                validTracks.Add(new MusicPlaylistTrack
                {
                    DisplayName = _settings.BackgroundMusicEvent,
                    FmodEventPath = _settings.BackgroundMusicEvent,
                    VisualizerProfile = _musicReactiveSettings != null ? _musicReactiveSettings.TrackProfile : null,
                });
            }

            _musicPlaylist = validTracks.ToArray();
            _musicPlayOrder = new int[_musicPlaylist.Length];
            for (int i = 0; i < _musicPlayOrder.Length; i++)
            {
                _musicPlayOrder[i] = i;
            }
            ShuffleMusicPlayOrder(-1);
            if (_musicPlayOrder.Length > 1)
            {
                int startingTrackIndex = Mathf.Clamp(
                    _settings.StartingBackgroundMusicTrackIndex,
                    0,
                    _musicPlayOrder.Length - 1);
                int shuffledPosition = Array.IndexOf(_musicPlayOrder, startingTrackIndex);
                (_musicPlayOrder[0], _musicPlayOrder[shuffledPosition]) =
                    (_musicPlayOrder[shuffledPosition], _musicPlayOrder[0]);
            }
            _musicPlayOrderPosition = 0;
        }

        private void ShuffleMusicPlayOrder(int trackIndexToAvoidFirst)
        {
            for (int i = _musicPlayOrder.Length - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (_musicPlayOrder[i], _musicPlayOrder[swapIndex]) =
                    (_musicPlayOrder[swapIndex], _musicPlayOrder[i]);
            }

            if (_musicPlayOrder.Length > 1 && _musicPlayOrder[0] == trackIndexToAvoidFirst)
            {
                int swapIndex = UnityEngine.Random.Range(1, _musicPlayOrder.Length);
                (_musicPlayOrder[0], _musicPlayOrder[swapIndex]) =
                    (_musicPlayOrder[swapIndex], _musicPlayOrder[0]);
            }
        }

        private void StartBackgroundMusic()
        {
            if (_musicPlayOrder.Length == 0)
            {
                return;
            }

            StartMusicTrackAtCurrentPosition();
        }

        private void StartMusicTrackAtCurrentPosition()
        {
            if (_musicPlayOrderPosition < 0 || _musicPlayOrderPosition >= _musicPlayOrder.Length)
            {
                return;
            }

            for (int attempt = 0; attempt < _musicPlayOrder.Length; attempt++)
            {
                MusicPlaylistTrack track = _musicPlaylist[_musicPlayOrder[_musicPlayOrderPosition]];
                if (TryStartMusicTrack(track))
                {
                    return;
                }
                _musicPlayOrderPosition = (_musicPlayOrderPosition + 1) % _musicPlayOrder.Length;
            }
            ActiveMusicTrack = null;
            Debug.LogError("No authored FMOD background-music event could be started.", this);
        }

        private bool TryStartMusicTrack(MusicPlaylistTrack track)
        {
            ReleaseCurrentMusic(FMOD.Studio.STOP_MODE.IMMEDIATE);

            try
            {
                _musicInstance = RuntimeManager.CreateInstance(track.FmodEventPath);
                ActiveMusicTrack = track;
                _musicPlaybackGeneration++;
                MusicReactiveSkyTuning activeReactiveSettings = ActiveMusicReactiveSkySettings;
                int queueCapacity = activeReactiveSettings != null
                    ? activeReactiveSettings.TimelineCallbackQueueCapacity
                    : 128;
                _timelineBridge = new MusicTimelineCallbackBridge(queueCapacity);
                bool callbackRegistered = _timelineBridge.Attach(_musicInstance, _musicPlaybackGeneration);
                MusicTimelineCallbackRegistered = callbackRegistered;
                if (_musicInstance.getDescription(out EventDescription musicDescription) == FMOD.RESULT.OK)
                {
                    musicDescription.getLength(out int eventLength);
                    MusicEventLengthMilliseconds = Mathf.Max(0, eventLength);
                }
                _timelineState = new MusicTimelineState
                {
                    IsValid = true,
                    TrackId = track.VisualizerProfile != null
                        ? track.VisualizerProfile.StableTrackHash
                        : 0,
                    PlaybackGeneration = _musicPlaybackGeneration,
                };
                if (!callbackRegistered)
                {
                    Debug.LogWarning("FMOD music timeline callback registration failed; authored beat and marker diagnostics are unavailable.", this);
                }
                if (!_hasMusicBus)
                {
                    ApplyMusicInstanceVolumeAndMute();
                }
                _musicInstance.start();
                if (_userMusicPaused)
                {
                    _musicInstance.setPaused(true);
                }
                _lastPolledTimelinePosition = 0;
                _restartIssuedForStoppedState = false;
                ActiveMusicTrackChanged?.Invoke(track);
                return true;
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"FMOD background event '{track.FmodEventPath}' was not found. {exception.Message}",
                    this);
                return false;
            }
        }

        private void ReleaseCurrentMusic(FMOD.Studio.STOP_MODE stopMode)
        {
            _timelineBridge?.Dispose();
            _timelineBridge = null;
            MusicTimelineCallbackRegistered = false;
            if (!_musicInstance.isValid())
            {
                return;
            }

            _musicInstance.stop(stopMode);
            _musicInstance.release();
            _musicInstance.clearHandle();
        }

        private void ApplyMixerVolumes()
        {
            if (_hasMusicBus && _musicBus.isValid())
            {
                ApplyBusVolumeAndMute(_musicBus, EffectiveMusicVolume);
            }
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                ApplyBusVolumeAndMute(_soundEffectsBus, SoundEffectsVolume);
            }
            if (_hasDialogueBus && _dialogueBus.isValid())
            {
                ApplyBusVolumeAndMute(_dialogueBus, DialogueVolume);
            }
        }

        private void ApplyMusicInstanceVolumeAndMute()
        {
            float effectiveVolume = EffectiveMusicVolume;
            bool muted = IsMuted(effectiveVolume);
            if (muted || _userMusicPaused)
            {
                _musicInstance.setPaused(true);
                _musicInstance.setVolume(muted ? 0f : effectiveVolume);
                return;
            }

            _musicInstance.setVolume(effectiveVolume);
            _musicInstance.setPaused(false);
        }

        private float EffectiveMusicVolume => MusicVolume * _musicDuckMultiplier;

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
            DialogueVolume = Mathf.Clamp01(_settings.DefaultDialogueVolume);
            PauseMenuVisualTuning defaults = _settings.PauseMenu;
            VisualizerMode = defaults == null || defaults.DefaultMusicVisualizerEnabled
                ? MusicVisualizerMode.All
                : MusicVisualizerMode.Off;
            VisualizerEffectMask = defaults != null
                ? defaults.BuildDefaultMusicVisualizerEffectMask()
                : MusicVisualEffectGroups.All;
            VisualizerFovEnabled = defaults != null && defaults.DefaultVisualizerFovEnabled;
            ChromaticAberrationEnabled = _settings.PauseMenu != null
                && _settings.PauseMenu.DefaultChromaticAberrationEnabled;
            LensDistortionEnabled = _settings.PauseMenu != null
                && _settings.PauseMenu.DefaultLensDistortionEnabled;
            CrtLinesEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultCrtLinesEnabled;
            FilmGrainEnabled = _settings.PauseMenu != null
                && _settings.PauseMenu.DefaultFilmGrainEnabled;
            VignetteEnabled = _settings.PauseMenu != null
                && _settings.PauseMenu.DefaultVignetteEnabled;
            BloomEnabled = _settings.PauseMenu != null
                && _settings.PauseMenu.DefaultBloomEnabled;
            AntiAliasingMode = _defaultAntiAliasingMode == DuneVectorCameraAntiAliasingMode.TemporalAntiAliasing
                ? DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing
                : _defaultAntiAliasingMode;
            if (!_settings.PersistVolumeSettings || !File.Exists(_preferencesPath))
            {
                return;
            }

            try
            {
                AudioPreferencesData stored = JsonUtility.FromJson<AudioPreferencesData>(File.ReadAllText(_preferencesPath));
                if (stored != null && stored.Version >= 1 && stored.Version <= 13)
                {
                    MusicVolume = Mathf.Clamp01(stored.MusicVolume);
                    SoundEffectsVolume = Mathf.Clamp01(stored.SoundEffectsVolume);
                    if (stored.Version >= 11)
                    {
                        DialogueVolume = Mathf.Clamp01(stored.DialogueVolume);
                    }
                    VisualizerMode = stored.Version >= 3
                        && Enum.IsDefined(typeof(MusicVisualizerMode), stored.MusicVisualizerMode)
                            ? (MusicVisualizerMode)stored.MusicVisualizerMode
                            : stored.Version >= 2 && !stored.MusicVisualizerEnabled
                                ? MusicVisualizerMode.Off
                                : MusicVisualizerMode.All;
                    if (stored.Version >= 4)
                    {
                        ChromaticAberrationEnabled = stored.ChromaticAberrationEnabled;
                    }
                    if (stored.Version >= 5)
                    {
                        LensDistortionEnabled = stored.LensDistortionEnabled;
                        CrtLinesEnabled = stored.CrtLinesEnabled;
                        FilmGrainEnabled = stored.FilmGrainEnabled;
                    }
                    if (stored.Version >= 6 &&
                        Enum.IsDefined(typeof(DuneVectorCameraAntiAliasingMode), stored.AntiAliasingMode) &&
                        stored.AntiAliasingMode != (int)DuneVectorCameraAntiAliasingMode.TemporalAntiAliasing)
                    {
                        AntiAliasingMode = (DuneVectorCameraAntiAliasingMode)stored.AntiAliasingMode;
                    }
                    if (stored.Version >= 7)
                    {
                        VignetteEnabled = stored.VignetteEnabled;
                    }
                    if (stored.Version >= 8)
                    {
                        VisualizerFovEnabled = stored.VisualizerFovEnabled;
                    }
                    if (stored.Version >= 9)
                    {
                        VisualizerEffectMask = (MusicVisualEffectGroups)stored.MusicVisualizerEffectMask
                            & MusicVisualEffectGroups.All;
                    }
                    if (stored.Version >= 13)
                    {
                        BloomEnabled = stored.BloomEnabled;
                    }
                    if (VisualizerMode == MusicVisualizerMode.NoFlash)
                    {
                        VisualizerEffectMask &= ~PauseMenuVisualTuning.FlashMusicVisualizerEffects;
                        VisualizerMode = MusicVisualizerMode.All;
                    }
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
                    DialogueVolume = DialogueVolume,
                    MusicVisualizerEnabled = VisualizerMode != MusicVisualizerMode.Off,
                    MusicVisualizerMode = (int)VisualizerMode,
                    ChromaticAberrationEnabled = ChromaticAberrationEnabled,
                    LensDistortionEnabled = LensDistortionEnabled,
                    CrtLinesEnabled = CrtLinesEnabled,
                    FilmGrainEnabled = FilmGrainEnabled,
                    VignetteEnabled = VignetteEnabled,
                    BloomEnabled = BloomEnabled,
                    AntiAliasingMode = (int)AntiAliasingMode,
                    VisualizerFovEnabled = VisualizerFovEnabled,
                    MusicVisualizerEffectMask = (int)VisualizerEffectMask,
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
            ReleaseCurrentMusic(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
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
        private DroneTuning _playerTuning;
        private PauseMenuVisualTuning _visuals;
        private DuneVectorUpgradeShopView _shopView;
        private DuneVectorCourierGame _courierGame;
        private DuneVectorPhotographySystem _photography;
        private bool _showShop;
        private bool _showGallery;
        private bool _showCompendium;
        private bool _showControls;
        private bool _showVideoSettings;
        private bool _showMusicVisualizerSettings;
        private float _controlsFade;
        private Keyboard _textInputKeyboard;
        private int _upgradeCheatProgress;
        private readonly Dictionary<ChromaticAberration, bool> _chromaticAberrationOriginalStates = new();
        private readonly Dictionary<LensDistortion, bool> _lensDistortionOriginalStates = new();
        private readonly Dictionary<FilmGrain, bool> _filmGrainOriginalStates = new();
        private readonly Dictionary<Vignette, bool> _vignetteOriginalStates = new();
        private readonly Dictionary<Bloom, bool> _bloomOriginalStates = new();
        private RetroCrtScanlineTuning _retroCrtScanlines;

        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _mixerLabelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _buttonLabelStyle;
        private GUIStyle _buttonLabelLeftStyle;
        private GUIStyle _pillLabelStyle;
        private GUIStyle _chevronStyle;
        private GUIStyle _invisibleButtonStyle;
        private GUIStyle _sliderStyle;
        private GUIStyle _sliderThumbStyle;
        private GUIStyle _songTitleStyle;
        private GUIStyle _songTitleShadowStyle;
        private GUIStyle _songControlStyle;
        private GUIStyle _songControlShadowStyle;
        private GUIStyle _songPauseStyle;
        private GUIStyle _songPauseShadowStyle;

        private Texture2D _transparentTexture;
        private float _styledScale = -1f;
        private float _scale = 1f;
        private float _openFade;
        private float _uiAlpha = 1f;
        private readonly GUIContent _glyphContent = new GUIContent();

        private enum PauseButtonKind
        {
            Primary,
            Secondary,
            Danger,
        }

        public void Initialize(
            DronePlayer player,
            DroneHealth health,
            DuneVectorAudioManager audio,
            DroneGoldWallet wallet,
            DronePermanentUpgradeSystem upgrades,
            DroneTuning playerTuning,
            PauseMenuVisualTuning visuals,
            UpgradeShopVisualTuning shopVisuals,
            RetroCrtScanlineTuning retroCrtScanlines)
        {
            _player = player;
            _health = health;
            _audio = audio;
            _wallet = wallet;
            _upgrades = upgrades;
            _playerTuning = playerTuning;
            _visuals = visuals;
            _retroCrtScanlines = retroCrtScanlines;
            _shopView = new DuneVectorUpgradeShopView(_upgrades, _wallet, shopVisuals);
            if (_health != null)
            {
                _health.Died += HandleDeath;
            }
            ApplyVideoPreferences();
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
            BindTextInputKeyboard();
            UpdateControlsFade();
            UpdateOpenFade();
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
                else if (IsPaused && _showVideoSettings)
                {
                    _showVideoSettings = false;
                }
                else if (IsPaused && _showMusicVisualizerSettings)
                {
                    _showMusicVisualizerSettings = false;
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
                    if (_photography == null || !_photography.CloseCompendiumViewer())
                    {
                        _showCompendium = false;
                    }
                }
                else
                {
                    SetPaused(!IsPaused);
                }
            }
        }

        private void BindTextInputKeyboard()
        {
            Keyboard currentKeyboard = Keyboard.current;
            if (_textInputKeyboard == currentKeyboard)
            {
                return;
            }

            if (_textInputKeyboard != null)
            {
                _textInputKeyboard.onTextInput -= HandleTextInput;
            }
            _textInputKeyboard = currentKeyboard;
            if (_textInputKeyboard != null)
            {
                _textInputKeyboard.onTextInput += HandleTextInput;
            }
        }

        private void HandleTextInput(char character)
        {
            string cheatCode = _visuals?.UpgradeUnlockCheatCode;
            if (!IsPaused || string.IsNullOrEmpty(cheatCode))
            {
                _upgradeCheatProgress = 0;
                return;
            }
            if (_upgradeCheatProgress >= cheatCode.Length)
            {
                _upgradeCheatProgress = 0;
            }

            char typedCharacter = char.ToLowerInvariant(character);
            char expectedCharacter = char.ToLowerInvariant(cheatCode[_upgradeCheatProgress]);
            if (typedCharacter == expectedCharacter)
            {
                _upgradeCheatProgress++;
                if (_upgradeCheatProgress >= cheatCode.Length)
                {
                    _upgradeCheatProgress = 0;
                    if (_upgrades != null && _upgrades.TryUnlockAllUpgrades())
                    {
                        Vector3 position = _player != null ? _player.transform.position : transform.position;
                        _audio?.PlayFlightRingSwoosh(position);
                    }
                }
                return;
            }

            _upgradeCheatProgress = typedCharacter == char.ToLowerInvariant(cheatCode[0]) ? 1 : 0;
        }

        private void SetPaused(bool paused)
        {
            if (paused && _health != null && _health.IsDead)
            {
                return;
            }

            IsPaused = paused;
            if (paused)
            {
                _openFade = 0f;
            }
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
                _upgradeCheatProgress = 0;
                _showShop = false;
                _showGallery = false;
                _showCompendium = false;
                _showControls = false;
                _showVideoSettings = false;
                _showMusicVisualizerSettings = false;
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

        private void UpdateOpenFade()
        {
            float target = IsPaused ? 1f : 0f;
            float duration = _visuals != null ? Mathf.Max(0f, _visuals.OpenAnimationDuration) : 0f;
            _openFade = duration <= 0f
                ? target
                : Mathf.MoveTowards(_openFade, target, Time.unscaledDeltaTime / duration);
        }

        private void HandleDeath()
        {
            IsPaused = false;
            _showShop = false;
            _showGallery = false;
            _showCompendium = false;
            _showControls = false;
            _showVideoSettings = false;
            _showMusicVisualizerSettings = false;
            _controlsFade = 0f;
            _openFade = 0f;
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
            _scale = scale;
            _uiAlpha = Mathf.Clamp01(_openFade);

            // The open animation fades every element together. Solid rects apply
            // _uiAlpha themselves because they overwrite GUI.color while drawing.
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _uiAlpha);
            try
            {
                DrawPauseScreens(scale);
            }
            finally
            {
                GUI.color = previousGuiColor;
                _uiAlpha = 1f;
            }
        }

        private void DrawPauseScreens(float scale)
        {
            DrawOverlayBackdrop(scale);

            if (_showControls || _controlsFade > 0f)
            {
                DrawControlsScreen();
                return;
            }

            if (_showVideoSettings)
            {
                DrawVideoSettingsScreen(scale);
                return;
            }

            if (_showMusicVisualizerSettings)
            {
                DrawMusicVisualizerSettingsScreen(scale);
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

            DrawSongControls(scale);
            DrawMainPauseScreen(scale);
        }

        private void DrawMainPauseScreen(float scale)
        {
            Rect panel = CalculatePanelRect(scale);
            DrawPanelChrome(panel, scale);

            Rect content = CalculateContentRect(panel, scale);
            float gap = _visuals.ButtonGap * scale;
            float y = DrawPanelHeader(content, content.y, "PAUSED", "DUNE VECTOR  /  SYSTEMS ON HOLD", scale, gap);

            y = DrawSectionHeader(content, y, "AUDIO MIXER", scale);

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
            y += sliderRowHeight;

            DrawVolumeRow(
                new Rect(content.x, y, content.width, sliderRowHeight),
                "DIALOGUE",
                _audio != null ? _audio.DialogueVolume : 0f,
                value => _audio?.SetDialogueVolume(value),
                scale);
            y += sliderRowHeight + (_visuals.DialogueButtonGap * scale);

            float buttonHeight = _visuals.ButtonHeight * scale;
            if (DrawMenuButton(new Rect(content.x, y, content.width, buttonHeight), "RESUME FLIGHT", PauseButtonKind.Primary))
            {
                SetPaused(false);
            }
            y += buttonHeight + gap;

            string galleryButtonLabel = _photography != null && _photography.Tuning != null
                ? _photography.Tuning.PauseMenuButtonLabel
                : string.Empty;
            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    galleryButtonLabel,
                    PauseButtonKind.Secondary,
                    true))
            {
                _showGallery = true;
            }
            y += buttonHeight + gap;

            string compendiumButtonLabel = _photography != null && _photography.Tuning != null
                ? _photography.Tuning.CompendiumPauseMenuButtonLabel
                : string.Empty;
            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    compendiumButtonLabel,
                    PauseButtonKind.Secondary,
                    true))
            {
                _showCompendium = true;
                _photography?.ShowCompendium();
            }
            y += buttonHeight + gap;

            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    "UPGRADE SHOP",
                    PauseButtonKind.Secondary,
                    true))
            {
                _showShop = true;
                _shopView?.Open();
            }
            y += buttonHeight + gap;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _courierGame != null && _courierGame.State != CourierRunState.Hub;
            if (DrawMenuButton(new Rect(content.x, y, content.width, buttonHeight), "RETURN TO HUB", PauseButtonKind.Secondary))
            {
                SetPaused(false);
                _courierGame?.RequestReturnToHub();
            }
            GUI.enabled = previousEnabled;
            y += buttonHeight + gap;

            float splitButtonWidth = (content.width - gap) * 0.5f;
            if (DrawMenuButton(new Rect(content.x, y, splitButtonWidth, buttonHeight), "RESTART RUN", PauseButtonKind.Secondary))
            {
                RestartRun();
            }
            if (DrawMenuButton(
                    new Rect(content.x + splitButtonWidth + gap, y, splitButtonWidth, buttonHeight),
                    "QUIT",
                    PauseButtonKind.Danger))
            {
                QuitGame();
            }
            y += buttonHeight + gap;

            // The trailing entries open settings screens rather than acting on the run,
            // so extra breathing room keeps them separate from the run controls above.
            y += gap * 1.5f;

            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.ControlsButtonLabel,
                    PauseButtonKind.Secondary,
                    true))
            {
                _showControls = true;
            }
            y += buttonHeight + gap;

            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.MusicVisualizerSettingsButtonLabel,
                    PauseButtonKind.Secondary,
                    true))
            {
                _showMusicVisualizerSettings = true;
            }
            y += buttonHeight + gap;

            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.VideoSettingsButtonLabel,
                    PauseButtonKind.Secondary,
                    true))
            {
                _showVideoSettings = true;
            }

            DrawFooterHint(content, "ESC  /  RETURN TO THE DESERT", scale);
        }

        private void DrawVideoSettingsScreen(float scale)
        {
            Rect panel = CalculatePanelRect(scale);
            DrawPanelChrome(panel, scale);

            Rect content = CalculateContentRect(panel, scale);
            float gap = _visuals.ButtonGap * scale;
            float y = DrawPanelHeader(
                content,
                content.y,
                _visuals.VideoSettingsTitle,
                _visuals.VideoSettingsSubtitle,
                scale,
                gap);

            float buttonHeight = _visuals.ButtonHeight * scale;
            y = DrawSectionHeader(content, y, _visuals.VideoAntiAliasingLabel, scale);

            float segmentedGap = gap;
            float segmentedWidth = (content.width - segmentedGap) / 2f;
            DrawAntiAliasingButton(
                new Rect(content.x, y, segmentedWidth, buttonHeight),
                _visuals.VideoAntiAliasingOffLabel,
                DuneVectorCameraAntiAliasingMode.None);
            DrawAntiAliasingButton(
                new Rect(content.x + segmentedWidth + segmentedGap, y, segmentedWidth, buttonHeight),
                _visuals.VideoAntiAliasingSmaaLabel,
                DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing);
            y += buttonHeight + (gap * 2f);

            y = DrawSectionHeader(content, y, _visuals.VideoSettingsSectionLabel, scale);

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoChromaticAberrationLabel,
                _audio == null || _audio.ChromaticAberrationEnabled,
                value => _audio?.SetChromaticAberrationEnabled(value));
            y += buttonHeight + gap;

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoLensDistortionLabel,
                _audio == null || _audio.LensDistortionEnabled,
                value => _audio?.SetLensDistortionEnabled(value));
            y += buttonHeight + gap;

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoCrtLinesLabel,
                _audio == null || _audio.CrtLinesEnabled,
                value => _audio?.SetCrtLinesEnabled(value));
            y += buttonHeight + gap;

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoFilmGrainLabel,
                _audio == null || _audio.FilmGrainEnabled,
                value => _audio?.SetFilmGrainEnabled(value));
            y += buttonHeight + gap;

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoVignetteLabel,
                _audio == null || _audio.VignetteEnabled,
                value => _audio?.SetVignetteEnabled(value));
            y += buttonHeight + gap;

            DrawVideoToggle(
                new Rect(content.x, y, content.width, buttonHeight),
                _visuals.VideoBloomLabel,
                _audio == null || _audio.BloomEnabled,
                value => _audio?.SetBloomEnabled(value));
            y += buttonHeight + gap;

            y += gap;

            float navigationWidth = (content.width - gap) * 0.5f;
            if (DrawMenuButton(
                    new Rect(content.x, y, navigationWidth, buttonHeight),
                    _visuals.VideoSettingsResetButtonLabel,
                    PauseButtonKind.Secondary))
            {
                _audio?.ResetVideoSettingsToDefaults();
                ApplyVideoPreferences();
            }
            if (DrawMenuButton(
                    new Rect(content.x + navigationWidth + gap, y, navigationWidth, buttonHeight),
                    _visuals.VideoSettingsBackButtonLabel,
                    PauseButtonKind.Primary))
            {
                _showVideoSettings = false;
            }

            DrawFooterHint(content, _visuals.VideoSettingsHintLabel, scale);
        }

        private void DrawMusicVisualizerSettingsScreen(float scale)
        {
            Rect panel = CalculatePanelRect(scale);
            DrawPanelChrome(panel, scale);

            Rect content = CalculateContentRect(panel, scale);
            float gap = _visuals.ButtonGap * scale;
            float y = DrawPanelHeader(
                content,
                content.y,
                _visuals.MusicVisualizerSettingsTitle,
                _visuals.MusicVisualizerSettingsSubtitle,
                scale,
                gap);

            float buttonHeight = _visuals.ButtonHeight * scale;
            y = DrawSectionHeader(content, y, _visuals.MusicVisualizerSettingsSectionLabel, scale);

            DrawMusicVisualizerMasterToggle(new Rect(content.x, y, content.width, buttonHeight));
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerSkyLabel,
                MusicVisualEffectGroups.Sky | MusicVisualEffectGroups.Filaments | MusicVisualEffectGroups.TrebleParticles);
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerBloomLabel,
                MusicVisualEffectGroups.Bloom);
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerPressureFrontsLabel,
                MusicVisualEffectGroups.PressureFront);
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerWorldResponseLabel,
                MusicVisualEffectGroups.Road | MusicVisualEffectGroups.Structures | MusicVisualEffectGroups.Drone);
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerStreaksLabel,
                MusicVisualEffectGroups.Streaks);
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerCameraLabel,
                MusicVisualEffectGroups.Camera);
            y += buttonHeight + gap;
            DrawMusicVisualizerToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerFovLabel,
                _audio != null && _audio.VisualizerFovEnabled, value => _audio?.SetVisualizerFovEnabled(value));
            y += buttonHeight + gap;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, content.width, buttonHeight), _visuals.MusicVisualizerGlitchLabel,
                MusicVisualEffectGroups.Glitch | MusicVisualEffectGroups.HudBorder);
            y += buttonHeight + (gap * 2f);

            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.MusicVisualizerSettingsResetButtonLabel,
                    PauseButtonKind.Secondary))
            {
                _audio?.ResetMusicVisualizerSettingsToDefaults();
            }
            y += buttonHeight + gap;
            if (DrawMenuButton(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.MusicVisualizerSettingsBackButtonLabel,
                    PauseButtonKind.Primary))
            {
                _showMusicVisualizerSettings = false;
            }

            DrawFooterHint(content, _visuals.MusicVisualizerSettingsHintLabel, scale);
        }

        private void DrawSongControls(float scale)
        {
            float left = _visuals.SongControlsLeft * scale;
            float top = _visuals.SongControlsTop * scale;
            float width = _visuals.SongControlsWidth * scale;
            float titleHeight = _visuals.SongTitleHeight * scale;
            float shadowOffset = _visuals.SongTextShadowOffset * scale;
            string title = _audio != null && !string.IsNullOrWhiteSpace(_audio.ActiveMusicDisplayName)
                ? _audio.ActiveMusicDisplayName
                : "NO SONG";

            Rect titleRect = new Rect(left, top, width, titleHeight);
            GUI.Label(OffsetRect(titleRect, shadowOffset), title, _songTitleShadowStyle);
            GUI.Label(titleRect, title, _songTitleStyle);

            float controlSize = _visuals.SongControlSize * scale;
            float gap = _visuals.SongControlGap * scale;
            float controlY = titleRect.yMax + gap;
            Rect previousRect = new Rect(left, controlY, controlSize, controlSize);
            Rect playPauseRect = new Rect(previousRect.xMax + gap, controlY, controlSize, controlSize);
            Rect nextRect = new Rect(playPauseRect.xMax + gap, controlY, controlSize, controlSize);

            if (DrawSongControlButton(previousRect, "|◀", shadowOffset))
            {
                _audio?.PlayPreviousMusicTrack();
            }
            bool playbackPaused = _audio != null && _audio.IsMusicPlaybackPaused;
            Rect pauseRect = playPauseRect;
            pauseRect.y += _visuals.SongPauseVerticalOffset * scale;
            bool playPausePressed = playbackPaused
                ? DrawSongControlButton(playPauseRect, "▶", shadowOffset)
                : DrawSongPauseButton(pauseRect, shadowOffset);
            if (playPausePressed)
            {
                _audio?.ToggleMusicPlayback();
            }
            if (DrawSongControlButton(nextRect, "▶|", shadowOffset))
            {
                _audio?.PlayNextMusicTrack();
            }
        }

        private bool DrawSongControlButton(Rect rect, string label, float shadowOffset)
        {
            GUI.Label(OffsetRect(rect, shadowOffset), label, _songControlShadowStyle);
            return GUI.Button(rect, label, _songControlStyle);
        }

        private bool DrawSongPauseButton(Rect rect, float shadowOffset)
        {
            GUI.Label(OffsetRect(rect, shadowOffset), "Ⅱ", _songPauseShadowStyle);
            return GUI.Button(rect, "Ⅱ", _songPauseStyle);
        }

        private static Rect OffsetRect(Rect rect, float offset)
        {
            rect.x += offset;
            rect.y += offset;
            return rect;
        }

        private void DrawVideoToggle(Rect rect, string label, bool enabled, Action<bool> apply)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _audio != null;
            string stateLabel = enabled
                ? _visuals.VideoEffectEnabledLabel
                : _visuals.VideoEffectDisabledLabel;
            if (DrawToggleRow(rect, label, stateLabel, enabled))
            {
                apply?.Invoke(!enabled);
                ApplyVideoPreferences();
            }
            GUI.enabled = previousEnabled;
        }

        private void DrawMusicVisualizerMasterToggle(Rect rect)
        {
            bool enabled = _audio != null && _audio.VisualizerMode != MusicVisualizerMode.Off;
            DrawMusicVisualizerToggle(rect, _visuals.MusicVisualizerMasterLabel, enabled,
                value => _audio?.SetMusicVisualizerMode(value ? MusicVisualizerMode.All : MusicVisualizerMode.Off));
        }

        private void DrawMusicVisualizerEffectToggle(Rect rect, string label, MusicVisualEffectGroups effects)
        {
            bool enabled = _audio != null && _audio.IsMusicVisualizerEffectEnabled(effects);
            DrawMusicVisualizerToggle(rect, label, enabled,
                value => _audio?.SetMusicVisualizerEffectEnabled(effects, value));
        }

        private void DrawMusicVisualizerToggle(Rect rect, string label, bool enabled, Action<bool> apply)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _audio != null;
            string stateLabel = enabled
                ? _visuals.MusicVisualizerEffectEnabledLabel
                : _visuals.MusicVisualizerEffectDisabledLabel;
            if (DrawToggleRow(rect, label, stateLabel, enabled))
            {
                apply?.Invoke(!enabled);
            }
            GUI.enabled = previousEnabled;
        }

        private void DrawAntiAliasingButton(
            Rect rect,
            string label,
            DuneVectorCameraAntiAliasingMode mode)
        {
            bool selected = _audio != null && _audio.AntiAliasingMode == mode;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _audio != null;
            if (DrawMenuButton(rect, label, selected ? PauseButtonKind.Primary : PauseButtonKind.Secondary))
            {
                _audio.SetAntiAliasingMode(mode);
                ApplyAntiAliasingPreference();
            }
            GUI.enabled = previousEnabled;
        }

        private void ApplyVideoPreferences()
        {
            ApplyAntiAliasingPreference();
            ApplyVolumePreference(
                _audio == null || _audio.ChromaticAberrationEnabled,
                _chromaticAberrationOriginalStates);
            ApplyVolumePreference(
                _audio == null || _audio.LensDistortionEnabled,
                _lensDistortionOriginalStates);
            ApplyVolumePreference(
                _audio == null || _audio.FilmGrainEnabled,
                _filmGrainOriginalStates);
            ApplyVolumePreference(
                _audio == null || _audio.VignetteEnabled,
                _vignetteOriginalStates,
                true);
            ApplyVolumePreference(
                _audio == null || _audio.BloomEnabled,
                _bloomOriginalStates);

            if (_retroCrtScanlines?.Material != null)
            {
                bool enabled = (_audio == null || _audio.CrtLinesEnabled) && _retroCrtScanlines.Enabled;
                _retroCrtScanlines.Material.SetFloat(
                    "_ScanlineStrength",
                    enabled ? Mathf.Clamp01(_retroCrtScanlines.ScanlineStrength) : 0f);
            }
        }

        private void ApplyAntiAliasingPreference()
        {
            Camera gameplayCamera = _player?.CharacterCamera?.Camera;
            if (gameplayCamera == null)
            {
                return;
            }

            UniversalAdditionalCameraData cameraData = gameplayCamera.GetUniversalAdditionalCameraData();
            DuneVectorCameraAntiAliasingMode mode = _audio != null
                ? _audio.AntiAliasingMode
                : DuneVectorCameraAntiAliasingMode.None;
            cameraData.antialiasing = mode switch
            {
                DuneVectorCameraAntiAliasingMode.TemporalAntiAliasing =>
                    AntialiasingMode.TemporalAntiAliasing,
                DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing =>
                    AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                _ => AntialiasingMode.None,
            };
            if (_playerTuning != null)
            {
                cameraData.antialiasingQuality = _playerTuning.SmaaQuality switch
                {
                    DuneVectorSmaaQuality.Low => AntialiasingQuality.Low,
                    DuneVectorSmaaQuality.Medium => AntialiasingQuality.Medium,
                    _ => AntialiasingQuality.High,
                };

                int msaaSampleCount = (int)_playerTuning.CameraMsaaSampleCount;
                if (UniversalRenderPipeline.asset != null)
                {
                    UniversalRenderPipeline.asset.msaaSampleCount = msaaSampleCount;
                }
                gameplayCamera.allowMSAA = mode ==
                    DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing
                    && msaaSampleCount > 1;
            }
            else
            {
                gameplayCamera.allowMSAA = false;
            }
        }

        private static void ApplyVolumePreference<T>(
            bool enabled,
            Dictionary<T, bool> originalStates,
            bool globalVolumesOnly = false)
            where T : VolumeComponent
        {
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include);
            foreach (Volume volume in volumes)
            {
                if (volume == null
                    || volume.sharedProfile == null
                    || (globalVolumesOnly && !volume.isGlobal))
                {
                    continue;
                }

                VolumeProfile runtimeProfile = volume.profile;
                if (runtimeProfile == null || !runtimeProfile.TryGet(out T component))
                {
                    continue;
                }

                if (!originalStates.ContainsKey(component))
                {
                    originalStates.Add(component, component.active);
                }

                component.active = enabled && originalStates[component];
            }
        }

        private void DrawControlsScreen()
        {
            float alpha = Mathf.Clamp01(_controlsFade);
            Color background = _visuals.ControlsBackgroundColor;
            background.a *= alpha;
            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), background);

            Texture2D controlsImage = _visuals.ControlsImage;
            if (controlsImage != null && controlsImage.width > 0 && controlsImage.height > 0)
            {
                float imageHeight = Screen.width * ((float)controlsImage.height / controlsImage.width);
                Rect imageRect = new Rect(
                    0f,
                    (Screen.height - imageHeight) * 0.5f,
                    Screen.width,
                    imageHeight);
                Color imagePreviousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(imageRect, controlsImage, ScaleMode.StretchToFill, false);
                GUI.color = imagePreviousColor;
            }

        }

        private void DrawVolumeRow(
            Rect area,
            string label,
            float value,
            Action<float> apply,
            float scale)
        {
            float labelHeight = Mathf.Max(_mixerLabelStyle.lineHeight, _valueStyle.lineHeight);
            DrawTintedLabel(
                new Rect(area.x, area.y, area.width * 0.7f, labelHeight),
                label,
                _mixerLabelStyle,
                _visuals.PrimaryTextColor);
            DrawTintedLabel(
                new Rect(area.x + (area.width * 0.7f), area.y, area.width * 0.3f, labelHeight),
                $"{Mathf.RoundToInt(value * 100f):00}%",
                _valueStyle,
                _visuals.TitleColor);

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

            bool hovered = GUI.enabled && sliderRect.Contains(Event.current.mousePosition);
            float clampedValue = Mathf.Clamp01(value);

            // Recessed track: a darker lip above the base reads as an inset groove.
            DrawSolidRect(trackRect, DarkenColor(_visuals.SliderTrackColor, 0.45f));
            float lip = Mathf.Max(1f, scale);
            DrawSolidRect(
                new Rect(trackRect.x, trackRect.y + lip, trackRect.width, trackRect.height - lip),
                _visuals.SliderTrackColor);

            Color tickColor = _visuals.DividerColor;
            tickColor.a *= 0.55f;
            float tickWidth = Mathf.Max(1f, scale);
            float tickHeight = trackHeight + (6f * scale);
            for (int i = 0; i <= 4; i++)
            {
                float tickX = trackRect.x + (trackRect.width * (i / 4f)) - (tickWidth * 0.5f);
                DrawSolidRect(
                    new Rect(tickX, trackRect.center.y - (tickHeight * 0.5f), tickWidth, tickHeight),
                    tickColor);
            }

            if (clampedValue > 0f)
            {
                Rect fillRect = new Rect(trackRect.x, trackRect.y, trackRect.width * clampedValue, trackRect.height);
                DrawVerticalGradient(
                    fillRect,
                    LightenColor(_visuals.SliderFillColor, 0.32f),
                    _visuals.SliderFillColor);
                Color fillGlow = _visuals.SliderFillColor;
                fillGlow.a *= hovered ? 0.32f : 0.16f;
                DrawSolidRect(
                    new Rect(fillRect.x, fillRect.y - (2f * scale), fillRect.width, fillRect.height + (4f * scale)),
                    fillGlow);
            }

            Rect thumbRect = new Rect(
                trackRect.x + (trackRect.width * clampedValue) - (thumbWidth * 0.5f),
                sliderRect.y,
                thumbWidth,
                thumbHeight);
            Color thumbGlow = _visuals.SliderThumbColor;
            thumbGlow.a *= hovered ? 0.4f : 0.2f;
            float glowInset = 3f * scale;
            DrawSolidRect(
                new Rect(
                    thumbRect.x - glowInset,
                    thumbRect.y - glowInset,
                    thumbRect.width + (glowInset * 2f),
                    thumbRect.height + (glowInset * 2f)),
                thumbGlow);
            DrawVerticalGradient(
                thumbRect,
                LightenColor(_visuals.SliderThumbColor, 0.25f),
                DarkenColor(_visuals.SliderThumbColor, 0.2f));
            Color notchColor = DarkenColor(_visuals.SliderThumbColor, 0.55f);
            DrawSolidRect(
                new Rect(thumbRect.center.x - (tickWidth * 0.5f), thumbRect.y + (5f * scale), tickWidth, thumbRect.height - (10f * scale)),
                notchColor);

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
            float minimumScale = Mathf.Min(_visuals.MinimumScale, _visuals.MaximumScale);
            float maximumScale = Mathf.Max(_visuals.MinimumScale, _visuals.MaximumScale);
            float preferredScale = Mathf.Clamp(Mathf.Min(widthScale, heightScale), minimumScale, maximumScale);

            // MinimumScale is a readability preference. On a viewport smaller than that
            // floor, it must yield so the panel and all of its scaled contents stay visible.
            float fitWidthScale = Screen.width /
                Mathf.Max(1f, _visuals.PanelWidth + (_visuals.ScreenMargin * 2f));
            float fitHeightScale = Screen.height /
                Mathf.Max(1f, _visuals.PanelHeight + (_visuals.ScreenMargin * 2f));
            return Mathf.Min(preferredScale, fitWidthScale, fitHeightScale);
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
            _songTitleStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SongTitleFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _visuals.SongTextColor);
            SetStaticTextColor(_songTitleStyle, _visuals.SongTextColor);
            _songTitleShadowStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SongTitleFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _visuals.SongTextShadowColor);
            SetStaticTextColor(_songTitleShadowStyle, _visuals.SongTextShadowColor);
            _songControlStyle = CreateTransparentButtonStyle(
                Mathf.RoundToInt(_visuals.SongControlFontSize * scale),
                _visuals.SongTextColor);
            _songControlShadowStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SongControlFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.SongTextShadowColor);
            _songPauseStyle = CreateTransparentButtonStyle(
                Mathf.RoundToInt(_visuals.SongPauseFontSize * scale),
                _visuals.SongTextColor);
            _songPauseShadowStyle = CreateLabelStyle(
                Mathf.RoundToInt(_visuals.SongPauseFontSize * scale),
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.SongTextShadowColor);

            int buttonFontSize = Mathf.RoundToInt(_visuals.ButtonFontSize * scale);
            _buttonLabelStyle = CreateLabelStyle(
                buttonFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.PrimaryTextColor);
            _buttonLabelLeftStyle = CreateLabelStyle(
                buttonFontSize,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                _visuals.PrimaryTextColor);
            _pillLabelStyle = CreateLabelStyle(
                Mathf.Max(9, Mathf.RoundToInt(buttonFontSize * 0.82f)),
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                _visuals.PrimaryTextColor);
            _chevronStyle = CreateLabelStyle(
                Mathf.RoundToInt(buttonFontSize * 1.3f),
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                _visuals.SecondaryTextColor);
            _invisibleButtonStyle = CreateTransparentButtonStyle(buttonFontSize, Color.clear);

            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = _visuals.SliderTrackHeight * scale,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            _sliderStyle.normal.background = _transparentTexture;
            _sliderStyle.hover.background = _transparentTexture;
            _sliderStyle.active.background = _transparentTexture;

            // The thumb is drawn by DrawVolumeRow so it can carry a glow and a notch;
            // this style only supplies the drag hit area.
            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = _visuals.SliderThumbWidth * scale,
                fixedHeight = _visuals.SliderThumbHeight * scale,
                margin = new RectOffset(),
                padding = new RectOffset(),
            };
            _sliderThumbStyle.normal.background = _transparentTexture;
            _sliderThumbStyle.hover.background = _transparentTexture;
            _sliderThumbStyle.active.background = _transparentTexture;
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

        // Unity's skin supplies its own text color for hovered and active controls,
        // which would otherwise recolor headings and mixer labels whenever the mouse
        // happens to pass over them. Every state is pinned to the requested color so
        // the caller's choice is the only thing that decides how a label reads.
        private static void ApplyTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            SetStaticTextColor(style, color);
        }

        private static void SetStaticTextColor(GUIStyle style, Color color)
        {
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private GUIStyle CreateTransparentButtonStyle(int fontSize, Color color)
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
                padding = new RectOffset(),
            };
            style.normal.background = _transparentTexture;
            style.hover.background = _transparentTexture;
            style.active.background = _transparentTexture;
            style.focused.background = _transparentTexture;
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            return style;
        }

        private void EnsureTextures()
        {
            if (_transparentTexture != null)
            {
                return;
            }

            // Every panel surface is painted with tinted white quads, so the only
            // texture the styles still need is a fully transparent background.
            _transparentTexture = CreateSolidTexture("Pause Transparent", Color.clear);
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

        private void DrawSolidRect(Rect rect, Color color)
        {
            color.a *= _uiAlpha;
            if (color.a <= 0.001f)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private const int GradientBandCount = 16;

        private void DrawVerticalGradient(Rect rect, Color top, Color bottom)
        {
            float bandHeight = rect.height / GradientBandCount;
            for (int i = 0; i < GradientBandCount; i++)
            {
                float t = (i + 0.5f) / GradientBandCount;
                DrawSolidRect(
                    new Rect(rect.x, rect.y + (bandHeight * i), rect.width, bandHeight + 1f),
                    Color.Lerp(top, bottom, t));
            }
        }

        private void DrawHorizontalGradient(Rect rect, Color left, Color right)
        {
            float bandWidth = rect.width / GradientBandCount;
            for (int i = 0; i < GradientBandCount; i++)
            {
                float t = (i + 0.5f) / GradientBandCount;
                DrawSolidRect(
                    new Rect(rect.x + (bandWidth * i), rect.y, bandWidth + 1f, rect.height),
                    Color.Lerp(left, right, t));
            }
        }

        private static Color LightenColor(Color color, float amount)
        {
            return new Color(
                Mathf.Lerp(color.r, 1f, amount),
                Mathf.Lerp(color.g, 1f, amount),
                Mathf.Lerp(color.b, 1f, amount),
                color.a);
        }

        private static Color DarkenColor(Color color, float amount)
        {
            float keep = 1f - Mathf.Clamp01(amount);
            return new Color(color.r * keep, color.g * keep, color.b * keep, color.a);
        }

        private static Color WithAlpha(Color color, float alphaScale)
        {
            color.a *= alphaScale;
            return color;
        }

        private Rect CalculatePanelRect(float scale)
        {
            float screenMargin = _visuals.ScreenMargin * scale;
            float panelWidth = Mathf.Min(_visuals.PanelWidth * scale, Screen.width - (screenMargin * 2f));
            float panelHeight = Mathf.Min(_visuals.PanelHeight * scale, Screen.height - (screenMargin * 2f));

            // The panel settles downward as it fades in, so the open reads as a
            // deliberate motion instead of a hard cut.
            float rise = (1f - Mathf.Clamp01(_openFade)) * _visuals.OpenAnimationRise * scale;
            return new Rect(
                Mathf.Round((Screen.width - panelWidth) * 0.5f),
                Mathf.Round(((Screen.height - panelHeight) * 0.5f) - rise),
                panelWidth,
                panelHeight);
        }

        private Rect CalculateContentRect(Rect panel, float scale)
        {
            float padding = _visuals.PanelPadding * scale;
            return new Rect(
                panel.x + padding,
                panel.y + padding,
                panel.width - (padding * 2f),
                panel.height - (padding * 2f));
        }

        private void DrawOverlayBackdrop(float scale)
        {
            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), _visuals.OverlayColor);

            float strength = Mathf.Clamp01(_visuals.OverlayVignetteStrength);
            if (strength <= 0.001f)
            {
                return;
            }

            // A single flat dim over the whole screen, so the backdrop reads as an
            // even darkening with no tiling pattern showing through.
            DrawSolidRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0f, 0f, 0f, strength));
        }

        private void DrawPanelChrome(Rect panel, float scale)
        {
            // Three decreasing offsets approximate a soft drop shadow.
            float shadowOffset = _visuals.ShadowOffset * scale;
            for (int i = 3; i >= 1; i--)
            {
                float offset = shadowOffset * (i / 3f);
                DrawSolidRect(
                    new Rect(panel.x + offset, panel.y + offset, panel.width, panel.height),
                    WithAlpha(_visuals.ShadowColor, 0.5f));
            }

            // Flat panel fill using the panel's darker bottom tone, drawn opaque so
            // the scene behind cannot wash it out toward grey.
            float gradient = Mathf.Clamp01(_visuals.PanelGradientStrength);
            Color panelFill = Color.Lerp(_visuals.PanelColor, DarkenColor(_visuals.PanelColor, 0.4f), gradient);
            panelFill.a = 1f;
            DrawSolidRect(panel, panelFill);

            float borderThickness = Mathf.Max(1f, scale * 2f);
            Color borderTop = _visuals.PanelBorderColor;
            Color borderBottom = WithAlpha(borderTop, 0.3f);
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, borderThickness), borderTop);
            DrawSolidRect(
                new Rect(panel.x, panel.yMax - borderThickness, panel.width, borderThickness),
                borderBottom);
            DrawVerticalGradient(
                new Rect(panel.x, panel.y, borderThickness, panel.height),
                borderTop,
                borderBottom);
            DrawVerticalGradient(
                new Rect(panel.xMax - borderThickness, panel.y, borderThickness, panel.height),
                borderTop,
                borderBottom);

            float accentHeight = Mathf.Max(1f, _visuals.AccentBarHeight * scale);
            Rect accentBar = new Rect(panel.x, panel.y, panel.width, accentHeight);
            Color accent = _visuals.AccentColor;
            Color accentEdge = WithAlpha(accent, 0.18f);
            DrawHorizontalGradient(
                new Rect(accentBar.x, accentBar.y, accentBar.width * 0.5f, accentBar.height),
                accentEdge,
                accent);
            DrawHorizontalGradient(
                new Rect(accentBar.center.x, accentBar.y, accentBar.width * 0.5f, accentBar.height),
                accent,
                accentEdge);
            DrawSolidRect(
                new Rect(panel.x, accentBar.yMax, panel.width, Mathf.Max(1f, scale)),
                _visuals.PanelHighlightColor);

            DrawCornerBrackets(panel, scale);
        }

        private void DrawCornerBrackets(Rect panel, float scale)
        {
            float length = _visuals.CornerBracketLength * scale;
            float thickness = Mathf.Max(1f, _visuals.CornerBracketThickness * scale);
            if (length <= 0.5f)
            {
                return;
            }

            Color color = _visuals.AccentColor;
            DrawSolidRect(new Rect(panel.x, panel.y, length, thickness), color);
            DrawSolidRect(new Rect(panel.x, panel.y, thickness, length), color);
            DrawSolidRect(new Rect(panel.xMax - length, panel.y, length, thickness), color);
            DrawSolidRect(new Rect(panel.xMax - thickness, panel.y, thickness, length), color);
            DrawSolidRect(new Rect(panel.x, panel.yMax - thickness, length, thickness), color);
            DrawSolidRect(new Rect(panel.x, panel.yMax - length, thickness, length), color);
            DrawSolidRect(new Rect(panel.xMax - length, panel.yMax - thickness, length, thickness), color);
            DrawSolidRect(new Rect(panel.xMax - thickness, panel.yMax - length, thickness, length), color);
        }

        private float DrawPanelHeader(
            Rect content,
            float y,
            string title,
            string subtitle,
            float scale,
            float gap)
        {
            float titleHeight = _titleStyle.lineHeight;
            Rect titleRect = new Rect(content.x, y, content.width, titleHeight);
            float titleTracking = _visuals.TitleTracking * scale;

            float glow = Mathf.Clamp01(_visuals.TitleGlowStrength);
            if (glow > 0.001f)
            {
                Color glowColor = WithAlpha(_visuals.AccentColor, glow * 0.45f);
                float offset = Mathf.Max(1f, 2f * scale);
                DrawTrackedLabel(new Rect(titleRect.x, titleRect.y - offset, titleRect.width, titleRect.height), title, _titleStyle, titleTracking, glowColor);
                DrawTrackedLabel(new Rect(titleRect.x, titleRect.y + offset, titleRect.width, titleRect.height), title, _titleStyle, titleTracking, glowColor);
                DrawTrackedLabel(new Rect(titleRect.x - offset, titleRect.y, titleRect.width, titleRect.height), title, _titleStyle, titleTracking, glowColor);
                DrawTrackedLabel(new Rect(titleRect.x + offset, titleRect.y, titleRect.width, titleRect.height), title, _titleStyle, titleTracking, glowColor);
            }
            DrawTrackedLabel(titleRect, title, _titleStyle, titleTracking, _visuals.TitleColor);
            y += titleHeight;

            float subtitleHeight = _subtitleStyle.lineHeight;
            DrawTrackedLabel(
                new Rect(content.x, y, content.width, subtitleHeight),
                subtitle,
                _subtitleStyle,
                _visuals.SubtitleTracking * scale,
                _visuals.SecondaryTextColor);
            y += subtitleHeight + gap;

            DrawFadedDivider(new Rect(content.x, y, content.width, Mathf.Max(1f, scale)));
            return y + (gap * 1.5f);
        }

        private void DrawFadedDivider(Rect rect)
        {
            Color color = _visuals.DividerColor;
            Color clear = WithAlpha(color, 0f);
            DrawHorizontalGradient(new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height), clear, color);
            DrawHorizontalGradient(new Rect(rect.center.x, rect.y, rect.width * 0.5f, rect.height), color, clear);
        }

        private float DrawSectionHeader(Rect content, float y, string label, float scale)
        {
            float height = _sectionStyle.lineHeight;
            float tickWidth = Mathf.Max(2f, 3f * scale);
            float tickHeight = height * 0.72f;
            DrawSolidRect(
                new Rect(content.x, y + ((height - tickHeight) * 0.5f), tickWidth, tickHeight),
                _visuals.AccentColor);

            float indent = tickWidth + (8f * scale);
            DrawTrackedLabel(
                new Rect(content.x + indent, y, content.width - indent, height),
                label,
                _sectionStyle,
                _visuals.SectionTracking * scale,
                _visuals.AccentColor);
            return y + height + (_visuals.ButtonGap * scale);
        }

        private void DrawFooterHint(Rect content, string text, float scale)
        {
            float hintHeight = _hintStyle.lineHeight;
            Rect hintRect = new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight);
            DrawTrackedLabel(hintRect, text, _hintStyle, _visuals.HintTracking * scale, _visuals.SecondaryTextColor);
        }

        private bool DrawMenuButton(Rect rect, string label, PauseButtonKind kind, bool opensScreen = false)
        {
            bool interactive = GUI.enabled;
            bool hovered = interactive && rect.Contains(Event.current.mousePosition);
            bool held = hovered && Mouse.current != null && Mouse.current.leftButton.isPressed;

            Color fill;
            Color textColor;
            switch (kind)
            {
                case PauseButtonKind.Primary:
                    fill = held
                        ? _visuals.ButtonActiveColor
                        : hovered ? _visuals.SliderThumbColor : _visuals.AccentColor;
                    textColor = _visuals.PrimaryButtonTextColor;
                    break;
                case PauseButtonKind.Danger:
                    fill = held
                        ? _visuals.ButtonActiveColor
                        : hovered ? _visuals.DangerButtonHoverColor : _visuals.DangerButtonColor;
                    textColor = hovered ? Color.white : _visuals.PrimaryTextColor;
                    break;
                default:
                    fill = held
                        ? _visuals.ButtonActiveColor
                        : hovered ? _visuals.ButtonHoverColor : _visuals.ButtonColor;
                    textColor = hovered ? _visuals.ButtonHoverTextColor : _visuals.PrimaryTextColor;
                    break;
            }

            if (!interactive)
            {
                fill = DarkenColor(_visuals.ButtonColor, 0.35f);
                textColor = WithAlpha(_visuals.SecondaryTextColor, 0.45f);
            }

            DrawVerticalGradient(rect, LightenColor(fill, 0.1f), DarkenColor(fill, 0.18f));

            float outlineThickness = Mathf.Max(1f, _scale);
            Color outline = interactive && hovered
                ? WithAlpha(_visuals.AccentColor, 0.6f)
                : WithAlpha(Color.black, 0.35f);
            DrawBorder(rect, outline, outlineThickness);

            bool clicked = GUI.Button(rect, GUIContent.none, _invisibleButtonStyle);
            DrawTintedLabel(rect, label, _buttonLabelStyle, textColor);

            if (opensScreen)
            {
                Color chevronColor = interactive
                    ? WithAlpha(hovered ? _visuals.AccentColor : _visuals.SecondaryTextColor, 0.85f)
                    : WithAlpha(_visuals.SecondaryTextColor, 0.3f);
                float chevronPadding = 12f * _scale;
                DrawTintedLabel(
                    new Rect(rect.x, rect.y, rect.width - chevronPadding, rect.height),
                    "›",
                    _chevronStyle,
                    chevronColor);
            }

            return clicked;
        }

        private bool DrawToggleRow(Rect rect, string label, string stateLabel, bool on)
        {
            bool interactive = GUI.enabled;
            bool hovered = interactive && rect.Contains(Event.current.mousePosition);
            bool held = hovered && Mouse.current != null && Mouse.current.leftButton.isPressed;

            Color fill = held
                ? _visuals.ButtonActiveColor
                : hovered ? _visuals.ButtonHoverColor : _visuals.ButtonColor;
            if (on && !held)
            {
                fill = Color.Lerp(fill, _visuals.AccentColor, 0.16f);
            }
            if (!interactive)
            {
                fill = DarkenColor(_visuals.ButtonColor, 0.35f);
            }

            DrawVerticalGradient(rect, LightenColor(fill, 0.1f), DarkenColor(fill, 0.18f));
            DrawBorder(
                rect,
                hovered && interactive ? WithAlpha(_visuals.AccentColor, 0.6f) : WithAlpha(Color.black, 0.35f),
                Mathf.Max(1f, _scale));

            if (on)
            {
                DrawSolidRect(
                    new Rect(rect.x, rect.y, Mathf.Max(1f, _visuals.ButtonHoverStripeWidth * _scale), rect.height),
                    _visuals.AccentColor);
            }

            float padding = 14f * _scale;
            float pillHeight = Mathf.Max(12f, rect.height - (14f * _scale));
            _glyphContent.text = stateLabel;
            float pillWidth = Mathf.Max(
                _pillLabelStyle.CalcSize(_glyphContent).x + (16f * _scale),
                48f * _scale);
            Rect pillRect = new Rect(
                rect.xMax - padding - pillWidth,
                rect.center.y - (pillHeight * 0.5f),
                pillWidth,
                pillHeight);

            Color labelColor = interactive
                ? hovered ? _visuals.ButtonHoverTextColor : _visuals.PrimaryTextColor
                : WithAlpha(_visuals.SecondaryTextColor, 0.45f);
            DrawTintedLabel(
                new Rect(rect.x + padding, rect.y, pillRect.x - rect.x - padding - (6f * _scale), rect.height),
                label,
                _buttonLabelLeftStyle,
                labelColor);

            Color pillFill = on ? _visuals.AccentColor : DarkenColor(_visuals.SliderTrackColor, 0.2f);
            if (!interactive)
            {
                pillFill = DarkenColor(pillFill, 0.55f);
            }
            DrawVerticalGradient(pillRect, LightenColor(pillFill, 0.18f), DarkenColor(pillFill, 0.12f));
            DrawBorder(
                pillRect,
                on ? WithAlpha(Color.white, 0.2f) : WithAlpha(Color.black, 0.4f),
                Mathf.Max(1f, _scale));
            DrawTintedLabel(
                pillRect,
                stateLabel,
                _pillLabelStyle,
                on ? _visuals.PrimaryButtonTextColor : WithAlpha(_visuals.SecondaryTextColor, 0.9f));

            return GUI.Button(rect, GUIContent.none, _invisibleButtonStyle);
        }

        private void DrawTintedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Color previousColor = style.normal.textColor;
            ApplyTextColor(style, color);
            _glyphContent.text = text;
            GUI.Label(rect, _glyphContent, style);
            ApplyTextColor(style, previousColor);
        }

        // IMGUI has no letter-spacing, so tracked headings are laid out one glyph at
        // a time. Only short headings use this, so the per-glyph measuring is cheap.
        private void DrawTrackedLabel(Rect rect, string text, GUIStyle style, float tracking, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (tracking <= 0.01f || text.Length < 2)
            {
                DrawTintedLabel(rect, text, style, color);
                return;
            }

            float totalWidth = tracking * (text.Length - 1);
            for (int i = 0; i < text.Length; i++)
            {
                _glyphContent.text = text[i].ToString();
                totalWidth += style.CalcSize(_glyphContent).x;
            }

            TextAnchor previousAlignment = style.alignment;
            TextClipping previousClipping = style.clipping;
            Color previousColor = style.normal.textColor;

            float x = rect.x;
            if (previousAlignment == TextAnchor.MiddleCenter
                || previousAlignment == TextAnchor.UpperCenter
                || previousAlignment == TextAnchor.LowerCenter)
            {
                x = rect.x + ((rect.width - totalWidth) * 0.5f);
            }
            else if (previousAlignment == TextAnchor.MiddleRight
                || previousAlignment == TextAnchor.UpperRight
                || previousAlignment == TextAnchor.LowerRight)
            {
                x = rect.xMax - totalWidth;
            }

            style.alignment = TextAnchor.MiddleLeft;
            style.clipping = TextClipping.Overflow;
            ApplyTextColor(style, color);
            for (int i = 0; i < text.Length; i++)
            {
                _glyphContent.text = text[i].ToString();
                float glyphWidth = style.CalcSize(_glyphContent).x;
                GUI.Label(new Rect(x, rect.y, glyphWidth + 1f, rect.height), _glyphContent, style);
                x += glyphWidth + tracking;
            }

            style.alignment = previousAlignment;
            style.clipping = previousClipping;
            ApplyTextColor(style, previousColor);
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
            if (_textInputKeyboard != null)
            {
                _textInputKeyboard.onTextInput -= HandleTextInput;
                _textInputKeyboard = null;
            }
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
