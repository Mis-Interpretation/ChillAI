using System.Collections.Generic;

namespace ChillAI.Model.UserProfile
{
    public interface IProfileReader
    {
        ProfileAnswer GetAnswer(string questionId);
        IReadOnlyList<ProfileAnswer> AllAnswers { get; }
        bool HasAnswer(string questionId);
        int AnsweredCount { get; }

        /// <summary>
        /// Returns answers grouped by section, preserving the document's 3-tier structure.
        /// Each entry maps a <see cref="ProfileSection"/> to its list of
        /// (question, answer-or-null) pairs, in document order.
        /// Ideal for UI rendering.
        /// </summary>
        IReadOnlyList<ProfileSectionSnapshot> GetSectionSnapshots();

        /// <summary>
        /// Returns a compact one-liner-per-question summary for the triage prompt.
        /// </summary>
        string GetProfileSummary();

        /// <summary>
        /// When the profile agent last ran. Null if never run.
        /// </summary>
        string LastRunTime { get; }

        /// <summary>
        /// Whether the profile agent has ever run.
        /// </summary>
        bool HasEverRun { get; }
    }

    public class ProfileSectionSnapshot
    {
        public ProfileSection Section { get; }
        public IReadOnlyList<ProfileQuestionAnswer> Entries { get; }

        public ProfileSectionSnapshot(ProfileSection section, IReadOnlyList<ProfileQuestionAnswer> entries)
        {
            Section = section;
            Entries = entries;
        }
    }

    public class ProfileQuestionAnswer
    {
        public ProfileQuestionDef Question { get; }
        public ProfileAnswer Answer { get; }

        public ProfileQuestionAnswer(ProfileQuestionDef question, ProfileAnswer answer)
        {
            Question = question;
            Answer = answer;
        }
    }
}
