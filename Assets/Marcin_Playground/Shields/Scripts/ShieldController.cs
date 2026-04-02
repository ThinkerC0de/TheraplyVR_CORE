using System;
using System.Collections;
using System.Collections.Generic;
//using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Random = UnityEngine.Random;

public class ShieldController : MonoBehaviour
{
    public static ShieldController Instance;
    [SerializeField] private GameObject aviaryDoor;
    public bool hideDoors = false;
    [SerializeField] private float x = 0;
    [SerializeField] private float y = 0;
    [SerializeField] private float z = 0;
    [SerializeField] private float doorAnimationTime = 0.5f;
    [SerializeField] private float distanceToOpenDoor = 0.5f;
    [SerializeField] private float distanceToCollectButterfly = 0.2f;
    [SerializeField] private float duration = 1f;
    private float time;
    private float distance;
    private bool isOpening = false;
    private bool isClosing = false;
    private bool isOpened = false;
    public AudioClip sound;
    private AudioSource audioSource;
    private MagicStickPoint actualStick;
    
    Renderer _renderer;
    [SerializeField] AnimationCurve _DisplacementCurve;
    [SerializeField] float _DisplacementMagnitude;
    [SerializeField] float _LerpSpeed;
    [SerializeField] float _DisolveSpeed;
    bool _shieldOn;
    Coroutine _disolveCoroutine;

    // Start is called before the first frame update
    void Start()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }

        if (!audioSource) gameObject.AddComponent<AudioSource>();
        audioSource = GetComponent<AudioSource>();
        _renderer = GetComponent<Renderer>();
    }

    
    // Update is called once per frame
    void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.O))
        {
            StartCoroutine(RotateDoor(true, doorAnimationTime));
        }
        else if (Input.GetKeyDown(KeyCode.C))
        {
            StartCoroutine(RotateDoor(false, doorAnimationTime));
        }
