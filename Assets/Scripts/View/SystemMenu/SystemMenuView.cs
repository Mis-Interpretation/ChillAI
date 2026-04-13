using System.Collections.Generic;
using ChillAI.Controller;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
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
        [Inject] IWindowService _windowService;
        [Inject] AppSettings _appSettings;
        [Inject] UserSettingsService _userSettings;
        [Inject] QuestController _questController;
        [Inject] DisplaySwitchController _displaySwitchController;
        [Inject] SignalBus _signalBus;
        [Inject] UiLayoutController _uiLayout;

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
        Slider _alphaSlider;
        SliderInt _fpsSlider;
        SliderInt _bubblesSlider;
        Toggle _autoTaskToggle;
        Toggle _dragGuideToggle;
        Label _alphaValue;
        Label _fpsValue;
        Label _bubblesValue;

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

            _signalBus?.Subscribe<ActiveProcessChangedSignal>(OnActiveProcessChanged);

            // Move Chat + Task HUD roots with the system menu (same delta as menu wrapper).
            var companions = CollectHudCompanionRoots();

            // Menu button: hold to drag entire menu, click to toggle menu
            _dragManipulator = new WindowDragManipulator(
                _wrapper,
                dragThreshold: 5f,
                alsoMove: companions.ToArray());
            _dragManipulator.OnClicked += ToggleMenu;
            _dragManipulator.DragEnded += OnHudLayoutChanged;
            _menuBtn.AddManipulator(_dragManipulator);

            // Menu items
            root.Q<Button>("settings-btn").clicked += OpenSettings;
            root.Q<Button>("profile-insight-btn").clicked += OnProfileInsight;
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

            _alphaSlider = root.Q<Slider>("alpha-slider");
            _fpsSlider = root.Q<SliderInt>("fps-slider");
            _bubblesSlider = root.Q<SliderInt>("bubbles-slider");
            _alphaValue = root.Q<Label>("alpha-value");
            _fpsValue = root.Q<Label>("fps-value");
            _bubblesValue = root.Q<Label>("bubbles-value");

            _alphaSlider.value = _appSettings.windowAlpha;
            _fpsSlider.value = data.targetFrameRate;
            _bubblesSlider.value = data.maxChatBubbles;
            UpdateValueLabels();

            _autoTaskToggle = root.Q<Toggle>("auto-task-toggle");
            _autoTaskToggle.value = data.autoGenerateTasks;
            _autoTaskToggle.RegisterValueChangedCallback(OnAutoTaskChanged);

            _dragGuideToggle = root.Q<Toggle>("drag-guide-toggle");
            _dragGuideToggle.value = data.knowsDragMenu;
            _dragGuideToggle.RegisterValueChangedCallback(OnDragGuideChanged);

            _alphaSlider.RegisterValueChangedCallback(OnAlphaChanged);
            _fpsSlider.RegisterValueChangedCallback(OnFpsChanged);
            _bubblesSlider.RegisterValueChangedCallback(OnBubblesChanged);

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
                if (r != null) list.Add(r);
            }

            var tasks = Object.FindObjectsByType<TaskPanelView>(FindObjectsSortMode.None);
            if (tasks.Length > 0)
            {
                var r = tasks[0].GetComponent<UIDocument>()?.rootVisualElement;
                if (r != null) list.Add(r);
            }

            var profiles = Object.FindObjectsByType<ProfileInsightPanelView>(FindObjectsSortMode.None);
            if (profiles.Length > 0)
            {
                var r = profiles[0].GetComponent<UIDocument>()?.rootVisualElement;
                if (r != null) list.Add(r);
            }

            return list;
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
            if (_processHud != null)
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

        void UpdateValueLabels()
        {
            _alphaValue.text = $"{_alphaSlider.value:F1}";
            _fpsValue.text = $"{_fpsSlider.value}";
            _bubblesValue.text = $"{_bubblesSlider.value}";
        }

        void OnAlphaChanged(ChangeEvent<float> evt)
        {
            _appSettings.windowAlpha = evt.newValue;
            _alphaValue.text = $"{evt.newValue:F1}";
#if !UNITY_EDITOR
            _windowService.MakeTransparent(evt.newValue);
#endif
        }

        void OnFpsChanged(ChangeEvent<int> evt)
        {
            _userSettings.Data.targetFrameRate = evt.newValue;
            _fpsValue.text = $"{evt.newValue}";
            Application.targetFrameRate = evt.newValue;
            _userSettings.Save();
        }

        void OnBubblesChanged(ChangeEvent<int> evt)
        {
            _userSettings.Data.maxChatBubbles = evt.newValue;
            _bubblesValue.text = $"{evt.newValue}";
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
