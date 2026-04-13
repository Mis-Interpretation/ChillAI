using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ChillAI.Model.Quest
{
    public class QuestRuntimeModel : IQuestRuntimeStore
    {
        static string SavePath => Path.Combine(Application.persistentDataPath, "quest_runtime.json");

        QuestRuntimeState _state = new();

        public QuestRuntimeState State => _state;
        public IReadOnlyList<QuestProgressRuntimeState> Quests => _state.quests;

        public QuestRuntimeModel()
        {
            Load();
        }

        public QuestProgressRuntimeState GetQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            return _state.quests.FirstOrDefault(q => q.questId == questId);
        }

        public QuestProgressRuntimeState GetOrCreateQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            var quest = GetQuest(questId);
            if (quest != null) return quest;

            quest = new QuestProgressRuntimeState
            {
                questId = questId
            };
            _state.quests.Add(quest);
            return quest;
        }

        public QuestStepRuntimeState GetOrCreateStep(string questId, string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return null;
            var quest = GetOrCreateQuest(questId);
            if (quest == null) return null;

            var step = quest.steps.FirstOrDefault(s => s.stepId == stepId);
            if (step != null) return step;

            step = new QuestStepRuntimeState
            {
                stepId = stepId
            };
            quest.steps.Add(step);
            return step;
        }

        public void Reset()
        {
            _state = new QuestRuntimeState();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(_state, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to save quest runtime: {e.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    _state = new QuestRuntimeState();
                    return;
                }

                var json = File.ReadAllText(SavePath);
                _state = JsonUtility.FromJson<QuestRuntimeState>(json) ?? new QuestRuntimeState();
                if (_state.quests == null)
                    _state.quests = new List<QuestProgressRuntimeState>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to load quest runtime: {e.Message}");
                _state = new QuestRuntimeState();
            }
        }
    }
}
