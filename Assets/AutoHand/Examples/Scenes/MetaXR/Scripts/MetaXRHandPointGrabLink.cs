using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Autohand.Demo;

namespace Autohand.Demo {
    public class MetaXRHandPointGrabLink : MonoBehaviour {
        public HandDistanceGrabber pointGrab;

        [Header("Input Buttons")]
        public OVRInput.Button pointButton;
        public OVRInput.Button stopPointButton;
        public OVRInput.Button selectButton;
        public OVRInput.Button stopSelectButton;

        [Header("Controller")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        void Update() {
            if(OVRInput.GetDown(pointButton, controller)) {
                pointGrab.StartPointing();
            }
            if(OVRInput.GetUp(stopPointButton, controller)) {
                pointGrab.StopPointing();
            }
            if(OVRInput.GetDown(selectButton, controller)) {
                pointGrab.SelectTarget();
            }
            if(OVRInput.GetUp(stopSelectButton, controller)) {
                pointGrab.CancelSelect();
            }
        }
    }
}
