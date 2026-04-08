using System.Collections.Generic;

namespace ChillAI.Model.UsageTracking
{
    public interface IUsageTrackingReader
    {
        float GetUsageSeconds(string processName, string date);
        IReadOnlyList<string> TrackedProcesses { get; }
        IReadOnlyList<DailyUsageEntry> GetDailyEntries(string processName);
        float GetTotalUsageForDate(string date);
    }
}
