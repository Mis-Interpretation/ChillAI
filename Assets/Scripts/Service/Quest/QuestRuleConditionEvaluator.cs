using System.Collections.Generic;
using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public class QuestRuleConditionEvaluator
    {
        readonly Dictionary<QuestRuleConditionType, IQuestRuleCondition> _conditions = new();

        public QuestRuleConditionEvaluator()
        {
            Register(new ChatContainsKeywordCondition());
            Register(new ProfileFieldExistsCondition());
            Register(new TaskCountReachedCondition());
        }

        public bool Evaluate(QuestRuleCondition condition, QuestEvaluationContext context)
        {
            if (condition == null || condition.conditionType == QuestRuleConditionType.None)
                return false;

            if (!_conditions.TryGetValue(condition.conditionType, out var evaluator))
                return false;

            return evaluator.Evaluate(condition, context);
        }

        void Register(IQuestRuleCondition condition)
        {
            _conditions[condition.Type] = condition;
        }
    }
}
