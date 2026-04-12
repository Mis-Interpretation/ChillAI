using System.Collections.Generic;

namespace ChillAI.Model.TaskArchive
{
    public interface ITaskArchiveStore
    {
        IReadOnlyList<TaskArchiveRecord> Entries { get; }
        /// <param name="entryKind"><see cref="TaskArchiveKinds.BigEvent"/> or <see cref="TaskArchiveKinds.SubTask"/>.</param>
        void AppendEntry(string parentBigEventId, string content, string entryKind);
        void Save();
        void Load();
    }
}
