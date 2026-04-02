using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class InsectSpawner : MonoBehaviour
{
    public static InsectSpawner Instance;
    public GameObject insectPrefab;
    public int insectsCount = 1;

    public GameObject spawnPlane;
    private Bounds spawnBounds;

    int counter = 0;

    public float avoidRadius = 0.3f;

    GameObject[] sunflowers;

    public TMP_Text text;

    public int score = 0;


    void Awake()
    {
        if (Instance == null) Instance = this;
        else
            Destroy(this.gameObject);
    }

    void Start()
    {
        sunflowers = GameObject.FindGameObjectsWithTag("Sunflower");
        spawnBounds = spawnPlane.GetComponent<Collider>().bounds;
        StartCoroutine(SpawnLarvae());
    }

    IEnumerator SpawnLarvae()
    {
        while (true)
        {
            SpawnLarva();

            yield return new WaitForSeconds(0.5f);
            if (insectsCount == 0) yield break;
        }
    }

    void SpawnLarva()
    {
        StartCoroutine(GetSpawnPosition());
    }

    bool IsPositionClear(Vector3 newPosition, GameObject[] existingObjects, float radius = 0.3f)
    {
        foreach (GameObject obj in existingObjects)
        {
            float distance = Vector3.Distance(newPosition, obj.transform.position);
            if (distance < radius)
            {
                // Nowy obiekt znajduje się w zasięgu promienia innego obiektu
                return false;
            }
        }
        // Nowy obiekt nie koliduje z innymi obiektami
        return true;
    }

    IEnumerator GetSpawnPosition()
    {
        Vector3 spawnPosition = new Vector3(
                    Random.Range(spawnBounds.min.x, spawnBounds.max.x),
                    spawnPlane.transform.position.y,// + 0.02f,//spawnBounds.center.y,
                    Random.Range(spawnBounds.min.z, spawnBounds.max.z)
                );

        while (IsPositionClear(spawnPosition, sunflowers, avoidRadius) == false)
        {
            yield return null;
        }

        GameObject insect = Instantiate(insectPrefab, spawnPosition, Quaternion.identity);//Quaternion.Euler(0, Random.Range(0, 360), 0));
        //insect.transform.GetChild(0).GetComponent<Renderer>().material.color = new Color(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f));
        insect.name = "Insect_" + counter;
        counter++;
        insectsCount--;
    }

    public void UpdateScore()
    {
        if (score < 0)
        {
            text.text = score.ToString();
            text.color = Color.red;
        }

        if (score > 0)
        {
            text.text = score.ToString();
            text.color = Color.green;
        }
    }
}


