using System;
using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Service.Layout;
using ChillAI.Service.Platform;
using ChillAI.View.Window;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.TaskUI
{
    [RequireComponent(typeof(UIDocument))]
    public class TaskPanelView : MonoBehaviour
    {
        SignalBus _signalBus;
        TaskDecompositionController _controller;
        ITaskDecompositionReader _taskReader;
        IConfigReader _configReader;
        IWindowService _windowService;
        AppSettings _appSettings;
        UiLayoutController _uiLayout;

        // UI Elements
        Button _toggleBtn;
        VisualElement _panel;
        Button _closeBtn;
        Label _statusLabel;
        ScrollView _listScroll;
        Button _addListBtn;
        ScrollView _taskScroll;

        bool _panelVisible;
        string _selectedBigEventId;

        // Chat-to-task badge
        Label _badge;
        int _pendingBadgeCount;
        IVisualElementScheduledItem _badgeHideSchedule;

        // Inline input (shared, only one active at a time)
        VisualElement _inlineInputRow;

        // Double-click tracking for big event titles
        float _lastListClickTime;
        string _lastListClickId;
        const float DoubleClickThreshold = 0.5f;
        /// <summary>Top/bottom fraction of a list row treated as "insert line" gaps; middle is merge-into.</summary>
        const float ListRowEdgeGapFraction = 0.24f;

        // Inline edit state
        TextField _activeEditField;
        Label _activeEditLabel;
        Action<string> _activeEditCallback;
        string _activeEditOriginalText;
        bool _isCommittingEdit;

        // Window drag
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;
        VisualElement _resizeHandle;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            TaskDecompositionController controller,
            ITaskDecompositionReader taskReader,
            IConfigReader configReader,
            IWindowService windowService,
            AppSettings appSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _controller = controller;
            _taskReader = taskReader;
            _configReader = configReader;
            _windowService = windowService;
            _appSettings = appSettings;
            _uiLayout = uiLayout;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _toggleBtn = root.Q<Button>("toggle-btn");
            _panel = root.Q<VisualElement>("panel");
            _closeBtn = root.Q<Button>("close-btn");
            _statusLabel = root.Q<Label>("status-label");
            _listScroll = root.Q<ScrollView>("list-scroll");
            _addListBtn = root.Q<Button>("add-list-btn");
            _taskScroll = root.Q<ScrollView>("task-scroll");

            _uiLayout.RegisterTaskHudRoot(root);
            _uiLayout.RegisterTaskPanel(_panel);
            _panelVisible = !_panel.ClassListContains("hidden");

            // Create badge overlay on toggle button
            _badge = new Label("+1");
            _badge.AddToClassList("toggle-badge");
            _badge.AddToClassList("hidden");
            _toggleBtn.Add(_badge);

            _toggleBtn.clicked += OnToggle;
            _closeBtn.clicked += OnClose;
            _addListBtn.clicked += OnAddListClicked;

            _panel.RegisterCallback<MouseDownEvent>(OnPanelMouseDown);

            var header = root.Q<VisualElement>(className: "panel-header");
            _dragManipulator = new WindowDragManipulator(_panel);
            header.AddManipulator(_dragManipulator);
            _dragManipulator.DragEnded += OnHudLayoutChanged;

            _resizeHandle = root.Q<VisualElement>("task-resize-handle");
            if (_resizeHandle != null)
            {
                _resizeManipulator = new PanelResizeManipulator(_panel, 320f, 220f, OnHudLayoutChanged);
                _resizeHandle.AddManipulator(_resizeManipulator);
            }

            _signalBus?.Subscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.Subscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);

            UpdateStatus();
            AutoSelectFirst();
            RebuildLeftColumn();
            RebuildRightColumn();

            // Pre-warm: show the panel for one frame so UIToolkit computes layout
            // on all children while the user hasn't opened it yet. This eliminates
            // the ~1 s freeze caused by display:none → visible layout pass on first open.
            _panel.RemoveFromClassList("hidden");
            _panel.schedule.Execute(() => _panel.AddToClassList("hidden"));
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _closeBtn.clicked -= OnClose;
            _addListBtn.clicked -= OnAddListClicked;
            _panel.UnregisterCallback<MouseDownEvent>(OnPanelMouseDown);

            var header = _panel.Q<VisualElement>(className: "panel-header");
            if (_dragManipulator != null)
            {
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            _uiLayout?.UnregisterTaskHudRoot();
            _uiLayout?.UnregisterTaskPanel();

            _signalBus?.TryUnsubscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.TryUnsubscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);
        }

        // ── Panel ──

        void OnToggle()
        {
            _panelVisible = !_panelVisible;

            if (_panelVisible)
            {
                _badge.AddToClassList("hidden");
                _pendingBadgeCount = 0;
                _badgeHideSchedule?.Pause();

                // Show a loading emoji this frame, then reveal the panel next frame.
                // Even after pre-warm, subsequent re-opens can still stall slightly;
                // the emoji gives immediate visual feedback before the layout pass.
                _toggleBtn.text = "⌛";
                _toggleBtn.schedule.Execute(() =>
                {
                    _panel.RemoveFromClassList("hidden");
                    _toggleBtn.text = "Todo";
                });
            }
            else
            {
                _panel.AddToClassList("hidden");
            }

            _uiLayout?.RequestSave();
        }

        void OnClose()
        {
            CommitEditMode();
            _panelVisible = false;
            _panel.AddToClassList("hidden");
            _toggleBtn.text = "Todo";
            _uiLayout?.RequestSave();
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void OnPanelMouseDown(MouseDownEvent evt)
        {
            var target = evt.target as VisualElement;

            // Dismiss active inline edit
            if (_activeEditField != null)
            {
                if (target == null || (!_activeEditField.Equals(target) && !_activeEditField.Contains(target)))
                    CommitEditMode();
            }

            // Dismiss active inline input (focus-out handles save)
        }

        // ── Selection ──

        void AutoSelectFirst()
        {
            _selectedBigEventId = _taskReader.BigEvents.Count > 0
                ? _taskReader.BigEvents[0].Id
                : null;
        }

        void SelectBigEvent(string id)
        {
            if (_selectedBigEventId == id) return;
            CommitEditMode();
            RemoveInlineInput();
            _selectedBigEventId = id;
            RebuildLeftColumn();
            RebuildRightColumn();
        }

        // ── Left Column (big events) ──

        void OnAddListClicked()
        {
            CommitEditMode();
            if (_inlineInputRow != null) return;
            ShowInlineInput(_listScroll, text =>
            {
                var id = _controller.CreateBigEvent(text);
                if (id != null)
                {
                    _selectedBigEventId = id;
                    RebuildLeftColumn();
                    RebuildRightColumn();
                }
            });
        }

        void RebuildLeftColumn()
        {
            _listScroll.Clear();

            foreach (var bigEvent in _taskReader.BigEvents)
            {
                var item = CreateListItem(bigEvent);
                _listScroll.Add(item);
            }
        }

        VisualElement CreateListItem(BigEvent bigEvent)
        {
            var row = new VisualElement();
            row.AddToClassList("list-item");
            row.userData = bigEvent.Id;
            if (bigEvent.Id == _selectedBigEventId)
                row.AddToClassList("list-item--selected");

            var title = new Label(bigEvent.Title);
            title.AddToClassList("list-item-title");

            var deleteBtn = new Button { text = "✕" };
            deleteBtn.AddToClassList("list-item-delete");

            row.Add(title);
            row.Add(deleteBtn);

            var bigEventId = bigEvent.Id;

            // Click row → select
            row.RegisterCallback<ClickEvent>(evt =>
            {
                var t = evt.target as VisualElement;
                if (t == deleteBtn || deleteBtn.Contains(t)) return;
                if (_activeEditField != null) return;
                SelectBigEvent(bigEventId);
            });

            // Double-click title → inline edit (manual 0.5s window)
            title.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                float now = Time.realtimeSinceStartup;
                if (_lastListClickId == bigEventId && now - _lastListClickTime < DoubleClickThreshold)
                {
                    _lastListClickId = null;
                    evt.StopPropagation();
                    EnterEditMode(title, evt.localMousePosition.x, "edit-field--big", newText =>
                    {
                        _controller.UpdateBigEventTitle(bigEventId, newText);
                    });
                }
                else
                {
                    _lastListClickId = bigEventId;
                    _lastListClickTime = now;
                }
            });

            deleteBtn.clicked += () => _controller.DeleteBigEvent(bigEventId);

            // Long-press drag
            var dragManip = new TaskItemDragManipulator(
                () => new DragItemInfo { Type = DragItemType.BigEvent, ItemId = bigEventId },
                ResolveDropTarget,
                HandleDragEnd,
                () => { CommitEditMode(); RemoveInlineInput(); },
                _panel,
                _appSettings.taskPanelDragLongPressMs);
            row.AddManipulator(dragManip);

            return row;
        }

        // ── Right Column (subtasks) ──

        void RebuildRightColumn()
        {
            _taskScroll.Clear();

            var selected = _selectedBigEventId != null
                ? _taskReader.GetBigEvent(_selectedBigEventId)
                : null;

            if (selected == null) return;

            // Loading
            if (selected.IsProcessing)
            {
                var loading = new Label("思考中...");
                loading.style.color = new Color(0.59f, 0.78f, 1f, 0.8f);
                loading.style.fontSize = 11;
                loading.style.unityFontStyleAndWeight = FontStyle.Italic;
                loading.style.paddingLeft = 6;
                loading.style.paddingTop = 4;
                _taskScroll.Add(loading);
            }

            // Error
            if (!string.IsNullOrEmpty(selected.ErrorMessage))
            {
                var error = new Label(selected.ErrorMessage);
                error.style.color = new Color(1f, 0.39f, 0.31f, 0.9f);
                error.style.fontSize = 11;
                error.style.whiteSpace = WhiteSpace.Normal;
                error.style.paddingLeft = 6;
                _taskScroll.Add(error);
            }

            // Subtasks
            foreach (var subTask in selected.SubTasks)
            {
                var item = CreateSubTaskElement(selected.Id, subTask);
                _taskScroll.Add(item);
            }

            // "＋ 新任务" button right after last task
            var addTaskBtn = new Button { text = "＋ 新任务" };
            addTaskBtn.AddToClassList("add-task-btn");
            var capturedId = selected.Id;
            addTaskBtn.clicked += () =>
            {
                CommitEditMode();
                if (_inlineInputRow != null) return;
                // Insert inline input just before the add-task button
                ShowInlineInputBefore(addTaskBtn, text =>
                {
                    _controller.AddSubTask(capturedId, text);
                });
            };
            _taskScroll.Add(addTaskBtn);
        }

        VisualElement CreateSubTaskElement(string bigEventId, SubTask subTask)
        {
            var row = new VisualElement();
            row.AddToClassList("sub-task-item");
            row.name = "subtask-" + subTask.Id;

            var checkbox = new Toggle();
            checkbox.AddToClassList("sub-task-checkbox");
            checkbox.value = subTask.IsCompleted;

            var label = new Label(subTask.Title);
            label.AddToClassList("sub-task-title");
            label.EnableInClassList("sub-task-title--completed", subTask.IsCompleted);

            var deleteBtn = new Button { text = "✕" };
            deleteBtn.AddToClassList("sub-task-delete");

            row.Add(checkbox);
            row.Add(label);
            row.Add(deleteBtn);

            var subTaskId = subTask.Id;

            checkbox.RegisterValueChangedCallback(_ =>
            {
                _controller.ToggleSubTask(bigEventId, subTaskId);
            });

            deleteBtn.clicked += () => _controller.DeleteSubTask(bigEventId, subTaskId);

            // Click label → inline edit
            label.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || _activeEditField != null) return;
                var localPos = label.WorldToLocal(evt.position);
                EnterEditMode(label, localPos.x, "edit-field--sub", newText =>
                {
                    _controller.UpdateSubTaskTitle(bigEventId, subTaskId, newText);
                });
            });

            // Long-press drag
            var dragManip = new TaskItemDragManipulator(
                () => new DragItemInfo { Type = DragItemType.SubTask, ItemId = subTaskId, ParentBigEventId = bigEventId },
                ResolveDropTarget,
                HandleDragEnd,
                () => { CommitEditMode(); RemoveInlineInput(); },
                _panel,
                _appSettings.taskPanelDragLongPressMs);
            row.AddManipulator(dragManip);

            return row;
        }

        // ── Inline Input ──

        void ShowInlineInput(ScrollView parent, Action<string> onSubmit)
        {
            CreateInlineInput(onSubmit);
            parent.Add(_inlineInputRow);
            _inlineInputRow.Q<TextField>().Focus();
        }

        void ShowInlineInputBefore(VisualElement sibling, Action<string> onSubmit)
        {
            CreateInlineInput(onSubmit);
            var parent = sibling.parent;
            var idx = parent.IndexOf(sibling);
            parent.Insert(idx, _inlineInputRow);
            _inlineInputRow.Q<TextField>().Focus();
        }

        void CreateInlineInput(Action<string> onSubmit)
        {
            _inlineInputRow = new VisualElement();
            _inlineInputRow.AddToClassList("inline-input-row");

            var input = new TextField();
            input.AddToClassList("inline-input");
            input.multiline = true;

            _inlineInputRow.Add(input);

            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
                {
                    evt.StopImmediatePropagation();
                    SubmitInlineInput(input, onSubmit);
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    RemoveInlineInput();
                }
            }, TrickleDown.TrickleDown);

            // Click elsewhere → save (same pattern as inline edit)
            input.RegisterCallback<FocusOutEvent>(_ =>
            {
                SubmitInlineInput(input, onSubmit);
            });
        }

        void SubmitInlineInput(TextField input, Action<string> onSubmit)
        {
            if (_inlineInputRow == null) return; // already submitted
            var text = input.value?.Trim();
            RemoveInlineInput();
            if (!string.IsNullOrEmpty(text))
                onSubmit(text);
        }

        void RemoveInlineInput()
        {
            if (_inlineInputRow == null) return;
            _inlineInputRow.RemoveFromHierarchy();
            _inlineInputRow = null;
        }

        // ── Inline Editing ──

        void EnterEditMode(Label label, float clickLocalX, string cssVariant, Action<string> onCommit)
        {
            if (_activeEditField != null)
                CommitEditMode();

            var parent = label.parent;
            var index = parent.IndexOf(label);
            var text = label.text;
            var fontSize = label.resolvedStyle.fontSize;

            _activeEditOriginalText = text;
            _activeEditLabel = label;
            _activeEditCallback = onCommit;

            label.style.display = DisplayStyle.None;

            var field = new TextField();
            field.AddToClassList("edit-field");
            field.AddToClassList(cssVariant);
            field.multiline = true;
            field.value = text;
            parent.Insert(index + 1, field);

            _activeEditField = field;

            field.schedule.Execute(() =>
            {
                field.Focus();
                int cursorIdx = EstimateCursorIndex(text, clickLocalX, fontSize);
                field.SelectRange(cursorIdx, cursorIdx);
            });

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
                {
                    evt.StopImmediatePropagation();
                    CommitEditMode();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopImmediatePropagation();
                    CancelEditMode();
                }
            }, TrickleDown.TrickleDown);

            field.RegisterCallback<FocusOutEvent>(_ => CommitEditMode());
        }

        void CommitEditMode()
        {
            if (_activeEditField == null || _isCommittingEdit) return;
            _isCommittingEdit = true;

            var newText = _activeEditField.value?.Trim();
            var changed = !string.IsNullOrEmpty(newText) && newText != _activeEditOriginalText;

            _activeEditLabel.style.display = DisplayStyle.Flex;
            if (changed)
            {
                _activeEditLabel.text = newText;
                _activeEditCallback?.Invoke(newText);
            }

            _activeEditField.RemoveFromHierarchy();
            _activeEditField = null;
            _activeEditLabel = null;
            _activeEditCallback = null;
            _activeEditOriginalText = null;
            _isCommittingEdit = false;
        }

        void CancelEditMode()
        {
            if (_activeEditField == null || _isCommittingEdit) return;
            _isCommittingEdit = true;

            _activeEditLabel.style.display = DisplayStyle.Flex;
            _activeEditField.RemoveFromHierarchy();
            _activeEditField = null;
            _activeEditLabel = null;
            _activeEditCallback = null;
            _activeEditOriginalText = null;
            _isCommittingEdit = false;
        }

        static int EstimateCursorIndex(string text, float clickX, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float x = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                float charWidth = text[i] < 128 ? fontSize * 0.55f : fontSize;
                if (x + charWidth * 0.5f > clickX)
                    return i;
                x += charWidth;
            }
            return text.Length;
        }

        // ── Signal Handlers ──

        void OnBigEventChanged(BigEventChangedSignal signal)
        {
            switch (signal.ChangeType)
            {
                case BigEventChangeType.Added:
                    RebuildLeftColumn();
                    break;

                case BigEventChangeType.BigEventsReordered:
                    RebuildLeftColumn();
                    break;

                case BigEventChangeType.Removed:
                    if (signal.BigEventId == _selectedBigEventId)
                        AutoSelectFirst();
                    RebuildLeftColumn();
                    RebuildRightColumn();
                    break;

                case BigEventChangeType.SubTaskAdded:
                case BigEventChangeType.SubTaskRemoved:
                case BigEventChangeType.SubTaskReordered:
                    if (signal.BigEventId == _selectedBigEventId)
                        RebuildRightColumn();
                    break;
            }
        }

        void OnSubTaskCompletionChanged(SubTaskCompletionChangedSignal signal)
        {
            if (signal.BigEventId != _selectedBigEventId) return;

            var subtaskRow = _taskScroll.Q<VisualElement>("subtask-" + signal.SubTaskId);
            if (subtaskRow != null)
            {
                var checkbox = subtaskRow.Q<Toggle>();
                checkbox?.SetValueWithoutNotify(signal.IsCompleted);

                var label = subtaskRow.Q<Label>(className: "sub-task-title");
                label?.EnableInClassList("sub-task-title--completed", signal.IsCompleted);
            }
        }

        void OnTaskAddedViaChat(TaskAddedViaChatSignal signal)
        {
            if (_panelVisible) return;

            _pendingBadgeCount++;
            _badge.text = $"+{_pendingBadgeCount}";
            _badge.RemoveFromClassList("hidden");

            _badgeHideSchedule?.Pause();
            _badgeHideSchedule = _badge.schedule.Execute(() =>
            {
                _badge.AddToClassList("hidden");
                _pendingBadgeCount = 0;
            }).StartingIn(5000);
        }

        // ── Drag and Drop ──

        DropTarget ResolveDropTarget(Vector2 panelPos, DragItemInfo info)
        {
            var listBounds = _listScroll.worldBound;
            var taskBounds = _taskScroll.worldBound;

            // SubTask dragged to left column → merge onto row vs promote in gap
            if (info.Type == DragItemType.SubTask && listBounds.Contains(panelPos))
                return ResolveSubTaskLeftColumnDrop(panelPos, info, listBounds);

            // BigEvent dragged to right column → demote
            if (info.Type == DragItemType.BigEvent && taskBounds.Contains(panelPos))
            {
                if (_selectedBigEventId != null && _selectedBigEventId != info.ItemId)
                {
                    float indicatorY = CalcIndicatorY(_taskScroll, panelPos.y, "sub-task-item");
                    return new DropTarget
                    {
                        Type = DropTargetType.DemoteToSubTask,
                        TargetBigEventId = _selectedBigEventId,
                        IndicatorY = indicatorY,
                        IndicatorLeft = taskBounds.x,
                        IndicatorWidth = taskBounds.width
                    };
                }
                return new DropTarget { Type = DropTargetType.None };
            }

            // BigEvent dragged within left column → reorder list
            if (info.Type == DragItemType.BigEvent && listBounds.Contains(panelPos))
            {
                int insertIndex;
                float indicatorY;
                CalcInsertIndexAndIndicator(_listScroll, panelPos.y, "list-item",
                    out insertIndex, out indicatorY);
                return new DropTarget
                {
                    Type = DropTargetType.ReorderBigEvent,
                    InsertIndex = insertIndex,
                    IndicatorY = indicatorY,
                    IndicatorLeft = listBounds.x,
                    IndicatorWidth = listBounds.width
                };
            }

            // SubTask dragged within right column → reorder
            if (info.Type == DragItemType.SubTask && taskBounds.Contains(panelPos))
            {
                int insertIndex;
                float indicatorY;
                CalcInsertIndexAndIndicator(_taskScroll, panelPos.y, "sub-task-item",
                    out insertIndex, out indicatorY);
                return new DropTarget
                {
                    Type = DropTargetType.ReorderSubTask,
                    InsertIndex = insertIndex,
                    TargetBigEventId = info.ParentBigEventId,
                    IndicatorY = indicatorY,
                    IndicatorLeft = taskBounds.x,
                    IndicatorWidth = taskBounds.width
                };
            }

            return new DropTarget { Type = DropTargetType.None };
        }

        DropTarget ResolveSubTaskLeftColumnDrop(Vector2 panelPos, DragItemInfo info, Rect listBounds)
        {
            var container = _listScroll.contentContainer;

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container[i];
                if (!child.ClassListContains("list-item")) continue;

                var b = child.worldBound;
                if (!b.Contains(panelPos)) continue;

                float rowT = (panelPos.y - b.y) / Mathf.Max(b.height, 1f);
                int rowListIndex = CountListItemsBefore(container, child);

                if (rowT < ListRowEdgeGapFraction)
                {
                    return new DropTarget
                    {
                        Type = DropTargetType.PromoteToList,
                        InsertIndex = rowListIndex,
                        IndicatorY = b.y,
                        IndicatorLeft = listBounds.x,
                        IndicatorWidth = listBounds.width
                    };
                }

                if (rowT > 1f - ListRowEdgeGapFraction)
                {
                    return new DropTarget
                    {
                        Type = DropTargetType.PromoteToList,
                        InsertIndex = rowListIndex + 1,
                        IndicatorY = b.yMax,
                        IndicatorLeft = listBounds.x,
                        IndicatorWidth = listBounds.width
                    };
                }

                var targetId = child.userData as string;
                if (string.IsNullOrEmpty(targetId) || targetId == info.ParentBigEventId)
                    return new DropTarget { Type = DropTargetType.None };

                return new DropTarget
                {
                    Type = DropTargetType.MergeSubTaskIntoBigEvent,
                    TargetBigEventId = targetId,
                    IndicatorLeft = b.x,
                    IndicatorY = b.y,
                    IndicatorWidth = b.width,
                    IndicatorHeight = b.height,
                    IsRowMergeHighlight = true
                };
            }

            int insertIndex;
            float indicatorY;
            CalcInsertIndexAndIndicator(_listScroll, panelPos.y, "list-item",
                out insertIndex, out indicatorY);
            return new DropTarget
            {
                Type = DropTargetType.PromoteToList,
                InsertIndex = insertIndex,
                IndicatorY = indicatorY,
                IndicatorLeft = listBounds.x,
                IndicatorWidth = listBounds.width
            };
        }

        static int CountListItemsBefore(VisualElement container, VisualElement item)
        {
            int n = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                var c = container[i];
                if (c == item)
                    return n;
                if (c.ClassListContains("list-item"))
                    n++;
            }
            return n;
        }

        void CalcInsertIndexAndIndicator(ScrollView scroll, float panelY, string itemClass,
            out int insertIndex, out float indicatorY)
        {
            var container = scroll.contentContainer;
            insertIndex = 0;
            indicatorY = scroll.worldBound.y;

            int itemCount = 0;
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container[i];
                if (!child.ClassListContains(itemClass)) continue;
                var bounds = child.worldBound;
                float midY = bounds.y + bounds.height * 0.5f;
                if (panelY > midY)
                {
                    itemCount++;
                    insertIndex = itemCount;
                    indicatorY = bounds.yMax;
                }
                else
                {
                    indicatorY = bounds.y;
                    break;
                }
            }
        }

        float CalcIndicatorY(ScrollView scroll, float panelY, string itemClass)
        {
            int unused;
            float indicatorY;
            CalcInsertIndexAndIndicator(scroll, panelY, itemClass, out unused, out indicatorY);
            return indicatorY;
        }

        void HandleDragEnd(DragItemInfo info, DropTarget dropTarget)
        {
            switch (dropTarget.Type)
            {
                case DropTargetType.ReorderSubTask:
                    _controller.ReorderSubTask(
                        info.ParentBigEventId, info.ItemId, dropTarget.InsertIndex);
                    break;

                case DropTargetType.ReorderBigEvent:
                    _controller.ReorderBigEvent(info.ItemId, dropTarget.InsertIndex);
                    break;

                case DropTargetType.PromoteToList:
                    var newId = _controller.PromoteSubTaskToBigEvent(
                        info.ParentBigEventId, info.ItemId, dropTarget.InsertIndex);
                    if (newId != null)
                    {
                        _selectedBigEventId = newId;
                        RebuildLeftColumn();
                        RebuildRightColumn();
                    }
                    break;

                case DropTargetType.MergeSubTaskIntoBigEvent:
                    _selectedBigEventId = dropTarget.TargetBigEventId;
                    _controller.MoveSubTaskToBigEvent(
                        info.ParentBigEventId, info.ItemId, dropTarget.TargetBigEventId);
                    RebuildLeftColumn();
                    RebuildRightColumn();
                    break;

                case DropTargetType.DemoteToSubTask:
                    bool wasDemotedSelected = info.ItemId == _selectedBigEventId;
                    _controller.DemoteBigEventToSubTask(info.ItemId, dropTarget.TargetBigEventId);
                    if (wasDemotedSelected)
                    {
                        _selectedBigEventId = dropTarget.TargetBigEventId;
                        RebuildLeftColumn();
                        RebuildRightColumn();
                    }
                    break;
            }
        }

        // ── Helpers ──

        void UpdateStatus()
        {
            if (!_controller.IsAIConfigured)
            {
                _statusLabel.text = $"Please set API Key in:\n{_configReader.ConfigFilePath}";
                _statusLabel.style.color = new Color(1f, 0.7f, 0.3f);
            }
            else
            {
                _statusLabel.text = "";
            }
        }
    }
}
