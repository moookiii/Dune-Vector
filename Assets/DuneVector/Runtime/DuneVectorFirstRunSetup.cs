using System;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace DuneVector
{
    /// <summary>
    /// The two questions a save is asked once, the first time it launches. It is drawn over the
    /// title screen rather than in a scene of its own, so the title video, fonts and FMOD menu
    /// sounds are already up and the answers land before anything loads the world.
    /// </summary>
    public sealed class DuneVectorFirstRunSetup
    {
        private enum Step
        {
            PostProcessing = 0,
            Visualizer = 1,
        }

        private const int StepCount = 2;

        [Serializable]
        private sealed class FirstRunData
        {
            public int Version = 1;
            public bool Completed;
            public bool PostProcessingEnabled;
            public bool VisualizerFlashEffectsEnabled;
        }

        private readonly FirstRunSetupTuning _settings;
        private readonly TitleScreenTuning _titleSettings;
        private readonly MonoBehaviour _host;
        private readonly Func<DuneVectorAudioManager> _ensureAudioManager;
        private readonly string _savePath;

        private Step _step = Step.PostProcessing;
        private bool _yesSelected = true;
        private bool _postProcessingAnswer;
        private bool _visualizerAnswer;
        private float _stepStartedAt;
        private float _completedAt;
        private bool _answersWritten;

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTarget;
        private GUIStyle _stepStyle;
        private GUIStyle _questionStyle;
        private GUIStyle _detailStyle;
        private GUIStyle _captionStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _footerStyle;
        private Rect _yesRect;
        private Rect _noRect;
        private float _fade = 1f;
        private readonly GUIContent _measureContent = new GUIContent();

        /// <summary>True once both answers are in and the hold after the second confirm has passed.</summary>
        public bool IsComplete { get; private set; }

        public DuneVectorFirstRunSetup(
            FirstRunSetupTuning settings,
            TitleScreenTuning titleSettings,
            MonoBehaviour host,
            Func<DuneVectorAudioManager> ensureAudioManager)
        {
            _settings = settings;
            _titleSettings = titleSettings;
            _host = host;
            _ensureAudioManager = ensureAudioManager;
            _savePath = GetSavePath(settings);
            _stepStartedAt = Time.unscaledTime;
        }

        public static string GetSavePath(FirstRunSetupTuning settings)
        {
            string fileName = settings != null && !string.IsNullOrWhiteSpace(settings.SaveFileName)
                ? settings.SaveFileName
                : "DuneVectorFirstRun.dat";
            return System.IO.Path.Combine(Application.persistentDataPath, fileName);
        }

        /// <summary>
        /// Whether this save has already answered both questions. A missing or unreadable marker
        /// counts as not answered, so a corrupt file re-asks rather than silently skipping setup.
        /// </summary>
        public static bool HasCompleted(FirstRunSetupTuning settings)
        {
            string path = GetSavePath(settings);
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return false;
                }

                FirstRunData stored = JsonUtility.FromJson<FirstRunData>(System.IO.File.ReadAllText(path));
                return stored != null && stored.Completed;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read the first-run marker at '{path}'. {exception.Message}");
                return false;
            }
        }

        public void Update()
        {
            if (IsComplete)
            {
                return;
            }

            if (_answersWritten)
            {
                // Both answers are in. The last confirm is held on screen briefly so the choice
                // reads before the title menu replaces it.
                if (Time.unscaledTime - _completedAt >= Mathf.Max(0f, _settings.CompletionHoldSeconds))
                {
                    ReleaseVideo();
                    IsComplete = true;
                }
                return;
            }

            UpdateMouse();
            UpdateKeyboard();
        }

        private void UpdateMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            bool overYes = _yesRect.Contains(guiPosition);
            bool overNo = _noRect.Contains(guiPosition);
            if (overYes || overNo)
            {
                SetSelection(overYes);
            }

            if ((overYes || overNo) && mouse.leftButton.wasPressedThisFrame)
            {
                Confirm();
            }
        }

        private void UpdateKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            bool movedLeft = keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame;
            bool movedRight = keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame;
            if (movedLeft != movedRight)
            {
                SetSelection(movedLeft);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Confirm();
            }
        }

        /// <summary>Moves the highlight, sounding the swap event only when it actually changes.</summary>
        private void SetSelection(bool yes)
        {
            if (_yesSelected == yes)
            {
                return;
            }

            _yesSelected = yes;
            PlayOneShot(_titleSettings.SwapEventPath, _titleSettings.SwapVolume);
        }

        private void Confirm()
        {
            PlayOneShot(_titleSettings.ButtonEventPath, _titleSettings.ButtonVolume);

            if (_step == Step.PostProcessing)
            {
                ApplyPostProcessing(_yesSelected);
                _postProcessingAnswer = _yesSelected;
                _step = Step.Visualizer;
                _yesSelected = true;
                _stepStartedAt = Time.unscaledTime;
                CreateVideoPlayer();
                return;
            }

            ApplyVisualizerEffects(_yesSelected);
            _visualizerAnswer = _yesSelected;
            Save();
            _answersWritten = true;
            _completedAt = Time.unscaledTime;
        }

        /// <summary>
        /// YES switches on every post-processing option the video settings screen exposes; NO
        /// switches the same set off. Both answers write through the audio manager so the choice
        /// lands in the same preferences file the options menu edits.
        /// </summary>
        private void ApplyPostProcessing(bool enabled)
        {
            DuneVectorAudioManager audio = _ensureAudioManager?.Invoke();
            if (audio == null)
            {
                return;
            }

            if (_settings.AppliesChromaticAberration)
            {
                audio.SetChromaticAberrationEnabled(enabled);
            }
            if (_settings.AppliesLensDistortion)
            {
                audio.SetLensDistortionEnabled(enabled);
            }
            if (_settings.AppliesCrtLines)
            {
                audio.SetCrtLinesEnabled(enabled);
            }
            if (_settings.AppliesVignette)
            {
                audio.SetVignetteEnabled(enabled);
            }
            if (_settings.AppliesLensFlare)
            {
                audio.SetLensFlareEnabled(enabled);
            }
            if (_settings.AppliesBloom)
            {
                audio.SetBloomEnabled(enabled);
            }
        }

        /// <summary>
        /// YES turns on the authored effect groups: pressure fronts, foreground streaks, and the
        /// glitch and HUD pass. The master visualizer is forced back to All alongside them,
        /// because leaving it Off or NoFlash would mask the groups the player just asked for.
        /// </summary>
        private void ApplyVisualizerEffects(bool enabled)
        {
            DuneVectorAudioManager audio = _ensureAudioManager?.Invoke();
            if (audio == null)
            {
                return;
            }

            if (enabled && audio.VisualizerMode != MusicVisualizerMode.All)
            {
                audio.SetMusicVisualizerMode(MusicVisualizerMode.All);
            }
            audio.SetMusicVisualizerEffectEnabled(_settings.VisualizerEffectsOnYes, enabled);
        }

        private void Save()
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(_savePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                FirstRunData stored = new FirstRunData
                {
                    Completed = true,
                    PostProcessingEnabled = _postProcessingAnswer,
                    VisualizerFlashEffectsEnabled = _visualizerAnswer,
                };
                System.IO.File.WriteAllText(_savePath, JsonUtility.ToJson(stored));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not write the first-run marker to '{_savePath}'. The setup questions will appear again. {exception.Message}");
            }
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
                instance.setVolume(Mathf.Clamp01(volume));
                instance.start();
                instance.release();
            }
            catch (EventNotFoundException exception)
            {
                Debug.LogWarning($"First-run setup event '{eventPath}' was not found. {exception.Message}");
            }
        }

        /// <summary>
        /// The demo clip only spins up once the second question is reached, so the first screen
        /// does not pay for decoding a clip nobody is looking at yet.
        /// </summary>
        private void CreateVideoPlayer()
        {
            if (_videoPlayer != null || _settings.DemoVideo == null || _host == null)
            {
                if (_settings.DemoVideo == null)
                {
                    Debug.LogWarning(
                        "No first-run visualizer demo video is assigned on the runtime settings asset. The second question draws over the title background instead.");
                }
                return;
            }

            int width = Mathf.Max(1, (int)_settings.DemoVideo.width);
            int height = Mathf.Max(1, (int)_settings.DemoVideo.height);
            _videoTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Dune Vector First Run Demo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _videoTarget.Create();

            _videoPlayer = _host.gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = _settings.DemoVideo;
            _videoPlayer.isLooping = _settings.LoopDemoVideo;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTarget;
            _videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.audioOutputMode = _settings.DemoVideoAudioVolume > 0f
                ? VideoAudioOutputMode.Direct
                : VideoAudioOutputMode.None;
            if (_videoPlayer.audioOutputMode == VideoAudioOutputMode.Direct)
            {
                _videoPlayer.SetDirectAudioVolume(0, _settings.DemoVideoAudioVolume);
            }
            _videoPlayer.playbackSpeed = Mathf.Max(0.01f, _settings.DemoVideoPlaybackSpeed);
            _videoPlayer.Play();
        }

        public void ReleaseVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                UnityEngine.Object.Destroy(_videoPlayer);
                _videoPlayer = null;
            }
            if (_videoTarget != null)
            {
                _videoTarget.Release();
                UnityEngine.Object.Destroy(_videoTarget);
                _videoTarget = null;
            }
        }

        public void Draw(float scale, Font font)
        {
            if (IsComplete)
            {
                return;
            }

            EnsureStyles(scale, font);
            DrawDemoVideo();

            // The chrome helpers write their colour straight into GUI.color, so a global tint
            // would be overwritten. The fade is folded into every colour by Tint instead.
            _fade = _settings.ScreenFadeInSeconds > 0f
                ? Mathf.Clamp01((Time.unscaledTime - _stepStartedAt) / _settings.ScreenFadeInSeconds)
                : 1f;

            DuneVectorHudChrome.DrawRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Tint(_step == Step.Visualizer ? _settings.VisualizerBackdropColor : _settings.BackdropColor));
            DrawPanel(scale);
        }

        private Color Tint(Color color)
        {
            color.a *= _fade;
            return color;
        }

        private void DrawDemoVideo()
        {
            if (_step != Step.Visualizer || _videoTarget == null || _videoPlayer == null || !_videoPlayer.isPrepared)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = _settings.DemoVideoTint;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                _videoTarget,
                _settings.FillScreenWithDemoVideo ? ScaleMode.ScaleAndCrop : ScaleMode.ScaleToFit,
                false);
            GUI.color = previous;
        }

        private void DrawPanel(float scale)
        {
            float padding = _settings.PanelPadding * scale;
            float width = Mathf.Min(Screen.width - (padding * 2f), _settings.PanelWidth * scale);
            float contentWidth = Mathf.Max(1f, width - (padding * 2f));
            float bodyHeight = GetBodyHeight(scale);
            float height = padding
                + (_settings.StepRowHeight * scale)
                + (_settings.StepToQuestionGap * scale)
                + (_settings.QuestionRowHeight * scale)
                + (_settings.QuestionToDetailGap * scale)
                + (_settings.DetailRowHeight * scale)
                + (_settings.DetailToBodyGap * scale)
                + bodyHeight
                + (_settings.BodyToButtonsGap * scale)
                + (_settings.ButtonHeight * scale)
                + (_settings.ButtonsToFooterGap * scale)
                + (_settings.FooterRowHeight * scale)
                + padding;

            float verticalOffset = (_step == Step.Visualizer
                ? _settings.VisualizerPanelVerticalOffset
                : _settings.PanelVerticalOffset) * scale;
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                Mathf.Clamp(((Screen.height - height) * 0.5f) + verticalOffset, 0f, Mathf.Max(0f, Screen.height - height)),
                width,
                height);

            DuneVectorHudChrome.DrawGlassPanel(
                panel,
                Tint(_settings.PanelBodyColor),
                Tint(_settings.PanelBorderColor),
                Mathf.Max(1f, _settings.PanelBorderThickness * scale),
                scale);
            DuneVectorHudChrome.DrawAccentRail(
                panel,
                Tint(_settings.PanelAccentColor),
                Mathf.Max(1f, _settings.PanelAccentWidth * scale),
                _settings.PanelAccentGlowWidth * scale);
            DuneVectorHudChrome.DrawCornerBrackets(
                panel,
                Tint(_settings.PanelAccentColor),
                _settings.PanelCornerLength * scale,
                Mathf.Max(1f, _settings.PanelBorderThickness * scale));

            string question = _step == Step.PostProcessing
                ? _settings.PostProcessingQuestion
                : _settings.VisualizerQuestion;
            string detail = _step == Step.PostProcessing
                ? _settings.PostProcessingDetail
                : _settings.VisualizerDetail;
            FitStyle(_questionStyle, question, _settings.QuestionTracking * scale, contentWidth, scale);
            FitStyle(_detailStyle, detail, 0f, contentWidth, scale);
            FitStyle(_footerStyle, _settings.FooterHint, _settings.LabelTracking * scale, contentWidth, scale);

            Rect content = new Rect(panel.x + padding, panel.y + padding, contentWidth, panel.height - (padding * 2f));
            Vector2 shadowOffset = new Vector2(_settings.TextShadowOffset * scale, _settings.TextShadowOffset * scale);
            float y = content.y;

            DuneVectorHudChrome.DrawTrackedLabel(
                new Rect(content.x, y, content.width, _settings.StepRowHeight * scale),
                string.Format(_settings.StepFormat, (int)_step + 1, StepCount),
                _stepStyle,
                _settings.LabelTracking * scale,
                Tint(_settings.StepColor),
                Color.clear,
                0f,
                Tint(_settings.TextShadowColor),
                shadowOffset);
            y += (_settings.StepRowHeight + _settings.StepToQuestionGap) * scale;

            DuneVectorHudChrome.DrawTrackedLabel(
                new Rect(content.x, y, content.width, _settings.QuestionRowHeight * scale),
                question,
                _questionStyle,
                _settings.QuestionTracking * scale,
                Tint(_settings.QuestionColor),
                Tint(_settings.QuestionGlowColor),
                _settings.QuestionGlowRadius * scale,
                Tint(_settings.TextShadowColor),
                shadowOffset);
            y += (_settings.QuestionRowHeight + _settings.QuestionToDetailGap) * scale;

            DuneVectorHudChrome.DrawLabel(
                new Rect(content.x, y, content.width, _settings.DetailRowHeight * scale),
                detail,
                _detailStyle,
                Tint(_settings.DetailColor),
                Tint(_settings.TextShadowColor),
                shadowOffset * 0.5f);
            y += (_settings.DetailRowHeight + _settings.DetailToBodyGap) * scale;

            if (_step == Step.PostProcessing)
            {
                DrawComparison(new Rect(content.x, y, content.width, bodyHeight), scale, shadowOffset);
            }
            y += bodyHeight + (_settings.BodyToButtonsGap * scale);

            DrawAnswerButtons(content, y, scale, shadowOffset);
            y += (_settings.ButtonHeight + _settings.ButtonsToFooterGap) * scale;

            DuneVectorHudChrome.DrawTrackedLabel(
                new Rect(content.x, y, content.width, _settings.FooterRowHeight * scale),
                _settings.FooterHint,
                _footerStyle,
                _settings.LabelTracking * scale,
                Tint(_settings.FooterColor),
                Color.clear,
                0f,
                Tint(_settings.TextShadowColor),
                shadowOffset * 0.5f);
        }

        /// <summary>
        /// Shrinks a single-line style until its text fits the panel. The questions are authored
        /// copy of any length and IMGUI will happily run a line straight out through the panel
        /// edge, so the fit is measured rather than assumed.
        /// </summary>
        private void FitStyle(GUIStyle style, string text, float tracking, float maximumWidth, float scale)
        {
            if (style == null || string.IsNullOrEmpty(text) || maximumWidth <= 1f)
            {
                return;
            }

            int minimum = Mathf.Max(6, Mathf.RoundToInt(_settings.MinimumFittedFontSize * scale));
            _measureContent.text = text;
            for (int guard = 0; guard < 16; guard++)
            {
                float needed = style.CalcSize(_measureContent).x + (tracking * Mathf.Max(0, text.Length - 1));
                if (needed <= maximumWidth || style.fontSize <= minimum)
                {
                    return;
                }

                int next = Mathf.FloorToInt(style.fontSize * Mathf.Max(0.5f, maximumWidth / needed));
                style.fontSize = Mathf.Max(minimum, Mathf.Min(next, style.fontSize - 1));
            }
        }

        /// <summary>
        /// The comparison images only exist on the first screen. The second screen's demo runs
        /// full screen behind the panel instead, so its body block collapses to nothing.
        /// </summary>
        private float GetBodyHeight(float scale)
        {
            if (_step != Step.PostProcessing)
            {
                return 0f;
            }

            return (_settings.PreviewHeight + _settings.CaptionRowHeight) * scale;
        }

        private void DrawComparison(Rect body, float scale, Vector2 shadowOffset)
        {
            float gap = _settings.PreviewGap * scale;
            float slotWidth = (body.width - gap) * 0.5f;
            float previewHeight = _settings.PreviewHeight * scale;

            DrawPreviewSlot(
                new Rect(body.x, body.y, slotWidth, previewHeight),
                _settings.PostProcessingOnPreview,
                _settings.PostProcessingOnCaption,
                scale,
                shadowOffset);
            DrawPreviewSlot(
                new Rect(body.x + slotWidth + gap, body.y, slotWidth, previewHeight),
                _settings.PostProcessingOffPreview,
                _settings.PostProcessingOffCaption,
                scale,
                shadowOffset);
        }

        private void DrawPreviewSlot(Rect frame, Texture2D preview, string caption, float scale, Vector2 shadowOffset)
        {
            if (preview != null)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, _fade);
                GUI.DrawTexture(frame, preview, ScaleMode.ScaleAndCrop, false);
                GUI.color = previous;
            }
            else
            {
                // A missing screenshot leaves a labelled slot rather than a hole, so the screen
                // still reads as a comparison while the art is being dropped in.
                DuneVectorHudChrome.DrawRect(frame, Tint(_settings.PreviewMissingFillColor));
                DuneVectorHudChrome.DrawLabel(
                    frame,
                    _settings.MissingPreviewLabel,
                    _captionStyle,
                    Tint(_settings.CaptionColor),
                    Tint(_settings.TextShadowColor),
                    shadowOffset * 0.5f);
            }

            DuneVectorHudChrome.DrawBorder(
                frame,
                Tint(_settings.PreviewBorderColor),
                Mathf.Max(1f, _settings.PreviewBorderThickness * scale));
            DuneVectorHudChrome.DrawTrackedLabel(
                new Rect(frame.x, frame.yMax, frame.width, _settings.CaptionRowHeight * scale),
                caption,
                _captionStyle,
                _settings.LabelTracking * scale,
                Tint(_settings.CaptionColor),
                Color.clear,
                0f,
                Tint(_settings.TextShadowColor),
                shadowOffset * 0.5f);
        }

        private void DrawAnswerButtons(Rect content, float y, float scale, Vector2 shadowOffset)
        {
            float buttonWidth = Mathf.Min(_settings.ButtonWidth * scale, (content.width - (_settings.ButtonGap * scale)) * 0.5f);
            float buttonHeight = _settings.ButtonHeight * scale;
            float gap = _settings.ButtonGap * scale;
            float pairWidth = (buttonWidth * 2f) + gap;
            float x = content.x + ((content.width - pairWidth) * 0.5f);

            _yesRect = new Rect(x, y, buttonWidth, buttonHeight);
            _noRect = new Rect(x + buttonWidth + gap, y, buttonWidth, buttonHeight);

            DrawAnswerButton(_yesRect, _settings.YesLabel, _yesSelected, scale, shadowOffset);
            DrawAnswerButton(_noRect, _settings.NoLabel, !_yesSelected, scale, shadowOffset);
        }

        private void DrawAnswerButton(Rect rect, string label, bool selected, float scale, Vector2 shadowOffset)
        {
            float pulse = 1f;
            if (selected && _settings.SelectionPulseSpeed > 0f)
            {
                float wave = (Mathf.Sin(Time.unscaledTime * _settings.SelectionPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                pulse = Mathf.Lerp(_settings.SelectionPulseMinimumAlpha, _settings.SelectionPulseMaximumAlpha, wave);
            }

            Color fill = Tint(selected ? _settings.SelectedButtonFillColor : _settings.ButtonFillColor);
            Color border = Tint(selected ? _settings.SelectedButtonBorderColor : _settings.ButtonBorderColor);
            fill.a *= pulse;
            border.a *= pulse;

            DuneVectorHudChrome.DrawRect(rect, fill);
            DuneVectorHudChrome.DrawBorder(rect, border, Mathf.Max(1f, _settings.ButtonBorderThickness * scale));
            DuneVectorHudChrome.DrawTrackedLabel(
                rect,
                label,
                _buttonStyle,
                _settings.LabelTracking * scale,
                Tint(selected ? _settings.SelectedButtonLabelColor : _settings.ButtonLabelColor),
                Color.clear,
                0f,
                Tint(_settings.TextShadowColor),
                shadowOffset * 0.5f);
        }

        private void EnsureStyles(float scale, Font font)
        {
            _stepStyle = EnsureStyle(_stepStyle, font, _settings.StepFontSize, scale, TextAnchor.MiddleCenter);
            _questionStyle = EnsureStyle(_questionStyle, font, _settings.QuestionFontSize, scale, TextAnchor.MiddleCenter);
            _detailStyle = EnsureStyle(_detailStyle, font, _settings.DetailFontSize, scale, TextAnchor.MiddleCenter);
            _captionStyle = EnsureStyle(_captionStyle, font, _settings.CaptionFontSize, scale, TextAnchor.MiddleCenter);
            _buttonStyle = EnsureStyle(_buttonStyle, font, _settings.ButtonFontSize, scale, TextAnchor.MiddleCenter);
            _footerStyle = EnsureStyle(_footerStyle, font, _settings.FooterFontSize, scale, TextAnchor.MiddleCenter);
        }

        private static GUIStyle EnsureStyle(GUIStyle style, Font font, int fontSize, float scale, TextAnchor alignment)
        {
            style ??= new GUIStyle
            {
                wordWrap = false,
                richText = false,
            };
            style.alignment = alignment;
            style.font = font;
            style.fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale));
            // The chrome helpers tint with GUI.color, which multiplies this. Leave it white or
            // the authored colours come out black.
            style.normal.textColor = Color.white;
            return style;
        }
    }
}
