using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChillAI.Core;
using UnityEngine;

namespace ChillAI.Model.UsageTracking
{
    public class UsageTrackingModel : IUsageTrackingWriter
    {
        static readonly string FilePath = Path.Combine(Application.persistentDataPath, "usage_data.json");

        readonly Dictionary<string, ProcessUsageRecord> _processMap = new();
        List<string> _trackedProcessesCache;
        bool _trackedProcessesDirty = true;

        public bool IsDirty { get; private set; }

        public UsageTrackingModel()
        {
            Load();
        }

        public void Load()
        {
            _processMap.Clear();

            if (!File.Exists(FilePath))
            {
                Debug.Log($"[ChillAI] No usage data file found. Will create on first save.");
                return;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var root = JsonUtility.FromJson<UsageDataRoot>(json);

                if (root?.processes != null)
                {
                    foreach (var record in root.processes)
                    {
                        if (!string.IsNullOrEmpty(record.processName))
                            _processMap[record.processName] = record;
                    }
                }

                Debug.Log($"[ChillAI] Usage data loaded: {_processMap.Count} processes from {FilePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to load usage data: {e.Message}. Backing up and starting fresh.");

                try
                {
                    var backupPath = FilePath + ".bak";
                    File.Copy(FilePath, backupPath, true);
                    Debug.Log($"[ChillAI] Corrupt file backed up to {backupPath}");
                }
                catch (Exception backupEx)
                {
                    Debug.LogWarning($"[ChillAI] Failed to backup corrupt file: {backupEx.Message}");
                }

                _processMap.Clear();
            }

            _trackedProcessesDirty = true;
        }

        public void AddUsage(string processName, SoftwareCategory category, float deltaSeconds)
        {
            if (string.IsNullOrEmpty(processName) || deltaSeconds <= 0f) return;

            var today = DateTime.Now.ToString("yyyy-MM-dd");

            if (!_processMap.TryGetValue(processName, out var record))
            {
                record = new ProcessUsageRecord
                {
                    processName = processName,
                    dailyEntries = new List<DailyUsageEntry>()
                };
                _processMap[processName] = record;
                _trackedProcessesDirty = true;
            }

            record.category = category.ToString();

            var dailyEntry = record.dailyEntries.Find(e => e.date == today);
            if (dailyEntry == null)
            {
                dailyEntry = new DailyUsageEntry { date = today, totalSeconds = 0f };
                record.dailyEntries.Add(dailyEntry);
            }

            dailyEntry.totalSeconds += deltaSeconds;
            IsDirty = true;
        }

        public bool Save()
        {
            if (!IsDirty) return false;

            try
            {
                var root = new UsageDataRoot
                {
                    processes = _processMap.Values.ToList()
                };

                var json = JsonUtility.ToJson(root, true);
                File.WriteAllText(FilePath, json);
                IsDirty = false;

                Debug.Log($"[ChillAI] Usage data saved ({_processMap.Count} processes).");
                return true;
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[ChillAI] Failed to save usage data: {e.Message}");
                return false;
            }
        }

        public float GetUsageSeconds(string processName, string date)
        {
            if (!_processMap.TryGetValue(processName, out var record)) return 0f;
            var entry = record.dailyEntries.Find(e => e.date == date);
            return entry?.totalSeconds ?? 0f;
        }

        public IReadOnlyList<string> TrackedProcesses
        {
            get
            {
                if (_trackedProcessesDirty)
                {
                    _trackedProcessesCache = _processMap.Keys.ToList();
                    _trackedProcessesDirty = false;
                }
                return _trackedProcessesCache;
            }
        }

        public IReadOnlyList<DailyUsageEntry> GetDailyEntries(string processName)
        {
            if (_processMap.TryGetValue(processName, out var record))
                return record.dailyEntries;
            return Array.Empty<DailyUsageEntry>();
        }

        public float GetTotalUsageForDate(string date)
        {
            var total = 0f;
            foreach (var record in _processMap.Values)
            {
                var entry = record.dailyEntries.Find(e => e.date == date);
                if (entry != null)
                    total += entry.totalSeconds;
            }
            return total;
        }
    }
}
