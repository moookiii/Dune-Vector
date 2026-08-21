using System;
using System.Collections;
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
        Quit,
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
        private RenderTexture _gradedTarget;
        private Material _gradeMaterial;
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int GradeStartId = Shader.PropertyToID("_GradeStartV");
        private static readonly int GradeFullId = Shader.PropertyToID("_GradeFullV");
        private Font _runtimeFont;
        private GUIStyle _titleStyle;
        private GUIStyle _menuStyle;
        private DuneVectorTitleMenuEntry _selectedEntry = DuneVectorTitleMenuEntry.Start;
        private EventInstance _musicInstance;
        private bool _musicStarted;
        private bool _confirmed;
        private DuneVectorPauseMenu _optionsMenu;
        private DuneVectorAudioManager _audioManager;
        private GUIStyle _loadingStyle;
        private GUIStyle _versionStyle;
        private bool _loading;
        private float _loadingStartedAt;
        private bool OptionsOpen => _optionsMenu != null && _optionsMenu.IsPaused;

        private static readonly DuneVectorTitleMenuEntry[] MenuOrder =
        {
            DuneVectorTitleMenuEntry.Start,
            DuneVectorTitleMenuEntry.Options,
            DuneVectorTitleMenuEntry.Quit,
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
            // The mixer has to be up before the theme starts, or the stored music and effects
            // volumes only reach the buses when the options panel is first opened and everything
            // plays at full volume until then.
            EnsureAudioManager();
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
            _videoPlayer.playbackSpeed = Mathf.Max(0.01f, _settings.VideoPlaybackSpeed);
            _videoPlayer.Play();

            CreateGradeMaterial(width, height);
        }

        /// <summary>
        /// Builds the second target the graded frame lands in. The grade cannot be folded into the
        /// draw call because the menu draws the clip through GUI.DrawTexture for its crop, so the
        /// frame is graded on the way out of the video target instead and the crop path is left
        /// exactly as it was.
        /// </summary>
        private void CreateGradeMaterial(int width, int height)
        {
            if (_settings.VideoGradeShader == null || !GradingWanted)
            {
                return;
            }
            if (!_settings.VideoGradeShader.isSupported)
            {
                Debug.LogWarning(
                    "The title background grade shader is not supported on this device. The clip is drawn ungraded.",
                    this);
                return;
            }

            _gradeMaterial = new Material(_settings.VideoGradeShader)
            {
                name = "Dune Vector Title Background Grade",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _gradedTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Dune Vector Title Background Graded",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _gradedTarget.Create();
        }

        /// <summary>Nothing to grade when both knobs are already at their neutral values.</summary>
        private bool GradingWanted =>
            _settings.VideoGradeSaturation < 0.999f || _settings.VideoGradeBrightness < 0.999f;

        /// <summary>
        /// Regrades the newest frame. The band is authored as a share of the screen but sampled in
        /// texture space, so the crop the draw applies has to be undone here or the band would
        /// slide up the frame on any aspect ratio wider than the clip.
        /// </summary>
        private void UpdateGradedFrame()
        {
            if (_gradeMaterial == null || _gradedTarget == null || _videoTarget == null)
            {
                return;
            }
            if (_videoPlayer == null || !_videoPlayer.isPrepared)
            {
                return;
            }

            float visibleHeight = 1f;
            if (_settings.FillScreenWithVideo && _videoTarget.height > 0 && Screen.height > 0)
            {
                float screenAspect = Screen.width / (float)Screen.height;
                float videoAspect = _videoTarget.width / (float)_videoTarget.height;
                if (screenAspect > videoAspect)
                {
                    // The clip is scaled to the screen width, so the crop takes the top and bottom.
                    visibleHeight = Mathf.Clamp01(videoAspect / screenAspect);
                }
            }

            float visibleBottom = (1f - visibleHeight) * 0.5f;
            float visibleTop = visibleBottom + visibleHeight;
            float startV = visibleTop - (Mathf.Clamp01(_settings.VideoGradeStartFraction) * visibleHeight);
            float fullV = visibleTop - (Mathf.Clamp01(_settings.VideoGradeFullFraction) * visibleHeight);

            _gradeMaterial.SetFloat(SaturationId, Mathf.Clamp01(_settings.VideoGradeSaturation));
            _gradeMaterial.SetFloat(BrightnessId, Mathf.Clamp01(_settings.VideoGradeBrightness));
            _gradeMaterial.SetFloat(GradeStartId, startV);
            _gradeMaterial.SetFloat(GradeFullId, Mathf.Min(fullV, startV));
            Graphics.Blit(_videoTarget, _gradedTarget, _gradeMaterial);
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
            if (_gradedTarget != null)
            {
                _gradedTarget.Release();
                Destroy(_gradedTarget);
                _gradedTarget = null;
            }
            if (_gradeMaterial != null)
            {
                Destroy(_gradeMaterial);
                _gradeMaterial = null;
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
            PlayOneShot(_settings.ButtonEventPath, Mathf.Clamp01(_settings.ButtonVolume));
        }

        private void PlaySwapSound()
        {
            PlayOneShot(_settings.SwapEventPath, Mathf.Clamp01(_settings.SwapVolume));
        }

        private void PlayOneShot(string eventPath, float volume)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return;
            }

            try
            {
                EventInstance instance = RuntimeManager.CreateInstance(eventPath);
                instance.setVolume(volume);
                instance.start();
                instance.release();
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning(
                    $"Title screen event '{eventPath}' was not found. {exception.Message}",
                    this);
            }
        }

        /// <summary>
        /// Moves the highlight, sounding the swap event only when the entry actually changes so
        /// held keys and idle mouse movement stay silent.
        /// </summary>
        private void SetSelectedEntry(DuneVectorTitleMenuEntry entry)
        {
            if (_selectedEntry == entry)
            {
                return;
            }

            _selectedEntry = entry;
            PlaySwapSound();
        }

        private void Update()
        {
            KeepTitleMusicLooping();

            if (_confirmed || OptionsOpen)
            {
                return;
            }

            UpdateMouseSelection();
            UpdateKeyboardSelection();
        }

        private void LateUpdate()
        {
            UpdateGradedFrame();
        }

        /// <summary>
        /// Restarts the authored title event when it reaches its natural end. Keeping this at the
        /// screen level makes the title loop even when the FMOD event itself is authored as a
        /// one-shot, without spawning overlapping instances or changing menu sound effects.
        /// </summary>
        private void KeepTitleMusicLooping()
        {
            if (!_musicStarted || !_musicInstance.isValid())
            {
                return;
            }

            if (_musicInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == FMOD.RESULT.OK
                && playbackState == PLAYBACK_STATE.STOPPED)
            {
                _musicInstance.start();
            }
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

                case DuneVectorTitleMenuEntry.Quit:
                    QuitGame();
                    break;
            }
        }

        /// <summary>
        /// Closes the game from the title. Audio preferences are flushed first because the title
        /// options panel only holds them in memory until something asks for the write.
        /// </summary>
        private void QuitGame()
        {
            _audioManager?.FlushPreferences();
            ReleaseMusic();
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            StartCoroutine(LoadGameplayRoutine());
        }

        /// <summary>
        /// Streams the gameplay scene in behind the loading screen. Activation is held back until
        /// the scene is ready and the minimum display time has passed, so the title never cuts to
        /// a single flashed frame of loader on a warm load.
        /// </summary>
        private IEnumerator LoadGameplayRoutine()
        {
            _loading = true;
            _loadingStartedAt = Time.unscaledTime;

            AsyncOperation operation = SceneManager.LoadSceneAsync(_settings.GameplaySceneName);
            if (operation == null)
            {
                Debug.LogError(
                    $"Scene '{_settings.GameplaySceneName}' could not be loaded. Add it to Build Settings.",
                    this);
                _loading = false;
                _confirmed = false;
                yield break;
            }

            // Unity parks a ready scene at 0.9 and will not go further until it is activated.
            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
            {
                yield return null;
            }

            float minimumSeconds = Mathf.Max(0f, _settings.LoadingMinimumSeconds);
            while (Time.unscaledTime - _loadingStartedAt < minimumSeconds)
            {
                yield return null;
            }

            ReleaseMusic();
            operation.allowSceneActivation = true;
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
                wordWrap = false,
                richText = false,
            };
            _titleStyle.font = font;
            _titleStyle.fontStyle = _settings.TitleFontStyle;
            _titleStyle.fontSize = Mathf.Max(16, Mathf.RoundToInt(_settings.TitleFontSize * scale));
            // DrawLabel tints with GUI.color, which multiplies this. Leave it white or the
            // authored colors come out black.
            _titleStyle.normal.textColor = Color.white;

            _menuStyle ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                richText = false,
            };
            _menuStyle.font = font;
            _menuStyle.fontStyle = _settings.MenuFontStyle;
            _menuStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(_settings.MenuFontSize * scale));
            _menuStyle.normal.textColor = Color.white;

            _loadingStyle ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Normal,
                wordWrap = false,
                richText = false,
            };
            _loadingStyle.font = font;
            _loadingStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(_settings.LoadingFontSize * scale));
            _loadingStyle.normal.textColor = Color.white;

            _versionStyle ??= new GUIStyle
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Normal,
                wordWrap = false,
                richText = false,
            };
            _versionStyle.font = font;
            _versionStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(_settings.VersionFontSize * scale));
            _versionStyle.normal.textColor = Color.white;
        }

        private void OnGUI()
        {
            if (_settings == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureStyles();
            DrawBackground();
            DrawScrim();
            if (_loading)
            {
                DrawLoadingDim();
            }
            DrawTitle();
            if (_loading)
            {
                DrawLoading();
            }
            else
            {
                DrawMenu();
                DrawVersionStamp();
            }
        }

        /// <summary>
        /// Darkens the top of the video behind the headline and menu. A flat tint over the whole
        /// frame would protect the text just as well but would also drain the tunnel the clip is
        /// there to show, so the scrim holds full strength across the text and then fades out
        /// before it reaches the bright lower half.
        /// </summary>
        private void DrawScrim()
        {
            Color scrim = _settings.ScrimColor;
            if (scrim.a <= 0f)
            {
                return;
            }

            float solidHeight = Screen.height * Mathf.Clamp01(_settings.ScrimSolidFraction);
            float fadeHeight = Screen.height * Mathf.Clamp01(_settings.ScrimFadeFraction);
            if (solidHeight > 0f)
            {
                DuneVectorHudChrome.DrawRect(new Rect(0f, 0f, Screen.width, solidHeight), scrim);
            }
            if (fadeHeight > 0f)
            {
                DuneVectorHudChrome.DrawVerticalFade(
                    new Rect(0f, solidHeight, Screen.width, fadeHeight),
                    scrim,
                    true);
            }
        }

        /// <summary>
        /// Build number in the bottom corner. It reads from Player Settings rather than a second
        /// copy on the settings asset, so a shipped build cannot disagree with the version it
        /// reports.
        /// </summary>
        private void DrawVersionStamp()
        {
            if (!_settings.ShowVersionStamp || string.IsNullOrWhiteSpace(Application.version))
            {
                return;
            }

            float scale = GetScale();
            float margin = _settings.VersionMargin * scale;
            Rect safeArea = Screen.safeArea;
            float height = _versionStyle.fontSize * 2f;
            // safeArea is in screen space with y up, so the GUI-space bottom inset is the gap
            // between the safe area floor and the screen floor.
            float bottomInset = safeArea.yMin;
            float rightInset = Screen.width - safeArea.xMax;
            Rect rect = new Rect(
                0f,
                Screen.height - bottomInset - margin - height,
                Screen.width - rightInset - margin,
                height);
            DuneVectorHudChrome.DrawLabel(
                rect,
                _settings.VersionPrefix + Application.version,
                _versionStyle,
                _settings.VersionColor,
                _settings.TextShadowColor,
                new Vector2(scale, scale));
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
                _gradedTarget != null ? _gradedTarget : _videoTarget,
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
            DuneVectorHudChrome.DrawTrackedLabel(
                GetTitleRect(),
                _settings.TitleText,
                _titleStyle,
                _settings.TitleTracking * scale,
                _settings.TitleColor,
                _settings.TitleGlowColor,
                _settings.TitleGlowRadius * scale,
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
                else if (_settings.FrameUnselectedEntries)
                {
                    DrawUnselectedBox(GetSelectionBoxRect(entry), scale);
                }

                DuneVectorHudChrome.DrawTrackedLabel(
                    GetMenuItemRect(entry),
                    GetLabel(entry),
                    _menuStyle,
                    _settings.MenuTracking * scale,
                    selected ? _settings.SelectedMenuItemColor : _settings.MenuItemColor,
                    Color.clear,
                    0f,
                    _settings.TextShadowColor,
                    shadowOffset);
            }
        }

        private void DrawLoadingDim()
        {
            if (_settings.LoadingDimOpacity <= 0f)
            {
                return;
            }

            DuneVectorHudChrome.DrawRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0f, 0f, 0f, Mathf.Clamp01(_settings.LoadingDimOpacity)));
        }

        private void DrawLoading()
        {
            float scale = GetScale();
            float elapsed = Time.unscaledTime - _loadingStartedAt;
            Rect textRect = GetMenuItemRect(DuneVectorTitleMenuEntry.Start);
            Vector2 shadowOffset = new Vector2(
                _settings.TextShadowOffset * scale * 0.5f,
                _settings.TextShadowOffset * scale * 0.5f);

            DuneVectorHudChrome.DrawLabel(
                textRect,
                GetLoadingText(elapsed),
                _loadingStyle,
                _settings.LoadingColor,
                _settings.TextShadowColor,
                shadowOffset);

            DrawLoadingBar(textRect, elapsed, scale);
        }

        /// <summary>
        /// Cycles the trailing dots. The word itself is drawn from a fixed-width rect so the text
        /// does not jitter sideways as dots come and go.
        /// </summary>
        private string GetLoadingText(float elapsed)
        {
            if (_settings.LoadingDotCount <= 0)
            {
                return _settings.LoadingText;
            }

            int steps = _settings.LoadingDotCount + 1;
            int dots = Mathf.FloorToInt(elapsed / Mathf.Max(0.02f, _settings.LoadingDotIntervalSeconds)) % steps;
            return _settings.LoadingText + new string('.', dots);
        }

        /// <summary>
        /// An indeterminate sweep. Unity's load progress jumps to 0.9 and parks there, so showing
        /// it as a filling bar would misrepresent what the load is actually doing.
        /// </summary>
        private void DrawLoadingBar(Rect textRect, float elapsed, float scale)
        {
            float width = _settings.LoadingBarWidth * scale;
            float height = Mathf.Max(1f, _settings.LoadingBarHeight * scale);
            Rect track = new Rect(
                (Screen.width - width) * 0.5f,
                textRect.yMax + (_settings.LoadingBarGap * scale),
                width,
                height);
            DuneVectorHudChrome.DrawRect(track, _settings.LoadingBarTrackColor);

            float sweepWidth = Mathf.Max(1f, width * Mathf.Clamp01(_settings.LoadingBarSweepFraction));
            float period = Mathf.Max(0.1f, _settings.LoadingBarSweepSeconds);
            // Ping-pong so the sweep runs back and forth instead of snapping to the left edge.
            float travel = Mathf.PingPong(elapsed / period, 1f);
            DuneVectorHudChrome.DrawRect(
                new Rect(track.x + (travel * (width - sweepWidth)), track.y, sweepWidth, height),
                _settings.LoadingBarSweepColor);
        }

        /// <summary>
        /// The resting frame every unselected entry wears. It never pulses, so the moving
        /// highlight stays the only thing the eye tracks.
        /// </summary>
        private void DrawUnselectedBox(Rect box, float scale)
        {
            DuneVectorHudChrome.DrawRect(box, _settings.UnselectedBoxFillColor);
            DuneVectorHudChrome.DrawBorder(
                box,
                _settings.UnselectedBoxColor,
                Mathf.Max(1f, _settings.SelectionBoxThickness * scale));
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
                    _settings.OptionsFooterHint,
                    _settings.OptionsPanelWidth,
                    _settings.OptionsPanelHeight,
                    _settings.OptionsVideoPanelHeight,
                    _settings.OptionsVideoBackButtonLabel,
                    _settings.OptionsVideoFooterHint,
                    _settings.OptionsPanelVerticalOffset);
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
            if (_audioManager != null)
            {
                return _audioManager;
            }

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
            _audioManager = audio;
            return audio;
        }

        private string GetLabel(DuneVectorTitleMenuEntry entry)
        {
            switch (entry)
            {
                case DuneVectorTitleMenuEntry.Start:
                    return _settings.StartLabel;
                case DuneVectorTitleMenuEntry.Options:
                    return _settings.OptionsLabel;
                default:
                    return _settings.QuitLabel;
            }
        }
    }
}
