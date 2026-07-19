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
        public bool IsOpen { get; private set; }
        public bool IsTyping => IsOpen && _visibleCharacterCount < CurrentPage.Length;
        public int CurrentPageIndex => _pageIndex;
        public string VisibleText => CurrentPage.Substring(0, Mathf.Clamp(_visibleCharacterCount, 0, CurrentPage.Length));

        private readonly DeliveryMessageInputReader _input = new DeliveryMessageInputReader();
        private readonly DeliveryMessageTypingAudio _typingAudio = new DeliveryMessageTypingAudio();
        private IReadOnlyList<string> _pages = Array.Empty<string>();
        private DeliveryMessageTuning _settings;
        private Action _completed;
        private int _pageIndex;
        private int _visibleCharacterCount;
        private float _characterAccumulator;
        private float _pageFinishedAt;
        private int _openedFrame;
        private bool _completionSent;

        private string CurrentPage => _pageIndex >= 0 && _pageIndex < _pages.Count
            ? _pages[_pageIndex] ?? string.Empty
            : string.Empty;

        public void Initialize(DeliveryMessageTuning settings)
        {
            _settings = settings ?? new DeliveryMessageTuning();
            _settings.EnsureInitialized();
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
            _openedFrame = Time.frameCount;
            _completionSent = false;
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

            bool advancePressed = Time.frameCount != _openedFrame && _input.WasAdvancePressedThisFrame();
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
                _pageIndex++;
                _visibleCharacterCount = 0;
                _characterAccumulator = 0f;
                BeginCurrentPage();
                return;
            }

            Close(invokeCompletion: true);
        }

        private void BeginCurrentPage()
        {
            _pageFinishedAt = float.PositiveInfinity;
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

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            GUI.depth = -1200;
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), VisibleText, style);
        }

        private void OnDisable()
        {
            Close(invokeCompletion: false);
        }

        private void OnDestroy()
        {
            _typingAudio.Dispose();
            Close(invokeCompletion: false);
        }
    }
}
