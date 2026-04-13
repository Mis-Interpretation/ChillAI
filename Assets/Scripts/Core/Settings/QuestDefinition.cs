using System;
using System.Collections.Generic;
using ChillAI.Core.Settings.QuestActions;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    public enum QuestConditionLogic
    {
        And,
        Or
    }

    public enum QuestCheckTiming
    {
        PerChat,
        PerTaskComplete,
        Manual
    }

    public enum QuestRuleConditionType
    {
        None,
        ChatContainsAnyKeyword,
        ProfileFieldExists,
        TaskCountReached
    }

    [Serializable]
    public class QuestUnlockCondition
    {
        [Tooltip("Leave empty to unlock immediately. All referenced quests must be completed before this quest unlocks.")]
        public List<QuestDefinition> requiredQuests = new();
    }

    [Serializable]
    public class QuestRuleCondition
    {
        public QuestRuleConditionType conditionType = QuestRuleConditionType.None;

        [Tooltip("Generic string argument. Example: comma-separated keywords or profile question id.")]
        public string stringArg;

        [Tooltip("Generic integer argument. Example: minimum task count threshold.")]
        public int intArg;
    }

    [Serializable]
    public class QuestStepDefinition
    {
        public string stepId;
        public string displayTitle;

        [TextArea(2, 8)]
        public string smartConditionPrompt;

        public QuestRuleCondition ruleCondition = new();
        public QuestConditionLogic conditionLogic = QuestConditionLogic.And;
        public QuestCheckTiming checkTiming = QuestCheckTiming.PerChat;
    }

    [CreateAssetMenu(fileName = "QuestDefinition", menuName = "ChillAI/Quest Definition")]
    public class QuestDefinition : ScriptableObject
    {
        public string questId;
        public string title;
        public QuestUnlockCondition unlockCondition = new();

        [Tooltip("Data-driven actions executed when this quest is completed.")]
        public List<QuestCompleteAction> onCompleteActions = new();

        public List<QuestStepDefinition> steps = new();
    }
}
