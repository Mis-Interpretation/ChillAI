namespace ChillAI.Core.Signals
{
    public enum BigEventChangeType
    {
        Added,
        Removed,
        SubTaskAdded,
        SubTaskRemoved,
        SubTaskReordered,
        BigEventsReordered,
        CategoryChanged
    }

    public class BigEventChangedSignal
    {
        public string BigEventId { get; }
        public BigEventChangeType ChangeType { get; }

        public BigEventChangedSignal(string bigEventId, BigEventChangeType changeType)
        {
            BigEventId = bigEventId;
            ChangeType = changeType;
        }
    }
}
