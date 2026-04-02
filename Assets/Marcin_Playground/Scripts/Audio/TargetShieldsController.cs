using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autohand;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(AudioSource))]
public class TargetShieldsController : MonoBehaviour
{
    public static TargetShieldsController Instance;
    private byte[] _randomNumbers = { 0, 8, 4, 1, 5, 9, 3, 5, 7, 10, 1, 8, 3, 7, 5, 6, 10, 3, 7, 0, 10, 2, 11, 5, 3, 7, 1, 9, 2, 8 };
    private byte _numberIndex = 0;
    [SerializeField] private byte _crowSoundsCount = 3;
    [SerializeField] private byte _roundsCount = 5;
    public List<TargetShield> targetShields;
    public int targetIndex;
    public bool targetSelected = false;
    public GameObject puppetPrefab;
    private Stopwatch stopwatch;
    private int correctAnswers = 0;
    private int answersNr = 0;
    private float bestAnswerTime = 0;
    private float averageAnswerTime = 0;
    private List<float> answerTimes;
    public bool canAnimate = true;
    public GameObject startBtn;
    bool isBusy = false;
    bool isPressed = false;
    private testState actualState = testState.PreTest;
    public AudioClip correctAnswerAudio;
    public AudioClip welcome1Audio;
    public AudioClip welcome2Audio;
    public AudioClip thanksBye;
    public AudioSource audioSource;
    public AudioSource jungleSource;
    public AudioSource oceanSource;

    private SeekAndHideData _collectData;
    private SeekAndHideLevelData _levelData;
    private Coroutine _btnCoroutine;
    private Coroutine _crowCoroutine;
    private string desc = "";
    public bool firstRun = true;

