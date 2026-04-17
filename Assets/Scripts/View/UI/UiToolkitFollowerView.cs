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
        // Offset from the target's anchor to this element's origin, expressed in UITK panel
        // coordinates (same space as VisualElement.LocalToWorld / layout). Storing in panel
        // space means the offset scales together with the target under PanelSettings scaling,
        // so the relative position stays proportional across resolution / aspect changes.
        Vector2 _initialPanelOffset;

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
            // Do not capture the offset here: on the frame this component is enabled the
            // UITK target's layout can still be unresolved (layout.width/height == 0),
            // which would bake a wrong anchor point and cause a visible jump on the
            // first frame after real layout comes in. The capture is deferred to the
            // first LateUpdate where TryGetTargetPanelPoint reports a real layout.
            _hasCapturedInitialOffset = false;
        }

        /// <summary>
        /// Re-captures the current relative offset between this UGUI element and the target UITK element.
        /// Useful after manual runtime adjustments.
        /// </summary>
        public void RecalibrateNow()
        {
            _hasCapturedInitialOffset = false;

            if (!TryGetTargetPanelPoint(out var targetPanel))
                return;

            if (!TryGetSelfPanelPoint(out var selfPanel))
                return;

            _initialPanelOffset = selfPanel - targetPanel;
            _hasCapturedInitialOffset = IsFinite(_initialPanelOffset);
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

            if (!TryGetTargetPanelPoint(out var targetPanel))
            {
                SetVisible(false);
                return;
            }

            // Lazy capture: OnEnable runs before the UITK element is laid out, so we
            // defer the baseline to the first frame where the target has a real layout.
            if (keepInitialRelativeOffset && !_hasCapturedInitialOffset)
            {
                if (!TryCaptureInitialPanelOffset())
                {
                    // Leave _selfRect.position untouched so the editor-time placement
                    // is preserved until we have a valid baseline. This is what makes
                    // the follower look identical before and after entering Play mode.
                    return;
                }
            }

            var desiredPanel = keepInitialRelativeOffset
                ? targetPanel + _initialPanelOffset
                : targetPanel;

            var screenTopLeft = PanelToScreenTopLeft(_targetElement.panel, desiredPanel);
            if (!IsFinite(screenTopLeft))
            {
                SetVisible(false);
                return;
            }

            var screenBottomLeft = new Vector2(screenTopLeft.x, Screen.height - screenTopLeft.y) + screenOffset;

            if (!TryConvertScreenToWorld(screenBottomLeft, out var targetWorld))
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

        bool TryCaptureInitialPanelOffset()
        {
            // Use layout-only panel position for the target — i.e. position derived purely
            // from VisualElement.layout up the tree, ignoring any transform.position on
            // ancestors. This matches the edit-time view of the target (where no ancestor
            // has been dragged or restored from saved layout).
            //
            // LateUpdate still looks up the target via LocalToWorld, which *does* include
            // transforms. The combination means the follower tracks ancestor transform
            // deltas (drag / restore) while preserving the editor-time relative offset.
            if (!TryGetTargetLayoutOnlyPanelPoint(out var targetPanel))
                return false;

            if (!TryGetSelfPanelPoint(out var selfPanel))
                return false;

            _initialPanelOffset = selfPanel - targetPanel;
            _hasCapturedInitialOffset = IsFinite(_initialPanelOffset);
            return _hasCapturedInitialOffset;
        }

        bool TryGetTargetLayoutOnlyPanelPoint(out Vector2 panelPoint)
        {
            panelPoint = default;

            if (_targetElement == null || _targetElement.panel == null)
                ResolveTargetElement();
            if (_targetElement == null || _targetElement.panel == null)
                return false;

            if (!TryGetAnchorLocalPoint(_targetElement, anchorMode, out var localPoint))
                return false;

            // Walk up the tree summing layout.min — this converts element-local to panel
            // space through pure layout, bypassing VisualElement.transform on every node.
            var accumulated = localPoint;
            var current = _targetElement;
            while (current != null)
            {
                var layout = current.layout;
                if (!IsFinite(layout.x) || !IsFinite(layout.y))
                    return false;
                accumulated += new Vector2(layout.x, layout.y);
                if (current.hierarchy.parent == null) break;
                current = current.hierarchy.parent;
            }

            if (!IsFinite(accumulated))
                return false;

            panelPoint = accumulated;
            return true;
        }

        bool TryGetSelfPanelPoint(out Vector2 panelPoint)
        {
            panelPoint = default;

            if (_targetElement == null || _targetElement.panel == null)
                ResolveTargetElement();
            if (_targetElement == null || _targetElement.panel == null)
                return false;

            if (!TryGetSelfScreenBottomLeft(out var selfScreenBottomLeft))
                return false;

            var screenTopLeft = new Vector2(selfScreenBottomLeft.x, Screen.height - selfScreenBottomLeft.y);
            panelPoint = RuntimePanelUtils.ScreenToPanel(_targetElement.panel, screenTopLeft);
            return IsFinite(panelPoint);
        }

        bool TryGetTargetPanelPoint(out Vector2 panelPoint)
        {
            panelPoint = default;

            if (_targetElement == null || _targetElement.panel == null)
                ResolveTargetElement();
            if (_targetElement == null || _targetElement.panel == null)
                return false;

            return TryGetAnchorPanelPoint(_targetElement, anchorMode, out panelPoint);
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

            if (!TryGetAnchorLocalPoint(element, mode, out var localPoint))
                return false;

            panelPoint = element.LocalToWorld(localPoint);
            return IsFinite(panelPoint);
        }

        static bool TryGetAnchorLocalPoint(VisualElement element, AnchorMode mode, out Vector2 localPoint)
        {
            localPoint = default;
            if (element == null)
                return false;

            var layout = element.layout;
            if (!IsFinite(layout.width) || !IsFinite(layout.height))
                return false;

            // Reject unresolved layouts. A zero-size layout looks "finite" but means
            // UITK has not computed the element yet; capturing off of it produces a
            // wrong anchor that causes a visible jump once the real size comes in.
            if (layout.width <= 0f || layout.height <= 0f)
                return false;

            var width = layout.width;
            var height = layout.height;
            localPoint = mode switch
            {
                AnchorMode.TopLeft => new Vector2(0f, 0f),
                AnchorMode.TopRight => new Vector2(width, 0f),
                AnchorMode.BottomLeft => new Vector2(0f, height),
                AnchorMode.BottomRight => new Vector2(width, height),
                _ => new Vector2(width * 0.5f, height * 0.5f)
            };
            return true;
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
