using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ChillAI.Core.Settings;
using UnityEngine;

namespace ChillAI.Service.EmojiFilter
{
    public class EmojiFilterService : IEmojiFilterService
    {
        readonly EmojiPaletteData _palette;
        readonly HashSet<string> _allowedSet;
        readonly string _promptConstraint;
        readonly string _placeholder;
        readonly List<string> _allowedList; // for random fallback picks

        public IReadOnlyCollection<string> AllowedEmojis => _allowedSet;

        public EmojiFilterService(EmojiPaletteData palette)
        {
            _palette = palette;
            _allowedSet = palette.BuildAllowedSet();
            _placeholder = palette.placeholderEmoji ?? "\U0001F512";
            _allowedList = new List<string>(_allowedSet);

            var emojiString = palette.BuildAllowedEmojiString();
            _promptConstraint = string.IsNullOrEmpty(emojiString)
                ? ""
                : "\n\nIMPORTANT CONSTRAINT: You may ONLY use emojis from this exact list: " +
                  emojiString +
                  "\nUsing ANY emoji not in this list is strictly forbidden. " +
                  "Choose the most expressive combination from the allowed set.";

            if (_allowedSet.Count == 0)
                Debug.Log("[ChillAI] [emoji-filter] all emojis allowed (wildcard list detected or no lists configured)");
            else
                Debug.Log($"[ChillAI] [emoji-filter] initialized with {_allowedSet.Count} allowed emojis");
        }

        public string BuildPromptConstraint() => _promptConstraint;

        public List<string> FilterMessages(List<string> messages)
        {
            if (messages == null) return new List<string>();
            if (_allowedSet.Count == 0) return messages; // no filter configured

            var result = new List<string>(messages.Count);
            foreach (var msg in messages)
            {
                var filtered = FilterSingleMessage(msg);
                if (!string.IsNullOrEmpty(filtered))
                    result.Add(filtered);
            }

            // If everything got filtered out, show a single placeholder
            if (result.Count == 0 && messages.Count > 0)
                result.Add(_placeholder);

            return result;
        }

        string FilterSingleMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var sb = new StringBuilder();
            var replacedCount = 0;
            var emojiCount = 0;

            var enumerator = StringInfo.GetTextElementEnumerator(message);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();

                if (IsEmojiTextElement(element))
                {
                    emojiCount++;
                    var normalized = EmojiPaletteData.NormalizeEmoji(element);

                    if (_allowedSet.Contains(normalized))
                    {
                        sb.Append(element); // keep original (with variation selectors etc.)
                    }
                    else
                    {
                        sb.Append(_placeholder);
                        replacedCount++;
                    }
                }
                else
                {
                    // Keep non-emoji characters (spaces, etc.)
                    sb.Append(element);
                }
            }

            return sb.ToString().Trim();
        }

        string PickRandomAllowed()
        {
            if (_allowedList.Count == 0) return _placeholder;
            return _allowedList[UnityEngine.Random.Range(0, _allowedList.Count)];
        }

        // ── Emoji detection (adapted from EmojiChatPanelView) ──

        static bool IsEmojiTextElement(string textElement)
        {
            if (string.IsNullOrEmpty(textElement))
                return false;

            for (var i = 0; i < textElement.Length; i++)
            {
                var codePoint = char.ConvertToUtf32(textElement, i);
                if (char.IsSurrogatePair(textElement, i))
                    i++;

                if (IsEmojiBaseCodePoint(codePoint))
                    return true;
            }

            return false;
        }

        static bool IsEmojiBaseCodePoint(int codePoint)
        {
            return (codePoint >= 0x1F000 && codePoint <= 0x1FAFF) ||
                   (codePoint >= 0x2600 && codePoint <= 0x27BF) ||
                   (codePoint >= 0x2300 && codePoint <= 0x23FF) ||
                   (codePoint >= 0x2B00 && codePoint <= 0x2BFF);
        }
    }
}
