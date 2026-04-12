using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Service.Layout;
using ChillAI.View.Window;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        UserSettingsService _userSettings;
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
        const float ErrorDedupWindowSeconds = 3f;

        // Window drag
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;
        VisualElement _resizeHandle;
        string _lastErrorMessage;
        float _lastErrorShownAt = -100f;

        [Inject]
        public void Construct(
            SignalBus signalBus,
            EmojiChatController controller,
            IConfigReader configReader,
            UserSettingsService userSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _controller = controller;
            _configReader = configReader;
            _userSettings = userSettings;
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
                    _userSettings.Data.chatPanelMinWidth,
                    _userSettings.Data.chatPanelMinHeight,
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
                if (ShouldSuppressDuplicateError(signal.ErrorMessage))
                    return;

                SetStatusText(signal.ErrorMessage, isError: true);
                return;
            }

            SetStatusText(string.Empty, isError: false);

            foreach (var msg in signal.Messages)
                AddBubble(msg, false);

            var toggleEmoji = TryGetToggleEmoji(signal.Messages);
            if (!string.IsNullOrEmpty(toggleEmoji))
                _lastAiEmojiForToggle = toggleEmoji;

            RefreshToggleButtonVisual();
        }

        bool ShouldSuppressDuplicateError(string message)
        {
            var normalized = message?.Trim() ?? string.Empty;
            var now = Time.unscaledTime;
            var isDuplicate = string.Equals(_lastErrorMessage, normalized, StringComparison.Ordinal);
            if (!isDuplicate || now - _lastErrorShownAt > ErrorDedupWindowSeconds)
            {
                _lastErrorMessage = normalized;
                _lastErrorShownAt = now;
                return false;
            }

            return true;
        }

        void SetStatusText(string message, bool isError)
        {
            _statusLabel.text = message ?? string.Empty;
            _statusLabel.EnableInClassList("chat-status-label--error", isError && !string.IsNullOrEmpty(message));
            _statusLabel.EnableInClassList("chat-status-label--hint", !isError && !string.IsNullOrEmpty(message));
        }

        void RefreshToggleButtonVisual()
        {
            if (_toggleBtn == null) return;

            var useEmoji = !_panelVisible && !string.IsNullOrEmpty(_lastAiEmojiForToggle);
            _toggleBtn.text = useEmoji ? _lastAiEmojiForToggle : DefaultToggleText;
            _toggleBtn.EnableInClassList("chat-toggle-btn--last-emoji", useEmoji);
        }

        static string TryGetToggleEmoji(IReadOnlyList<string> messages)
        {
            if (messages == null || messages.Count == 0)
                return null;

            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var content = messages[i]?.Trim();
                if (string.IsNullOrEmpty(content) || IsTaskMarker(content))
                    continue;

                var lastSentence = GetLastSentence(content);
                var firstEmoji = ExtractFirstEmoji(lastSentence);
                if (!string.IsNullOrEmpty(firstEmoji))
                    return firstEmoji;
            }

            return null;
        }

        static bool IsTaskMarker(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            var compact = content.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            compact = compact.Replace("\uFE0F", "");
            return compact == "\u270D\u270D\u270D";
        }

        static string GetLastSentence(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            var normalized = content.Replace("\r\n", "\n");
            var lineParts = normalized.Split('\n');
            for (var i = lineParts.Length - 1; i >= 0; i--)
            {
                var line = lineParts[i]?.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var sentenceParts = line.Split('。', '！', '？', '!', '?', '.');
                for (var j = sentenceParts.Length - 1; j >= 0; j--)
                {
                    var sentence = sentenceParts[j]?.Trim();
                    if (!string.IsNullOrEmpty(sentence))
                        return sentence;
                }
            }

            return normalized.Trim();
        }

        static string ExtractFirstEmoji(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var enumerator = StringInfo.GetTextElementEnumerator(content);
            while (enumerator.MoveNext())
            {
                var textElement = enumerator.GetTextElement();
                if (IsEmojiTextElement(textElement))
                    return NormalizeEmojiForToggle(textElement);
            }

            return null;
        }

        static bool IsEmojiTextElement(string textElement)
        {
            if (string.IsNullOrEmpty(textElement))
                return false;

            var hasEmojiBase = false;
            for (var i = 0; i < textElement.Length; i++)
            {
                var codePoint = char.ConvertToUtf32(textElement, i);
                if (char.IsSurrogatePair(textElement, i))
                    i++;

                if (IsEmojiBaseCodePoint(codePoint))
                    hasEmojiBase = true;
            }

            return hasEmojiBase;
        }

        static bool IsEmojiBaseCodePoint(int codePoint)
        {
            return (codePoint >= 0x1F000 && codePoint <= 0x1FAFF) ||
                   (codePoint >= 0x2600 && codePoint <= 0x27BF) ||
                   (codePoint >= 0x2300 && codePoint <= 0x23FF) ||
                   (codePoint >= 0x2B00 && codePoint <= 0x2BFF);
        }

        static string NormalizeEmojiForToggle(string emoji)
        {
            if (string.IsNullOrEmpty(emoji))
                return emoji;

            // UI Toolkit may render gender/role ZWJ emoji as split glyphs.
            // Fallback to the leading base emoji to keep toggle icon stable.
            var zwjIndex = emoji.IndexOf('\u200D');
            if (zwjIndex <= 0)
                return emoji;

            var basePart = emoji.Substring(0, zwjIndex).Trim();
            return IsEmojiTextElement(basePart) ? basePart : emoji;
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
            while (_chatMessages.contentContainer.childCount > _userSettings.Data.maxChatBubbles)
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
                SetStatusText($"Set API Key in:\n{_configReader.ConfigFilePath}", isError: false);
            }
            else
            {
                SetStatusText(string.Empty, isError: false);
            }
        }
    }
}
