using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SparksController : MonoBehaviour
{
    [SerializeField] private ParticleSystem _particles;
    [SerializeField] private float _maxSize = 0.2f;
    private float _currentSize = 0.0f;
    private bool _updateParticle = false;
    private Coroutine _particleCoroutine;
    
    public void Restart()
    {
        _particles.Stop();
        _updateParticle = false;
        StopCoroutine(_particleCoroutine);
        var mainRestart = _particles.main;
        mainRestart.startSize = 0;
        StartParticles();
    }

    public void StartParticles()
    {
        _particles.Play();
        _updateParticle = true;
        _particleCoroutine = StartCoroutine(ParticleCoroutine());
    }

    IEnumerator ParticleCoroutine()
    {
        var main = _particles.main;
        while (_updateParticle)
        {
            float t = 0.0f;

            while (t < 3.0f)
            {
                main.startSize = Mathf.Lerp(0.0f, _maxSize, t / 3.0f);
                t += Time.deltaTime;
                yield return null;
            }

            main.startSize = _maxSize;

            yield return new WaitForSeconds(3);

            t = 0.0f;

            while (t < 6.0f)
            {
                main.startSize = Mathf.Lerp(_maxSize, 0.0f, t / 6.0f);
                t += Time.deltaTime;
                yield return null;
            }

            main.startSize = 0;
        }
        yield return null;
    }

    public void EndParticles()
    {
        Debug.Log("END PARTICLES");
        StartCoroutine(EndParticlesCoroutine());
    }

    IEnumerator EndParticlesCoroutine()
    {
        float t = 0.0f;
        _updateParticle = false;
        var particlesMain = _particles.main;
        var particlesMainStartSize = particlesMain.startSize;
        _currentSize = particlesMainStartSize.constant;
        Debug.Log("PRZED PARTICLEM");
        while (t < 5)
        {
            particlesMainStartSize.constant = Mathf.Lerp(_currentSize, 0.0f, t / 5);
            t += Time.deltaTime;
            yield return null;
        }
        Debug.Log("PO PARTICLEM");

        StopCoroutine(_particleCoroutine);
        _particles.Stop();
        gameObject.SetActive(false);
    }

    public void SetLifeTime(float lifetime)
    {
        var main = _particles.main;
        main.startLifetime = lifetime;
    }

    public void StopEmitParticles()
    {
        var emission = _particles.emission;
        emission.enabled = false;
    }
}
