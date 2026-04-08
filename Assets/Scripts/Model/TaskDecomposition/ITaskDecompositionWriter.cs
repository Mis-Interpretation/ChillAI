using System.Collections.Generic;

namespace ChillAI.Model.TaskDecomposition
{
    public interface ITaskDecompositionWriter : ITaskDecompositionReader
    {
        void SetSubTasks(string originalTask, List<SubTask> subTasks);
        void SetProcessing(bool isProcessing);
        void SetError(string errorMessage);
        void Clear();
    }
}
