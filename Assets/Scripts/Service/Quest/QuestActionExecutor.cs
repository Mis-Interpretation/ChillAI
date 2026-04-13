using ChillAI.Core.Settings.QuestActions;
using Zenject;

namespace ChillAI.Service.Quest
{
    public class QuestActionExecutor
    {
        readonly SignalBus _signalBus;

        public QuestActionExecutor(SignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        public void Execute(QuestCompleteAction action, string questId, string questTitle)
        {
            if (action == null)
                return;

            var context = new QuestActionContext(questId, questTitle, _signalBus);
            action.Execute(context);
        }
    }
}
