using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class BalloonController : MonoBehaviour
{
    public float t = 3;
    public AudioClip sound;
    private AudioSource audioSource;
    private bool _hided = false;
    private string _targetInstanceId = string.Empty;
    private string _targetAppearedAtUtc = string.Empty;
    private float _targetAppearedAtElapsedSec = -1f;

    public string TargetInstanceId => _targetInstanceId;
    public string TargetAppearedAtUtc => _targetAppearedAtUtc;
    public float TargetAppearedAtElapsedSec => _targetAppearedAtElapsedSec;
    
    void Start()
    {
        if (!audioSource) gameObject.AddComponent<AudioSource>();
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        MarkAppearance();
    }
    public void ScareButterflies()
    {
        if (SceneManager_Butterflies.Instance.isScarred == true) return;
        
        UI_LifeCounter.Instance.SubstractLife();
        StartCoroutine("Scare");
    }

    IEnumerator Scare()
    {
        SceneManager_Butterflies.Instance.isScarred = true;
        _hided = true;
        
        if (sound)
        {
            audioSource.clip = sound;
            audioSource.pitch = Random.Range(1.0f, 2.0f);
            audioSource.Play();
        }
        GetComponent<MeshRenderer>().enabled = false;
        
        var butterflies = GameObject.FindObjectsByType<ButterflyController>(FindObjectsSortMode.None);
        foreach (var butterfly in butterflies)
        {
            //butterfly._splineFollower.followSpeed = 5;
            butterfly.Panic();
            /*
            foreach (var stick in GameObject.FindObjectsOfType<MagicStickPoint>())
            {
                if (stick.butterfly == null)
                {
                    //var bf = stick.butterfly.GetComponent<ButterflyController>();
                    //bf.isCatched = false;
                    //bf.holdingStick = null;
                    stick.butterfly.gameObject.transform.parent = null;
                    stick.butterfly = null;
                    stick.haveButterfly = false;
                    stick.ScaleDownOrb(0.5f);
                }
            }
            */
        }
        
        /*
        GameObject volume = GameObject.FindGameObjectWithTag("ButterflyVolume");
        var butterflies = GameObject.FindObjectsByType<ButterflyController>(FindObjectsSortMode.None);
        float time = 0;
        Vector3 offset = new Vector3(0, 2, 0);

        volume.transform.position += offset;
        
        foreach (var butterfly in butterflies)
        {
            //butterfly._splineFollower.followSpeed = 5;
            butterfly.Panic();
        }

        yield return new WaitForSeconds(t);

        volume.transform.position = SceneManager_Butterflies.Instance.butterflyVolumePosition;
        
        foreach (var butterfly in butterflies)
        {
            while (butterfly._splineFollower.followSpeed > 1f)
            {
                butterfly._splineFollower.followSpeed = Mathf.Lerp(5, 1, time/duration);
                time += Time.deltaTime;
                yield return null;
            }
            butterfly._splineFollower.followSpeed = 1;
        }
        
        //
        
        //gameObject.SetActive(false);
*/
        
        
        yield return new WaitForSeconds(t+1);
        
        MarkAppearance();
        gameObject.GetComponent<MeshRenderer>().enabled = true;
        _hided = false;
        SceneManager_Butterflies.Instance.isScarred = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("ballon Collision with " + collision.gameObject.name);
        if (_hided) return;
        if (collision.gameObject.layer == 11)
        {
            Debug.Log("Scare");
            ScareButterflies();
        }
    }

    private void OnEnable()
    {
        MarkAppearance();
        //GetComponent<Rigidbody>().isKinematic = false;
        //GetComponent<MeshCollider>().enabled = true;
    }

    private void MarkAppearance()
    {
        _targetInstanceId = LegacyInteractionTelemetry.CreateTargetInstanceId();
        _targetAppearedAtUtc = LegacyInteractionTelemetry.CurrentUtcIso();
        _targetAppearedAtElapsedSec = LegacyInteractionTelemetry.CurrentRealtimeSec();
    }
}
