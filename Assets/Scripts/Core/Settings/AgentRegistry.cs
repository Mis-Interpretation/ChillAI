using System.Collections.Generic;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    /// <summary>
    /// Central registry of all agent profiles.
    /// Drag agent profile assets into the list in Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "AgentRegistry", menuName = "ChillAI/Agent Registry")]
    public class AgentRegistry : ScriptableObject
    {
        [Tooltip("All available agent profiles")]
        public List<AgentProfile> agents = new();

        public AgentProfile GetProfile(string agentId)
        {
            foreach (var agent in agents)
            {
                if (agent != null && agent.agentId == agentId)
                    return agent;
            }
            Debug.LogWarning($"[ChillAI] Agent profile '{agentId}' not found in registry.");
            return null;
        }

        /// <summary>
        /// Well-known agent IDs. Add new ones here as you create them.
        /// </summary>
        public static class Ids
        {
            public const string TaskDecomposition = "task-decomposition";
            public const string EmojiChat = "emoji-chat";
            public const string ProfileTriageT1 = "profile-triage-t1";
            public const string ProfileTriageT2 = "profile-triage-t2";
            public const string ProfileTriageT3 = "profile-triage-t3";
            public const string ProfileUpdateT1 = "profile-update-t1";
            public const string ProfileUpdateT2 = "profile-update-t2";
            public const string ProfileUpdateT3 = "profile-update-t3";
        }
    }
}
