using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Autohand.Demo {
    public class MetaXRAutoHandAxisFingerBender : MonoBehaviour {
        public Hand hand;
        public OVRInput.Axis1D bendAxis;

        [HideInInspector]
        public float[] bendOffsets;
        private float lastAxis;

        [Header("OVR Input Settings")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        private void LateUpdate() {
            float currAxis = OVRInput.Get(bendAxis, controller);
            for(int i = 0; i < bendOffsets.Length; i++) {
                hand.fingers[i].bendOffset += (currAxis - lastAxis) * bendOffsets[i];
            }
            lastAxis = currAxis;
        }
    }
}
