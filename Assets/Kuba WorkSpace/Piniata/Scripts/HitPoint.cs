using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class HitPoint : MonoBehaviour
{
    [SerializeField] private PiniataGame piniataGame;
    [SerializeField] private bool isShown = false;
    public bool IsShown => isShown;
    [SerializeField] private PiniataGame.HitPointType pointType;

    public PiniataGame.HitPointType PointType => pointType;

    [SerializeField] private List<Material> colorList;
    [SerializeField] private List<Material> colorRingsList;
    [SerializeField] private Renderer ringRenderer;

    [SerializeField] private float startScale = 0.1f;
    [SerializeField] private new Rigidbody rigidbody;
    [SerializeField] private bool lastPoint = false;
    [SerializeField] private List<HitPoint> otherPoints;

    [Space(10)]
    [SerializeField] private AudioClip correctAudio;
    [SerializeField] private AudioClip wrongAudio;

    [SerializeField] private AudioClip appearAudio;
    public Rigidbody SetRigidbody
    {
        set => rigidbody = value;
    }

    [SerializeField] private bool pointTouched = false;
    [SerializeField] private bool interactionsEnabled = true;
    [SerializeField] private string _targetInstanceId = string.Empty;
    [SerializeField] private string _targetAppearedAtUtc = string.Empty;
    [SerializeField] private float _targetAppearedAtElapsedSec = -1f;
    public string TargetInstanceId => _targetInstanceId;
    public string TargetAppearedAtUtc => _targetAppearedAtUtc;
    public float TargetAppearedAtElapsedSec => _targetAppearedAtElapsedSec;

    public bool PointTouched => pointTouched;

    [SerializeField] private ParticleSystem particle;
    [SerializeField] private AudioSource _audioSource;

    [SerializeField] float fc = 100000;

    public PiniataGame Piniata
    {
        set => piniataGame = value;
    }

    private void Awake()
    {
        GetComponent<Renderer>().material = colorList[(int)pointType];
        ringRenderer.material = colorRingsList[(int)pointType];

        _audioSource.clip = correctAudio;
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        interactionsEnabled = isEnabled;

        var colliderRef = GetComponent<Collider>();
        if (colliderRef != null)
        {
            colliderRef.enabled = isEnabled;
        }
    }

    public void ShowPoint()
    {
        _audioSource.clip = appearAudio;
        _audioSource.Play();

        MarkPointAppearance();
        pointTouched = false;
        ResetTimer();
        isShown = true;
        timer = true;
        transform.DOScale(startScale, 0.4f);
    }

    private void JustHidePoint()
    {
        transform.DOScale(0.0f, 0.6f);
    }

    public void HidePoint(bool wrongAnswer = false)
    {
        StartCoroutine(HidePointEnum(wrongAnswer));
    }

    IEnumerator HidePointEnum(bool wrongAnswer = false)
    {
        if (wrongAnswer)
        {
            if (!pointTouched)
            {
                if (isShown)
                {
                    _audioSource.clip = wrongAudio;
                    _audioSource.Play();
                }

                ReportOutcome(
                    "piniata_point_timeout",
                    "INCORRECT",
                    "PINIATA_TARGET_TIMEOUT",
                    string.Empty,
                    "SYSTEM");
                piniataGame.WrongAnswer();
            }
        }

        timer = false;
        isShown = false;
        transform.DOScale(0.0f, 0.6f);
        yield return WaitForGameplaySeconds(0.65f);

        if (_audioSource.isPlaying)
        {
            while (_audioSource.isPlaying)
            {
                yield return null;
            }
            _audioSource.clip = correctAudio;
        }
        yield return WaitForGameplaySeconds(0.03f);

        if (lastPoint)
        {
            bool b = false;
            if (otherPoints.Count > 0)
            {
                foreach (HitPoint otherPoint in otherPoints)
                {
                    if (otherPoint.isShown)
                    {
                        b = otherPoint.isShown;
                    }
                    else
                    {
                        b = otherPoint.isShown;
                        break;
                    }
                }
            }

            // pointTouched = false; // TO JEST DODANE
            if (b) yield break;

            if (piniataGame.GameOver) yield break;
            piniataGame.GameOver = true;

            piniataGame.GetPiniataLevel.BrokePiniata();
            // yield return new WaitForEndOfFrame();
            // yield return new WaitForSeconds(0.5f);
            piniataGame.SaveProgress();
            piniataGame.ResetTheGame(false, false);
        }
    }

    [SerializeField] private float timeElapsed = 0.0f;

    public float TimeElapsed => timeElapsed;


    [SerializeField] private bool timer = false;

    void ResetTimer()
    {
        timeElapsed = 0.0f;
    }

    private void FixedUpdate()
    {
        if (timer && (piniataGame == null || !piniataGame.IsGameplayPaused))
        {
            timeElapsed += Time.fixedDeltaTime;
        }
    }

    private IEnumerator VibrateControllerRight()
    {
        if (HapticController.Instance != null)
        {
            HapticController.Instance.RightHandGentleVibrations();
        }
        yield return new WaitForSeconds(0.2f);
    }

    private IEnumerator VibrateControllerLeft()
    {
        if (HapticController.Instance != null)
        {
            HapticController.Instance.LeftHandGentleVibrations();
        }
        yield return new WaitForSeconds(0.2f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!interactionsEnabled || (piniataGame != null && piniataGame.IsGameplayPaused))
        {
            return;
        }

        if (!isShown) return;

        timer = false;

        if (other.gameObject.CompareTag("Stick_Red") || other.gameObject.CompareTag("Stick_Blue"))
        {
            if (pointTouched) return;

            var hittingStick = other.GetComponentInParent<HittingStick>();
            var inputHand = ResolveInputHand(
                hittingStick != null ? hittingStick.handType : HittingStick.HandType.NotSelected);
            var inputSource = other.gameObject.tag;

            if (other.gameObject.CompareTag("Stick_Red") && pointType == PiniataGame.HitPointType.Red)
            {
                hittingStick?.RegisterCorrectPointInteraction();
                piniataGame.GoodAnswer();
                _audioSource.clip = correctAudio;
                TheraplyCore.Interactions.PhysicsContactHitReporter.SuppressGroup("PINIATA_BODY", 0.4f);
                ReportOutcome(
                    "piniata_point_hit",
                    "CORRECT",
                    "PINIATA_TARGET_MATCH",
                    inputHand,
                    inputSource);

                HittingStick.HandType ht = hittingStick != null
                    ? hittingStick.handType
                    : HittingStick.HandType.NotSelected;
                if (ht == HittingStick.HandType.LeftHand)
                {
                    StartCoroutine(VibrateControllerLeft());
                }
                else if (ht == HittingStick.HandType.RightHand)
                {
                    StartCoroutine(VibrateControllerRight());
                }
            }
            else if (other.gameObject.CompareTag("Stick_Blue") && pointType == PiniataGame.HitPointType.Blue)
            {
                hittingStick?.RegisterCorrectPointInteraction();
                piniataGame.GoodAnswer();
                _audioSource.clip = correctAudio;
                TheraplyCore.Interactions.PhysicsContactHitReporter.SuppressGroup("PINIATA_BODY", 0.4f);
                ReportOutcome(
                    "piniata_point_hit",
                    "CORRECT",
                    "PINIATA_TARGET_MATCH",
                    inputHand,
                    inputSource);

                HittingStick.HandType ht = hittingStick != null
                    ? hittingStick.handType
                    : HittingStick.HandType.NotSelected;
                if (ht == HittingStick.HandType.LeftHand)
                {
                    StartCoroutine(VibrateControllerLeft());
                }
                else if (ht == HittingStick.HandType.RightHand)
                {
                    StartCoroutine(VibrateControllerRight());
                }
            }
            else
            {
                PlayWrongAudio();
                ReportOutcome(
                    "piniata_point_hit_invalid",
                    "INCORRECT",
                    "PINIATA_TARGET_MISMATCH",
                    inputHand,
                    inputSource);
                piniataGame.WrongAnswer();
            }

            pointTouched = true;

            // Dodanie odrzutu do piniaty po uderzeniu w punkt
            Transform parent;
            Vector3 forceDirection = -transform.position + (parent = transform.parent).transform.position;

            rigidbody.AddForce(forceDirection * fc);

            switch (piniataGame.GetPiniataLevel.LevelType)
            {
                case PiniataLevel.LevelTypeEnum.PointTouched:
                    // tutaj normalnie pokazuje punkt i czekam aż dziecko uderzy 

                    foreach (HitPoint point in piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.GetPiniataLevel.touchIndex].points)
                    {
                        piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.GetPiniataLevel.touchIndex].reactionTime.Add(timeElapsed);
                        piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.GetPiniataLevel.touchIndex].pointColor.Add((int)pointType);
                    }

                    if (piniataGame.AllPointsTouched())
                    {
                        piniataGame.GetPiniataLevel.touchIndex++;

                        piniataGame.ShowPoints(true);
                    }

                    break;
                    // case PiniataLevel.LevelTypeEnum.FixedTime:
                    //     
                    //     foreach (HitPoint point in piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.GetPiniataLevel.touchIndex].points)
                    //     {
                    //         piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.currentGameRound].reactionTime.Add(timeElapsed);
                    //         piniataGame.GetPiniataLevel.GameSeries[0].roundPoints[piniataGame.currentGameRound].pointColor.Add((int)pointType);
                    //     }
                    //     break;
            }

            _audioSource.Play();
            particle.Play();
            Debug.Log("Hide z uderzenia w punkt!");

            if (piniataGame.GetPiniataLevel.LevelType == PiniataLevel.LevelTypeEnum.FixedTime && lastPoint)
            {
                Debug.Log("OSTATNI PUNKT TYLKO GO CHOWAM");
                JustHidePoint();
            }
            else
            {
                HidePoint();
            }
        }
    }

    private void PlayWrongAudio()
    {
        StartCoroutine(WrongAudioEnum());
    }

    IEnumerator WrongAudioEnum()
    {
        _audioSource.clip = wrongAudio;
        _audioSource.Play();
        yield return null;
        // yield return new WaitForSeconds(wrongAudio.length + 0.2f);
        // _audioSource.clip = correctAudio;
    }

    private IEnumerator WaitForGameplaySeconds(float seconds)
    {
        if (piniataGame != null)
        {
            yield return piniataGame.WaitForGameplaySeconds(seconds);
            yield break;
        }

        yield return new WaitForSeconds(seconds);
    }

    private void ReportOutcome(
        string eventType,
        string actionOutcome,
        string reasonCode,
        string inputHand,
        string inputSource)
    {
        if (PiniataZoneTruthTelemetry.Enabled &&
            !string.Equals(eventType, "piniata_point_timeout", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var details = new Dictionary<string, object>
        {
            { "pointType", pointType.ToString() },
            { "pointTouched", pointTouched },
            { "targetCategory", "PINIATA_POINT" },
            { "reactionSec", timeElapsed },
        };
        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            "PINIATA_POINT",
            _targetInstanceId,
            _targetAppearedAtUtc,
            _targetAppearedAtElapsedSec,
            timeElapsed);

        LegacyInteractionTelemetry.EmitOutcome(
            "piniata",
            eventType,
            piniataGame != null ? piniataGame.State.ToString() : "InProgress",
            actionOutcome,
            reasonCode,
            nameof(HitPoint),
            targetId: GetInstanceID().ToString(),
            targetName: gameObject.name,
            inputHand: inputHand,
            inputSource: inputSource,
            inputValue: timeElapsed,
            extraDetails: details);
    }

    private void MarkPointAppearance()
    {
        _targetInstanceId = LegacyInteractionTelemetry.CreateTargetInstanceId();
        _targetAppearedAtUtc = LegacyInteractionTelemetry.CurrentUtcIso();
        _targetAppearedAtElapsedSec = LegacyInteractionTelemetry.CurrentRealtimeSec();
    }

    private static string ResolveInputHand(HittingStick.HandType handType)
    {
        switch (handType)
        {
            case HittingStick.HandType.LeftHand:
                return "LEFT";
            case HittingStick.HandType.RightHand:
                return "RIGHT";
            default:
                return string.Empty;
        }
    }
}
