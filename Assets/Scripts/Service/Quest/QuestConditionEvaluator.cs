using System.Collections.Generic;
using System.Threading.Tasks;
using ChillAI.Core.Settings;

namespace ChillAI.Service.Quest
{
    public class QuestConditionEvaluator
    {
        readonly QuestRuleConditionEvaluator _ruleEvaluator;
        readonly QuestAgentEvaluator _agentEvaluator;

        public QuestConditionEvaluator(QuestRuleConditionEvaluator ruleEvaluator, QuestAgentEvaluator agentEvaluator)
        {
            _ruleEvaluator = ruleEvaluator;
            _agentEvaluator = agentEvaluator;
        }

        public async Task<Dictionary<string, bool>> EvaluateAsync(
            QuestDefinition quest,
            QuestEvaluationContext context)
        {
            var result = new Dictionary<string, bool>();
            if (quest?.steps == null || quest.steps.Count == 0) return result;

            var smartSteps = new List<QuestStepDefinition>();
            foreach (var step in quest.steps)
            {
                if (step == null || step.checkTiming != context.CheckTiming)
                    continue;

                if (!string.IsNullOrWhiteSpace(step.smartConditionPrompt))
                    smartSteps.Add(step);
            }

            var smartMap = smartSteps.Count > 0
                ? await _agentEvaluator.EvaluateAsync(smartSteps, context)
                : new Dictionary<string, bool>();

            foreach (var step in quest.steps)
            {
                if (step == null || step.checkTiming != context.CheckTiming || string.IsNullOrWhiteSpace(step.stepId))
                    continue;

                var hasRule = step.ruleCondition != null && step.ruleCondition.conditionType != QuestRuleConditionType.None;
                var hasSmart = !string.IsNullOrWhiteSpace(step.smartConditionPrompt);

                var ruleValue = hasRule && _ruleEvaluator.Evaluate(step.ruleCondition, context);
                var smartValue = hasSmart && smartMap.TryGetValue(step.stepId, out var value) && value;

                bool finalValue;
                if (!hasRule && !hasSmart)
                    finalValue = true;
                else if (hasRule && hasSmart)
                    finalValue = step.conditionLogic == QuestConditionLogic.And
                        ? ruleValue && smartValue
                        : ruleValue || smartValue;
                else
                    finalValue = hasRule ? ruleValue : smartValue;

                result[step.stepId] = finalValue;
            }

            return result;
        }
    }
}
