using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ChillAI.Model.UserProfile
{
    public class ProfileModel : IProfileWriter
    {
        readonly Dictionary<string, ProfileAnswer> _answers = new();
        List<ProfileAnswer> _answersCache;
        bool _cacheDirty = true;
        string _lastRunTime;

        static string SavePath => Path.Combine(Application.persistentDataPath, "user_profile.json");

        public ProfileModel()
        {
            Load();
        }

        public ProfileAnswer GetAnswer(string questionId)
        {
            return _answers.TryGetValue(questionId, out var answer) ? answer : null;
        }

        public IReadOnlyList<ProfileAnswer> AllAnswers
        {
            get
            {
                if (_cacheDirty)
                {
                    _answersCache = new List<ProfileAnswer>(_answers.Values);
                    _cacheDirty = false;
                }
                return _answersCache;
            }
        }

        public bool HasAnswer(string questionId)
        {
            return _answers.ContainsKey(questionId) &&
                   !string.IsNullOrWhiteSpace(_answers[questionId].answer);
        }

        public int AnsweredCount
        {
            get
            {
                var count = 0;
                foreach (var a in _answers.Values)
                {
                    if (!string.IsNullOrWhiteSpace(a.answer))
                        count++;
                }
                return count;
            }
        }

        public IReadOnlyList<ProfileSectionSnapshot> GetSectionSnapshots()
        {
            var snapshots = new List<ProfileSectionSnapshot>();
            foreach (var section in ProfileQuestions.Sections)
            {
                var entries = new List<ProfileQuestionAnswer>();
                foreach (var q in section.questions)
                {
                    _answers.TryGetValue(q.id, out var answer);
                    entries.Add(new ProfileQuestionAnswer(q, answer));
                }
                snapshots.Add(new ProfileSectionSnapshot(section, entries));
            }
            return snapshots;
        }

        public string GetProfileSummary()
        {
            var sb = new StringBuilder();
            foreach (var section in ProfileQuestions.Sections)
                AppendSectionSummary(sb, section);
            return sb.ToString();
        }

        public string GetTierSummary(ProfileTier tier)
        {
            var sb = new StringBuilder();
            foreach (var section in ProfileQuestions.Sections)
            {
                if (section.tier == tier)
                {
                    AppendSectionSummary(sb, section);
                    break;
                }
            }
            return sb.ToString();
        }

        void AppendSectionSummary(StringBuilder sb, ProfileSection section)
        {
            sb.AppendLine($"[{section.title}]");
            foreach (var q in section.questions)
            {
                if (_answers.TryGetValue(q.id, out var a) && !string.IsNullOrWhiteSpace(a.answer))
                {
                    var summary = a.answer.Length > 60 ? a.answer.Substring(0, 60) + "..." : a.answer;
                    var conf = a.confidence < 0.5f ? " [low confidence]" : "";
                    sb.AppendLine($"  {q.id} ({q.label}): {summary}{conf}");
                }
                else
                {
                    sb.AppendLine($"  {q.id} ({q.label}): (unanswered)");
                }
            }
        }

        public string LastRunTime => _lastRunTime;
        public bool HasEverRun => !string.IsNullOrEmpty(_lastRunTime);

        public void RecordRunTime()
        {
            _lastRunTime = DateTime.Now.ToString("o");
        }

        public void SetAnswer(string questionId, string answer, float confidence)
        {
            if (ProfileQuestions.Get(questionId) == null)
            {
                Debug.LogWarning($"[ChillAI] [profile] Unknown question ID: {questionId}");
                return;
            }

            var now = DateTime.Now.ToString("o");
            if (_answers.TryGetValue(questionId, out var existing))
            {
                existing.answer = answer;
                existing.confidence = confidence;
                existing.updatedAt = now;
            }
            else
            {
                _answers[questionId] = new ProfileAnswer(questionId, answer, confidence, now);
            }
            _cacheDirty = true;
        }

        public void Save()
        {
            try
            {
                var data = new ProfileSaveData();
                data.lastRunTime = _lastRunTime ?? "";
                foreach (var a in _answers.Values)
                    data.answers.Add(new ProfileAnswerData
                    {
                        questionId = a.questionId,
                        answer = a.answer,
                        confidence = a.confidence,
                        updatedAt = a.updatedAt
                    });
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to save user profile: {e.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                var json = File.ReadAllText(SavePath);
                var data = JsonUtility.FromJson<ProfileSaveData>(json);
                if (data?.answers == null) return;

                _lastRunTime = string.IsNullOrEmpty(data.lastRunTime) ? null : data.lastRunTime;
                _answers.Clear();
                foreach (var a in data.answers)
                {
                    if (string.IsNullOrEmpty(a.questionId)) continue;
                    // Only load answers for known questions
                    if (ProfileQuestions.Get(a.questionId) == null) continue;
                    _answers[a.questionId] = new ProfileAnswer(
                        a.questionId, a.answer, a.confidence, a.updatedAt);
                }
                _cacheDirty = true;
                Debug.Log($"[ChillAI] Loaded user profile with {_answers.Count} answers.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChillAI] Failed to load user profile: {e.Message}");
            }
        }

        // ── Serializable DTOs ──

        [Serializable]
        class ProfileSaveData
        {
            public string lastRunTime = "";
            public List<ProfileAnswerData> answers = new();
        }

        [Serializable]
        class ProfileAnswerData
        {
            public string questionId;
            public string answer;
            public float confidence;
            public string updatedAt;
        }
    }
}
