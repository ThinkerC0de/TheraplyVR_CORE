using Autohand;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CubeGenerator : MonoBehaviour
{
    [SerializeField] private SpatialManager spatialManager;
    [SerializeField] private GameObject cubePrefab;
    [SerializeField] private Transform cubes;
    [SerializeField] private ParticleSystem particle;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private new Rigidbody rigidbody;
    [SerializeField] private TMP_Text number;
    [SerializeField] private Grabbable grabbable;
    [SerializeField] private bool generateCubes;
    [SerializeField] private int size;


    private List<Vector3> cubePositions = new List<Vector3>();
    private List<MaterialPulse> materialPulses = new List<MaterialPulse>();
    private int numberOfCubes;
    private bool colorMix = true;
    public float duration = 6f;

    private Vector3[] directions = {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

    void Update()
    {
        if (generateCubes)
        {
             numberOfCubes = RandomNumberOfCubes();
             GenerateCubes(numberOfCubes);
             generateCubes = false;
        }
    }

    // Randomly selects a number of cubes from 3 to 16
    private int RandomNumberOfCubes()
    {
        if (size > 2 && size < 17) return size;
        return Random.Range(3, 17);
    }

    // Generate solid from number of cubes
    public void GenerateCubes(int count)
    {
        Debug.Log("Generating a new solid of size " + count);
        numberOfCubes = count;
        ResettCubesPosition();

        List<Vector3> positions = new List<Vector3>();
        cubes.localRotation = Quaternion.Euler(0f, 0f, 0f);
        cubePositions.Clear();
        materialPulses.Clear();

        foreach (Transform child in cubes)
        {
            Destroy(child.gameObject);
        }

        // Set the maximum matrix size
        int maxX = (count <= 5) ? 2 : 3;
        int maxY = (count <= 10) ? 2 : 3;
        int maxZ = (count == 3) ? 1 : (count <= 14) ? 2 : 3;

        for (int x = 0; x < maxX; x++)
        {
            for (int y = 0; y < maxY; y++)
            {
                for (int z = 0; z < maxZ; z++)
                {
                    positions.Add(new Vector3(x, y, z));
                }
            }
        }

        // Add the first cube in a random position
        int randomIndex = Random.Range(0, positions.Count);
        Vector3 currentPosition = positions[randomIndex];
        Color color = RandomColor();
        GameObject newCube = Instantiate(cubePrefab, currentPosition, Quaternion.identity, cubes);
        newCube.transform.localPosition = currentPosition;
        newCube.GetComponent<Renderer>().material.color = color;
        materialPulses.Add(newCube.GetComponent<MaterialPulse>());
        cubePositions.Add(currentPosition);

        // Generating the remaining cubes
        for (int i = 1; i < count; i++)
        {
            Vector3 newPosition = Vector3.zero;
            bool positionFound = false;

            // Trying to find a free position
            for (int attempts = 0; attempts < 100; attempts++)
            {
                // Random direction
                Vector3 direction = directions[Random.Range(0, directions.Length)];
                newPosition = currentPosition + direction;

                // Is a new position free and fits into the matrix
                if (!cubePositions.Contains(newPosition) &&
                    newPosition.x >= 0 && newPosition.x < maxX &&
                    newPosition.y >= 0 && newPosition.y < maxY &&
                    newPosition.z >= 0 && newPosition.z < maxZ)
                {
                    positionFound = true;
                    break;
                }
            }

            if (positionFound)
            {
                if (colorMix) color = RandomColor();

                newCube = Instantiate(cubePrefab, newPosition, Quaternion.identity, cubes);
                newCube.transform.localPosition = newPosition;
                newCube.GetComponent<Renderer>().material.color = color;
                materialPulses.Add(newCube.GetComponent<MaterialPulse>());
                cubePositions.Add(newPosition);
                currentPosition = newPosition;
            }
            else
            {
                Debug.Log("Could not find a free position for the next cube");
                GenerateCubes(count);
                break;
            }
        }
        grabbable.Awake();

    }

    private Color RandomColor()
    {
        float r = Random.Range(0f, 1f);
        float g = Random.Range(0f, 1f);
        float b = Random.Range(0f, 1f);

        return new Color(r, g, b);
    }

    public void SwitchColorMix(bool state)
    {
        colorMix = state;
    }

    public void Freeze()
    {
        ResettCubesPosition();
        rigidbody.constraints = RigidbodyConstraints.FreezePosition;
    }

    public void Unfreeze()
    {
        rigidbody.constraints = RigidbodyConstraints.None;
    }

    public void StartPulsing()
    {
        foreach (MaterialPulse material in materialPulses)
        {
            material.StartPulsing();
        }
    }

    public void StopPulsing()
    {
        foreach (MaterialPulse material in materialPulses)
        {
            material.StopPulsing();
        }
    }

    public void ShowNumber(bool state)
    {
        if (state)
        {
            number.text = numberOfCubes.ToString();
        }

        number.gameObject.SetActive(state);
    }

    public IEnumerator RotateRandomly()
    {
        Vector3 randomAxis = Random.insideUnitSphere;
        float randomAngle = Random.Range(0f, 360f);

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            // Obliczanie k�ta obrotu na podstawie up�ywaj�cego czasu
            float angleThisFrame = randomAngle * (Time.deltaTime / duration);
            cubes.Rotate(randomAxis, angleThisFrame, Space.World);

            elapsedTime += Time.deltaTime;
            yield return null; // Czekaj na nast�pny frame
        }
    }

    public void ParticlePlay()
    {
        particle.Play();
        audioSource.Play();

        foreach (var item in materialPulses)
        {
            item.transform.gameObject.SetActive(false);
        }
    }

    public Vector3 GetCubesPosition()
    {
        return cubes.localPosition;
    }

    private void ResettCubesPosition()
    {
        cubes.localRotation = Quaternion.identity;
        cubes.localPosition = Vector3.zero;
    }
}
