using System.Collections;
using System.Collections.Generic;
using Autohand;
using Autohand.Demo;
using UnityEngine;

public class StickGlue : MonoBehaviour
{
    public void StickToHand(GameObject gObj)
    {
        var hand = gObj.GetComponent<Grabbable>().GetHeldBy()[0].gameObject;
        var h = hand.GetComponent<Hand>();
        hand.GetComponent<XRHandControllerLink>().enabled = false;
    }
}
