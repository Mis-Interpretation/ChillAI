using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Service.Layout;
using ChillAI.View.Window;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.EmojiChat
{
    [RequireComponent(typeof(UIDocument))]
    public class EmojiChatPanelView : MonoBehaviour
    {
        SignalBus _signalBus;
        EmojiChatController _controller;
        IConfigReader _configReader;
        AppSettings _appSettings;
        UiLayoutController _uiLayout;

        // UI elements
        Button _toggleBtn;
        VisualElement _panel;
        Button _closeBtn;
        ScrollView _chatMessages;
        TextField _chatInput;
        Button _sendBtn;
        Label _statusLabel;
        Label _loadingLabel;

        bool _panelVisible;

        /// <summary>Last AI emoji line this session; toggle shows it while the panel is closed.</summary>
        string _lastAiEmojiForToggle;

        const string DefaultToggleText = "💬";

        // Window drag
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;
        VisualElement _resizeHandle;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            EmojiChatController controller,
            IConfigReader configReader,
            AppSettings appSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _controller = controller;
            _configReader = configReader;
            _appSettings = appSettings;
            _uiLayout = uiLayout;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _toggleBtn = root.Q<Button>("chat-toggle-btn");
            _panel = root.Q<VisualElement>("chat-panel");
            _closeBtn = root.Q<Button>("chat-close-btn");
            _chatMessages = root.Q<ScrollView>("chat-messages");
            _chatInput = root.Q<TextField>("chat-input");
            _sendBtn = root.Q<Button>("chat-send-btn");
            _statusLabel = root.Q<Label>("chat-status-label");
            _loadingLabel = root.Q<Label>("chat-loading-label");

            _uiLayout.RegisterChatHudRoot(root);
            _uiLayout.RegisterChatPanel(_panel);
            _panelVisible = !_panel.ClassListContains("hidden");

            _toggleBtn.clicked += OnToggle;
            _closeBtn.clicked += OnToggle;
            _sendBtn.clicked += OnSubmit;

            var header = root.Q<VisualElement>(className: "chat-panel-header");
            _dragManipulator = new WindowDragManipulator(_panel);
            header.AddManipulator(_dragManipulator);
            _dragManipulator.DragEnded += OnHudLayoutChanged;

            _resizeHandle = root.Q<VisualElement>("chat-resize-handle");
            if (_resizeHandle != null)
            {
                _resizeManipulator = new PanelResizeManipulator(
                    _panel,
                    _appSettings.chatPanelMinWidth,
                    _appSettings.chatPanelMinHeight,
                    OnHudLayoutChanged);
                _resizeHandle.AddManipulator(_resizeManipulator);
            }

            _chatInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);

            _signalBus?.Subscribe<EmojiChatResponseSignal>(OnEmojiResponse);

            UpdateStatus();
            RefreshToggleButtonVisual();
        }

        void OnDisable()
        {
            _toggleBtn.clicked -= OnToggle;
            _closeBtn.clicked -= OnToggle;
            _sendBtn.clicked -= OnSubmit;

            var header = _panel.Q<VisualElement>(className: "chat-panel-header");
            if (_dragManipulator != null)
            {
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            _chatInput.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);

            _uiLayout?.UnregisterChatHudRoot();
            _uiLayout?.UnregisterChatPanel();

            _signalBus?.TryUnsubscribe<EmojiChatResponseSignal>(OnEmojiResponse);
        }

        void Update()
        {
            _loadingLabel.EnableInClassList("hidden", !_controller.IsProcessing);
            _sendBtn.SetEnabled(!_controller.IsProcessing);
        }

        void OnToggle()
        {
            _panelVisible = !_panelVisible;
            _panel.EnableInClassList("hidden", !_panelVisible);

            if (_panelVisible)
                _chatInput.Focus();

            RefreshToggleButtonVisual();
            _uiLayout?.RequestSave();
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void OnInputKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
            {
                evt.StopImmediatePropagation();
                OnSubmit();
            }
        }

        void OnSubmit()
        {
            var text = _chatInput.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // Show user bubble immediately
            AddBubble(text, true);

            _controller.SendMessage(text);
            // Defer clear to avoid ArgumentOutOfRangeException from
            // TextField's internal handler accessing stale cursor indices
            _chatInput.schedule.Execute(() =>
            {
                _chatInput.Blur();
                _chatInput.value = "";
                _chatInput.Focus();
            });
        }

        void OnEmojiResponse(EmojiChatResponseSignal signal)
        {
            if (signal.IsError)
            {
                _statusLabel.text = signal.ErrorMessage;
                _statusLabel.style.color = new Color(1f, 0.5f, 0.3f);
                return;
            }

            _statusLabel.text = "";

            foreach (var msg in signal.Messages)
                AddBubble(msg, false);

            if (signal.Messages.Count > 0)
            {
                var last = signal.Messages[signal.Messages.Count - 1]?.Trim();
                if (!string.IsNullOrEmpty(last))
                    _lastAiEmojiForToggle = last;
            }

            RefreshToggleButtonVisual();
        }

        void RefreshToggleButtonVisual()
        {
            if (_toggleBtn == null) return;

            var useEmoji = !_panelVisible && !string.IsNullOrEmpty(_lastAiEmojiForToggle);
            _toggleBtn.text = useEmoji ? _lastAiEmojiForToggle : DefaultToggleText;
            _toggleBtn.EnableInClassList("chat-toggle-btn--last-emoji", useEmoji);
        }

        void AddBubble(string text, bool isUser)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-bubble-row");
            row.AddToClassList(isUser ? "chat-bubble-row--user" : "chat-bubble-row--ai");

            var bubble = new Label(text);
            bubble.AddToClassList("chat-bubble");
            bubble.AddToClassList(isUser ? "chat-bubble--user" : "chat-bubble--ai");

            row.Add(bubble);
            _chatMessages.Add(row);

            // Enforce max visible bubbles
            while (_chatMessages.contentContainer.childCount > _appSettings.maxChatBubbles)
                _chatMessages.contentContainer.RemoveAt(0);

            // Auto-scroll to bottom after layout recalculates
            void ScrollToBottom(GeometryChangedEvent evt)
            {
                _chatMessages.contentContainer.UnregisterCallback<GeometryChangedEvent>(ScrollToBottom);
                _chatMessages.scrollOffset = new Vector2(0, _chatMessages.contentContainer.layout.height);
            }
            _chatMessages.contentContainer.RegisterCallback<GeometryChangedEvent>(ScrollToBottom);
        }

        void UpdateStatus()
        {
            if (!_controller.IsAIConfigured)
            {
                _statusLabel.text = $"Set API Key in:\n{_configReader.ConfigFilePath}";
                _statusLabel.style.color = new Color(1f, 0.7f, 0.3f);
            }
            else
            {
                _statusLabel.text = "";
            }
        }
    }
}
