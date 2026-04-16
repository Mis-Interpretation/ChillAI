using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.ChatHistory;
using ChillAI.Service.Layout;
using ChillAI.View.Window;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.ChatHistory
{
    [RequireComponent(typeof(UIDocument))]
    public class ChatHistoryPanelView : MonoBehaviour
    {
        const string HiddenClass = "hidden";
        const int DefaultPageSize = 20;

        SignalBus _signalBus;
        IChatHistoryReader _chatHistoryReader;
        UserSettingsService _userSettings;
        UiLayoutController _uiLayout;

        VisualElement _panel;
        Button _closeBtn;
        ScrollView _historyMessages;
        VisualElement _resizeHandle;
        Button _prevPageBtn;
        Button _nextPageBtn;
        Label _pageLabel;

        bool _panelVisible;
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;
        bool _isSetup;
        int _currentPageFromLatest;
        bool _hasReachedOldestPage;
        int _lastHistoryCount = -1;
        IReadOnlyList<ChatHistoryEntry> _history;

        [SerializeField, Min(1)]
        int _pageSize = DefaultPageSize;

        readonly List<PageData> _cachedPagesFromLatest = new();

        public int PageSize
        {
            get => _pageSize;
            set
            {
                _pageSize = Mathf.Max(1, value);
                if (_isSetup && _panelVisible)
                    RefreshHistory(jumpToLastPage: true);
            }
        }

        [Inject]
        public void Construct(
            SignalBus signalBus,
            IChatHistoryReader chatHistoryReader,
            UserSettingsService userSettings,
            UiLayoutController uiLayout)
        {
            _signalBus = signalBus;
            _chatHistoryReader = chatHistoryReader;
            _userSettings = userSettings;
            _uiLayout = uiLayout;

            TrySetup();
        }

        void OnEnable()
        {
            TrySetup();
        }

        void OnDisable()
        {
            if (!_isSetup)
                return;

            if (_closeBtn != null)
                _closeBtn.clicked -= OnClose;
            if (_prevPageBtn != null)
                _prevPageBtn.clicked -= OnPrevPage;
            if (_nextPageBtn != null)
                _nextPageBtn.clicked -= OnNextPage;

            if (_dragManipulator != null)
            {
                var header = _panel?.Q<VisualElement>(className: "chat-history-panel-header");
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            _uiLayout?.UnregisterChatHistoryHudRoot();
            _uiLayout?.UnregisterChatHistoryPanel();

            _signalBus?.TryUnsubscribe<ToggleChatHistoryPanelSignal>(OnTogglePanelSignal);
            _signalBus?.TryUnsubscribe<EmojiChatResponseSignal>(OnEmojiResponse);

            _isSetup = false;
        }

        void TrySetup()
        {
            if (_isSetup || !isActiveAndEnabled)
                return;

            if (_chatHistoryReader == null || _userSettings == null || _uiLayout == null)
                return;

            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;

            _panel = root.Q<VisualElement>("chat-history-panel");
            _closeBtn = root.Q<Button>("chat-history-close-btn");
            _historyMessages = root.Q<ScrollView>("chat-history-messages");
            _resizeHandle = root.Q<VisualElement>("chat-history-resize-handle");
            _prevPageBtn = root.Q<Button>("chat-history-prev-page-btn");
            _nextPageBtn = root.Q<Button>("chat-history-next-page-btn");
            _pageLabel = root.Q<Label>("chat-history-page-label");
            var header = root.Q<VisualElement>(className: "chat-history-panel-header");

            if (_panel == null || _closeBtn == null || _historyMessages == null || header == null || _prevPageBtn == null || _nextPageBtn == null || _pageLabel == null)
            {
                Debug.LogWarning($"[{nameof(ChatHistoryPanelView)}] Required UI elements are missing in UIDocument.");
                return;
            }

            _uiLayout.RegisterChatHistoryHudRoot(root);
            _uiLayout.RegisterChatHistoryPanel(_panel);

            _panelVisible = !_panel.ClassListContains(HiddenClass);
            _closeBtn.clicked += OnClose;
            _prevPageBtn.clicked += OnPrevPage;
            _nextPageBtn.clicked += OnNextPage;

            _dragManipulator = new WindowDragManipulator(_panel);
            _dragManipulator.DragEnded += OnHudLayoutChanged;
            header.AddManipulator(_dragManipulator);

            if (_resizeHandle != null)
            {
                var minWidth = Mathf.Max(240, _userSettings.Data.chatPanelMinWidth);
                var minHeight = Mathf.Max(180, _userSettings.Data.chatPanelMinHeight);
                _resizeManipulator = new PanelResizeManipulator(_panel, minWidth, minHeight, OnHudLayoutChanged);
                _resizeHandle.AddManipulator(_resizeManipulator);
            }

            _signalBus?.Subscribe<ToggleChatHistoryPanelSignal>(OnTogglePanelSignal);
            _signalBus?.Subscribe<EmojiChatResponseSignal>(OnEmojiResponse);
            _isSetup = true;

            if (_panelVisible)
                RefreshHistory(jumpToLastPage: true);
            else
                UpdatePaginationControls();
        }

        void OnTogglePanelSignal()
        {
            SetPanelVisible(!_panelVisible);
        }

        void OnClose()
        {
            SetPanelVisible(false);
        }

        void SetPanelVisible(bool visible)
        {
            _panelVisible = visible;
            _panel.EnableInClassList(HiddenClass, !visible);
            if (visible)
                RefreshHistory(jumpToLastPage: true);
            _uiLayout?.RequestSave();
        }

        void OnEmojiResponse(EmojiChatResponseSignal _)
        {
            if (_panelVisible)
                RefreshHistory(jumpToLastPage: true);
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void OnPrevPage()
        {
            var target = _currentPageFromLatest + 1;
            if (!EnsurePageLoaded(target))
                return;

            _currentPageFromLatest = target;
            RenderCurrentPage();
        }

        void OnNextPage()
        {
            if (_currentPageFromLatest <= 0)
                return;

            _currentPageFromLatest--;
            RenderCurrentPage();
        }

        void RefreshHistory(bool jumpToLastPage = false)
        {
            if (_historyMessages == null)
                return;

            _history = _chatHistoryReader.GetHistory(AgentRegistry.Ids.EmojiChat);
            if (_history == null)
                _history = Array.Empty<ChatHistoryEntry>();

            var historyChanged = _history.Count != _lastHistoryCount;
            if (historyChanged)
            {
                _lastHistoryCount = _history.Count;
                InvalidatePageCache();
            }

            if (jumpToLastPage)
                _currentPageFromLatest = 0;

            EnsurePageLoaded(_currentPageFromLatest);
            RenderCurrentPage();
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
            _historyMessages.Add(row);
        }

        void UpdatePaginationControls()
        {
            if (_pageLabel != null)
            {
                var total = _hasReachedOldestPage
                    ? _cachedPagesFromLatest.Count.ToString()
                    : "?";
                _pageLabel.text = $"Page {_currentPageFromLatest + 1}/{total} (按轮次)";
            }

            var canGoOlder = !_hasReachedOldestPage || _currentPageFromLatest < _cachedPagesFromLatest.Count - 1;
            _prevPageBtn?.SetEnabled(canGoOlder);
            _nextPageBtn?.SetEnabled(_currentPageFromLatest > 0);
        }

        void RenderCurrentPage()
        {
            _historyMessages.Clear();

            if (_currentPageFromLatest < 0 || _currentPageFromLatest >= _cachedPagesFromLatest.Count)
            {
                UpdatePaginationControls();
                return;
            }

            var page = _cachedPagesFromLatest[_currentPageFromLatest];
            for (int i = 0; i < page.Entries.Count; i++)
            {
                var item = page.Entries[i];
                AddBubble(item.Text, item.IsUser);
            }

            UpdatePaginationControls();

            _historyMessages.schedule.Execute(() =>
            {
                if (_historyMessages == null || _historyMessages.panel == null)
                    return;

                _historyMessages.scrollOffset = new Vector2(0, 0f);
            }).StartingIn(0);
        }

        bool EnsurePageLoaded(int pageIndexFromLatest)
        {
            if (pageIndexFromLatest < 0)
                return false;

            while (_cachedPagesFromLatest.Count <= pageIndexFromLatest && !_hasReachedOldestPage)
            {
                var page = BuildNextOlderPage();
                if (page == null)
                    break;

                _cachedPagesFromLatest.Add(page);
            }

            return pageIndexFromLatest < _cachedPagesFromLatest.Count;
        }

        PageData BuildNextOlderPage()
        {
            if (_history == null || _history.Count == 0)
            {
                _hasReachedOldestPage = true;
                return null;
            }

            var endIndex = _cachedPagesFromLatest.Count == 0
                ? _history.Count - 1
                : _cachedPagesFromLatest[_cachedPagesFromLatest.Count - 1].StartHistoryIndex - 1;

            if (endIndex < 0)
            {
                _hasReachedOldestPage = true;
                return null;
            }

            var pageEntries = new List<DisplayEntry>();
            var pageStartIndex = endIndex;
            var bubbleCount = 0;
            var pageSize = Mathf.Max(1, _pageSize);
            var scanEnd = endIndex;

            while (scanEnd >= 0)
            {
                var roundStart = FindRoundStart(scanEnd);
                var roundEntries = BuildRoundEntries(roundStart, scanEnd);
                scanEnd = roundStart - 1;

                if (roundEntries.Count == 0)
                    continue;

                if (bubbleCount > 0 && bubbleCount + roundEntries.Count > pageSize)
                    break;

                pageEntries.InsertRange(0, roundEntries);
                bubbleCount += roundEntries.Count;
                pageStartIndex = roundStart;
            }

            if (pageEntries.Count == 0)
            {
                _hasReachedOldestPage = true;
                return null;
            }

            if (pageStartIndex <= 0)
                _hasReachedOldestPage = true;

            return new PageData(pageStartIndex, endIndex, pageEntries);
        }

        int FindRoundStart(int endIndex)
        {
            for (int i = endIndex; i >= 0; i--)
            {
                if (IsDisplayableUserEntry(_history[i]))
                    return i;
            }

            return 0;
        }

        List<DisplayEntry> BuildRoundEntries(int startIndex, int endIndex)
        {
            var result = new List<DisplayEntry>();
            for (int i = startIndex; i <= endIndex; i++)
                AppendEntryDisplay(_history[i], result);
            return result;
        }

        void AppendEntryDisplay(ChatHistoryEntry entry, List<DisplayEntry> target)
        {
            if (entry == null || target == null)
                return;

            if (IsUserRole(entry.Role))
            {
                var userText = entry.Content?.Trim();
                if (!string.IsNullOrWhiteSpace(userText))
                    target.Add(new DisplayEntry(userText, isUser: true));
                return;
            }

            if (!IsAssistantRole(entry.Role))
                return;

            var rawContent = entry.Content?.Trim();
            if (string.IsNullOrWhiteSpace(rawContent))
                return;

            var parsed = TryParse<EmojiChatResponsePayload>(rawContent);
            if (parsed?.messages != null && parsed.messages.Count > 0)
            {
                for (int i = 0; i < parsed.messages.Count; i++)
                {
                    var message = parsed.messages[i]?.Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                        target.Add(new DisplayEntry(message, isUser: false));
                }

                return;
            }

            if (LooksLikeJson(rawContent))
                return;

            target.Add(new DisplayEntry(rawContent, isUser: false));
        }

        bool IsDisplayableUserEntry(ChatHistoryEntry entry)
        {
            return entry != null &&
                   IsUserRole(entry.Role) &&
                   !string.IsNullOrWhiteSpace(entry.Content);
        }

        void InvalidatePageCache()
        {
            _cachedPagesFromLatest.Clear();
            _hasReachedOldestPage = false;
        }

        static bool IsUserRole(string role)
        {
            return string.Equals(role?.Trim(), "user", StringComparison.OrdinalIgnoreCase);
        }

        static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        static bool IsAssistantRole(string role)
        {
            return string.Equals(role?.Trim(), "assistant", StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        class EmojiChatResponsePayload
        {
            public List<string> messages;
            public string task_intent;
        }

        readonly struct DisplayEntry
        {
            public readonly string Text;
            public readonly bool IsUser;

            public DisplayEntry(string text, bool isUser)
            {
                Text = text;
                IsUser = isUser;
            }
        }

        sealed class PageData
        {
            public readonly int StartHistoryIndex;
            public readonly int EndHistoryIndex;
            public readonly List<DisplayEntry> Entries;

            public PageData(int startHistoryIndex, int endHistoryIndex, List<DisplayEntry> entries)
            {
                StartHistoryIndex = startHistoryIndex;
                EndHistoryIndex = endHistoryIndex;
                Entries = entries;
            }
        }
    }
}
