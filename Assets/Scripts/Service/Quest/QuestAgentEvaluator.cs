using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ChillAI.Core.Settings;
using ChillAI.Model.ChatHistory;
using ChillAI.Service.AI;
using UnityEngine;

namespace ChillAI.Service.Quest
{
    public class QuestAgentEvaluator
    {
        readonly IAIService _aiService;
        readonly AgentRegistry _agentRegistry;
        readonly IChatHistoryWriter _chatHistory;

        public QuestAgentEvaluator(IAIService aiService, AgentRegistry agentRegistry, IChatHistoryWriter chatHistory)
        {
            _aiService = aiService;
            _agentRegistry = agentRegistry;
            _chatHistory = chatHistory;
        }

        public async Task<Dictionary<string, bool>> EvaluateAsync(
            IReadOnlyList<QuestStepDefinition> steps,
            QuestEvaluationContext context)
        {
            var result = new Dictionary<string, bool>();
            if (steps == null || steps.Count == 0) return result;
            if (!_aiService.IsConfigured) return result;

            var profile = _agentRegistry.GetProfile(AgentRegistry.Ids.Quest);
            if (profile == null) return result;

            var prompt = BuildPrompt(steps, context);
            var history = BuildHistoryTuples(profile);
            var raw = await _aiService.ChatAsync(profile, history, prompt);
            _chatHistory.AddEntry(profile.agentId, "user", prompt);
            _chatHistory.AddEntry(profile.agentId, "assistant", raw);

            var parsed = TryParse<QuestAgentResponse>(raw);
            if (parsed?.results == null) return result;

            foreach (var item in parsed.results)
            {
                if (string.IsNullOrWhiteSpace(item.stepId)) continue;
                result[item.stepId] = item.isCompleted;
            }

            return result;
        }

        List<(string role, string content)> BuildHistoryTuples(AgentProfile profile)
        {
            var maxCount = profile.maxHistoryToSend;
            var entries = maxCount > 0
                ? _chatHistory.GetRecentHistory(profile.agentId, maxCount)
                : _chatHistory.GetHistory(profile.agentId);

            var result = new List<(string role, string content)>(entries.Count);
            foreach (var e in entries)
                result.Add((e.Role, e.Content));
            return result;
        }

        static string BuildPrompt(IReadOnlyList<QuestStepDefinition> steps, QuestEvaluationContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Evaluate quest step completion from the latest context.");
            sb.AppendLine("Return only JSON with schema:");
            sb.AppendLine("{\"results\":[{\"stepId\":\"string\",\"isCompleted\":true,\"reason\":\"string\"}]}");
            sb.AppendLine();
            sb.AppendLine("[Timing]");
            sb.AppendLine(context?.CheckTiming.ToString() ?? "Unknown");
            sb.AppendLine();
            sb.AppendLine("[LatestUserMessage]");
            sb.AppendLine(string.IsNullOrWhiteSpace(context?.LatestUserMessage) ? "(none)" : context.LatestUserMessage);
            sb.AppendLine();
            sb.AppendLine("[Steps]");
            foreach (var step in steps)
                sb.AppendLine($"- stepId:{step.stepId} prompt:{step.smartConditionPrompt}");
            return sb.ToString();
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        class QuestAgentResult
        {
            public string stepId;
            public bool isCompleted;
            public string reason;
        }

        [Serializable]
        class QuestAgentResponse
        {
            public List<QuestAgentResult> results;
        }
    }
}
