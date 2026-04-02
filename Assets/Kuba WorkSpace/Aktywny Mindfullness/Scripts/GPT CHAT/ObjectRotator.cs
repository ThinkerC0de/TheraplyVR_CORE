using UnityEngine;

public class ObjectRotator : MonoBehaviour
{ public Transform triggerObject;
    public float rotationSpeed = 90f;
    public float triggerSpeed = 5f;

    private bool isRotating = false;
    private bool shouldRotateLeft = false;
    private Vector3 targetRotation;

    private void Update()
    {
        if (isRotating)
        {
            // Rotate towards the target rotation
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(targetRotation), rotationSpeed * Time.deltaTime);

            // Check if the rotation is complete
            if (Quaternion.Angle(transform.rotation, Quaternion.Euler(targetRotation)) == 0f)
            {
                isRotating = false;
            }
        }
        else
        {
            // Check the speed and direction of the trigger object
            float speed = triggerObject.InverseTransformDirection(triggerObject.GetComponent<Rigidbody>().linearVelocity).x;
            bool isMovingLeft = speed < 0f;

            if (isMovingLeft && Mathf.Abs(speed) > triggerSpeed)
            {
                // Trigger left rotation
                shouldRotateLeft = true;
                targetRotation = transform.rotation.eulerAngles - new Vector3(0f, 90f, 0f);
                isRotating = true;
            }
            else
            {
                shouldRotateLeft = false;
            }
        }
    }
}