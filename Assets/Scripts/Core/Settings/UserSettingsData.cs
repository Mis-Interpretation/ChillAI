using System;

namespace ChillAI.Core.Settings
{
    /// <summary>
    /// Runtime user settings persisted to user_settings.json.
    /// All fields are public for JsonUtility serialization.
    /// </summary>
    [Serializable]
    public class UserSettingsData
    {
        // Window
        public int targetFrameRate;

        // Emoji Chat
        public int maxChatBubbles;
        public bool autoGenerateTasks;

        // User Guide
        public bool knowsDragMenu;

        // Task Panel
        public float taskColLeftMinRatio;
        public float taskColLeftMaxRatio;
        public int taskPanelMinWidth;
        public int taskPanelMinHeight;

        // Chat Panel
        public int chatPanelMinWidth;
        public int chatPanelMinHeight;
    }
}
