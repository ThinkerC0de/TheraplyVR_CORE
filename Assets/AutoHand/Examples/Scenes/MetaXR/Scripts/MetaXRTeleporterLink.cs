using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Autohand.Demo {
    public class MetaXRTeleporterLink : MonoBehaviour {
        public Teleporter hand;

        [Header("Input Buttons")]
        public OVRInput.Button teleportButton;

        [Header("Controller")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        private bool teleporting = false;

        private void Update() {
            if(!teleporting && OVRInput.GetDown(teleportButton, controller)) {
                hand.StartTeleport();
                teleporting = true;
            }

            if(teleporting && OVRInput.GetUp(teleportButton, controller)) {
                hand.Teleport();
                teleporting = false;
            }
        }
    }
}
