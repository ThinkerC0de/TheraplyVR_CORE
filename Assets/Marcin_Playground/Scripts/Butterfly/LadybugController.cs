using System.Collections;
using System.Collections.Generic;
using Dreamteck.Splines;
using UnityEngine;

public class LadybugController : MonoBehaviour
{
    [SerializeField] private Animator animCtrl;
    public Collider volume;
    public SplineComputer _splineComputer;
    public SplineFollower _splineFollower;
    private GameObject sc;
    public SplinePoint[] points = new SplinePoint[4];
    public AudioClip soundOk;
    public AudioClip soundBad;
    private AudioSource audioSource;
    public bool isCollected = false;
    public stickColor reactToStick;
    private bool _gameplayPaused;
    private bool _colliderWasEnabled;
    private bool _splineFollowerWasEnabled;
    private bool _splineFollowerWasFollowing;
    private float _animatorSpeed = 1f;
    private string _targetInstanceId = string.Empty;
    private string _targetAppearedAtUtc = string.Empty;
    private float _targetAppearedAtElapsedSec = -1f;

    // Start is called before the first frame update
    void Start()
    {
        animCtrl.SetBool("isFlying", true);
        if (!audioSource) gameObject.AddComponent<AudioSource>();
        audioSource = GetComponent<AudioSource>();
        MarkAppearance();
    }

    private void OnEnable()
    {
        MarkAppearance();
    }

    public void RegeneratePoints(double d)
    {
        points[0].position = points[2].position;
        points[1].position = GetPointInVolume();
        points[2].position = GetPointInVolume();
        _splineComputer.SetPoints(points);
        _splineFollower.spline = _splineComputer;
    }

    public Vector3 GetPointInVolume()
    {
        if (!volume) return Vector3.zero;
        Vector3 extents = volume.bounds.size / 2f;
        Vector3 point = new Vector3(
            Random.Range(-extents.x, extents.x),
            Random.Range(-extents.y, extents.y),
            Random.Range(-extents.z, extents.z)
        );//+ volume.bounds.center;
        return volume.transform.TransformPoint(point);
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (_gameplayPaused)
        {
            return;
        }

        if (isCollected) return;
        
        if (collision.gameObject.layer == 11)
        {
            var stick = collision.gameObject.GetComponent<MagicStickPoint>();
            if (stick.isActive)
            {
                if (stick._stickColor == reactToStick)
                {
                    ReportLadybugOutcome(
                        stick,
                        "ladybug_catch",
                        "CORRECT",
                        "LADYBUG_STICK_MATCH");
                    StartCoroutine(FlyAwayCoroutine());
                    if (soundOk)
                    {
                        SceneManager_Butterflies.Instance.AddLadybug();
                        audioSource.clip = soundOk;
                        audioSource.pitch = Random.Range(1.0f, 2.0f);
                        audioSource.Play();
                    }

                    isCollected = true;

                    SceneManager_Butterflies.Instance.SwapMagicStickActivity();
                }
                else
                {
                    if (soundBad)
                    {
                        ReportLadybugOutcome(
                            stick,
                            "ladybug_catch_invalid",
                            "INCORRECT",
                            "LADYBUG_STICK_MISMATCH");
                        SceneManager_Butterflies.Instance.badAnswers++;
                        audioSource.clip = soundBad;
                        audioSource.Play();

                        //UI_LifeCounter.Instance.SubstractLife();
                    }
                }
            }
            else return;
        }
    }

    IEnumerator FlyAwayCoroutine()
    {
        float duration = 2;
        float time = 0;
        /*
        points[0].position = transform.parent.transform.position;
        points[1].position = points[0].position + Vector3.up*2 + Vector3.left*2;
        points[2].position = points[0].position + Vector3.up*2 + Vector3.right*2;
        _splineComputer.SetPoints(points);
        _splineFollower.spline = _splineComputer;
        yield return new WaitForSeconds(3);
        */
        
        Destroy(_splineComputer);
        _splineFollower.enabled = false;
        GetComponent<BoxCollider>().enabled = false;
        
        Vector3 startPosition = transform.position;
        while (time < duration)
        {
            transform.position = Vector3.Lerp(startPosition, new Vector3(startPosition.x, startPosition.y + 5, startPosition.z), time / duration);
            transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        
        Destroy(gameObject);
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
            _animatorSpeed = animCtrl != null ? animCtrl.speed : 1f;

            if (animCtrl != null)
            {
                animCtrl.speed = 0f;
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

        if (animCtrl != null)
        {
            animCtrl.speed = _animatorSpeed;
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

    private void ReportLadybugOutcome(
        MagicStickPoint stick,
        string eventType,
        string actionOutcome,
        string reasonCode)
    {
        var details = new Dictionary<string, object>
        {
            { "targetCategory", "LADYBUG" },
            { "reactToStick", reactToStick.ToString() },
            { "stickColor", stick != null ? stick._stickColor.ToString() : string.Empty },
            { "stickActive", stick != null && stick.isActive },
        };
        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "LADYBUG",
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
            nameof(LadybugController),
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
