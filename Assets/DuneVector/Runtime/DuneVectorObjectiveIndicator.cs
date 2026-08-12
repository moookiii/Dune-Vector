using UnityEngine;

namespace DuneVector
{
    /// <summary>
    /// Shared immediate-mode presentation for pickup and delivery objectives.
    /// Objective selection and distance calculation remain owned by the calling game mode.
    /// </summary>
    internal sealed class DuneVectorObjectiveIndicator
    {
        private GUIStyle _labelStyle;
        private Transform _target;
        private Vector2 _position;
        private Vector2 _direction = Vector2.up;
        private float _arrowVisibility;
        private bool _onScreen;
        private bool _initialized;
        private int _lastUpdateFrame = -1;
        private float _startFlashTime;

        public void Draw(
            Camera camera,
            Transform target,
            string objectiveLabel,
            float distance,
            DeliveryTuning settings,
            Texture2D objectiveIcon = null,
            float objectiveIconRadius = -1f)
        {
            if (camera == null || target == null || settings == null)
            {
                return;
            }

            float minimumScale = Mathf.Min(
                settings.ObjectiveIndicatorMinimumScale,
                settings.ObjectiveIndicatorMaximumScale);
            float maximumScale = Mathf.Max(
                settings.ObjectiveIndicatorMinimumScale,
                settings.ObjectiveIndicatorMaximumScale);
            float scale = Mathf.Clamp(
                Screen.height / Mathf.Max(1f, settings.ObjectiveIndicatorReferenceHeight),
                minimumScale,
                maximumScale);

            float authoredRadius = objectiveIcon != null && objectiveIconRadius > 0f
                ? objectiveIconRadius
                : settings.ObjectiveIndicatorHexagonRadius;
            float radius = authoredRadius * scale;
            float arrowLength = settings.ObjectiveIndicatorArrowLength * scale;
            float arrowWidth = settings.ObjectiveIndicatorArrowWidth * scale;
            float arrowGap = settings.ObjectiveIndicatorArrowGap * scale;
            float textGap = settings.ObjectiveIndicatorTextGap * scale;
            float textWidth = settings.ObjectiveIndicatorTextWidth * scale;
            float textHeight = settings.ObjectiveIndicatorTextHeight * scale;
            float edgePadding = settings.ObjectiveIndicatorEdgePadding * scale;
            float hysteresis = settings.ObjectiveIndicatorViewportHysteresis * scale;

            Vector3 projected = camera.WorldToScreenPoint(target.position);
            Vector2 projectedPoint = new Vector2(projected.x, Screen.height - projected.y);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 targetDirection = projectedPoint - screenCenter;
            if (projected.z <= 0f)
            {
                targetDirection = -targetDirection;
            }
            if (targetDirection.sqrMagnitude < 0.001f)
            {
                targetDirection = Vector2.up;
            }
            targetDirection.Normalize();

            Rect safeArea = Screen.safeArea;
            Rect guiSafeArea = new Rect(
                safeArea.x,
                Screen.height - safeArea.yMax,
                safeArea.width,
                safeArea.height);
            float shapeExtent = radius + arrowGap + arrowLength;
            float horizontalExtent = Mathf.Max(textWidth * 0.5f, shapeExtent);
            float topExtent = shapeExtent;
            float bottomExtent = Mathf.Max(shapeExtent, radius + textGap + textHeight);
            Rect anchorBounds = BuildAnchorBounds(
                guiSafeArea,
                edgePadding,
                horizontalExtent,
                topExtent,
                bottomExtent);

            bool targetChanged = _target != target;
            if (targetChanged)
            {
                _target = target;
                _initialized = false;
                _startFlashTime = Time.unscaledTime;
            }

            if (_lastUpdateFrame != Time.frameCount)
            {
                _lastUpdateFrame = Time.frameCount;
                UpdateState(
                    projected,
                    projectedPoint,
                    targetDirection,
                    screenCenter,
                    anchorBounds,
                    hysteresis,
                    settings);
            }

            EnsureStyle(settings, scale);
            float iconVisibility = EvaluateStartFlashVisibility(settings);
            Rect hexagonRect = new Rect(
                _position.x - radius,
                _position.y - radius,
                radius * 2f,
                radius * 2f);
            Color iconShadowColor = settings.ObjectiveIndicatorShadowColor;
            iconShadowColor.a *= iconVisibility;
            Color iconColor = settings.ObjectiveIndicatorColor;
            iconColor.a *= iconVisibility;
            DrawIcon(
                objectiveIcon != null ? objectiveIcon : settings.ObjectiveIndicatorHexagonIcon,
                hexagonRect,
                iconShadowColor,
                settings.ObjectiveIndicatorShadowOffset * scale,
                0f);
            DrawIcon(
                objectiveIcon != null ? objectiveIcon : settings.ObjectiveIndicatorHexagonIcon,
                hexagonRect,
                iconColor,
                Vector2.zero,
                0f);

            if (_arrowVisibility * iconVisibility > 0.001f)
            {
                Color shadow = settings.ObjectiveIndicatorShadowColor;
                shadow.a *= _arrowVisibility * iconVisibility;
                Color foreground = settings.ObjectiveIndicatorColor;
                foreground.a *= _arrowVisibility * iconVisibility;
                Vector2 arrowCenter = _position
                    + (_direction * (radius + arrowGap + (arrowLength * 0.5f)));
                Rect arrowRect = new Rect(
                    arrowCenter.x - (arrowLength * 0.5f),
                    arrowCenter.y - (arrowWidth * 0.5f),
                    arrowLength,
                    arrowWidth);
                float arrowRotation = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg - 180f;
                DrawIcon(
                    settings.ObjectiveIndicatorArrowIcon,
                    arrowRect,
                    shadow,
                    settings.ObjectiveIndicatorShadowOffset * scale,
                    arrowRotation);
                DrawIcon(
                    settings.ObjectiveIndicatorArrowIcon,
                    arrowRect,
                    foreground,
                    Vector2.zero,
                    arrowRotation);
            }

            Rect labelRect = new Rect(
                _position.x - (textWidth * 0.5f),
                _position.y + radius + textGap,
                textWidth,
                textHeight);
            Color previousColor = GUI.color;
            GUI.color = settings.ObjectiveIndicatorShadowColor;
            Rect shadowRect = labelRect;
            shadowRect.position += settings.ObjectiveIndicatorShadowOffset * scale;
            GUI.Label(shadowRect, $"{objectiveLabel}  {distance:0} m", _labelStyle);
            GUI.color = settings.ObjectiveIndicatorColor;
            GUI.Label(labelRect, $"{objectiveLabel}  {distance:0} m", _labelStyle);
            GUI.color = previousColor;
        }

