using ChillAI.Core.Settings;
using ChillAI.Service.Platform;
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

            _menuBtn = root.Q("menu-btn");
            _menuPopup = root.Q<VisualElement>("menu-popup");
            _settingsPanel = root.Q<VisualElement>("settings-panel");

            // Menu button: hold to drag window, click to toggle menu
            _dragManipulator = new WindowDragManipulator(_windowService, dragThreshold: 5f);
            _dragManipulator.OnClicked += ToggleMenu;
            _menuBtn.AddManipulator(_dragManipulator);

            // Menu items
            root.Q<Button>("settings-btn").clicked += OpenSettings;
            root.Q<Button>("exit-btn").clicked += OnExit;

            // Settings panel
            root.Q<Button>("settings-close-btn").clicked += CloseSettings;

            var settingsHeader = root.Q<VisualElement>(className: "settings-header");
            _settingsDragManipulator = new WindowDragManipulator(_windowService);
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
