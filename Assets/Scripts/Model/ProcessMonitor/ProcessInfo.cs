using System;
using ChillAI.Core;

namespace ChillAI.Model.ProcessMonitor
{
    public class ProcessInfo
    {
        public string ProcessName { get; }
        public string WindowTitle { get; }
        public SoftwareCategory Category { get; }
        public DateTime DetectedAt { get; }

        public ProcessInfo(string processName, string windowTitle, SoftwareCategory category)
        {
            ProcessName = processName;
            WindowTitle = windowTitle;
            Category = category;
            DetectedAt = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{ProcessName} ({Category}) - {WindowTitle}";
        }
    }
}
