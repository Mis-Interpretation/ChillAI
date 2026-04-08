using System;
using System.Collections.Generic;
using System.Linq;

namespace ChillAI.Model.TaskDecomposition
{
    public class BigEvent
    {
        readonly List<SubTask> _subTasks = new();

        public string Id { get; }
        public string Title { get; set; }
        public IReadOnlyList<SubTask> SubTasks => _subTasks;
        public bool IsProcessing { get; set; }
        public string ErrorMessage { get; set; } = "";

        public int CompletedCount => _subTasks.Count(s => s.IsCompleted);
        public int TotalCount => _subTasks.Count;

        public BigEvent(string title)
        {
            Id = Guid.NewGuid().ToString();
            Title = title;
        }

        public void SetSubTasks(List<SubTask> subTasks)
        {
            _subTasks.Clear();
            _subTasks.AddRange(subTasks);
        }

        public void AddSubTask(SubTask subTask)
        {
            _subTasks.Add(subTask);
        }

        public void RemoveSubTask(string subTaskId)
        {
            _subTasks.RemoveAll(s => s.Id == subTaskId);
            for (int i = 0; i < _subTasks.Count; i++)
                _subTasks[i].Order = i + 1;
        }

        public void ClearSubTasks()
        {
            _subTasks.Clear();
        }
    }
}
