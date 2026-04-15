using UnityEngine;
using System.Collections.Generic;

namespace ChillAI.View.Character
{
    /// <summary>
    /// Repeats a state for a target loop count (per animator instance),
    /// then fires an exit trigger.
    /// Loop count is read from an animator int parameter.
    /// </summary>
    public class DogAnimatorSMB : StateMachineBehaviour
    {
        [Header("Runtime Loop Source")]
        [SerializeField, Tooltip("Animator int parameter written by DogAnimationView.")]
        string repeatCountInt = "chat_response_count";

        [SerializeField, Min(1)]
        int fallbackLoopCount = 1;

        [Header("State Exit")]
        [SerializeField, Tooltip("Trigger to fire after loop count is reached.")]
        string finishTrigger = "idle";

        [SerializeField, Tooltip("Optional fallback state for forced exit if trigger transition is blocked.")]
        string fallbackIdleState = "dog_idle";

        [SerializeField, Tooltip("When enabled, force cross-fade to fallback idle after target loops.")]
        bool forceIdleFallback = true;

        [SerializeField, Min(0f)]
        float fallbackCrossFadeSeconds = 0.06f;

        [SerializeField, Range(0.5f, 0.99f), Tooltip("When using self-transition repeat, request exit near the end of the target loop.")]
        float exitNormalizedThreshold = 0.9f;

        [SerializeField]
        bool enableDebugLog = false;

        class RuntimeState
        {
            public int targetLoopCount;
            public int stateFullPathHash;
            public int enterCount;
            public int lastExitFrame;
            public bool forceExitRequested;
        }

        static readonly Dictionary<int, RuntimeState> RuntimeByAnimator = new();
        int _repeatCountHash;
        int _finishTriggerHash;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _repeatCountHash = Animator.StringToHash(repeatCountInt);
            _finishTriggerHash = Animator.StringToHash(finishTrigger);

            var animatorId = animator.GetInstanceID();
            var fromAnimator = animator.GetInteger(_repeatCountHash);
            var loopCount = Mathf.Max(1, fromAnimator > 0 ? fromAnimator : fallbackLoopCount);
            animator.ResetTrigger(_finishTriggerHash);

            RuntimeByAnimator.TryGetValue(animatorId, out var existing);
            var sameStateReEnter = existing != null &&
                                   existing.stateFullPathHash == stateInfo.fullPathHash &&
                                   Time.frameCount - existing.lastExitFrame <= 1;

            var runtime = sameStateReEnter ? existing : new RuntimeState();
            runtime.targetLoopCount = loopCount;
            runtime.stateFullPathHash = stateInfo.fullPathHash;
            runtime.enterCount = sameStateReEnter ? runtime.enterCount + 1 : 1;
            runtime.forceExitRequested = false;
            RuntimeByAnimator[animatorId] = runtime;

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[DogAnimatorSMB] Enter state={stateInfo.fullPathHash}, enterCount={runtime.enterCount}, " +
                    $"targetLoops={runtime.targetLoopCount}, fromAnimator={fromAnimator}");
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            var animatorId = animator.GetInstanceID();
            if (!RuntimeByAnimator.TryGetValue(animatorId, out var state))
                return;

            // For loop states, normalizedTime grows as 0..1 for first cycle, 1..2 for second, etc.
            var completedLoops = Mathf.FloorToInt(stateInfo.normalizedTime);
            var reachedByLoopingClip = completedLoops >= Mathf.Max(0, state.targetLoopCount - 1);
            var reachedBySelfReEnter =
                state.enterCount >= state.targetLoopCount &&
                stateInfo.normalizedTime >= exitNormalizedThreshold;

            if (!reachedByLoopingClip && !reachedBySelfReEnter)
                return;

            // Keep requesting trigger during the final loop.
            // This avoids missing a one-frame trigger window when transitions
            // also depend on Exit Time / other conditions.
            animator.ResetTrigger(_finishTriggerHash);
            animator.SetTrigger(_finishTriggerHash);

            if (enableDebugLog)
            {
                Debug.Log(
                    $"[DogAnimatorSMB] Request exit state={stateInfo.fullPathHash}, " +
                    $"normalized={stateInfo.normalizedTime:F2}, completedLoops={completedLoops}, " +
                    $"enterCount={state.enterCount}/{state.targetLoopCount}");
            }

            if (!forceIdleFallback || state.forceExitRequested || !reachedBySelfReEnter)
                return;

            state.forceExitRequested = true;
            if (!string.IsNullOrEmpty(fallbackIdleState))
            {
                animator.CrossFadeInFixedTime(fallbackIdleState, fallbackCrossFadeSeconds, layerIndex);
                if (enableDebugLog)
                    Debug.Log($"[DogAnimatorSMB] Force fallback -> {fallbackIdleState}");
            }
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!RuntimeByAnimator.TryGetValue(animator.GetInstanceID(), out var state))
                return;

            state.lastExitFrame = Time.frameCount;
        }
    }
}