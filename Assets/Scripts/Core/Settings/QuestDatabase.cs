using System.Collections.Generic;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    [CreateAssetMenu(fileName = "QuestDatabase", menuName = "ChillAI/Quest Database")]
    public class QuestDatabase : ScriptableObject
    {
        [Tooltip("Ordered quest definitions. Unlock rules can still gate progression.")]
        public List<QuestDefinition> quests = new();
    }
}
