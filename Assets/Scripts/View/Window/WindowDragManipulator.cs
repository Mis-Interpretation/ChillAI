using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// UIElements manipulator that drags one or more VisualElements within the panel
    /// by applying the same transform offset.
    /// When dragThreshold > 0, short clicks invoke OnClicked instead of dragging.
    /// Drag delta is derived from <see cref="Input.mousePosition"/> (uGUI convention) and
    /// converted per Unity docs for UI Toolkit via Y-flip before <see cref="RuntimePanelUtils.ScreenToPanel"/>.
    /// </summary>
    public class WindowDragManipulator : Manipulator
    {
        readonly VisualElement[] _moveTargets;
        readonly float _dragThreshold;

        bool _isPointerDown;
        bool _isDragging;
        Vector2 _panelPointerDown;
        Vector3[] _startPositions;
        Vector2 _mouseScreenStart;

        /// <summary>Fires when pointer is released without exceeding the drag threshold.</summary>
        public event Action OnClicked;

        /// <summary>Fires when a drag actually occurred (movement exceeded threshold, or immediate drag mode).</summary>
        public event Action DragEnded;

        /// <summary>Fires exactly once per drag sequence when dragging actually starts.</summary>
        public event Action DragStarted;

        public WindowDragManipulator(
            VisualElement moveTarget,
            float dragThreshold = 0f,
            VisualElement[] alsoMove = null)
        {
            _dragThreshold = dragThreshold;

            var list = new List<VisualElement> { moveTarget };
            if (alsoMove != null)
            {
                foreach (var e in alsoMove)
                {
                    if (e != null) list.Add(e);
                }
            }

            _moveTargets = list.ToArray();
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

        static Vector2 PointerPanelPosition(VisualElement from, Vector2 positionInTargetSpace)
        {
            var panel = from.panel;
            return from.ChangeCoordinatesTo(panel.visualTree, positionInTargetSpace);
        }

        /// <summary>
        /// Unity UI Toolkit expects this screen space before <see cref="RuntimePanelUtils.ScreenToPanel"/>
        /// (see Unity 6 docs: flip Y relative to <see cref="Input.mousePosition"/>).
        /// </summary>
        static Vector2 ScreenPositionForUIToolkit(Vector2 screenBottomLeftOrigin)
        {
            return new Vector2(screenBottomLeftOrigin.x, Screen.height - screenBottomLeftOrigin.y);
        }

        static Vector2 PointerPanelDeltaFromMouse(IPanel panel, Vector2 mouseScreenStart, Vector2 mouseScreenNow)
        {
            var a = ScreenPositionForUIToolkit(mouseScreenStart);
            var b = ScreenPositionForUIToolkit(mouseScreenNow);
            return RuntimePanelUtils.ScreenToPanel(panel, b) - RuntimePanelUtils.ScreenToPanel(panel, a);
        }

        static Vector3 ClampTransformPositionToPanel(
            VisualElement element,
            Vector3 desiredPosition,
            VisualElement clampReference)
        {
            var root = element?.panel?.visualTree;
            if (root == null) return desiredPosition;

            var viewport = root.worldBound;
            if (viewport.width <= 0f || viewport.height <= 0f)
                return desiredPosition;

#pragma warning disable CS0618
            var current = element.transform.position;
#pragma warning restore CS0618

            var dx = desiredPosition.x - current.x;
            var dy = desiredPosition.y - current.y;

            // Fullscreen wrappers (menu HUD roots) should not be constrained by their own bounds,
            // otherwise any movement is immediately snapped back. In that case clamp by drag handle.
            var basisBounds = element.worldBound;
            bool isBasisValid = basisBounds.width > 0f && basisBounds.height > 0f;
            bool basisIsFullscreenLike = isBasisValid &&
                                         basisBounds.width >= viewport.width - 0.5f &&
                                         basisBounds.height >= viewport.height - 0.5f;

            if (!isBasisValid || basisIsFullscreenLike)
            {
                if (clampReference != null && clampReference.panel == element.panel)
                {
                    var refBounds = clampReference.worldBound;
                    if (refBounds.width > 0f && refBounds.height > 0f)
                    {
                        basisBounds = refBounds;
                        isBasisValid = true;
                    }
                }

                if (!isBasisValid)
                    return desiredPosition;
            }

            var moved = new Rect(basisBounds.x + dx, basisBounds.y + dy, basisBounds.width, basisBounds.height);

            float adjustX;
            if (moved.width > viewport.width)
                adjustX = viewport.xMin - moved.xMin;
            else if (moved.xMin < viewport.xMin)
                adjustX = viewport.xMin - moved.xMin;
            else if (moved.xMax > viewport.xMax)
                adjustX = viewport.xMax - moved.xMax;
            else
                adjustX = 0f;

            float adjustY;
            if (moved.height > viewport.height)
                adjustY = viewport.yMin - moved.yMin;
            else if (moved.yMin < viewport.yMin)
                adjustY = viewport.yMin - moved.yMin;
            else if (moved.yMax > viewport.yMax)
                adjustY = viewport.yMax - moved.yMax;
            else
                adjustY = 0f;

            return new Vector3(desiredPosition.x + adjustX, desiredPosition.y + adjustY, desiredPosition.z);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            if (evt.target is UnityEngine.UIElements.Button uieBtn && uieBtn != target) return;

            var panel = target.panel;
            if (panel?.visualTree == null) return;

            target.CapturePointer(evt.pointerId);

            _panelPointerDown = PointerPanelPosition(target, evt.position);
            _mouseScreenStart = Input.mousePosition;

#pragma warning disable CS0618
            _startPositions = new Vector3[_moveTargets.Length];
            for (var i = 0; i < _moveTargets.Length; i++)
                _startPositions[i] = _moveTargets[i].transform.position;
#pragma warning restore CS0618

            _isPointerDown = true;
            _isDragging = false;

            if (_dragThreshold <= 0f)
            {
                _isDragging = true;
                DragStarted?.Invoke();
                evt.StopPropagation();
            }
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_isPointerDown || !target.HasPointerCapture(evt.pointerId)) return;

            var panel = target.panel;
            if (panel?.visualTree == null) return;

            var panelNow = PointerPanelPosition(target, evt.position);
            var panelDeltaForThreshold = panelNow - _panelPointerDown;

            if (!_isDragging)
            {
                if (panelDeltaForThreshold.sqrMagnitude < _dragThreshold * _dragThreshold)
                    return;
                _isDragging = true;
                DragStarted?.Invoke();
            }

            var mouseNow = Input.mousePosition;

            // One delta for every UITK root: separate UIDocument panels can map the same mouse motion to
            // slightly different vectors — using per-panel d made menu / chat / task drift apart while dragging.
            var refPanel = _moveTargets[0].panel;
            var dShared = refPanel != null
                ? PointerPanelDeltaFromMouse(refPanel, _mouseScreenStart, mouseNow)
                : Vector2.zero;

            for (var i = 0; i < _moveTargets.Length; i++)
            {
                var t = _moveTargets[i];
                if (t.panel == null) continue;

                var s = _startPositions[i];
                var desired = new Vector3(s.x + dShared.x, s.y + dShared.y, s.z);
                var clamped = ClampTransformPositionToPanel(t, desired, target);
#pragma warning disable CS0618
                t.transform.position = clamped;
#pragma warning restore CS0618
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_isPointerDown) return;

            bool wasDrag = _isDragging;
            _isPointerDown = false;
            _isDragging = false;
            target.ReleasePointer(evt.pointerId);

            if (wasDrag)
                DragEnded?.Invoke();
            else
                OnClicked?.Invoke();
        }
    }
}
