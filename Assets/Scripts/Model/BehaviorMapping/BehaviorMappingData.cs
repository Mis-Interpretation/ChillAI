using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillAI.Model.BehaviorMapping
{
    [CreateAssetMenu(fileName = "BehaviorMappingData", menuName = "ChillAI/Behavior Mapping Data")]
    public class BehaviorMappingData : ScriptableObject
    {
        [Tooltip("Default category for processes not in the mapping list")]
        public Core.SoftwareCategory defaultCategory = Core.SoftwareCategory.Unknown;

        [Tooltip("Process name to category mappings (also serves as whitelist)")]
        public List<ProcessCategoryEntry> mappings = new()
        {
            // Working
            new() { processName = "devenv", category = Core.SoftwareCategory.Working },
            new() { processName = "Code", category = Core.SoftwareCategory.Working },
            new() { processName = "rider64", category = Core.SoftwareCategory.Working },
            new() { processName = "idea64", category = Core.SoftwareCategory.Working },
            new() { processName = "WINWORD", category = Core.SoftwareCategory.Working },
            new() { processName = "EXCEL", category = Core.SoftwareCategory.Working },
            new() { processName = "POWERPNT", category = Core.SoftwareCategory.Working },
            new() { processName = "Notion", category = Core.SoftwareCategory.Working },
            new() { processName = "Obsidian", category = Core.SoftwareCategory.Working },

            // Gaming
            new() { processName = "steam", category = Core.SoftwareCategory.Gaming },
            new() { processName = "steamwebhelper", category = Core.SoftwareCategory.Gaming },
            new() { processName = "EpicGamesLauncher", category = Core.SoftwareCategory.Gaming },

            // Browsing
            new() { processName = "chrome", category = Core.SoftwareCategory.Browsing },
            new() { processName = "msedge", category = Core.SoftwareCategory.Browsing },
            new() { processName = "firefox", category = Core.SoftwareCategory.Browsing },

            // Creating
            new() { processName = "Photoshop", category = Core.SoftwareCategory.Creating },
            new() { processName = "Illustrator", category = Core.SoftwareCategory.Creating },
            new() { processName = "Unity", category = Core.SoftwareCategory.Creating },
            new() { processName = "blender", category = Core.SoftwareCategory.Creating },

            // Resting
            new() { processName = "Spotify", category = Core.SoftwareCategory.Resting },
            new() { processName = "PotPlayerMini64", category = Core.SoftwareCategory.Resting }
        };

        public bool TryGetCategory(string processName, out Core.SoftwareCategory category)
        {
            foreach (var entry in mappings)
            {
                if (string.Equals(entry.processName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    category = entry.category;
                    return true;
                }
            }
            category = defaultCategory;
            return false;
        }

        public bool IsWhitelisted(string processName)
        {
            foreach (var entry in mappings)
            {
                if (string.Equals(entry.processName, processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    [Serializable]
    public class ProcessCategoryEntry
    {
        public string processName;
        public Core.SoftwareCategory category;
    }
}
