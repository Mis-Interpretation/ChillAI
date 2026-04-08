using System;
using ChillAI.Service.Platform;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// UIElements manipulator that drags the OS window by tracking
    /// screen-space cursor delta via Win32 GetCursorPos.
    /// Attach to any VisualElement (e.g. a panel header) to make it a drag handle.
    /// When dragThreshold > 0, short clicks invoke OnClicked instead of dragging.
    /// </summary>
    public class WindowDragManipulator : Manipulator
    {
        readonly IWindowService _windowService;
        readonly float _dragThreshold;
        bool _isPointerDown;
        bool _isDragging;
        int _dragStartCursorX, _dragStartCursorY;
        int _dragStartWindowX, _dragStartWindowY;

        /// <summary>Fires when pointer is released without exceeding the drag threshold.</summary>
        public event Action OnClicked;

        public WindowDragManipulator(IWindowService windowService, float dragThreshold = 0f)
        {
            _windowService = windowService;
            _dragThreshold = dragThreshold;
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

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            // Don't intercept clicks on child buttons (e.g. close button)
            if (evt.target is Button && evt.target != target) return;

            target.CapturePointer(evt.pointerId);

            var (cx, cy) = _windowService.GetCursorScreenPosition();
            var (wx, wy) = _windowService.GetWindowPosition();

            _dragStartCursorX = cx;
            _dragStartCursorY = cy;
            _dragStartWindowX = wx;
            _dragStartWindowY = wy;
            _isPointerDown = true;
            _isDragging = false;

            // No threshold: start dragging immediately
            if (_dragThreshold <= 0f)
            {
                _isDragging = true;
                evt.StopPropagation();
            }
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_isPointerDown || !target.HasPointerCapture(evt.pointerId)) return;

            var (cx, cy) = _windowService.GetCursorScreenPosition();
            int dx = cx - _dragStartCursorX;
            int dy = cy - _dragStartCursorY;

            if (!_isDragging)
            {
                if (dx * dx + dy * dy < _dragThreshold * _dragThreshold)
                    return;
                _isDragging = true;
            }

            _windowService.SetWindowPosition(_dragStartWindowX + dx, _dragStartWindowY + dy);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_isPointerDown) return;

            bool wasDrag = _isDragging;
            _isPointerDown = false;
            _isDragging = false;
            target.ReleasePointer(evt.pointerId);

            if (!wasDrag)
                OnClicked?.Invoke();
        }
    }
}
