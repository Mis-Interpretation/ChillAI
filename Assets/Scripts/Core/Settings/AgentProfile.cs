using UnityEngine;

namespace ChillAI.Core.Settings
{
    /// <summary>
    /// Each AI agent gets its own profile with independent model, prompt, and parameters.
    /// Create one asset per agent via: Create -> ChillAI -> Agent Profile
    /// </summary>
    [CreateAssetMenu(fileName = "AgentProfile", menuName = "ChillAI/Agent Profile")]
    public class AgentProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique identifier for this agent")]
        public string agentId = "unnamed";

        [Tooltip("Display name shown in UI")]
        public string displayName = "Agent";

        [Header("Model")]
        [Tooltip("OpenAI model name")]
        public string modelName = "gpt-4o";

        [Tooltip("Maximum tokens for the response")]
        [Range(50, 16384)]
        public int maxTokens = 1024;

        [Tooltip("Temperature for response randomness (0 = deterministic, 2 = creative)")]
        [Range(0f, 2f)]
        public float temperature = 0.7f;

        [Header("Prompt")]
        [Tooltip("System prompt that defines this agent's behavior")]
        [TextArea(5, 20)]
        public string systemPrompt = "You are a helpful assistant.";

        [Header("Response Format")]
        [Tooltip("Enable to force the model to return JSON matching the schema below")]
        public bool useJsonSchema;

        [Tooltip("Name for the schema (a-z, 0-9, underscores, dashes, max 64 chars)")]
        public string schemaName = "response";

        [Tooltip("JSON Schema to constrain the response format. Leave empty if useJsonSchema is false.")]
        [TextArea(5, 20)]
        public string jsonSchema = "";
    }
}
