using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnExit : StateMachineBehaviour
{
    [SerializeField] private Animation animation;
    [SerializeField] private bool lockLayer;
    [SerializeField] private float crossfade;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        LisekMovement.instance.StartCoroutine(Wait());

        IEnumerator Wait()
        {
            yield return new WaitForSeconds(stateInfo.length - crossfade);
        }
    }
}
