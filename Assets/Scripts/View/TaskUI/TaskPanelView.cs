using System;
using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Signals;
using ChillAI.Model.TaskDecomposition;
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

        // Inline edit state
        TextField _activeEditField;
        Label _activeEditLabel;
        Action<string> _activeEditCallback;
        string _activeEditOriginalText;
        bool _isCommittingEdit;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            TaskDecompositionController controller,
            ITaskDecompositionReader taskReader,
            IConfigReader configReader)
        {
            _signalBus = signalBus;
            _controller = controller;
            _taskReader = taskReader;
            _configReader = configReader;
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

            // Create badge overlay on toggle button
            _badge = new Label("+1");
            _badge.AddToClassList("toggle-badge");
            _badge.AddToClassList("hidden");
            _toggleBtn.Add(_badge);

            _toggleBtn.clicked += OnToggle;
            _closeBtn.clicked += OnClose;
            _addListBtn.clicked += OnAddListClicked;

            _panel.RegisterCallback<MouseDownEvent>(OnPanelMouseDown);

            _signalBus?.Subscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.Subscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);

            UpdateStatus();
            AutoSelectFirst();
            RebuildLeftColumn();
            RebuildRightColumn();
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _closeBtn.clicked -= OnClose;
            _addListBtn.clicked -= OnAddListClicked;
            _panel.UnregisterCallback<MouseDownEvent>(OnPanelMouseDown);

            _signalBus?.TryUnsubscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
            _signalBus?.TryUnsubscribe<TaskAddedViaChatSignal>(OnTaskAddedViaChat);
        }

        // ── Panel ──

        void OnToggle()
        {
            _panelVisible = !_panelVisible;
            _panel.EnableInClassList("hidden", !_panelVisible);

            if (_panelVisible)
            {
                _badge.AddToClassList("hidden");
                _pendingBadgeCount = 0;
                _badgeHideSchedule?.Pause();
            }
        }

        void OnClose()
        {
            CommitEditMode();
            _panelVisible = false;
            _panel.EnableInClassList("hidden", true);
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
            label.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                EnterEditMode(label, evt.localMousePosition.x, "edit-field--sub", newText =>
                {
                    _controller.UpdateSubTaskTitle(bigEventId, subTaskId, newText);
                });
            });

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

                case BigEventChangeType.Removed:
                    if (signal.BigEventId == _selectedBigEventId)
                        AutoSelectFirst();
                    RebuildLeftColumn();
                    RebuildRightColumn();
                    break;

                case BigEventChangeType.SubTaskAdded:
                case BigEventChangeType.SubTaskRemoved:
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
