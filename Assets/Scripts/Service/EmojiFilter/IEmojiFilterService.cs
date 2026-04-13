using System.Collections.Generic;

namespace ChillAI.Service.EmojiFilter
{
    public interface IEmojiFilterService
    {
        IReadOnlyCollection<string> AllowedEmojis { get; }

        /// <summary>
        /// Filters AI response messages: replaces unauthorized emojis with placeholder.
        /// </summary>
        List<string> FilterMessages(List<string> messages);

        /// <summary>
        /// Builds a prompt constraint string to append to the system prompt.
        /// </summary>
        string BuildPromptConstraint();
    }
}
