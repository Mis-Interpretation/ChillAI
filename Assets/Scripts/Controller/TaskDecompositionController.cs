using System;
using System.Collections.Generic;
using System.Linq;
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

        public void CreateBigEvent(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;

            var bigEvent = _taskModel.AddBigEvent(title);
            _signalBus.Fire(new BigEventChangedSignal(bigEvent.Id, BigEventChangeType.Added));
            RequestDecomposition(bigEvent.Id);
        }

        public async void RequestDecomposition(string bigEventId)
        {
            var bigEvent = _taskModel.GetBigEvent(bigEventId);
            if (bigEvent == null) return;

            var profile = TaskProfile;
            if (profile == null)
            {
                var err = "Task agent profile not found in AgentRegistry.";
                _taskModel.SetBigEventError(bigEventId, err);
                _signalBus.Fire(new TaskDecompositionResultSignal(bigEventId, bigEvent.Title, err));
                return;
            }

            if (!_aiService.IsConfigured)
            {
                var err = "API Key not configured. Please edit config.json.";
                _taskModel.SetBigEventError(bigEventId, err);
                _signalBus.Fire(new TaskDecompositionResultSignal(bigEventId, bigEvent.Title, err));
                return;
            }

            if (bigEvent.IsProcessing) return;

            try
            {
                _taskModel.SetBigEventProcessing(bigEventId, true);

                var rawJson = await _aiService.ChatAsync(profile, bigEvent.Title);
                var subTaskDatas = ParseSubTasks(rawJson);

                var subTasks = new List<SubTask>();
                foreach (var data in subTaskDatas)
                    subTasks.Add(new SubTask(data.Title, data.Order));

                _taskModel.SetBigEventSubTasks(bigEventId, subTasks);
                _taskModel.SetBigEventProcessing(bigEventId, false);
                _signalBus.Fire(new TaskDecompositionResultSignal(
                    bigEventId, bigEvent.Title, (IReadOnlyList<SubTaskData>)subTaskDatas));

                Debug.Log($"[ChillAI] Big event '{bigEvent.Title}' decomposed into {subTasks.Count} subtasks.");
            }
            catch (AIServiceException e)
            {
                _taskModel.SetBigEventError(bigEventId, e.Message);
                _signalBus.Fire(new TaskDecompositionResultSignal(bigEventId, bigEvent.Title, e.Message));
                Debug.LogWarning($"[ChillAI] {e.Message}");
            }
            catch (Exception e)
            {
                var errorMsg = $"Unexpected error: {e.Message}";
                _taskModel.SetBigEventError(bigEventId, errorMsg);
                _signalBus.Fire(new TaskDecompositionResultSignal(bigEventId, bigEvent.Title, errorMsg));
                Debug.LogError($"[ChillAI] {errorMsg}\n{e.StackTrace}");
            }
        }

        public void ToggleSubTask(string bigEventId, string subTaskId)
        {
            _taskModel.ToggleSubTaskCompletion(bigEventId, subTaskId);
            var bigEvent = _taskModel.GetBigEvent(bigEventId);
            var subTask = bigEvent?.SubTasks.FirstOrDefault(s => s.Id == subTaskId);
            if (subTask != null)
                _signalBus.Fire(new SubTaskCompletionChangedSignal(bigEventId, subTaskId, subTask.IsCompleted));
        }

        public void DeleteSubTask(string bigEventId, string subTaskId)
        {
            _taskModel.RemoveSubTask(bigEventId, subTaskId);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskRemoved));
        }

        public void UpdateBigEventTitle(string bigEventId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            _taskModel.UpdateBigEventTitle(bigEventId, newTitle);
        }

        public void UpdateSubTaskTitle(string bigEventId, string subTaskId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            _taskModel.UpdateSubTaskTitle(bigEventId, subTaskId, newTitle);
        }

        public void DeleteBigEvent(string bigEventId)
        {
            _taskModel.RemoveBigEvent(bigEventId);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.Removed));
        }

        static List<SubTaskData> ParseSubTasks(string json)
        {
            var schemaWrapper = TryParse<StringTaskListWrapper>(json);
            if (schemaWrapper?.tasks is { Count: > 0 })
                return ToSubTaskList(schemaWrapper.tasks);

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
