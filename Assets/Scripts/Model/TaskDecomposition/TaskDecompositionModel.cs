using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ChillAI.Model.TaskDecomposition
{
    public class TaskDecompositionModel : ITaskDecompositionWriter
    {
        readonly List<BigEvent> _bigEvents = new();

        static string SavePath => Path.Combine(Application.persistentDataPath, "tasks.json");

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

        // ── Persistence ──

        public void Save()
        {
            try
            {
                var data = new SaveData();
                foreach (var be in _bigEvents)
                {
                    var bed = new BigEventData { id = be.Id, title = be.Title };
                    foreach (var st in be.SubTasks)
                        bed.subTasks.Add(new SubTaskPersist
                        {
                            id = st.Id,
                            title = st.Title,
                            order = st.Order,
                            isCompleted = st.IsCompleted
                        });
                    data.bigEvents.Add(bed);
                }

                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to save tasks: {e.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;

                var json = File.ReadAllText(SavePath);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data?.bigEvents == null) return;

                _bigEvents.Clear();
                foreach (var bed in data.bigEvents)
                {
                    var be = new BigEvent(bed.id, bed.title);
                    var subTasks = new List<SubTask>();
                    if (bed.subTasks != null)
                    {
                        foreach (var std in bed.subTasks)
                            subTasks.Add(new SubTask(std.id, std.title, std.order, std.isCompleted));
                    }
                    be.SetSubTasks(subTasks);
                    _bigEvents.Add(be);
                }

                Debug.Log($"[ChillAI] Loaded {_bigEvents.Count} task lists.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to load tasks: {e.Message}");
            }
        }

        // ── Serializable DTOs ──

        [Serializable]
        class SaveData
        {
            public List<BigEventData> bigEvents = new();
        }

        [Serializable]
        class BigEventData
        {
            public string id;
            public string title;
            public List<SubTaskPersist> subTasks = new();
        }

        [Serializable]
        class SubTaskPersist
        {
            public string id;
            public string title;
            public int order;
            public bool isCompleted;
        }
    }
}
