using System.Collections.Generic;
using ChillAI.Controller;
using ChillAI.Core.Settings;
using ChillAI.Service.Platform;
using ChillAI.View.EmojiChat;
using ChillAI.View.HUD;
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
        [Inject] DisplaySwitchController _displaySwitchController;

        VisualElement _wrapper;
        VisualElement _menuBtn;
        VisualElement _menuPopup;
        VisualElement _settingsPanel;
        WindowDragManipulator _dragManipulator;

        // Settings controls
        Slider _alphaSlider;
        SliderInt _fpsSlider;
        SliderInt _bubblesSlider;
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

            _menuBtn = root.Q("menu-btn");
            _menuPopup = root.Q<VisualElement>("menu-popup");
            _settingsPanel = root.Q<VisualElement>("settings-panel");

            // Move Chat + Task HUD roots with the system menu (same delta as menu wrapper).
            var companions = CollectHudCompanionRoots();

            // Menu button: hold to drag entire menu, click to toggle menu
            _dragManipulator = new WindowDragManipulator(
                _wrapper,
                dragThreshold: 5f,
                alsoMove: companions.ToArray(),
                uguiAlsoMove: CollectUguiHudCompanionRects());
            _dragManipulator.OnClicked += ToggleMenu;
            _menuBtn.AddManipulator(_dragManipulator);

            // Menu items
            root.Q<Button>("settings-btn").clicked += OpenSettings;
            root.Q<Button>("switch-display-btn").clicked += OnSwitchDisplay;
            root.Q<Button>("exit-btn").clicked += OnExit;

            // Settings panel
            root.Q<Button>("settings-close-btn").clicked += CloseSettings;

            var settingsHeader = root.Q<VisualElement>(className: "settings-header");
            _settingsDragManipulator = new WindowDragManipulator(_settingsPanel);
            settingsHeader.AddManipulator(_settingsDragManipulator);

            // Settings sliders
            _alphaSlider = root.Q<Slider>("alpha-slider");
            _fpsSlider = root.Q<SliderInt>("fps-slider");
            _bubblesSlider = root.Q<SliderInt>("bubbles-slider");
            _alphaValue = root.Q<Label>("alpha-value");
            _fpsValue = root.Q<Label>("fps-value");
            _bubblesValue = root.Q<Label>("bubbles-value");

            _alphaSlider.value = _appSettings.windowAlpha;
            _fpsSlider.value = _appSettings.targetFrameRate;
            _bubblesSlider.value = _appSettings.maxChatBubbles;
            UpdateValueLabels();

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

            return list;
        }

        static RectTransform[] CollectUguiHudCompanionRects()
        {
            var huds = Object.FindObjectsByType<ActiveProcessHUDView>(FindObjectsSortMode.None);
            if (huds.Length == 0) return null;
            var rt = huds[0].MenuDragCompanionRect;
            return rt != null ? new[] { rt } : null;
        }

        void OnDisable()
        {
            if (_dragManipulator != null)
            {
                _menuBtn?.RemoveManipulator(_dragManipulator);
                _dragManipulator.OnClicked -= ToggleMenu;
            }

            var settingsHeader = _settingsPanel?.Q<VisualElement>(className: "settings-header");
            if (_settingsDragManipulator != null)
                settingsHeader?.RemoveManipulator(_settingsDragManipulator);

            var root = GetComponent<UIDocument>()?.rootVisualElement;
            root?.UnregisterCallback<PointerDownEvent>(OnRootPointerDown);
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
            _appSettings.targetFrameRate = evt.newValue;
            _fpsValue.text = $"{evt.newValue}";
            Application.targetFrameRate = evt.newValue;
        }

        void OnBubblesChanged(ChangeEvent<int> evt)
        {
            _appSettings.maxChatBubbles = evt.newValue;
            _bubblesValue.text = $"{evt.newValue}";
        }

        void OnSwitchDisplay()
        {
            _menuPopup.EnableInClassList("hidden", true);
#if !UNITY_EDITOR
            _displaySwitchController.CycleToNextDisplay();
#endif
        }

        void OnExit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
