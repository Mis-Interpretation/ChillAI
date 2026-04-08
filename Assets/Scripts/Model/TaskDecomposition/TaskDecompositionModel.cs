using System.Collections.Generic;
using System.Linq;

namespace ChillAI.Model.TaskDecomposition
{
    public class TaskDecompositionModel : ITaskDecompositionWriter
    {
        readonly List<BigEvent> _bigEvents = new();

        public IReadOnlyList<BigEvent> BigEvents => _bigEvents;

        public BigEvent GetBigEvent(string bigEventId)
        {
            return _bigEvents.FirstOrDefault(e => e.Id == bigEventId);
        }

        public BigEvent AddBigEvent(string title)
        {
            var bigEvent = new BigEvent(title);
            _bigEvents.Add(bigEvent);
            return bigEvent;
        }

        public void RemoveBigEvent(string bigEventId)
        {
            _bigEvents.RemoveAll(e => e.Id == bigEventId);
        }

        public void SetBigEventSubTasks(string bigEventId, List<SubTask> subTasks)
        {
            var bigEvent = GetBigEvent(bigEventId);
            bigEvent?.SetSubTasks(subTasks);
        }

        public void SetBigEventProcessing(string bigEventId, bool isProcessing)
        {
            var bigEvent = GetBigEvent(bigEventId);
            if (bigEvent != null)
                bigEvent.IsProcessing = isProcessing;
        }

        public void SetBigEventError(string bigEventId, string errorMessage)
        {
            var bigEvent = GetBigEvent(bigEventId);
            if (bigEvent == null) return;
            bigEvent.ErrorMessage = errorMessage;
            bigEvent.IsProcessing = false;
        }

        public void ToggleSubTaskCompletion(string bigEventId, string subTaskId)
        {
            var bigEvent = GetBigEvent(bigEventId);
            var subTask = bigEvent?.SubTasks.FirstOrDefault(s => s.Id == subTaskId);
            if (subTask != null)
                subTask.IsCompleted = !subTask.IsCompleted;
        }

        public void RemoveSubTask(string bigEventId, string subTaskId)
        {
            var bigEvent = GetBigEvent(bigEventId);
            bigEvent?.RemoveSubTask(subTaskId);
        }

        public void UpdateBigEventTitle(string bigEventId, string newTitle)
        {
            var bigEvent = GetBigEvent(bigEventId);
            if (bigEvent != null)
                bigEvent.Title = newTitle;
        }

        public void UpdateSubTaskTitle(string bigEventId, string subTaskId, string newTitle)
        {
            var bigEvent = GetBigEvent(bigEventId);
            var subTask = bigEvent?.SubTasks.FirstOrDefault(s => s.Id == subTaskId);
            if (subTask != null)
                subTask.Title = newTitle;
        }

        public void Clear()
        {
            _bigEvents.Clear();
        }
    }
}