*/
        
        /*
        if (GetDistance())
        {
            if (isOpening)
            {
                //
            }
            else
            {
                if (!isOpened)
                {
                    if (!hideDoors)
                    {
                        OpenCloseShield();
                        //StartCoroutine(RotateDoor(true, doorAnimationTime));`
                    }
                    else
                    {
                        aviaryDoor.SetActive(false);
                    }
                }
                else
                {
                    /*
                    if (MagicStickPoint.Instance.butterfly)
                    {
                        foreach (var stick in magicSticks)
                        {
                            if (Vector3.Distance(stick.transform.position, transform.position) <=
                                distanceToCollectButterfly)
                                StartCoroutine(CollectButterflyCoroutine(MagicStickPoint.Instance.butterfly));
                        }
                    }
                    */
                }
            /*}
        }
        else
        {
            if (isClosing)
            {
                //
            }
            else
            {
                if (isOpened)
                {
                    if (!hideDoors)
                        OpenCloseShield();
                    //StartCoroutine(RotateDoor(false, doorAnimationTime));
                    else
                    {
                        aviaryDoor.SetActive(true);
                    }
                }
            }
        }
    }*/

    bool GetDistance()
    {
        bool temp = false;
        foreach (var stickObj in SceneManager_Butterflies.Instance.magicSticks)
        {
            if (temp == false)
            {
                if (stickObj)
                {
                    MagicStickPoint stick = stickObj.transform.GetChild(0).GetComponent<MagicStickPoint>();
                    if (!stick) break;
                    distance = Vector3.Distance(stick.transform.position, transform.position);
                    if (distance <= distanceToOpenDoor)
                    {
                        temp = true;
                    }
                    else
                    {
                        temp = false;
                    }
                }
            }
        }

        return temp;
    }

    IEnumerator RotateDoor(bool openDoor, float duration)
    {
        Quaternion endValue;
        float time = 0;
        Quaternion startValue = aviaryDoor.transform.rotation;
        if (openDoor)
        {
            GameObject stick = null;
            foreach (var stickObj in SceneManager_Butterflies.Instance.magicSticks)
            {
                if (stickObj == null) yield break;
                if (stickObj.transform.GetChild(0).gameObject.GetComponent<MagicStickPoint>().haveButterfly)
                    stick = stickObj.transform.GetChild(0).gameObject;
            }

            if (stick == null) yield break;

            if (!stick.GetComponent<MagicStickPoint>().haveButterfly) yield break;
            actualStick = stick.GetComponent<MagicStickPoint>();
            isOpening = true;
            endValue = Quaternion.Euler(x, y, z);
            while (time < duration)
            {
                aviaryDoor.transform.rotation = Quaternion.Lerp(startValue, endValue, time / duration);
                time += Time.deltaTime;
                yield return null;
            }

            isOpening = false;
            isOpened = true;
            aviaryDoor.transform.rotation = endValue;

        }
        else
        {
            isClosing = true;
            endValue = Quaternion.Euler(0, 0, 0);
            while (time < duration)
            {
                aviaryDoor.transform.rotation = Quaternion.Lerp(startValue, endValue, time / duration);
                time += Time.deltaTime;
                yield return null;
            }

            isClosing = false;
            isOpened = false;
            aviaryDoor.transform.rotation = endValue;
        }
    }

    IEnumerator CollectButterflyCoroutine(GameObject butterfly, GameObject stick)
    {
        yield return null;

        if (butterfly == null) yield break;
        actualStick = stick.GetComponent<MagicStickPoint>();
        butterfly.transform.parent = null;
        Vector3 startPosition = butterfly.transform.position;
        Vector3 offset = new Vector3(0, 0.2f, 0);
        /*      
              while (Vector3.Distance(transform.position + offset, butterfly.transform.position) > .1f)
              {
                  sitPos = transform.position + offset;
                  butterfly.transform.position = Vector3.Lerp(startPosition, sitPos, time / duration);
                  time += Time.deltaTime;
                  yield return null;
              }
      */
        butterfly.transform.position = transform.position + offset;

        butterfly.GetComponent<Rigidbody>().isKinematic = false;

        if (sound)
        {
            audioSource.clip = sound;
            audioSource.Play();
            var b = butterfly.GetComponent<ButterflyController>();
            if (b.isCollected == false)
            {
                SceneManager_Butterflies.Instance.AddButterfly();
                b.isCollected = true;
                actualStick.ScaleDownOrb(0.5f);
            }
        }

        ButterflyController bc = butterfly.GetComponent<ButterflyController>();
        bc.volume = transform.GetComponent<BoxCollider>();
        bc.points[0].position = bc.GetPointInVolume(); //bc.transform.position;
        bc.points[1].position = bc.GetPointInVolume();
        bc.points[2].position = bc.GetPointInVolume();
        bc.points[3].position = bc.GetPointInVolume();
        bc._splineFollower.autoStartPosition = true;
        bc._splineFollower.follow = true;
        bc.RegeneratePoints(0);
        bc.material.SetFloat("_PREDKOSC", Random.Range(10, 30));
        bc._splineComputer.RebuildImmediate();
        yield return null;
        bc._splineFollower.SetEnable(false);
        //bc.Restart();

        actualStick.closestButterfly.distance = 1000;
        actualStick.closestButterfly.butterfly = null;
        actualStick.haveButterfly = false;
        actualStick.butterfly = null;

        yield return null;
        bc._splineFollower.SetEnable(true);

        bc.GetComponent<BoxCollider>().enabled = false;
    }

    public void CollectButterfly(GameObject butterfly, GameObject stick)
    {
        if (butterfly != null)
            StartCoroutine(CollectButterflyCoroutine(butterfly, stick));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer == 11)
        {
            HitShield(other.transform.position);
        }
    }
   
    

    public void HitShield(Vector3 hitPos)
    {
        _renderer.material.SetVector("_HitPos", hitPos);
        StopAllCoroutines();
        StartCoroutine(Coroutine_HitDisplacement());
    }
    
    public void OpenCloseShield()
    {
        float target = 1;
        if (_shieldOn)
        {
            target = 0;
        }
        _shieldOn = !_shieldOn;
        if (_disolveCoroutine != null)
        {
            StopCoroutine(_disolveCoroutine);
        }
        _disolveCoroutine = StartCoroutine(Coroutine_DisolveShield(target));
    }

    public void OpenShield()
    {
        float target = 1;
        _shieldOn = false;
        if (_disolveCoroutine != null)
        {
            StopCoroutine(_disolveCoroutine);
        }
        _disolveCoroutine = StartCoroutine(Coroutine_DisolveShield(target));
    }
    
    public void CloseShield()
    {
        float target = 0;
        _shieldOn = true;
        if (_disolveCoroutine != null)
        {
            StopCoroutine(_disolveCoroutine);
        }
        _disolveCoroutine = StartCoroutine(Coroutine_DisolveShield(target));
    }

    IEnumerator Coroutine_HitDisplacement()
    {
        float lerp = 0;
        while (lerp < 1)
        {
            _renderer.material.SetFloat("_DisplacementStrength", _DisplacementCurve.Evaluate(lerp) * _DisplacementMagnitude);
            lerp += Time.deltaTime*_LerpSpeed;
            yield return null;
        }
    }

    IEnumerator Coroutine_DisolveShield(float target)
    {
        float start = _renderer.material.GetFloat("_Disolve");
        float lerp = 0;
        while (lerp < 1)
        {
            _renderer.material.SetFloat("_Disolve", Mathf.Lerp(start,target,lerp));
            lerp += Time.deltaTime * _DisolveSpeed;
            yield return null;
        }
    }
}