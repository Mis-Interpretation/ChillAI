using System;
using System.Collections.Generic;
using System.Text;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
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
    public class ProfileController : IInitializable, IDisposable, ITickable
    {
        readonly IAIService _aiService;
        readonly AgentRegistry _agentRegistry;
        readonly SignalBus _signalBus;
        readonly IProfileWriter _profileWriter;
        readonly IChatHistoryReader _chatHistory;
        readonly ITaskDecompositionReader _taskReader;
        readonly ITaskArchiveStore _taskArchive;
        readonly IUsageTrackingReader _usageTracking;
        readonly UserSettingsService _userSettings;
        readonly AppSettings _appSettings;

        int _chatEventCount;
        int _taskEventCount;
        float _secondsSinceLastRun;
        int _lastChatEntryCount;
        bool _isProcessing;

        public ProfileController(
            IAIService aiService,
            AgentRegistry agentRegistry,
            SignalBus signalBus,
            IProfileWriter profileWriter,
            IChatHistoryReader chatHistory,
            ITaskDecompositionReader taskReader,
            ITaskArchiveStore taskArchive,
            IUsageTrackingReader usageTracking,
            UserSettingsService userSettings,
            AppSettings appSettings)
        {
            _aiService = aiService;
            _agentRegistry = agentRegistry;
            _signalBus = signalBus;
            _profileWriter = profileWriter;
            _chatHistory = chatHistory;
            _taskReader = taskReader;
            _taskArchive = taskArchive;
            _usageTracking = usageTracking;
            _userSettings = userSettings;
            _appSettings = appSettings;
        }

        AgentProfile _triageProfile;
        AgentProfile _updateProfile;

        public bool IsProcessing => _isProcessing;

        AgentProfile TriageProfile
        {
            get
            {
                if (_triageProfile != null) return _triageProfile;
                _triageProfile = ScriptableObject.CreateInstance<AgentProfile>();
                _triageProfile.agentId = AgentRegistry.Ids.ProfileTriage;
                _triageProfile.modelName = "gpt-4o-mini";
                _triageProfile.maxTokens = 512;
                _triageProfile.temperature = 0.2f;
                _triageProfile.maxHistoryToSend = 0;
                _triageProfile.systemPrompt = ProfilePrompts.TriageSystemPrompt;
                _triageProfile.useJsonSchema = true;
                _triageProfile.schemaName = ProfilePrompts.TriageSchemaName;
                _triageProfile.jsonSchema = ProfilePrompts.TriageJsonSchema;
                return _triageProfile;
            }
        }

        AgentProfile UpdateProfile
        {
            get
            {
                if (_updateProfile != null) return _updateProfile;
                _updateProfile = ScriptableObject.CreateInstance<AgentProfile>();
                _updateProfile.agentId = AgentRegistry.Ids.ProfileUpdate;
                _updateProfile.modelName = "gpt-4o";
                _updateProfile.maxTokens = 2048;
                _updateProfile.temperature = 0.5f;
                _updateProfile.maxHistoryToSend = 0;
                _updateProfile.systemPrompt = ProfilePrompts.UpdateSystemPrompt;
                _updateProfile.useJsonSchema = true;
                _updateProfile.schemaName = ProfilePrompts.UpdateSchemaName;
                _updateProfile.jsonSchema = ProfilePrompts.UpdateJsonSchema;
                return _updateProfile;
            }
        }

        public void Initialize()
        {
            _signalBus.Subscribe<EmojiChatResponseSignal>(OnChatMessage);
            _signalBus.Subscribe<BigEventChangedSignal>(OnTaskChanged);
            _signalBus.Subscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompleted);
            Application.quitting += OnQuitting;

            _lastChatEntryCount = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat).Count;

            // Seed _secondsSinceLastRun with real elapsed time since last persisted run
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
                // Never run before — seed with a large value so the time threshold
                // triggers on first Tick cycle (after AI is configured)
                _secondsSinceLastRun = float.MaxValue;
                Debug.Log("[ChillAI] [profile] First ever run, will trigger once AI is ready.");
            }
        }

        public void Dispose()
        {
            _signalBus.TryUnsubscribe<EmojiChatResponseSignal>(OnChatMessage);
            _signalBus.TryUnsubscribe<BigEventChangedSignal>(OnTaskChanged);
            _signalBus.TryUnsubscribe<SubTaskCompletionChangedSignal>(OnSubTaskCompleted);
            Application.quitting -= OnQuitting;
        }

        public void Tick()
        {
            if (_isProcessing || !_aiService.IsConfigured) return;

            // Debug hotkey: Q + E + P triggers immediate profile update
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

        // ── Signal Handlers ──

        void OnChatMessage(EmojiChatResponseSignal _) => _chatEventCount++;
        void OnTaskChanged(BigEventChangedSignal _) => _taskEventCount++;
        void OnSubTaskCompleted(SubTaskCompletionChangedSignal _) => _taskEventCount++;

        // ── Two-Phase Update ──

        async void TryRunProfileUpdate()
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;

                var newDataSummary = BuildNewDataSummary();
                var profileSummary = _profileWriter.GetProfileSummary();

                // Phase 1: Triage
                var triageMessage = BuildTriageMessage(newDataSummary, profileSummary);
                var triageResponse = await _aiService.ChatAsync(TriageProfile, triageMessage);
                var triageResult = TryParse<TriageResponse>(triageResponse);

                if (triageResult?.updates == null || triageResult.updates.Count == 0)
                {
                    Debug.Log("[ChillAI] [profile] Triage: no questions need updating.");
                    ResetCounters();

                    return;
                }

                Debug.Log($"[ChillAI] [profile] Triage: {triageResult.updates.Count} question(s) flagged for update.");

                // Phase 2: Focused Update
                var updateMessage = BuildUpdateMessage(newDataSummary, triageResult.updates);
                var updateResponse = await _aiService.ChatAsync(UpdateProfile, updateMessage);
                var updateResult = TryParse<UpdateResponse>(updateResponse);

                if (updateResult?.answers == null || updateResult.answers.Count == 0)
                {
                    Debug.LogWarning("[ChillAI] [profile] Update phase returned no answers.");
                    ResetCounters();

                    return;
                }

                // Apply updates
                var updatedIds = new List<string>();
                foreach (var a in updateResult.answers)
                {
                    if (string.IsNullOrWhiteSpace(a.id) || string.IsNullOrWhiteSpace(a.answer))
                        continue;
                    _profileWriter.SetAnswer(a.id, a.answer, a.confidence);
                    updatedIds.Add(a.id);
                }

                _profileWriter.Save();
                _signalBus.Fire(new ProfileUpdatedSignal(updatedIds));

                Debug.Log($"[ChillAI] [profile] Updated {updatedIds.Count} answer(s): [{string.Join(", ", updatedIds)}]");
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

        void ResetCounters()
        {
            _chatEventCount = 0;
            _taskEventCount = 0;
            _secondsSinceLastRun = 0f;
            _lastChatEntryCount = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat).Count;
        }

        // ── Message Builders ──

        string BuildNewDataSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[System Time: {DateTime.Now:yyyy-MM-dd HH:mm}]");
            sb.AppendLine();

            // Recent chat messages since last run
            var allChat = _chatHistory.GetHistory(AgentRegistry.Ids.EmojiChat);
            var newEntries = allChat.Count > _lastChatEntryCount
                ? allChat.Count - _lastChatEntryCount
                : 0;

            if (newEntries > 0)
            {
                var startIdx = allChat.Count - newEntries;
                sb.AppendLine($"[Recent chat ({newEntries} new messages):]");
                var capStart = Math.Max(startIdx, allChat.Count - 20);
                for (var i = capStart; i < allChat.Count; i++)
                    sb.AppendLine($"- {allChat[i].Role}: {allChat[i].Content}");
                sb.AppendLine();
            }

            // Current tasks
            var tasks = _taskReader.BigEvents;
            if (tasks.Count > 0)
            {
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

            // Recent completed tasks from archive
            var archive = _taskArchive.Entries;
            if (archive.Count > 0)
            {
                sb.AppendLine("[Recently completed tasks:]");
                var recentCount = Math.Min(archive.Count, 10);
                for (var i = archive.Count - recentCount; i < archive.Count; i++)
                    sb.AppendLine($"- {archive[i].content}");
                sb.AppendLine();
            }

            // App usage data (today)
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var processes = _usageTracking.TrackedProcesses;
            if (processes.Count > 0)
            {
                sb.AppendLine("[App usage today:]");
                foreach (var proc in processes)
                {
                    var seconds = _usageTracking.GetUsageSeconds(proc, today);
                    if (seconds < 60f) continue;
                    var minutes = (int)(seconds / 60f);
                    sb.AppendLine($"- {proc}: {minutes} min");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        string BuildTriageMessage(string newDataSummary, string profileSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine(newDataSummary);
            sb.AppendLine("[Current profile:]");
            sb.AppendLine(profileSummary);
            sb.AppendLine("Based on the new data above, which profile question IDs need updating or answering?");
            sb.AppendLine("Return only the IDs that have clear evidence for change in the new data.");
            return sb.ToString();
        }

        string BuildUpdateMessage(string newDataSummary, List<TriageUpdate> updates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Evidence:]");
            sb.AppendLine(newDataSummary);
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
            return sb.ToString();
        }

        // ── Parsing ──

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        // ── Response Models ──

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
