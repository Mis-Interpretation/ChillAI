using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    [CreateAssetMenu(fileName = "EmojiListData", menuName = "ChillAI/Emoji List Data")]
    public class EmojiListData : ScriptableObject
    {
        [Tooltip("Unique identifier for this emoji group")]
        public string listId = "unnamed";

        [Tooltip("Display name shown in UI")]
        public string displayName = "Emoji Group";

        public List<EmojiEntry> emojis = new();
    }

    [Serializable]
    public class EmojiEntry
    {
        [Tooltip("Single emoji character")]
        public string emoji;

        [Tooltip("Optional description")]
        public string description;
    }
}
