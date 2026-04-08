namespace ChillAI.Core.Signals
{
    public class ActiveProcessChangedSignal
    {
        public string ProcessName { get; }
        public string WindowTitle { get; }
        public SoftwareCategory Category { get; }

        public ActiveProcessChangedSignal(string processName, string windowTitle, SoftwareCategory category)
        {
            ProcessName = processName;
            WindowTitle = windowTitle;
            Category = category;
        }
    }
}
