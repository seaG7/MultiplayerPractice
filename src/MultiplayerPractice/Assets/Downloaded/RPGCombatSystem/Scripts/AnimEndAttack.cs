using UnityEngine;

public class AnimEndAttack : StateMachineBehaviour
{
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.transform.GetComponent<AnimatorEvents>().EnableMove();
    }
}
