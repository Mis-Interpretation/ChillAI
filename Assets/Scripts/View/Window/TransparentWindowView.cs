using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Service.Layout;
using ChillAI.Service.Platform;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace ChillAI.View.Window
{
    public class TransparentWindowView : MonoBehaviour
    {
        [Inject] IWindowService _windowService;
        [Inject] AppSettings _appSettings;
        [Inject] UserSettingsService _userSettings;
        [Inject] IConfigReader _configReader;
        [Inject] UiLayoutController _uiLayout;

#if !UNITY_EDITOR
        bool _clickThroughActive;
        IPanel _panel;
#endif

        void Start()
        {
            Application.targetFrameRate = _userSettings.Data.targetFrameRate;
            Application.runInBackground = true;

#if UNITY_EDITOR
            Debug.Log("[ChillAI] Window transparency skipped in Editor.");
#else
            float alpha = _configReader.GetEffectiveFloat(
                _configReader.Config.windowAlphaOverride,
                _appSettings.windowAlpha);

            _windowService.MakeTransparent(alpha);
            _windowService.SetAlwaysOnTop(true);
            _uiLayout.ApplyGameWindowIfSaved();

            if (_appSettings.enableClickThrough)
            {
                foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                {
                    var root = doc.rootVisualElement;
                    if (root?.panel == null) continue;
                    _panel ??= root.panel;
                }
            }

            Debug.Log($"[ChillAI] Window initialized: alpha={alpha:F2}, clickThrough={_appSettings.enableClickThrough}");
#endif
        }

#if !UNITY_EDITOR
        void Update()
        {
            if (_panel == null) return;

            var (cx, cy) = _windowService.GetCursorScreenPosition();
            var (wx, wy) = _windowService.GetWindowPosition();
            var screenPosNoFlip = new Vector2(cx - wx, cy - wy);

            var panelPosNoFlip = RuntimePanelUtils.ScreenToPanel(_panel, screenPosNoFlip);
            var pickedNoFlip = _panel.Pick(panelPosNoFlip);

            bool shouldPassThrough = pickedNoFlip == null;
            if (shouldPassThrough != _clickThroughActive)
            {
                _windowService.SetClickThrough(shouldPassThrough);
                _clickThroughActive = shouldPassThrough;
            }
        }
#endif

        void OnApplicationQuit()
        {
            _userSettings?.Save();
            _uiLayout?.SaveNow();
        }
    }
}
