using UnityEngine;
using System.Collections;
using DG.Tweening;

public class PestSpawner : MonoBehaviour
{
    public GameObject redLarvaPrefab;
    public GameObject blueLarvaPrefab;
    public GameObject spawnZone;
    public int currentLevel;

    private float spawnInterval;
    private int larvaePerSpawn;
    private Bounds spawnBounds;

    GameObject larva;
    public Transform[] targetPoints;
    void Start()
    {
        spawnBounds = spawnZone.GetComponent<Collider>().bounds;

        ConfigureLevelParameters();
        StartCoroutine(SpawnLarvae());
    }

    void ConfigureLevelParameters()
    {
        if (currentLevel <= 4)
        {
            spawnInterval = 5f;
            larvaePerSpawn = 1;
        }
        else if (currentLevel <= 7)
        {
            spawnInterval = 3f;
            larvaePerSpawn = 2;
        }
        else
        {
            spawnInterval = 2f;
            larvaePerSpawn = 3;
        }
    }

    IEnumerator SpawnLarvae()
    {
        while (true)
        {
            for (int i = 0; i < larvaePerSpawn; i++)
            {
                SpawnLarva();
            }

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnLarva()
    {
        Vector3 spawnPosition = new Vector3(
            Random.Range(spawnBounds.min.x, spawnBounds.max.x),
            spawnBounds.min.y, // - 0.1f,
        Random.Range(spawnBounds.min.z, spawnBounds.max.z)
        );


        Transform closestPoint = GetClosestPoint(spawnPosition);

        GameObject larvaPrefab = redLarvaPrefab;

        if (currentLevel >= 6)
        {
            float blueLarvaChance = 0.3f;
            larvaPrefab = (Random.value < blueLarvaChance) ? blueLarvaPrefab : redLarvaPrefab;
        }


        larva = Instantiate(larvaPrefab, spawnPosition, Quaternion.identity);

        float spawnHeight = spawnBounds.max.y - spawnZone.GetComponent<Collider>().bounds.size.y;

        larva.transform.DOMoveY(spawnHeight + 0.1f, 1.5f);
        //larva.transform.DORotate(new Vector3(0, 360, 0), 2.5f).OnComplete(() => { Debug.Log("tera"); });


        //larva.transform.DOMove(closestPoint.position, 2f);
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

    void OnTriggerExit(Collider other)
    {
        if (DOTween.IsTweening(other.gameObject.transform))
            larva.transform.DOKill();

        larva.transform.DORotate(new Vector3(-90, 0, 0), 1f).OnComplete(() =>
        {
            larva.GetComponent<LarvaController>().closestPoint = GetClosestPoint(larva.transform.position);

            larva.GetComponent<LarvaController>().MoveToClosestPoint();
        }
        );
    }
}
