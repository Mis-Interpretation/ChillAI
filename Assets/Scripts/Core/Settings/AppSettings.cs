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
        [Tooltip("Window transparency (0 = fully transparent, 1 = opaque)")]
        [Range(0f, 1f)]
        public float windowAlpha = 0.85f;

        [Tooltip("Allow mouse clicks to pass through the window")]
        public bool enableClickThrough;

        [Header("Task Panel")]
        [Tooltip("How long to press (ms) before drag mode starts on task rows")]
        [Range(50, 3000)]
        public int taskPanelDragLongPressMs = 300;

        [Header("OpenAI")]
        [Tooltip("Built-in API key for testing distribution. Takes priority over config.json when not empty.")]
        public string openaiApiKey = "";

        [Header("Usage Tracking")]
        [Tooltip("Auto-save interval in minutes to prevent crash data loss")]
        [Range(1f, 60f)]
        public float usageAutoSaveIntervalMinutes = 10f;
    }
}
