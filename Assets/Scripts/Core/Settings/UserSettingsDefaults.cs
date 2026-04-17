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

        [Header("Audio")]
        [Range(0f, 1f)]
        public float globalVolume = 1f;

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

        [Header("Profile Panel")]
        public int profilePanelMinWidth = 320;
        public int profilePanelMinHeight = 220;

        [Header("Profile Agent")]
        [Range(1, 100)]
        public int profileChatThreshold = 10;

        [Range(1, 50)]
        public int profileTaskThreshold = 3;

        [Range(1, 1440)]
        public int profileTimeThresholdMinutes = 60;

        [Range(5, 100)]
        public int profileMaxChatMessages = 20;

        public UserSettingsData CreateData()
        {
            return new UserSettingsData
            {
                targetFrameRate = targetFrameRate,
                globalVolume = globalVolume,
                maxChatBubbles = maxChatBubbles,
                autoGenerateTasks = autoGenerateTasks,
                knowsDragMenu = knowsDragMenu,
                taskColLeftMinRatio = taskColLeftMinRatio,
                taskColLeftMaxRatio = taskColLeftMaxRatio,
                taskPanelMinWidth = taskPanelMinWidth,
                taskPanelMinHeight = taskPanelMinHeight,
                chatPanelMinWidth = chatPanelMinWidth,
                chatPanelMinHeight = chatPanelMinHeight,
                profilePanelMinWidth = profilePanelMinWidth,
                profilePanelMinHeight = profilePanelMinHeight,
                profileChatThreshold = profileChatThreshold,
                profileTaskThreshold = profileTaskThreshold,
                profileTimeThresholdMinutes = profileTimeThresholdMinutes,
                profileMaxChatMessages = profileMaxChatMessages,
            };
        }
    }
}
