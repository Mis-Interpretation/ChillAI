using UnityEngine;

namespace ChillAI.Core.Settings.QuestActions
{
    [CreateAssetMenu(fileName = "QuestAction_LogComplete", menuName = "ChillAI/Quest Actions/Log Complete")]
    public class LogQuestCompleteAction : QuestCompleteAction
    {
        [TextArea(1, 3)]
        public string logTemplate = "[ChillAI] Quest completed: {title} ({id})";

        public override void Execute(QuestActionContext context)
        {
            var message = string.IsNullOrWhiteSpace(logTemplate)
                ? "[ChillAI] Quest completed: {title} ({id})"
                : logTemplate;
            message = message.Replace("{id}", context?.QuestId ?? "")
                             .Replace("{title}", context?.QuestTitle ?? "");
            Debug.Log(message);
        }
    }
}
