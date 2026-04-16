using UnityEngine;
using ChillAI.Controller;
using ChillAI.Core.Signals;
using Zenject;
using System;

namespace ChillAI.View.Character
{
    public class DogAnimationView : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;

        [SerializeField, Tooltip("Animation Parameters")]
        private string waitBool = "wait", pressTrigger = "press", idleTrigger = "idle";

        [SerializeField, Tooltip("Animator int parameter: number of received messages in this response batch.")]
        private string responseCountInt = "chat_response_count";

        /// <summary>
        /// Exposes AI message count so other components can react without
        /// directly coupling to the chat signal.
        /// </summary>
        public event Action<int> ResponseMessageCountChanged;

        EmojiChatController _chatController;
        SignalBus _signalBus;

        int _waitBoolHash;
        int _pressTriggerHash;
        int _idleTriggerHash;
        int _responseCountHash;
        bool _lastProcessingState;

        [Inject]
        public void Construct(EmojiChatController chatController, SignalBus signalBus)
        {
            _chatController = chatController;
            _signalBus = signalBus;
        }

        void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            _waitBoolHash = Animator.StringToHash(waitBool);
            _pressTriggerHash = Animator.StringToHash(pressTrigger);
            _idleTriggerHash = Animator.StringToHash(idleTrigger);
            _responseCountHash = Animator.StringToHash(responseCountInt);
        }

        void OnEnable()
        {
            _signalBus?.Subscribe<EmojiChatResponseSignal>(OnEmojiChatResponse);
            _lastProcessingState = _chatController != null && _chatController.IsProcessing;
            ApplyWaitState(_lastProcessingState);
        }

        void OnDisable()
        {
            _signalBus?.TryUnsubscribe<EmojiChatResponseSignal>(OnEmojiChatResponse);
        }

        void Update()
        {
            if (animator == null || _chatController == null)
                return;

            var isProcessing = _chatController.IsProcessing;
            if (isProcessing == _lastProcessingState)
                return;

            _lastProcessingState = isProcessing;
            ApplyWaitState(isProcessing);
        }

        void OnEmojiChatResponse(EmojiChatResponseSignal signal)
        {
            if (animator == null || signal == null)
                return;

            ApplyWaitState(false);
            _lastProcessingState = false;

            var count = Mathf.Max(0, signal.Messages?.Count ?? 0);
            animator.SetInteger(_responseCountHash, count);
            ResponseMessageCountChanged?.Invoke(count);

            if (!signal.IsError && count > 0)
            {
                TriggerEmojiButtonPressAnimation(count);
                return;
            }

            // Reset opposite trigger to keep transitions deterministic.
            animator.ResetTrigger(_pressTriggerHash);
            animator.ResetTrigger(_idleTriggerHash);
            animator.SetTrigger(_idleTriggerHash);
        }

        /// <summary>
        /// Proactively plays the "emoji button pressed" animation without waiting
        /// for an AI response signal.
        /// </summary>
        public void TriggerEmojiButtonPressAnimation(int responseCount = 1)
        {
            if (animator == null)
                return;

            ApplyWaitState(false);
            _lastProcessingState = false;

            var clampedCount = Mathf.Max(0, responseCount);
            animator.SetInteger(_responseCountHash, clampedCount);
            ResponseMessageCountChanged?.Invoke(clampedCount);

            animator.ResetTrigger(_idleTriggerHash);
            animator.ResetTrigger(_pressTriggerHash);
            animator.SetTrigger(_pressTriggerHash);
        }

        void ApplyWaitState(bool isWaiting)
        {
            if (animator == null)
                return;

            animator.SetBool(_waitBoolHash, isWaiting);
        }
    }
}

