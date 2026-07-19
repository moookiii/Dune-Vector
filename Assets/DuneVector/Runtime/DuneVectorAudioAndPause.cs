using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorAudioManager : MonoBehaviour
    {
        private const string MusicVolumePreference = "DuneVector.Audio.MusicVolume";
        private const string SoundEffectsVolumePreference = "DuneVector.Audio.SoundEffectsVolume";

        public float MusicVolume { get; private set; }
        public float SoundEffectsVolume { get; private set; }

        private AudioTuning _settings;
        private EventInstance _musicInstance;
        private Bus _musicBus;
        private Bus _soundEffectsBus;
        private bool _hasMusicBus;
        private bool _hasSoundEffectsBus;

        public void Initialize(AudioTuning settings)
        {
            _settings = settings;
            if (_settings == null)
            {
                Debug.LogError("Dune Vector audio requires Audio Tuning in the Runtime Settings asset.", this);
                enabled = false;
                return;
            }

            MusicVolume = LoadVolume(MusicVolumePreference, _settings.DefaultMusicVolume);
            SoundEffectsVolume = LoadVolume(SoundEffectsVolumePreference, _settings.DefaultSoundEffectsVolume);

            _hasMusicBus = TryGetBus(_settings.MusicBusPath, out _musicBus);
            _hasSoundEffectsBus = TryGetBus(_settings.SoundEffectsBusPath, out _soundEffectsBus);
            ApplyMixerVolumes();
            StartBackgroundMusic();
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
            StoreVolume(MusicVolumePreference, MusicVolume);
        }

        public void SetSoundEffectsVolume(float volume)
        {
            SoundEffectsVolume = Mathf.Clamp01(volume);
            if (_hasSoundEffectsBus && _soundEffectsBus.isValid())
            {
                _soundEffectsBus.setVolume(SoundEffectsVolume);
            }
            StoreVolume(SoundEffectsVolumePreference, SoundEffectsVolume);
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

        private float LoadVolume(string key, float defaultVolume)
        {
            float fallback = Mathf.Clamp01(defaultVolume);
            return _settings.PersistVolumeSettings
                ? Mathf.Clamp01(PlayerPrefs.GetFloat(key, fallback))
                : fallback;
        }

        private void StoreVolume(string key, float volume)
        {
            if (_settings != null && _settings.PersistVolumeSettings)
            {
                PlayerPrefs.SetFloat(key, volume);
            }
        }

        private void OnDestroy()
        {
            if (_musicInstance.isValid())
            {
                _musicInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _musicInstance.release();
                _musicInstance.clearHandle();
            }
            if (_settings != null && _settings.PersistVolumeSettings)
            {
                PlayerPrefs.Save();
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorPauseMenu : MonoBehaviour
    {
        public bool IsPaused { get; private set; }

        private DronePlayer _player;
        private DroneHealth _health;
        private DuneVectorAudioManager _audio;

        public void Initialize(DronePlayer player, DroneHealth health, DuneVectorAudioManager audio)
        {
            _player = player;
            _health = health;
            _audio = audio;
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
        }

        private void HandleDeath()
        {
            IsPaused = false;
            _player?.SetInputEnabled(false);
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
        }
    }
}
