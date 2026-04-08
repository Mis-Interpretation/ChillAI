using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChillAI.Model.ChatHistory
{
    public class ChatHistoryModel : IChatHistoryWriter
    {
        static readonly IReadOnlyList<ChatHistoryEntry> Empty = new List<ChatHistoryEntry>();

        readonly Dictionary<string, List<ChatHistoryEntry>> _histories = new();
        readonly HashSet<string> _persistentAgentIds = new();

        static string SavePath => Path.Combine(Application.persistentDataPath, "chat_history.json");

        public ChatHistoryModel()
        {
            Load();
        }

        public void RegisterPersistentAgent(string agentId)
        {
            _persistentAgentIds.Add(agentId);
        }

        public IReadOnlyList<ChatHistoryEntry> GetHistory(string agentId)
        {
            return _histories.TryGetValue(agentId, out var list) ? list : Empty;
        }

        public IReadOnlyList<ChatHistoryEntry> GetRecentHistory(string agentId, int maxCount)
        {
            if (maxCount <= 0)
                return GetHistory(agentId);

            if (!_histories.TryGetValue(agentId, out var list) || list.Count == 0)
                return Empty;

            if (list.Count <= maxCount)
                return list;

            return list.GetRange(list.Count - maxCount, maxCount);
        }

        public void AddEntry(string agentId, string role, string content)
        {
            if (!_histories.TryGetValue(agentId, out var list))
            {
                list = new List<ChatHistoryEntry>();
                _histories[agentId] = list;
            }

            list.Add(new ChatHistoryEntry(role, content));
        }

        public void ClearHistory(string agentId)
        {
            if (_histories.TryGetValue(agentId, out var list))
                list.Clear();
        }

        // ── Persistence ──

        public void Save()
        {
            try
            {
                var data = new ChatHistorySaveData();
                foreach (var agentId in _persistentAgentIds)
                {
                    if (!_histories.TryGetValue(agentId, out var list))
                        continue;

                    var agentData = new AgentHistoryData { agentId = agentId };
                    foreach (var entry in list)
                        agentData.entries.Add(new ChatEntryData
                        {
                            role = entry.Role,
                            content = entry.Content
                        });

                    data.agents.Add(agentData);
                }

                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to save chat history: {e.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;

                var json = File.ReadAllText(SavePath);
                var data = JsonUtility.FromJson<ChatHistorySaveData>(json);
                if (data?.agents == null) return;

                foreach (var agentData in data.agents)
                {
                    var list = new List<ChatHistoryEntry>();
                    if (agentData.entries != null)
                    {
                        foreach (var entry in agentData.entries)
                            list.Add(new ChatHistoryEntry(entry.role, entry.content));
                    }

                    _histories[agentData.agentId] = list;
                }

                Debug.Log($"[ChillAI] Loaded chat history for {data.agents.Count} agent(s).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to load chat history: {e.Message}");
            }
        }

        // ── Serializable DTOs ──

        [Serializable]
        class ChatHistorySaveData
        {
            public List<AgentHistoryData> agents = new();
        }

        [Serializable]
        class AgentHistoryData
        {
            public string agentId;
            public List<ChatEntryData> entries = new();
        }

        [Serializable]
        class ChatEntryData
        {
            public string role;
            public string content;
        }
    }
}
