using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoundGameController : MonoBehaviour
{
    public List<GameObject> poles;
    public float startDelay = 2f;
    public float soundDelay = 2f;
    public AudioClip audioClip;
    
    // Start is called before the first frame update
    void Start()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            poles.Add(transform.GetChild(i).gameObject);
            //AddSound(poles[i]);
        }
        
        //StartDemo();
    }

    private void AddSound(GameObject go)
    {
        go.AddComponent<AudioSource>();
        AudioSource audioSource = go.GetComponent<AudioSource>();
        audioSource.clip = audioClip;
        audioSource.playOnAwake = false;
        audioSource.spatialize = true;
        audioSource.spatializePostEffects = true;
        audioSource.spatialBlend = 1.0f;
    }

    public void StartDemo()
    {
        StartCoroutine(StartDemoCoroutine());
    }

    IEnumerator StartDemoCoroutine()
    {
        yield return new WaitForSeconds(startDelay);

        for (int i = 0; i < poles.Count; i++)
        {
            //ColorizePole(poles[i].transform.GetChild(0).gameObject, Color.red);
            
            AudioSource _poleAudio = poles[i].transform.GetChild(0).gameObject.GetComponent<AudioSource>();
            _poleAudio.Play();
            yield return null;
            while (_poleAudio.isPlaying)
            {
                yield return null;
            }
            
            //ColorizePole(poles[i].transform.GetChild(0).gameObject, Color.white);
            
            yield return new WaitForSeconds(soundDelay);
        }

        int nr = Random.Range(0, 11);
        while (true)
        {
            nr = Random.Range(0, 11); 
            
            //ColorizePole(poles[nr].transform.GetChild(0).gameObject, Color.red);
            
            AudioSource _poleAudio = poles[nr].transform.GetChild(0).gameObject.GetComponent<AudioSource>();
            _poleAudio.Play();
            yield return null;
            while (_poleAudio.isPlaying)
            {
                yield return null;
            }
            
            //ColorizePole(poles[nr].transform.GetChild(0).gameObject, Color.white);
            
            yield return new WaitForSeconds(soundDelay);
        }
    }

    void ColorizePole(GameObject go, Color newColor)
    {
        go.GetComponent<Renderer>().material.color = newColor;
    }
}
