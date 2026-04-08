using System.Collections.Generic;

namespace ChillAI.Model.TaskDecomposition
{
    public interface ITaskDecompositionReader
    {
        string OriginalTask { get; }
        IReadOnlyList<SubTask> CurrentSubTasks { get; }
        bool IsProcessing { get; }
        string ErrorMessage { get; }
    }
}
