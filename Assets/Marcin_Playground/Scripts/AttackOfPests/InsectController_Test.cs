using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Dreamteck.Splines;


public class InsectController_Test
 : MonoBehaviour
{
    public AudioClip hittedAudioClip;
    public GameObject hittedVFX;
    public GameObject[] sunflowers;

    public SplineComputer spline;
    private UnityEngine.AI.NavMeshAgent agent;

    public Transform closest;
    private Transform oldClosest;

    private AudioSource _audioSource;

    SplineFollower follower;

    Sunflower sunflower;

    public Animator animator;
    float velocity = 0.0f;
    int VelocityHash;

    public bool canWalk = true;
    // Start is called before the first frame update
    void OnEnable()
    {
        follower = GetComponent<SplineFollower>();
        follower.onEndReached += OnEndReached;
        agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        GetClosestPoint();
        agent.SetDestination(closest.position);
        agent.updateRotation = true;
        animator = GetComponent<Animator>();
        VelocityHash = Animator.StringToHash("Velocity");
        _audioSource = this.GetComponent<AudioSource>();
        _audioSource.clip = hittedAudioClip;
    }

    void GetClosestPoint()
    {
        sunflowers = GetSunflowers();

        /*
        float closestDistance = Mathf.Infinity;

        

        foreach (Sunflower sunflower in sunflowers)
        {
            float distance = Vector3.Distance(transform.position, sunflower.transform.position);
            Debug.Log("Distance = " + distance + " closestDistance = " + closestDistance);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = sunflower.transform;
            }
        }
        if (closest != oldClosest)
            oldClosest = closest;
            */

        List<float> distances = new List<float>();
        for (int i = 0; i < sunflowers.Length; i++)
        {
            distances.Add(Vector3.Distance(transform.position, sunflowers[i].transform.position));
        }
        int minIndex = distances.IndexOf(distances.Min());
        //Debug.Log(distances.Min());
        closest = sunflowers[minIndex].transform;
        sunflower = sunflowers[minIndex].GetComponent<Sunflower>();
    }

    IEnumerator GoToClosestSunflower()
    {
        while (canWalk)
        {
            GetClosestPoint();
            yield return new WaitForSeconds(3);
        }
    }

    GameObject[] GetSunflowers()
    {
        return GameObject.FindGameObjectsWithTag("Sunflower");//GameObject.FindObjectsOfType<Sunflower>();
    }

    /*void OnCollisionEnter(Collision collision)
    {
        GetClosestPoint();
        agent.SetDestination(closest.position);
    }*/


    public void Climb()
    {
        if (spline == null) return;

        follower.spline = spline;
        follower.followSpeed = Random.Range(0.025f, 0.1f);
        follower.follow = true;

        InsectSpawner.Instance.score--;
        InsectSpawner.Instance.UpdateScore();

    }

    void OnEndReached(double result)
    {
        Destroy(this.gameObject);
        sunflower.RemovePetal();
    }

    void Update()
    {

        if (Input.GetMouseButtonDown(0))
        {
            Touch();
        }

        if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)
        {
            Vector3 directionToTarget = agent.steeringTarget - transform.position;
            directionToTarget.y = 0; // Ignoruj oś Y
            if (directionToTarget != Vector3.zero)
            {
                Quaternion lookRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
            }

            velocity = agent.velocity.magnitude * 10;

            animator.SetFloat(VelocityHash, velocity);

        }
    }

    public void ChangeToBeetle()
    {

    }

    void Touch()
    {

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            /*
            if (_controller != null)
            {
                if (hit.collider.gameObject == gameObject)
                    _controller.OnTouch();
            }
            else
            {
                Debug.Log("The object hit does not have a SunflowerController component.");
            }
            */
            //Debug.Log(hit.collider.gameObject.name);
            //Debug.Log(this.gameObject.name);


            if (hit.collider.gameObject == this.gameObject)
            {
                StartCoroutine(DestroyInsect());
            }
        }
        else
        {
            Debug.Log("Raycast did not hit any object.");
        }
    }

    IEnumerator DestroyInsect()
    {
        _audioSource.Play();
        canWalk = false;
        GetComponent<BoxCollider>().enabled = false;
        hittedVFX.SetActive(true);
        while (_audioSource.isPlaying)
            yield return null;
        Destroy(this.gameObject);
        InsectSpawner.Instance.score++;
        InsectSpawner.Instance.UpdateScore();
    }
}
