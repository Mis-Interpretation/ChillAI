using System.Collections.Generic;

namespace ChillAI.Core.Signals
{
    public class TaskDecompositionResultSignal
    {
        public string OriginalTask { get; }
        public IReadOnlyList<SubTaskData> SubTasks { get; }
        public bool IsError { get; }
        public string ErrorMessage { get; }

        public TaskDecompositionResultSignal(string originalTask, IReadOnlyList<SubTaskData> subTasks)
        {
            OriginalTask = originalTask;
            SubTasks = subTasks;
        }

        public TaskDecompositionResultSignal(string originalTask, string errorMessage)
        {
            OriginalTask = originalTask;
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
