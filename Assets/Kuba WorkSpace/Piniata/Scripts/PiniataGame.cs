using System;
using System.Collections;
using System.Runtime.CompilerServices;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class PiniataGame : MonoBehaviour
{
    [SerializeField] private bool gameStarted = false;

    [Space(10)] 
    [SerializeField] private PiniataLevel piniataLevel;

    public PiniataLevel GetPiniataLevel => piniataLevel;
    
    [Space(10)]
    [SerializeField] private TextMeshPro infoText;

    // Debounce: after a correct hit, ignore wrong-answer events for this duration.
    // Prevents physics tunneling (fast swing overlapping multiple trigger zones simultaneously).
    private const float CorrectHitDebounceSec = 0.3f;
    private float _correctHitDebounceEndTime = -1f;

    [Space(20)]
    [SerializeField] private int correctAnswersPoints = 0;
    [SerializeField] private int wrongAnswersPoints = 0;
    public int WrongAnswerPoint => wrongAnswersPoints;
    [SerializeField] private int pointIndex = 0;
    [SerializeField] private bool gameOver = false;

    [Space(20)]
    [SerializeField] private SliderIndicator triesLeft;
    [SerializeField] private SliderIndicator pointsLeft;
    public bool GameOver
    {
        get => gameOver;
        set => gameOver = value;
    }

    [SerializeField] private float passPercent = 50;


    [Space(20)] 
    public TextMeshPro generalText;
    public TextMeshPro goodAnswersText;
    public TextMeshPro wrongAnswersText;


    [Space(20)]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField] private AudioClip gameFinished;
    [SerializeField] private PiniataLevelManager _piniataLevelManager;
    [SerializeField] private PresentsManager _presentsManager;

    [Space(20)]
    [SerializeField] private Rigidbody rb;

    public Rigidbody Rb => rb;
    public bool IsGameplayPaused => _isGameplayPaused;
    public enum HitPointType
    {
        Red,
        Blue
    }

    [SerializeField] private GameState gameState;
    public GameState State => gameState;
    public enum GameState
    {
        NotStarted,
        InProgress,
        GameLost,
        GameFinished,
    }

    public PiniataLevel SetPiniataLevel
    {
        set => piniataLevel = value;
    }

    private bool _isGameplayPaused;
    private bool _rbWasKinematic;
    private Vector3 _pausedVelocity;
    private Vector3 _pausedAngularVelocity;

    public void GoodAnswer()
    {
        _correctHitDebounceEndTime = Time.realtimeSinceStartup + CorrectHitDebounceSec;
        correctAnswersPoints++;
        pointIndex++;
        
        pointsLeft.CurrentValue = pointIndex;
        
        if(goodAnswersText == null) return;
        goodAnswersText.text = "Ilość dobrych odpowiedzi: " + correctAnswersPoints.ToString();
    }

    public void WrongAnswer(bool bug = false)
    {
        if (Time.realtimeSinceStartup < _correctHitDebounceEndTime) return;
        wrongAnswersPoints++;
        if(!bug) pointIndex++;
        
        float t = piniataLevel.MaxPoints * ((passPercent) / 100);
        triesLeft.CurrentValue = t - wrongAnswersPoints;
        pointsLeft.CurrentValue = pointIndex;
           
        if (wrongAnswersPoints > piniataLevel.MaxPoints * ((passPercent) / 100))
        {
            if (piniataLevel.ResetOnWrong)
            {
                Debug.Log("Single Game Piniata Data z PINIATA GAME");
                piniataLevel.SingleGameFinalData();
                gameState = GameState.GameLost;
                StartCoroutine(VirtualFriend.Instance.FriendTalking("GameOver",3.0f));
                gameOver = true;
                _audioSource.clip = gameOverClip;
                _audioSource.Play();
                
                piniataLevel.SingleGameFinalData();

                PiniataCommunication pc = FindFirstObjectByType<PiniataCommunication>();
                pc.OnGameFinished("GameFinished:Piniata");
                
                ResetTheGame();
            }
        }
        
        if(wrongAnswersText == null) return;
        wrongAnswersText.text = "Ilość złych odpowiedzi: " + wrongAnswersPoints.ToString();
    }

    public void SetPassPercent(int p)
    {
        passPercent = p;
    }
           
            
    // Avarage and Fixed Time Section ----------------------------------------------------------------

    private Coroutine mainGameCoroutine;

    [ContextMenu("SHOW POINTS")]
    public void TEST()
    {
        ShowPoints();
    }

    public void PlayFinishedAudio()
    {
        if(_audioSource.clip == gameFinished && _audioSource.isPlaying) return;
        _audioSource.clip = gameFinished;
        _audioSource.Play();
    }

    public void SaveProgress()
    {
        _presentsManager.CollectedPresent(_piniataLevelManager.GetCurrentLevelIndex());
        _piniataLevelManager.LevelFinished();
    }

    public void SetSliders()
    {
        triesLeft.MaxValue = piniataLevel.MaxPoints * ((passPercent) / 100) + 1;
        triesLeft.CurrentValue = piniataLevel.MaxPoints * ((passPercent) / 100) + 1;
        
        pointsLeft.MaxValue = piniataLevel.MaxPoints;
        pointsLeft.CurrentValue = 0;
    }
    
    public void ShowPoints(bool touch = false)
    {
        if (piniataLevel == null) return;
        gameState = GameState.InProgress;
        
        if(!touch && gameStarted) return;
        piniataLevel.GenerateInsects();
        
        generalText.text = "Gra Rozpoczęta!";
        goodAnswersText.text = "-";
        wrongAnswersText.text = "-";
        
        gameStarted = true;
        mainGameCoroutine = StartCoroutine(StartGameEnum());
    }

    public int currentGameSeries = 0;
    public int currentGameRound = 0;
    
    private Coroutine singleRoundCoroutine;
    IEnumerator StartGameEnum()
    {
        currentGameSeries = 0;
        foreach (SingleRound singleRound in piniataLevel.GameSeries)
        {
            singleRoundCoroutine = StartCoroutine(SingleRound(singleRound));
            yield return singleRoundCoroutine;
            currentGameSeries++;
        }

        gameState = GameState.GameFinished;
        piniataLevel.levelFinished = true;
    }
    
    
    private Coroutine singlePointsCoroutine;
    
    IEnumerator SingleRound(SingleRound round)
    {
        // currentGameRound = 0;
        if (piniataLevel.LevelType == PiniataLevel.LevelTypeEnum.PointTouched)
        {
            if (piniataLevel.touchIndex < round.roundPoints.Count)
            {
                yield return new WaitForSeconds(0.3f);
                singlePointsCoroutine = StartCoroutine(SinglePoints(round.roundPoints[piniataLevel.touchIndex]));
                yield return singlePointsCoroutine;
            }
            
        }
        else
        {
            foreach (RoundPoints roundPoints in round.roundPoints)
            {
                singlePointsCoroutine = StartCoroutine(SinglePoints(roundPoints));
                yield return singlePointsCoroutine;
            }
        }

        currentGameRound++;
        
        if (piniataLevel.LevelType != PiniataLevel.LevelTypeEnum.AverageTime) yield break;
        
        float avg = 0.0f;

        foreach (RoundPoints roundPoints in round.roundPoints)
        {
            avg += roundPoints.averageTime;
        }

        avg /= round.roundPoints.Count;
        round.averageRoundTime = Mathf.Round(avg*100.0f) / 100.0f;
        avg *= (100 + round.intervalAdvantage) / 100;
        piniataLevel.IntervalTime = Mathf.Round(avg*100.0f) / 100.0f;
        infoText.text = piniataLevel.IntervalTime + " s.";
    }

    IEnumerator WaitForTime(float t)
    {
        yield return WaitForGameplaySeconds(t);
    }

    public IEnumerator WaitForGameplaySeconds(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            if (!_isGameplayPaused)
            {
                remaining -= Time.unscaledDeltaTime;
            }

            yield return null;
        }
    }

    IEnumerator SinglePoints(RoundPoints points)
    {
        Debug.Log("ILOSC PUNKTOW: " + points.points.Count);
        float t = piniataLevel.MaxPoints * ((passPercent) / 100);
        
        if (wrongAnswersPoints > piniataLevel.MaxPoints * ((passPercent) / 100))
        {
            gameState = GameState.GameLost;
            if (piniataLevel.ResetOnWrong)
            {
                _audioSource.clip = gameOverClip;
                _audioSource.Play();
                ResetTheGame();
                yield break;
            }
        }

        foreach (var point in points.points)  
        {
            point.ShowPoint();
        }

        if (piniataLevel.LevelType != PiniataLevel.LevelTypeEnum.PointTouched)
        {
            
            yield return WaitForTime(piniataLevel.IntervalTime);;
            Debug.Log("Dochodzę tutaj 1!");
            // foreach (var point in points.points)  
            // {
            //     point.HidePoint(true);
            // }
        }
        
        yield return WaitForGameplaySeconds(0.3f);
        
        float avg = 0.0f;
        
        if (piniataLevel.LevelType != PiniataLevel.LevelTypeEnum.PointTouched)
        {
            Debug.Log("Dochodzę tutaj 2!");
            foreach (HitPoint point in points.points)
            {
                Debug.Log("ILE UPŁYNĘŁO: " + point.TimeElapsed);
                float tm = point.TimeElapsed;
                if (point.TimeElapsed >= piniataLevel.IntervalTime - 0.05f)
                {
                    tm = piniataLevel.IntervalTime;
                }
                points.reactionTime.Add(tm);
                Debug.Log("TYP PUNKTU: " + point.PointType);
                points.pointColor.Add((int)point.PointType);
                avg += point.TimeElapsed;
                yield return null;
            }
            
            foreach (var point in points.points)  
            {
                Debug.Log("Hide ze skończonego timera!!");
                point.HidePoint(true);
            }
        }
        
        if (piniataLevel.LevelType != PiniataLevel.LevelTypeEnum.AverageTime) yield break;

        avg /= points.points.Count;
        points.averageTime = Mathf.Round(avg*100.0f) / 100.0f;
    }

    public void ResetTheGame(bool menu = true, bool resetCoroutines = true)
    {
        _isGameplayPaused = false;
        ResumePiniataBody();
        SetHitPointsInteractable(true);
        ResumeHitPointTweens();

        _piniataLevelManager.ResetSticks();
        if(menu) _piniataLevelManager.ShowMenu();
        // StopAllCoroutines();
        if (resetCoroutines)
        {
            if(singlePointsCoroutine != null) StopCoroutine(singlePointsCoroutine);
            if(singleRoundCoroutine != null) StopCoroutine(singleRoundCoroutine);
            if(mainGameCoroutine != null)
            {
                StopCoroutine(mainGameCoroutine);
            };
        }
        
        piniataLevel.DeleteInsects();
        correctAnswersPoints = 0;
        wrongAnswersPoints = 0;
        pointIndex = 0;
        
        gameStarted = false;
        generalText.text = "Gra Zakończona!";
        goodAnswersText.text = "-";
        wrongAnswersText.text = "-";
        
    }

    public void PauseGameplay()
    {
        if (_isGameplayPaused || gameState != GameState.InProgress)
        {
            return;
        }

        _isGameplayPaused = true;
        PausePiniataBody();
        SetHitPointsInteractable(false);
        PauseHitPointTweens();
    }

    public void ResumeGameplay()
    {
        if (!_isGameplayPaused || gameState != GameState.InProgress)
        {
            return;
        }

        _isGameplayPaused = false;
        ResumePiniataBody();
        SetHitPointsInteractable(true);
        ResumeHitPointTweens();
    }

    private void PausePiniataBody()
    {
        if (rb == null)
        {
            return;
        }

        _rbWasKinematic = rb.isKinematic;
        _pausedVelocity = rb.linearVelocity;
        _pausedAngularVelocity = rb.angularVelocity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }

    private void ResumePiniataBody()
    {
        if (rb == null)
        {
            return;
        }

        rb.isKinematic = _rbWasKinematic;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = _pausedVelocity;
            rb.angularVelocity = _pausedAngularVelocity;
        }
    }

    private void SetHitPointsInteractable(bool isEnabled)
    {
        foreach (var point in GetActiveHitPoints())
        {
            point.SetInteractionEnabled(isEnabled);
        }
    }

    private void PauseHitPointTweens()
    {
        foreach (var point in GetActiveHitPoints())
        {
            DOTween.Pause(point.transform);
        }
    }

    private void ResumeHitPointTweens()
    {
        foreach (var point in GetActiveHitPoints())
        {
            DOTween.Play(point.transform);
        }
    }

    private HitPoint[] GetActiveHitPoints()
    {
        if (piniataLevel == null)
        {
            return Array.Empty<HitPoint>();
        }

        return piniataLevel.GetComponentsInChildren<HitPoint>(true);
    }

    public bool AllPointsTouched()
    {
        bool b = true;

        foreach (var pts in piniataLevel.GameSeries[0].roundPoints[piniataLevel.touchIndex].points)
        {
            if (!pts.PointTouched)
            {
                b = false;
                break;
            }
        }
        
        return b;
    }
}
