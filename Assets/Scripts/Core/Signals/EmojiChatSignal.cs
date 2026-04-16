using System.Collections.Generic;

namespace ChillAI.Core.Signals
{
    public class EmojiChatResponseSignal
    {
        public string UserMessage { get; }
        public IReadOnlyList<string> Messages { get; }
        public bool IsError { get; }
        public string ErrorMessage { get; }
        public bool SkipFirstBubbleDelay { get; }

        public EmojiChatResponseSignal(string userMessage, IReadOnlyList<string> messages,
            bool skipFirstBubbleDelay = false)
        {
            UserMessage = userMessage;
            Messages = messages;
            SkipFirstBubbleDelay = skipFirstBubbleDelay;
        }

        public EmojiChatResponseSignal(string userMessage, string errorMessage)
        {
            UserMessage = userMessage;
            Messages = new List<string>();
            IsError = true;
            ErrorMessage = errorMessage;
        }
    }
}
