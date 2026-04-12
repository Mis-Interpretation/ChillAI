using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// Top-left resize handle: the right/bottom edges stay fixed; dragging moves the left/top edge,
    /// expanding or contracting width/height inversely.
    /// </summary>
    public sealed class PanelResizeManipulator : Manipulator
    {
        readonly VisualElement _panel;
        readonly float _minW;
        readonly float _minH;
        readonly Action _onEnded;

        bool _down;
        Vector2 _mousePanelStart;

        // Geometry captured at pointer-down (all in parent-local space, transform already zeroed).
        float _startLeft;
        float _startTop;
        float _startW;
        float _startH;
        float _fixedRight;   // startLeft + startW — stays constant during the drag
        float _fixedBottom;  // startTop  + startH

        public PanelResizeManipulator(VisualElement panel, float minW, float minH, Action onEnded = null)
        {
            _panel = panel;
            _minW = minW;
            _minH = minH;
            _onEnded = onEnded;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        }

        static Vector2 ToUIToolkitScreen(Vector2 unityScreen)
            => new Vector2(unityScreen.x, Screen.height - unityScreen.y);

        static Vector2 MouseToPanelPos(IPanel panel, Vector2 mouseScreen)
        {
            if (panel?.visualTree == null) return Vector2.zero;
            return RuntimePanelUtils.ScreenToPanel(panel, ToUIToolkitScreen(mouseScreen));
        }

        /// <summary>
        /// Pins the panel to left/top style coordinates (clears right/bottom/transform),
        /// then returns the resolved left and top in parent-local space.
        /// </summary>
        static (float left, float top) PinToLeftTop(VisualElement panel)
        {
            var parent = panel.parent;
            if (parent == null) return (0, 0);

            var wb = panel.worldBound;
            var tl = parent.WorldToLocal(new Vector3(wb.xMin, wb.yMin, 0f));

            panel.style.position = Position.Absolute;
            panel.style.bottom = StyleKeyword.Auto;
            panel.style.right = StyleKeyword.Auto;
            panel.style.left = tl.x;
            panel.style.top = tl.y;
            panel.style.width = Mathf.Max(wb.width, 1f);
            panel.style.height = Mathf.Max(wb.height, 1f);
#pragma warning disable CS0618
            panel.transform.position = Vector3.zero;
#pragma warning restore CS0618
            panel.style.translate = new StyleTranslate(new Translate(0f, 0f));

            return (tl.x, tl.y);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            var uitkPanel = _panel.panel;
            if (uitkPanel?.visualTree == null) return;

            (_startLeft, _startTop) = PinToLeftTop(_panel);

            _startW = _panel.resolvedStyle.width;
            _startH = _panel.resolvedStyle.height;
            if (float.IsNaN(_startW) || _startW < 1f) _startW = _panel.layout.width;
            if (float.IsNaN(_startH) || _startH < 1f) _startH = _panel.layout.height;

            _fixedRight  = _startLeft + _startW;
            _fixedBottom = _startTop  + _startH;

            _mousePanelStart = MouseToPanelPos(uitkPanel, Input.mousePosition);

            target.CapturePointer(evt.pointerId);
            _down = true;
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_down || !target.HasPointerCapture(evt.pointerId)) return;

            var uitkPanel = _panel.panel;
            if (uitkPanel?.visualTree == null) return;

            // Cumulative delta from the original mouse-down position avoids floating-point drift.
            var mousePanelNow = MouseToPanelPos(uitkPanel, Input.mousePosition);
            var d = mousePanelNow - _mousePanelStart;

            // Left edge moves with drag; width shrinks/grows to keep right edge fixed.
            float newLeft = _startLeft + d.x;
            float newW    = _fixedRight - newLeft;
            if (newW < _minW) { newW = _minW; newLeft = _fixedRight - _minW; }

            // Top edge moves with drag; height shrinks/grows to keep bottom edge fixed.
            float newTop = _startTop + d.y;
            float newH   = _fixedBottom - newTop;
            if (newH < _minH) { newH = _minH; newTop = _fixedBottom - _minH; }

            _panel.style.left   = newLeft;
            _panel.style.top    = newTop;
            _panel.style.width  = newW;
            _panel.style.height = newH;

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_down) return;
            _down = false;
            target.ReleasePointer(evt.pointerId);
            _onEnded?.Invoke();
        }
    }
}
