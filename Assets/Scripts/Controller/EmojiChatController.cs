using System;
using System.Collections.Generic;
using System.Text;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.ChatHistory;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Service.AI;
using ChillAI.Service.EmojiFilter;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class EmojiChatController
    {
        readonly IAIService _aiService;
        readonly AgentRegistry _agentRegistry;
        readonly SignalBus _signalBus;
        readonly ITaskDecompositionReader _taskReader;
        readonly TaskDecompositionController _taskController;
        readonly IChatHistoryWriter _chatHistory;
        readonly UserSettingsService _userSettings;
        readonly IEmojiFilterService _emojiFilter;

        AgentProfile _taskRouterProfile;
        AgentProfile _augmentedEmojiProfile;
        bool _isProcessing;

        public EmojiChatController(
            IAIService aiService,
            AgentRegistry agentRegistry,
            SignalBus signalBus,
            ITaskDecompositionReader taskReader,
            TaskDecompositionController taskController,
            IChatHistoryWriter chatHistory,
            UserSettingsService userSettings,
            IEmojiFilterService emojiFilter)
        {
            _aiService = aiService;
            _agentRegistry = agentRegistry;
            _signalBus = signalBus;
            _taskReader = taskReader;
            _taskController = taskController;
            _chatHistory = chatHistory;
            _userSettings = userSettings;
            _emojiFilter = emojiFilter;

            _chatHistory.RegisterPersistentAgent(AgentRegistry.Ids.EmojiChat);
        }

        public bool IsAIConfigured => _aiService.IsConfigured;
        public bool IsProcessing => _isProcessing;

        AgentProfile EmojiProfile => _agentRegistry.GetProfile(AgentRegistry.Ids.EmojiChat);

        AgentProfile AugmentedEmojiProfile
        {
            get
            {
                if (_augmentedEmojiProfile != null) return _augmentedEmojiProfile;

                var constraint = _emojiFilter.BuildPromptConstraint();
                if (string.IsNullOrEmpty(constraint)) return EmojiProfile;

                var baseProfile = EmojiProfile;
                _augmentedEmojiProfile = ScriptableObject.CreateInstance<AgentProfile>();
                _augmentedEmojiProfile.agentId = baseProfile.agentId;
                _augmentedEmojiProfile.displayName = baseProfile.displayName;
                _augmentedEmojiProfile.modelName = baseProfile.modelName;
                _augmentedEmojiProfile.maxTokens = baseProfile.maxTokens;
                _augmentedEmojiProfile.temperature = baseProfile.temperature;
                _augmentedEmojiProfile.systemPrompt = baseProfile.systemPrompt + constraint;
                _augmentedEmojiProfile.maxHistoryToSend = baseProfile.maxHistoryToSend;
                _augmentedEmojiProfile.useJsonSchema = baseProfile.useJsonSchema;
                _augmentedEmojiProfile.schemaName = baseProfile.schemaName;
                _augmentedEmojiProfile.jsonSchema = baseProfile.jsonSchema;
                return _augmentedEmojiProfile;
            }
        }

        AgentProfile TaskRouterProfile
        {
            get
            {
                if (_taskRouterProfile != null) return _taskRouterProfile;

                _taskRouterProfile = ScriptableObject.CreateInstance<AgentProfile>();
                _taskRouterProfile.agentId = "task-router";
                _taskRouterProfile.modelName = "gpt-4o";
                _taskRouterProfile.maxTokens = 512;
                _taskRouterProfile.temperature = 0.3f;
                _taskRouterProfile.maxHistoryToSend = 10;
                _taskRouterProfile.systemPrompt =
                    "你是一个任务管理助手。用户在聊天中提到了想做的事情，你需要根据已有的任务列表来决定该怎么处理。\n\n" +
                    "规则：\n" +
                    "1. 如果用户提到的事情和已有任务的含义相似或重复，返回 action:\"none\"\n" +
                    "2. 如果用户提到的事情是某个已有任务的子任务（属于某个大任务的一部分），返回 action:\"add_subtask\"，" +
                    "并在 target_id 填写对应大任务的 id，title 填写子任务标题\n" +
                    "3. 只有当用户提到的事情和所有已有任务都无关时，才返回 action:\"create\"，" +
                    "title 填写任务标题，subtasks 填写3个子任务（基于\"准备，执行，收尾\"的结构，每个15字以内）\n\n" +
                    "使用中文填写 title 和 subtasks。Return ONLY a JSON object.";
                _taskRouterProfile.useJsonSchema = true;
                _taskRouterProfile.schemaName = "task_decision";
                _taskRouterProfile.jsonSchema =
                    "{\"type\":\"object\",\"properties\":{" +
                    "\"action\":{\"type\":\"string\",\"enum\":[\"none\",\"create\",\"add_subtask\"]}," +
                    "\"target_id\":{\"type\":\"string\"}," +
                    "\"title\":{\"type\":\"string\"}," +
                    "\"subtasks\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}" +
                    "},\"required\":[\"action\",\"target_id\",\"title\",\"subtasks\"],\"additionalProperties\":false}";
                return _taskRouterProfile;
            }
        }

        public async void SendMessage(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || _isProcessing)
                return;

            var profile = EmojiProfile;
            if (profile == null || !_aiService.IsConfigured)
                return;

            try
            {
                _isProcessing = true;

                var augmented = AugmentedEmojiProfile;
                var historyTuples = BuildHistoryTuples(profile);
                var response = await _aiService.ChatAsync(augmented, historyTuples, userMessage);

                // Store original response in history to keep AI context coherent
                _chatHistory.AddEntry(profile.agentId, "user", userMessage);
                _chatHistory.AddEntry(profile.agentId, "assistant", response);
                _chatHistory.Save();

                var parsed = ParseChatResponse(response);
                var filtered = _emojiFilter.FilterMessages(parsed.messages);

                // Fire filtered emoji response
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, filtered));

                // If task intent detected, route to task agent in background
                if (_userSettings.Data.autoGenerateTasks && !string.IsNullOrWhiteSpace(parsed.task_intent))
                    HandleTaskIntent(parsed.task_intent);
            }
            catch (AIServiceException e)
            {
                var friendlyMessage = BuildFriendlyErrorMessage(e.Message);
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, friendlyMessage));
                Debug.LogWarning($"[ChillAI] [emoji-chat] request failed: {e.Message}");
            }
            catch (Exception e)
            {
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, BuildFriendlyErrorMessage("AI_ERR_UNKNOWN")));
                Debug.LogError($"[ChillAI] [emoji-chat] unexpected error: {e.GetType().Name}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        static string BuildFriendlyErrorMessage(string errorCode)
        {
            return errorCode switch
            {
                "AI_ERR_NOT_CONFIGURED" => "🌀❌API🔑",
                "AI_ERR_AUTH" => "🌀🚫API🔑",
                "AI_ERR_RATE_LIMIT" => "🌀⏳⏳",
                "AI_ERR_SERVER" => "🌀❌☁️☁️",
                "AI_ERR_TIMEOUT" => "🌀📶🐢🐢🐢",
                "AI_ERR_NETWORK" => "🌀❌🌐🌐",
                _ => "🌀😿😿😿"
            };
        }

        public void ClearHistory()
        {
            _chatHistory.ClearHistory(EmojiProfile.agentId);
            _chatHistory.Save();
        }

        // ── Task Router (second-phase, async fire-and-forget) ──

        async void HandleTaskIntent(string taskIntent)
        {
            try
            {
                var contextMessage = BuildTaskRouterMessage(taskIntent);
                var routerProfile = TaskRouterProfile;
                var routerHistory = BuildHistoryTuples(routerProfile);
                var response = await _aiService.ChatAsync(routerProfile, routerHistory, contextMessage);

                _chatHistory.AddEntry(routerProfile.agentId, "user", contextMessage);
                _chatHistory.AddEntry(routerProfile.agentId, "assistant", response);

                var decision = TryParse<TaskRouterDecision>(response);

                if (decision == null || decision.action == "none")
                {
                    Debug.Log($"[ChillAI] [task-router] No action for intent: \"{taskIntent}\"");
                    return;
                }

                switch (decision.action)
                {
                    case "create":
                        if (!string.IsNullOrWhiteSpace(decision.title))
                        {
                            var id = _taskController.CreateBigEvent(decision.title);
                            if (id != null && decision.subtasks is { Count: > 0 })
                            {
                                foreach (var sub in decision.subtasks)
                                    _taskController.AddSubTask(id, sub);
                            }
                            _signalBus.Fire(new TaskAddedViaChatSignal(id));
                            Debug.Log($"[ChillAI] [task-router] Created: \"{decision.title}\" with {decision.subtasks?.Count ?? 0} subtasks");
                        }
                        break;

                    case "add_subtask":
                        if (!string.IsNullOrWhiteSpace(decision.target_id) &&
                            !string.IsNullOrWhiteSpace(decision.title))
                        {
                            var parent = _taskReader.GetBigEvent(decision.target_id);
                            if (parent != null)
                            {
                                _taskController.AddSubTask(decision.target_id, decision.title);
                                _signalBus.Fire(new TaskAddedViaChatSignal(decision.target_id));
                                Debug.Log($"[ChillAI] [task-router] Added subtask \"{decision.title}\" to \"{parent.Title}\"");
                            }
                        }
                        break;
                }

                // Show writing emoji in chat to confirm task action
                _signalBus.Fire(new EmojiChatResponseSignal("", new List<string> { "\u270D\uFE0F\u270D\uFE0F\u270D\uFE0F" }));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] [task-router] Error: {e.Message}");
            }
        }

        string BuildTaskRouterMessage(string taskIntent)
        {
            var events = _taskReader.BigEvents;
            var sb = new StringBuilder();

            if (events.Count > 0)
            {
                sb.AppendLine("[Current tasks with subtasks:]");
                foreach (var e in events)
                {
                    sb.AppendLine($"- id:{e.Id} title:\"{e.Title}\"");
                    if (e.SubTasks.Count > 0)
                    {
                        var subtaskTitles = new List<string>();
                        foreach (var s in e.SubTasks)
                            subtaskTitles.Add($"\"{s.Title}\"");
                        sb.AppendLine($"  subtasks: {string.Join(", ", subtaskTitles)}");
                    }
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("[No existing tasks.]");
                sb.AppendLine();
            }

            sb.Append($"User wants to do: {taskIntent}");
            return sb.ToString();
        }

        // ── History Helper ──

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

        // ── Parsing ──

        static ChatResponse ParseChatResponse(string rawResponse)
        {
            var parsed = TryParse<ChatResponse>(rawResponse);
            if (parsed?.messages is { Count: > 0 })
                return parsed;

            return new ChatResponse
            {
                messages = new List<string> { rawResponse },
                task_intent = ""
            };
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        // ── Response Models ──

        [Serializable]
        class ChatResponse
        {
            public List<string> messages;
            public string task_intent;
        }

        [Serializable]
        class TaskRouterDecision
        {
            public string action;
            public string target_id;
            public string title;
            public List<string> subtasks;
        }
    }
}
