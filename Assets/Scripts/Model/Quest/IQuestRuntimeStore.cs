using System.Collections.Generic;

namespace ChillAI.Model.Quest
{
    public interface IQuestRuntimeStore
    {
        QuestRuntimeState State { get; }
        IReadOnlyList<QuestProgressRuntimeState> Quests { get; }
        QuestProgressRuntimeState GetQuest(string questId);
        QuestProgressRuntimeState GetOrCreateQuest(string questId);
        QuestStepRuntimeState GetOrCreateStep(string questId, string stepId);
        void Reset();
        void Save();
        void Load();
    }
}
