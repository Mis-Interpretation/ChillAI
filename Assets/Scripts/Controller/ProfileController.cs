using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ChillAI.Core;
using ChillAI.Core.Settings;
using ChillAI.Core.Settings.QuestActions;
using ChillAI.Core.Signals;
using ChillAI.Model.BehaviorMapping;
using ChillAI.Model.ChatHistory;
using ChillAI.Model.TaskArchive;
using ChillAI.Model.TaskDecomposition;
using ChillAI.Model.UsageTracking;
using ChillAI.Model.UserProfile;
using ChillAI.Service.AI;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class ProfileController : IInitializable, IDisposable, ITickable, IProfileAgentRunner
    {
        readonly IAIService _aiService;
        readonly SignalBus _signalBus;
        readonly IProfileWriter _profileWriter;
        readonly IChatHistoryReader _chatHistory;
        readonly ITaskDecompositionReader _taskReader;
        readonly ITaskArchiveStore _taskArchive;
        readonly IUsageTrackingReader _usageTracking;
        readonly IBehaviorMappingReader _behaviorMapping;
        readonly UserSettingsService _userSettings;
        readonly AppSettings _appSettings;

        int _chatEventCount;
        int _taskEventCount;
        float _secondsSinceLastRun;
        int _lastChatEntryCount;
        bool _isProcessing;

        // Cached AgentProfiles per tier (lazy-created)
        readonly Dictionary<string, AgentProfile> _agentProfiles = new();

        public ProfileController(
            IAIService aiService,
            SignalBus signalBus,
            IProfileWriter profileWriter,
            IChatHistoryReader chatHistory,
            ITaskDecompositionReader taskReader,
            ITaskArchiveStore taskArchive,
            IUsageTrackingReader usageTracking,
            IBehaviorMappingReader behaviorMapping,
            UserSettingsService userSettings,
            AppSettings appSettings)
        {
            _aiService = aiService;
            _signalBus = signalBus;
            _profileWriter = profileWriter;
            _chatHistory = chatHistory;
            _taskReader = taskReader;
            _taskArchive = taskArchive;
            _usageTracking = usageTracking;
            _behaviorMapping = behaviorMapping;
            _userSettings = userSettings;
            _appSettings = appSettings;
        }

        public bool IsProcessing => _isProcessing;

        // ── Lifecycle ──────────────────────────────────────────────

        public void Initialize()
        {
            _signalBus.Subscribe<EmojiChatResponseSignal>(OnChatMessage);
            _signalBus.Subscribe<BigEventChangedSignal>(OnTaskChanged);
            _signalBus.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompleted);
            _signalBus.Subscribe<TriggerProfileAgentRequestedSignal>(OnTriggerProfileAgentRequested);
            Application.quitting += OnQuitting;

            _lastChatEntryCount = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat).Count;

            if (_profileWriter.HasEverRun)
            {
                if (DateTime.TryParse(_profileWriter.LastRunTime, out var lastRun))
                {
                    _secondsSinceLastRun = (float)(DateTime.Now - lastRun).TotalSeconds;
                    Debug.Log($"[ChillAI] [profile] Last run was {_secondsSinceLastRun / 60f:F0} minutes ago.");
                }
            }
            else
            {
                _secondsSinceLastRun = float.MaxValue;
                Debug.Log("[ChillAI] [profile] First ever run, will trigger once AI is ready.");
            }
        }

        public void Dispose()
        {
            _signalBus.TryUnsubscribe<EmojiChatResponseSignal>(OnChatMessage);
            _signalBus.TryUnsubscribe<BigEventChangedSignal>(OnTaskChanged);
            _signalBus.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompleted);
            _signalBus.TryUnsubscribe<TriggerProfileAgentRequestedSignal>(OnTriggerProfileAgentRequested);
            Application.quitting -= OnQuitting;
        }

        public void Tick()
        {
            if (_isProcessing || !_aiService.IsConfigured) return;

            if (_appSettings.debugMode &&
                Input.GetKey(KeyCode.Q) && Input.GetKey(KeyCode.E) && Input.GetKeyDown(KeyCode.P))
            {
                Debug.Log("[ChillAI] [profile] Debug hotkey triggered. Running profile update...");
                TryRunProfileUpdate();
                return;
            }

            _secondsSinceLastRun += Time.unscaledDeltaTime;

            if (ShouldRun())
                TryRunProfileUpdate();
        }

        bool ShouldRun()
        {
            var data = _userSettings.Data;
            var chatThreshold = data.profileChatThreshold > 0 ? data.profileChatThreshold : 10;
            var taskThreshold = data.profileTaskThreshold > 0 ? data.profileTaskThreshold : 3;
            var timeThreshold = data.profileTimeThresholdMinutes > 0 ? data.profileTimeThresholdMinutes : 60;

            if (_chatEventCount >= chatThreshold) return true;
            if (_taskEventCount >= taskThreshold) return true;
            if (_secondsSinceLastRun >= timeThreshold * 60f) return true;
            return false;
        }

        // ── Signal Handlers ────────────────────────────────────────

        void OnChatMessage(EmojiChatResponseSignal _) => _chatEventCount++;
        void OnTaskChanged(BigEventChangedSignal _) => _taskEventCount++;
        void OnSubTaskCompleted(SubTaskCompletionChangedSignal _) => _taskEventCount++;
        void OnTriggerProfileAgentRequested(TriggerProfileAgentRequestedSignal signal)
        {
            if (signal == null) return;

            var tiers = new List<ProfileTier>(3);
            if ((signal.TierSelection & ProfileTierSelection.Tier1Identity) != 0)
                tiers.Add(ProfileTier.Identity);
            if ((signal.TierSelection & ProfileTierSelection.Tier2Preferences) != 0)
                tiers.Add(ProfileTier.Preferences);
            if ((signal.TierSelection & ProfileTierSelection.Tier3CurrentState) != 0)
                tiers.Add(ProfileTier.CurrentState);

            RunProfileUpdateForTiers(tiers);
        }

        // ── Per-Tier Parallel Pipeline ─────────────────────────────

        public void RunProfileUpdateForTiers(IReadOnlyList<ProfileTier> tiers)
        {
            if (tiers == null || tiers.Count == 0)
            {
                TryRunProfileUpdate();
                return;
            }

            var filter = new HashSet<ProfileTier>(tiers);
            TryRunProfileUpdate(filter);
        }

        async void TryRunProfileUpdate(HashSet<ProfileTier> tierFilter = null)
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;

                // Collect shared data once (chat messages, etc.)
                var chatMessages = CollectNewChatMessages();

                // Phase 1: Parallel triage for all tiers
                var configs = ProfilePrompts.TierConfigs;
                var activeConfigs = new List<ProfileTierConfig>();
                for (var i = 0; i < configs.Length; i++)
                {
                    if (tierFilter == null || tierFilter.Contains(configs[i].Tier))
                        activeConfigs.Add(configs[i]);
                }

                if (activeConfigs.Count == 0)
                {
                    Debug.Log("[ChillAI] [profile] No target tiers selected, skip run.");
                    ResetCounters();
                    return;
                }

                var triageTasks = new Task<List<TriageUpdate>>[activeConfigs.Count];

                for (var i = 0; i < activeConfigs.Count; i++)
                {
                    var config = activeConfigs[i];
                    var dataSummary = BuildTierDataSummary(config, chatMessages);
                    var tierSummary = _profileWriter.GetTierSummary(config.Tier);
                    triageTasks[i] = RunTriage(config, dataSummary, tierSummary);
                }

                await Task.WhenAll(triageTasks);

                // Phase 2: Parallel update for tiers that have flagged questions
                var updateTasks = new List<Task<List<AnswerUpdate>>>();

                for (var i = 0; i < activeConfigs.Count; i++)
                {
                    var updates = triageTasks[i].Result;
                    if (updates == null || updates.Count == 0) continue;

                    var config = activeConfigs[i];
                    var dataSummary = BuildTierDataSummary(config, chatMessages);
                    Debug.Log($"[ChillAI] [profile] {config.Tier}: {updates.Count} question(s) flagged for update.");
                    updateTasks.Add(RunUpdate(config, dataSummary, updates));
                }

                if (updateTasks.Count == 0)
                {
                    Debug.Log("[ChillAI] [profile] Triage: no questions need updating across all tiers.");
                    ResetCounters();
                    return;
                }

                await Task.WhenAll(updateTasks);

                // Apply all updates
                var updatedIds = new List<string>();
                foreach (var task in updateTasks)
                {
                    var answers = task.Result;
                    if (answers == null) continue;
                    foreach (var a in answers)
                    {
                        if (string.IsNullOrWhiteSpace(a.id) || string.IsNullOrWhiteSpace(a.answer))
                            continue;
                        _profileWriter.SetAnswer(a.id, a.answer, a.confidence);
                        updatedIds.Add(a.id);
                    }
                }

                _profileWriter.Save();

                if (updatedIds.Count > 0)
                {
                    _signalBus.Fire(new ProfileUpdatedSignal(updatedIds));
                    Debug.Log($"[ChillAI] [profile] Updated {updatedIds.Count} answer(s): [{string.Join(", ", updatedIds)}]");
                }
            }
            catch (AIServiceException e)
            {
                Debug.LogWarning($"[ChillAI] [profile] AI error: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ChillAI] [profile] Unexpected error: {e.Message}");
            }
            finally
            {
                _isProcessing = false;
                _profileWriter.RecordRunTime();
                _profileWriter.Save();
                ResetCounters();
            }
        }

        async Task<List<TriageUpdate>> RunTriage(ProfileTierConfig config, string dataSummary, string tierSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine(dataSummary);
            sb.AppendLine("[Current profile:]");
            sb.AppendLine(tierSummary);
            sb.AppendLine("Based on the new data above, which profile question IDs need updating or answering?");
            sb.AppendLine("Return only the IDs that have clear evidence for change in the new data.");

            var profile = GetOrCreateAgentProfile(config.AgentIdTriage, "gpt-4o-mini", 512, 0.2f, config.TriageSystemPrompt);
            var response = await _aiService.ChatAsync(profile, sb.ToString());
            var result = TryParse<TriageResponse>(response);
            return result?.updates;
        }

        async Task<List<AnswerUpdate>> RunUpdate(ProfileTierConfig config, string dataSummary, List<TriageUpdate> updates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Evidence:]");
            sb.AppendLine(dataSummary);
            sb.AppendLine("[Questions to update:]");

            foreach (var u in updates)
            {
                var questionDef = ProfileQuestions.Get(u.id);
                if (questionDef == null) continue;

                var current = _profileWriter.GetAnswer(u.id);
                var currentText = current != null && !string.IsNullOrWhiteSpace(current.answer)
                    ? current.answer
                    : "(unanswered)";

                sb.AppendLine($"- {u.id} ({questionDef.label}): \"{questionDef.question}\"");
                sb.AppendLine($"  current answer: \"{currentText}\"");
                sb.AppendLine($"  reason for update: {u.reason}");
            }

            sb.AppendLine();
            sb.AppendLine("Return updated answers with confidence scores (0.0-1.0).");

            var profile = GetOrCreateAgentProfile(config.AgentIdUpdate, "gpt-4o", 2048, 0.5f, config.UpdateSystemPrompt);
            var response = await _aiService.ChatAsync(profile, sb.ToString());
            var result = TryParse<UpdateResponse>(response);
            return result?.answers;
        }

        void ResetCounters()
        {
            _chatEventCount = 0;
            _taskEventCount = 0;
            _secondsSinceLastRun = 0f;
            _lastChatEntryCount = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat).Count;
        }

        // ── Data Collection ────────────────────────────────────────

        /// <summary>
        /// Collect new user chat messages since last run. Called once per update cycle,
        /// shared across all tier data builders.
        /// </summary>
        List<string> CollectNewChatMessages()
        {
            var allChat = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat);
            var newEntries = allChat.Count > _lastChatEntryCount
                ? allChat.Count - _lastChatEntryCount
                : 0;

            var messages = new List<string>();
            if (newEntries <= 0) return messages;

            var startIdx = allChat.Count - newEntries;
            for (var i = startIdx; i < allChat.Count; i++)
            {
                if (allChat[i].Role == "user")
                    messages.Add(allChat[i].Content);
            }

            return messages;
        }

        /// <summary>
        /// Build data summary for a specific tier based on its declared DataSources.
        /// </summary>
        string BuildTierDataSummary(ProfileTierConfig config, List<string> chatMessages)
        {
            var sb = new StringBuilder();
            var sources = config.DataSources;

            if (sources.HasFlag(ProfileDataSource.SystemTime))
                AppendSystemTime(sb);

            if (sources.HasFlag(ProfileDataSource.ChatMessages))
                AppendChatMessages(sb, chatMessages);

            if (sources.HasFlag(ProfileDataSource.CurrentTasks))
                AppendCurrentTasks(sb);

            if (sources.HasFlag(ProfileDataSource.ArchivedTasks))
                AppendArchivedTasks(sb);

            if (sources.HasFlag(ProfileDataSource.AppUsageDetail))
                AppendAppUsageDetail(sb);

            if (sources.HasFlag(ProfileDataSource.AppUsageByCategory))
                AppendAppUsageByCategory(sb);

            if (sources.HasFlag(ProfileDataSource.AppActiveHours))
                AppendAppActiveHours(sb);

            return sb.ToString();
        }

        // ── Append* Methods (data formatting blocks) ───────────────

        static void AppendSystemTime(StringBuilder sb)
        {
            sb.AppendLine($"[System Time: {DateTime.Now:yyyy-MM-dd HH:mm}]");
            sb.AppendLine();
        }

        void AppendChatMessages(StringBuilder sb, List<string> messages)
        {
            if (messages.Count == 0) return;

            var maxMessages = _userSettings.Data.profileMaxChatMessages > 0
                ? _userSettings.Data.profileMaxChatMessages
                : 20;
            var capStart = Math.Max(0, messages.Count - maxMessages);

            sb.AppendLine($"[Recent user messages ({Math.Min(messages.Count, maxMessages)} messages):]");
            for (var i = capStart; i < messages.Count; i++)
                sb.AppendLine($"- {messages[i]}");
            sb.AppendLine();
        }

        void AppendCurrentTasks(StringBuilder sb)
        {
            var tasks = _taskReader.BigEvents;
            if (tasks.Count == 0) return;

            sb.AppendLine("[Current tasks:]");
            foreach (var t in tasks)
            {
                var completedCount = 0;
                foreach (var s in t.SubTasks)
                    if (s.IsCompleted) completedCount++;
                sb.AppendLine($"- \"{t.Title}\" ({completedCount}/{t.SubTasks.Count} completed)");
            }
            sb.AppendLine();
        }

        void AppendArchivedTasks(StringBuilder sb)
        {
            var archive = _taskArchive.Entries;
            if (archive.Count == 0) return;

            sb.AppendLine("[Recently completed tasks:]");
            var recentCount = Math.Min(archive.Count, 10);
            for (var i = archive.Count - recentCount; i < archive.Count; i++)
                sb.AppendLine($"- {archive[i].content}");
            sb.AppendLine();
        }

        void AppendAppUsageDetail(StringBuilder sb)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var processes = _usageTracking.TrackedProcesses;
            if (processes.Count == 0) return;

            var hasAny = false;
            var temp = new StringBuilder();
            foreach (var proc in processes)
            {
                var seconds = _usageTracking.GetUsageSeconds(proc, today);
                if (seconds < 60f) continue;
                var minutes = (int)(seconds / 60f);
                temp.AppendLine($"- {proc}: {minutes} min");
                hasAny = true;
            }

            if (hasAny)
            {
                sb.AppendLine("[App usage today:]");
                sb.Append(temp);
                sb.AppendLine();
            }
        }

        void AppendAppUsageByCategory(StringBuilder sb)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var processes = _usageTracking.TrackedProcesses;
            if (processes.Count == 0) return;

            // Accumulate usage by category
            var categoryTotals = new Dictionary<SoftwareCategory, float>();
            foreach (var proc in processes)
            {
                var seconds = _usageTracking.GetUsageSeconds(proc, today);
                if (seconds < 60f) continue;

                var category = _behaviorMapping.GetCategory(proc);
                if (!categoryTotals.ContainsKey(category))
                    categoryTotals[category] = 0f;
                categoryTotals[category] += seconds;
            }

            if (categoryTotals.Count == 0) return;

            sb.AppendLine("[App usage by category today:]");
            foreach (var kvp in categoryTotals)
            {
                var minutes = (int)(kvp.Value / 60f);
                sb.AppendLine($"- {kvp.Key}: {minutes} min");
            }
            sb.AppendLine();
        }

        void AppendAppActiveHours(StringBuilder sb)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var totalSeconds = _usageTracking.GetTotalUsageForDate(today);
            if (totalSeconds < 60f) return;

            var totalMinutes = (int)(totalSeconds / 60f);
            var now = DateTime.Now;

            sb.AppendLine("[Active hours today:]");
            sb.AppendLine($"- Total app usage today: {totalMinutes} min");
            sb.AppendLine($"- Current time: {now:HH:mm}");

            // Show recent days pattern for rhythm detection
            for (var dayOffset = 1; dayOffset <= 3; dayOffset++)
            {
                var date = now.AddDays(-dayOffset).ToString("yyyy-MM-dd");
                var daySeconds = _usageTracking.GetTotalUsageForDate(date);
                if (daySeconds >= 60f)
                {
                    var dayMinutes = (int)(daySeconds / 60f);
                    sb.AppendLine($"- {date}: {dayMinutes} min total usage");
                }
            }
            sb.AppendLine();
        }

        // ── Agent Profile Factory ──────────────────────────────────

        AgentProfile GetOrCreateAgentProfile(string agentId, string model, int maxTokens,
            float temperature, string systemPrompt)
        {
            if (_agentProfiles.TryGetValue(agentId, out var existing))
                return existing;

            var profile = ScriptableObject.CreateInstance<AgentProfile>();
            profile.agentId = agentId;
            profile.modelName = model;
            profile.maxTokens = maxTokens;
            profile.temperature = temperature;
            profile.maxHistoryToSend = 0;
            profile.systemPrompt = systemPrompt;
            profile.useJsonSchema = true;
            profile.schemaName = agentId.Contains("triage")
                ? ProfilePrompts.TriageSchemaName
                : ProfilePrompts.UpdateSchemaName;
            profile.jsonSchema = agentId.Contains("triage")
                ? ProfilePrompts.TriageJsonSchema
                : ProfilePrompts.UpdateJsonSchema;

            _agentProfiles[agentId] = profile;
            return profile;
        }

        // ── Parsing ────────────────────────────────────────────────

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        // ── Response Models ────────────────────────────────────────

        [Serializable]
        class TriageResponse
        {
            public List<TriageUpdate> updates;
        }

        [Serializable]
        class TriageUpdate
        {
            public string id;
            public string reason;
        }

        [Serializable]
        class UpdateResponse
        {
            public List<AnswerUpdate> answers;
        }

        [Serializable]
        class AnswerUpdate
        {
            public string id;
            public string answer;
            public float confidence;
        }

        void OnQuitting()
        {
            _profileWriter.Save();
        }
    }
}
