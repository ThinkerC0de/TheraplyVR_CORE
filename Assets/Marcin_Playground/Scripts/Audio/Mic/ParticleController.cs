using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticleController : MonoBehaviour
{
    private ParticleSystem particle;
    private ParticleSystem.Burst particleBurst;

    // Start is called before the first frame update
    void Start()
    {
        particle = GetComponent<ParticleSystem>();
        particle.Play();
        particleBurst = particle.emission.GetBurst(0);
    }

    public void StartBurst()
    {
        particleBurst.count = 1;
        particle.emission.SetBurst(0,particleBurst);
    }

    public void StopBurst()
    {
        particleBurst.count = 0;
        particle.emission.SetBurst(0,particleBurst);
    }
}
