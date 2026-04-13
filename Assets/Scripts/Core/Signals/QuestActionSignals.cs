using ChillAI.Core.Settings;
using ChillAI.Core.Settings.QuestActions;

namespace ChillAI.Core.Signals
{
    public class UnlockEmojiListRequestedSignal
    {
        public EmojiListData EmojiList { get; }
        public string QuestId { get; }

        public UnlockEmojiListRequestedSignal(EmojiListData emojiList, string questId)
        {
            EmojiList = emojiList;
            QuestId = questId;
        }
    }

    public class TriggerProfileAgentRequestedSignal
    {
        public ProfileTierSelection TierSelection { get; }
        public string QuestId { get; }

        public TriggerProfileAgentRequestedSignal(ProfileTierSelection tierSelection, string questId)
        {
            TierSelection = tierSelection;
            QuestId = questId;
        }
    }
}