        private float EvaluateStartFlashVisibility(DeliveryTuning settings)
        {
            int flashCount = Mathf.Max(0, settings.ObjectiveIndicatorStartFlashCount);
            if (flashCount == 0)
            {
                return 1f;
            }

            float onDuration = Mathf.Max(0.01f, settings.ObjectiveIndicatorStartFlashOnDuration);
            float offDuration = Mathf.Max(0.01f, settings.ObjectiveIndicatorStartFlashOffDuration);
            float cycleDuration = onDuration + offDuration;
            float elapsed = Mathf.Max(0f, Time.unscaledTime - _startFlashTime);
            if (elapsed >= flashCount * cycleDuration)
            {
                return 1f;
            }

            return Mathf.Repeat(elapsed, cycleDuration) < onDuration ? 1f : 0f;
        }

        private void UpdateState(
            Vector3 projected,
            Vector2 projectedPoint,
            Vector2 targetDirection,
            Vector2 screenCenter,
            Rect anchorBounds,
            float hysteresis,
            DeliveryTuning settings)
        {
            bool strictlyVisible = projected.z > 0f
                && projectedPoint.x >= 0f
                && projectedPoint.x <= Screen.width
                && projectedPoint.y >= 0f
                && projectedPoint.y <= Screen.height;
            if (!_initialized)
            {
                _onScreen = strictlyVisible;
            }
            else if (_onScreen)
            {
                _onScreen = projected.z > 0f
                    && projectedPoint.x >= -hysteresis
                    && projectedPoint.x <= Screen.width + hysteresis
                    && projectedPoint.y >= -hysteresis
                    && projectedPoint.y <= Screen.height + hysteresis;
            }
            else
            {
                _onScreen = projected.z > 0f
                    && projectedPoint.x >= hysteresis
                    && projectedPoint.x <= Screen.width - hysteresis
                    && projectedPoint.y >= hysteresis
                    && projectedPoint.y <= Screen.height - hysteresis;
            }

            Vector2 desiredPosition = _onScreen
                ? ClampToBounds(projectedPoint, anchorBounds)
                : IntersectBounds(screenCenter, targetDirection, anchorBounds);
            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            float positionBlend = ExpBlend(settings.ObjectiveIndicatorPositionSharpness, deltaTime);
            float transitionBlend = ExpBlend(settings.ObjectiveIndicatorTransitionSharpness, deltaTime);
            if (!_initialized)
            {
                _position = desiredPosition;
                _direction = targetDirection;
                _arrowVisibility = _onScreen ? 0f : 1f;
                _initialized = true;
                return;
            }

            _position = Vector2.Lerp(_position, desiredPosition, positionBlend);
            Vector2 blendedDirection = Vector2.Lerp(_direction, targetDirection, positionBlend);
            _direction = blendedDirection.sqrMagnitude > 0.001f
                ? blendedDirection.normalized
                : targetDirection;
            _arrowVisibility = Mathf.Lerp(_arrowVisibility, _onScreen ? 0f : 1f, transitionBlend);
        }

