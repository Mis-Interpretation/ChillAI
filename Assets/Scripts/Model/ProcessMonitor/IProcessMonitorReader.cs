using System.Collections.Generic;

namespace ChillAI.Model.ProcessMonitor
{
    public interface IProcessMonitorReader
    {
        ProcessInfo CurrentProcess { get; }
        IReadOnlyList<ProcessInfo> RecentProcesses { get; }
    }
}
