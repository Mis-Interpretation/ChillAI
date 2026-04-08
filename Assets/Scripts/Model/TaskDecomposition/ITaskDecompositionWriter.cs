using System.Collections.Generic;

namespace ChillAI.Model.TaskDecomposition
{
    public interface ITaskDecompositionWriter : ITaskDecompositionReader
    {
        BigEvent AddBigEvent(string title);
        void RemoveBigEvent(string bigEventId);
        void SetBigEventSubTasks(string bigEventId, List<SubTask> subTasks);
        void SetBigEventProcessing(string bigEventId, bool isProcessing);
        void SetBigEventError(string bigEventId, string errorMessage);
        void ToggleSubTaskCompletion(string bigEventId, string subTaskId);
        void RemoveSubTask(string bigEventId, string subTaskId);
        void UpdateBigEventTitle(string bigEventId, string newTitle);
        void UpdateSubTaskTitle(string bigEventId, string subTaskId, string newTitle);
        void Clear();
        void Save();
        void Load();
    }
}
