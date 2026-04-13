using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public class ProfileFieldExistsCondition : IQuestRuleCondition
    {
        public QuestRuleConditionType Type => QuestRuleConditionType.ProfileFieldExists;

        public bool Evaluate(QuestRuleCondition condition, QuestEvaluationContext context)
        {
            if (condition == null || context?.ProfileReader == null) return false;
            if (string.IsNullOrWhiteSpace(condition.stringArg)) return false;
            return context.ProfileReader.HasAnswer(condition.stringArg.Trim());
        }
    }
}
