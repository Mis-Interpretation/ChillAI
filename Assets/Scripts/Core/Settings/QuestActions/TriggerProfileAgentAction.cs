using System;
using ChillAI.Core.Signals;
using UnityEngine;

namespace ChillAI.Core.Settings.QuestActions
{
    [Flags]
    public enum ProfileTierSelection
    {
        None = 0,
        Tier1Identity = 1 << 0,
        Tier2Preferences = 1 << 1,
        Tier3CurrentState = 1 << 2,
        All = Tier1Identity | Tier2Preferences | Tier3CurrentState
    }

    [CreateAssetMenu(fileName = "QuestAction_TriggerProfileAgent", menuName = "ChillAI/Quest Actions/Trigger Profile Agent")]
    public class TriggerProfileAgentAction : QuestCompleteAction
    {
        [SerializeField]
        ProfileTierSelection targetTiers = ProfileTierSelection.All;

        public override void Execute(QuestActionContext context)
        {
            var signalBus = context?.SignalBus;
            if (signalBus == null)
            {
                Debug.LogWarning("[ChillAI] [quest-action] SignalBus unavailable.");
                return;
            }

            if (targetTiers == ProfileTierSelection.None)
            {
                Debug.Log("[ChillAI] [quest-action] TriggerProfileAgentAction skipped: no tiers selected.");
                return;
            }

            signalBus.Fire(new TriggerProfileAgentRequestedSignal(targetTiers, context.QuestId));
        }
    }
}
