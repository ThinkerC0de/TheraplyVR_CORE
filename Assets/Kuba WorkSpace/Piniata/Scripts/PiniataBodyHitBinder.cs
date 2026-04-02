using TheraplyCore.Interactions;
using UnityEngine;

/// <summary>
/// Place on PINIATA-CUKIEREK (or any object in the piñata hierarchy).
/// Finds the Rigidbody owner and attaches PhysicsContactHitReporter there —
/// because OnCollisionEnter fires on the Rigidbody's GameObject, not on child colliders.
/// </summary>
public sealed class PiniataBodyHitBinder : MonoBehaviour
{
    private void Awake()
    {
        var game = GetComponentInParent<PiniataGame>();
        if (game == null)
        {
            game = FindFirstObjectByType<PiniataGame>();
        }

        // OnCollisionEnter fires on the Rigidbody owner — that is where the reporter must live.
        var rb = game != null ? game.Rb : null;
        if (rb == null)
        {
            rb = GetComponentInParent<Rigidbody>();
        }

        if (rb == null)
        {
            Debug.LogWarning(
                $"[PiniataBodyHitBinder] No Rigidbody found from '{gameObject.name}'. " +
                "Assign PiniataGame.Rb in the Inspector.");
            return;
        }

        var rbGo = rb.gameObject;

        var reporter = rbGo.GetComponent<PhysicsContactHitReporter>();
        if (reporter == null)
        {
            reporter = rbGo.AddComponent<PhysicsContactHitReporter>();
        }

        reporter.BindManual(
            gameId: "piniata",
            targetId: "piniata_body",
            targetName: rbGo.name,
            semanticTag: "PINIATA_BODY",
            eventType: "piniata_body_hit",
            actionOutcome: "INCORRECT",
            reasonCode: "PINIATA_BODY_HIT",
            suppressionGroupId: "PINIATA_BODY");

        if (game != null)
        {
            reporter.AddHitListener(() => game.WrongAnswer());
        }

        Debug.Log(
            $"[PiniataBodyHitBinder] Reporter attached to Rigidbody GO: '{rbGo.name}'.");
    }
}
