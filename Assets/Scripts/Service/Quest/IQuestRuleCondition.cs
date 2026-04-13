using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public interface IQuestRuleCondition
    {
        QuestRuleConditionType Type { get; }
        bool Evaluate(QuestRuleCondition condition, QuestEvaluationContext context);
    }
}
