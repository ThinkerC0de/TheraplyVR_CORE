using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MicTrigger : MonoBehaviour
{
    public AudioLoudnessDetection detector;

    public float loudnessSensibility = 100;

    public float threshold = 0.1f;

    public float loudness;

    public UnityEvent triggerOn;

    public UnityEvent triggerOff;

    public GameObject bullet;
    public GameObject spawnDummy;

    public bool singleFire = true;
    
    public float force = 1;

    private bool holdingNearFace = false;
    private bool pauseFromSpawn = false;

    void Update()
    {
        loudness = detector.GetLoudnessFromMicrophone() * loudnessSensibility;
        if (loudness < threshold)
        {
            loudness = 0;
        }

        if (loudness == 0)
        {
            triggerOff?.Invoke();
        }
        else
        {
            if (holdingNearFace)
            {
                triggerOn?.Invoke();
            }
            else
            {
                triggerOff?.Invoke();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Collision Enter With: " + other.gameObject.name + " on layer: " + other.gameObject.layer);
        if (other.gameObject == this.transform.parent)
        {
            return;
        }
        if (other.gameObject.layer == 25)
        {
            holdingNearFace = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log("Collision Exit With: " + other.gameObject.name + " on layer: " + other.gameObject.layer);
        if (other.gameObject == this.transform.parent)
        {
            return;
        }
        if (other.gameObject.layer == 25)
        {
            holdingNearFace = false;
        }
    }
    
    public void SpawnBullet()
    {
        if (!pauseFromSpawn)
            StartCoroutine(SpawnBulletCoroutine());
    }

    IEnumerator SpawnBulletCoroutine()
    {
        pauseFromSpawn = true;
        var bullet = Instantiate(this.bullet, spawnDummy.transform.position, Quaternion.identity);
        bullet.GetComponent<Rigidbody>().AddForce(Camera.main.transform.forward * force);
        if (singleFire)
            yield return new WaitForSeconds(1);
        pauseFromSpawn = false;
    }

    public void SingleFireOn()
    {
        singleFire = true;
    }
    
    public void SingleFireOff()
    {
        singleFire = false;
    }
}
