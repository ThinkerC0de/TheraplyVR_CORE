using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Dreamteck.Splines;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

//using UnityEditor.Localization.Plugins.XLIFF.V20;
using Random = UnityEngine.Random;

//[RequireComponent(typeof(BoxCollider))]
public class ButterflyController : MonoBehaviour
{
    public float tresholdDistance = 0.1f;
    public float speed = 5f;
    public bool randomColor = false;
    public bool randomAnimationSpeed = false;
    public BoxCollider volume;
    private Vector3 actualPoint;
    private Vector3 direction;
    private float movSpeed;
    private float rotSpeed;
    private bool stopFlying = false;
    public Material material;
    private Vector3 lastPosition;
    public bool isCatched = false;
    private float zOffset;
    private Vector3 sitPos;
    public SplineComputer _splineComputer;
    public SplineFollower _splineFollower;
    private GameObject sc;
    float distance;
    float val;
    public SplinePoint[] points = new SplinePoint[3];
    public bool changed = false;
    public stickColor color;
    public AudioClip soundOk;
    public AudioClip soundBad;
    private AudioSource audioSource;
    public bool isCollected = false;
    public bool isInAviary = false;
    public bool isTriggered = false;
    public MagicStickPoint holdingStick = null;
    public bool startSittingOnStick = false;
    private bool _gameplayPaused;
    private bool _colliderWasEnabled;
    private bool _splineFollowerWasEnabled;
    private bool _splineFollowerWasFollowing;
    private bool _rigidbodyWasKinematic;
    private Vector3 _pausedVelocity;
    private Vector3 _pausedAngularVelocity;
    private string _targetInstanceId = string.Empty;
    private string _targetAppearedAtUtc = string.Empty;
    private float _targetAppearedAtElapsedSec = -1f;

    // Start is called before the first frame update
    void Start()
    {
        volume = SceneManager_Butterflies.Instance.butterflyVolume;
        if (!audioSource) gameObject.AddComponent<AudioSource>();
        audioSource = GetComponent<AudioSource>();
        //_splineFollower = GetComponent<SplineFollower>();
        actualPoint = GetPointInVolume();
        material = transform.GetChild(0).GetComponent<MeshRenderer>().material;
        if (randomColor)
            material.SetColor("_KOLOR", Random.ColorHSV(0.0f, 1, 0.85f, 1, 1, 1, 1, 1));
        if (randomAnimationSpeed)
            material.SetFloat("_PREDKOSC", Random.Range(20, 30));
        color = stickColor.random;
        MarkAppearance();
    }

    private void OnEnable()
    {
        MarkAppearance();
    }

    public void Restart()
    {
        points[0].position = GetPointInVolume();
        points[1].position = GetPointInVolume();
        points[2].position = GetPointInVolume();
        points[3].position = GetPointInVolume();
        _splineComputer.SetPoints(points);
        _splineFollower.spline = _splineComputer;
    }

    public void RegeneratePoints(double d)
    {
        /*
        points[0].position = points[3].position;
        points[1].position = GetPointInVolume();
        points[2].position = GetPointInVolume();
        points[3].position = GetPointInVolume();
        
        _splineComputer.SetPoints(points);
        */
        //_splineFollower.spline = _splineComputer;
        

        StartCoroutine(RegeneratePointsCoroutine());
    }

    IEnumerator RegeneratePointsCoroutine()
    {
        _splineFollower.follow = false;
        points[0].position = transform.position;

        points[1].position = GetPointInVolume();

        points[2].position = GetPointInVolume();

        //points[3].position = GetPointInVolume();
        
        _splineComputer.SetPointPosition(3,transform.position);

        yield return null;

        _splineComputer.SetPoints(points);
        
        _splineComputer.RebuildImmediate();
        _splineFollower.Restart();
        _splineFollower.follow = true;
        _splineFollower.spline = _splineComputer;
    }

