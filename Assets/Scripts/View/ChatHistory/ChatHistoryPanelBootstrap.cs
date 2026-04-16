using ChillAI.View.EmojiChat;
using UnityEngine;
using Zenject;

namespace ChillAI.View.ChatHistory
{
    public static class ChatHistoryPanelBootstrap
    {
        public static int DefaultPageSize = 20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachToChatDocuments()
        {
            var chatViews = Object.FindObjectsByType<EmojiChatPanelView>(FindObjectsSortMode.None);
            for (int i = 0; i < chatViews.Length; i++)
            {
                var host = chatViews[i];
                if (host == null || host.GetComponent<ChatHistoryPanelView>() != null)
                    continue;

                var container = ResolveContainerForHost(host.gameObject);
                if (container != null)
                {
                    var panelView = container.InstantiateComponent<ChatHistoryPanelView>(host.gameObject);
                    panelView.PageSize = DefaultPageSize;
                    continue;
                }

                Debug.LogWarning($"[{nameof(ChatHistoryPanelBootstrap)}] Could not find Zenject container for '{host.name}', using AddComponent fallback.");
                var fallbackPanelView = host.gameObject.AddComponent<ChatHistoryPanelView>();
                fallbackPanelView.PageSize = DefaultPageSize;
            }
        }

        static DiContainer ResolveContainerForHost(GameObject host)
        {
            var sceneContexts = Object.FindObjectsByType<SceneContext>(FindObjectsSortMode.None);
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var sceneContext = sceneContexts[i];
                if (sceneContext == null || sceneContext.gameObject.scene != host.scene)
                    continue;

                if (sceneContext.HasResolved && sceneContext.Container != null)
                    return sceneContext.Container;
            }

            if (ProjectContext.HasInstance && ProjectContext.Instance.Container != null)
                return ProjectContext.Instance.Container;

            return null;
        }
    }
}
