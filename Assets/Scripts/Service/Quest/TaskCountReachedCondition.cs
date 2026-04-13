using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public class TaskCountReachedCondition : IQuestRuleCondition
    {
        public QuestRuleConditionType Type => QuestRuleConditionType.TaskCountReached;

        public bool Evaluate(QuestRuleCondition condition, QuestEvaluationContext context)
        {
            if (condition == null || context == null) return false;
            if (condition.intArg <= 0) return false;
            return context.TaskBigEventCount >= condition.intArg;
        }
    }
}
