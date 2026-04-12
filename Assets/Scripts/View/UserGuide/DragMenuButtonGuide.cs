using System.Collections;
using ChillAI.Core.Settings;
using ChillAI.View.SystemMenu;
using ChillAI.View.UI;
using ChillAI.View.Window;
using UnityEngine;

namespace ChillAI.View.UserGuide
{
    /// <summary>
    /// On app start, spawns a text bubble above the menu button hinting at drag interaction.
    /// The bubble only dismisses when the user actually drags the menu button (not on click).
    /// Skips entirely when <see cref="UserSettingsData.knowsDragMenu"/> is already true.
    /// Self-bootstraps via RuntimeInitializeOnLoadMethod — no scene file changes needed.
    /// </summary>
    public class DragMenuButtonGuide : MonoBehaviour
    {
        const string GuideText = "\U0001F5B1\uFE0F\U0001F90F \u2195\uFE0F\u2194\uFE0F";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject(nameof(DragMenuButtonGuide));
            go.AddComponent<DragMenuButtonGuide>();
            DontDestroyOnLoad(go);
        }

        TextBubble _bubble;
        WindowDragManipulator _manipulator;
        UserSettingsService _userSettings;

        IEnumerator Start()
        {
            SystemMenuView sysMenu = null;
            float timeout = 2f;
            while (timeout > 0f)
            {
                sysMenu = FindFirstObjectByType<SystemMenuView>();
                if (sysMenu != null && sysMenu.MenuButton != null
                    && sysMenu.MenuDragManipulator != null && sysMenu.UserSettings != null)
                    break;
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (sysMenu == null || sysMenu.MenuButton == null)
                yield break;

            _userSettings = sysMenu.UserSettings;

            if (_userSettings.Data.knowsDragMenu)
            {
                Destroy(gameObject);
                yield break;
            }

            _bubble = new TextBubble(GuideText);
            _bubble.AnchorAbove(sysMenu.MenuButton, gap: 12f);

            _manipulator = sysMenu.MenuDragManipulator;
            _manipulator.DragStarted += OnMenuDragStarted;
        }

        void OnMenuDragStarted()
        {
            _manipulator.DragStarted -= OnMenuDragStarted;
            _manipulator = null;

            if (_userSettings != null)
            {
                _userSettings.Data.knowsDragMenu = true;
                _userSettings.Save();
            }

            _bubble?.Dismiss();
            _bubble = null;

            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_manipulator != null)
                _manipulator.DragStarted -= OnMenuDragStarted;
        }
    }
}
