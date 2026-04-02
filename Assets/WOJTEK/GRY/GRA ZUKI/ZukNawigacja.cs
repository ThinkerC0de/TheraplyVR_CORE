using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ZukNawigacja : MonoBehaviour
{
    
    public Transform cel;
    public NavMeshAgent agent;
    public Animator animator;
    public bool isMoving = false;

    public float velocity = 0;
    //public float rotationSpeed = 5f; // Szybkość obrotu postaci
  

    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.destination = cel.position; // Ustawienie celu na początku
        animator = GetComponent<Animator>();
    }


    public void GoToPoint(Transform target)
    {
        cel = target;

        agent.destination = cel.position;

        if (agent.velocity.magnitude > 0.0f)
        {
            isMoving = true;
        }

     
        
        /*
        // Sprawdź, czy agent zakończył ruch (dystans mniejszy niż mała wartość)
        if (agent.remainingDistance < 0.01f)
        {
            if (!isMoving) // Sprawdzamy czy agent zaczął już ruch
            {
                isMoving = true; // Ustawiamy flagę na true, oznaczając, że agent zaczął ruch

                // Oblicz kierunek do celu
                Vector3 targetDirection = target.position - transform.position;
                targetDirection.y = 0f; // Zablokuj obrót w osi Y (nie obracaj agenta w górę ani w dół)

                // Obróć agenta w kierunku celu używając rotacji Eulerowych i interpolacji liniowej
                if (targetDirection != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
                    Vector3 eulerRotation = targetRotation.eulerAngles;
                    Quaternion desiredRotation = Quaternion.Euler(0f, eulerRotation.y, 0f);
                    transform.rotation = Quaternion.Lerp(transform.rotation, desiredRotation, Time.deltaTime * rotationSpeed);
                }

                // Ustaw parametr w animatorze na true, aby uruchomić animację
                animator.SetBool("Odlozenie", true);
                isMoving = false;
            }
        }
        else
        {
            isMoving = false; // Ustawiamy flagę na false, gdy agent nadal się porusza
            // Jeśli agent nadal się porusza, zresetuj parametr w animatorze
            animator.SetBool("Odlozenie", false);
        }
        */
    }

    void Update()
    {
           float velocity = agent.velocity.magnitude/agent.speed;
    }
    
}
