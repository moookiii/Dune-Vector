using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace DuneVector
{
    public enum DuneVectorTitleMenuEntry
    {
        Start,
        Options,
    }

    /// <summary>
    /// Startup title screen. Plays the authored background video untranscoded behind a bold
    /// headline and a two-entry menu driven by the arrow keys, WASD, Enter, or the mouse.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorTitleScreen : MonoBehaviour
    {
        [Header("Runtime Configuration")]
        [Tooltip("Reusable asset containing every gameplay and presentation tuning value.")]
        public DuneVectorRuntimeSettings RuntimeSettings;

        [Header("Scene References")]
        [Tooltip("Camera authored in the scene so the title framing is visible before Play mode.")]
        public Camera SceneCamera;

        private TitleScreenTuning _settings;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTarget;
        private Font _runtimeFont;
        private GUIStyle _titleStyle;
        private GUIStyle _menuStyle;
        private DuneVectorTitleMenuEntry _selectedEntry = DuneVectorTitleMenuEntry.Start;
        private EventInstance _musicInstance;
        private bool _musicStarted;
        private bool _confirmed;
        private DuneVectorPauseMenu _optionsMenu;
        private bool OptionsOpen => _optionsMenu != null && _optionsMenu.IsPaused;

        private static readonly DuneVectorTitleMenuEntry[] MenuOrder =
        {
            DuneVectorTitleMenuEntry.Start,
            DuneVectorTitleMenuEntry.Options,
        };

        public DuneVectorTitleMenuEntry SelectedEntry => _selectedEntry;

        private void Awake()
        {
            if (RuntimeSettings == null)
            {
                Debug.LogError(
                    "DuneVectorTitleScreen requires the Dune Vector Runtime Settings asset. Assign it in the Inspector.",
                    this);
                enabled = false;
                return;
            }

            RuntimeSettings.EnsureInitialized();
            _settings = RuntimeSettings.TitleScreen;

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            EnsureCamera();
            CreateRuntimeFont();
            CreateVideoPlayer();
        }

        private void Start()
        {
            StartTitleMusic();
        }

        private void OnDestroy()
        {
            ReleaseMusic();
            ReleaseVideo();
        }

        private void EnsureCamera()
        {
            if (SceneCamera == null)
            {
                SceneCamera = Camera.main;
            }
            if (SceneCamera == null)
            {
                return;
            }

            SceneCamera.clearFlags = CameraClearFlags.SolidColor;
            SceneCamera.backgroundColor = _settings.BackgroundColor;
            if (SceneCamera.GetComponent<StudioListener>() == null)
            {
                SceneCamera.gameObject.AddComponent<StudioListener>();
            }
        }

        private void CreateRuntimeFont()
        {
            if (_settings.InterfaceFont != null)
            {
                return;
            }

            try
            {
                _runtimeFont = Font.CreateDynamicFontFromOSFont(
                    _settings.FallbackOsFontName,
                    Mathf.Max(10, _settings.MenuFontSize));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Title screen font '{_settings.FallbackOsFontName}' is unavailable. {exception.Message}",
                    this);
            }
        }

        private void CreateVideoPlayer()
        {
            if (_settings.BackgroundVideo == null)
            {
                Debug.LogWarning(
                    "No title screen background video is assigned on the runtime settings asset. The menu renders over the background color.",
                    this);
                return;
            }

            int width = Mathf.Max(1, (int)_settings.BackgroundVideo.width);
            int height = Mathf.Max(1, (int)_settings.BackgroundVideo.height);
            _videoTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Dune Vector Title Background",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _videoTarget.Create();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = _settings.BackgroundVideo;
            _videoPlayer.isLooping = _settings.LoopBackgroundVideo;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTarget;
            _videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.audioOutputMode = _settings.BackgroundVideoAudioVolume > 0f
                ? VideoAudioOutputMode.Direct
                : VideoAudioOutputMode.None;
            if (_videoPlayer.audioOutputMode == VideoAudioOutputMode.Direct)
            {
                _videoPlayer.SetDirectAudioVolume(0, _settings.BackgroundVideoAudioVolume);
            }
            _videoPlayer.Play();
        }

        private void ReleaseVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
            }
            if (_videoTarget != null)
            {
                _videoTarget.Release();
                Destroy(_videoTarget);
                _videoTarget = null;
            }
        }

        private void StartTitleMusic()
        {
            if (string.IsNullOrWhiteSpace(_settings.MusicEventPath))
            {
                return;
            }

            try
            {
                _musicInstance = RuntimeManager.CreateInstance(_settings.MusicEventPath);
                _musicInstance.setVolume(Mathf.Clamp01(_settings.MusicVolume));
                _musicInstance.start();
                _musicStarted = true;
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"Title theme event '{_settings.MusicEventPath}' was not found. {exception.Message}",
                    this);
            }
        }

        private void ReleaseMusic()
        {
            if (!_musicStarted)
            {
                return;
            }

            FMOD.Studio.STOP_MODE stopMode = _settings != null && _settings.MusicFadeOutSeconds > 0f
                ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                : FMOD.Studio.STOP_MODE.IMMEDIATE;
            _musicInstance.stop(stopMode);
            _musicInstance.release();
            _musicStarted = false;
        }

        private void PlayButtonSound()
        {
            if (string.IsNullOrWhiteSpace(_settings.ButtonEventPath))
            {
                return;
            }

            try
            {
                EventInstance instance = RuntimeManager.CreateInstance(_settings.ButtonEventPath);
                instance.setVolume(Mathf.Clamp01(_settings.ButtonVolume));
                instance.start();
                instance.release();
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"Title button event '{_settings.ButtonEventPath}' was not found. {exception.Message}",
                    this);
            }
        }

        /// <summary>
        /// Moves the highlight, sounding the button event only when the entry actually changes so
        /// held keys and idle mouse movement stay silent.
        /// </summary>
        private void SetSelectedEntry(DuneVectorTitleMenuEntry entry)
        {
            if (_selectedEntry == entry)
            {
                return;
            }

            _selectedEntry = entry;
            PlayButtonSound();
        }

        private void Update()
        {
            if (_confirmed || OptionsOpen)
            {
                return;
            }

            UpdateMouseSelection();
            UpdateKeyboardSelection();
        }

        private void UpdateMouseSelection()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            bool hoveringAnyEntry = false;
            for (int index = 0; index < MenuOrder.Length; index++)
            {
                if (!GetSelectionBoxRect(MenuOrder[index]).Contains(guiPosition))
                {
                    continue;
                }

                hoveringAnyEntry = true;
                SetSelectedEntry(MenuOrder[index]);
                break;
            }

            if (hoveringAnyEntry && mouse.leftButton.wasPressedThisFrame)
            {
                Confirm(_selectedEntry);
            }
        }

        private void UpdateKeyboardSelection()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            bool movedUp = keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame;
            bool movedDown = keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame;
            if (movedUp != movedDown)
            {
                MoveSelection(movedDown ? 1 : -1);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Confirm(_selectedEntry);
            }
        }

        private void MoveSelection(int direction)
        {
            int current = Array.IndexOf(MenuOrder, _selectedEntry);
            int next = ((current + direction) % MenuOrder.Length + MenuOrder.Length) % MenuOrder.Length;
            SetSelectedEntry(MenuOrder[next]);
        }

        private void Confirm(DuneVectorTitleMenuEntry entry)
        {
            if (_settings.PlayButtonOnConfirm)
            {
                PlayButtonSound();
            }

            switch (entry)
            {
                case DuneVectorTitleMenuEntry.Start:
                    LoadGameplayScene();
                    break;

                case DuneVectorTitleMenuEntry.Options:
                    if (!_settings.OptionsEnabled)
                    {
                        Debug.Log("The Dune Vector options screen is not built yet.", this);
                        break;
                    }

                    OpenOptions();
                    break;
            }
        }

        private void LoadGameplayScene()
        {
            if (string.IsNullOrWhiteSpace(_settings.GameplaySceneName))
            {
                Debug.LogError(
                    "No gameplay scene name is authored on the runtime settings asset, so START cannot load anything.",
                    this);
                return;
            }

            _confirmed = true;
            ReleaseMusic();
            SceneManager.LoadScene(_settings.GameplaySceneName);
        }

        private float GetScale()
        {
            float widthScale = Screen.width / Mathf.Max(1f, _settings.ReferenceWidth);
            float heightScale = Screen.height / Mathf.Max(1f, _settings.ReferenceHeight);
            float minimum = Mathf.Min(_settings.MinimumScale, _settings.MaximumScale);
            float maximum = Mathf.Max(_settings.MinimumScale, _settings.MaximumScale);
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), minimum, maximum);
        }

        private Rect GetTitleRect()
        {
            float scale = GetScale();
            float height = _settings.TitleHeight * scale;
            float y = Screen.safeArea.yMin + (_settings.TitleTopPadding * scale);
            return new Rect(0f, y, Screen.width, height);
        }

        private Rect GetMenuItemRect(DuneVectorTitleMenuEntry entry)
        {
            float scale = GetScale();
            Rect title = GetTitleRect();
            float width = _settings.MenuItemWidth * scale;
            float height = _settings.MenuItemHeight * scale;
            float gap = _settings.MenuItemGap * scale;
            float firstY = title.yMax + (_settings.TitleToMenuGap * scale);
            int index = Mathf.Max(0, Array.IndexOf(MenuOrder, entry));
            return new Rect(
                (Screen.width - width) * 0.5f,
                firstY + (index * (height + gap)),
                width,
                height);
        }

        private Rect GetSelectionBoxRect(DuneVectorTitleMenuEntry entry)
        {
            float scale = GetScale();
            Rect item = GetMenuItemRect(entry);
            float paddingX = _settings.SelectionBoxPaddingX * scale;
            float paddingY = _settings.SelectionBoxPaddingY * scale;
            return new Rect(
                item.x - paddingX,
                item.y - paddingY,
                item.width + (paddingX * 2f),
                item.height + (paddingY * 2f));
        }

        private void EnsureStyles()
        {
            Font font = _settings.InterfaceFont != null ? _settings.InterfaceFont : _runtimeFont;
            float scale = GetScale();

            _titleStyle ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                richText = false,
            };
            _titleStyle.font = font;
            _titleStyle.fontSize = Mathf.Max(16, Mathf.RoundToInt(_settings.TitleFontSize * scale));
            // DrawLabel tints with GUI.color, which multiplies this. Leave it white or the
            // authored colors come out black.
            _titleStyle.normal.textColor = Color.white;

            _menuStyle ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Normal,
                wordWrap = false,
                richText = false,
            };
            _menuStyle.font = font;
            _menuStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(_settings.MenuFontSize * scale));
            _menuStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (_settings == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureStyles();
            DrawBackground();
            DrawTitle();
            DrawMenu();
        }

        private void DrawBackground()
        {
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            DuneVectorHudChrome.DrawRect(screen, _settings.BackgroundColor);
            if (_videoTarget == null || _videoPlayer == null || !_videoPlayer.isPrepared)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = _settings.VideoTint;
            GUI.DrawTexture(
                screen,
                _videoTarget,
                _settings.FillScreenWithVideo ? ScaleMode.ScaleAndCrop : ScaleMode.ScaleToFit,
                false);
            GUI.color = previous;
        }

        private void DrawTitle()
        {
            float scale = GetScale();
            Vector2 shadowOffset = new Vector2(
                _settings.TextShadowOffset * scale,
                _settings.TextShadowOffset * scale);
            DuneVectorHudChrome.DrawLabel(
                GetTitleRect(),
                _settings.TitleText,
                _titleStyle,
                _settings.TitleColor,
                _settings.TextShadowColor,
                shadowOffset);
        }

        private void DrawMenu()
        {
            float scale = GetScale();
            Vector2 shadowOffset = new Vector2(
                _settings.TextShadowOffset * scale * 0.5f,
                _settings.TextShadowOffset * scale * 0.5f);

            for (int index = 0; index < MenuOrder.Length; index++)
            {
                DuneVectorTitleMenuEntry entry = MenuOrder[index];
                bool selected = entry == _selectedEntry;
                if (selected)
                {
                    DrawSelectionBox(GetSelectionBoxRect(entry), scale);
                }

                DuneVectorHudChrome.DrawLabel(
                    GetMenuItemRect(entry),
                    GetLabel(entry),
                    _menuStyle,
                    selected ? _settings.SelectedMenuItemColor : _settings.MenuItemColor,
                    _settings.TextShadowColor,
                    shadowOffset);
            }
        }

        private void DrawSelectionBox(Rect box, float scale)
        {
            float pulse = 1f;
            if (_settings.SelectionBoxPulseSpeed > 0f)
            {
                float wave = (Mathf.Sin(Time.unscaledTime * _settings.SelectionBoxPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                pulse = Mathf.Lerp(
                    _settings.SelectionBoxPulseMinimumAlpha,
                    _settings.SelectionBoxPulseMaximumAlpha,
                    wave);
            }

            Color fill = _settings.SelectionBoxFillColor;
            fill.a *= pulse;
            DuneVectorHudChrome.DrawRect(box, fill);

            Color border = _settings.SelectionBoxColor;
            border.a *= pulse;
            DuneVectorHudChrome.DrawBorder(
                box,
                border,
                Mathf.Max(1f, _settings.SelectionBoxThickness * scale));
        }

        /// <summary>
        /// Builds the options panel on first use. It is the pause menu in title mode, so the
        /// mixer, sensitivity, controls, visualizer and video screens all come from one place
        /// rather than a second copy that would drift from the in-game one.
        /// </summary>
        private void OpenOptions()
        {
            if (_optionsMenu == null)
            {
                DuneVectorAudioManager audio = EnsureAudioManager();
                _optionsMenu = gameObject.AddComponent<DuneVectorPauseMenu>();
                _optionsMenu.InitializeForTitleScreen(
                    audio,
                    RuntimeSettings.PlayerTuning,
                    RuntimeSettings.Audio.PauseMenu,
                    RuntimeSettings.RetroCrtScanlines,
                    _settings.OptionsHeading,
                    _settings.OptionsSubheading,
                    _settings.OptionsFooterHint);
                _optionsMenu.TitleOptionsClosed += HandleOptionsClosed;
            }

            _optionsMenu.OpenTitleOptions();
        }

        private void HandleOptionsClosed()
        {
            PlayButtonSound();
        }

        /// <summary>
        /// The audio manager is a persistent singleton, so the title only has to stand one up if
        /// nothing has yet. It is brought up preferences-only: the gameplay playlist must not
        /// start over the title theme, and the gameplay scene still runs the full Initialize.
        /// </summary>
        private DuneVectorAudioManager EnsureAudioManager()
        {
            DuneVectorAudioManager audio = DuneVectorAudioManager.Instance;
            if (audio == null)
            {
                GameObject audioObject = new GameObject("FMOD Audio and Background Music");
                audio = audioObject.AddComponent<DuneVectorAudioManager>();
            }

            audio.InitializePreferencesOnly(
                RuntimeSettings.Audio,
                RuntimeSettings.Performance,
                RuntimeSettings.PlayerTuning.CameraAntiAliasingMode);
            return audio;
        }

        private string GetLabel(DuneVectorTitleMenuEntry entry)
        {
            return entry == DuneVectorTitleMenuEntry.Start
                ? _settings.StartLabel
                : _settings.OptionsLabel;
        }
    }
}
