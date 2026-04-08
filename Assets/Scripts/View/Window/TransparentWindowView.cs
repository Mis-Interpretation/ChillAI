using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using ChillAI.Service.Platform;
using UnityEngine;
using Zenject;

namespace ChillAI.View.Window
{
    public class TransparentWindowView : MonoBehaviour
    {
        [Inject] IWindowService _windowService;
        [Inject] AppSettings _appSettings;
        [Inject] IConfigReader _configReader;

        void Start()
        {
            // Frame rate
            Application.targetFrameRate = _appSettings.targetFrameRate;
            Application.runInBackground = true;

#if UNITY_EDITOR
            Debug.Log("[ChillAI] Window transparency skipped in Editor to avoid making Editor invisible.");
#else
            // Window settings - only apply in standalone builds
            float alpha = _configReader.GetEffectiveFloat(
                _configReader.Config.windowAlphaOverride,
                _appSettings.windowAlpha);

            _windowService.MakeTransparent(alpha);
            _windowService.SetAlwaysOnTop(true);

            if (_appSettings.enableClickThrough)
                _windowService.SetClickThrough(true);

            Debug.Log($"[ChillAI] Window initialized: alpha={alpha:F2}, fps={_appSettings.targetFrameRate}");
#endif
        }
    }
}
