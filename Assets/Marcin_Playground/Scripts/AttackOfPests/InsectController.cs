using UnityEngine;
using UnityEngine.AI;

public class InsectController : MonoBehaviour
{
    private enum InsectState { Larva, Transforming, Beetle, Climbing }
    private InsectState currentState;

    private Animator animator;
    private NavMeshAgent agent;
    private Transform target;

    public GameObject beetleModel;
    public GameObject larvaModel;

    private float transformTime;

    void Start()
    {
        animator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();

        currentState = InsectState.Larva;
        transformTime = Random.Range(0.5f, 3f);
        Invoke("StartTransformation", transformTime);
    }

    void TryUpdate()
    {
        switch (currentState)
        {
            case InsectState.Larva:
                // Zachowanie larwy (np. idle lub poruszanie się losowo pod ziemią)
                break;

            case InsectState.Transforming:
                // Animacja transformacji
                break;

            case InsectState.Beetle:
                // Ruch żuka w kierunku słonecznika
                if (agent.remainingDistance <= agent.stoppingDistance)
                {
                    StartClimbing();
                }
                break;

            case InsectState.Climbing:
                // Logika wspinania się
                ClimbSunflower();
                break;
        }
    }

    void StartTransformation()
    {
        currentState = InsectState.Transforming;
        //animator.SetTrigger("Transform");
        // Po zakończeniu animacji transformacji wywołaj metodę OnTransformationComplete()
    }

    void OnTransformationComplete()
    {
        // Zmień model z larwy na żuka
        larvaModel.SetActive(false);
        beetleModel.SetActive(true);

        currentState = InsectState.Beetle;
        FindNearestSunflower();
    }

    void FindNearestSunflower()
    {
        GameObject[] sunflowers = GameObject.FindGameObjectsWithTag("Sunflower");
        float shortestDistance = Mathf.Infinity;
        GameObject nearestSunflower = null;

        foreach (GameObject sunflower in sunflowers)
        {
            float distanceToSunflower = Vector3.Distance(transform.position, sunflower.transform.position);
            if (distanceToSunflower < shortestDistance)
            {
                shortestDistance = distanceToSunflower;
                nearestSunflower = sunflower;
            }
        }

        if (nearestSunflower != null)
        {
            target = nearestSunflower.GetComponent<Sunflower>().climbPoint;
            agent.SetDestination(target.position);
        }
    }

    void StartClimbing()
    {
        currentState = InsectState.Climbing;
        agent.enabled = false; // Wyłącz NavMeshAgent podczas wspinania
        // Przygotuj animację wspinania
        animator.SetTrigger("Climb");
    }

    void ClimbSunflower()
    {
        float climbSpeed = 1.0f;
        // Przesuwaj żuka w kierunku punktu wspinania
        transform.position = Vector3.MoveTowards(transform.position, target.position, Time.deltaTime * climbSpeed);

        // Opcjonalnie: Użyj IK dla nóg
    }
}
