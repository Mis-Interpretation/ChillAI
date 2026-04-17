using System.Collections.Generic;
using ChillAI.Controller;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.View.ChatHistory;
using ChillAI.Service.Layout;
using ChillAI.Service.Platform;
using ChillAI.View.EmojiChat;
using ChillAI.View.Profile;
using ChillAI.View.TaskUI;
using ChillAI.View.Window;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.SystemMenu
{
    [RequireComponent(typeof(UIDocument))]
    public class SystemMenuView : MonoBehaviour
    {
        [Inject] UserSettingsService _userSettings;
        [Inject] QuestController _questController;
        [Inject] DisplaySwitchController _displaySwitchController;
        [Inject] SignalBus _signalBus;
        [Inject] UiLayoutController _uiLayout;

        [SerializeField] bool _showActiveProcessHud = true;

        VisualElement _wrapper;
        VisualElement _menuBtn;
        VisualElement _menuPopup;
        VisualElement _settingsPanel;
        WindowDragManipulator _dragManipulator;

        public VisualElement MenuButton => _menuBtn;
        public WindowDragManipulator MenuDragManipulator => _dragManipulator;
        public UserSettingsService UserSettings => _userSettings;
        Label _processHud;

        // Settings controls
        SliderInt _fpsSlider;
        Slider _volumeSlider;
        Toggle _autoTaskToggle;
        Toggle _dragGuideToggle;
        Label _fpsValue;
        Label _volumeValue;

        // Settings header drag
        WindowDragManipulator _settingsDragManipulator;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            // Wrap all root children so dragging the menu button moves everything together.
            // Without filling the root, the wrapper gets no layout size (NaN); absolutely
            // positioned children then have no valid containing block and disappear at runtime.
            _wrapper = new VisualElement { pickingMode = PickingMode.Ignore };
            _wrapper.style.position = Position.Absolute;
            _wrapper.style.left = 0;
            _wrapper.style.right = 0;
            _wrapper.style.top = 0;
            _wrapper.style.bottom = 0;
            while (root.childCount > 0)
                _wrapper.Add(root[0]);
            root.Add(_wrapper);

            _uiLayout.RegisterMenuWrapper(_wrapper);

            _menuBtn = root.Q("menu-btn");
            _menuPopup = root.Q<VisualElement>("menu-popup");
            _settingsPanel = root.Q<VisualElement>("settings-panel");
            _processHud = root.Q<Label>("active-process-hud");
            if (_processHud != null)
                _processHud.style.display = _showActiveProcessHud ? DisplayStyle.Flex : DisplayStyle.None;

            _signalBus?.Subscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);

            // Menu button: hold to drag entire menu, click to toggle menu
            _dragManipulator = new WindowDragManipulator(
                _wrapper,
                dragThreshold: 5f,
                alsoMove: CollectHudCompanionRoots().ToArray());
            _dragManipulator.OnClicked += ToggleMenu;
            _dragManipulator.DragEnded += OnHudLayoutChanged;
            _menuBtn.AddManipulator(_dragManipulator);
            RefreshMenuCompanionRoots();
            root.schedule.Execute(RefreshMenuCompanionRoots).StartingIn(16);
            root.schedule.Execute(RefreshMenuCompanionRoots).StartingIn(250);

            // Menu items
            root.Q<Button>("settings-btn").clicked += OpenSettings;
            root.Q<Button>("profile-insight-btn").clicked += OnProfileInsight;
            root.Q<Button>("chat-history-btn").clicked += OnChatHistory;
            root.Q<Button>("switch-display-btn").clicked += OnSwitchDisplay;
            root.Q<Button>("exit-btn").clicked += OnExit;
            root.Q<Button>("reset-quest-btn").clicked += OnResetQuest;

            // Settings panel
            root.Q<Button>("settings-close-btn").clicked += CloseSettings;

            var settingsHeader = root.Q<VisualElement>(className: "settings-header");
            _settingsDragManipulator = new WindowDragManipulator(_settingsPanel);
            _settingsDragManipulator.DragEnded += OnHudLayoutChanged;
            settingsHeader.AddManipulator(_settingsDragManipulator);

            // Settings sliders
            var data = _userSettings.Data;

            _fpsSlider = root.Q<SliderInt>("fps-slider");
            _fpsValue = root.Q<Label>("fps-value");
            _volumeSlider = root.Q<Slider>("volume-slider");
            _volumeValue = root.Q<Label>("volume-value");

            _fpsSlider.value = data.targetFrameRate;
            _volumeSlider.value = Mathf.Clamp01(data.globalVolume);
            UpdateValueLabels();

            _autoTaskToggle = root.Q<Toggle>("auto-task-toggle");
            _autoTaskToggle.value = data.autoGenerateTasks;
            _autoTaskToggle.RegisterValueChangedCallback(OnAutoTaskChanged);

            _dragGuideToggle = root.Q<Toggle>("drag-guide-toggle");
            _dragGuideToggle.value = data.knowsDragMenu;
            _dragGuideToggle.RegisterValueChangedCallback(OnDragGuideChanged);

            _fpsSlider.RegisterValueChangedCallback(OnFpsChanged);
            _volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);

            // Click outside menu to close
            root.RegisterCallback<PointerDownEvent>(OnRootPointerDown);
        }

        static List<VisualElement> CollectHudCompanionRoots()
        {
            var list = new List<VisualElement>();
            var chats = Object.FindObjectsByType<EmojiChatPanelView>(FindObjectsSortMode.None);
            if (chats.Length > 0)
            {
                var r = chats[0].GetComponent<UIDocument>()?.rootVisualElement;
                AddUniqueRoot(list, r);
            }

            var tasks = Object.FindObjectsByType<TaskPanelView>(FindObjectsSortMode.None);
            if (tasks.Length > 0)
            {
                var r = tasks[0].GetComponent<UIDocument>()?.rootVisualElement;
                AddUniqueRoot(list, r);
            }

            var profiles = Object.FindObjectsByType<ProfileInsightPanelView>(FindObjectsSortMode.None);
            if (profiles.Length > 0)
            {
                var r = profiles[0].GetComponent<UIDocument>()?.rootVisualElement;
                AddUniqueRoot(list, r);
            }

            var historyPanels = Object.FindObjectsByType<ChatHistoryPanelView>(FindObjectsSortMode.None);
            if (historyPanels.Length > 0)
            {
                var r = historyPanels[0].GetComponent<UIDocument>()?.rootVisualElement;
                AddUniqueRoot(list, r);
            }

            return list;
        }

        static void AddUniqueRoot(List<VisualElement> list, VisualElement root)
        {
            if (root == null || list.Contains(root))
                return;

            list.Add(root);
        }

        void RefreshMenuCompanionRoots()
        {
            if (_dragManipulator == null) return;
            _dragManipulator.SetAlsoMoveTargets(CollectHudCompanionRoots().ToArray());
        }

        void OnDisable()
        {
            _signalBus?.TryUnsubscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);

            if (_dragManipulator != null)
            {
                _menuBtn?.RemoveManipulator(_dragManipulator);
                _dragManipulator.OnClicked -= ToggleMenu;
                _dragManipulator.DragEnded -= OnHudLayoutChanged;
            }

            var settingsHeader = _settingsPanel?.Q<VisualElement>(className: "settings-header");
            if (_settingsDragManipulator != null)
            {
                _settingsDragManipulator.DragEnded -= OnHudLayoutChanged;
                settingsHeader?.RemoveManipulator(_settingsDragManipulator);
            }

            _uiLayout?.UnregisterMenuWrapper();

            var root = GetComponent<UIDocument>()?.rootVisualElement;
            root?.UnregisterCallback<PointerDownEvent>(OnRootPointerDown);
        }

        void OnActiveProcessChanged(ActiveProcessChangedSignal signal)
        {
            if (_showActiveProcessHud && _processHud != null)
                _processHud.text = $"{signal.ProcessName} - {signal.Category}";
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            // Close popup if clicking outside menu button and popup
            if (_menuPopup.ClassListContains("hidden")) return;

            var target = evt.target as VisualElement;
            if (IsChildOf(target, _menuPopup) || target == _menuBtn) return;

            _menuPopup.EnableInClassList("hidden", true);
        }

        static bool IsChildOf(VisualElement element, VisualElement parent)
        {
            while (element != null)
            {
                if (element == parent) return true;
                element = element.parent;
            }
            return false;
        }

        void ToggleMenu()
        {
            bool show = _menuPopup.ClassListContains("hidden");
            _menuPopup.EnableInClassList("hidden", !show);

            if (show)
                _settingsPanel.EnableInClassList("hidden", true);
        }

        void OpenSettings()
        {
            _menuPopup.EnableInClassList("hidden", true);
            _settingsPanel.EnableInClassList("hidden", false);
        }

        void CloseSettings()
        {
            _settingsPanel.EnableInClassList("hidden", true);
        }

        void OnProfileInsight()
        {
            _menuPopup.EnableInClassList("hidden", true);
            _signalBus?.Fire<ToggleProfileInsightPanelSignal>();
        }

        void OnChatHistory()
        {
            _menuPopup.EnableInClassList("hidden", true);
            _signalBus?.Fire<ToggleChatHistoryPanelSignal>();
        }

        void UpdateValueLabels()
        {
            _fpsValue.text = $"{_fpsSlider.value}";
            _volumeValue.text = $"{Mathf.RoundToInt(_volumeSlider.value * 100)}%";
        }

        void OnFpsChanged(ChangeEvent<int> evt)
        {
            _userSettings.Data.targetFrameRate = evt.newValue;
            _fpsValue.text = $"{evt.newValue}";
            Application.targetFrameRate = evt.newValue;
            _userSettings.Save();
        }

        void OnVolumeChanged(ChangeEvent<float> evt)
        {
            var v = Mathf.Clamp01(evt.newValue);
            _userSettings.Data.globalVolume = v;
            _volumeValue.text = $"{Mathf.RoundToInt(v * 100)}%";
            AudioListener.volume = v;
            _userSettings.Save();
        }

        void OnAutoTaskChanged(ChangeEvent<bool> evt)
        {
            _userSettings.Data.autoGenerateTasks = evt.newValue;
            _userSettings.Save();
        }

        void OnDragGuideChanged(ChangeEvent<bool> evt)
        {
            _userSettings.Data.knowsDragMenu = evt.newValue;
            _userSettings.Save();
        }

        void OnSwitchDisplay()
        {
            _menuPopup.EnableInClassList("hidden", true);
#if !UNITY_EDITOR
            _displaySwitchController.CycleToNextDisplay();
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.schedule.Execute(() => _uiLayout.RebindToCurrentContext()).StartingIn(48);
#endif
        }

        void OnHudLayoutChanged()
        {
            _uiLayout?.RequestSave();
        }

        void OnExit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnResetQuest()
        {
            _questController?.ResetProgress();
        }
    }
}