    // Update is called once per frame
    void Update()
    {
        
        
        //if (MagicStickPoint.Instance.haveButterfly && MagicStickPoint.Instance.butterfly != gameObject) return;
        /*
        var distance = Vector3.Distance(MagicStickPoint.Instance.gameObject.transform.position, this.transform.position);

        if (MagicStickPoint.Instance.closestButterfly.distance > distance)
        {
            MagicStickPoint.Instance.closestButterfly.butterfly = gameObject;
            MagicStickPoint.Instance.closestButterfly.distance = distance;
        }
        */
/*
        if (MagicStickPoint.Instance.closestButterfly.butterfly == gameObject)
        {
            if (distance < MagicStickPoint.Instance.actionDistance)
            {
                val = MagicStickPoint.Instance.actionDistance - distance;
                MagicStickPoint.Instance.gameObject.transform.localScale = new Vector3(val, val, val);
            }
            else
            {
                MagicStickPoint.Instance.gameObject.transform.localScale = Vector3.zero;
            }
        }

        if (isCatched) return;
        
        
        if (Vector3.Distance(transform.position, actualPoint) > tresholdDistance)
        {
            if (!stopFlying)
            {
                this.transform.position = Vector3.MoveTowards(this.transform.position, actualPoint, speed * Time.deltaTime);
                //if (lastPosition == transform.position)
                if (Vector3.Distance(lastPosition,transform.position) < tresholdDistance)
                    actualPoint = GetPointInVolume();
            }
        }
        else
        {
            actualPoint = GetPointInVolume();
            direction = (actualPoint - transform.position).normalized;
            this.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
        
        lastPosition = transform.position;
        

        if (Input.GetKeyDown(KeyCode.P))
        {
            Panic();
        }
        */
    }

    public void FlyAway()
    {
        this.transform.parent = null;
        GetComponent<Rigidbody>().isKinematic = false;
        //this.transform.GetChild(0).transform.localPosition = Vector3.zero;
        //this.transform.GetChild(0).transform.localRotation = Quaternion.Euler(0,0,0);
        this.transform.localPosition = Vector3.zero;
        this.transform.localRotation = Quaternion.Euler(0,0,0);
        this.transform.parent = null;
        stopFlying = false;
        _splineFollower.enabled = true;
        _splineFollower.SetEnable(true);
        
        points[0].position = transform.position;
        points[1].position = GetPointInVolume();
        points[2].position = GetPointInVolume();
        points[3].position = GetPointInVolume();
        if (_splineComputer)
        {
            _splineComputer.SetPoints(points);
            _splineComputer.RebuildImmediate();
        }

        if (_splineFollower)
        {
            _splineFollower.Restart();
            _splineFollower.follow = true;
        }
    }

    public void FlyAwayForGood()
    {
        volume = GameObject.Find("EndVolume").GetComponent<BoxCollider>();
    }

    public void FlyTo(GameObject obj)
    {
        StartCoroutine(FlyToCoroutine(obj));
    }

    IEnumerator FlyToCoroutine(GameObject obj)
    {
        stopFlying = true;
        GetComponent<Rigidbody>().isKinematic = true;
        transform.GetChild(0).GetComponent<MeshRenderer>().material.SetFloat("_PREDKOSC",2);
        _splineFollower.SetEnable(false);
        _splineFollower.follow = false;
        _splineFollower.enabled = false;
        Vector3 pos = Vector3.zero;

        if (obj.transform.rotation.z > 0)
        {
            pos = obj.GetComponent<AwardPosition>().top.position;
        }
        else
        {
            pos = obj.GetComponent<AwardPosition>().bottom.position;
        }
        
        /*
        while ((obj.transform.position - transform.position).magnitude > 0.01f)
        {
            float dystans = (pos - transform.position).magnitude;
            Vector3 kierunek = pos - transform.position;
            Vector3 nowaPozycja = transform.position + kierunek.normalized * Mathf.Min(1* Time.deltaTime, dystans);

            transform.position = nowaPozycja;

            yield return null;
        }
*/
        if (obj.transform.rotation.z > 0)
        {
            this.transform.position = obj.GetComponent<AwardPosition>().top.position;
            this.transform.parent = obj.GetComponent<AwardPosition>().top;
            yield return null;
            this.transform.localPosition = Vector3.zero;
            this.transform.localRotation = Quaternion.Euler(Vector3.zero);
        }
        else
        {
            this.transform.position = obj.GetComponent<AwardPosition>().bottom.position;
            this.transform.parent = obj.GetComponent<AwardPosition>().bottom;
            yield return null;
            this.transform.localPosition = Vector3.zero;
            this.transform.localRotation = Quaternion.Euler(Vector3.zero);
        }
        
        //this.transform.position = pos;
        /*
        this.transform.parent = obj.transform;
        
        if (obj.transform.rotation.z > 0)
        {
            pos = obj.GetComponent<AwardPosition>().top.position;
        }
        else
        {
            pos = obj.GetComponent<AwardPosition>().bottom.position;
        }
        this.transform.position = pos;
*/
        //this.transform.GetChild(0).transform.position = new Vector3(0.757f, -0.373f, -0.262f);
        //this.transform.GetChild(0).transform.rotation = Quaternion.Euler(-27.9f, -31.2f, 22.21f);
        
        yield return null;
        this.transform.localPosition = Vector3.zero;
        this.transform.localRotation = Quaternion.Euler(Vector3.zero);
    }

