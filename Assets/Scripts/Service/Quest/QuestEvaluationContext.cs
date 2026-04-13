using ChillAI.Core.Settings;
using ChillAI.Model.UserProfile;

namespace ChillAI.Service.Quest
{
    public class QuestEvaluationContext
    {
        public QuestCheckTiming CheckTiming { get; set; }
        public string LatestUserMessage { get; set; }
        public int TaskBigEventCount { get; set; }
        public IProfileReader ProfileReader { get; set; }
    }
}
