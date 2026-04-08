using System;
using System.Collections.Generic;
using ChillAI.Core.Settings;
using ChillAI.Core.Signals;
using ChillAI.Service.AI;
using UnityEngine;
using Zenject;

namespace ChillAI.Controller
{
    public class EmojiChatController
    {
        readonly IAIService _aiService;
        readonly AgentRegistry _agentRegistry;
        readonly SignalBus _signalBus;

        readonly List<(string role, string content)> _history = new();

        bool _isProcessing;

        public EmojiChatController(
            IAIService aiService,
            AgentRegistry agentRegistry,
            SignalBus signalBus)
        {
            _aiService = aiService;
            _agentRegistry = agentRegistry;
            _signalBus = signalBus;
        }

        public bool IsAIConfigured => _aiService.IsConfigured;
        public bool IsProcessing => _isProcessing;

        AgentProfile EmojiProfile => _agentRegistry.GetProfile(AgentRegistry.Ids.EmojiChat);

        public async void SendMessage(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || _isProcessing)
                return;

            var profile = EmojiProfile;
            if (profile == null || !_aiService.IsConfigured)
                return;

            try
            {
                _isProcessing = true;

                var response = await _aiService.ChatAsync(profile, _history, userMessage);

                _history.Add(("user", userMessage));
                _history.Add(("assistant", response));
                if (_history.Count > 40)
                    _history.RemoveRange(0, 2);

                var messages = ParseMessages(response);
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, messages));
            }
            catch (AIServiceException e)
            {
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, e.Message));
                Debug.LogWarning($"[ChillAI] [emoji-chat] {e.Message}");
            }
            catch (Exception e)
            {
                _signalBus.Fire(new EmojiChatResponseSignal(userMessage, $"Unexpected: {e.Message}"));
                Debug.LogError($"[ChillAI] [emoji-chat] {e.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public void ClearHistory()
        {
            _history.Clear();
        }

        static List<string> ParseMessages(string rawResponse)
        {
            var wrapper = TryParse<EmojiResponseWrapper>(rawResponse);
            if (wrapper?.messages is { Count: > 0 })
                return wrapper.messages;

            // Fallback: treat raw response as single message
            return new List<string> { rawResponse };
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        [Serializable]
        class EmojiResponseWrapper
        {
            public List<string> messages;
        }
    }
}
