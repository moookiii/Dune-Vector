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
            public int Version = 11;
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
            ChromaticAberrationEnabled = defaults == null || defaults.DefaultChromaticAberrationEnabled;
            LensDistortionEnabled = defaults == null || defaults.DefaultLensDistortionEnabled;
            CrtLinesEnabled = defaults == null || defaults.DefaultCrtLinesEnabled;
            FilmGrainEnabled = defaults == null || defaults.DefaultFilmGrainEnabled;
            VignetteEnabled = defaults == null || defaults.DefaultVignetteEnabled;
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
            ChromaticAberrationEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultChromaticAberrationEnabled;
            LensDistortionEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultLensDistortionEnabled;
            CrtLinesEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultCrtLinesEnabled;
            FilmGrainEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultFilmGrainEnabled;
            VignetteEnabled = _settings.PauseMenu == null
                || _settings.PauseMenu.DefaultVignetteEnabled;
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
                if (stored != null && stored.Version >= 1 && stored.Version <= 11)
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
                    if (stored.Version < 10
                        && (VisualizerEffectMask & MusicVisualEffectGroups.Sky) != 0)
                    {
                        VisualizerEffectMask |= MusicVisualEffectGroups.BassLines;
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
        private RetroCrtScanlineTuning _retroCrtScanlines;

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
        private GUIStyle _songTitleStyle;
        private GUIStyle _songTitleShadowStyle;
        private GUIStyle _songControlStyle;
        private GUIStyle _songControlShadowStyle;
        private GUIStyle _songPauseStyle;
        private GUIStyle _songPauseShadowStyle;

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
                    _showCompendium = false;
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
            y += sliderRowHeight;

            DrawVolumeRow(
                new Rect(content.x, y, content.width, sliderRowHeight),
                "DIALOGUE",
                _audio != null ? _audio.DialogueVolume : 0f,
                value => _audio?.SetDialogueVolume(value),
                scale);
            y += sliderRowHeight + (_visuals.DialogueButtonGap * scale);

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
            y += buttonHeight + gap;

            if (GUI.Button(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.MusicVisualizerSettingsButtonLabel,
                    _secondaryButtonStyle))
            {
                _showMusicVisualizerSettings = true;
            }
            y += buttonHeight + gap;

            if (GUI.Button(
                    new Rect(content.x, y, content.width, buttonHeight),
                    _visuals.VideoSettingsButtonLabel,
                    _secondaryButtonStyle))
            {
                _showVideoSettings = true;
            }

            float hintHeight = _hintStyle.lineHeight;
            GUI.Label(
                new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight),
                "ESC  /  RETURN TO THE DESERT",
                _hintStyle);
        }

        private void DrawVideoSettingsScreen(float scale)
        {
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
            GUI.Label(new Rect(content.x, y, content.width, titleHeight), _visuals.VideoSettingsTitle, _titleStyle);
            y += titleHeight;

            float subtitleHeight = _subtitleStyle.lineHeight;
            GUI.Label(new Rect(content.x, y, content.width, subtitleHeight), _visuals.VideoSettingsSubtitle, _subtitleStyle);
            y += subtitleHeight + gap;

            DrawSolidRect(new Rect(content.x, y, content.width, Mathf.Max(1f, scale)), _visuals.DividerColor);
            y += gap * 1.5f;

            float sectionHeight = _sectionStyle.lineHeight;
            float buttonHeight = _visuals.ButtonHeight * scale;
            GUI.Label(
                new Rect(content.x, y, content.width, sectionHeight),
                _visuals.VideoAntiAliasingLabel,
                _sectionStyle);
            y += sectionHeight + gap;

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

            GUI.Label(new Rect(content.x, y, content.width, sectionHeight), _visuals.VideoSettingsSectionLabel, _sectionStyle);
            y += sectionHeight + gap;

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

            y += gap;

            float navigationWidth = (content.width - gap) * 0.5f;
            if (GUI.Button(
                    new Rect(content.x, y, navigationWidth, buttonHeight),
                    _visuals.VideoSettingsResetButtonLabel,
                    _secondaryButtonStyle))
            {
                _audio?.ResetVideoSettingsToDefaults();
                ApplyVideoPreferences();
            }
            if (GUI.Button(
                    new Rect(content.x + navigationWidth + gap, y, navigationWidth, buttonHeight),
                    _visuals.VideoSettingsBackButtonLabel,
                    _secondaryButtonStyle))
            {
                _showVideoSettings = false;
            }

            float hintHeight = _hintStyle.lineHeight;
            GUI.Label(
                new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight),
                _visuals.VideoSettingsHintLabel,
                _hintStyle);
        }

        private void DrawMusicVisualizerSettingsScreen(float scale)
        {
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
            GUI.Label(new Rect(content.x, y, content.width, titleHeight), _visuals.MusicVisualizerSettingsTitle, _titleStyle);
            y += titleHeight;
            float subtitleHeight = _subtitleStyle.lineHeight;
            GUI.Label(new Rect(content.x, y, content.width, subtitleHeight), _visuals.MusicVisualizerSettingsSubtitle, _subtitleStyle);
            y += subtitleHeight + gap;
            DrawSolidRect(new Rect(content.x, y, content.width, Mathf.Max(1f, scale)), _visuals.DividerColor);
            y += gap * 1.5f;

            float sectionHeight = _sectionStyle.lineHeight;
            float buttonHeight = _visuals.ButtonHeight * scale;
            GUI.Label(new Rect(content.x, y, content.width, sectionHeight), _visuals.MusicVisualizerSettingsSectionLabel, _sectionStyle);
            y += sectionHeight + gap;

            DrawMusicVisualizerMasterToggle(new Rect(content.x, y, content.width, buttonHeight));
            y += buttonHeight + gap;
            float splitToggleWidth = (content.width - gap) * 0.5f;
            DrawMusicVisualizerEffectToggle(new Rect(content.x, y, splitToggleWidth, buttonHeight), _visuals.MusicVisualizerSkyLabel,
                MusicVisualEffectGroups.Sky | MusicVisualEffectGroups.Filaments | MusicVisualEffectGroups.TrebleParticles);
            DrawMusicVisualizerEffectToggle(
                new Rect(content.x + splitToggleWidth + gap, y, splitToggleWidth, buttonHeight),
                _visuals.MusicVisualizerBassLinesLabel,
                MusicVisualEffectGroups.BassLines);
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

            float navigationWidth = (content.width - gap) * 0.5f;
            if (GUI.Button(new Rect(content.x, y, navigationWidth, buttonHeight), _visuals.MusicVisualizerSettingsResetButtonLabel, _secondaryButtonStyle))
            {
                _audio?.ResetMusicVisualizerSettingsToDefaults();
            }
            if (GUI.Button(new Rect(content.x + navigationWidth + gap, y, navigationWidth, buttonHeight), _visuals.MusicVisualizerSettingsBackButtonLabel, _secondaryButtonStyle))
            {
                _showMusicVisualizerSettings = false;
            }

            float hintHeight = _hintStyle.lineHeight;
            GUI.Label(new Rect(content.x, content.yMax - hintHeight, content.width, hintHeight), _visuals.MusicVisualizerSettingsHintLabel, _hintStyle);
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
            if (GUI.Button(
                    rect,
                    $"{label}  /  {stateLabel}",
                    enabled ? _primaryButtonStyle : _secondaryButtonStyle))
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
            if (GUI.Button(rect, $"{label}  /  {stateLabel}", enabled ? _primaryButtonStyle : _secondaryButtonStyle))
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
            if (GUI.Button(rect, label, selected ? _primaryButtonStyle : _secondaryButtonStyle))
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

        private static void SetStaticTextColor(GUIStyle style, Color color)
        {
            // Unity's skin can supply a different label color for hovered controls.
            // The song title is informational and should remain visually static.
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
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
