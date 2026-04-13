using System;
using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public class ChatContainsKeywordCondition : IQuestRuleCondition
    {
        public QuestRuleConditionType Type => QuestRuleConditionType.ChatContainsAnyKeyword;

        public bool Evaluate(QuestRuleCondition condition, QuestEvaluationContext context)
        {
            if (condition == null || context == null) return false;
            if (string.IsNullOrWhiteSpace(condition.stringArg)) return false;
            if (string.IsNullOrWhiteSpace(context.LatestUserMessage)) return false;

            var source = context.LatestUserMessage;
            var tokens = condition.stringArg.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var keyword = token.Trim();
                if (keyword.Length == 0) continue;
                if (source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
