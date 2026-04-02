using UnityEngine;

public class BillboardY : MonoBehaviour
{
    [Header("Cel (np. kamera gracza)")]
    [SerializeField] private Transform target;

    [Header("Opcje")]
    [SerializeField] private float turnSpeedDegPerSec = 720f; // 0 = natychmiast
    [SerializeField] private float yawOffsetDeg = 0f;         // jeśli model patrzy „bokiem”

    private void Awake()
    {
        if (!target)
        {
            // W VR zwykle MainCamera to oko „CenterEye”.
            var cam = Camera.main;
            if (cam) target = cam.transform;
        }
    }

    private void LateUpdate()
    {
        if (!target) return;

        // Kierunek tylko w poziomie (Y=0), czyli obrót wyłącznie w osi Y.
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up)
                          * Quaternion.Euler(0f, yawOffsetDeg, 0f);

        if (turnSpeedDegPerSec <= 0f)
            transform.rotation = look; // natychmiast
        else
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, look, turnSpeedDegPerSec * Time.deltaTime);
    }
}