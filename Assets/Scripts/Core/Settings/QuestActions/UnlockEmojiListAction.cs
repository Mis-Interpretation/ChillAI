using ChillAI.Core.Signals;
using UnityEngine;

namespace ChillAI.Core.Settings.QuestActions
{
    [CreateAssetMenu(fileName = "QuestAction_UnlockEmojiList", menuName = "ChillAI/Quest Actions/Unlock Emoji List")]
    public class UnlockEmojiListAction : QuestCompleteAction
    {
        [Tooltip("Emoji list to add into palette active lists when quest completes.")]
        public EmojiListData emojiListToUnlock;

        public override void Execute(QuestActionContext context)
        {
            if (emojiListToUnlock == null)
            {
                Debug.LogWarning("[ChillAI] [quest-action] UnlockEmojiListAction missing emojiListToUnlock.");
                return;
            }

            var signalBus = context?.SignalBus;
            if (signalBus == null)
            {
                Debug.LogWarning("[ChillAI] [quest-action] SignalBus unavailable.");
                return;
            }

            signalBus.Fire(new UnlockEmojiListRequestedSignal(emojiListToUnlock, context.QuestId));
        }
    }
}
