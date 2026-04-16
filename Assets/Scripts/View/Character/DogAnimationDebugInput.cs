using System.Collections.Generic;
using ChillAI.Core.Signals;
using ChillAI.Core.Settings;
using UnityEngine;
using Zenject;

namespace ChillAI.View.Character
{
    /// <summary>
    /// Temporary input helper for quickly validating dog emoji-button animation.
    /// Press Q + 1 to trigger with a random emoji sample.
    /// </summary>
    public class DogAnimationDebugInput : MonoBehaviour
    {
        [SerializeField] DogAnimationView dogAnimationView;
        [SerializeField] EmojiPaletteData emojiPalette;
        SignalBus _signalBus;

        [Header("Debug Hotkey")]
        [SerializeField] KeyCode modifierKey = KeyCode.Q;
        [SerializeField] KeyCode triggerKey = KeyCode.Alpha1;

        [Header("Fallback Random Emojis")]
        [SerializeField] List<string> fallbackEmojis = new()
        {
            "😀",
            "🥳",
            "🐶",
            "🎉",
            "🍖"
        };

        [Inject]
        public void Construct(SignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        void Awake()
        {
            if (dogAnimationView == null)
                dogAnimationView = GetComponent<DogAnimationView>();
        }

        void Update()
        {
            if (!Input.GetKey(modifierKey) || !Input.GetKeyDown(triggerKey))
                return;

            if (dogAnimationView == null)
            {
                Debug.LogWarning("[ChillAI] DogAnimationDebugInput missing DogAnimationView reference.");
                return;
            }

            var emoji = PickRandomEmoji();

            if (_signalBus != null)
            {
                _signalBus.Fire(new EmojiChatResponseSignal("[debug]", new List<string> { emoji },
                    skipFirstBubbleDelay: true));
            }
            else
            {
                // Fallback path for scenes not using SignalBus injection.
                dogAnimationView.TriggerEmojiButtonPressAnimation();
                Debug.LogWarning("[ChillAI] DogAnimationDebugInput SignalBus is null; bubble signal was not fired.");
            }

            Debug.Log($"[ChillAI] Triggered dog emoji-button animation with random emoji: {emoji}");
        }

        string PickRandomEmoji()
        {
            var available = new List<string>();

            if (emojiPalette != null)
            {
                foreach (var list in emojiPalette.activeLists)
                {
                    if (list?.emojis == null)
                        continue;

                    foreach (var entry in list.emojis)
                    {
                        if (!string.IsNullOrWhiteSpace(entry?.emoji))
                            available.Add(entry.emoji.Trim());
                    }
                }
            }

            if (available.Count == 0)
                available = fallbackEmojis;

            if (available == null || available.Count == 0)
                return "😀";

            var index = Random.Range(0, available.Count);
            return available[index];
        }
    }
}
