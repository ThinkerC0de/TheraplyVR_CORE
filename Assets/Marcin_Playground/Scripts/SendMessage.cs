using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

public class SendMessage : MonoBehaviour
{
    private bool _t = false;

    VisualEffect visualEffect;
    VFXEventAttribute eventAttribute;

    private static readonly ExposedProperty enteredTriggerEvent1 = "ChangeParticle1";
    private static readonly ExposedProperty enteredTriggerEvent2 = "ChangeParticle2";
    
    void Start()
    {
        visualEffect = GetComponent<VisualEffect>();   
        // Caches an Event Attribute matching the
        // visualEffect.visualEffectAsset graph.
        eventAttribute = visualEffect.CreateVFXEventAttribute();
    }
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_t)
                visualEffect.SendEvent(enteredTriggerEvent1, eventAttribute);
            else
            {
                visualEffect.SendEvent(enteredTriggerEvent2, eventAttribute);
            }

            _t = !_t;
        }
    }
}
