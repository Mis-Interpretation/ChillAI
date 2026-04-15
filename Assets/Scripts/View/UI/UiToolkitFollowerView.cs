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
        [SerializeField] bool keepInitialRelativeOffset = true;
        [SerializeField] bool hideWhenTargetMissing = true;
        [SerializeField] bool followEvenWhenInactive = true;

        RectTransform _selfRect;
        Canvas _canvas;
        Graphic _selfGraphic;
        CanvasRenderer _canvasRenderer;
        VisualElement _targetElement;
        bool _hasCapturedInitialOffset;
        Vector3 _initialWorldOffset;

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
        }

        /// <summary>
        /// Re-captures the current relative offset between this UGUI element and the target UITK element.
        /// Useful after manual runtime adjustments.
        /// </summary>
        public void RecalibrateNow()
        {
            _hasCapturedInitialOffset = false;

            if (!TryGetTargetWorldPosition(out var targetWorld))
                return;

            _initialWorldOffset = _selfRect.position - targetWorld;
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

            if (!TryGetTargetWorldPosition(out var targetWorld))
            {
                SetVisible(false);
                return;
            }

            ApplyToCanvas(targetWorld, keepInitialRelativeOffset);
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

        bool TryGetTargetWorldPosition(out Vector3 targetWorld)
        {
            targetWorld = default;

            if (_targetElement == null || _targetElement.panel == null)
                ResolveTargetElement();

            if (_targetElement == null || _targetElement.panel == null)
                return false;

            if (_canvas == null)
                ResolveCanvasRefs();

            if (_canvas == null)
                return false;

            var panelPoint = GetAnchorPanelPoint(_targetElement.worldBound, anchorMode);
            var screenTopLeft = PanelToScreenTopLeft(_targetElement.panel, panelPoint);
            var screenBottomLeft = new Vector2(screenTopLeft.x, Screen.height - screenTopLeft.y) + screenOffset;

            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            var parentRect = _selfRect.parent as RectTransform;
            if (parentRect == null)
                return false;

            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, screenBottomLeft, camera, out targetWorld))
                return false;

            return true;
        }

        void ApplyToCanvas(Vector3 targetWorld, bool applyInitialOffset)
        {
            if (applyInitialOffset)
            {
                if (!_hasCapturedInitialOffset)
                {
                    _initialWorldOffset = _selfRect.position - targetWorld;
                    _hasCapturedInitialOffset = true;
                }
            }

            _selfRect.position = applyInitialOffset
                ? targetWorld + _initialWorldOffset
                : targetWorld;
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

        static Vector2 GetAnchorPanelPoint(Rect rect, AnchorMode mode)
        {
            return mode switch
            {
                AnchorMode.TopLeft => new Vector2(rect.xMin, rect.yMin),
                AnchorMode.TopRight => new Vector2(rect.xMax, rect.yMin),
                AnchorMode.BottomLeft => new Vector2(rect.xMin, rect.yMax),
                AnchorMode.BottomRight => new Vector2(rect.xMax, rect.yMax),
                _ => rect.center
            };
        }

        static Vector2 PanelToScreenTopLeft(IPanel panel, Vector2 panelPoint)
        {
            // Derive inverse mapping from ScreenToPanel to stay compatible with Unity versions
            // where RuntimePanelUtils.PanelToScreen is unavailable.
            var panelAt0 = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            var panelAtX = RuntimePanelUtils.ScreenToPanel(panel, Vector2.right);
            var panelAtY = RuntimePanelUtils.ScreenToPanel(panel, Vector2.up);

            var dx = panelAtX.x - panelAt0.x;
            var dy = panelAtY.y - panelAt0.y;

            if (Mathf.Abs(dx) < 0.0001f || Mathf.Abs(dy) < 0.0001f)
                return panelPoint;

            var sx = (panelPoint.x - panelAt0.x) / dx;
            var sy = (panelPoint.y - panelAt0.y) / dy;
            return new Vector2(sx, sy);
        }
    }
}
