using UnityEngine;
/// <summary>
/// Clears a trigger after a state is exited.
/// </summary>
public class ClearTriggerSMB : StateMachineBehaviour
{
    [SerializeField] private string triggerName;

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.ResetTrigger(triggerName);
    }
}
