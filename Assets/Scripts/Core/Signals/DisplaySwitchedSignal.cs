namespace ChillAI.Core.Signals
{
    /// <summary>Fired after the game window was moved to another display; carries Unity Screen size before the move.</summary>
    public class DisplaySwitchedSignal
    {
        public int PrevScreenWidth { get; }
        public int PrevScreenHeight { get; }

        public DisplaySwitchedSignal(int prevScreenWidth, int prevScreenHeight)
        {
            PrevScreenWidth = prevScreenWidth;
            PrevScreenHeight = prevScreenHeight;
        }
    }
}
