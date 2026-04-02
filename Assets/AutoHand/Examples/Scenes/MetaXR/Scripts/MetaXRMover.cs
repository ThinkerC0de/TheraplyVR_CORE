using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Autohand.Demo {
    public class MetaXRMover : MonoBehaviour {
        [Header("OVR Input Settings")]
        public OVRInput.Axis2D moveAxis = OVRInput.Axis2D.PrimaryThumbstick;
        public OVRInput.Axis2D turnAxis = OVRInput.Axis2D.SecondaryThumbstick;
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        [Header("Body")]
        public GameObject cam;
        private CharacterController controllerComponent;

        [Header("Settings")]
        public bool snapTurning;
        public float turnAngle;
        public float heightStep;
        public float minHeight, maxHeight;
        public float speed = 5;
        public float gravity = 1;

        private float currentGravity = 0;

        private bool turningReset = true, heightReset = true;

        private void Start() {
            controllerComponent = GetComponent<CharacterController>();
            gameObject.layer = LayerMask.NameToLayer("HandPlayer");
        }

        private void Update() {
            Vector3 headRotation = new Vector3(0, cam.transform.eulerAngles.y, 0);

            Vector2 moveInput = OVRInput.Get(moveAxis, controller);

            if(Mathf.Abs(moveInput.x) < 0.1f)
                moveInput.x = 0;
            if(Mathf.Abs(moveInput.y) < 0.1f)
                moveInput.y = 0;

            Vector3 direction = new Vector3(moveInput.x, 0, moveInput.y);
            direction = Quaternion.Euler(headRotation) * direction;

            if(controllerComponent.isGrounded)
                currentGravity = 0;
            else
                currentGravity = Physics.gravity.y * gravity;

            controllerComponent.Move(new Vector3(direction.x * speed, currentGravity, direction.z * speed) * Time.deltaTime);

            Vector2 turnInput = OVRInput.Get(turnAxis, controller);

            if(snapTurning) {
                if(turnInput.x > 0.7f && turningReset) {
                    transform.rotation *= Quaternion.Euler(0, turnAngle, 0);
                    turningReset = false;
                }
                else if(turnInput.x < -0.7f && turningReset) {
                    transform.rotation *= Quaternion.Euler(0, -turnAngle, 0);
                    turningReset = false;
                }
                else if(turnInput.y > 0.7f && heightReset) {
                    if(transform.position.y >= maxHeight) {
                        transform.position = new Vector3(transform.position.x, maxHeight, transform.position.z);
                        SetControllerHeight(maxHeight);
                    }
                    else {
                        transform.position += new Vector3(0, heightStep, 0);
                        AddControllerHeight(heightStep);
                    }

                    heightReset = false;
                }
                else if(turnInput.y < -0.7f && heightReset) {
                    if(transform.position.y <= minHeight) {
                        SetControllerHeight(minHeight);
                        transform.position = new Vector3(transform.position.x, minHeight, transform.position.z);
                    }
                    else {
                        AddControllerHeight(-heightStep);
                        transform.position += new Vector3(0, -heightStep, 0);
                    }

                    heightReset = false;
                }

                if(Mathf.Abs(turnInput.x) < 0.4f)
                    turningReset = true;
                if(Mathf.Abs(turnInput.y) < 0.4f)
                    heightReset = true;
            }
            else {
                transform.rotation *= Quaternion.Euler(0, Time.deltaTime * turnAngle * turnInput.x, 0);
                transform.position += new Vector3(0, Time.deltaTime * heightStep * turnInput.y, 0);

                AddControllerHeight(Time.deltaTime * heightStep * turnInput.y);

                if(transform.position.y <= minHeight)
                    transform.position = new Vector3(transform.position.x, minHeight, transform.position.z);
                else if(transform.position.y >= maxHeight)
                    transform.position = new Vector3(transform.position.x, maxHeight, transform.position.z);
            }
        }

        private void AddControllerHeight(float height) {
            controllerComponent.height += height;
            controllerComponent.center = new Vector3(0, controllerComponent.height / 2f, 0);
        }

        private void SetControllerHeight(float height) {
            controllerComponent.height = height;
            controllerComponent.center = new Vector3(0, height / 2f, 0);
        }
    }
}
