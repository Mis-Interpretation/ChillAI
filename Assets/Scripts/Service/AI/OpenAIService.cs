using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChillAI.Core.Config;
using ChillAI.Core.Settings;
using OpenAI;
using OpenAI.Chat;
using UnityEngine;

namespace ChillAI.Service.AI
{
    public class OpenAIService : IAIService
    {
        readonly IConfigReader _configReader;

        OpenAIClient _client;

        public bool IsConfigured => _configReader.HasApiKey;

        public OpenAIService(IConfigReader configReader)
        {
            _configReader = configReader;
        }

        OpenAIClient GetClient()
        {
            if (_client == null)
            {
                if (!IsConfigured)
                    throw new InvalidOperationException("OpenAI API key is not configured.");

                _client = new OpenAIClient(new OpenAIAuthentication(_configReader.OpenAIApiKey));
            }
            return _client;
        }

        public Task<string> ChatAsync(AgentProfile profile, string userMessage, CancellationToken ct = default)
        {
            return ChatAsync(profile, null, userMessage, ct);
        }

        public async Task<string> ChatAsync(AgentProfile profile, List<(string role, string content)> history,
            string userMessage, CancellationToken ct = default)
        {
            OpenAIClient client;
            try
            {
                client = GetClient();
            }
            catch (InvalidOperationException)
            {
                throw new AIServiceException("API Key not configured. Edit config.json to add your key.");
            }

            // Build message list
            var messages = new List<Message> { new(Role.System, profile.systemPrompt) };

            // Add conversation history if provided
            if (history != null)
            {
                foreach (var (role, content) in history)
                {
                    var r = role == "assistant" ? Role.Assistant : Role.User;
                    messages.Add(new Message(r, content));
                }
            }

            messages.Add(new Message(Role.User, userMessage));

            // Build JSON Schema constraint if configured
            JsonSchema jsonSchema = null;
            if (profile.useJsonSchema && !string.IsNullOrWhiteSpace(profile.jsonSchema))
            {
                jsonSchema = new JsonSchema(profile.schemaName, profile.jsonSchema, strict: true);
            }

            var request = new ChatRequest(
                messages: messages,
                model: profile.modelName,
                temperature: profile.temperature,
                maxTokens: profile.maxTokens,
                jsonSchema: jsonSchema
            );

            Debug.Log($"[ChillAI] [{profile.agentId}] request: model={profile.modelName}, msg=\"{userMessage}\"");

            try
            {
                var response = await client.ChatEndpoint.GetCompletionAsync(request, ct);
                var responseContent = response.FirstChoice.Message.Content.ToString();

                Debug.Log($"[ChillAI] [{profile.agentId}] response ({response.Usage.TotalTokens} tokens): {responseContent}");

                return responseContent;
            }
            catch (System.Net.Http.HttpRequestException e) when (e.Message.Contains("401"))
            {
                _client = null;
                throw new AIServiceException("Invalid API Key. Please check config.json.");
            }
            catch (System.Net.Http.HttpRequestException e) when (e.Message.Contains("429"))
            {
                throw new AIServiceException("Rate limited. Please wait a moment and try again.");
            }
            catch (System.Net.Http.HttpRequestException e) when (e.Message.Contains("500") || e.Message.Contains("503"))
            {
                throw new AIServiceException("OpenAI server error. Please try again later.");
            }
            catch (TaskCanceledException)
            {
                throw new AIServiceException("Request timed out. Check your network connection.");
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                throw new AIServiceException($"Network error: {e.Message}");
            }
        }
    }
}
