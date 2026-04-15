using ChillAI.View.EmojiChat;
using UnityEngine;

namespace ChillAI.View.ChatHistory
{
    public static class ChatHistoryPanelBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToChatDocuments()
        {
            var chatViews = Object.FindObjectsByType<EmojiChatPanelView>(FindObjectsSortMode.None);
            for (int i = 0; i < chatViews.Length; i++)
            {
                var host = chatViews[i];
                if (host == null || host.GetComponent<ChatHistoryPanelView>() != null)
                    continue;

                host.gameObject.AddComponent<ChatHistoryPanelView>();
            }
        }
    }
}
