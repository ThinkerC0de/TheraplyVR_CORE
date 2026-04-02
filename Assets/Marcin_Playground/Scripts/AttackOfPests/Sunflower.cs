using UnityEngine;
using Dreamteck.Splines;
using System.Collections.Generic;



public class Sunflower : MonoBehaviour
{
    public Transform climbPoint;

    public List<GameObject> petals;

    public GameObject splinesRoot;

    private string currentState;

    void Update()
    {
        if (Input.GetKeyUp(KeyCode.Space))
        {
            RemovePetal();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        //Debug.Log(collision.gameObject.name);
        var nav = collision.gameObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
        nav.enabled = false;
        //collision.gameObject.transform.position = climbPoint.transform.position;
        GoUp(collision.gameObject.GetComponent<InsectController_Test>());
        //if (currentState == "Larwa_Otwarcie") return;
        //collision.gameObject.GetComponent<Animator>().CrossFade("SMIERC", 0.1f);
        //currentState = "Larwa_Otwarcie";
        //Debug.Log("Larwa_Otwarcie");
    }

    public void RemovePetal()
    {
        if (petals.Count == 0) return;
        int index = Random.Range(0, petals.Count);
        Debug.Log(index);
        petals[index].SetActive(false);
        petals.RemoveAt(index);
    }

    SplineComputer GetSpline()
    {
        if (splinesRoot.transform.childCount > 0)
            return splinesRoot.transform.GetChild(Random.Range(0, splinesRoot.transform.childCount - 1)).GetComponent<SplineComputer>();
        else return null;
    }

    void GoUp(InsectController_Test insect)
    {
        //if (spline == null) return;

        insect.spline = GetSpline();
        insect.spline.SetPointPosition(0, FindIntersectionPoint(transform.position, insect.transform.position, 0.1f));
        insect.Climb();
    }

    public Vector3 FindIntersectionPoint(Vector3 A, Vector3 B, float r)
    {
        float dx = B.x - A.x;
        float dz = B.z - A.z;
        float distanceSquared = dx * dx + dz * dz;

        if (distanceSquared == 0)
        {
            Debug.LogError("Punkty A i B są takie same, nie można wyznaczyć punktu C.");
            return Vector3.zero; // Możesz zwrócić inny sensowny wynik w przypadku błędu, np. Vector3.positiveInfinity
        }

        float t = Mathf.Sqrt(r * r / distanceSquared);

        if (t < 0 || t > 1)
        {
            Debug.LogError("Punkt przecięcia nie znajduje się na odcinku AB.");
            return Vector3.zero; // Możesz zwrócić inny sensowny wynik w przypadku błędu, np. Vector3.positiveInfinity
        }

        float xC = A.x + t * dx;
        float zC = A.z + t * dz;

        return new Vector3(xC, 0, zC);
    }
}