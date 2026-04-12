using UnityEngine;

namespace ChillAI.Core.Settings
{
    /// <summary>
    /// ScriptableObject that provides default values for <see cref="UserSettingsData"/>.
    /// On first run (no JSON file), these defaults are copied into the persisted file.
    /// </summary>
    [CreateAssetMenu(fileName = "UserSettingsDefaults", menuName = "ChillAI/User Settings Defaults")]
    public class UserSettingsDefaults : ScriptableObject
    {
        [Header("Window")]
        [Range(10, 60)]
        public int targetFrameRate = 30;

        [Header("Emoji Chat")]
        [Range(5, 100)]
        public int maxChatBubbles = 20;

        public bool autoGenerateTasks;

        [Header("User Guide")]
        public bool knowsDragMenu;

        [Header("Task Panel")]
        [Range(0.10f, 0.45f)]
        public float taskColLeftMinRatio = 0.20f;

        [Range(0.25f, 0.80f)]
        public float taskColLeftMaxRatio = 0.50f;

        public int taskPanelMinWidth = 320;
        public int taskPanelMinHeight = 220;

        [Header("Chat Panel")]
        public int chatPanelMinWidth = 220;
        public int chatPanelMinHeight = 160;

        public UserSettingsData CreateData()
        {
            return new UserSettingsData
            {
                targetFrameRate = targetFrameRate,
                maxChatBubbles = maxChatBubbles,
                autoGenerateTasks = autoGenerateTasks,
                knowsDragMenu = knowsDragMenu,
                taskColLeftMinRatio = taskColLeftMinRatio,
                taskColLeftMaxRatio = taskColLeftMaxRatio,
                taskPanelMinWidth = taskPanelMinWidth,
                taskPanelMinHeight = taskPanelMinHeight,
                chatPanelMinWidth = chatPanelMinWidth,
                chatPanelMinHeight = chatPanelMinHeight,
            };
        }
    }
}
