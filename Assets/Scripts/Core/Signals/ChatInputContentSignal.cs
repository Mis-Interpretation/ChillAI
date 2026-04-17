namespace ChillAI.Core.Signals
{
    /// <summary>
    /// Fired when the emoji chat input transitions between empty and non-empty.
    /// Used alongside <see cref="ChatInputFocusSignal"/> to decide when the dog
    /// enters the "listening" tilt pose (focused AND the user has typed at
    /// least one character).
    /// </summary>
    public class ChatInputContentSignal
    {
        public bool HasContent { get; }

        public ChatInputContentSignal(bool hasContent)
        {
            HasContent = hasContent;
        }
    }
}
