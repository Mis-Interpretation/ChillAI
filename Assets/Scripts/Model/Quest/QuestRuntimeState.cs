using System;
using System.Collections.Generic;

namespace ChillAI.Model.Quest
{
    [Serializable]
    public class QuestStepRuntimeState
    {
        public string stepId;
        public bool isCompleted;
        public long lastCheckedUnixMs;
    }

    [Serializable]
    public class QuestProgressRuntimeState
    {
        public string questId;
        public bool isUnlocked;
        public bool isCompleted;
        public List<QuestStepRuntimeState> steps = new();
    }

    [Serializable]
    public class QuestRuntimeState
    {
        public int version = 1;
        public string activeQuestId;
        public List<QuestProgressRuntimeState> quests = new();
    }
}