    public Vector3 GetPointInVolume()
    {
        if (!volume)
        {
            volume = FindFirstObjectByType<AviaryController>().GetComponent<BoxCollider>();
        } 
        Vector3 extents = volume.size / 2f;
        Vector3 point = new Vector3(
            Random.Range(-extents.x, extents.x),
            Random.Range(-extents.y, extents.y),
            Random.Range(-extents.z, extents.z)
        ) + volume.center;
        return volume.transform.TransformPoint(point);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_gameplayPaused)
        {
            return;
        }

        MagicStickPoint stick = null;
        if (collision.gameObject.layer == 11)
        {
            if (isInAviary) return;
            //foreach (var stickObj in SceneManager_Butterflies.Instance.magicSticks)
            stick = collision.gameObject.GetComponent<MagicStickPoint>();//stickObj.transform.GetChild(0).GetComponent<MagicStickPoint>();
                //if (stickObj != collision.gameObject) return;
                //if (volume.gameObject.tag == "Aviary") return;
                //if (stick.closestButterfly.butterfly != gameObject) return;
            holdingStick = stick;

            if (SceneManager_Butterflies.Instance.SticksIsBusy()) return;
                
                if (stick.haveButterfly && stick.butterfly != null)
                {
                    if (stick.butterfly != gameObject) return;
                }
                
                //Debug.Log(collision.gameObject.name);
                
                //MagicStickPoint.Instance.haveButterfly = true;
                
                if (color == stickColor.random)
                {
                    if (soundOk)
                    {
                        audioSource.clip = soundOk;
                        audioSource.Play();
                    }
                    isCatched = true;
                    isInAviary = false;
                    stick.haveButterfly = true;
                    stick.butterfly = gameObject;
                    ReportButterflyOutcome(
                        stick,
                        "butterfly_catch",
                        "CORRECT",
                        "BUTTERFLY_RANDOM_MATCH");
                    StartCoroutine(SitOnStick(.01f, stick.transform.parent.gameObject));
                    GetComponent<Rigidbody>().isKinematic = true;
                }
                else
                {
                    if (stick.isActive)
                    {
                        if (color == stick._stickColor)
                        {
                            if (soundOk)
                            {
                                audioSource.clip = soundOk;
                                audioSource.Play();
                            }

                            isInAviary = false;
                            isCatched = true;
                            stick.haveButterfly = true;
                            stick.butterfly = gameObject;
                            ReportButterflyOutcome(
                                stick,
                                "butterfly_catch",
                                "CORRECT",
                                "BUTTERFLY_STICK_MATCH");
                            StartCoroutine(SitOnStick(.01f, stick.transform.parent.gameObject));
                            GetComponent<Rigidbody>().isKinematic = true;
                            
                            //SceneManager_Butterflies.Instance.SwapMagicStickActivity();
                        }
                        else
                        {
                            // beep
                            if (soundBad)
                            {
                                ReportButterflyOutcome(
                                    stick,
                                    "butterfly_catch_invalid",
                                    "INCORRECT",
                                    "BUTTERFLY_STICK_MISMATCH");
                                SceneManager_Butterflies.Instance.badAnswers++;
                                audioSource.clip = soundBad;
                                audioSource.Play();
                            
                                //UI_LifeCounter.Instance.SubstractLife();
                            }
                        }
                    }
                }
            return;
        }
        
        /*
        if (volume)
            StartCoroutine("ChangeDirection");
        else
        {
            if (isCatched)
            {
                volume = GameObject.FindGameObjectWithTag("Aviary").GetComponent<BoxCollider>();
                collision.gameObject.GetComponent<AviaryController>().CollectButterfly(gameObject,stick.gameObject);
                Debug.Log("powinien być złapany " + gameObject.name + " w " + collision.gameObject.name);
            }
            else
            {
                volume = GameObject.FindGameObjectWithTag("ButterflyVolume").GetComponent<BoxCollider>();
            }
        }
        
        if (collision.gameObject.layer == 14)
        {
            Debug.Log(collision.gameObject.name);
            collision.gameObject.GetComponent<AviaryController>().CollectButterfly(gameObject, stick.gameObject);
        }
        */

