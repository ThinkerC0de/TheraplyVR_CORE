using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Autohand.Demo {
    public enum PressOrRealease {
        Press,
        Release
    }   

    public class MetaXRControllerEvent : MonoBehaviour {
        [Header("Input Settings")]
        public OVRInput.Button inputButton;
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        public PressOrRealease activationType;

        [Header("Events")]
        public UnityEvent inputEvent;

        private void Update() {
            if(activationType == PressOrRealease.Press) {
                if(OVRInput.GetDown(inputButton, controller)) {
                    inputEvent?.Invoke();
                }
            }
            else if(activationType == PressOrRealease.Release) {
                if(OVRInput.GetUp(inputButton, controller)) {
                    inputEvent?.Invoke();
                }
            }
        }
    }
}
