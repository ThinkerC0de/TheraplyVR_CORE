using Autohand;
using Autohand.Demo;
using UnityEngine;

public class HittingStick : MonoBehaviour
{
    public enum HandType
    {
        NotSelected,
        RightHand,
        LeftHand
    }

    public HandType handType;
    public XRHandControllerLink handControllerLink;
    [SerializeField] private Grabbable handGrabbable;
    [SerializeField] private PiniataLevelManager _piniataLevelManager;

    private GameObject hand;

    public void OnGrab()
    {
        hand = handGrabbable.GetHeldBy()[0].gameObject;
        var heldHand = hand.GetComponent<Hand>();
        handControllerLink = hand.GetComponent<XRHandControllerLink>();
        handType = heldHand.left ? HandType.LeftHand : HandType.RightHand;
        handControllerLink.enabled = false;
        _piniataLevelManager.CheckIfSticksChosen();
    }

    [ContextMenu("Drop Stick")]
    public void OnGameEnded()
    {
        if (handControllerLink == null)
        {
            return;
        }

        handType = HandType.NotSelected;
        handControllerLink.enabled = true;
        gameObject.SetActive(false);
        transform.parent = null;
    }

    public void RegisterCorrectPointInteraction()
    {
        PiniataZoneTruthTelemetry.RegisterCorrectPointInteraction();
    }

    public void TryRegisterPiniataBodyHit(Collider targetCollider, float? impactMagnitude)
    {
        // Telemetry for piniata targets is emitted on the target side via TargetValidationZone.
    }
}
