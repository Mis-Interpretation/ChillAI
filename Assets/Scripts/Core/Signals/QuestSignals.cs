using ChillAI.Core.Settings;

namespace ChillAI.Core.Signals
{
    public class QuestCheckRequestedSignal
    {
        public QuestCheckTiming Timing { get; }
        public string LatestUserMessage { get; }

        public QuestCheckRequestedSignal(QuestCheckTiming timing, string latestUserMessage = "")
        {
            Timing = timing;
            LatestUserMessage = latestUserMessage;
        }
    }

    public class QuestProgressChangedSignal
    {
    }
}
