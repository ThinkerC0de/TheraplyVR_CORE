using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Dreamteck.Splines;
using UnityEngine;

public class DisturbBug : MonoBehaviour
{
    [SerializeField] private bool hitted = false;

    [SerializeField] private Animator _animator;
    [SerializeField] private SplineFollower _splineFollower;
    [SerializeField] private ParticleSystem particle;
    [SerializeField] private AudioSource _audioSource;
    private PiniataGame _piniataGame;
    [SerializeField] private Collider col;
    private float startScale;   
    [SerializeField] private string _targetInstanceId = string.Empty;
    [SerializeField] private string _targetAppearedAtUtc = string.Empty;
    [SerializeField] private float _targetAppearedAtElapsedSec = -1f;
    public bool IsHit => hitted;
    public string TargetInstanceId => _targetInstanceId;
    public string TargetAppearedAtUtc => _targetAppearedAtUtc;
    public float TargetAppearedAtElapsedSec => _targetAppearedAtElapsedSec;
    
    private void Awake()
    {
        _piniataGame = FindFirstObjectByType<PiniataGame>();
        startScale = transform.localScale.x;
        MarkAppearance();
    }

    private void OnTriggerEnter(Collider other)
    {
        if(hitted) return;
        
        if (other.gameObject.CompareTag("Stick_Red") || other.gameObject.CompareTag("Stick_Blue"))
        {
            hitted = true;
            BugHitted();
        }
    }

    [ContextMenu("BugHit")]
    public void BugHitted()
    {
        _piniataGame.GetPiniataLevel.BugHitted();

        if (!PiniataZoneTruthTelemetry.Enabled)
        {
            LegacyInteractionTelemetry.EmitOutcome(
                "piniata",
                "piniata_bug_hit",
                _piniataGame != null ? _piniataGame.State.ToString() : "InProgress",
                "INCORRECT",
                "PINIATA_BUG_TRIGGERED",
                nameof(DisturbBug),
                targetId: GetInstanceID().ToString(),
                targetName: gameObject.name,
                inputSource: "DISTURB_BUG",
                extraDetails: new Dictionary<string, object>
                {
                    { "targetCategory", "DISTURB_BUG" },
                    { "targetInstanceId", _targetInstanceId },
                    { "targetAppearedAtUtc", _targetAppearedAtUtc },
                    { "targetAppearedAtElapsedSec", _targetAppearedAtElapsedSec },
                    { "responseSec", LegacyInteractionTelemetry.ComputeResponseSec(_targetAppearedAtElapsedSec) },
                });
        }
        _piniataGame.WrongAnswer(true);
        _audioSource.Play();
        particle.Play();

        transform.DOScale(0.0f, 0.5f).OnComplete((() => StartCoroutine(ShowAgain())));
    }

    IEnumerator ShowAgain()
    {
        col.enabled = false;
        yield return new WaitForSeconds(3.0f);
        MarkAppearance();
        transform.DOScale(startScale, 0.5f);
        col.enabled = true;
        hitted = false;
    }

    private void MarkAppearance()
    {
        _targetInstanceId = LegacyInteractionTelemetry.CreateTargetInstanceId();
        _targetAppearedAtUtc = LegacyInteractionTelemetry.CurrentUtcIso();
        _targetAppearedAtElapsedSec = LegacyInteractionTelemetry.CurrentRealtimeSec();
    }
}
