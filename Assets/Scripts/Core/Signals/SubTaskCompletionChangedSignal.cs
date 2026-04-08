namespace ChillAI.Core.Signals
{
    public class SubTaskCompletionChangedSignal
    {
        public string BigEventId { get; }
        public string SubTaskId { get; }
        public bool IsCompleted { get; }

        public SubTaskCompletionChangedSignal(string bigEventId, string subTaskId, bool isCompleted)
        {
            BigEventId = bigEventId;
            SubTaskId = subTaskId;
            IsCompleted = isCompleted;
        }
    }
}
