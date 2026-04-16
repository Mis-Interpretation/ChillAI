using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace ChillAI.View.UI
{
    /// <summary>
    /// Make a UGUI RectTransform follow a UI Toolkit VisualElement.
    /// Works whether movement happens on the element itself or one of its parents,
    /// because position is read from worldBound each frame.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UiToolkitFollowerView : MonoBehaviour
    {
        enum AnchorMode
        {
            Center,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Header("UI Toolkit Target")]
        [SerializeField] UIDocument targetDocument;
        [SerializeField] string targetElementName = "menu-btn";
        [SerializeField] string targetElementClass;

        [Header("Follow Settings")]
        [SerializeField] AnchorMode anchorMode = AnchorMode.Center;
        [SerializeField] Vector2 screenOffset;
        [SerializeField] Vector2 assumedUiRootFromBottomRight;
        [SerializeField] bool keepInitialRelativeOffset = true;
        [SerializeField] bool hideWhenTargetMissing = true;
        [SerializeField] bool followEvenWhenInactive = true;

        RectTransform _selfRect;
        Canvas _canvas;
        Graphic _selfGraphic;
        CanvasRenderer _canvasRenderer;
        VisualElement _targetElement;
        bool _hasCapturedInitialOffset;
        Vector2 _initialScreenOffset;

        void Awake()
        {
            _selfRect = GetComponent<RectTransform>();
            _selfGraphic = GetComponent<Graphic>();
            _canvasRenderer = GetComponent<CanvasRenderer>();
            ResolveCanvasRefs();
            ResolveTargetElement();
        }

        void OnEnable()
        {
            _hasCapturedInitialOffset = false;

            if (!keepInitialRelativeOffset)
                return;

            TryCaptureInitialOffsetAtEnable();
        }

        /// <summary>
        /// Re-captures the current relative offset between this UGUI element and the target UITK element.
        /// Useful after manual runtime adjustments.
        /// </summary>
        public void RecalibrateNow()
        {
            _hasCapturedInitialOffset = false;

            if (!TryGetTargetScreenBottomLeft(out var targetScreenBottomLeft))
                return;

            if (!TryGetSelfScreenBottomLeft(out var selfScreenBottomLeft))
                return;

            _initialScreenOffset = selfScreenBottomLeft - targetScreenBottomLeft;
            _hasCapturedInitialOffset = true;
        }

        [ContextMenu("Recalibrate Follow Offset")]
        void RecalibrateFromContextMenu()
        {
            RecalibrateNow();
        }

        void LateUpdate()
        {
            if (!followEvenWhenInactive && (!isActiveAndEnabled || !gameObject.activeInHierarchy))
                return;

            if (!TryGetTargetScreenBottomLeft(out var targetScreenBottomLeft))
            {
                SetVisible(false);
                return;
            }

            var desiredScreenBottomLeft = keepInitialRelativeOffset
                ? targetScreenBottomLeft + (_hasCapturedInitialOffset ? _initialScreenOffset : Vector2.zero)
                : targetScreenBottomLeft;

            if (!TryConvertScreenToWorld(desiredScreenBottomLeft, out var targetWorld))
            {
                SetVisible(false);
                return;
            }

            _selfRect.position = targetWorld;
            SetVisible(true);
        }

        void ResolveCanvasRefs()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        void ResolveTargetElement()
        {
            var doc = targetDocument;
            if (doc == null)
                doc = FindFirstObjectByType<UIDocument>();

            var root = doc != null ? doc.rootVisualElement : null;
            if (root == null)
            {
                _targetElement = null;
                return;
            }

            if (!string.IsNullOrEmpty(targetElementName))
                _targetElement = root.Q<VisualElement>(targetElementName);

            if (_targetElement == null && !string.IsNullOrEmpty(targetElementClass))
                _targetElement = root.Q<VisualElement>(className: targetElementClass);
        }

        bool TryCaptureInitialOffsetAtEnable()
        {
            if (!TryGetSelfScreenBottomLeft(out var selfScreenBottomLeft))
                return false;

            if (TryGetTargetScreenBottomLeft(out var targetScreenBottomLeft))
            {
                _initialScreenOffset = selfScreenBottomLeft - targetScreenBottomLeft;
                _hasCapturedInitialOffset = IsFinite(_initialScreenOffset);
                return _hasCapturedInitialOffset;
            }

            // Fallback when target is not yet queryable on enable.
            var assumedRootBottomLeft = BottomRightToBottomLeft(assumedUiRootFromBottomRight);
            if (!IsFinite(assumedRootBottomLeft))
                return false;
            _initialScreenOffset = selfScreenBottomLeft - assumedRootBottomLeft;
            _hasCapturedInitialOffset = IsFinite(_initialScreenOffset);
            return _hasCapturedInitialOffset;
        }

        bool TryGetSelfScreenBottomLeft(out Vector2 selfScreenBottomLeft)
        {
            selfScreenBottomLeft = default;

            if (_canvas == null)
                ResolveCanvasRefs();

            if (_canvas == null)
                return false;

            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            selfScreenBottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _selfRect.position);
            return IsFinite(selfScreenBottomLeft);
        }

        bool TryGetTargetScreenBottomLeft(out Vector2 targetScreenBottomLeft)
        {
            targetScreenBottomLeft = default;

            if (_targetElement == null || _targetElement.panel == null)
                ResolveTargetElement();

            if (_targetElement == null || _targetElement.panel == null)
                return false;

            if (_canvas == null)
                ResolveCanvasRefs();

            if (_canvas == null)
                return false;

            if (!TryGetAnchorPanelPoint(_targetElement, anchorMode, out var panelPoint))
                return false;
            if (!IsFinite(panelPoint))
                return false;
            var screenTopLeft = PanelToScreenTopLeft(_targetElement.panel, panelPoint);
            if (!IsFinite(screenTopLeft))
                return false;
            targetScreenBottomLeft = new Vector2(screenTopLeft.x, Screen.height - screenTopLeft.y) + screenOffset;
            return IsFinite(targetScreenBottomLeft);
        }

        bool TryConvertScreenToWorld(Vector2 screenBottomLeft, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!IsFinite(screenBottomLeft))
                return false;

            if (_canvas == null)
                ResolveCanvasRefs();

            if (_canvas == null)
                return false;

            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            var parentRect = _selfRect.parent as RectTransform;
            if (parentRect == null)
                return false;

            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, screenBottomLeft, camera, out worldPosition))
                return false;

            return IsFinite(worldPosition);
        }

        void SetVisible(bool visible)
        {
            if (!hideWhenTargetMissing)
                return;

            if (_selfGraphic != null)
                _selfGraphic.enabled = visible;

            if (_canvasRenderer != null)
                _canvasRenderer.cull = !visible;
        }

        static bool TryGetAnchorPanelPoint(VisualElement element, AnchorMode mode, out Vector2 panelPoint)
        {
            panelPoint = default;
            if (element == null)
                return false;

            var layout = element.layout;
            if (!IsFinite(layout.width) || !IsFinite(layout.height))
                return false;

            var width = Mathf.Max(0f, layout.width);
            var height = Mathf.Max(0f, layout.height);
            var localPoint = mode switch
            {
                AnchorMode.TopLeft => new Vector2(0f, 0f),
                AnchorMode.TopRight => new Vector2(width, 0f),
                AnchorMode.BottomLeft => new Vector2(0f, height),
                AnchorMode.BottomRight => new Vector2(width, height),
                _ => new Vector2(width * 0.5f, height * 0.5f)
            };

            panelPoint = element.LocalToWorld(localPoint);
            return IsFinite(panelPoint);
        }

        static Vector2 PanelToScreenTopLeft(IPanel panel, Vector2 panelPoint)
        {
            // Derive inverse mapping from ScreenToPanel using wide samples.
            // 1px samples can introduce noticeable scale error due internal rounding.
            var screenWidth = Mathf.Max(1f, Screen.width);
            var screenHeight = Mathf.Max(1f, Screen.height);

            var s0 = Vector2.zero;
            var sX = new Vector2(screenWidth, 0f);
            var sY = new Vector2(0f, screenHeight);

            var panelAt0 = RuntimePanelUtils.ScreenToPanel(panel, s0);
            var panelAtX = RuntimePanelUtils.ScreenToPanel(panel, sX);
            var panelAtY = RuntimePanelUtils.ScreenToPanel(panel, sY);
            if (!IsFinite(panelAt0) || !IsFinite(panelAtX) || !IsFinite(panelAtY) || !IsFinite(panelPoint))
                return new Vector2(float.NaN, float.NaN);

            var dx = panelAtX.x - panelAt0.x;
            var dy = panelAtY.y - panelAt0.y;
            if (!IsFinite(dx) || !IsFinite(dy))
                return new Vector2(float.NaN, float.NaN);

            if (Mathf.Abs(dx) < 0.0001f || Mathf.Abs(dy) < 0.0001f)
                return panelPoint;

            var sx = (panelPoint.x - panelAt0.x) * (screenWidth / dx);
            var sy = (panelPoint.y - panelAt0.y) * (screenHeight / dy);
            return new Vector2(sx, sy);
        }

        static Vector2 BottomRightToBottomLeft(Vector2 positionFromBottomRight)
        {
            return new Vector2(Screen.width - positionFromBottomRight.x, positionFromBottomRight.y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
