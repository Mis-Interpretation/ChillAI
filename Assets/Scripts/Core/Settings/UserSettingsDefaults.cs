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

        [Header("Profile Agent")]
        [Range(1, 100)]
        public int profileChatThreshold = 10;

        [Range(1, 50)]
        public int profileTaskThreshold = 3;

        [Range(1, 1440)]
        public int profileTimeThresholdMinutes = 60;

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
                profileChatThreshold = profileChatThreshold,
                profileTaskThreshold = profileTaskThreshold,
                profileTimeThresholdMinutes = profileTimeThresholdMinutes,
            };
        }
    }
}
