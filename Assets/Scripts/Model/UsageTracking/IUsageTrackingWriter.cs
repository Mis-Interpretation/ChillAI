using ChillAI.Core;

namespace ChillAI.Model.UsageTracking
{
    public interface IUsageTrackingWriter : IUsageTrackingReader
    {
        void AddUsage(string processName, SoftwareCategory category, float deltaSeconds);
        bool Save();
        void Load();
        bool IsDirty { get; }
    }
}