        private void EnsureStyle(DeliveryTuning settings, float scale)
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                wordWrap = false,
            };
            _labelStyle.fontSize = Mathf.Max(1, Mathf.RoundToInt(settings.ObjectiveIndicatorFontSize * scale));
            _labelStyle.normal.textColor = Color.white;
        }

        private static Rect BuildAnchorBounds(
            Rect safeArea,
            float padding,
            float horizontalExtent,
            float topExtent,
            float bottomExtent)
        {
            float left = safeArea.xMin + padding + horizontalExtent;
            float right = safeArea.xMax - padding - horizontalExtent;
            float top = safeArea.yMin + padding + topExtent;
            float bottom = safeArea.yMax - padding - bottomExtent;
            if (left > right)
            {
                left = right = safeArea.center.x;
            }
            if (top > bottom)
            {
                top = bottom = safeArea.center.y;
            }
            return Rect.MinMaxRect(left, top, right, bottom);
        }

        private static Vector2 ClampToBounds(Vector2 point, Rect bounds)
        {
            return new Vector2(
                Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
        }

        private static Vector2 IntersectBounds(Vector2 origin, Vector2 direction, Rect bounds)
        {
            origin = ClampToBounds(origin, bounds);
            float horizontalDistance = direction.x > 0f
                ? bounds.xMax - origin.x
                : origin.x - bounds.xMin;
            float verticalDistance = direction.y > 0f
                ? bounds.yMax - origin.y
                : origin.y - bounds.yMin;
            float horizontalScale = Mathf.Abs(direction.x) > 0.0001f
                ? horizontalDistance / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float verticalScale = Mathf.Abs(direction.y) > 0.0001f
                ? verticalDistance / Mathf.Abs(direction.y)
                : float.PositiveInfinity;
            return ClampToBounds(origin + (direction * Mathf.Min(horizontalScale, verticalScale)), bounds);
        }

        private static float ExpBlend(float sharpness, float deltaTime)
        {
            return sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * deltaTime);
        }

        private static void DrawIcon(
            Texture2D texture,
            Rect rect,
            Color color,
            Vector2 offset,
            float rotation)
        {
            if (texture == null || rect.width <= 0f || rect.height <= 0f || color.a <= 0f)
            {
                return;
            }

            rect.position += offset;
            Vector2 pivot = rect.center;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(rotation, pivot);
            GUI.color = color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }
}
