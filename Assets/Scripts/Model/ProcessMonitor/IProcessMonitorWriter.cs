namespace ChillAI.Model.ProcessMonitor
{
    public interface IProcessMonitorWriter : IProcessMonitorReader
    {
        void UpdateCurrentProcess(ProcessInfo info);
        void ClearHistory();
    }
}
