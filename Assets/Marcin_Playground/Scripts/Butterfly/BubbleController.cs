using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Dreamteck.Splines;


public class BubbleController : MonoBehaviour
{
    public GameObject BubbleSubEmitter;
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
    private Material material;
    private Vector3 lastPosition;
    public bool isCatched = false;
    private float zOffset;
    private Vector3 sitPos;
    private SplineComputer _splineComputer;
    private SplineFollower _splineFollower;
    private GameObject sc;
    float distance;
    float val;
    SplinePoint[] points = new SplinePoint[3];
    public AudioClip sound;
    private AudioSource audioSource;
    private bool _gameplayPaused;
    private bool _colliderWasEnabled;
    private bool _splineFollowerWasEnabled;
    private bool _splineFollowerWasFollowing;
    private string _targetInstanceId = string.Empty;
    private string _targetAppearedAtUtc = string.Empty;
    private float _targetAppearedAtElapsedSec = -1f;

    // Start is called before the first frame update
    void Start()
    {
        _splineFollower = GetComponent<SplineFollower>();
        if (SceneManager_Butterflies.Instance != null)
            volume = SceneManager_Butterflies.Instance.spaceToFly;
        
        actualPoint = GetPointInVolume();
        material = transform.GetChild(0).GetComponent<MeshRenderer>().material;
        
        if (randomColor)
            material.SetColor("_KOLOR", Random.ColorHSV(0.5f, 1, 0.5f, 1, 1, 1, 1, 1));
        if (randomAnimationSpeed)
            material.SetFloat("_PREDKOSC", Random.Range(10, 30));
        speed = Random.Range(1, 3);

        if (!_splineFollower.spline)
        {
            GameObject sc = new GameObject();
            sc.name = name + "_Spline";
            sc.AddComponent<SplineComputer>();
            _splineComputer = sc.GetComponent<SplineComputer>();
            points[0].position = GetPointInVolume();
            points[1].position = GetPointInVolume();
            points[2].position = GetPointInVolume();
            //points[3].position = GetPointInVolume();
            _splineComputer.SetPoints(points);
            _splineComputer.multithreaded = true;
            //_splineComputer.Close();
            _splineFollower.spline = _splineComputer;
            _splineFollower.followSpeed /= 2;
        }

        _splineFollower.onEndReached += RegeneratePoints;
        MarkAppearance();
    }

    private void OnEnable()
    {
        MarkAppearance();
    }

    public void RegeneratePoints(double d)
    {
        if (points.Length >= 2)
            points[0].position = points[2].position;
        if (points.Length >= 3)
            points[1].position = GetPointInVolume();
        if (points.Length >= 4)
            points[2].position = GetPointInVolume();
        _splineComputer.SetPoints(points);
        _splineFollower.spline = _splineComputer;
    }
    
    
    
    // Update is called once per frame
    void Update()
    {
        if (_gameplayPaused)
        {
            return;
        }

        gameObject.transform.GetChild(0).transform.LookAt(Camera.main.transform);

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
        
    }

    Vector3 GetPointInVolume()
    {
        Vector3 extents = volume.bounds.size / 2f;
        Vector3 point = new Vector3(
            Random.Range(-extents.x, extents.x),
            Random.Range(-extents.y, extents.y),
            Random.Range(-extents.z, extents.z)
        );// + volume.bounds.center;
        return volume.transform.TransformPoint(point);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_gameplayPaused)
        {
            return;
        }

        if (collision.gameObject.layer == 11)
        {
            if (collision.gameObject.GetComponent<MagicStickPoint>().isActive)
            {
                if (collision.gameObject.GetComponent<MagicStickPoint>()._stickColor == stickColor.red)
                {
                    ReportBubbleOutcome(
                        collision.gameObject.GetComponent<MagicStickPoint>(),
                        "bubble_collect",
                        "CORRECT",
                        "BUBBLE_STICK_MATCH");
                    if (sound)
                    {
                        SceneManager_Butterflies.Instance.AddBubble();
                        GameObject soundObj = new GameObject();
                        soundObj.AddComponent<AudioSource>();
                        audioSource = soundObj.GetComponent<AudioSource>();
                        audioSource.clip = sound;
                        audioSource.pitch = Random.Range(1.0f, 2.0f);
                        audioSource.Play();
                    }

                    transform.GetChild(0).GetComponent<Renderer>().enabled = false;
                    Instantiate(BubbleSubEmitter, transform.position, Quaternion.identity);
                    GetComponent<BoxCollider>().enabled = false;
                    Destroy(GetComponent<Rigidbody>());
                    Destroy(sc);
                    Destroy(gameObject);
                    
                    SceneManager_Butterflies.Instance.SwapMagicStickActivity();
                }
            }
        }
        StartCoroutine("ChangeDirection");
    }

    IEnumerator ChangeDirection()
    {
        points[0].position = transform.position;
        points[1].position = GetPointInVolume();
        points[2].position = GetPointInVolume();
        //points[3].position = GetPointInVolume();
        _splineComputer.SetPoints(points);
        _splineFollower.spline = _splineComputer;
        _splineFollower.Restart();
        yield return null;
    }

    public void SetGameplayPaused(bool isPaused)
    {
        if (_gameplayPaused == isPaused)
        {
            return;
        }

        _gameplayPaused = isPaused;
        var boxCollider = GetComponent<BoxCollider>();

        if (isPaused)
        {
            _colliderWasEnabled = boxCollider != null && boxCollider.enabled;
            _splineFollowerWasEnabled = _splineFollower != null && _splineFollower.enabled;
            _splineFollowerWasFollowing = _splineFollower != null && _splineFollower.follow;

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

    private void ReportBubbleOutcome(
        MagicStickPoint stick,
        string eventType,
        string actionOutcome,
        string reasonCode)
    {
        var details = new Dictionary<string, object>
        {
            { "targetCategory", "BUBBLE" },
            { "stickColor", stick != null ? stick._stickColor.ToString() : string.Empty },
            { "stickActive", stick != null && stick.isActive },
        };
        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "BUBBLE",
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
            nameof(BubbleController),
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
}
