using System.Collections.Generic;

namespace ChillAI.Model.TaskDecomposition
{
    public class TaskDecompositionModel : ITaskDecompositionWriter
    {
        readonly List<SubTask> _subTasks = new();

        public string OriginalTask { get; private set; } = "";
        public IReadOnlyList<SubTask> CurrentSubTasks => _subTasks;
        public bool IsProcessing { get; private set; }
        public string ErrorMessage { get; private set; } = "";

        public void SetSubTasks(string originalTask, List<SubTask> subTasks)
        {
            OriginalTask = originalTask;
            _subTasks.Clear();
            _subTasks.AddRange(subTasks);
            ErrorMessage = "";
        }

        public void SetProcessing(bool isProcessing)
        {
            IsProcessing = isProcessing;
        }

        public void SetError(string errorMessage)
        {
            ErrorMessage = errorMessage;
            IsProcessing = false;
        }

        public void Clear()
        {
            OriginalTask = "";
            _subTasks.Clear();
            ErrorMessage = "";
            IsProcessing = false;
        }
    }
}
