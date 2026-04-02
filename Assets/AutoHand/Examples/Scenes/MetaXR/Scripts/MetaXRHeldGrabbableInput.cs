using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Autohand.Demo;
using OVR;

namespace Autohand {
    public class MetaXRHeldGrabbableInput : MonoBehaviour {
        public Hand hand;
        public OVRInput.Button startButton;
        public OVRInput.Button stopButton;
        [Tooltip("Must match the input layer of the GrabbableInput layer value")]
        public int inputLayer = 0;

        [Space]
        [Tooltip("If false, the events will only trigger if the hand is holding a grabbable with a GrabbableInput script of the same input layer")]
        public bool alwaysTriggerEvents = false;
        public UnityHandGrabEvent startInput;
        public UnityHandGrabEvent stopInput;

        public bool inputActive { get; set; }

        [Header("Controller")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        private void OnEnable() {
            if(hand == null && !gameObject.TryGetComponent(out hand))
                Debug.LogError("AUTOHAND: Hand not found on MetaXRHeldGrabbableInput", this);
        }

        private void Update() {
            if(OVRInput.GetDown(startButton, controller)) {
                StartInput();
            }
            if(OVRInput.GetUp(stopButton, controller)) {
                StopInput();
            }
        }

        private void StartInput() {
            if(!inputActive) {
                if(alwaysTriggerEvents)
                    startInput.Invoke(hand, hand.holdingObj);

                if(hand.holdingObj != null) {
                    if(hand.holdingObj.TryGetComponent<MetaXRGrabbableInput>(out var grabbableInput) && grabbableInput.inputLayer == inputLayer) {
                        grabbableInput.PressInput(hand);
                        if(!alwaysTriggerEvents)
                            startInput.Invoke(hand, hand.holdingObj);
                    }
                }

                inputActive = true;
            }
        }

        private void StopInput() {
            if(inputActive) {
                if(alwaysTriggerEvents)
                    stopInput.Invoke(hand, hand.holdingObj);

                if(hand.holdingObj != null) {
                    if(hand.holdingObj.TryGetComponent<MetaXRGrabbableInput>(out var grabbableInput) && grabbableInput.inputLayer == inputLayer) {
                        grabbableInput.ReleaseInput(hand);
                        if(!alwaysTriggerEvents)
                            stopInput.Invoke(hand, hand.holdingObj);
                    }
                }
                inputActive = false;
            }
        }
    }
}
