using System.Collections.Generic;

namespace ChillAI.Model.ChatHistory
{
    public interface IChatHistoryReader
    {
        /// <summary>
        /// Returns all stored history entries for the given agent.
        /// </summary>
        IReadOnlyList<ChatHistoryEntry> GetHistory(string agentId);

        /// <summary>
        /// Returns the most recent entries for the given agent.
        /// If maxCount is 0 or negative, returns all entries.
        /// </summary>
        IReadOnlyList<ChatHistoryEntry> GetRecentHistory(string agentId, int maxCount);
    }
}
