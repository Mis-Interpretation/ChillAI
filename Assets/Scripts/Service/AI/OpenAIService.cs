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
        const string ErrorNotConfigured = "AI_ERR_NOT_CONFIGURED";
        const string ErrorAuth = "AI_ERR_AUTH";
        const string ErrorRateLimit = "AI_ERR_RATE_LIMIT";
        const string ErrorServer = "AI_ERR_SERVER";
        const string ErrorTimeout = "AI_ERR_TIMEOUT";
        const string ErrorNetwork = "AI_ERR_NETWORK";
        const string ErrorUnknown = "AI_ERR_UNKNOWN";

        readonly IConfigReader _configReader;
        readonly AppSettings _appSettings;

        OpenAIClient _client;

        bool HasEffectiveApiKey => !string.IsNullOrWhiteSpace(_appSettings.openaiApiKey) || _configReader.HasApiKey;

        string EffectiveApiKey => !string.IsNullOrWhiteSpace(_appSettings.openaiApiKey)
            ? _appSettings.openaiApiKey
            : _configReader.OpenAIApiKey;

        public bool IsConfigured => HasEffectiveApiKey;

        public OpenAIService(IConfigReader configReader, AppSettings appSettings)
        {
            _configReader = configReader;
            _appSettings = appSettings;
        }

        OpenAIClient GetClient()
        {
            if (_client == null)
            {
                if (!IsConfigured)
                    throw new InvalidOperationException("OpenAI API key is not configured.");

                _client = new OpenAIClient(new OpenAIAuthentication(EffectiveApiKey));
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
                throw new AIServiceException(ErrorNotConfigured);
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
                Debug.LogWarning("[ChillAI] [ai] request failed: auth");
                throw new AIServiceException(ErrorAuth);
            }
            catch (System.Net.Http.HttpRequestException e) when (e.Message.Contains("429"))
            {
                Debug.LogWarning("[ChillAI] [ai] request failed: rate_limit");
                throw new AIServiceException(ErrorRateLimit);
            }
            catch (System.Net.Http.HttpRequestException e) when (e.Message.Contains("500") || e.Message.Contains("503"))
            {
                Debug.LogWarning("[ChillAI] [ai] request failed: server_unavailable");
                throw new AIServiceException(ErrorServer);
            }
            catch (TaskCanceledException e)
            {
                Debug.LogWarning($"[ChillAI] [ai] request failed: timeout ({e.GetType().Name})");
                throw new AIServiceException(ErrorTimeout);
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                Debug.LogWarning($"[ChillAI] [ai] request failed: network ({ShortenForLog(e.Message)})");
                throw new AIServiceException(ErrorNetwork);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ChillAI] [ai] request failed: unexpected ({e.GetType().Name})");
                throw new AIServiceException(ErrorUnknown, e);
            }
        }

        static string ShortenForLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "no_message";

            const int maxLength = 120;
            var compact = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return compact.Length <= maxLength ? compact : compact.Substring(0, maxLength) + "...";
        }
    }
}
