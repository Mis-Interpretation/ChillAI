using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChillAI.Core.Settings;

namespace ChillAI.Service.AI
{
    public interface IAIService
    {
        bool IsConfigured { get; }

        /// <summary>
        /// General-purpose chat: send a user message using the given agent profile.
        /// Returns the raw response string.
        /// </summary>
        Task<string> ChatAsync(AgentProfile profile, string userMessage, CancellationToken ct = default);

        /// <summary>
        /// Chat with conversation history for multi-turn agents.
        /// Each entry is (role, content) where role is "user" or "assistant".
        /// </summary>
        Task<string> ChatAsync(AgentProfile profile, List<(string role, string content)> history,
            string userMessage, CancellationToken ct = default);
    }
}
