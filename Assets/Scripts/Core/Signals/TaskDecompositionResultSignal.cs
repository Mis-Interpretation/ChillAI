using System.Collections.Generic;

namespace ChillAI.Core.Signals
{
    public class TaskDecompositionResultSignal
    {
        public string BigEventId { get; }
        public string BigEventTitle { get; }
        public IReadOnlyList<SubTaskData> SubTasks { get; }
        public bool IsError { get; }
        public string ErrorMessage { get; }

        public TaskDecompositionResultSignal(string bigEventId, string bigEventTitle, IReadOnlyList<SubTaskData> subTasks)
        {
            BigEventId = bigEventId;
            BigEventTitle = bigEventTitle;
            SubTasks = subTasks;
        }

        public TaskDecompositionResultSignal(string bigEventId, string bigEventTitle, string errorMessage)
        {
            BigEventId = bigEventId;
            BigEventTitle = bigEventTitle;
            SubTasks = new List<SubTaskData>();
            IsError = true;
            ErrorMessage = errorMessage;
        }
    }

    [System.Serializable]
    public class SubTaskData
    {
        public string title;

        [System.NonSerialized]
        public int Order;

        public string Title => title;

        public SubTaskData() { }

        public SubTaskData(string title, int order)
        {
            this.title = title;
            Order = order;
        }
    }
}
