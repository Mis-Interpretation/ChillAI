using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChillAI.View.TaskUI
{
    public enum DragItemType { BigEvent, SubTask }

    public struct DragItemInfo
    {
        public DragItemType Type;
        public string ItemId;
        public string ParentBigEventId;
    }

    public enum DropTargetType
    {
        None,
        ReorderSubTask,
        ReorderBigEvent,
        PromoteToList,
        MergeSubTaskIntoBigEvent,
        DemoteToSubTask
    }

    public struct DropTarget
    {
        public DropTargetType Type;
        public int InsertIndex;
        public string TargetBigEventId;
        // Visual feedback positioning (panel coordinates)
        public float IndicatorY;
        public float IndicatorLeft;
        public float IndicatorWidth;
        /// <summary>Row height when <see cref="IsRowMergeHighlight"/>; ignored for insert line.</summary>
        public float IndicatorHeight;
        /// <summary>True: highlight whole row (merge into folder). False: horizontal insert line.</summary>
        public bool IsRowMergeHighlight;
    }

    public class TaskItemDragManipulator : Manipulator
    {
        readonly Func<DragItemInfo> _getItemInfo;
        readonly Func<Vector2, DragItemInfo, DropTarget> _resolveDropTarget;
        readonly Action<DragItemInfo, DropTarget> _onDragEnd;
        readonly Action _onDragStarted;
        readonly VisualElement _overlayRoot;
        readonly long _longPressMs;

        const float CancelDistancePx = 5f;

        enum State { Idle, Waiting, Dragging }

        State _state = State.Idle;
        int _pointerId;
        Vector2 _pointerStartPanel;
        IVisualElementScheduledItem _longPressSchedule;

        // Drag visuals
        VisualElement _ghost;
        VisualElement _indicator;
        DragItemInfo _dragInfo;

        public TaskItemDragManipulator(
            Func<DragItemInfo> getItemInfo,
            Func<Vector2, DragItemInfo, DropTarget> resolveDropTarget,
            Action<DragItemInfo, DropTarget> onDragEnd,
            Action onDragStarted,
            VisualElement overlayRoot,
            long longPressMs = 300)
        {
            _getItemInfo = getItemInfo;
            _resolveDropTarget = resolveDropTarget;
            _onDragEnd = onDragEnd;
            _onDragStarted = onDragStarted;
            _overlayRoot = overlayRoot;
            _longPressMs = Math.Max(50L, Math.Min(3000L, longPressMs));
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _state != State.Idle) return;

            // Don't start drag from delete buttons
            if (evt.target is Button) return;

            _pointerId = evt.pointerId;
            _pointerStartPanel = evt.position;
            _state = State.Waiting;

            _longPressSchedule = target.schedule.Execute(OnLongPress).StartingIn(_longPressMs);

            // Do NOT capture pointer or stop propagation — let click events fire normally
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId) return;

            var panelPos = evt.position;

            if (_state == State.Waiting)
            {
                // If moved too far before long press, cancel
                if (Vector2.Distance(panelPos, _pointerStartPanel) > CancelDistancePx)
                    CancelWait();
                return;
            }

            if (_state == State.Dragging)
            {
                UpdateGhostPosition(panelPos);
                UpdateDropIndicator(panelPos);
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId) return;

            if (_state == State.Waiting)
            {
                CancelWait();
                // Normal click — do nothing, let event propagate
                return;
            }

            if (_state == State.Dragging)
            {
                var panelPos = evt.position;
                var dropTarget = _resolveDropTarget(panelPos, _dragInfo);

                Cleanup();
                target.ReleasePointer(evt.pointerId);

                if (dropTarget.Type != DropTargetType.None)
                    _onDragEnd?.Invoke(_dragInfo, dropTarget);
            }
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_state == State.Dragging)
                Cleanup();
        }

        void OnLongPress()
        {
            if (_state != State.Waiting) return;

            _dragInfo = _getItemInfo();
            _state = State.Dragging;

            _onDragStarted?.Invoke();

            target.CapturePointer(_pointerId);
            target.AddToClassList("drag-source-dimmed");

            CreateGhost();
            CreateIndicator();
            UpdateGhostPosition(_pointerStartPanel);
        }

        void CancelWait()
        {
            _longPressSchedule?.Pause();
            _longPressSchedule = null;
            _state = State.Idle;
        }

        void CreateGhost()
        {
            _ghost = new VisualElement();
            _ghost.AddToClassList("drag-ghost");
            _ghost.pickingMode = PickingMode.Ignore;

            // Get text from the dragged item
            string text;
            if (_dragInfo.Type == DragItemType.BigEvent)
            {
                var label = target.Q<Label>(className: "list-item-title");
                text = label?.text ?? "";
            }
            else
            {
                var label = target.Q<Label>(className: "sub-task-title");
                text = label?.text ?? "";
            }

            var ghostLabel = new Label(text);
            ghostLabel.AddToClassList("drag-ghost-label");
            _ghost.Add(ghostLabel);

            _overlayRoot.Add(_ghost);
        }

        void CreateIndicator()
        {
            _indicator = new VisualElement();
            _indicator.AddToClassList("drop-indicator");
            _indicator.pickingMode = PickingMode.Ignore;
            _indicator.style.display = DisplayStyle.None;
            _overlayRoot.Add(_indicator);
        }

        void UpdateGhostPosition(Vector2 panelPos)
        {
            if (_ghost == null) return;
            var localPos = ToOverlayLocal(panelPos);
            _ghost.style.left = localPos.x + 8;
            _ghost.style.top = localPos.y + 8;
        }

        void UpdateDropIndicator(Vector2 panelPos)
        {
            if (_indicator == null) return;
            var dropTarget = _resolveDropTarget(panelPos, _dragInfo);

            if (dropTarget.Type == DropTargetType.None || dropTarget.IndicatorWidth <= 0)
            {
                _indicator.style.display = DisplayStyle.None;
                return;
            }

            _indicator.style.display = DisplayStyle.Flex;
            var localIndicator = ToOverlayLocal(new Vector2(dropTarget.IndicatorLeft, dropTarget.IndicatorY));
            _indicator.style.left = localIndicator.x;
            _indicator.style.top = localIndicator.y;
            _indicator.style.width = dropTarget.IndicatorWidth;

            if (dropTarget.IsRowMergeHighlight && dropTarget.IndicatorHeight > 1f)
            {
                _indicator.AddToClassList("drop-indicator--merge");
                _indicator.style.height = dropTarget.IndicatorHeight;
            }
            else
            {
                _indicator.RemoveFromClassList("drop-indicator--merge");
                _indicator.style.height = 2f;
            }
        }

        void Cleanup()
        {
            _longPressSchedule?.Pause();
            _longPressSchedule = null;

            target.RemoveFromClassList("drag-source-dimmed");

            _ghost?.RemoveFromHierarchy();
            _ghost = null;

            _indicator?.RemoveFromHierarchy();
            _indicator = null;

            _state = State.Idle;
        }

        Vector2 ToOverlayLocal(Vector2 panelPos)
        {
            return target.panel.visualTree.ChangeCoordinatesTo(_overlayRoot, panelPos);
        }
    }
}
