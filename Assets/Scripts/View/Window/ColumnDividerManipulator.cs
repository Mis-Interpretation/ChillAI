using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// Horizontal drag-to-resize manipulator for a vertical column divider.
    /// Dragging left/right changes <see cref="colLeft"/> width, clamped to
    /// [minRatio, maxRatio] * columnsContainer.resolvedStyle.width.
    /// </summary>
    public sealed class ColumnDividerManipulator : Manipulator
    {
        readonly VisualElement _colLeft;
        readonly VisualElement _columns;
        readonly Func<float> _getMinRatio;
        readonly Func<float> _getMaxRatio;
        readonly Action _onDragEnded;

        bool _down;
        float _startPanelX;
        float _startColW;

        public ColumnDividerManipulator(
            VisualElement colLeft,
            VisualElement columns,
            Func<float> getMinRatio,
            Func<float> getMaxRatio,
            Action onDragEnded = null)
        {
            _colLeft      = colLeft;
            _columns      = columns;
            _getMinRatio  = getMinRatio;
            _getMaxRatio  = getMaxRatio;
            _onDragEnded  = onDragEnded;
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

        static float MouseToPanelX(IPanel panel, Vector2 mouseScreen)
        {
            if (panel?.visualTree == null) return 0f;
            var s = new Vector2(mouseScreen.x, Screen.height - mouseScreen.y);
            return RuntimePanelUtils.ScreenToPanel(panel, s).x;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            var p = target.panel;
            if (p?.visualTree == null) return;

            _startPanelX = MouseToPanelX(p, Input.mousePosition);
            _startColW   = _colLeft.resolvedStyle.width;
            if (float.IsNaN(_startColW) || _startColW < 1f)
                _startColW = _colLeft.layout.width;

            target.CapturePointer(evt.pointerId);
            _down = true;
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_down || !target.HasPointerCapture(evt.pointerId)) return;

            var p = target.panel;
            if (p?.visualTree == null) return;

            float nowX = MouseToPanelX(p, Input.mousePosition);
            float dx   = nowX - _startPanelX;

            float colsW  = _columns.resolvedStyle.width;
            if (colsW <= 0f) colsW = _columns.layout.width;

            float minW = _getMinRatio() * colsW;
            float maxW = _getMaxRatio() * colsW;
            _colLeft.style.width = Mathf.Clamp(_startColW + dx, minW, maxW);

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_down) return;
            _down = false;
            target.ReleasePointer(evt.pointerId);
            _onDragEnded?.Invoke();
        }
    }
}
