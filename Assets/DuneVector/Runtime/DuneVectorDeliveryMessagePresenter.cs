using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    internal sealed class DeliveryMessageInputReader
    {
        public bool WasAdvancePressedThisFrame()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
        }
    }

    internal sealed class DeliveryMessageTypingAudio : IDisposable
    {
        private EventInstance _instance;
        private bool _repeatRequested;
        private bool _restartFailureLogged;
        private EventReference _eventReference;
        private UnityEngine.Object _context;

        public void Start(EventReference eventReference, UnityEngine.Object context)
        {
            if (eventReference.IsNull)
            {
                Stop();
                return;
            }

            try
            {
                _eventReference = eventReference;
                _context = context;
                _restartFailureLogged = false;
                _repeatRequested = true;

                if (_instance.isValid())
                {
                    FMOD.RESULT stateResult = _instance.getPlaybackState(out PLAYBACK_STATE playbackState);
                    if (stateResult == FMOD.RESULT.OK && playbackState != PLAYBACK_STATE.STOPPED)
                    {
                        return;
                    }

                    if (stateResult == FMOD.RESULT.OK)
                    {
                        FMOD.RESULT resumeResult = _instance.start();
                        if (resumeResult == FMOD.RESULT.OK)
                        {
                            return;
                        }
                    }

                    ReleaseWithoutStopping();
                    _eventReference = eventReference;
                    _context = context;
                    _repeatRequested = true;
                }

                _instance = RuntimeManager.CreateInstance(eventReference);
                FMOD.RESULT startResult = _instance.start();
                if (startResult != FMOD.RESULT.OK)
                {
                    Debug.LogWarning(
                        $"FMOD delivery typing loop '{eventReference}' could not start. {startResult}",
                        context);
                    _instance.release();
                    _instance.clearHandle();
                    return;
                }

            }
            catch (Exception exception)
            {
                Debug.LogWarning($"FMOD delivery typing loop '{eventReference}' could not start. {exception.Message}", context);
                _instance.clearHandle();
            }
        }

        public void Tick()
        {
            if (!_instance.isValid())
            {
                return;
            }

            FMOD.RESULT stateResult = _instance.getPlaybackState(out PLAYBACK_STATE playbackState);
            if (stateResult != FMOD.RESULT.OK || playbackState != PLAYBACK_STATE.STOPPED)
            {
                return;
            }

            if (!_repeatRequested)
            {
                ReleaseWithoutStopping();
                return;
            }

            if (_restartFailureLogged)
            {
                return;
            }

            FMOD.RESULT restartResult = _instance.start();
            if (restartResult == FMOD.RESULT.OK)
            {
                return;
            }

            _restartFailureLogged = true;
            Debug.LogWarning(
                $"FMOD delivery typing loop '{_eventReference}' could not restart. {restartResult}",
                _context);
        }

        public void Stop()
        {
            _repeatRequested = false;
        }

        private void ReleaseWithoutStopping()
        {
            if (_instance.isValid())
            {
                _instance.release();
                _instance.clearHandle();
            }

            ClearState();
        }

        private void ClearState()
        {
            _repeatRequested = false;
            _eventReference = default;
            _context = null;
            _restartFailureLogged = false;
        }

        public void Dispose()
        {
            Stop();
            ReleaseWithoutStopping();
        }
    }

    internal sealed class DeliveryMessageVoiceAudio : IDisposable
    {
        private EventInstance _instance;

        public void Play(EventReference eventReference, UnityEngine.Object context)
        {
            Stop();
            if (eventReference.IsNull)
            {
                return;
            }

            try
            {
                _instance = RuntimeManager.CreateInstance(eventReference);
                FMOD.RESULT startResult = _instance.start();
                if (startResult != FMOD.RESULT.OK)
                {
                    Debug.LogWarning(
                        $"FMOD delivery voice event '{eventReference}' could not start. {startResult}",
                        context);
                    Release();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"FMOD delivery voice event '{eventReference}' could not start. {exception.Message}",
                    context);
                Release();
            }
        }

        public void Stop()
        {
            if (!_instance.isValid())
            {
                return;
            }

            _instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            Release();
        }

        private void Release()
        {
            if (_instance.isValid())
            {
                _instance.release();
                _instance.clearHandle();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorDeliveryMessagePresenter : MonoBehaviour
    {
        private enum PagePresentationPhase
        {
            OpeningDelay,
            Presenting,
            FadingOut,
            EmptyBeat,
        }

        private readonly struct WrappedTextLine
        {
            public readonly int SourceStart;
            public readonly int SourceEnd;

            public WrappedTextLine(int sourceStart, int sourceEnd)
            {
                SourceStart = sourceStart;
                SourceEnd = sourceEnd;
            }
        }

        public bool IsOpen { get; private set; }
        public Font PresentationFont => _settings != null && _settings.NarrativeFont != null
            ? _settings.NarrativeFont
            : _runtimeFont;
        public bool IsTyping => IsOpen &&
            _phase == PagePresentationPhase.Presenting &&
            _visibleCharacterCount < CurrentPage.Length;
        public int CurrentPageIndex => _pageIndex;
        public string VisibleText => _phase == PagePresentationPhase.EmptyBeat
            ? string.Empty
            : CurrentPage.Substring(0, Mathf.Clamp(_visibleCharacterCount, 0, CurrentPage.Length));

        private readonly DeliveryMessageInputReader _input = new DeliveryMessageInputReader();
        private readonly DeliveryMessageTypingAudio _typingAudio = new DeliveryMessageTypingAudio();
        private readonly DeliveryMessageVoiceAudio _voiceAudio = new DeliveryMessageVoiceAudio();
        private IReadOnlyList<string> _pages = Array.Empty<string>();
        private DeliveryMessageAsset _message;
        private DeliveryMessageTuning _settings;
        private DuneVectorAudioManager _audio;
        private float _timeScaleBeforeOpen = 1f;
        private bool _gameplayPauseActive;
        private Action _completed;
        private int _pageIndex;
        private int _visibleCharacterCount;
        private float _characterAccumulator;
        private float _pageFinishedAt;
        private float _pageStartedAt;
        private float _phaseStartedAt;
        private float _hintOpenedAt;
        private float _hintDismissedAt;
        private float _newestCharacterRevealedAt;
        private int _openedFrame;
        private int _emissiveVisibleCharacterCount;
        private bool _completionSent;
        private bool _hasAcknowledgedInputHint;
        private bool _showFirstUseHint;
        private bool _allowCancel;
        private PagePresentationPhase _phase;
        private Action _firstInteraction;
        private Font _runtimeFont;
        private GUIStyle _narrativeStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _indicatorStyle;
        private GUIStyle _hintStyle;

        private string CurrentPage => _pageIndex >= 0 && _pageIndex < _pages.Count
            ? _pages[_pageIndex] ?? string.Empty
            : string.Empty;

        public void Initialize(
            DeliveryMessageTuning settings,
            DuneVectorAudioManager audio,
            bool hasAcknowledgedInputHint,
            Action firstInteraction)
        {
            _settings = settings ?? new DeliveryMessageTuning();
            _settings.EnsureInitialized();
            _audio = audio;
            _hasAcknowledgedInputHint = hasAcknowledgedInputHint;
            _firstInteraction = firstInteraction;
            CreateRuntimeFont();
        }

        public bool Open(DeliveryMessageAsset message, Action completed)
        {
            return OpenInternal(
                message,
                completed,
                allowCancel: false,
                showFirstUseHint: true,
                delayFirstPage: true);
        }

        public bool OpenReplay(DeliveryMessageAsset message, Action closed)
        {
            return OpenInternal(
                message,
                closed,
                allowCancel: true,
                showFirstUseHint: false,
                delayFirstPage: false);
        }

        private bool OpenInternal(
            DeliveryMessageAsset message,
            Action completed,
            bool allowCancel,
            bool showFirstUseHint,
            bool delayFirstPage)
        {
            if (IsOpen || message == null)
            {
                return false;
            }

            _pages = message.BuildPages();
            _message = message;
            _completed = completed;
            _pageIndex = 0;
            _visibleCharacterCount = 0;
            _characterAccumulator = 0f;
            _pageFinishedAt = float.PositiveInfinity;
            _pageStartedAt = Time.unscaledTime;
            _phaseStartedAt = Time.unscaledTime;
            _hintOpenedAt = Time.unscaledTime;
            _hintDismissedAt = float.PositiveInfinity;
            _newestCharacterRevealedAt = float.NegativeInfinity;
            _emissiveVisibleCharacterCount = -1;
            _openedFrame = Time.frameCount;
            _completionSent = false;
            _phase = delayFirstPage
                ? PagePresentationPhase.OpeningDelay
                : PagePresentationPhase.Presenting;
            _allowCancel = allowCancel;
            _showFirstUseHint = showFirstUseHint && !_hasAcknowledgedInputHint;
            IsOpen = true;
            if (!allowCancel)
            {
                _audio?.SetMusicDuckMultiplier(_settings.PostContractMusicVolumeMultiplier);
            }
            BeginGameplayPause();
            if (!delayFirstPage)
            {
                BeginCurrentPage();
            }
            return true;
        }

        public void Close(bool invokeCompletion)
        {
            if (!IsOpen && _completed == null)
            {
                return;
            }

            _typingAudio.Stop();
            _voiceAudio.Stop();
            IsOpen = false;
            if (!_allowCancel)
            {
                _audio?.SetMusicDuckMultiplier(1f);
            }
            EndGameplayPause();
            Action callback = _completed;
            _completed = null;
            _pages = Array.Empty<string>();
            _message = null;
            _allowCancel = false;
            _phase = PagePresentationPhase.Presenting;
            if (invokeCompletion && !_completionSent)
            {
                _completionSent = true;
                callback?.Invoke();
            }
        }

        private void BeginGameplayPause()
        {
            if (_gameplayPauseActive)
            {
                return;
            }

            _timeScaleBeforeOpen = Time.timeScale;
            _gameplayPauseActive = true;
            Time.timeScale = 0f;
        }

        private void EndGameplayPause()
        {
            if (!_gameplayPauseActive)
            {
                return;
            }

            _gameplayPauseActive = false;
            if (Mathf.Approximately(Time.timeScale, 0f))
            {
                Time.timeScale = _timeScaleBeforeOpen;
            }
        }

        private void Update()
        {
            _typingAudio.Tick();
            if (!IsOpen)
            {
                return;
            }

            if (_allowCancel && Time.frameCount != _openedFrame &&
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close(invokeCompletion: true);
                return;
            }

            UpdatePageTransition();
            if (_phase != PagePresentationPhase.Presenting)
            {
                return;
            }

            bool advancePressed = Time.frameCount != _openedFrame && _input.WasAdvancePressedThisFrame();
            if (advancePressed)
            {
                AcknowledgeFirstInteraction();
            }

            if (IsTyping)
            {
                if (advancePressed)
                {
                    RevealCurrentPage();
                    return;
                }

                _characterAccumulator += Time.unscaledDeltaTime * Mathf.Max(0.01f, _settings.CharactersPerSecond);
                int charactersToReveal = Mathf.FloorToInt(_characterAccumulator);
                if (charactersToReveal > 0)
                {
                    _characterAccumulator -= charactersToReveal;
                    int previousVisibleCharacterCount = _visibleCharacterCount;
                    _visibleCharacterCount = Mathf.Min(CurrentPage.Length, _visibleCharacterCount + charactersToReveal);
                    if (_visibleCharacterCount > previousVisibleCharacterCount)
                    {
                        _emissiveVisibleCharacterCount = _visibleCharacterCount;
                        _newestCharacterRevealedAt = Time.unscaledTime;
                    }
                    if (!IsTyping)
                    {
                        FinishCurrentPage();
                    }
                }
                return;
            }

            if (!advancePressed || Time.unscaledTime < _pageFinishedAt + Mathf.Max(0f, _settings.PageAdvanceInputDelay))
            {
                return;
            }

            if (_pageIndex + 1 < _pages.Count)
            {
                _voiceAudio.Stop();
                _phase = PagePresentationPhase.FadingOut;
                _phaseStartedAt = Time.unscaledTime;
                return;
            }

            Close(invokeCompletion: true);
        }

        private void BeginCurrentPage()
        {
            _phase = PagePresentationPhase.Presenting;
            _pageFinishedAt = float.PositiveInfinity;
            _pageStartedAt = Time.unscaledTime;
            _newestCharacterRevealedAt = float.NegativeInfinity;
            _emissiveVisibleCharacterCount = -1;
            if (CurrentPage.Length == 0)
            {
                FinishCurrentPage();
                return;
            }
            _typingAudio.Start(_settings.TypingLoopEvent, this);
            if (_settings.TryResolveVoiceEvent(_message, _pageIndex, out EventReference voiceEvent))
            {
                _voiceAudio.Play(voiceEvent, this);
            }
        }

        private void RevealCurrentPage()
        {
            _visibleCharacterCount = CurrentPage.Length;
            _newestCharacterRevealedAt = float.NegativeInfinity;
            _emissiveVisibleCharacterCount = -1;
            FinishCurrentPage();
        }

        private void FinishCurrentPage()
        {
            _typingAudio.Stop();
            _pageFinishedAt = Time.unscaledTime;
        }

        private void UpdatePageTransition()
        {
            if (_phase == PagePresentationPhase.OpeningDelay &&
                Time.unscaledTime >= _phaseStartedAt + Mathf.Max(0f, _settings.FirstPageTypingDelay))
            {
                BeginCurrentPage();
            }

            if (_phase == PagePresentationPhase.FadingOut &&
                Time.unscaledTime >= _phaseStartedAt + Mathf.Max(0f, _settings.PageFadeOutDuration))
            {
                _phase = PagePresentationPhase.EmptyBeat;
                _phaseStartedAt = Time.unscaledTime;
                _visibleCharacterCount = 0;
                _emissiveVisibleCharacterCount = -1;
            }

            if (_phase == PagePresentationPhase.EmptyBeat &&
                Time.unscaledTime >= _phaseStartedAt + Mathf.Max(0f, _settings.EmptyPageBeatDuration))
            {
                _pageIndex++;
                _visibleCharacterCount = 0;
                _characterAccumulator = 0f;
                BeginCurrentPage();
            }
        }

        private void AcknowledgeFirstInteraction()
        {
            if (_hasAcknowledgedInputHint)
            {
                return;
            }

            _hasAcknowledgedInputHint = true;
            _hintDismissedAt = Time.unscaledTime;
            _firstInteraction?.Invoke();
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -1200;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;

            float minimumScale = Mathf.Min(_settings.MinimumScale, _settings.MaximumScale);
            float maximumScale = Mathf.Max(_settings.MinimumScale, _settings.MaximumScale);
            float scale = Mathf.Clamp(
                Mathf.Min(
                    Screen.width / Mathf.Max(1f, _settings.ReferenceWidth),
                    Screen.height / Mathf.Max(1f, _settings.ReferenceHeight)),
                minimumScale,
                maximumScale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float virtualWidth = Screen.width / scale;
            float virtualHeight = Screen.height / scale;

            DrawSolidRect(new Rect(0f, 0f, virtualWidth, virtualHeight), _settings.BackdropColor);
            DrawTransmissionArtifacts(virtualWidth, virtualHeight);

            float readingWidth = Mathf.Min(
                _settings.ReadingAreaWidth,
                virtualWidth - (_settings.ScreenMargin * 2f));
            float readingHeight = Mathf.Min(
                _settings.ReadingAreaHeight,
                virtualHeight - (_settings.ScreenMargin * 2f));
            Rect readingArea = new Rect(
                (virtualWidth - readingWidth) * 0.5f,
                ((virtualHeight - readingHeight) * 0.5f) + _settings.ReadingAreaVerticalOffset,
                readingWidth,
                readingHeight);

            float flicker = 1f - (_settings.TransmissionFlickerAmount *
                Mathf.PerlinNoise(Time.unscaledTime * _settings.TransmissionFlickerSpeed, 0.413f));
            DrawSolidRect(readingArea, WithAlpha(_settings.ReadingAreaColor, _settings.ReadingAreaColor.a * flicker));
            DrawReadingFrame(readingArea, flicker);

            Rect textRect = new Rect(
                readingArea.x + _settings.HorizontalPadding,
                readingArea.y + _settings.TextTopPadding,
                readingArea.width - (_settings.HorizontalPadding * 2f),
                Mathf.Max(1f, readingArea.height - _settings.TextTopPadding - _settings.TextBottomPadding));
            float textAlpha = CurrentTextAlpha();
            Color textColor = CurrentNarrativeColor(flicker, textAlpha);
            DrawNarrativeText(textRect, CurrentPage, VisibleText, textColor);

            if (_phase == PagePresentationPhase.Presenting && !IsTyping)
            {
                DrawContinueIndicator(readingArea, _pageIndex + 1 >= _pages.Count, flicker);
            }

            DrawFirstUseHint(virtualWidth, virtualHeight);
            DrawArchiveReplayHint(virtualWidth, virtualHeight);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawArchiveReplayHint(float virtualWidth, float virtualHeight)
        {
            if (!_allowCancel)
            {
                return;
            }
            _hintStyle.normal.textColor = _settings.SecondaryTextColor;
            GUI.Label(
                new Rect(
                    0f,
                    virtualHeight - _settings.FirstUseHintBottomMargin - _settings.FirstUseHintHeight,
                    virtualWidth,
                    _settings.FirstUseHintHeight),
                _settings.ArchiveReplayHint ?? string.Empty,
                _hintStyle);
        }

        public void DrawArchiveChrome(float virtualWidth, float virtualHeight, Rect archiveArea)
        {
            EnsureStyles();
            DrawSolidRect(new Rect(0f, 0f, virtualWidth, virtualHeight), _settings.BackdropColor);
            DrawTransmissionArtifacts(virtualWidth, virtualHeight);
            float flicker = 1f - (_settings.TransmissionFlickerAmount *
                Mathf.PerlinNoise(Time.unscaledTime * _settings.TransmissionFlickerSpeed, 0.413f));
            DrawSolidRect(
                archiveArea,
                WithAlpha(_settings.ReadingAreaColor, _settings.ReadingAreaColor.a * flicker));
            DrawTransmissionFrame(archiveArea, flicker, _settings.ArchiveHeader, showTypingSignal: false);
        }

        private void CreateRuntimeFont()
        {
            if (_settings.NarrativeFont != null)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.PreferredFontName))
            {
                return;
            }

            try
            {
                _runtimeFont = Font.CreateDynamicFontFromOSFont(
                    _settings.PreferredFontName,
                    Mathf.Max(10, _settings.NarrativeFontSize));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Delivery message font '{_settings.PreferredFontName}' is unavailable. {exception.Message}",
                    this);
            }
        }

        private void EnsureStyles()
        {
            if (_narrativeStyle != null)
            {
                return;
            }

            Font font = _settings.NarrativeFont != null
                ? _settings.NarrativeFont
                : _runtimeFont != null
                    ? _runtimeFont
                    : GUI.skin.font;
            _narrativeStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = _settings.NarrativeFontSize,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                // Lines are wrapped manually to the authored reading width. Overflow
                // prevents font-specific ascenders, descenders, and glow from being
                // cropped by IMGUI's inaccurate dynamic-font line metrics.
                clipping = TextClipping.Overflow,
            };
            _headerStyle = new GUIStyle(_narrativeStyle)
            {
                fontSize = _settings.HeaderFontSize,
                alignment = TextAnchor.LowerLeft,
            };
            _indicatorStyle = new GUIStyle(_narrativeStyle)
            {
                fontSize = _settings.IndicatorFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
            _hintStyle = new GUIStyle(_narrativeStyle)
            {
                fontSize = _settings.HintFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        private void DrawReadingFrame(Rect readingArea, float flicker)
        {
            DrawTransmissionFrame(readingArea, flicker, _settings.TransmissionHeader, IsTyping);
        }

        private void DrawTransmissionFrame(
            Rect readingArea,
            float flicker,
            string header,
            bool showTypingSignal)
        {
            float thickness = Mathf.Max(0.5f, _settings.RuleThickness);
            float ruleY = readingArea.y + _settings.RuleOffset;
            Color border = WithAlpha(_settings.BorderColor, _settings.BorderColor.a * flicker);
            DrawSolidRect(new Rect(readingArea.x, ruleY, readingArea.width, thickness), border);
            DrawSolidRect(
                new Rect(readingArea.x, readingArea.yMax - thickness, readingArea.width, thickness),
                WithAlpha(border, border.a * _settings.BottomRuleOpacity));

            float detail = Mathf.Min(_settings.CornerDetailLength, readingArea.width * 0.1f);
            DrawSolidRect(new Rect(readingArea.x, ruleY, thickness, detail), border);
            DrawSolidRect(new Rect(readingArea.xMax - thickness, ruleY, thickness, detail), border);
            DrawSolidRect(
                new Rect(readingArea.x, readingArea.yMax - detail, thickness, detail),
                WithAlpha(border, border.a * _settings.BottomRuleOpacity));
            DrawSolidRect(
                new Rect(readingArea.xMax - thickness, readingArea.yMax - detail, thickness, detail),
                WithAlpha(border, border.a * _settings.BottomRuleOpacity));

            _headerStyle.normal.textColor = WithAlpha(
                _settings.SecondaryTextColor,
                _settings.SecondaryTextColor.a * flicker);
            GUI.Label(
                new Rect(
                    readingArea.x + _settings.HorizontalPadding,
                    readingArea.y,
                    readingArea.width - (_settings.HorizontalPadding * 2f),
                    Mathf.Max(1f, _settings.RuleOffset - _settings.HeaderRuleGap)),
                header ?? string.Empty,
                _headerStyle);

            if (showTypingSignal)
            {
                DrawTypingSignal(readingArea, ruleY, flicker);
            }
        }

        private void DrawTypingSignal(Rect readingArea, float ruleY, float flicker)
        {
            int segments = Mathf.Max(4, _settings.TypingSignalSegments);
            float width = Mathf.Max(0f, _settings.TypingSignalWidth);
            float startX = readingArea.xMax - _settings.HorizontalPadding - width;
            float segmentWidth = width / segments;
            Color color = WithAlpha(_settings.SignalColor, _settings.SignalColor.a * flicker);
            for (int index = 0; index < segments; index++)
            {
                float wave = Mathf.Sin(
                    (index * 1.73f) + (Time.unscaledTime * _settings.TypingSignalSpeed));
                float height = Mathf.Max(0.5f, Mathf.Abs(wave) * _settings.TypingSignalHeight);
                DrawSolidRect(
                    new Rect(
                        startX + (index * segmentWidth),
                        ruleY - (height * 0.5f),
                        _settings.TypingSignalSegmentThickness,
                        height),
                    color);
            }
        }

        private void DrawNarrativeText(
            Rect area,
            string fullPageText,
            string visibleText,
            Color textColor)
        {
            string pageText = fullPageText ?? string.Empty;
            List<WrappedTextLine> layout = BuildWrappedLayout(pageText, area.width, _narrativeStyle);
            List<string> lines = BuildVisibleLines(pageText, visibleText?.Length ?? 0, layout);
            float lineAdvance = Mathf.Max(1f, _narrativeStyle.lineHeight + _settings.NarrativeLineSpacing);
            float glowOffset = _settings.TextGlowOffset;
            if (_settings.TextGlowOpacity > 0f && glowOffset > 0f)
            {
                _narrativeStyle.normal.textColor = WithAlpha(
                    _settings.SignalColor,
                    textColor.a * _settings.TextGlowOpacity);
                DrawTextLines(area, lines, lineAdvance, glowOffset, glowOffset);
            }

            _narrativeStyle.normal.textColor = textColor;
            DrawTextLines(area, lines, lineAdvance, 0f, 0f);
            DrawNewestCharacterEmission(
                area,
                pageText,
                visibleText ?? string.Empty,
                layout,
                lineAdvance,
                textColor);
        }

        private void DrawNewestCharacterEmission(
            Rect area,
            string fullPageText,
            string visibleText,
            IReadOnlyList<WrappedTextLine> layout,
            float lineAdvance,
            Color textColor)
        {
            if (string.IsNullOrEmpty(visibleText) ||
                visibleText.Length != _emissiveVisibleCharacterCount ||
                char.IsWhiteSpace(visibleText[visibleText.Length - 1]) ||
                layout.Count == 0)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, _settings.NewestCharacterFlashDuration);
            float age01 = Mathf.Clamp01((Time.unscaledTime - _newestCharacterRevealedAt) / duration);
            if (age01 >= 1f)
            {
                return;
            }

            int sourceIndex = visibleText.Length - 1;
            int lineIndex = FindSourceLine(layout, sourceIndex);
            if (lineIndex < 0)
            {
                return;
            }

            WrappedTextLine sourceLine = layout[lineIndex];
            string glyph = fullPageText.Substring(sourceIndex, 1);
            string prefix = fullPageText.Substring(
                sourceLine.SourceStart,
                sourceIndex - sourceLine.SourceStart);
            float glyphX = area.x + _narrativeStyle.CalcSize(new GUIContent(prefix)).x;
            float glyphY = area.y + (lineIndex * lineAdvance);
            float flash = (1f - age01) * Mathf.Max(0f, _settings.NewestCharacterFlashIntensity);
            float glowAlpha = textColor.a * _settings.NewestCharacterGlowOpacity * flash;
            float radius = Mathf.Max(0f, _settings.NewestCharacterGlowRadius);
            int samples = Mathf.Max(4, _settings.NewestCharacterGlowSamples);

            _narrativeStyle.normal.textColor = WithAlpha(
                _settings.NewestCharacterEmissionColor,
                glowAlpha);
            for (int sample = 0; sample < samples; sample++)
            {
                float angle = (Mathf.PI * 2f * sample) / samples;
                DrawEmissionGlyph(
                    glyph,
                    glyphX + (Mathf.Cos(angle) * radius),
                    glyphY + (Mathf.Sin(angle) * radius));
            }

            Color coreColor = Color.LerpUnclamped(
                textColor,
                _settings.NewestCharacterEmissionColor,
                flash);
            coreColor.a = textColor.a;
            _narrativeStyle.normal.textColor = coreColor;
            DrawEmissionGlyph(glyph, glyphX, glyphY);
        }

        private void DrawEmissionGlyph(string glyph, float x, float y)
        {
            GUI.Label(
                new Rect(
                    x,
                    y,
                    _narrativeStyle.CalcSize(new GUIContent(glyph)).x + (_settings.NewestCharacterGlowRadius * 2f),
                    _narrativeStyle.lineHeight + _settings.NarrativeLineClipPadding),
                glyph,
                _narrativeStyle);
        }

        private void DrawTextLines(
            Rect area,
            IReadOnlyList<string> lines,
            float lineAdvance,
            float offsetX,
            float offsetY)
        {
            float y = area.y + offsetY;
            for (int index = 0; index < lines.Count && y < area.yMax; index++)
            {
                GUI.Label(
                    new Rect(
                        area.x + offsetX,
                        y,
                        area.width,
                        _narrativeStyle.lineHeight + _settings.NarrativeLineClipPadding),
                    lines[index],
                    _narrativeStyle);
                y += lineAdvance;
            }
        }

        private static List<WrappedTextLine> BuildWrappedLayout(
            string text,
            float maximumWidth,
            GUIStyle style)
        {
            List<WrappedTextLine> result = new List<WrappedTextLine>();
            int paragraphStart = 0;
            for (int index = 0; index <= text.Length; index++)
            {
                if (index < text.Length && text[index] != '\n')
                {
                    continue;
                }

                AddWrappedParagraph(result, text, paragraphStart, index, maximumWidth, style);
                paragraphStart = index + 1;
            }
            return result;
        }

        private static void AddWrappedParagraph(
            List<WrappedTextLine> result,
            string text,
            int paragraphStart,
            int paragraphEnd,
            float maximumWidth,
            GUIStyle style)
        {
            if (paragraphStart >= paragraphEnd)
            {
                result.Add(new WrappedTextLine(paragraphStart, paragraphStart));
                return;
            }

            int cursor = paragraphStart;
            int lineStart = paragraphStart;
            bool lineHasWord = false;
            while (cursor < paragraphEnd)
            {
                while (cursor < paragraphEnd && char.IsWhiteSpace(text[cursor]))
                {
                    cursor++;
                }

                if (cursor >= paragraphEnd)
                {
                    break;
                }

                int wordStart = cursor;
                while (cursor < paragraphEnd && !char.IsWhiteSpace(text[cursor]))
                {
                    cursor++;
                }
                int wordEnd = cursor;

                if (!lineHasWord)
                {
                    lineStart = wordStart;
                    lineHasWord = true;
                    continue;
                }

                string candidate = text.Substring(lineStart, wordEnd - lineStart);
                if (style.CalcSize(new GUIContent(candidate)).x <= maximumWidth)
                {
                    continue;
                }

                int lineEnd = wordStart;
                while (lineEnd > lineStart && char.IsWhiteSpace(text[lineEnd - 1]))
                {
                    lineEnd--;
                }
                result.Add(new WrappedTextLine(lineStart, lineEnd));
                lineStart = wordStart;
            }

            if (!lineHasWord)
            {
                result.Add(new WrappedTextLine(paragraphStart, paragraphStart));
                return;
            }

            int finalLineEnd = paragraphEnd;
            while (finalLineEnd > lineStart && char.IsWhiteSpace(text[finalLineEnd - 1]))
            {
                finalLineEnd--;
            }
            result.Add(new WrappedTextLine(lineStart, finalLineEnd));
        }

        private static List<string> BuildVisibleLines(
            string fullText,
            int visibleCharacterCount,
            IReadOnlyList<WrappedTextLine> layout)
        {
            List<string> result = new List<string>(layout.Count);
            int visibleEnd = Mathf.Clamp(visibleCharacterCount, 0, fullText.Length);
            for (int index = 0; index < layout.Count; index++)
            {
                WrappedTextLine line = layout[index];
                int lineVisibleEnd = Mathf.Min(visibleEnd, line.SourceEnd);
                result.Add(lineVisibleEnd <= line.SourceStart
                    ? string.Empty
                    : fullText.Substring(line.SourceStart, lineVisibleEnd - line.SourceStart));
            }
            return result;
        }

        private static int FindSourceLine(IReadOnlyList<WrappedTextLine> layout, int sourceIndex)
        {
            for (int index = 0; index < layout.Count; index++)
            {
                WrappedTextLine line = layout[index];
                if (sourceIndex >= line.SourceStart && sourceIndex < line.SourceEnd)
                {
                    return index;
                }
            }
            return -1;
        }

        private void DrawContinueIndicator(Rect readingArea, bool finalPage, float flicker)
        {
            float pulse01 = (Mathf.Sin(Time.unscaledTime * _settings.IndicatorPulseSpeed) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_settings.IndicatorMinimumAlpha, 1f, pulse01) * flicker;
            _indicatorStyle.fontSize = finalPage
                ? _settings.IndicatorFontSize
                : Mathf.Max(1, Mathf.RoundToInt(_settings.IndicatorFontSize * _settings.ContinueIndicatorScale));
            _indicatorStyle.normal.textColor = WithAlpha(_settings.SecondaryTextColor, alpha);
            GUI.Label(
                new Rect(
                    readingArea.center.x - (_settings.IndicatorWidth * 0.5f),
                    readingArea.yMax - _settings.TextBottomPadding,
                    _settings.IndicatorWidth,
                    _settings.TextBottomPadding),
                finalPage ? _settings.FinalIndicator : _settings.ContinueIndicator,
                _indicatorStyle);
        }

        private void DrawFirstUseHint(float virtualWidth, float virtualHeight)
        {
            if (!_showFirstUseHint || string.IsNullOrEmpty(_settings.FirstUseInputHint))
            {
                return;
            }

            float elapsed = Time.unscaledTime - _hintOpenedAt;
            float alpha = elapsed <= _settings.FirstUseHintHoldDuration
                ? 1f
                : 1f - Mathf.Clamp01(
                    (elapsed - _settings.FirstUseHintHoldDuration) /
                    Mathf.Max(0.01f, _settings.FirstUseHintFadeDuration));
            if (!float.IsPositiveInfinity(_hintDismissedAt))
            {
                alpha = Mathf.Min(
                    alpha,
                    1f - Mathf.Clamp01(
                        (Time.unscaledTime - _hintDismissedAt) /
                        Mathf.Max(0.01f, _settings.FirstUseHintFadeDuration)));
            }
            if (alpha <= 0f)
            {
                return;
            }

            _hintStyle.normal.textColor = WithAlpha(
                _settings.SecondaryTextColor,
                _settings.SecondaryTextColor.a * alpha);
            GUI.Label(
                new Rect(
                    0f,
                    virtualHeight - _settings.FirstUseHintBottomMargin - _settings.FirstUseHintHeight,
                    virtualWidth,
                    _settings.FirstUseHintHeight),
                _settings.FirstUseInputHint,
                _hintStyle);
        }

        private void DrawTransmissionArtifacts(float width, float height)
        {
            float scanY = Mathf.Repeat(Time.unscaledTime * _settings.ScanLineSpeed, Mathf.Max(1f, height));
            float scanThickness = Mathf.Max(0.5f, _settings.ScanLineThickness);
            DrawSolidRect(
                new Rect(0f, scanY - _settings.ChromaticSeparation, width, scanThickness),
                _settings.ChromaticWarmColor);
            DrawSolidRect(
                new Rect(0f, scanY + _settings.ChromaticSeparation, width, scanThickness),
                _settings.ChromaticCoolColor);

            int grainFrame = Mathf.FloorToInt(Time.unscaledTime * _settings.GrainRefreshRate);
            int count = Mathf.Max(0, _settings.GrainPointCount);
            float pointSize = Mathf.Max(0.5f, _settings.GrainPointSize);
            for (int index = 0; index < count; index++)
            {
                float x = PseudoRandom01((grainFrame * 92821) + (index * 37)) * width;
                float y = PseudoRandom01((grainFrame * 68917) + (index * 71) + 17) * height;
                DrawSolidRect(new Rect(x, y, pointSize, pointSize), _settings.GrainColor);
            }
        }

        private float CurrentTextAlpha()
        {
            if (_phase == PagePresentationPhase.EmptyBeat)
            {
                return 0f;
            }
            if (_phase != PagePresentationPhase.FadingOut)
            {
                return 1f;
            }
            return 1f - Mathf.Clamp01(
                (Time.unscaledTime - _phaseStartedAt) /
                Mathf.Max(0.01f, _settings.PageFadeOutDuration));
        }

        private Color CurrentNarrativeColor(float flicker, float alpha)
        {
            float pulseDuration = Mathf.Max(0.01f, _settings.PageStartBrightnessDuration);
            float pageStart01 = Mathf.Clamp01((Time.unscaledTime - _pageStartedAt) / pulseDuration);
            Color color = Color.Lerp(_settings.PageStartTextColor, _settings.NarrativeTextColor, pageStart01);
            color.a *= alpha * flicker;
            return color;
        }

        private static float PseudoRandom01(int seed)
        {
            return Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDisable()
        {
            Close(invokeCompletion: false);
        }

        private void OnDestroy()
        {
            _typingAudio.Dispose();
            _voiceAudio.Dispose();
            Close(invokeCompletion: false);
            if (_runtimeFont != null)
            {
                Destroy(_runtimeFont);
            }
        }
    }
}
