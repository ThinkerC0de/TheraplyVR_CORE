using System.Collections;
using System.Collections.Generic;
using System.Xml;
using DG.Tweening;
using Dreamteck.Splines;
using UnityEngine;

public class AviaryController : MonoBehaviour
{
    public static AviaryController Instance;
    [SerializeField] private GameObject field;
    [SerializeField] private GameObject aviaryDoor;
    public bool hideDoors = false;
    [SerializeField] private float x = 0;
    [SerializeField] private float y = 0;
    [SerializeField] private float z = 0;
    [SerializeField] private float doorAnimationTime = 0.5f;
    [SerializeField] private float distanceToOpenDoor = 0.5f;
    [SerializeField] private float distanceToCollectButterfly = 0.2f;
    [SerializeField] private float duration=1f;
    private float time;
    private float distance;
    private bool isOpening = false;
    private bool isClosing = false;
    private bool isOpened = false;
    public AudioClip sound;
    private AudioSource audioSource;
    private MagicStickPoint actualStick;
    public BoxCollider AviaryVolume;
    public Collider ButterflyTakeOverVolume;
    public StickTriggerVolume stickTriggerVolume;

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
                        //_shield.OpenShield();
                    StartCoroutine(RotateDoor(true, doorAnimationTime));
                    else
                    {
                        //aviaryDoor.SetActive(false);
                        //_shield.OpenShield();
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
            }
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
                    if(!hideDoors)
                        StartCoroutine(RotateDoor(false, doorAnimationTime));
                        //_shield.CloseShield();
                    else
                    {
                        //_shield.CloseShield();
                        //aviaryDoor.SetActive(true);
                    }
                }
            }
        }
    }

    public void CloseDoor()
    {
        StartCoroutine(RotateDoor(false, doorAnimationTime));
        field.SetActive(false);
    }
    
    public void OpenDoor()
    {
        StartCoroutine(RotateDoor(true, doorAnimationTime));
        field.SetActive(true);
    }
    
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
                
                transform.forward = transform.parent.right;
                
                
                while (time < duration)
                {
                    
                    aviaryDoor.transform.localRotation = Quaternion.Euler(new Vector3((time/duration)*x, 0 , 0));//Quaternion.Lerp(startValue, targetRotation, time / duration);
                    time += Time.deltaTime;
                    yield return null;
                }
                
                isOpening = false;
                isOpened = true;
                
            
        }
        else
        {
            isClosing = true;
            
            /*
            while (time < duration)
            {
                aviaryDoor.transform.rotation = Quaternion.Lerp(startValue, endValue, time / duration);
                time += Time.deltaTime;
                yield return null;
            }
            */

            var targetRotation = Quaternion.Euler(0,0,0);
            aviaryDoor.transform.DOLocalRotate(targetRotation.eulerAngles,duration);
            
            isClosing = false;
            isOpened = false;
            //aviaryDoor.transform.rotation = endValue;
        }
    }

    IEnumerator CollectButterflyCoroutine(GameObject butterfly, GameObject stick)
    {
        yield return null;
        
        if (butterfly==null) yield break;
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
        butterfly.transform.position = AviaryVolume.center;
        butterfly.GetComponent<Rigidbody>().isKinematic = false;
        stickTriggerVolume.canCloseAviary = false;
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
        bc.volume = AviaryVolume;
        bc.points[0].position = bc.GetPointInVolume();//bc.transform.position;
        bc.points[1].position = bc.GetPointInVolume();
        bc.points[2].position = bc.GetPointInVolume();
        bc.points[3].position = bc.GetPointInVolume();
        bc._splineFollower.autoStartPosition = true;
        bc.isInAviary = true;
        bc.RegeneratePoints(0);
        bc.material.SetFloat("_PREDKOSC", Random.Range(10, 30));
        bc._splineComputer.RebuildImmediate();
        yield return null;
        bc._splineFollower.SetEnable(false);
        bc.Restart();
        bc.holdingStick = null;
        bc._splineFollower.follow = true;
        actualStick.closestButterfly.distance = 1000;
        actualStick.closestButterfly.butterfly = null;
        actualStick.haveButterfly = false;
        actualStick.butterfly = null;
        
        yield return null;
        bc._splineFollower.SetEnable(true);

        bc.GetComponent<BoxCollider>().enabled = false;
        
        if (stickTriggerVolume.canCloseAviary)
            CloseDoor();
        
        SceneManager_Butterflies.Instance.SwapMagicStickActivity();
    }

    public void CollectButterfly(GameObject butterfly, GameObject stick)
    {
        if (butterfly!=null)
            StartCoroutine(CollectButterflyCoroutine(butterfly, stick));
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Butterfly")
        {
            Debug.Log("Trigger: " + other.gameObject.name);
            if (other.GetComponent<ButterflyController>() != null)
                if (other.GetComponent<ButterflyController>().holdingStick != null)
                    CollectButterfly(other.gameObject, other.GetComponent<ButterflyController>().holdingStick.gameObject);
        }
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 11)
        {
            Debug.Log(collision.gameObject.name);
        }
    }
}
