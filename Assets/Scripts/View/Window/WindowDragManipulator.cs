using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// UIElements manipulator that drags a VisualElement within the panel
    /// by applying a transform offset. Attach to a header element; pass the
    /// panel/container you want to move as moveTarget.
    /// When dragThreshold > 0, short clicks invoke OnClicked instead of dragging.
    /// </summary>
    public class WindowDragManipulator : Manipulator
    {
        readonly VisualElement _moveTarget;
        readonly float _dragThreshold;
        bool _isPointerDown;
        bool _isDragging;
        Vector2 _pointerStart;
        Vector3 _startPos;

        /// <summary>Fires when pointer is released without exceeding the drag threshold.</summary>
        public event Action OnClicked;

        public WindowDragManipulator(VisualElement moveTarget, float dragThreshold = 0f)
        {
            _moveTarget = moveTarget;
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

            _pointerStart = evt.position;
            _startPos = _moveTarget.transform.position;
            _isPointerDown = true;
            _isDragging = false;

            if (_dragThreshold <= 0f)
            {
                _isDragging = true;
                evt.StopPropagation();
            }
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_isPointerDown || !target.HasPointerCapture(evt.pointerId)) return;

            float dx = evt.position.x - _pointerStart.x;
            float dy = evt.position.y - _pointerStart.y;

            if (!_isDragging)
            {
                if (dx * dx + dy * dy < _dragThreshold * _dragThreshold)
                    return;
                _isDragging = true;
            }

            _moveTarget.transform.position = new Vector3(
                _startPos.x + dx,
                _startPos.y + dy,
                _startPos.z);
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
