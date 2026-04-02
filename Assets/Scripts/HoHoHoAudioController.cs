using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HoHoHoAudioController : MonoBehaviour
{
    public new AudioSource audio;
    public float delay = 10f;
    public float offset = 0f;
    
    void Start()
    {
        StartCoroutine(PlayWithDelay());
    }

    IEnumerator PlayWithDelay()
    {
        yield return new WaitForSeconds(offset);
        while (true)
        {
            audio.Play();
            yield return new WaitForSeconds(delay);
        }
    }
}
