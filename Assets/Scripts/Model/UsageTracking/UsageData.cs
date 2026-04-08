using System;
using System.Collections.Generic;

namespace ChillAI.Model.UsageTracking
{
    [Serializable]
    public class DailyUsageEntry
    {
        public string date;
        public float totalSeconds;
    }

    [Serializable]
    public class ProcessUsageRecord
    {
        public string processName;
        public string category;
        public List<DailyUsageEntry> dailyEntries = new();
    }

    [Serializable]
    public class UsageDataRoot
    {
        public List<ProcessUsageRecord> processes = new();
    }
}
