using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    [CreateAssetMenu(fileName = "ExpressionMappingData", menuName = "ChillAI/Expression Mapping Data")]
    public class ExpressionMappingData : ScriptableObject
    {
        [Tooltip("Mapping from software category to expression type")]
        public List<CategoryExpressionEntry> mappings = new()
        {
            new() { category = SoftwareCategory.Working, expression = ExpressionType.Focused },
            new() { category = SoftwareCategory.Gaming, expression = ExpressionType.Excited },
            new() { category = SoftwareCategory.Browsing, expression = ExpressionType.Neutral },
            new() { category = SoftwareCategory.Creating, expression = ExpressionType.Happy },
            new() { category = SoftwareCategory.Resting, expression = ExpressionType.Sleepy },
            new() { category = SoftwareCategory.Unknown, expression = ExpressionType.Neutral }
        };

        public ExpressionType GetExpression(SoftwareCategory category)
        {
            foreach (var entry in mappings)
            {
                if (entry.category == category)
                    return entry.expression;
            }
            return ExpressionType.Neutral;
        }
    }

    [Serializable]
    public class CategoryExpressionEntry
    {
        public SoftwareCategory category;
        public ExpressionType expression;
    }
}
