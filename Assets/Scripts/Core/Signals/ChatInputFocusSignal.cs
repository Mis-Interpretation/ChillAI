namespace ChillAI.Core.Signals
{
    /// <summary>
    /// Fired when the emoji chat input field gains or loses focus. Used to
    /// drive character "listening" animations (e.g. dog head tilt).
    /// </summary>
    public class ChatInputFocusSignal
    {
        public bool IsFocused { get; }

        public ChatInputFocusSignal(bool isFocused)
        {
            IsFocused = isFocused;
        }
    }
}
