using System.Collections.Generic;

namespace ChillAI.Model.ProcessMonitor
{
    public class ProcessMonitorModel : IProcessMonitorWriter
    {
        const int MaxHistoryCount = 50;

        readonly List<ProcessInfo> _recentProcesses = new();

        public ProcessInfo CurrentProcess { get; private set; }
        public IReadOnlyList<ProcessInfo> RecentProcesses => _recentProcesses;

        public void UpdateCurrentProcess(ProcessInfo info)
        {
            CurrentProcess = info;
            _recentProcesses.Add(info);

            if (_recentProcesses.Count > MaxHistoryCount)
                _recentProcesses.RemoveAt(0);
        }

        public void ClearHistory()
        {
            _recentProcesses.Clear();
            CurrentProcess = null;
        }
    }
}
