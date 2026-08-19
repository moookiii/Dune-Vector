using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    /// <summary>
    /// Full-screen award card played when a courier contract grants a new drone trail. The
    /// trail is cloned onto an isolated rig that flies a loop far above the streamed desert
    /// and is filmed by a dedicated camera, so the render view shows the real world-space
    /// effect rather than a still icon. The authored trails emit over distance, so the
    /// preview only produces a plume while the clone is actually moving.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DuneVectorTrailUnlockShowcase : MonoBehaviour
    {
        private readonly DeliveryMessageInputReader _input = new DeliveryMessageInputReader();
        private DroneTrailUnlockShowcaseTuning _tuning;
        private DronePlayer _playerInput;
        private Camera _gameplayCamera;
        private RenderTexture _renderTexture;
        private Transform _rigRoot;
        private Transform _previewMover;
        private Camera _previewCamera;
        private ParticleSystem[] _previewParticles = Array.Empty<ParticleSystem>();
        private TrailRenderer[] _previewTrails = Array.Empty<TrailRenderer>();
        private int _trailClearTicksRemaining;
        private GUIStyle _headerStyle;
        private GUIStyle _nameStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _promptStyle;
        private Action _closed;
        private string _trailDisplayName = string.Empty;
        private float _openedAt;
        private float _orbitAngle;
        private float _styledScale = -1f;
        private int _previewLayer = -1;
        private int _openedFrame;

        public bool IsOpen { get; private set; }

        public void Initialize(
            DroneTrailUnlockShowcaseTuning tuning,
            DronePlayer playerInput,
            Camera gameplayCamera)
        {
            _tuning = tuning ?? new DroneTrailUnlockShowcaseTuning();
            _playerInput = playerInput;
            _gameplayCamera = gameplayCamera;
            _previewLayer = LayerMask.NameToLayer(_tuning.PreviewLayerName);
            if (_previewLayer < 0)
            {
                Debug.LogWarning(
                    $"Layer '{_tuning.PreviewLayerName}' does not exist, so the drone trail unlock " +
                    "showcase cannot isolate its preview rig by layer. Add the layer in Project " +
                    "Settings > Tags and Layers.",
                    this);
                return;
            }

            // The rig lives in the gameplay scene, so the gameplay camera has to stop drawing
            // its layer or the preview would also hang in the sky over the world.
            if (_gameplayCamera != null)
            {
                _gameplayCamera.cullingMask &= ~(1 << _previewLayer);
            }
        }

        public bool Open(DroneTrailOption option, Action closed)
        {
            if (IsOpen || option == null || option.TrailObject == null || _tuning == null)
            {
                return false;
            }

            if (!BuildPreviewRig(option))
            {
                DestroyPreviewRig();
                return false;
            }

            _closed = closed;
            _trailDisplayName = option.DisplayName ?? string.Empty;
            _openedAt = Time.unscaledTime;
            _openedFrame = Time.frameCount;
            IsOpen = true;
            _playerInput?.SetInputEnabled(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            DestroyPreviewRig();
            _playerInput?.SetInputEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Action callback = _closed;
            _closed = null;
            callback?.Invoke();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            AdvancePreviewFlight();
            if (_trailClearTicksRemaining > 0)
            {
                _trailClearTicksRemaining--;
                for (int i = 0; i < _previewTrails.Length; i++)
                {
                    _previewTrails[i]?.Clear();
                }
            }
            RestartFinishedPreviewEffects();

            bool inputAllowed = Time.frameCount != _openedFrame &&
                Time.unscaledTime >= _openedAt + Mathf.Max(0f, _tuning.OpenInputDelay);
            if (inputAllowed && _input.WasAdvancePressedThisFrame())
            {
                Close();
            }
        }

        private void OnDestroy()
        {
            DestroyPreviewRig();
            ReleaseRenderTexture();
        }

        private bool BuildPreviewRig(DroneTrailOption option)
        {
            EnsureRenderTexture();
            if (_renderTexture == null)
            {
                return false;
            }

            GameObject root = new GameObject("Drone Trail Unlock Showcase");
            // Built inactive so the cloned effect's own scripts never reach Awake before the
            // rig has been moved onto the isolated preview layer and silenced.
            root.SetActive(false);
            root.transform.position = _tuning.PreviewWorldOrigin;
            _rigRoot = root.transform;

            GameObject mover = new GameObject("Trail Flight Path");
            mover.transform.SetParent(_rigRoot, false);
            mover.transform.localScale = Vector3.one * Mathf.Max(0.01f, _tuning.PreviewScale);
            _previewMover = mover.transform;
            _orbitAngle = 0f;
            ApplyFlightPose();

            // Spawned straight onto the flight path. Cloning it where the drone stands and
            // moving it afterwards makes the effect's Trail Renderer record one enormous
            // segment stretching from the hub up to the rig.
            GameObject clone = Instantiate(
                option.TrailObject,
                _previewMover.position,
                _previewMover.rotation,
                _previewMover);
            clone.name = option.ObjectName;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = option.TrailObject.transform.localScale;
            clone.SetActive(true);
            SilenceClonedEffect(clone);
            RestoreGatedDistanceEmission(clone);
            _previewParticles = clone.GetComponentsInChildren<ParticleSystem>(true);
            _previewTrails = clone.GetComponentsInChildren<TrailRenderer>(true);

            GameObject cameraObject = new GameObject("Trail Preview Camera");
            cameraObject.transform.SetParent(_rigRoot, false);
            _previewCamera = cameraObject.AddComponent<Camera>();
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = _tuning.PreviewBackgroundColor;
            _previewCamera.cullingMask = _previewLayer >= 0 ? 1 << _previewLayer : ~0;
            _previewCamera.fieldOfView = _tuning.PreviewFieldOfView;
            _previewCamera.nearClipPlane = _tuning.PreviewNearClip;
            _previewCamera.farClipPlane = Mathf.Max(_tuning.PreviewNearClip + 1f, _tuning.PreviewFarClip);
            _previewCamera.useOcclusionCulling = false;
            _previewCamera.allowHDR = true;
            _previewCamera.targetTexture = _renderTexture;
            UniversalAdditionalCameraData cameraData = _previewCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderPostProcessing = false;
                cameraData.renderShadows = false;
            }

            if (_previewLayer >= 0)
            {
                ApplyLayerRecursively(_rigRoot, _previewLayer);
            }

            UpdatePreviewCamera();
            root.SetActive(true);
            ClearPreviewHistory();
            _trailClearTicksRemaining = 2;
            return true;
        }

        /// <summary>
        /// The drone's own trail gate switches every distance emitter off whenever the drone is
        /// not moving horizontally, and Instantiate copies that live component state. The
        /// showcase clone flies under its own power, so the gated emitters are switched back
        /// on or the preview shows the effect's core with no plume behind it.
        /// </summary>
        private static void RestoreGatedDistanceEmission(GameObject clone)
        {
            ParticleSystem[] particleSystems = clone.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = particleSystem.emission;
                if (emission.rateOverDistanceMultiplier > 0f)
                {
                    emission.enabled = true;
                }
            }
        }

        /// <summary>
        /// Drops anything the effect recorded before or during its first activated frame, so no
        /// spawn-position artefact is left hanging in the render view.
        /// </summary>
        private void ClearPreviewHistory()
        {
            for (int i = 0; i < _previewTrails.Length; i++)
            {
                _previewTrails[i]?.Clear();
            }
            for (int i = 0; i < _previewParticles.Length; i++)
            {
                _previewParticles[i]?.Clear(false);
            }
        }

        /// <summary>
        /// The authored trails carry the missile pack's audio sources and pitch helpers. The
        /// showcase wants their visuals only, so every script is switched off before the rig
        /// is activated and every audio source is silenced at the source.
        /// </summary>
        private static void SilenceClonedEffect(GameObject clone)
        {
            MonoBehaviour[] behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null)
                {
                    behaviours[i].enabled = false;
                }
            }

            AudioSource[] audioSources = clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                AudioSource audioSource = audioSources[i];
                if (audioSource == null)
                {
                    continue;
                }
                audioSource.playOnAwake = false;
                audioSource.enabled = false;
            }
        }

        private static void ApplyLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++)
            {
                ApplyLayerRecursively(target.GetChild(i), layer);
            }
        }

        private void AdvancePreviewFlight()
        {
            if (_previewMover == null)
            {
                return;
            }

            _orbitAngle = Mathf.Repeat(
                _orbitAngle + (_tuning.OrbitDegreesPerSecond * Time.unscaledDeltaTime),
                360f);
            ApplyFlightPose();
            UpdatePreviewCamera();
        }

        private void ApplyFlightPose()
        {
            _previewMover.localPosition = FlightPathPoint(_orbitAngle);

            // A tangent sample keeps the effect nose-forward, which is the orientation the
            // authored missile trails stream their plume behind.
            Vector3 ahead = FlightPathPoint(_orbitAngle + 4f);
            Vector3 forward = ahead - _previewMover.localPosition;
            if (forward.sqrMagnitude > 0.000001f)
            {
                _previewMover.localRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// The camera rides in the effect's own frame rather than watching the loop from a
        /// fixed point, so the subject stays the same size in the card no matter how wide or
        /// fast the flight path is tuned.
        /// </summary>
        private void UpdatePreviewCamera()
        {
            if (_previewCamera == null || _previewMover == null)
            {
                return;
            }

            Vector3 offset = _tuning.PreviewCameraChaseOffset;
            Vector3 subject = _previewMover.position;
            Transform cameraTransform = _previewCamera.transform;
            cameraTransform.position = subject +
                (_previewMover.right * offset.x) +
                (Vector3.up * offset.y) +
                (_previewMover.forward * offset.z);
            cameraTransform.LookAt(
                subject + (_previewMover.forward * _tuning.PreviewCameraLookAhead),
                Vector3.up);
        }

        private Vector3 FlightPathPoint(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float radius = Mathf.Max(0.1f, _tuning.OrbitRadius);
            float height = Mathf.Sin(radians * Mathf.Max(0f, _tuning.OrbitVerticalCycles)) *
                Mathf.Max(0f, _tuning.OrbitVerticalAmplitude);
            return new Vector3(Mathf.Cos(radians) * radius, height, Mathf.Sin(radians) * radius);
        }

        /// <summary>
        /// Some authored trails are finite bursts rather than looping emitters. Replaying them
        /// once every emitter has run dry keeps the render view alive for as long as the card
        /// is up.
        /// </summary>
        private void RestartFinishedPreviewEffects()
        {
            if (_previewParticles.Length == 0)
            {
                return;
            }

            for (int i = 0; i < _previewParticles.Length; i++)
            {
                ParticleSystem particles = _previewParticles[i];
                if (particles != null && particles.IsAlive(true))
                {
                    return;
                }
            }

            for (int i = 0; i < _previewParticles.Length; i++)
            {
                _previewParticles[i]?.Play(false);
            }
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(64, _tuning.RenderTextureWidth);
            int height = Mathf.Max(64, _tuning.RenderTextureHeight);
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            {
                return;
            }

            ReleaseRenderTexture();
            _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Drone Trail Unlock Preview",
                antiAliasing = 1,
            };
            _renderTexture.Create();
        }

        private void ReleaseRenderTexture()
        {
            if (_renderTexture == null)
            {
                return;
            }

            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        private void DestroyPreviewRig()
        {
            if (_previewCamera != null)
            {
                _previewCamera.targetTexture = null;
            }
            if (_rigRoot != null)
            {
                Destroy(_rigRoot.gameObject);
            }
            _rigRoot = null;
            _previewMover = null;
            _previewCamera = null;
            _previewParticles = Array.Empty<ParticleSystem>();
            _previewTrails = Array.Empty<TrailRenderer>();
            _trailClearTicksRemaining = 0;
        }

        private void OnGUI()
        {
            // Draw-only overlay: it owns no controls, so the layout pass would repeat every
            // measurement for nothing. Only Repaint does work.
            if (Event.current.type != EventType.Repaint || !IsOpen)
            {
                return;
            }

            float scale = CalculateScale();
            EnsureStyles(scale);
            float fade = Mathf.Clamp01(
                (Time.unscaledTime - _openedAt) / Mathf.Max(0.01f, _tuning.OpenFadeDuration));

            GUI.depth = -1250;
            Color previousColor = GUI.color;

            FillRect(new Rect(0f, 0f, Screen.width, Screen.height), _tuning.BackdropColor, fade);

            float panelWidth = Mathf.Min(_tuning.PanelWidth * scale, Screen.width - (16f * scale));
            float panelHeight = Mathf.Min(_tuning.PanelHeight * scale, Screen.height - (16f * scale));
            Rect panel = new Rect(
                Mathf.Round((Screen.width - panelWidth) * 0.5f),
                Mathf.Round((Screen.height - panelHeight) * 0.5f),
                panelWidth,
                panelHeight);

            float borderThickness = Mathf.Max(1f, _tuning.BorderThickness * scale);
            float shadowOffset = _tuning.ShadowOffset * scale;
            FillRect(
                new Rect(panel.x + shadowOffset, panel.y + shadowOffset, panel.width, panel.height),
                _tuning.ShadowColor,
                fade);
            FillRect(panel, _tuning.PanelColor, fade);
            DrawBorder(panel, _tuning.PanelBorderColor, borderThickness, fade);

            Rect header = new Rect(panel.x, panel.y, panel.width, _tuning.HeaderHeight * scale);
            FillRect(header, _tuning.HeaderColor, fade);
            FillRect(
                new Rect(panel.x, panel.y, panel.width, _tuning.AccentBarHeight * scale),
                _tuning.AccentColor,
                fade);
            FillRect(
                new Rect(panel.x, header.yMax - borderThickness, panel.width, borderThickness),
                _tuning.PanelBorderColor,
                fade);
            DrawLabel(header, _tuning.HeaderText, _headerStyle, _tuning.AccentColor, fade);

            float padding = _tuning.PanelPadding * scale;
            Rect content = new Rect(
                panel.x + padding,
                header.yMax + padding,
                panel.width - (padding * 2f),
                panel.yMax - header.yMax - (padding * 2f));

            Rect preview = new Rect(content.x, content.y, content.width, _tuning.PreviewHeight * scale);
            FillRect(preview, _tuning.PreviewBackgroundColor, fade);
            if (_renderTexture != null)
            {
                GUI.color = new Color(1f, 1f, 1f, fade);
                GUI.DrawTexture(preview, _renderTexture, ScaleMode.ScaleToFit, false);
                GUI.color = previousColor;
            }
            DrawBorder(preview, _tuning.PanelBorderColor, borderThickness, fade);
            DrawCornerBrackets(preview, _tuning.AccentColor, scale, fade);

            Rect nameRect = new Rect(
                content.x,
                preview.yMax + (_tuning.NameTopGap * scale),
                content.width,
                _tuning.NameFontSize * 1.6f * scale);
            DrawLabel(nameRect, _trailDisplayName, _nameStyle, _tuning.PrimaryTextColor, fade);

            Rect promptRect = new Rect(
                content.x,
                panel.yMax - (_tuning.PromptBottomGap * scale) - (_tuning.PromptFontSize * 1.8f * scale),
                content.width,
                _tuning.PromptFontSize * 1.8f * scale);
            Rect bodyRect = new Rect(
                content.x,
                nameRect.yMax + (_tuning.BodyTopGap * scale),
                content.width,
                Mathf.Max(0f, promptRect.y - nameRect.yMax - (_tuning.BodyTopGap * 2f * scale)));
            DrawLabel(bodyRect, _tuning.BodyText, _bodyStyle, _tuning.SecondaryTextColor, fade);

            float pulse = Mathf.Lerp(
                Mathf.Clamp01(_tuning.PromptMinimumAlpha),
                1f,
                0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * _tuning.PromptPulseSpeed)));
            DrawLabel(promptRect, _tuning.ContinuePrompt, _promptStyle, _tuning.PromptColor, fade * pulse);

            GUI.color = previousColor;
        }

        private float CalculateScale()
        {
            float minimumScale = Mathf.Min(_tuning.MinimumScale, _tuning.MaximumScale);
            float maximumScale = Mathf.Max(_tuning.MinimumScale, _tuning.MaximumScale);
            return Mathf.Clamp(
                Mathf.Min(
                    Screen.width / Mathf.Max(1f, _tuning.ReferenceWidth),
                    Screen.height / Mathf.Max(1f, _tuning.ReferenceHeight)),
                minimumScale,
                maximumScale);
        }

        private void EnsureStyles(float scale)
        {
            if (_headerStyle != null && Mathf.Abs(scale - _styledScale) < 0.001f)
            {
                return;
            }
            _styledScale = scale;

            _headerStyle = CreateLabelStyle(_tuning.HeaderFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
            _nameStyle = CreateLabelStyle(_tuning.NameFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
            _bodyStyle = CreateLabelStyle(_tuning.BodyFontSize, FontStyle.Normal, TextAnchor.UpperCenter, true, scale);
            _promptStyle = CreateLabelStyle(_tuning.PromptFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, false, scale);
        }

        private static GUIStyle CreateLabelStyle(
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            bool wordWrap,
            float scale)
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * scale)),
                fontStyle = fontStyle,
                wordWrap = wordWrap,
                clipping = TextClipping.Clip,
                padding = new RectOffset(),
                margin = new RectOffset(),
            };
        }

        private static void DrawLabel(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            if (string.IsNullOrEmpty(text) || style == null)
            {
                return;
            }

            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(alpha));
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        private void DrawCornerBrackets(Rect rect, Color color, float scale, float alpha)
        {
            float length = _tuning.CornerBracketLength * scale;
            float thickness = Mathf.Max(1f, _tuning.CornerBracketThickness * scale);
            if (length <= 0f)
            {
                return;
            }

            FillRect(new Rect(rect.x, rect.y, length, thickness), color, alpha);
            FillRect(new Rect(rect.x, rect.y, thickness, length), color, alpha);
            FillRect(new Rect(rect.xMax - length, rect.y, length, thickness), color, alpha);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, length), color, alpha);
            FillRect(new Rect(rect.x, rect.yMax - thickness, length, thickness), color, alpha);
            FillRect(new Rect(rect.x, rect.yMax - length, thickness, length), color, alpha);
            FillRect(new Rect(rect.xMax - length, rect.yMax - thickness, length, thickness), color, alpha);
            FillRect(new Rect(rect.xMax - thickness, rect.yMax - length, thickness, length), color, alpha);
        }

        private static void DrawBorder(Rect rect, Color color, float thickness, float alpha)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color, alpha);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color, alpha);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color, alpha);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color, alpha);
        }

        private static void FillRect(Rect rect, Color color, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(alpha));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
