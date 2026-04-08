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
        TextField _taskInput;
        Button _submitBtn;
        Label _statusLabel;
        Label _loadingLabel;
        ScrollView _taskList;

        bool _panelVisible;

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
            _taskInput = root.Q<TextField>("task-input");
            _submitBtn = root.Q<Button>("submit-btn");
            _statusLabel = root.Q<Label>("status-label");
            _loadingLabel = root.Q<Label>("loading-label");
            _taskList = root.Q<ScrollView>("task-list");

            _toggleBtn.clicked += OnToggle;
            _submitBtn.clicked += OnSubmit;
            _taskInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown);

            _signalBus?.Subscribe<TaskDecompositionResultSignal>(OnTaskResult);

            UpdateStatus();
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _submitBtn.clicked -= OnSubmit;
            _taskInput.UnregisterCallback<KeyDownEvent>(OnInputKeyDown);

            _signalBus?.TryUnsubscribe<TaskDecompositionResultSignal>(OnTaskResult);
        }

        void Update()
        {
            _loadingLabel.EnableInClassList("hidden", !_taskReader.IsProcessing);
            _submitBtn.SetEnabled(!_taskReader.IsProcessing);
        }

        void OnToggle()
        {
            _panelVisible = !_panelVisible;
            _panel.EnableInClassList("hidden", !_panelVisible);

            if (_panelVisible)
                _taskInput.Focus();
        }

        void OnInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopPropagation();
                OnSubmit();
            }
        }

        void OnSubmit()
        {
            var text = _taskInput.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _controller.RequestDecomposition(text);
            _taskInput.value = "";
        }

        void OnTaskResult(TaskDecompositionResultSignal signal)
        {
            _taskList.Clear();

            if (signal.IsError)
            {
                _statusLabel.text = signal.ErrorMessage;
                _statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
                return;
            }

            _statusLabel.text = "";

            foreach (var sub in signal.SubTasks)
            {
                var item = new VisualElement();
                item.AddToClassList("task-item");

                var title = new Label($"{sub.Order}. {sub.Title}");
                title.AddToClassList("task-item-title");

                item.Add(title);
                _taskList.Add(item);
            }
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
