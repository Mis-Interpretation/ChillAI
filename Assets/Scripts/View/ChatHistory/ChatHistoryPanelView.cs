using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.ChatHistory;
using ChillAI.Service.Layout;
using ChillAI.View.Window;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.ChatHistory
{
    [RequireComponent(typeof(UIDocument))]
    public class ChatHistoryPanelView : MonoBehaviour
    {
        const string HiddenClass = "hidden";

        SignalBus _signalBus;
        IChatHistoryReader _chatHistoryReader;
        UserSettingsService _userSettings;
        UiLayoutController _uiLayout;

        VisualElement _panel;
        Button _closeBtn;
        ScrollView _historyMessages;
        VisualElement _resizeHandle;

        bool _panelVisible;
        WindowDragManipulator _dragManipulator;
        PanelResizeManipulator _resizeManipulator;

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
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _panel = root.Q<VisualElement>("chat-history-panel");
            _closeBtn = root.Q<Button>("chat-history-close-btn");
            _historyMessages = root.Q<ScrollView>("chat-history-messages");
            _resizeHandle = root.Q<VisualElement>("chat-history-resize-handle");

            _uiLayout.RegisterChatHistoryHudRoot(root);
            _uiLayout.RegisterChatHistoryPanel(_panel);

            _panelVisible = !_panel.ClassListContains(HiddenClass);
            _closeBtn.clicked += OnClose;

            var header = root.Q<VisualElement>(className: "chat-history-panel-header");
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
        }

        void OnDisable()
        {
            _closeBtn.clicked -= OnClose;

            var header = _panel?.Q<VisualElement>(className: "chat-history-panel-header");
            if (_dragManipulator != null)
            {
                header?.RemoveManipulator(_dragManipulator);
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            if (_resizeManipulator != null && _resizeHandle != null)
                _resizeHandle.RemoveManipulator(_resizeManipulator);

            _uiLayout?.UnregisterChatHistoryHudRoot();
            _uiLayout?.UnregisterChatHistoryPanel();

            _signalBus?.TryUnsubscribe<ToggleChatHistoryPanelSignal>(OnTogglePanelSignal);
            _signalBus?.TryUnsubscribe<EmojiChatResponseSignal>(OnEmojiResponse);
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
                RefreshHistory();
            _uiLayout?.RequestSave();
        }

        void OnEmojiResponse(EmojiChatResponseSignal _)
        {
            if (_panelVisible)
                RefreshHistory();
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void RefreshHistory()
        {
            _historyMessages.Clear();

            var history = _chatHistoryReader.GetHistory(AgentRegistry.Ids.EmojiChat);
            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Content))
                    continue;

                AddBubble(entry.Content.Trim(), IsUserRole(entry.Role));
            }

            _historyMessages.schedule.Execute(() =>
            {
                if (_historyMessages == null || _historyMessages.panel == null)
                    return;

                _historyMessages.scrollOffset = new Vector2(0, _historyMessages.contentContainer.layout.height);
            }).StartingIn(0);
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

        static bool IsUserRole(string role)
        {
            return string.Equals(role?.Trim(), "user", StringComparison.OrdinalIgnoreCase);
        }
    }
}
