using System.Collections;
using UnityEngine;

public class BeetleController : MonoBehaviour
{
    [Header("Movement Settings")]
    public Transform target; // Cel DEF-HEAD
    public float speed = 0.5f; // Maksymalna prędkość
    public float detectionRange = 1.0f; // Zasięg detekcji
    public float rotationSpeed = 5f; // Szybkość obrotu

    private bool isClimbing = false; // Flaga wspinaczki

    [Header("Animation Settings")]
    [SerializeField] private Animator animator;
    private const string WALK_ANIMATION = "WALK";
    private float minVelocityForAnimation = 0.05f; // Minimalna prędkość dla animacji

    void Start()
    {
        if (target == null)
        {
            Debug.LogError("Cel nie został przypisany!");
            return;
        }

        StartCoroutine(MoveTowardsTarget());
    }

    void Update()
    {
        // Sprawdzenie prędkości i ustawienie animacji
        float velocity = (transform.forward * speed).magnitude;
        if (velocity > minVelocityForAnimation)
        {
            animator.SetTrigger(WALK_ANIMATION);
        }
    }

    IEnumerator MoveTowardsTarget()
    {
        while (!isClimbing)
        {
            // Wyznaczanie kierunku do celu
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0; // Ignorowanie osi Y, aby poruszać się tylko po powierzchni

            // Sprawdzanie, czy żuk dotarł do celu
            if (Vector3.Distance(transform.position, target.position) < detectionRange)
            {
                isClimbing = true;
                StartCoroutine(ClimbSunflower());
                yield break;
            }

            // Poruszanie żuka
            transform.position += direction * speed * Time.deltaTime;

            // Ustawienie obrotu żuka
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);

            yield return null;
        }
    }

    IEnumerator ClimbSunflower()
    {
        while (isClimbing)
        {
            // Wykonywanie Raycastu w dół, aby upewnić się, że żuk nie spadnie
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, detectionRange))
            {
                // Wyznaczanie kierunku wspinaczki
                Vector3 climbDirection = (target.position - transform.position).normalized;
                climbDirection.x = 0; // Poruszanie się tylko w osi Y
                climbDirection.z = 0;

                // Poruszanie żuka wzdłuż powierzchni słonecznika
                transform.position += climbDirection * speed * Time.deltaTime;

                // Obrót żuka w kierunku wspinaczki
                Vector3 directionToTarget = target.position - transform.position;
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);

                // Sprawdzanie, czy żuk dotarł na DEF-HEAD
                if (Vector3.Distance(transform.position, target.position) < 0.1f)
                {
                    Debug.Log("Żuk dotarł na DEF-HEAD!");
                    isClimbing = false; // Zatrzymanie wspinaczki
                }
            }

            yield return null; // Poczekaj na następną klatkę
        }
    }
}