        if (collision.gameObject.layer == 0)
        {
            if (_splineFollower)
                _splineFollower.follow = false;
            //RegeneratePoints(0);
            points[0].position = transform.position;
            points[1].position = GetPointInVolume();
            points[2].position = GetPointInVolume();
            points[3].position = GetPointInVolume();
            if (_splineComputer)
            {
                _splineComputer.SetPoints(points);
                _splineComputer.RebuildImmediate();
            }

            if (_splineFollower)
            {
                _splineFollower.Restart();
                _splineFollower.follow = true;
            }
        }
    }

    private void ReportButterflyOutcome(
        MagicStickPoint stick,
        string eventType,
        string actionOutcome,
        string reasonCode)
    {
        var details = new Dictionary<string, object>
        {
            { "targetCategory", "BUTTERFLY" },
            { "butterflyColor", color.ToString() },
            { "stickColor", stick != null ? stick._stickColor.ToString() : string.Empty },
            { "stickActive", stick != null && stick.isActive },
        };
        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "BUTTERFLY",
            _targetInstanceId,
            _targetAppearedAtUtc,
            _targetAppearedAtElapsedSec,
            LegacyInteractionTelemetry.ComputeResponseSec(_targetAppearedAtElapsedSec));

        LegacyInteractionTelemetry.EmitOutcome(
            "butterflies",
            eventType,
            SceneManager_Butterflies.Instance != null && SceneManager_Butterflies.Instance.IsGameplayPaused
                ? "Paused"
                : "Playing",
            actionOutcome,
            reasonCode,
            nameof(ButterflyController),
            targetId: GetInstanceID().ToString(),
            targetName: gameObject.name,
            inputSource: stick != null
                ? "MAGIC_STICK_" + stick._stickColor.ToString().ToUpperInvariant()
                : "MAGIC_STICK",
            extraDetails: details);
    }

    private void MarkAppearance()
    {
        _targetInstanceId = LegacyInteractionTelemetry.CreateTargetInstanceId();
        _targetAppearedAtUtc = LegacyInteractionTelemetry.CurrentUtcIso();
        _targetAppearedAtElapsedSec = LegacyInteractionTelemetry.CurrentRealtimeSec();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_gameplayPaused)
        {
            return;
        }

        Debug.Log("Trigger: " + other.gameObject.name);
        if (other.gameObject.tag == "Aviary")
        {
            if (!isTriggered)
            {
                //_splineFollower.onEndReached -= RegeneratePoints;
                volume = other.GetComponent<BoxCollider>();
                isTriggered = true;
                //RegeneratePoints(0);
                //_splineFollower.onEndReached += RegeneratePoints;
            }
        }
    }

    IEnumerator ChangeDirection()
    {
        stopFlying = true;
        StartCoroutine("RunAway");
        actualPoint = GetPointInVolume();
        direction = (actualPoint - transform.position).normalized;
        this.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        yield return null;
        stopFlying = false;
    }

    public void Panic()
    {
        if (_gameplayPaused)
        {
            return;
        }

        if (holdingStick == null)
            StartCoroutine("RunAway");
    }
    
    IEnumerator RunAway2()
    {
        speed = 3;
        yield return new WaitForSeconds(1);
        speed = SceneManager_Butterflies.Instance.speed  + 0.5f;;
    }
    
    IEnumerator RunAway()
    {
        if (isCollected) yield break;
        Debug.Log("Run away");
        
        gameObject.GetComponent<BoxCollider>().enabled = false;

        //SceneManager_Butterflies.Instance.butterflyVolume.enabled = false;
        
        Vector3 oldPos = gameObject.transform.position;
        Vector3 newPos = gameObject.transform.forward;
        newPos.y = Mathf.Abs(newPos.y);
        newPos *= 10;
        _splineFollower.follow = false;
        _splineFollower.enabled = false;
        //_splineFollower.Restart(0);
        //gameObject.transform.Translate(newPos);
        
        stopFlying = true;
        
        yield return null;

        Vector3 oldScale = new Vector3(0.1f,0.1f,0.1f);
        
        //Debug.Log("teraz w górę");
        StartCoroutine(TheraplyHelpers.LerpPosition(gameObject, newPos, 0.5f));
        StartCoroutine(TheraplyHelpers.LerpScale(gameObject, Vector3.zero, 0.5f));
        //speed = 3;
        yield return new WaitForSeconds(1);

        //Debug.Log("teraz w dół");
        StartCoroutine(TheraplyHelpers.LerpScale(gameObject, oldScale, 0.5f));
        //yield return new WaitForSeconds(1);
        StartCoroutine(TheraplyHelpers.LerpPosition(gameObject, oldPos, 2));
        
        //yield return new WaitForSeconds(1);
        _splineFollower.follow = true;
        _splineFollower.enabled = true;
        //_splineFollower.Restart();
        
        speed = SceneManager_Butterflies.Instance.speed  + 0.5f;

        stopFlying = false;
        SceneManager_Butterflies.Instance.butterflyVolume.enabled = true;
        gameObject.GetComponent<BoxCollider>().enabled = true;
    }
    
    

    IEnumerator SitOnStick(float duration, GameObject parent = null)
    {
        startSittingOnStick = true;
        //foreach (var stickObj in SceneManager_Butterflies.Instance.magicSticks)
        {
            //Debug.Log(parent.gameObject.name);
            MagicStickPoint stick = parent.transform.GetChild(0).GetComponent<MagicStickPoint>();

            if (stick.haveButterfly)
            {
                _splineFollower.SetEnable(false);
                var tempScale = this.transform.localScale;
                //
                stick.ScaleUpOrb(0.5f);
                isCatched = true;
                Vector3 startPosition = transform.position;
                
                /*
                while (Vector3.Distance(transform.position, parent.transform.position) > .1f)
                {
                    sitPos = parent.gameObject.transform.TransformPoint(parent.gameObject.GetComponent<SphereCollider>()
                        .center);
                    transform.position = Vector3.Lerp(startPosition, sitPos, time / duration);
                    time += Time.deltaTime;
                    yield return null;
                }
                */

                AviaryController.Instance.OpenDoor();
                _splineFollower.follow = false;
                _splineFollower.enabled = false;
                //_splineFollower.Restart(0);
                //yield return null;
                transform.parent = parent.transform;
                //yield return null;
                //parent.gameObject.transform.TransformPoint(new Vector3(0,0,0.65f));//
                gameObject.transform.rotation = Quaternion.Euler(0,0,0);
                material.SetFloat("_PREDKOSC", 1);
                //if (parent != null)
                {
                    stick.haveButterfly = true;
                    stick.butterfly = gameObject;
                }
                
                transform.localScale = tempScale;
            }
        }
        yield return null;
        startSittingOnStick = false;
        yield return new WaitForSeconds(0.2f);
        gameObject.transform.localPosition = new Vector3(0, 0, 0.65f);
    }

    void TurnOffCollisions(bool value)
    {
        if (value == true)
        {
            GetComponent<BoxCollider>().enabled = false;
            GetComponent<Rigidbody>().isKinematic = true;
        }
        else
        {
            GetComponent<BoxCollider>().enabled = true;
            GetComponent<Rigidbody>().isKinematic = false;
        }
    }

    public void SetGameplayPaused(bool isPaused)
    {
        if (_gameplayPaused == isPaused)
        {
            return;
        }

        _gameplayPaused = isPaused;

        var boxCollider = GetComponent<BoxCollider>();
        var body = GetComponent<Rigidbody>();

        if (isPaused)
        {
            _colliderWasEnabled = boxCollider != null && boxCollider.enabled;
            _splineFollowerWasEnabled = _splineFollower != null && _splineFollower.enabled;
            _splineFollowerWasFollowing = _splineFollower != null && _splineFollower.follow;
            _rigidbodyWasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                _pausedVelocity = body.linearVelocity;
                _pausedAngularVelocity = body.angularVelocity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            if (_splineFollower != null)
            {
                _splineFollower.follow = false;
                _splineFollower.enabled = false;
            }

            if (boxCollider != null)
            {
                boxCollider.enabled = false;
            }

            return;
        }

        if (body != null)
        {
            body.isKinematic = _rigidbodyWasKinematic;
            if (!body.isKinematic)
            {
                body.linearVelocity = _pausedVelocity;
                body.angularVelocity = _pausedAngularVelocity;
            }
        }

        if (_splineFollower != null)
        {
            _splineFollower.enabled = _splineFollowerWasEnabled;
            _splineFollower.follow = _splineFollowerWasFollowing;
            if (_splineFollower.enabled && _splineFollower.follow)
            {
                _splineFollower.Restart();
            }
        }

        if (boxCollider != null)
        {
            boxCollider.enabled = _colliderWasEnabled;
        }
    }
}
