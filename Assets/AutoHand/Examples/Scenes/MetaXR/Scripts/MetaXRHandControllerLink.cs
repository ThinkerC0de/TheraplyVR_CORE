using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Autohand.Demo;

namespace Autohand.Demo {
    [HelpURL("https://app.gitbook.com/s/5zKO0EvOjzUDeT2aiFk3/auto-hand/controller-input")]
    public class MetaXRHandControllerLink : HandControllerLink {

        [Header("Input Buttons")]
        public OVRInput.Button grabButton;
        public OVRInput.Button squeezeButton;
        public OVRInput.Axis1D grabValue;
        public OVRInput.Axis1D squeezeValue;

        private bool grabbing;
        private bool squeezing;
        OVRInput.Controller controller;

        void Start() {
            if(hand.left)
                handLeft = this;
            else
                handRight = this;

            controller = hand.left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        }

        void Update() {


            hand.SetGrip(OVRInput.Get(grabValue, controller), OVRInput.Get(squeezeValue, controller));

            if(OVRInput.GetDown(grabButton, controller) && !grabbing) {
                hand.Grab();
                grabbing = true;
            }
            else if(OVRInput.GetUp(grabButton, controller) && grabbing) {
                hand.Release();
                grabbing = false;
            }

            if(OVRInput.GetDown(squeezeButton, controller) && !squeezing) {
                hand.Squeeze();
                squeezing = true;
            }
            else if(OVRInput.GetUp(squeezeButton, controller) && squeezing) {
                hand.Unsqueeze();
                squeezing = false;
            }
        }

        public override void TryHapticImpulse(float duration, float amplitude, float frequency = 10) {
            amplitude = Mathf.Clamp01(amplitude);
            float freqClamped = Mathf.Clamp(frequency, 0f, 1f);

            StartCoroutine(HapticCoroutine(duration, amplitude, freqClamped));

            base.TryHapticImpulse(duration, amplitude, frequency);
        }

        private IEnumerator HapticCoroutine(float duration, float amplitude, float frequency) {
            OVRInput.Controller controller = hand.left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

            OVRInput.SetControllerVibration(frequency, amplitude, controller);

            yield return new WaitForSeconds(duration);

            OVRInput.SetControllerVibration(0, 0, controller);
        }
    }
}
