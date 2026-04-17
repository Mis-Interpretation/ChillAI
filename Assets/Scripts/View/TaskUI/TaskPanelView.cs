using System;
using System.Collections.Generic;
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
        QuestController _questController;
        ITaskDecompositionReader _taskReader;
        IConfigReader _configReader;
        IWindowService _windowService;
        AppSettings _appSettings;
        UserSettingsService _userSettings;
        UiLayoutController _uiLayout;

        // UI Elements
        Button _toggleBtn;
        VisualElement _panel;
        Button _closeBtn;
        Label _statusLabel;
        VisualElement _columns;
        VisualElement _colLeft;
        VisualElement _colDivider;
        ScrollView _listScroll;
        Button _addListBtn;
        ScrollView _taskScroll;

        bool _panelVisible;
        string _selectedBigEventId;

        // Category side tabs
        Label _panelTitle;
        VisualElement _sideTabs;
        VisualElement _tabDoing;
        VisualElement _tabWanting;
        TaskCategory _selectedCategory = TaskCategory.Doing;
        int _activeDragCount; // >0 while any big-task drag is in progress
        IVisualElementScheduledItem _hoverSwitchSchedule;
        VisualElement _hoveredTab;

        // Big-task drag transfer state (hover-switch support)
        readonly Dictionary<string, TaskItemDragManipulator> _listDragManips = new();
        TaskItemDragManipulator _activeDragManipulator;
        Vector2 _lastPanelPointerPos;
        // Mid-drag revert state: if the user hover-switches but drops outside
        // any valid zone, the task should return to its original category/position.
        TaskCategory? _dragOriginalCategory;
        string _dragTaskId;
        bool _dragHoverSwitchHappened;
        bool _dragDropHandled;

        const long HoverSwitchMs = 600;
        static readonly Color TabFillDoing = new Color(0.961f, 0.745f, 0.333f, 0.95f);
        static readonly Color TabBorderDoing = new Color(1.000f, 0.863f, 0.549f, 0.95f);
        static readonly Color TabFillWanting = new Color(0.490f, 0.804f, 0.784f, 0.95f);
        static readonly Color TabBorderWanting = new Color(0.667f, 0.902f, 0.894f, 0.95f);

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
        const string QuestVirtualBigEventId = "__quest_virtual_big_event__";
        /// <summary>Top/bottom fraction of a list row treated as "insert line" gaps; middle is merge-into.</summary>
        const float ListRowEdgeGapFraction = 0.24f;

        // Inline edit state
        TextField _activeEditField;
        Label _activeEditLabel;
        Action<string> _activeEditCallback;
        string _activeEditOriginalText;
        bool _isCommittingEdit;

        // Window drag / resize / column divider
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;
        ColumnDividerManipulator _dividerManipulator;
        VisualElement _resizeHandle;

        // One-shot callback used to restore the saved column ratio after the first layout pass.
        EventCallback<GeometryChangedEvent> _colRatioRestoreCallback;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            TaskDecompositionController controller,
            QuestController questController,
            ITaskDecompositionReader taskReader,
            IConfigReader configReader,
            IWindowService windowService,
            AppSettings appSettings,
            UserSettingsService userSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _controller = controller;
            _questController = questController;
            _taskReader = taskReader;
            _configReader = configReader;
            _windowService = windowService;
            _appSettings = appSettings;
            _userSettings = userSettings;
            _uiLayout = uiLayout;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _toggleBtn  = root.Q<Button>("toggle-btn");
            _panel      = root.Q<VisualElement>("panel");
            _closeBtn   = root.Q<Button>("close-btn");
            _statusLabel = root.Q<Label>("status-label");
            _columns    = root.Q<VisualElement>(className: "columns");
            _colLeft    = root.Q<VisualElement>(className: "col-left");
            _colDivider = root.Q<VisualElement>("col-divider");
            _listScroll = root.Q<ScrollView>("list-scroll");
            _addListBtn = root.Q<Button>("add-list-btn");
            _taskScroll = root.Q<ScrollView>("task-scroll");

            _sideTabs    = root.Q<VisualElement>("side-tabs");
            _tabDoing    = root.Q<VisualElement>("tab-doing");
            _tabWanting  = root.Q<VisualElement>("tab-wanting");
            _panelTitle  = root.Q<Label>("panel-title");
            InitSideTabs();
            UpdatePanelTitle();

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
            // Track latest pointer position in panel coords for mid-drag hover-switch transfer.
            _panel.RegisterCallback<PointerMoveEvent>(OnPanelPointerMove);

            var header = root.Q<VisualElement>(className: "panel-header");
            _dragManipulator = new WindowDragManipulator(_panel);
            header.AddManipulator(_dragManipulator);
            _dragManipulator.DragEnded += OnHudLayoutChanged;

            _resizeHandle = root.Q<VisualElement>("task-resize-handle");
            if (_resizeHandle != null)
            {
                _resizeManipulator = new PanelResizeManipulator(
                    _panel,
                    _userSettings.Data.taskPanelMinWidth,
                    _userSettings.Data.taskPanelMinHeight,
                    OnHudLayoutChanged);
                _resizeHandle.AddManipulator(_resizeManipulator);
            }

            if (_colDivider != null)
            {
                _dividerManipulator = new ColumnDividerManipulator(
                    _colLeft,
                    _columns,
                    () => _userSettings.Data.taskColLeftMinRatio,
                    () => _userSettings.Data.taskColLeftMaxRatio,
                    OnDividerDragEnded);
                _colDivider.AddManipulator(_dividerManipulator);
            }

            _signalBus?.Subscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.Subscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);
            _signalBus?.Subscribe<QuestProgressChangedSignal>(OnQuestProgressChanged);

            UpdateStatus();
            AutoSelectFirst();
            RebuildLeftColumn();
            RebuildRightColumn();

            // Pre-warm: make the panel visible for one layout pass so UIToolkit computes
            // geometry for all children. This eliminates the ~1 s freeze on first open.
            // We listen for GeometryChangedEvent on _columns (fires *after* the layout
            // pass) to safely read resolvedStyle.width before hiding again.
            _colRatioRestoreCallback = _ =>
            {
                if (_columns == null) return;
                _columns.UnregisterCallback<GeometryChangedEvent>(_colRatioRestoreCallback);
                _colRatioRestoreCallback = null;

                float colsW = _columns.resolvedStyle.width;
                if (colsW > 0f && _uiLayout != null && _uiLayout.TryGetTaskColLeftRatio(out float ratio) && _colLeft != null)
                {
                    _colLeft.style.width = Mathf.Clamp(
                        ratio * colsW,
                        _userSettings.Data.taskColLeftMinRatio * colsW,
                        _userSettings.Data.taskColLeftMaxRatio * colsW);
                }

                _panel.AddToClassList("hidden");
            };
            _columns.RegisterCallback<GeometryChangedEvent>(_colRatioRestoreCallback);
            _panel.RemoveFromClassList("hidden");
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _closeBtn.clicked -= OnClose;
            _addListBtn.clicked -= OnAddListClicked;
            _panel.UnregisterCallback<MouseDownEvent>(OnPanelMouseDown);
            _panel.UnregisterCallback<PointerMoveEvent>(OnPanelPointerMove);

            var header = _panel.Q<VisualElement>(className: "panel-header");
            if (_dragManipulator != null)
            {
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            if (_dividerManipulator != null && _colDivider != null)
                _colDivider.RemoveManipulator(_dividerManipulator);

            if (_colRatioRestoreCallback != null && _columns != null)
            {
                _columns.UnregisterCallback<GeometryChangedEvent>(_colRatioRestoreCallback);
                _colRatioRestoreCallback = null;
            }

            _uiLayout?.UnregisterTaskHudRoot();
            _uiLayout?.UnregisterTaskPanel();

            _signalBus?.TryUnsubscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.TryUnsubscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);
            _signalBus?.TryUnsubscribe<QuestProgressChangedSignal>(OnQuestProgressChanged);
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

        void OnDividerDragEnded()
        {
            if (_uiLayout == null || _colLeft == null || _columns == null) return;

            float colsW = _columns.resolvedStyle.width;
            if (colsW > 0f)
            {
                float ratio = _colLeft.resolvedStyle.width / colsW;
                _uiLayout.SetTaskColLeftRatio(ratio);
            }
            _uiLayout.RequestSave();
        }

        void OnPanelPointerMove(PointerMoveEvent evt)
        {
            _lastPanelPointerPos = evt.position;

            // Hover-over-tab detection during a big-task drag. We poll the pointer
            // position on _panel (which always receives bubbled move events, even
            // when another element has pointer capture) instead of relying on
            // PointerEnter/Leave on the tab itself — those can be suppressed
            // during an active capture on an unrelated element.
            if (_activeDragCount <= 0) return;

            VisualElement over = null;
            TaskCategory overCat = TaskCategory.Doing;
            if (_tabDoing != null && _tabDoing.worldBound.Contains(evt.position))
            {
                over = _tabDoing;
                overCat = TaskCategory.Doing;
            }
            else if (_tabWanting != null && _tabWanting.worldBound.Contains(evt.position))
            {
                over = _tabWanting;
                overCat = TaskCategory.Wanting;
            }

            if (over == _hoveredTab) return;

            if (_hoveredTab != null)
                _hoveredTab.RemoveFromClassList("side-tab--drag-hover");
            _hoverSwitchSchedule?.Pause();
            _hoverSwitchSchedule = null;

            _hoveredTab = over;
            if (over == null) return;

            over.AddToClassList("side-tab--drag-hover");
            if (_selectedCategory == overCat) return; // visual feedback only; no switch needed

            var scheduledTab = over;
            var scheduledCat = overCat;
            _hoverSwitchSchedule = _panel.schedule.Execute(() =>
            {
                _hoverSwitchSchedule = null;
                if (_hoveredTab == scheduledTab && _activeDragCount > 0)
                    PerformHoverSwitch(scheduledCat);
            }).StartingIn(HoverSwitchMs);
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
            if (_selectedCategory == TaskCategory.Doing && HasQuestToShow())
            {
                _selectedBigEventId = QuestVirtualBigEventId;
                return;
            }

            foreach (var be in _taskReader.BigEvents)
            {
                if (be.Category == _selectedCategory)
                {
                    _selectedBigEventId = be.Id;
                    return;
                }
            }

            _selectedBigEventId = null;
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
            // New big tasks inherit the currently-viewed category so they appear
            // in the same list the user is looking at.
            var categoryForNew = _selectedCategory;
            ShowInlineInput(_listScroll, text =>
            {
                var id = _controller.CreateBigEvent(text, categoryForNew);
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
            _listDragManips.Clear();

            // Quest is virtual and always logically "Doing".
            if (_selectedCategory == TaskCategory.Doing && TryGetDisplayedQuest(out var quest))
            {
                var questItem = CreateQuestListItem(quest);
                _listScroll.Add(questItem);
            }

            foreach (var bigEvent in _taskReader.BigEvents)
            {
                if (bigEvent.Category != _selectedCategory) continue;
                var item = CreateListItem(bigEvent);
                _listScroll.Add(item);
            }
        }

        VisualElement CreateQuestListItem(QuestViewData quest)
        {
            var row = new VisualElement();
            row.AddToClassList("list-item");
            row.AddToClassList("list-item--quest");
            row.userData = QuestVirtualBigEventId;
            if (_selectedBigEventId == QuestVirtualBigEventId)
                row.AddToClassList("list-item--selected");

            var title = new Label($"Quest: {quest.Title}");
            title.AddToClassList("list-item-title");
            row.Add(title);

            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (_activeEditField != null) return;
                SelectBigEvent(QuestVirtualBigEventId);
            });

            return row;
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

            // Long-press drag. `dragManip` is assigned BEFORE the onDragStarted
            // closure is invoked, so capturing it by reference is safe.
            TaskItemDragManipulator dragManip = null;
            dragManip = new TaskItemDragManipulator(
                () => new DragItemInfo { Type = DragItemType.BigEvent, ItemId = bigEventId },
                ResolveDropTarget,
                HandleDragEnd,
                () => { CommitEditMode(); RemoveInlineInput(); OnBigEventDragStarted(dragManip, bigEventId); },
                _panel,
                _appSettings.taskPanelDragLongPressMs,
                OnBigEventDragFinished);
            row.AddManipulator(dragManip);
            _listDragManips[bigEventId] = dragManip;

            return row;
        }

        // ── Right Column (subtasks) ──

        void RebuildRightColumn()
        {
            _taskScroll.Clear();

            if (_selectedBigEventId == QuestVirtualBigEventId)
            {
                RebuildQuestRightColumn();
                return;
            }

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

            var completeRow = new VisualElement();
            completeRow.AddToClassList("big-task-archive-row");

            // "现在就做！" — only shown for Wanting-category tasks. Moves the task
            // to Doing AND puts it at the front of the real BigEvents list (Quest
            // virtual item still renders first in the view).
            if (selected.Category == TaskCategory.Wanting)
            {
                var doNowBtn = new Button { text = "现在就做！" };
                doNowBtn.AddToClassList("big-task-do-now-btn");
                var doNowBigId = selected.Id;
                doNowBtn.clicked += () =>
                {
                    CommitEditMode();
                    RemoveInlineInput();
                    _controller.MoveBigEventToDoingFront(doNowBigId);
                };
                completeRow.Add(doNowBtn);
            }

            var completeBtn = new Button { text = "完成" };
            completeBtn.AddToClassList("big-task-complete-btn");
            completeBtn.SetEnabled(selected.IsReadyForCompletedBigEventArchive);
            var completeBigId = selected.Id;
            completeBtn.clicked += () =>
            {
                CommitEditMode();
                RemoveInlineInput();
                _controller.TryArchiveAndCompleteBigEvent(completeBigId);
            };
            completeRow.Add(completeBtn);
            _taskScroll.Add(completeRow);

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

        void RebuildQuestRightColumn()
        {
            if (!TryGetDisplayedQuest(out var quest))
                return;

            var completeRow = new VisualElement();
            completeRow.AddToClassList("big-task-archive-row");
            var completeBtn = new Button { text = "完成" };
            completeBtn.AddToClassList("big-task-complete-btn");
            completeBtn.SetEnabled(IsQuestReadyForComplete(quest));
            var questId = quest.QuestId;
            completeBtn.clicked += () =>
            {
                CommitEditMode();
                RemoveInlineInput();

                _selectedBigEventId = null;
                if (_questController.TryCompleteQuest(questId))
                {
                    AutoSelectFirst();
                    RebuildLeftColumn();
                    RebuildRightColumn();
                }
                else
                {
                    _selectedBigEventId = QuestVirtualBigEventId;
                }
            };
            completeRow.Add(completeBtn);
            _taskScroll.Add(completeRow);

            foreach (var step in quest.Steps)
            {
                var item = CreateSubTaskElement(
                    QuestVirtualBigEventId,
                    step.StepId,
                    step.Title,
                    step.IsCompleted,
                    isReadOnly: true);
                _taskScroll.Add(item);
            }
        }

        VisualElement CreateSubTaskElement(string bigEventId, SubTask subTask)
        {
            return CreateSubTaskElement(bigEventId, subTask.Id, subTask.Title, subTask.IsCompleted, false);
        }

        VisualElement CreateSubTaskElement(
            string bigEventId,
            string subTaskId,
            string titleText,
            bool isCompleted,
            bool isReadOnly)
        {
            var row = new VisualElement();
            row.AddToClassList("sub-task-item");
            if (isReadOnly)
                row.AddToClassList("sub-task-item--quest");
            row.name = "subtask-" + subTaskId;

            var checkbox = new Toggle();
            checkbox.AddToClassList("sub-task-checkbox");
            if (isReadOnly)
            {
                checkbox.AddToClassList("sub-task-checkbox--readonly");
                checkbox.SetEnabled(false);
            }
            checkbox.value = isCompleted;

            var label = new Label(titleText);
            label.AddToClassList("sub-task-title");
            label.EnableInClassList("sub-task-title--completed", isCompleted);

            var deleteBtn = new Button { text = "✕" };
            deleteBtn.AddToClassList("sub-task-delete");
            if (isReadOnly)
                deleteBtn.style.display = DisplayStyle.None;

            row.Add(checkbox);
            row.Add(label);
            row.Add(deleteBtn);

            if (!isReadOnly)
            {
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
            }

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

                case BigEventChangeType.CategoryChanged:
                    // Task may have left the current category list; re-auto-select if needed.
                    if (signal.BigEventId == _selectedBigEventId)
                    {
                        var be = _taskReader.GetBigEvent(signal.BigEventId);
                        if (be == null || be.Category != _selectedCategory)
                        {
                            AutoSelectFirst();
                            RebuildRightColumn();
                        }
                    }
                    RebuildLeftColumn();
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

            var completeBtn = _taskScroll.Q<Button>(className: "big-task-complete-btn");
            var big = _taskReader.GetBigEvent(_selectedBigEventId);
            completeBtn?.SetEnabled(big != null && big.IsReadyForCompletedBigEventArchive);
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

        void OnQuestProgressChanged(QuestProgressChangedSignal _)
        {
            if (_selectedBigEventId == QuestVirtualBigEventId && !HasQuestToShow())
                AutoSelectFirst();
            RebuildLeftColumn();
            RebuildRightColumn();
        }

        // ── Drag and Drop ──

        DropTarget ResolveDropTarget(Vector2 panelPos, DragItemInfo info)
        {
            var listBounds = _listScroll.worldBound;
            var taskBounds = _taskScroll.worldBound;

            // BigEvent dragged onto a category side tab → reassign category
            if (info.Type == DragItemType.BigEvent)
            {
                if (_tabDoing != null && _tabDoing.worldBound.Contains(panelPos))
                    return BuildCategoryDropTarget(_tabDoing, TaskCategory.Doing);
                if (_tabWanting != null && _tabWanting.worldBound.Contains(panelPos))
                    return BuildCategoryDropTarget(_tabWanting, TaskCategory.Wanting);
            }

            // SubTask dragged to left column → merge onto row vs promote in gap
            if (info.Type == DragItemType.SubTask && listBounds.Contains(panelPos))
                return ResolveSubTaskLeftColumnDrop(panelPos, info, listBounds);

            // BigEvent dragged to right column → demote
            if (info.Type == DragItemType.BigEvent && taskBounds.Contains(panelPos))
            {
                if (_selectedBigEventId != null &&
                    _selectedBigEventId != info.ItemId &&
                    !IsQuestVirtualId(_selectedBigEventId))
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
                if (string.IsNullOrEmpty(targetId) || targetId == info.ParentBigEventId || IsQuestVirtualId(targetId))
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
                if (c.ClassListContains("list-item") && !IsQuestVirtualId(c.userData as string))
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
                if (itemClass == "list-item" && IsQuestVirtualId(child.userData as string)) continue;
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

        static bool IsQuestVirtualId(string id)
        {
            return id == QuestVirtualBigEventId;
        }

        DropTarget BuildCategoryDropTarget(VisualElement tab, TaskCategory cat)
        {
            var b = tab.worldBound;
            return new DropTarget
            {
                Type = DropTargetType.ReassignCategory,
                TargetCategory = cat,
                IndicatorLeft = b.x,
                IndicatorY = b.y,
                IndicatorWidth = b.width,
                IndicatorHeight = b.height,
                IsRowMergeHighlight = true
            };
        }

        void HandleDragEnd(DragItemInfo info, DropTarget dropTarget)
        {
            // Any valid drop counts as a handled outcome — suppresses the
            // hover-switch revert in OnBigEventDragFinished.
            _dragDropHandled = true;
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

                case DropTargetType.ReassignCategory:
                    // Move the task to the target category WITHOUT switching tabs.
                    // The CategoryChanged signal handler will rebuild the left column
                    // and reselect if the moved task was the active one.
                    _controller.SetBigEventCategory(info.ItemId, dropTarget.TargetCategory);
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

        bool HasQuestToShow()
        {
            return TryGetDisplayedQuest(out _);
        }

        static bool IsQuestReadyForComplete(QuestViewData quest)
        {
            if (quest == null || !quest.IsUnlocked || quest.IsCompleted || quest.Steps == null || quest.Steps.Count == 0)
                return false;
            foreach (var step in quest.Steps)
            {
                if (!step.IsCompleted)
                    return false;
            }

            return true;
        }

        bool TryGetDisplayedQuest(out QuestViewData quest)
        {
            quest = null;
            if (_questController == null) return false;

            IReadOnlyList<QuestViewData> quests = _questController.GetQuestViews();
            if (quests == null || quests.Count == 0)
                return false;

            foreach (var candidate in quests)
            {
                if (candidate.IsUnlocked && !candidate.IsCompleted)
                {
                    quest = candidate;
                    return true;
                }
            }

            return false;
        }

        // ── Category Side Tabs ──

        void InitSideTabs()
        {
            if (_tabDoing == null || _tabWanting == null) return;

            // Transparent container: let pointer events pass through to the tabs
            // (which override pickingMode to Position below) or to elements behind.
            if (_sideTabs != null)
                _sideTabs.pickingMode = PickingMode.Ignore;

            RegisterSideTab(_tabDoing, TaskCategory.Doing, TabFillDoing, TabBorderDoing);
            RegisterSideTab(_tabWanting, TaskCategory.Wanting, TabFillWanting, TabBorderWanting);

            ApplySelectedTabClass();
        }

        void RegisterSideTab(VisualElement tab, TaskCategory category, Color fill, Color border)
        {
            tab.pickingMode = PickingMode.Position;

            // Custom pentagon shape — triangular cut on the left, rounded on the right.
            tab.generateVisualContent += ctx => DrawSideTabShape(ctx, fill, border);
            tab.RegisterCallback<GeometryChangedEvent>(_ => tab.MarkDirtyRepaint());

            tab.RegisterCallback<ClickEvent>(_ => SelectCategory(category));

            // Drag-hover-to-switch detection is handled in OnPanelPointerMove.
            // Pointer enter/leave events on the tab can be suppressed while the
            // dragged row owns pointer capture, so we poll instead.
        }

        /// <summary>
        /// Called when the user hovers over a category tab for HoverSwitchMs
        /// while actively dragging a big task. Moves the task to the target
        /// category, rebuilds the left column to that category, then transfers
        /// the active drag onto the task's new row so the user can keep
        /// dragging (e.g. to reorder within the new list).
        /// </summary>
        void PerformHoverSwitch(TaskCategory newCategory)
        {
            if (_selectedCategory == newCategory) return; // already on this tab

            var manip = _activeDragManipulator;
            if (manip == null || !manip.IsDragging)
            {
                // Shouldn't normally happen; fall back to plain switch.
                SelectCategory(newCategory);
                return;
            }

            // 1. Detach the drag from the current row's manipulator (keeps ghost/indicator).
            var detached = manip.DetachForTransfer(_lastPanelPointerPos);
            var bigEventId = detached.info.ItemId;
            if (string.IsNullOrEmpty(bigEventId))
            {
                // Couldn't identify the task — abandon the handoff.
                detached.ghost?.RemoveFromHierarchy();
                detached.indicator?.RemoveFromHierarchy();
                _activeDragManipulator = null;
                return;
            }

            // 2. Switch selected category + make the dragged task the active
            //    selection BEFORE firing the model change. The CategoryChanged
            //    signal handler will RebuildLeftColumn with correct filter and
            //    highlight in one pass.
            _selectedCategory = newCategory;
            bool selectionChanged = _selectedBigEventId != bigEventId;
            _selectedBigEventId = bigEventId;
            ApplySelectedTabClass();
            UpdatePanelTitle();
            _uiLayout?.SetTaskSelectedCategory(newCategory);
            _uiLayout?.RequestSave();

            // 3. Commit category change. Fires CategoryChanged → RebuildLeftColumn,
            //    which populates _listDragManips with fresh manipulators for the
            //    new category's rows (including our dragged task).
            _dragHoverSwitchHappened = true;
            _controller.SetBigEventCategory(bigEventId, newCategory);

            // 4. Refresh the right column if the selected task changed.
            if (selectionChanged)
                RebuildRightColumn();

            // 5. Find the new row's manipulator and hand off the drag.
            if (!_listDragManips.TryGetValue(bigEventId, out var newManip))
            {
                // New row didn't appear (unexpected) — drop the ghost and bail.
                detached.ghost?.RemoveFromHierarchy();
                detached.indicator?.RemoveFromHierarchy();
                _activeDragManipulator = null;
                return;
            }

            newManip.AdoptDrag(detached.pointerId, _lastPanelPointerPos,
                               detached.info, detached.ghost, detached.indicator);
            _activeDragManipulator = newManip;

            // Clear tab hover visuals since the drag is now inside the new list.
            _tabDoing?.RemoveFromClassList("side-tab--drag-hover");
            _tabWanting?.RemoveFromClassList("side-tab--drag-hover");
            _hoveredTab = null;
        }

        void OnBigEventDragStarted(TaskItemDragManipulator manip, string bigEventId)
        {
            _activeDragCount++;
            _activeDragManipulator = manip;
            _dragTaskId = bigEventId;
            _dragHoverSwitchHappened = false;
            _dragDropHandled = false;
            _dragOriginalCategory = _taskReader.GetBigEvent(bigEventId)?.Category;
        }

        void OnBigEventDragFinished()
        {
            if (_activeDragCount > 0) _activeDragCount--;

            _hoverSwitchSchedule?.Pause();
            _hoverSwitchSchedule = null;
            _hoveredTab = null;

            _tabDoing?.RemoveFromClassList("side-tab--drag-hover");
            _tabWanting?.RemoveFromClassList("side-tab--drag-hover");

            // Revert mid-drag category change if the user dropped outside any
            // valid zone after a hover-switch. Spec: "如果用户打开了task list
            // 但是没有把大任务丢放在task panel有效区域里面，此次对大任务的
            // 操作将视为无效行为。大任务会回到原本的list的原本的位置。"
            if (_dragHoverSwitchHappened && !_dragDropHandled &&
                _dragOriginalCategory.HasValue && !string.IsNullOrEmpty(_dragTaskId))
            {
                var original = _dragOriginalCategory.Value;
                _controller.SetBigEventCategory(_dragTaskId, original);

                _selectedCategory = original;
                ApplySelectedTabClass();
                UpdatePanelTitle();
                _uiLayout?.SetTaskSelectedCategory(original);
                _uiLayout?.RequestSave();
                // The CategoryChanged signal handler already triggers RebuildLeftColumn.
            }

            _activeDragManipulator = null;
            _dragTaskId = null;
            _dragOriginalCategory = null;
            _dragHoverSwitchHappened = false;
            _dragDropHandled = false;
        }

        void SelectCategory(TaskCategory category)
        {
            if (_selectedCategory == category) return;

            CommitEditMode();
            RemoveInlineInput();

            _selectedCategory = category;
            ApplySelectedTabClass();
            UpdatePanelTitle();

            _uiLayout?.SetTaskSelectedCategory(category);
            _uiLayout?.RequestSave();

            // If the selected big task is no longer visible in this category,
            // pick the first one that is.
            if (!string.IsNullOrEmpty(_selectedBigEventId) &&
                _selectedBigEventId != QuestVirtualBigEventId)
            {
                var be = _taskReader.GetBigEvent(_selectedBigEventId);
                if (be == null || be.Category != _selectedCategory)
                    AutoSelectFirst();
            }
            else if (_selectedBigEventId == QuestVirtualBigEventId &&
                     _selectedCategory != TaskCategory.Doing)
            {
                AutoSelectFirst();
            }
            else if (string.IsNullOrEmpty(_selectedBigEventId))
            {
                AutoSelectFirst();
            }

            RebuildLeftColumn();
            RebuildRightColumn();
        }

        void ApplySelectedTabClass()
        {
            if (_tabDoing != null)
                _tabDoing.EnableInClassList("side-tab--selected", _selectedCategory == TaskCategory.Doing);
            if (_tabWanting != null)
                _tabWanting.EnableInClassList("side-tab--selected", _selectedCategory == TaskCategory.Wanting);
        }

        void UpdatePanelTitle()
        {
            if (_panelTitle == null) return;
            _panelTitle.text = _selectedCategory == TaskCategory.Doing
                ? "正在做的事"
                : "想要做的事";
        }

        static void DrawSideTabShape(MeshGenerationContext ctx, Color fill, Color border)
        {
            // generateVisualContent's local coord origin is the element's top-left
            // (border box). layout.width/height give the full element size so the
            // shape spans the whole tag regardless of padding.
            var el = ctx.visualElement;
            float w = el.layout.width;
            float h = el.layout.height;
            if (w <= 1f || h <= 1f) return;

            // Five vertices: triangular cut on the left, rounded-ish right edge via
            // slightly tucked-in top-right and bottom-right points.
            float apex = Mathf.Min(12f, w * 0.28f);
            float corner = Mathf.Min(6f, h * 0.2f);

            var v0 = new Vector3(0f, h * 0.5f, 0f);      // left tip (triangle point)
            var v1 = new Vector3(apex, 0f, 0f);          // top-left
            var v2 = new Vector3(w - corner, 0f, 0f);    // top-right (near rounded corner)
            var v3 = new Vector3(w, corner, 0f);         // right just below top
            var v4 = new Vector3(w, h - corner, 0f);     // right just above bottom
            var v5 = new Vector3(w - corner, h, 0f);     // bottom-right
            var v6 = new Vector3(apex, h, 0f);           // bottom-left

            var mesh = ctx.Allocate(7, 15); // 5 triangles + 5 "fan" indices — we use 5 tris = 15 indices

            mesh.SetNextVertex(new Vertex { position = v0, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v1, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v2, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v3, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v4, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v5, tint = (Color32)fill });
            mesh.SetNextVertex(new Vertex { position = v6, tint = (Color32)fill });

            // Fan triangulation from v0.
            ushort i0 = 0, i1 = 1, i2 = 2, i3 = 3, i4 = 4, i5 = 5, i6 = 6;
            mesh.SetNextIndex(i0); mesh.SetNextIndex(i1); mesh.SetNextIndex(i2);
            mesh.SetNextIndex(i0); mesh.SetNextIndex(i2); mesh.SetNextIndex(i3);
            mesh.SetNextIndex(i0); mesh.SetNextIndex(i3); mesh.SetNextIndex(i4);
            mesh.SetNextIndex(i0); mesh.SetNextIndex(i4); mesh.SetNextIndex(i5);
            mesh.SetNextIndex(i0); mesh.SetNextIndex(i5); mesh.SetNextIndex(i6);
            _ = border; // Border is reserved for a future stroke pass; fill-only for now.
        }
    }
}
