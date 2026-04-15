using ChillAI.Controller;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Service.Layout;
using ChillAI.View.UI;
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
        EmojiPaletteData _emojiPalette;

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
        VisualElement _toggleEmojiBubbleStack;
        readonly List<ToggleEmojiBubbleEntry> _activeToggleEmojiBubbles = new();
        Coroutine _toggleEmojiSpawnCoroutine;
        string _lastErrorMessage;
        float _lastErrorShownAt = -100f;

        [Header("Toggle Emoji Bubble")]
        [SerializeField] bool enableToggleEmojiBubbles = true;
        [SerializeField, Min(0f)] float toggleEmojiFirstBubbleDelaySeconds = 0f;
        [SerializeField, Min(0f)] float toggleEmojiSpawnIntervalSeconds = 0.16f;
        [SerializeField, Min(0f)] float toggleEmojiBubbleLifetimeSeconds = 2.6f;
        [SerializeField, Min(0f)] float toggleEmojiBubbleBaseGap = 12f;
        [SerializeField, Min(0f)] float toggleEmojiBubbleVerticalSpacing = 8f;

        class ToggleEmojiBubbleEntry
        {
            public TextBubble Bubble;
            public IVisualElementScheduledItem DismissTask;
        }

        [Inject]
        public void Construct(
            SignalBus signalBus,
            EmojiChatController controller,
            IConfigReader configReader,
            UserSettingsService userSettings,
            UiLayoutController uiLayout,
            EmojiPaletteData emojiPalette)
        {
            _signalBus = signalBus;
            _controller = controller;
            _configReader = configReader;
            _userSettings = userSettings;
            _uiLayout = uiLayout;
            _emojiPalette = emojiPalette;
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

            EnsureToggleEmojiBubbleStack();
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

            ClearToggleEmojiBubbles();
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
            if (!string.IsNullOrEmpty(toggleEmoji) && toggleEmoji != _emojiPalette.placeholderEmoji)
                _lastAiEmojiForToggle = toggleEmoji;

            ShowToggleEmojiBubbles(signal.Messages);
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

        List<string> TryGetToggleBubbleEmojis(IReadOnlyList<string> messages)
        {
            var result = new List<string>();
            if (messages == null || messages.Count == 0)
                return result;

            for (var i = 0; i < messages.Count; i++)
            {
                var content = messages[i]?.Trim();
                if (string.IsNullOrEmpty(content) || IsTaskMarker(content))
                    continue;

                var emojis = ExtractEmojis(content);
                EnsurePlaceholderEmojisIncluded(content, emojis);
                if (emojis.Count > 0)
                    result.AddRange(emojis);
            }

            return result;
        }

        void EnsurePlaceholderEmojisIncluded(string content, List<string> emojis)
        {
            if (string.IsNullOrEmpty(content) || emojis == null)
                return;

            var placeholder = _emojiPalette?.placeholderEmoji;
            if (string.IsNullOrEmpty(placeholder))
                return;

            var totalPlaceholderCount = CountOccurrences(content, placeholder);
            if (totalPlaceholderCount <= 0)
                return;

            var existingPlaceholderCount = 0;
            for (var i = 0; i < emojis.Count; i++)
            {
                if (string.Equals(emojis[i], placeholder, StringComparison.Ordinal))
                    existingPlaceholderCount++;
            }

            for (var i = existingPlaceholderCount; i < totalPlaceholderCount; i++)
                emojis.Add(placeholder);
        }

        static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
                return 0;

            var count = 0;
            var index = 0;
            while (index <= text.Length - value.Length)
            {
                var found = text.IndexOf(value, index, StringComparison.Ordinal);
                if (found < 0)
                    break;

                count++;
                index = found + value.Length;
            }

            return count;
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

        static List<string> ExtractEmojis(string content)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(content))
                return result;

            var enumerator = StringInfo.GetTextElementEnumerator(content);
            while (enumerator.MoveNext())
            {
                var textElement = enumerator.GetTextElement();
                if (IsEmojiTextElement(textElement))
                    result.Add(NormalizeEmojiForToggle(textElement));
            }

            return result;
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

        void ShowToggleEmojiBubbles(IReadOnlyList<string> messages)
        {
            if (_toggleBtn == null)
                return;

            if (!enableToggleEmojiBubbles)
            {
                ClearToggleEmojiBubbles();
                return;
            }

            var emojis = TryGetToggleBubbleEmojis(messages);
            if (emojis.Count == 0)
                return;

            if (_toggleEmojiSpawnCoroutine != null)
                StopCoroutine(_toggleEmojiSpawnCoroutine);
            _toggleEmojiSpawnCoroutine = StartCoroutine(SpawnToggleEmojiBubbles(emojis));
        }

        System.Collections.IEnumerator SpawnToggleEmojiBubbles(List<string> emojis)
        {
            var firstDelay = Mathf.Max(0f, toggleEmojiFirstBubbleDelaySeconds);
            var delay = Mathf.Max(0f, toggleEmojiSpawnIntervalSeconds);
            if (firstDelay > 0f)
                yield return new WaitForSecondsRealtime(firstDelay);

            for (var i = 0; i < emojis.Count; i++)
            {
                CreateToggleEmojiBubble(emojis[i]);
                if (i < emojis.Count - 1 && delay > 0f)
                    yield return new WaitForSecondsRealtime(delay);
            }

            _toggleEmojiSpawnCoroutine = null;
        }

        void CreateToggleEmojiBubble(string emojiText)
        {
            if (string.IsNullOrWhiteSpace(emojiText) || !EnsureToggleEmojiBubbleStack())
                return;

            var entry = new ToggleEmojiBubbleEntry
            {
                Bubble = new TextBubble(emojiText, showDelayMs: 0, useAbsolutePosition: false)
                    .ApplyChatPanelAiBubbleStyle(hideTail: true)
            };

            _activeToggleEmojiBubbles.Add(entry); // Newest bubble is appended to bottom in vertical stack.
            entry.Bubble.Attach(_toggleEmojiBubbleStack);
            RefreshToggleEmojiBubbleStackSpacing();

            var lifetimeMs = Mathf.RoundToInt(Mathf.Max(0f, toggleEmojiBubbleLifetimeSeconds) * 1000f);
            entry.DismissTask = _toggleBtn.schedule.Execute(() => DismissToggleEmojiBubbleEntry(entry))
                .StartingIn(lifetimeMs);
        }

        void DismissToggleEmojiBubbleEntry(ToggleEmojiBubbleEntry entry)
        {
            if (entry == null)
                return;

            entry.DismissTask?.Pause();
            entry.DismissTask = null;
            entry.Bubble?.Dismiss();
            _activeToggleEmojiBubbles.Remove(entry);
            RefreshToggleEmojiBubbleStackSpacing();
        }

        bool EnsureToggleEmojiBubbleStack()
        {
            if (_toggleBtn == null)
                return false;

            if (_toggleEmojiBubbleStack != null && _toggleEmojiBubbleStack.parent == _toggleBtn)
                return true;

            _toggleEmojiBubbleStack = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            _toggleEmojiBubbleStack.style.position = Position.Absolute;
            _toggleEmojiBubbleStack.style.left = Length.Percent(50);
            _toggleEmojiBubbleStack.style.bottom = Length.Percent(100);
            _toggleEmojiBubbleStack.style.marginBottom = toggleEmojiBubbleBaseGap;
            _toggleEmojiBubbleStack.style.translate = new StyleTranslate(new Translate(Length.Percent(-50), 0));
            _toggleEmojiBubbleStack.style.flexDirection = FlexDirection.Column;
            _toggleEmojiBubbleStack.style.alignItems = Align.Center;
            _toggleEmojiBubbleStack.style.justifyContent = Justify.FlexStart;

            _toggleBtn.Add(_toggleEmojiBubbleStack);
            return true;
        }

        void RefreshToggleEmojiBubbleStackSpacing()
        {
            for (var i = 0; i < _activeToggleEmojiBubbles.Count; i++)
            {
                var bubble = _activeToggleEmojiBubbles[i].Bubble;
                if (bubble == null)
                    continue;

                bubble.style.marginTop = i == 0 ? 0f : toggleEmojiBubbleVerticalSpacing;
            }
        }

        void ClearToggleEmojiBubbles()
        {
            if (_toggleEmojiSpawnCoroutine != null)
            {
                StopCoroutine(_toggleEmojiSpawnCoroutine);
                _toggleEmojiSpawnCoroutine = null;
            }

            for (var i = 0; i < _activeToggleEmojiBubbles.Count; i++)
            {
                var entry = _activeToggleEmojiBubbles[i];
                entry.DismissTask?.Pause();
                entry.DismissTask = null;
                entry.Bubble?.Dismiss();
            }

            _activeToggleEmojiBubbles.Clear();
            _toggleEmojiBubbleStack?.RemoveFromHierarchy();
            _toggleEmojiBubbleStack = null;
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
