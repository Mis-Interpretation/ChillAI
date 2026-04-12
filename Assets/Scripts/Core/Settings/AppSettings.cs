using UnityEngine;

namespace ChillAI.Core.Settings
{
    [CreateAssetMenu(fileName = "AppSettings", menuName = "ChillAI/App Settings")]
    public class AppSettings : ScriptableObject
    {
        [Header("Process Monitor")]
        [Tooltip("Interval in seconds between process scans")]
        [Range(1f, 600f)]
        public float processScanInterval = 300f;

        [Header("Window")]
        [Tooltip("Target frame rate for the application")]
        [Range(10, 60)]
        public int targetFrameRate = 30;

        [Tooltip("Window transparency (0 = fully transparent, 1 = opaque)")]
        [Range(0f, 1f)]
        public float windowAlpha = 0.85f;

        [Tooltip("Allow mouse clicks to pass through the window")]
        public bool enableClickThrough;

        [Header("Emoji Chat")]
        [Tooltip("Maximum number of chat bubbles visible in the chat panel")]
        [Range(5, 100)]
        public int maxChatBubbles = 20;

        [Tooltip("Allow task agent to auto-create tasks from chat intent")]
        public bool autoGenerateTasks = false;

        [Header("Task Panel")]
        [Tooltip("How long to press (ms) before drag mode starts on task rows")]
        [Range(50, 3000)]
        public int taskPanelDragLongPressMs = 300;

        [Tooltip("Minimum fraction of panel width occupied by the left (big-event) column")]
        [Range(0.10f, 0.45f)]
        public float taskColLeftMinRatio = 0.20f;

        [Tooltip("Maximum fraction of panel width occupied by the left (big-event) column")]
        [Range(0.25f, 0.80f)]
        public float taskColLeftMaxRatio = 0.50f;

        [Header("Panel Minimum Sizes (pixels @ 1920×1080)")]
        [Tooltip("Minimum width of the Chat panel in pixels")]
        public int chatPanelMinWidth = 220;

        [Tooltip("Minimum height of the Chat panel in pixels")]
        public int chatPanelMinHeight = 160;

        [Tooltip("Minimum width of the Task panel in pixels")]
        public int taskPanelMinWidth = 320;

        [Tooltip("Minimum height of the Task panel in pixels")]
        public int taskPanelMinHeight = 220;

        [Header("OpenAI")]
        [Tooltip("Built-in API key for testing distribution. Takes priority over config.json when not empty.")]
        public string openaiApiKey = "";

        [Header("Usage Tracking")]
        [Tooltip("Auto-save interval in minutes to prevent crash data loss")]
        [Range(1f, 60f)]
        public float usageAutoSaveIntervalMinutes = 10f;
    }
}
