using System;
using System.Collections.Generic;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Service.AI;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class TaskDecompositionController
    {
        readonly IAIService _aiService;
        readonly ITaskDecompositionWriter _taskModel;
        readonly AgentRegistry _agentRegistry;
        readonly SignalBus _signalBus;

        public TaskDecompositionController(
            IAIService aiService,
            ITaskDecompositionWriter taskModel,
            AgentRegistry agentRegistry,
            SignalBus signalBus)
        {
            _aiService = aiService;
            _taskModel = taskModel;
            _agentRegistry = agentRegistry;
            _signalBus = signalBus;
        }

        public bool IsAIConfigured => _aiService.IsConfigured;

        AgentProfile TaskProfile => _agentRegistry.GetProfile(AgentRegistry.Ids.TaskDecomposition);

        public async void RequestDecomposition(string taskText)
        {
            if (string.IsNullOrWhiteSpace(taskText))
                return;

            var profile = TaskProfile;
            if (profile == null)
            {
                var err = "Task agent profile not found in AgentRegistry.";
                _taskModel.SetError(err);
                _signalBus.Fire(new TaskDecompositionResultSignal(taskText, err));
                return;
            }

            if (!_aiService.IsConfigured)
            {
                var err = "API Key not configured. Please edit config.json.";
                _taskModel.SetError(err);
                _signalBus.Fire(new TaskDecompositionResultSignal(taskText, err));
                return;
            }

            if (_taskModel.IsProcessing)
                return;

            try
            {
                _taskModel.SetProcessing(true);

                var rawJson = await _aiService.ChatAsync(profile, taskText);
                var subTaskDatas = ParseSubTasks(rawJson);

                var subTasks = new List<SubTask>();
                foreach (var data in subTaskDatas)
                    subTasks.Add(new SubTask(data.Title, data.Order));

                _taskModel.SetSubTasks(taskText, subTasks);
                _taskModel.SetProcessing(false);
                _signalBus.Fire(new TaskDecompositionResultSignal(taskText, (IReadOnlyList<SubTaskData>)subTaskDatas));

                Debug.Log($"[ChillAI] Task decomposed into {subTasks.Count} subtasks.");
            }
            catch (AIServiceException e)
            {
                _taskModel.SetError(e.Message);
                _signalBus.Fire(new TaskDecompositionResultSignal(taskText, e.Message));
                Debug.LogWarning($"[ChillAI] {e.Message}");
            }
            catch (Exception e)
            {
                var errorMsg = $"Unexpected error: {e.Message}";
                _taskModel.SetError(errorMsg);
                _signalBus.Fire(new TaskDecompositionResultSignal(taskText, errorMsg));
                Debug.LogError($"[ChillAI] {errorMsg}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Parses subtasks from AI response. Supports:
        /// 1. JSON Schema mode: {"tasks": ["step1", "step2", ...]}
        /// 2. Raw array mode:   ["step1", "step2", ...]
        /// </summary>
        static List<SubTaskData> ParseSubTasks(string json)
        {
            // Try JSON Schema format: {"tasks": ["...", "..."]}
            var schemaWrapper = TryParse<StringTaskListWrapper>(json);
            if (schemaWrapper?.tasks is { Count: > 0 })
                return ToSubTaskList(schemaWrapper.tasks);

            // Fallback: extract raw JSON array
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');

            if (start < 0 || end < 0 || end <= start)
            {
                Debug.LogWarning($"[ChillAI] Could not parse response: {json}");
                return new List<SubTaskData>();
            }

            var arrayJson = json.Substring(start, end - start + 1);
            var wrapped = $"{{\"items\":{arrayJson}}}";
            var wrapper = TryParse<StringItemsWrapper>(wrapped);

            if (wrapper?.items is { Count: > 0 })
                return ToSubTaskList(wrapper.items);

            Debug.LogWarning("[ChillAI] Failed to parse subtasks.");
            return new List<SubTaskData>();
        }

        static List<SubTaskData> ToSubTaskList(List<string> strings)
        {
            var result = new List<SubTaskData>();
            for (int i = 0; i < strings.Count; i++)
                result.Add(new SubTaskData(strings[i], i + 1));
            return result;
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        [Serializable]
        class StringTaskListWrapper
        {
            public List<string> tasks;
        }

        [Serializable]
        class StringItemsWrapper
        {
            public List<string> items;
        }

    }
}
