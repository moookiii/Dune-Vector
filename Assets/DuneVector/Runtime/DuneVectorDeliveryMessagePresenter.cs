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
        private bool _playing;

        public void Start(EventReference eventReference, UnityEngine.Object context)
        {
            Stop();
            if (eventReference.IsNull)
            {
                return;
            }

            try
            {
                _instance = RuntimeManager.CreateInstance(eventReference);
                _instance.start();
                _playing = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"FMOD delivery typing loop '{eventReference}' could not start. {exception.Message}", context);
                _instance.clearHandle();
            }
        }

        public void Stop()
        {
            if (!_instance.isValid())
            {
                _playing = false;
                return;
            }

            if (_playing)
            {
                _instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            }
            _instance.release();
            _instance.clearHandle();
            _playing = false;
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
            Presenting,
            FadingOut,
            EmptyBeat,
        }

        public bool IsOpen { get; private set; }
        public bool IsTyping => IsOpen &&
            _phase == PagePresentationPhase.Presenting &&
            _visibleCharacterCount < CurrentPage.Length;
        public int CurrentPageIndex => _pageIndex;
        public string VisibleText => _phase == PagePresentationPhase.EmptyBeat
            ? string.Empty
            : CurrentPage.Substring(0, Mathf.Clamp(_visibleCharacterCount, 0, CurrentPage.Length));

        private readonly DeliveryMessageInputReader _input = new DeliveryMessageInputReader();
        private readonly DeliveryMessageTypingAudio _typingAudio = new DeliveryMessageTypingAudio();
        private IReadOnlyList<string> _pages = Array.Empty<string>();
        private DeliveryMessageTuning _settings;
        private Action _completed;
        private int _pageIndex;
        private int _visibleCharacterCount;
        private float _characterAccumulator;
        private float _pageFinishedAt;
        private float _pageStartedAt;
        private float _phaseStartedAt;
        private float _hintOpenedAt;
        private float _hintDismissedAt;
        private int _openedFrame;
        private bool _completionSent;
        private bool _hasAcknowledgedInputHint;
        private bool _showFirstUseHint;
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
            bool hasAcknowledgedInputHint,
            Action firstInteraction)
        {
            _settings = settings ?? new DeliveryMessageTuning();
            _settings.EnsureInitialized();
            _hasAcknowledgedInputHint = hasAcknowledgedInputHint;
            _firstInteraction = firstInteraction;
            CreateRuntimeFont();
        }

        public bool Open(DeliveryMessageAsset message, Action completed)
        {
            if (IsOpen || message == null)
            {
                return false;
            }

            _pages = message.BuildPages();
            _completed = completed;
            _pageIndex = 0;
            _visibleCharacterCount = 0;
            _characterAccumulator = 0f;
            _pageFinishedAt = float.PositiveInfinity;
            _pageStartedAt = Time.unscaledTime;
            _phaseStartedAt = Time.unscaledTime;
            _hintOpenedAt = Time.unscaledTime;
            _hintDismissedAt = float.PositiveInfinity;
            _openedFrame = Time.frameCount;
            _completionSent = false;
            _phase = PagePresentationPhase.Presenting;
            _showFirstUseHint = !_hasAcknowledgedInputHint;
            IsOpen = true;
            BeginCurrentPage();
            return true;
        }

        public void Close(bool invokeCompletion)
        {
            if (!IsOpen && _completed == null)
            {
                return;
            }

            _typingAudio.Stop();
            IsOpen = false;
            Action callback = _completed;
            _completed = null;
            _pages = Array.Empty<string>();
            _phase = PagePresentationPhase.Presenting;
            if (invokeCompletion && !_completionSent)
            {
                _completionSent = true;
                callback?.Invoke();
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
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
                    _visibleCharacterCount = Mathf.Min(CurrentPage.Length, _visibleCharacterCount + charactersToReveal);
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
            if (CurrentPage.Length == 0)
            {
                FinishCurrentPage();
                return;
            }
            _typingAudio.Start(_settings.TypingLoopEvent, this);
        }

        private void RevealCurrentPage()
        {
            _visibleCharacterCount = CurrentPage.Length;
            FinishCurrentPage();
        }

        private void FinishCurrentPage()
        {
            _typingAudio.Stop();
            _pageFinishedAt = Time.unscaledTime;
        }

        private void UpdatePageTransition()
        {
            if (_phase == PagePresentationPhase.FadingOut &&
                Time.unscaledTime >= _phaseStartedAt + Mathf.Max(0f, _settings.PageFadeOutDuration))
            {
                _phase = PagePresentationPhase.EmptyBeat;
                _phaseStartedAt = Time.unscaledTime;
                _visibleCharacterCount = 0;
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
            DrawNarrativeText(textRect, VisibleText, textColor);

            if (_phase == PagePresentationPhase.Presenting && !IsTyping)
            {
                DrawContinueIndicator(readingArea, _pageIndex + 1 >= _pages.Count, flicker);
            }

            DrawFirstUseHint(virtualWidth, virtualHeight);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
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
                clipping = TextClipping.Clip,
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
                _settings.TransmissionHeader ?? string.Empty,
                _headerStyle);

            if (IsTyping)
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

        private void DrawNarrativeText(Rect area, string text, Color textColor)
        {
            List<string> lines = WrapText(text ?? string.Empty, area.width, _narrativeStyle);
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
                        _narrativeStyle.lineHeight + _settings.TextGlowOffset),
                    lines[index],
                    _narrativeStyle);
                y += lineAdvance;
            }
        }

        private static List<string> WrapText(string text, float maximumWidth, GUIStyle style)
        {
            List<string> result = new List<string>();
            string[] authoredLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int lineIndex = 0; lineIndex < authoredLines.Length; lineIndex++)
            {
                string authoredLine = authoredLines[lineIndex];
                if (authoredLine.Length == 0)
                {
                    result.Add(string.Empty);
                    continue;
                }

                string[] words = authoredLine.Split(' ');
                string current = string.Empty;
                for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
                {
                    string candidate = current.Length == 0 ? words[wordIndex] : $"{current} {words[wordIndex]}";
                    if (current.Length > 0 && style.CalcSize(new GUIContent(candidate)).x > maximumWidth)
                    {
                        result.Add(current);
                        current = words[wordIndex];
                    }
                    else
                    {
                        current = candidate;
                    }
                }
                result.Add(current);
            }
            return result;
        }

        private void DrawContinueIndicator(Rect readingArea, bool finalPage, float flicker)
        {
            float pulse01 = (Mathf.Sin(Time.unscaledTime * _settings.IndicatorPulseSpeed) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_settings.IndicatorMinimumAlpha, 1f, pulse01) * flicker;
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
            Close(invokeCompletion: false);
            if (_runtimeFont != null)
            {
                Destroy(_runtimeFont);
            }
        }
    }
}