    public PhysicsGadgetButton startPGB;
    private bool _btnPressed = false;
    public HidingGameCommunication hidingGameCommunication;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        AnimateButton();
        DisableAllTargets();
    }

    IEnumerator PlayWelcomeAudio1()
    {
        StopAnimateButton();
        yield return new WaitForSeconds(0.5f);
        StartCoroutine(VirtualFriend.Instance.FriendTalking("Pretest_1_welcome"));
        yield return new WaitForSeconds(0.5f);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        AnimateButton();
        firstRun = false;
        _btnPressed = false;
    }

    IEnumerator PlayWelcomeAudio2()
    {
        StopAnimateButton();
        yield return new WaitForSeconds(3);
        StartCoroutine(VirtualFriend.Instance.FriendTalking("Pretest_2_welcome"));
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        AnimateButton();
        _btnPressed = false;
    }

    public void Run()
    {
        if (_btnPressed) return;
        _btnPressed = true;
        if (firstRun)
        {
            _collectData = new SeekAndHideData();
            _collectData.levelData = new List<SeekAndHideLevelData>();
            _levelData = new SeekAndHideLevelData();
            answerTimes = new List<float>();
            StartCoroutine(PlayWelcomeAudio1());
        }
        else
        {
            StopAnimateButton();
            StartStopwatch();

            ClearTargetShields();
            RunRound();
        }
    }

    public void DisableAllTargets()
    {
        foreach (var target in targetShields)
        {
            target.targetShieldMesh.transform.GetChild(0).gameObject.SetActive(false);
        }
    }

    public void EnableAllTargets()
    {
        foreach (var target in targetShields)
        {
            target.targetShieldMesh.transform.GetChild(0).gameObject.SetActive(true);
        }
    }

    void StartCollect(string description = "")
    {
        _levelData = new SeekAndHideLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        _levelData.description = description;
    }

    void RunRound()
    {
        if (_numberIndex > 30) return;
        targetIndex = _randomNumbers[_numberIndex];
        if (_numberIndex == 0)
        {
            StartCollect(actualState + "1");
        }

        if (_numberIndex == 15)
        {

            //Add noise
            jungleSource.Play();
            oceanSource.Play();

            StartCollect(actualState + "2");

        }
        else if (_numberIndex == 30)
        {
            jungleSource.Stop();
            oceanSource.Stop();
            return;
        }

        targetSelected = false;

        _crowCoroutine = StartCoroutine(PlayCrowSoundsCoroutine(targetIndex));

        _roundsCount--;
        if (_roundsCount == 0)
        {
            _roundsCount = 5;
            _crowSoundsCount--;
            if (_crowSoundsCount == 0)
            {
                _crowSoundsCount = 3;
            }
        }

        _numberIndex++;
        answersNr++;
    }

    public void StopCrowCoroutine()
    {
        if (_crowCoroutine != null)
            StopCoroutine(_crowCoroutine);
    }

    IEnumerator PlayCrowSoundsCoroutine(int n)
    {
        StopAllTargets();
        EnableAllTargets();
        for (int i = _crowSoundsCount; i > 0; i--)
        {
            targetShields[n].PlayCrow();
            yield return new WaitWhile(() => targetShields[n].targetShieldAudio.isPlaying);
            if (targetSelected) yield break;
        }
    }

    void StopAllTargets()
    {
        StopCrowCoroutine();
    }

    IEnumerator StopCrowSoundsCoroutine()
    {
        foreach (var target in targetShields)
        {
            target.StopPlay();
            yield return null;
        }

        yield return null;
    }

    void CollectData()
    {
        
        Debug.Log("#2");
        if (GeneralDataManager.Instance)
        {
            _collectData.kidID = GeneralDataManager.Instance.KidID;
            _collectData.therapistID = GeneralDataManager.Instance.TherapistsID;
        }
        _collectData.levelData.Add(_levelData);

        Debug.Log(JsonUtility.ToJson(_collectData));

        Debug.Log("#3");
        GeneralDataManager.Instance.SaveDataToServer<SeekAndHideData>(_collectData);
        Debug.Log("#4");
        answersNr = 1;
        if (_numberIndex == 15)
        {
            desc += " with noise";
            StartCoroutine(PlayWelcomeAudio2());
            correctAnswers = 0;
        }
        else if (_numberIndex == 30)
        {
            Debug.Log("#1");
            _levelData.endDate = TheraplyHelpers.DateTimeNowToString();
            _levelData.level = -1;
            _levelData.properAnswers = correctAnswers;
            _levelData.answersCount = answersNr;
            _levelData.bestTime = answerTimes.Min() * 0.001f;
            _levelData.averageTime = answerTimes.Average() * 0.001f;
            _levelData.description = desc;
            Debug.Log("#5");
            var gameTime = DateTimeOffset.Parse(_levelData.endDate).UtcDateTime -
                           DateTimeOffset.Parse(_levelData.startDate).UtcDateTime;
        
            Debug.Log("#6");
            HidingGameCommunication pc = FindFirstObjectByType<HidingGameCommunication>();
            pc.SetData((double) gameTime.Seconds, _levelData.answersCount - _levelData.properAnswers);
            Debug.Log("#7");
            hidingGameCommunication.OnGameFinished("GameFinished:Zabawa w chowanego - " + _levelData.description);
            
            StartCoroutine(EndTest());
        }
        
        //
    }
    
    

    public void StopCuckooCoroutine()
    {
        StopAllTargets();
        //StopCoroutine(cuckooCoroutine);
    }

    void StartStopwatch()
    {
        stopwatch = new Stopwatch();
        stopwatch.Start();
    }

    public void StopStopwatch()
    {
        stopwatch.Stop();
        //stopwatch.Reset();
    }

    float GetStopwatchTime()
    {
        StopStopwatch();
        float t = stopwatch.ElapsedMilliseconds;
        stopwatch.Reset();
        return t;
    }
    public void CorrectAnswer()
    {
        StopAllTargets();
        StartCoroutine(CorrectAnswerCoroutine());
    }

    IEnumerator CorrectAnswerCoroutine()
    {
        StopCrowCoroutine();
        correctAnswers++;
        Debug.Log("Correct Answer");
        answerTimes.Add(GetStopwatchTime());

        //yield return new WaitWhile(()=> audioSource.isPlaying);
        yield return new WaitForSeconds(1.5f);

        //isBusy = false;
        //isBusy moved to AnimateButton
        canAnimate = true;
        AnimateButton();

        if (_numberIndex == 15 || _numberIndex == 30)
        {
            CollectData();
        }
        else
        {
            _btnPressed = false;            
        }
    }

    IEnumerator EndTest()
    {
        
        Debug.Log("Koniec");
        StopAnimateButton();
        StartCoroutine(VirtualFriend.Instance.FriendTalking("Thanks"));
        yield return new WaitUntil(() => VirtualFriend.Instance.IsTalking);

        yield return null;
        //CollectData();
        yield return new WaitForSeconds(1);
        RestartScene();
        /*
        var screenFade = FindObjectOfType<OVRScreenFade>();
        yield return new WaitForSeconds(4);
        if (screenFade != null)
        {
            screenFade.FadeOut();
            var t = screenFade.fadeTime + 0.5f;
            yield return new WaitForSeconds(t);
            SceneManager.LoadScene(0);
        }
        else
        {
            SceneManager.LoadScene(0);
        }
        */
    }
    
    public void RestartScene()
    {
        StartCoroutine(RestartSceneCoroutine());
    }

    IEnumerator RestartSceneCoroutine()
    {
        //CollectData();
        OVRScreenFade.instance.FadeOut();
        yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
        SceneManager.LoadScene("chowany_wersja_po_kole");
    }

    public void WrongAnswer()
    {
        StopAllTargets();
        StartCoroutine(WrongAnswerCoroutine());
    }
    IEnumerator WrongAnswerCoroutine()
    {
        answerTimes.Add(GetStopwatchTime());
        Debug.Log("Wrong Answer");
        targetShields[targetIndex].ShowAnswer();

        if (_numberIndex == 15 || _numberIndex == 30)
        {
            CollectData();
        }
        else
        {
            _btnPressed = false;
        }
        
        yield return null;
    }

    public void AnimateButton()
    {
        //startPGB.Unlock();
        //startBtn.GetComponent<Rigidbody>().isKinematic = false;
        _btnCoroutine = StartCoroutine(AnimateButtonCoroutine());
    }

    IEnumerator AnimateButtonCoroutine()
    {
        isBusy = false;

        isPressed = false;

        while (canAnimate)
        {

            float time = 0;
            float value = 0;
            while (time < 1)
            {
                value = Mathf.Lerp(0, 1, time / 0.5f);
                time += Time.deltaTime;
                startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", value);
                yield return null;
            }

            startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", 1);
            time = 0;

            while (time < 1)
            {
                value = Mathf.Lerp(0, 1, time / 0.5f);
                time += Time.deltaTime;
                startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", value);
                yield return null;
            }

            startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", 0);
        }
    }

    void StopAnimateButton()
    {
        _btnPressed = true;
        if (_btnCoroutine != null)
            StopCoroutine(_btnCoroutine);
        startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", 0);
        //startBtn.GetComponent<Rigidbody>().isKinematic = true;
    }

    void ClearTargetShields()
    {
        foreach (TargetShield shield in targetShields)
        {
            shield.OnStopTargeting();
        }
    }

    public void SetPreTest()
    {
        actualState = testState.PreTest;
        desc = "Pre Test";
    }

    public void SetPostTest()
    {
        actualState = testState.PostTest;
        desc = "Post Test";
    }
}

public enum testState
{
    PreTest,
    PostTest
}


