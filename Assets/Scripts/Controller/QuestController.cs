using System;
using System.Collections.Generic;
using System.Linq;
using ChillAI.Core.Settings;
using ChillAI.Core.Settings.QuestActions;
using ChillAI.Core.Signals;
using ChillAI.Model.Quest;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Model.UserProfile;
using ChillAI.Service.Quest;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class QuestStepViewData
    {
        public string StepId { get; }
        public string Title { get; }
        public bool IsCompleted { get; }

        public QuestStepViewData(string stepId, string title, bool isCompleted)
        {
            StepId = stepId;
            Title = title;
            IsCompleted = isCompleted;
        }
    }

    public class QuestViewData
    {
        public string QuestId { get; }
        public string Title { get; }
        public bool IsUnlocked { get; }
        public bool IsCompleted { get; }
        public IReadOnlyList<QuestStepViewData> Steps { get; }

        public QuestViewData(string questId, string title, bool isUnlocked, bool isCompleted, IReadOnlyList<QuestStepViewData> steps)
        {
            QuestId = questId;
            Title = title;
            IsUnlocked = isUnlocked;
            IsCompleted = isCompleted;
            Steps = steps;
        }
    }

    public class QuestController
    {
        readonly QuestDatabase _questDatabase;
        readonly IQuestRuntimeStore _runtime;
        readonly QuestConditionEvaluator _conditionEvaluator;
        readonly QuestActionExecutor _actionExecutor;
        readonly ITaskDecompositionReader _taskReader;
        readonly IProfileReader _profileReader;
        readonly SignalBus _signalBus;
        readonly List<QuestDefinition> _fallbackQuests;

        [Inject]
        public QuestController(
            QuestDatabase questDatabase,
            IQuestRuntimeStore runtime,
            QuestConditionEvaluator conditionEvaluator,
            QuestActionExecutor actionExecutor,
            ITaskDecompositionReader taskReader,
            IProfileReader profileReader,
            SignalBus signalBus)
        {
            _questDatabase = questDatabase;
            _runtime = runtime;
            _conditionEvaluator = conditionEvaluator;
            _actionExecutor = actionExecutor;
            _taskReader = taskReader;
            _profileReader = profileReader;
            _signalBus = signalBus;
            _fallbackQuests = BuildFallbackQuests();

            BootstrapRuntime();
            _signalBus.Subscribe<QuestCheckRequestedSignal>(OnQuestCheckRequested);
        }

        public IReadOnlyList<QuestViewData> GetQuestViews()
        {
            var views = new List<QuestViewData>();
            foreach (var def in GetQuestDefinitions())
            {
                var progress = _runtime.GetOrCreateQuest(def.questId);
                var stepViews = new List<QuestStepViewData>();
                foreach (var step in def.steps)
                {
                    var runtimeStep = _runtime.GetOrCreateStep(def.questId, step.stepId);
                    stepViews.Add(new QuestStepViewData(step.stepId, step.displayTitle, runtimeStep.isCompleted));
                }

                views.Add(new QuestViewData(
                    def.questId,
                    def.title,
                    progress.isUnlocked,
                    progress.isCompleted,
                    stepViews));
            }

            return views;
        }

        public void ResetProgress()
        {
            _runtime.Reset();
            BootstrapRuntime();
            _runtime.Save();
            _signalBus.Fire<QuestProgressChangedSignal>();
        }

        async void OnQuestCheckRequested(QuestCheckRequestedSignal signal)
        {
            await EvaluateAsync(signal.Timing, signal.LatestUserMessage);
        }

        async System.Threading.Tasks.Task EvaluateAsync(QuestCheckTiming timing, string latestUserMessage)
        {
            bool changed = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var defs = GetQuestDefinitions();
            changed |= RefreshUnlockStates(defs);
            var context = new QuestEvaluationContext
            {
                CheckTiming = timing,
                LatestUserMessage = latestUserMessage,
                TaskBigEventCount = _taskReader.BigEvents.Count,
                ProfileReader = _profileReader
            };

            foreach (var def in defs)
            {
                var progress = _runtime.GetOrCreateQuest(def.questId);
                if (!progress.isUnlocked || progress.isCompleted)
                    continue;

                var stepResult = await _conditionEvaluator.EvaluateAsync(def, context);
                foreach (var pair in stepResult)
                {
                    var step = _runtime.GetOrCreateStep(def.questId, pair.Key);
                    step.lastCheckedUnixMs = now;
                    if (pair.Value && !step.isCompleted)
                    {
                        step.isCompleted = true;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                RefreshUnlockStates(defs);
                SyncActiveQuest();
                _runtime.Save();
                _signalBus.Fire<QuestProgressChangedSignal>();
            }
        }

        public bool TryCompleteQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            var def = GetQuestDefinitions().FirstOrDefault(q => q.questId == questId);
            if (def == null) return false;

            var progress = _runtime.GetOrCreateQuest(questId);
            if (!progress.isUnlocked || progress.isCompleted || !IsQuestCompleted(def))
                return false;

            progress.isCompleted = true;

            if (def.onCompleteActions != null)
            {
                foreach (var action in def.onCompleteActions)
                    _actionExecutor.Execute(action, def.questId, def.title);
            }

            RefreshUnlockStates(GetQuestDefinitions());
            SyncActiveQuest();
            _runtime.Save();
            _signalBus.Fire<QuestProgressChangedSignal>();
            return true;
        }

        void BootstrapRuntime()
        {
            foreach (var def in GetQuestDefinitions())
            {
                var progress = _runtime.GetOrCreateQuest(def.questId);
                progress.isUnlocked = IsUnlocked(def);
                foreach (var step in def.steps)
                    _runtime.GetOrCreateStep(def.questId, step.stepId);
            }

            SyncActiveQuest();
            _runtime.Save();
        }

        void SyncActiveQuest()
        {
            var active = _runtime.Quests.FirstOrDefault(q => q.isUnlocked && !q.isCompleted);
            _runtime.State.activeQuestId = active?.questId;
        }

        bool IsUnlocked(QuestDefinition quest)
        {
            var requiredQuests = quest.unlockCondition?.requiredQuests;
            if (requiredQuests == null || requiredQuests.Count == 0)
                return true;

            foreach (var requiredQuest in requiredQuests)
            {
                var requiredQuestId = requiredQuest?.questId;
                if (string.IsNullOrWhiteSpace(requiredQuestId))
                    continue;
                if (_runtime.GetQuest(requiredQuestId)?.isCompleted != true)
                    return false;
            }

            return true;
        }

        bool RefreshUnlockStates(IReadOnlyList<QuestDefinition> defs)
        {
            bool changed = false;
            foreach (var def in defs)
            {
                var progress = _runtime.GetOrCreateQuest(def.questId);
                var unlockNow = IsUnlocked(def);
                if (progress.isUnlocked != unlockNow)
                {
                    progress.isUnlocked = unlockNow;
                    changed = true;
                }
            }

            return changed;
        }

        bool IsQuestCompleted(QuestDefinition quest)
        {
            if (quest.steps == null || quest.steps.Count == 0) return false;
            foreach (var step in quest.steps)
            {
                var runtimeStep = _runtime.GetOrCreateStep(quest.questId, step.stepId);
                if (!runtimeStep.isCompleted)
                    return false;
            }

            return true;
        }

        IReadOnlyList<QuestDefinition> GetQuestDefinitions()
        {
            if (_questDatabase != null && _questDatabase.quests != null)
            {
                var valid = _questDatabase.quests
                    .Where(q => q != null && !string.IsNullOrWhiteSpace(q.questId))
                    .ToList();
                if (valid.Count > 0)
                    return valid;
            }

            return _fallbackQuests;
        }

        static List<QuestDefinition> BuildFallbackQuests()
        {
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.questId = "quest_first_meeting";
            quest.title = "初次见面";
            quest.onCompleteActions = new List<QuestCompleteAction>
            {
                ScriptableObject.CreateInstance<LogQuestCompleteAction>()
            };
            quest.steps = new List<QuestStepDefinition>
            {
                new QuestStepDefinition
                {
                    stepId = "step_name",
                    displayTitle = "你的名字是？",
                    smartConditionPrompt = "The user clearly states their name.",
                    checkTiming = QuestCheckTiming.PerChat,
                    conditionLogic = QuestConditionLogic.Or
                },
                new QuestStepDefinition
                {
                    stepId = "step_identity",
                    displayTitle = "你的身份是？",
                    smartConditionPrompt = "The user clearly states their identity, role, or occupation.",
                    checkTiming = QuestCheckTiming.PerChat,
                    conditionLogic = QuestConditionLogic.Or
                },
                new QuestStepDefinition
                {
                    stepId = "step_recent_plan",
                    displayTitle = "最近想要做什么事情？",
                    smartConditionPrompt = "The user clearly states a recent plan, goal, or next action.",
                    checkTiming = QuestCheckTiming.PerChat,
                    conditionLogic = QuestConditionLogic.Or
                }
            };

            return new List<QuestDefinition> { quest };
        }
    }
}
