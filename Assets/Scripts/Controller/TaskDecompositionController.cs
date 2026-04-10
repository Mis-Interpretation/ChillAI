using System;
using System.Collections.Generic;
using System.Linq;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Model.ChatHistory;
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
        readonly IChatHistoryWriter _chatHistory;

        public TaskDecompositionController(
            IAIService aiService,
            ITaskDecompositionWriter taskModel,
            AgentRegistry agentRegistry,
            SignalBus signalBus,
            IChatHistoryWriter chatHistory)
        {
            _aiService = aiService;
            _taskModel = taskModel;
            _agentRegistry = agentRegistry;
            _signalBus = signalBus;
            _chatHistory = chatHistory;

            _taskModel.Load();
        }

        public bool IsAIConfigured => _aiService.IsConfigured;

        AgentProfile TaskProfile => _agentRegistry.GetProfile(AgentRegistry.Ids.TaskDecomposition);

        public string CreateBigEvent(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            var bigEvent = _taskModel.AddBigEvent(title);
            _signalBus.Fire(new BigEventChangedSignal(bigEvent.Id, BigEventChangeType.Added));
            _taskModel.Save();
            return bigEvent.Id;
        }

        public void AddSubTask(string bigEventId, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            var bigEvent = _taskModel.GetBigEvent(bigEventId);
            if (bigEvent == null) return;
            var subTask = new SubTask(title, bigEvent.TotalCount + 1);
            bigEvent.AddSubTask(subTask);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskAdded));
            _taskModel.Save();
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

                var recentHistory = BuildHistoryTuples(profile);
                var rawJson = await _aiService.ChatAsync(profile, recentHistory, bigEvent.Title);
                _chatHistory.AddEntry(profile.agentId, "user", bigEvent.Title);
                _chatHistory.AddEntry(profile.agentId, "assistant", rawJson);
                var subTaskDatas = ParseSubTasks(rawJson);

                var subTasks = new List<SubTask>();
                foreach (var data in subTaskDatas)
                    subTasks.Add(new SubTask(data.Title, data.Order));

                _taskModel.SetBigEventSubTasks(bigEventId, subTasks);
                _taskModel.SetBigEventProcessing(bigEventId, false);
                _signalBus.Fire(new TaskDecompositionResultSignal(
                    bigEventId, bigEvent.Title, (IReadOnlyList<SubTaskData>)subTaskDatas));

                Debug.Log($"[ChillAI] Big event '{bigEvent.Title}' decomposed into {subTasks.Count} subtasks.");
                _taskModel.Save();
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
            _taskModel.Save();
        }

        public void DeleteSubTask(string bigEventId, string subTaskId)
        {
            _taskModel.RemoveSubTask(bigEventId, subTaskId);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskRemoved));
            _taskModel.Save();
        }

        public void UpdateBigEventTitle(string bigEventId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            _taskModel.UpdateBigEventTitle(bigEventId, newTitle);
            _taskModel.Save();
        }

        public void UpdateSubTaskTitle(string bigEventId, string subTaskId, string newTitle)
        {
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            _taskModel.UpdateSubTaskTitle(bigEventId, subTaskId, newTitle);
            _taskModel.Save();
        }

        public void DeleteBigEvent(string bigEventId)
        {
            _taskModel.RemoveBigEvent(bigEventId);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.Removed));
            _taskModel.Save();
        }

        public void ReorderSubTask(string bigEventId, string subTaskId, int newIndex)
        {
            _taskModel.MoveSubTaskToIndex(bigEventId, subTaskId, newIndex);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskReordered));
            _taskModel.Save();
        }

        public void ReorderBigEvent(string bigEventId, int newIndex)
        {
            _taskModel.MoveBigEventToIndex(bigEventId, newIndex);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.BigEventsReordered));
            _taskModel.Save();
        }

        public string PromoteSubTaskToBigEvent(string bigEventId, string subTaskId, int insertIndex)
        {
            var subTask = _taskModel.DetachSubTask(bigEventId, subTaskId);
            if (subTask == null) return null;
            var newBigEvent = new BigEvent(subTask.Title);
            int n = _taskModel.BigEvents.Count;
            insertIndex = Mathf.Clamp(insertIndex, 0, n);
            _taskModel.InsertBigEvent(insertIndex, newBigEvent);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.SubTaskRemoved));
            _signalBus.Fire(new BigEventChangedSignal(newBigEvent.Id, BigEventChangeType.Added));
            _taskModel.Save();
            return newBigEvent.Id;
        }

        public void MoveSubTaskToBigEvent(string fromBigEventId, string subTaskId, string targetBigEventId)
        {
            if (fromBigEventId == targetBigEventId) return;
            var subTask = _taskModel.DetachSubTask(fromBigEventId, subTaskId);
            if (subTask == null) return;
            var target = _taskModel.GetBigEvent(targetBigEventId);
            if (target == null) return;
            target.InsertSubTask(target.TotalCount, subTask);
            _signalBus.Fire(new BigEventChangedSignal(fromBigEventId, BigEventChangeType.SubTaskRemoved));
            _signalBus.Fire(new BigEventChangedSignal(targetBigEventId, BigEventChangeType.SubTaskAdded));
            _taskModel.Save();
        }

        public void DemoteBigEventToSubTask(string bigEventId, string targetBigEventId)
        {
            if (bigEventId == targetBigEventId) return;
            var bigEvent = _taskModel.GetBigEvent(bigEventId);
            if (bigEvent == null) return;
            var target = _taskModel.GetBigEvent(targetBigEventId);
            if (target == null) return;

            var title = bigEvent.Title;
            _taskModel.RemoveBigEvent(bigEventId);
            var subTask = new SubTask(title, target.TotalCount + 1);
            target.AddSubTask(subTask);
            _signalBus.Fire(new BigEventChangedSignal(bigEventId, BigEventChangeType.Removed));
            _signalBus.Fire(new BigEventChangedSignal(targetBigEventId, BigEventChangeType.SubTaskAdded));
            _taskModel.Save();
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
