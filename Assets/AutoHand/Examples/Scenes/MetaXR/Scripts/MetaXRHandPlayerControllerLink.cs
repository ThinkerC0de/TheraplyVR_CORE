using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Autohand.Demo;
using OVR; // Ensure OVR namespace is available

namespace Autohand.Demo {
    [HelpURL("https://app.gitbook.com/s/5zKO0EvOjzUDeT2aiFk3/auto-hand/controller-input")]
    public class MetaXRHandPlayerControllerLink : MonoBehaviour {
        [Header("Player Reference")]
        [Tooltip("Reference to the AutoHandPlayer component.")]
        public AutoHandPlayer player;

        [Header("OVR Input Settings")]
        [Tooltip("Controller to read input from. Set to LTouch or RTouch.")]
        public OVRInput.Controller movementController = OVRInput.Controller.RTouch;
        [Tooltip("Controller to read input from. Set to LTouch or RTouch.")]
        public OVRInput.Controller turningController = OVRInput.Controller.RTouch;

        [Tooltip("Axis to use for movement. Typically PrimaryThumbstick or SecondaryThumbstick.")]
        public OVRInput.Axis2D movementAxis = OVRInput.Axis2D.PrimaryThumbstick;

        [Tooltip("Axis to use for turning. Typically PrimaryThumbstick or SecondaryThumbstick.")]
        public OVRInput.Axis2D turningAxis = OVRInput.Axis2D.SecondaryThumbstick;

        private Vector2 currentMoveInput = Vector2.zero;
        private float currentTurnInput = 0f;

        private void Update() {
            currentMoveInput = OVRInput.Get(movementAxis, movementController);
            currentTurnInput = OVRInput.Get(turningAxis, turningController).x;
        }

        private void FixedUpdate() {
            player.Move(currentMoveInput);
            player.Turn(currentTurnInput);
        }
    }
}
