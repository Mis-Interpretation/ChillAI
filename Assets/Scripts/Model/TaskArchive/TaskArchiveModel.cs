using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChillAI.Model.TaskArchive
{
    public class TaskArchiveModel : ITaskArchiveStore
    {
        readonly List<TaskArchiveRecord> _entries = new();

        static string SavePath => Path.Combine(Application.persistentDataPath, "task_archive.json");

        public IReadOnlyList<TaskArchiveRecord> Entries => _entries;

        public void AppendEntry(string parentBigEventId, string content, string entryKind)
        {
            if (string.IsNullOrEmpty(parentBigEventId) || string.IsNullOrEmpty(content))
                return;
            if (entryKind != TaskArchiveKinds.BigEvent && entryKind != TaskArchiveKinds.SubTask)
                entryKind = TaskArchiveKinds.SubTask;
            _entries.Add(new TaskArchiveRecord(parentBigEventId, content, entryKind));
        }

        public void Save()
        {
            try
            {
                var data = new SaveData();
                foreach (var e in _entries)
                    data.entries.Add(new TaskArchiveRecord(e.parentBigEventId, e.content, e.entryKind));
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChillAI] Failed to save task archive: {ex.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                var json = File.ReadAllText(SavePath);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data?.entries == null) return;
                _entries.Clear();
                foreach (var e in data.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.parentBigEventId) || string.IsNullOrEmpty(e.content))
                        continue;
                    var kind = string.IsNullOrEmpty(e.entryKind) ? TaskArchiveKinds.SubTask : e.entryKind;
                    if (kind != TaskArchiveKinds.BigEvent && kind != TaskArchiveKinds.SubTask)
                        kind = TaskArchiveKinds.SubTask;
                    _entries.Add(new TaskArchiveRecord(e.parentBigEventId, e.content, kind));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChillAI] Failed to load task archive: {ex.Message}");
            }
        }

        [Serializable]
        class SaveData
        {
            public List<TaskArchiveRecord> entries = new();
        }
    }
}
