using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace ChillAI.View.Window
{
    /// <summary>
    /// UIElements manipulator that drags one or more VisualElements within the panel
    /// by applying the same transform offset. Optional UGUI RectTransforms can move
    /// in lockstep using screen-to-local conversion (correct when mixing UI Toolkit + uGUI).
    /// When dragThreshold > 0, short clicks invoke OnClicked instead of dragging.
    /// Drag delta is derived from <see cref="Input.mousePosition"/> (uGUI convention) and
    /// converted per Unity docs for UI Toolkit via Y-flip before <see cref="RuntimePanelUtils.ScreenToPanel"/>.
    /// </summary>
    public class WindowDragManipulator : Manipulator
    {
        readonly VisualElement[] _moveTargets;
        readonly RectTransform[] _uguiTargets;
        readonly float _dragThreshold;

        bool _isPointerDown;
        bool _isDragging;
        Vector2 _panelPointerDown;
        Vector3[] _startPositions;
        Vector2 _mouseScreenStart;
        Vector2[] _uguiStartAnchored;

        /// <summary>Fires when pointer is released without exceeding the drag threshold.</summary>
        public event Action OnClicked;

        public WindowDragManipulator(
            VisualElement moveTarget,
            float dragThreshold = 0f,
            VisualElement[] alsoMove = null,
            RectTransform[] uguiAlsoMove = null)
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

            if (uguiAlsoMove != null && uguiAlsoMove.Length > 0)
            {
                var ug = new List<RectTransform>();
                foreach (var r in uguiAlsoMove)
                {
                    if (r != null) ug.Add(r);
                }

                _uguiTargets = ug.Count > 0 ? ug.ToArray() : null;
            }
            else
            {
                _uguiTargets = null;
            }
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

        static Camera GetCanvasCamera(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
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

            if (_uguiTargets != null)
            {
                _uguiStartAnchored = new Vector2[_uguiTargets.Length];
                for (var i = 0; i < _uguiTargets.Length; i++)
                    _uguiStartAnchored[i] = _uguiTargets[i].anchoredPosition;
            }
            else
            {
                _uguiStartAnchored = null;
            }

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

            var panel = target.panel;
            if (panel?.visualTree == null) return;

            var panelNow = PointerPanelPosition(target, evt.position);
            var panelDeltaForThreshold = panelNow - _panelPointerDown;

            if (!_isDragging)
            {
                if (panelDeltaForThreshold.sqrMagnitude < _dragThreshold * _dragThreshold)
                    return;
                _isDragging = true;
            }

            var mouseNow = Input.mousePosition;

            for (var i = 0; i < _moveTargets.Length; i++)
            {
                var t = _moveTargets[i];
                var p = t.panel;
                if (p == null) continue;

                var d = PointerPanelDeltaFromMouse(p, _mouseScreenStart, mouseNow);
                var s = _startPositions[i];
#pragma warning disable CS0618
                t.transform.position = new Vector3(s.x + d.x, s.y + d.y, s.z);
#pragma warning restore CS0618
            }

            if (_uguiStartAnchored != null)
            {
                for (var i = 0; i < _uguiTargets.Length; i++)
                {
                    var rt = _uguiTargets[i];
                    var parent = rt.parent as RectTransform;
                    if (parent == null) continue;

                    var cam = GetCanvasCamera(rt);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parent, _mouseScreenStart, cam, out var lp0);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parent, mouseNow, cam, out var lp1);

                    rt.anchoredPosition = _uguiStartAnchored[i] + (lp1 - lp0);
                }
            }
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
