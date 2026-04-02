using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class LarvaController : MonoBehaviour
{
    public Transform[] targetPoints; // Punkty docelowe
    public Transform closestPoint;

    // Start is called before the first frame update
    void Start()
    {
        closestPoint = GetClosestPoint(transform.position);
        //MoveToClosestPoint();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void MoveToClosestPoint()
    {
        if (closestPoint != null)
        {
            float moveDuration = 2f; // Czas ruchu
            transform.DOLookAt(closestPoint.position, moveDuration, AxisConstraint.None, Vector3.right);
            transform.DOMove(closestPoint.position, moveDuration).OnComplete(() =>
            {
                // logika po dotarciu do punktu
            });
        }
    }

    Transform GetClosestPoint(Vector3 position)
    {
        Transform closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (Transform point in targetPoints)
        {
            float distance = Vector3.Distance(position, point.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = point;
            }
        }

        return closest;
    }
}
