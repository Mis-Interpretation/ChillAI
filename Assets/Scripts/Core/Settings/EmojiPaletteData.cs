using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ChillAI.Core.Settings
{
    [CreateAssetMenu(fileName = "EmojiPaletteData", menuName = "ChillAI/Emoji Palette Data")]
    public class EmojiPaletteData : ScriptableObject
    {
        [Header("Active Emoji Lists")]
        [Tooltip("Emoji groups currently enabled. Leave empty to allow ALL emojis (no filtering).")]
        public List<EmojiListData> activeLists = new();

        [Header("Filter Settings")]
        [Tooltip("Emoji used to replace unauthorized emojis in AI responses")]
        public string placeholderEmoji = "\U0001F512"; // 🔒

        /// <summary>
        /// Builds a merged set of all allowed emojis (normalized) from active lists.
        /// Returns empty set when any list has zero emojis (wildcard = all unlocked).
        /// </summary>
        public HashSet<string> BuildAllowedSet()
        {
            var set = new HashSet<string>();
            foreach (var list in activeLists)
            {
                if (list == null) continue;

                // An empty list acts as a wildcard: all emojis are unlocked.
                if (list.emojis == null || list.emojis.Count == 0)
                    return new HashSet<string>();

                foreach (var entry in list.emojis)
                {
                    if (string.IsNullOrEmpty(entry.emoji)) continue;
                    set.Add(NormalizeEmoji(entry.emoji.Trim()));
                }
            }
            return set;
        }

        /// <summary>
        /// Returns a flat string of all allowed emojis, space-separated.
        /// Returns empty when any list has zero emojis (wildcard = all unlocked).
        /// </summary>
        public string BuildAllowedEmojiString()
        {
            // Check wildcard first
            foreach (var list in activeLists)
            {
                if (list == null) continue;
                if (list.emojis == null || list.emojis.Count == 0)
                    return "";
            }

            var sb = new System.Text.StringBuilder();
            foreach (var list in activeLists)
            {
                if (list == null) continue;
                foreach (var entry in list.emojis)
                {
                    if (string.IsNullOrEmpty(entry.emoji)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(entry.emoji.Trim());
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Strips variation selectors for lookup normalization.
        /// </summary>
        public static string NormalizeEmoji(string emoji)
        {
            if (string.IsNullOrEmpty(emoji)) return emoji;

            var sb = new System.Text.StringBuilder();
            var enumerator = StringInfo.GetTextElementEnumerator(emoji);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                foreach (var ch in element)
                {
                    // Skip Variation Selector-15 and Variation Selector-16
                    if (ch == '\uFE0E' || ch == '\uFE0F') continue;
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
