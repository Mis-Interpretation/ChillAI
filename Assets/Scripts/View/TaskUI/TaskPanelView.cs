using System;
using System.Collections.Generic;
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
        ScrollView _bigEventList;
        Button _addBtn;

        bool _panelVisible;

        // Track big event elements for partial updates
        readonly Dictionary<string, VisualElement> _bigEventElements = new();

        // Track expanded state per big event (view-only state)
        readonly HashSet<string> _expandedBigEvents = new();

        // Inline input reference (only one at a time)
        VisualElement _inlineInputRow;

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
            _bigEventList = root.Q<ScrollView>("big-event-list");
            _addBtn = root.Q<Button>("add-btn");

            _toggleBtn.clicked += OnToggle;
            _closeBtn.clicked += OnClose;
            _addBtn.clicked += OnAddClicked;

            _signalBus?.Subscribe<TaskDecompositionResultSignal>(OnTaskResult);
            _signalBus?.Subscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);

            // Click anywhere on panel to dismiss active edit
            _panel.RegisterCallback<MouseDownEvent>(OnPanelMouseDown);

            UpdateStatus();
            RebuildEntireList();
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _closeBtn.clicked -= OnClose;
            _addBtn.clicked -= OnAddClicked;

            _panel.UnregisterCallback<MouseDownEvent>(OnPanelMouseDown);

            _signalBus?.TryUnsubscribe<TaskDecompositionResultSignal>(OnTaskResult);
            _signalBus?.TryUnsubscribe<BigEventChangedSignal>(OnBigEventChanged);
            _signalBus?.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompletionChanged);
        }

        void Update()
        {
            foreach (var bigEvent in _taskReader.BigEvents)
            {
                if (!_bigEventElements.TryGetValue(bigEvent.Id, out var element)) continue;
                var loading = element.Q<Label>("loading-" + bigEvent.Id);
                if (loading != null)
                    loading.EnableInClassList("hidden", !bigEvent.IsProcessing);
            }
        }

        // ── Panel Click → Dismiss Edit ──

        void OnPanelMouseDown(MouseDownEvent evt)
        {
            if (_activeEditField == null) return;

            // If click target is inside the active edit field, let it through
            var target = evt.target as VisualElement;
            if (target != null && (_activeEditField == target || _activeEditField.Contains(target)))
                return;

            CommitEditMode();
        }

        // ── Panel Toggle ──

        void OnToggle()
        {
            _panelVisible = !_panelVisible;
            _panel.EnableInClassList("hidden", !_panelVisible);
        }

        void OnClose()
        {
            CommitEditMode();
            _panelVisible = false;
            _panel.EnableInClassList("hidden", true);
        }

        // ── Add Big Event ──

        void OnAddClicked()
        {
            CommitEditMode();
            if (_inlineInputRow != null) return;

            _inlineInputRow = new VisualElement();
            _inlineInputRow.AddToClassList("inline-input-row");

            var input = new TextField();
            input.AddToClassList("inline-input");

            var confirmBtn = new Button { text = "Go" };
            confirmBtn.AddToClassList("inline-confirm-btn");

            _inlineInputRow.Add(input);
            _inlineInputRow.Add(confirmBtn);

            _bigEventList.Insert(0, _inlineInputRow);
            input.Focus();

            confirmBtn.clicked += () => SubmitInlineInput(input);
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    SubmitInlineInput(input);
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    RemoveInlineInput();
                }
            });
        }

        void SubmitInlineInput(TextField input)
        {
            var text = input.value?.Trim();
            RemoveInlineInput();
            if (string.IsNullOrEmpty(text)) return;
            _controller.CreateBigEvent(text);
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
            // If already editing, commit the previous edit first
            if (_activeEditField != null)
                CommitEditMode();

            var parent = label.parent;
            var index = parent.IndexOf(label);
            var text = label.text;
            var fontSize = label.resolvedStyle.fontSize;

            _activeEditOriginalText = text;
            _activeEditLabel = label;
            _activeEditCallback = onCommit;

            // Hide label, insert TextField in its place
            label.style.display = DisplayStyle.None;

            var field = new TextField();
            field.AddToClassList("edit-field");
            field.AddToClassList(cssVariant);
            field.multiline = true;
            field.value = text;
            parent.Insert(index + 1, field);

            _activeEditField = field;

            // Focus and set cursor position after layout
            field.schedule.Execute(() =>
            {
                field.Focus();
                int cursorIdx = EstimateCursorIndex(text, clickLocalX, fontSize);
                field.SelectRange(cursorIdx, cursorIdx);
            });

            // Enter → commit, Escape → cancel
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    CommitEditMode();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    CancelEditMode();
                }
            });

            // FocusOut also commits (covers Tab, etc.)
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                CommitEditMode();
            });
        }

        void CommitEditMode()
        {
            if (_activeEditField == null || _isCommittingEdit) return;
            _isCommittingEdit = true;

            var newText = _activeEditField.value?.Trim();
            var changed = !string.IsNullOrEmpty(newText) && newText != _activeEditOriginalText;

            // Restore label
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

        /// <summary>
        /// Estimate which character index a click at localX corresponds to,
        /// accounting for CJK (full-width) vs ASCII (half-width) characters.
        /// </summary>
        static int EstimateCursorIndex(string text, float clickX, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            float x = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                float charWidth = c < 128 ? fontSize * 0.55f : fontSize;
                if (x + charWidth * 0.5f > clickX)
                    return i;
                x += charWidth;
            }
            return text.Length;
        }

        // ── Build UI ──

        void RebuildEntireList()
        {
            _bigEventList.Clear();
            _bigEventElements.Clear();

            foreach (var bigEvent in _taskReader.BigEvents)
            {
                var element = CreateBigEventElement(bigEvent);
                _bigEventList.Add(element);
                _bigEventElements[bigEvent.Id] = element;
            }
        }

        VisualElement CreateBigEventElement(BigEvent bigEvent)
        {
            var container = new VisualElement();
            container.AddToClassList("big-event");

            // Header row
            var header = new VisualElement();
            header.AddToClassList("big-event-header");

            var isExpanded = _expandedBigEvents.Contains(bigEvent.Id);

            var arrow = new Button { text = isExpanded ? "▾" : "▸" };
            arrow.AddToClassList("big-event-arrow");

            var title = new Label(bigEvent.Title);
            title.AddToClassList("big-event-title");

            var progress = new Label(FormatProgress(bigEvent));
            progress.AddToClassList("big-event-progress");
            progress.name = "progress-" + bigEvent.Id;

            var deleteBtn = new Button { text = "✕" };
            deleteBtn.AddToClassList("big-event-delete");

            header.Add(arrow);
            header.Add(title);
            header.Add(progress);
            header.Add(deleteBtn);
            container.Add(header);

            // Content (subtasks)
            var content = new VisualElement();
            content.AddToClassList("big-event-content");
            content.name = "content-" + bigEvent.Id;
            content.EnableInClassList("hidden", !isExpanded);

            BuildBigEventContent(content, bigEvent);
            container.Add(content);

            // Events
            var bigEventId = bigEvent.Id;

            arrow.clicked += () => ToggleExpand(bigEventId, arrow, content);

            deleteBtn.clicked += () => _controller.DeleteBigEvent(bigEventId);

            // Click title → edit
            title.RegisterCallback<MouseDownEvent>(evt =>
            {
                evt.StopPropagation();
                EnterEditMode(title, evt.localMousePosition.x, "edit-field--big", newText =>
                {
                    _controller.UpdateBigEventTitle(bigEventId, newText);
                });
            });

            return container;
        }

        void BuildBigEventContent(VisualElement content, BigEvent bigEvent)
        {
            content.Clear();

            // Loading
            var loading = new Label("思考中...");
            loading.AddToClassList("big-event-loading");
            loading.name = "loading-" + bigEvent.Id;
            loading.EnableInClassList("hidden", !bigEvent.IsProcessing);
            content.Add(loading);

            // Error
            if (!string.IsNullOrEmpty(bigEvent.ErrorMessage))
            {
                var error = new Label(bigEvent.ErrorMessage);
                error.AddToClassList("big-event-error");
                content.Add(error);
            }

            // Subtasks
            foreach (var subTask in bigEvent.SubTasks)
            {
                var item = CreateSubTaskElement(bigEvent.Id, subTask);
                content.Add(item);
            }
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

            // Click label → edit
            label.RegisterCallback<MouseDownEvent>(evt =>
            {
                evt.StopPropagation();
                EnterEditMode(label, evt.localMousePosition.x, "edit-field--sub", newText =>
                {
                    _controller.UpdateSubTaskTitle(bigEventId, subTaskId, newText);
                });
            });

            return row;
        }

        void ToggleExpand(string bigEventId, Button arrow, VisualElement content)
        {
            if (_expandedBigEvents.Contains(bigEventId))
            {
                _expandedBigEvents.Remove(bigEventId);
                arrow.text = "▸";
                content.EnableInClassList("hidden", true);
            }
            else
            {
                _expandedBigEvents.Add(bigEventId);
                arrow.text = "▾";
                content.EnableInClassList("hidden", false);
            }
        }

        // ── Signal Handlers ──

        void OnTaskResult(TaskDecompositionResultSignal signal)
        {
            if (!_bigEventElements.TryGetValue(signal.BigEventId, out var element)) return;

            var bigEvent = _taskReader.GetBigEvent(signal.BigEventId);
            if (bigEvent == null) return;

            var content = element.Q<VisualElement>("content-" + signal.BigEventId);
            if (content != null)
                BuildBigEventContent(content, bigEvent);

            UpdateProgress(signal.BigEventId);

            // Auto-expand when results arrive
            if (!_expandedBigEvents.Contains(signal.BigEventId))
            {
                _expandedBigEvents.Add(signal.BigEventId);
                var arrow = element.Q<Button>(className: "big-event-arrow");
                if (arrow != null) arrow.text = "▾";
                if (content != null) content.EnableInClassList("hidden", false);
            }
        }

        void OnBigEventChanged(BigEventChangedSignal signal)
        {
            switch (signal.ChangeType)
            {
                case BigEventChangeType.Added:
                {
                    var bigEvent = _taskReader.GetBigEvent(signal.BigEventId);
                    if (bigEvent == null) return;
                    _expandedBigEvents.Add(bigEvent.Id);
                    var element = CreateBigEventElement(bigEvent);
                    _bigEventList.Add(element);
                    _bigEventElements[bigEvent.Id] = element;
                    break;
                }

                case BigEventChangeType.Removed:
                {
                    if (_bigEventElements.TryGetValue(signal.BigEventId, out var element))
                    {
                        element.RemoveFromHierarchy();
                        _bigEventElements.Remove(signal.BigEventId);
                        _expandedBigEvents.Remove(signal.BigEventId);
                    }
                    break;
                }

                case BigEventChangeType.SubTaskRemoved:
                {
                    var bigEvent = _taskReader.GetBigEvent(signal.BigEventId);
                    if (bigEvent == null) return;
                    if (!_bigEventElements.TryGetValue(signal.BigEventId, out var element)) return;
                    var content = element.Q<VisualElement>("content-" + signal.BigEventId);
                    if (content != null)
                        BuildBigEventContent(content, bigEvent);
                    UpdateProgress(signal.BigEventId);
                    break;
                }
            }
        }

        void OnSubTaskCompletionChanged(SubTaskCompletionChangedSignal signal)
        {
            if (!_bigEventElements.TryGetValue(signal.BigEventId, out var element)) return;

            var subtaskRow = element.Q<VisualElement>("subtask-" + signal.SubTaskId);
            if (subtaskRow != null)
            {
                var checkbox = subtaskRow.Q<Toggle>();
                if (checkbox != null)
                    checkbox.SetValueWithoutNotify(signal.IsCompleted);

                var label = subtaskRow.Q<Label>(className: "sub-task-title");
                if (label != null)
                    label.EnableInClassList("sub-task-title--completed", signal.IsCompleted);
            }

            UpdateProgress(signal.BigEventId);
        }

        // ── Helpers ──

        void UpdateProgress(string bigEventId)
        {
            if (!_bigEventElements.TryGetValue(bigEventId, out var element)) return;
            var bigEvent = _taskReader.GetBigEvent(bigEventId);
            if (bigEvent == null) return;

            var progressLabel = element.Q<Label>("progress-" + bigEventId);
            if (progressLabel != null)
                progressLabel.text = FormatProgress(bigEvent);
        }

        static string FormatProgress(BigEvent bigEvent)
        {
            return bigEvent.TotalCount > 0
                ? $"{bigEvent.CompletedCount}/{bigEvent.TotalCount}"
                : "";
        }

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
