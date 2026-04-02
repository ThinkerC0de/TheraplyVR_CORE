using UnityEngine;

public class WindmillRotate : MonoBehaviour
{
    public float rotationSpeed = 10f; // stopnie na sekundę

    void Update()
    {
        transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);
    }
}
