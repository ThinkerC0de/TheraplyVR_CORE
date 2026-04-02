using System.Collections;
using UnityEngine;

public class CampfireConroller : MonoBehaviour
{
    public GameObject lightsParent;

    public GameObject campfireParent;
    
    public FireController[] fireController;

    public float minLightIntensity = 1.0f;
    public float maxLightIntensity = 1.6f;

    public float min_t = 0.1f;
    public float max_t = 1.0f;

    private bool isBurning = false;

    private Coroutine _coroutine;

    
    /*
    public void LightUp()
    {
        _coroutine = StartCoroutine(LightUpCoroutine());
    }

    IEnumerator LightUpCoroutine()
    {
        
    }
    
    public void LightDown()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(LightDownCoroutine());
        }
    }
    
    IEnumerator LightDownCoroutine()
    {
        
    }

    public void BreathIn()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(BreathInCoroutine());
        }
    }
    
    IEnumerator BreathInCoroutine()
    {
        
    }

    public void BreathOut()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(BreathOutCoroutine());
        }
    }
    
    IEnumerator BreathOutCoroutine()
    {
        
    }
    
    
    
    */
    
    
    
    
    
    
    private void Start()
    {
        //isBurning = true;
        //StartCoroutine(Burn());
    }

    IEnumerator Burn()
    {
        if (isBurning)
        {
            for (int i = 0; i < lightsParent.transform.childCount; i++)
            {
                StartCoroutine(LerpFlame(lightsParent.transform.GetChild(i).GetComponent<Light>()));
            }
        }

        yield return null;
    }

    IEnumerator LerpFlame(Light flame)
    {
        while (isBurning)
        {
            flame.intensity = UnityEngine.Random.Range(minLightIntensity, maxLightIntensity);

            yield return new WaitForSeconds(UnityEngine.Random.Range(min_t, max_t));
        }
    }


    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public void LightCampfire()
    {
        foreach (var fire in fireController)
        {
            fire.LightUpFlames();
        }
        for (int i = 0; i < lightsParent.transform.childCount; i++)
        {
            lightsParent.transform.GetChild(i).GetComponent<FireLightController>().LightUp();
        }

        StartCoroutine(LightCampfireCoroutine());
    }

    IEnumerator LightCampfireCoroutine()
    {
        Debug.Log("StartFire");
        var t = 0f;

        if (t < 1)
            for (int i = 0; i < lightsParent.transform.childCount; i++)
            {
                lightsParent.transform.GetChild(i).GetComponent<Light>().intensity = Mathf.Lerp(0, 1.65f, t);
                t += Time.deltaTime;
                yield return null;
            }
        gameObject.GetComponent<AudioSource>().Play();
    }

    public void ExtinguishFire()
    {
        StartCoroutine(ExtinguishFireCoroutine());
    }

    IEnumerator ExtinguishFireCoroutine()
    {
        Debug.Log("StopFire");
        foreach (var fire in fireController)
        {
            fire.ExtinguishFire();
        }

        for (int i = 0; i < lightsParent.transform.childCount; i++)
        {
            lightsParent.transform.GetChild(i).GetComponent<FireLightController>().ExtinguishFire();
        }

        /*var t = 0f;
        
        if (t < 10)
            for (int i = 0; i < lightsParent.transform.childCount; i++)
            {
                lightsParent.transform.GetChild(i).GetComponent<Light>().intensity = Mathf.Lerp(1.65f, 0, t);
                t += Time.deltaTime;
                yield return null;
            }
        */
        yield return null;

        gameObject.GetComponent<AudioSource>().Stop();
    }

    void StopImmediateFire()
    {
        foreach (var fire in fireController)
        {

        }
    }

    public void RestartFlame()
    {
        foreach (var fire in fireController)
        {
            fire.Restart();
        }
    }
}
