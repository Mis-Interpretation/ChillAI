namespace ChillAI.Core.Signals
{
    public class TaskAddedViaChatSignal
    {
        public string BigEventId { get; }

        public TaskAddedViaChatSignal(string bigEventId)
        {
            BigEventId = bigEventId;
        }
    }
}
