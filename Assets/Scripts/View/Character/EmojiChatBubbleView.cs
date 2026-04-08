using System;
using System.Collections;
using ChillAI.Core.Signals;
using TMPro;
using UnityEngine;
using Zenject;

namespace ChillAI.View.Character
{
    /// <summary>
    /// Displays emoji responses from the emoji-chat agent as a speech bubble
    /// near the No-Face character. Auto-hides after a duration.
    /// </summary>
    public class EmojiChatBubbleView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] TextMeshProUGUI emojiText;
        [SerializeField] GameObject bubbleContainer;

        [Header("Settings")]
        [SerializeField] float displayDuration = 5f;
        [SerializeField] float fadeOutDuration = 0.5f;

        SignalBus _signalBus;
        Coroutine _hideCoroutine;

        [Inject]
        public void Construct(SignalBus signalBus)
        {
            _signalBus = signalBus;
        }

        void OnEnable()
        {
            if (bubbleContainer != null)
                bubbleContainer.SetActive(false);

            _signalBus?.Subscribe<EmojiChatResponseSignal>(OnEmojiResponse);
        }

        void OnDisable()
        {
            _signalBus?.TryUnsubscribe<EmojiChatResponseSignal>(OnEmojiResponse);
        }

        void OnEmojiResponse(EmojiChatResponseSignal signal)
        {
            if (emojiText == null || signal.IsError) return;

            emojiText.text = string.Join(" ", signal.Messages);

            if (bubbleContainer != null)
                bubbleContainer.SetActive(true);

            // Reset hide timer
            if (_hideCoroutine != null)
                StopCoroutine(_hideCoroutine);
            _hideCoroutine = StartCoroutine(HideAfterDelay());
        }

        IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(displayDuration);

            // Simple fade by reducing alpha
            if (emojiText != null)
            {
                var elapsed = 0f;
                var originalColor = emojiText.color;

                while (elapsed < fadeOutDuration)
                {
                    elapsed += Time.deltaTime;
                    var alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
                    emojiText.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
                    yield return null;
                }

                emojiText.color = originalColor; // Reset for next time
            }

            if (bubbleContainer != null)
                bubbleContainer.SetActive(false);
        }
    }
}
