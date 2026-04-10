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

        public BigEvent(string id, string title)
        {
            Id = id;
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
            ReindexSubTasks();
        }

        public void InsertSubTask(int index, SubTask subTask)
        {
            index = Math.Max(0, Math.Min(index, _subTasks.Count));
            _subTasks.Insert(index, subTask);
            ReindexSubTasks();
        }

        public void MoveSubTask(string subTaskId, int newIndex)
        {
            var idx = _subTasks.FindIndex(s => s.Id == subTaskId);
            if (idx < 0) return;
            var task = _subTasks[idx];
            _subTasks.RemoveAt(idx);
            newIndex = Math.Max(0, Math.Min(newIndex, _subTasks.Count));
            _subTasks.Insert(newIndex, task);
            ReindexSubTasks();
        }

        public void ClearSubTasks()
        {
            _subTasks.Clear();
        }

        void ReindexSubTasks()
        {
            for (int i = 0; i < _subTasks.Count; i++)
                _subTasks[i].Order = i + 1;
        }
    }
}
