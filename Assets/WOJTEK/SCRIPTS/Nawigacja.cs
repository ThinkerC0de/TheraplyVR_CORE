using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using DG.Tweening;
using FIMSpace;
using MalbersAnimations.Controller;
// using Oculus.Interaction.Throw;
//using UnityEditor.Localization.Plugins.XLIFF.V12;
//using UnityEditor.Localization.Plugins.XLIFF.V20;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Primitives;

public class Nawigacja : MonoBehaviour
{

    public Transform cel;
    public Transform target;
    public NavMeshAgent agent;
    public NavMeshAgent navMeshAgent;
    public NavMeshAgent headTarget;
    public Animator animator;
    public RigBuilder rigBuilder;
    public Rig rig;
    public GameObject cube;

    ///

    [SerializeField] public MultiAimConstraint _multiAimConstraint;
    [SerializeField] private GameObject lisek;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform root;


    /// 


    public float velocity;
    public bool isWalking = false;
    public bool isRunning = false;
    public bool isMoving = false;



    // Start is called before the first frame update
    void Start()
    {
        lisek = gameObject;
        agent = GetComponent<NavMeshAgent>();
        agent.destination = cel.position; // Ustawienie celu na początku
        animator = GetComponent<Animator>();
        rig = GetComponent<Rig>();
    }


    public void GoToPoint(Transform target)
    {
        cel = target;
        //}    

        //void Update() 

        //{
        agent.destination = cel.position;
        animator.SetFloat("velocity", navMeshAgent.velocity.magnitude);
        velocity = navMeshAgent.velocity.magnitude;

        if (velocity >= 0.001f)
        {
            isWalking = true;

            if (velocity >= 1.0f)
            {
                isRunning = true;
                isWalking = false;
            }
            else
            {
                isRunning = false;
            }
        }

        else
        {
            isRunning = false;
            isWalking = false;
        }

        if (velocity > 0.0f)
        {
            isMoving = true;
        }
        else
        {
            isMoving = false;
        }



    }

    void OnEnable()
    {
        // Inicjalizacja cube i headTarget
        cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = lisek.transform.position + new Vector3(0, 0, 1);
        cube.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        headTarget = cube.AddComponent<NavMeshAgent>();
        headTarget.destination = cel.position;
        headTarget.acceleration = agent.acceleration;
        headTarget.radius = 0.1f;
        headTarget.stoppingDistance = .5f;
        Destroy(cube.GetComponent<MeshCollider>());
        Destroy(cube.GetComponent<MeshRenderer>());
        Destroy(cube.GetComponent<Rigidbody>());

        // Tworzenie WeightedTransform na podstawie cube
        WeightedTransform weightedCube = new WeightedTransform { transform = cube.transform, weight = 1f };

        // Pobranie istniejących źródeł obiektów z MultiAimConstraint
        var sourceObjects = _multiAimConstraint.data.sourceObjects;

        // Dodanie cube jako źródło obiektu, jeśli jeszcze nie istnieje
        if (!sourceObjects.Contains(weightedCube))
        {
            sourceObjects.Add(weightedCube);
            _multiAimConstraint.data.sourceObjects = sourceObjects;
        }

        if (cube != null && Vector3.Distance(cube.transform.position, headTarget.destination) < 0.1f)
        {
            // Usunięcie cube, jeśli dotarł do celu
            Destroy(cube);
            Destroy(cube.GetComponent<NavMeshAgent>());

            Debug.Log("Cube dotarł do celu.");


            // Usunięcie cube z sourceObjects w MultiAimConstraint
            sourceObjects.Remove(weightedCube);
        }

    }

    private void OnDisable()
    {
        if (cube != null)
        {
            Destroy(cube);
        }
    }

}


