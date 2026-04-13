using UnityEngine;
using Zenject;

namespace ChillAI.Core.Settings.QuestActions
{
    public class QuestActionContext
    {
        public string QuestId { get; }
        public string QuestTitle { get; }
        public SignalBus SignalBus { get; }

        public QuestActionContext(
            string questId,
            string questTitle,
            SignalBus signalBus)
        {
            QuestId = questId;
            QuestTitle = questTitle;
            SignalBus = signalBus;
        }
    }

    public abstract class QuestCompleteAction : ScriptableObject
    {
        public abstract void Execute(QuestActionContext context);
    }
}
