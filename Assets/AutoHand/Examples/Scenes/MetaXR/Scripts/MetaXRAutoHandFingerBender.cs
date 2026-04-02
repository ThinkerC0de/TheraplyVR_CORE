using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Autohand.Demo {
    public class MetaXRAutoHandFingerBender : MonoBehaviour {
        public Hand hand;
        public OVRInput.Button bendButton;

        [HideInInspector]
        public float[] bendOffsets;

        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        private bool pressed;

        private void Update() {
            if(!pressed && OVRInput.GetDown(bendButton, controller)) {
                pressed = true;
                for(int i = 0; i < hand.fingers.Length; i++) {
                    hand.fingers[i].bendOffset += bendOffsets[i];
                }
            }

            if(pressed && OVRInput.GetUp(bendButton, controller)) {
                pressed = false;
                for(int i = 0; i < hand.fingers.Length; i++) {
                    hand.fingers[i].bendOffset -= bendOffsets[i];
                }
            }
        }
    }
}
