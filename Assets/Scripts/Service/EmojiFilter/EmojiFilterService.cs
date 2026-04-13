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
        readonly HashSet<string> _allowedSet = new();
        readonly string _placeholder;
        readonly List<string> _allowedList = new();
        string _promptConstraint;

        public IReadOnlyCollection<string> AllowedEmojis => _allowedSet;

        public EmojiFilterService(EmojiPaletteData palette)
        {
            _palette = palette;
            _placeholder = palette.placeholderEmoji ?? "\U0001F512";
            RebuildCache();
        }

        public bool UnlockEmojiList(EmojiListData emojiList)
        {
            if (emojiList == null || _palette == null)
                return false;

            if (_palette.activeLists == null)
                _palette.activeLists = new List<EmojiListData>();
            if (_palette.activeLists.Contains(emojiList))
                return false;

            _palette.activeLists.Add(emojiList);
            RebuildCache();
            Debug.Log($"[ChillAI] [emoji-filter] unlocked emoji list: {emojiList.listId}");
            return true;
        }

        public string BuildPromptConstraint() => _promptConstraint;

        public List<string> FilterMessages(List<string> messages)
        {
            if (messages == null) return new List<string>();
            if (_allowedSet.Count == 0) return messages;

            var result = new List<string>(messages.Count);
            foreach (var msg in messages)
            {
                var filtered = FilterSingleMessage(msg);
                if (!string.IsNullOrEmpty(filtered))
                    result.Add(filtered);
            }

            if (result.Count == 0 && messages.Count > 0)
                result.Add(_placeholder);

            return result;
        }

        string FilterSingleMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var tokens = Tokenize(message);
            var sb = new StringBuilder();

            foreach (var token in tokens)
            {
                if (token.isEmoji)
                {
                    var normalized = EmojiPaletteData.NormalizeEmoji(token.text);
                    sb.Append(_allowedSet.Contains(normalized) ? token.text : _placeholder);
                }
                else
                {
                    sb.Append(token.text);
                }
            }

            return sb.ToString().Trim();
        }

        // ── Emoji tokenizer (ZWJ-aware) ──

        /// <summary>
        /// Splits a string into tokens. Each token is either a complete emoji sequence
        /// (including ZWJ chains, variation selectors, skin tones, gender signs)
        /// or a run of non-emoji text.
        /// </summary>
        static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            var textBuf = new StringBuilder();
            var pos = 0;

            while (pos < s.Length)
            {
                var emojiEnd = TryReadEmojiSequence(s, pos);
                if (emojiEnd > pos)
                {
                    // Flush pending text
                    if (textBuf.Length > 0)
                    {
                        tokens.Add(new Token { text = textBuf.ToString(), isEmoji = false });
                        textBuf.Clear();
                    }
                    tokens.Add(new Token { text = s.Substring(pos, emojiEnd - pos), isEmoji = true });
                    pos = emojiEnd;
                }
                else
                {
                    // Non-emoji character — accumulate into text buffer
                    if (char.IsHighSurrogate(s[pos]) && pos + 1 < s.Length && char.IsLowSurrogate(s[pos + 1]))
                    {
                        textBuf.Append(s[pos]);
                        textBuf.Append(s[pos + 1]);
                        pos += 2;
                    }
                    else
                    {
                        textBuf.Append(s[pos]);
                        pos++;
                    }
                }
            }

            if (textBuf.Length > 0)
                tokens.Add(new Token { text = textBuf.ToString(), isEmoji = false });

            return tokens;
        }

        /// <summary>
        /// Attempts to read a complete emoji sequence starting at <paramref name="start"/>.
        /// Returns the end index (exclusive). If no emoji found, returns <paramref name="start"/>.
        ///
        /// Greedily consumes:
        ///   base emoji → (variation selector)? → (skin tone)? → (ZWJ → next emoji component → ...)*
        /// </summary>
        static int TryReadEmojiSequence(string s, int start)
        {
            var pos = start;
            var cp = ReadCodePoint(s, pos);
            if (cp < 0 || !IsEmojiStartCodePoint(cp))
                return start;

            pos += CharCount(cp);

            // Greedily consume modifiers and ZWJ continuations
            while (pos < s.Length)
            {
                var next = ReadCodePoint(s, pos);
                if (next < 0) break;

                // Variation selectors (FE0E / FE0F)
                if (next == 0xFE0E || next == 0xFE0F)
                {
                    pos += 1;
                    continue;
                }

                // Skin tone modifiers (1F3FB–1F3FF)
                if (next >= 0x1F3FB && next <= 0x1F3FF)
                {
                    pos += CharCount(next);
                    continue;
                }

                // Combining Enclosing Keycap (20E3)
                if (next == 0x20E3)
                {
                    pos += 1;
                    continue;
                }

                // ZWJ → consume the next emoji component
                if (next == 0x200D)
                {
                    var afterZwj = pos + 1; // ZWJ is BMP, 1 char
                    if (afterZwj >= s.Length) break;

                    var zwjTarget = ReadCodePoint(s, afterZwj);
                    if (zwjTarget >= 0 && IsEmojiComponentCodePoint(zwjTarget))
                    {
                        pos = afterZwj + CharCount(zwjTarget);
                        continue; // loop back to consume further modifiers / ZWJ chains
                    }
                    break; // ZWJ not followed by valid component — stop
                }

                break; // nothing more to consume
            }

            return pos > start + CharCount(ReadCodePoint(s, start)) ? pos : pos; // always return what we consumed
        }

        // ── Code point helpers ──

        static int ReadCodePoint(string s, int index)
        {
            if (index >= s.Length) return -1;
            if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
                return char.ConvertToUtf32(s[index], s[index + 1]);
            return s[index];
        }

        static int CharCount(int codePoint) => codePoint > 0xFFFF ? 2 : 1;

        /// <summary>
        /// Code points that can START an emoji sequence.
        /// </summary>
        static bool IsEmojiStartCodePoint(int cp)
        {
            return IsEmojiBaseCodePoint(cp) || IsRegionalIndicator(cp);
        }

        /// <summary>
        /// Code points valid after a ZWJ (broader than start — includes gender signs, etc.)
        /// </summary>
        static bool IsEmojiComponentCodePoint(int cp)
        {
            return IsEmojiBaseCodePoint(cp) ||
                   cp == 0x2640 ||   // ♀ female sign
                   cp == 0x2642 ||   // ♂ male sign
                   cp == 0x2695 ||   // ⚕ medical symbol
                   cp == 0x2696 ||   // ⚖ balance scale
                   cp == 0x2708 ||   // ✈ airplane
                   cp == 0x2764 ||   // ❤ heavy heart
                   IsRegionalIndicator(cp);
        }

        static bool IsEmojiBaseCodePoint(int cp)
        {
            return (cp >= 0x1F000 && cp <= 0x1FAFF) || // emoticons, symbols, pictographs
                   (cp >= 0x2600 && cp <= 0x27BF) ||   // misc symbols, dingbats
                   (cp >= 0x2300 && cp <= 0x23FF) ||   // misc technical
                   (cp >= 0x2B00 && cp <= 0x2BFF);     // misc symbols & arrows
        }

        static bool IsRegionalIndicator(int cp)
        {
            return cp >= 0x1F1E6 && cp <= 0x1F1FF;
        }

        struct Token
        {
            public string text;
            public bool isEmoji;
        }

        void RebuildCache()
        {
            _allowedSet.Clear();
            foreach (var item in _palette.BuildAllowedSet())
                _allowedSet.Add(item);

            _allowedList.Clear();
            _allowedList.AddRange(_allowedSet);

            var emojiString = _palette.BuildAllowedEmojiString();
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
    }
}
