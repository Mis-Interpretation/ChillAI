using System.Collections.Generic;

namespace ChillAI.Core.Signals
{
    public class EmojiChatResponseSignal
    {
        public string UserMessage { get; }
        public IReadOnlyList<string> Messages { get; }
        public bool IsError { get; }
        public string ErrorMessage { get; }

        public EmojiChatResponseSignal(string userMessage, IReadOnlyList<string> messages)
        {
            UserMessage = userMessage;
            Messages = messages;
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
