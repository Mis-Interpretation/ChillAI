using System;

namespace ChillAI.Model.TaskArchive
{
    /// <summary>Values for <see cref="TaskArchiveRecord.entryKind"/> in persisted JSON.</summary>
    public static class TaskArchiveKinds
    {
        public const string BigEvent = "BigEvent";
        public const string SubTask = "SubTask";
    }

    /// <summary>
    /// One archived line: which big task it belonged to, the text of that event,
    /// and whether this row is the big-task headline or a subtask line.
    /// </summary>
    [Serializable]
    public class TaskArchiveRecord
    {
        public string parentBigEventId;
        public string content;
        /// <summary><see cref="TaskArchiveKinds.BigEvent"/> or <see cref="TaskArchiveKinds.SubTask"/>.</summary>
        public string entryKind;

        public TaskArchiveRecord()
        {
        }

        public TaskArchiveRecord(string parentBigEventId, string content, string entryKind)
        {
            this.parentBigEventId = parentBigEventId;
            this.content = content;
            this.entryKind = entryKind;
        }

        public bool IsBigEventEntry =>
            string.Equals(entryKind, TaskArchiveKinds.BigEvent, StringComparison.Ordinal);

        public bool IsSubTaskEntry =>
            string.Equals(entryKind, TaskArchiveKinds.SubTask, StringComparison.Ordinal);
    }
}
