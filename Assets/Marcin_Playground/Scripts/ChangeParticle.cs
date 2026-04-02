using UnityEngine;

public class ChangeParticle : MonoBehaviour
{
    public new ParticleSystem particleSystem;
    public GameObject mainObject;
    public GameObject subObject;

    private void Start()
    {
        subObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
            ToggleObjects();
    }

    public void ToggleObjects()
    {
        particleSystem.Play();
        mainObject.SetActive(!mainObject.activeInHierarchy);
        subObject.SetActive(!mainObject.activeInHierarchy);
    }
}
