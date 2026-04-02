using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autohand;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;


[RequireComponent(typeof(AudioSource))]
public class HideAndSeekGameController : MonoBehaviour
{
    public static HideAndSeekGameController Instance;
    public bool canEnableMenuButtons = false;
    public GameObject cuckooPrefab;
    public GameObject puppetPrefab;
    public AudioSource audioSource;
    public int actualStage = 0;
    private float timeBetweenAttempts = 1f;
    public int roundIndex = 0;
    public bool isBusy = false;
    public bool toNextRound = false;
    private Stopwatch stopwatch;

    public GameObject stagesGameObject;
    public GameObject stagesPositionDummy;
    public GameObject testGameObject;
    public GameObject testPositionDummy;
    public GameObject uiForStages;
    public List<GameObject> stagesList;
    public List<HideAndSeekTree> treesOnStage;

    public List<byte> randomNumbers;

    public int actualTree = -1;
    public float remainingTime = 15f;

    public UnityEvent OnCorrectAnswer;
    public UnityEvent OnWrongAnswer;

    public Coroutine cuckooCoroutine;

    public AudioClip welcomeAudio;
    public AudioClip levelCompletedAudio;
    public AudioClip levelCompletedText;
    public AudioClip correctAnswerSound;
    public AudioClip wrongAnswerSound;
    public AudioClip gameOverAudio;
    public AudioClip pretestWithNoisesAudio;
    public List<AudioClip> congratAudioClips;


    //Stats
    public GameObject statsUI;
    public GameObject startBtn;
    public GameObject startBtnMain;
    public PhysicsGadgetButton startPGB;
    public TMP_Text statText;
    private int correctAnswers = 0;
    private int answersNr = 0;
    private float bestAnswerTime = 0;
    private float averageAnswerTime = 0;
    private List<float> answerTimes;
    public Coroutine buttonCoroutine;
    public Color btnColor;
    private bool btnPushed = false;

    private bool isGameStarted = false;
    private bool isPressed = false;
    private bool canAnimate = true;
    public bool treeSelected = false;
    private int oldTreeIndex = 0;
    public bool firstRun = true;
    private bool isInGame = false;
    public TMP_Text levelInfoText;
    private bool _lastAnswer = false;
    public byte badRoundsInRow = 0;

    private List<byte> _congratsRandomNumbers;
    private List<byte> _treesRandomNumbers;

    private Coroutine _roundCoroutine;

    private SeekAndHideData _seekAndHideData;
    private SeekAndHideLevelData _levelData;

    private bool realFirstRun = true;

    public HideAndSeekUImenu uiMenu;
    public GameObject captionLevelInfo;
    public GameObject actualLevelInfo;

    public HidingGameCommunication hidingGameCommunication;

    [Header("Noise cycle")]
    public AudioSource ambientNoise;       // podłącz AudioSource z obiektu AmbientNoise
    public string welcome2Key = "Welcome2Audio";

    private void Awake()
    {
        //StartCoroutine(TestTime());

        if (Instance == null) Instance = this;
        else
        {
            Destroy(this.gameObject);
        }

        LoadData();

        audioSource = GetComponent<AudioSource>();

        CollectStages();
        SetActiveStage(actualStage);
        uiForStages.SetActive(true);
        CollectTreesForStage();
        //AnimateButton();
        //ShowQuestionAndCaption(true);
    }

    IEnumerator TestTime()
    {
        var data1 = TheraplyHelpers.DateTimeNowToString();
        yield return new WaitForSeconds(3);
        var data2 = TheraplyHelpers.DateTimeNowToString();

        var d = DateTimeOffset.Parse(data2).UtcDateTime - DateTimeOffset.Parse(data1).UtcDateTime;

        Debug.Log(d.Hours + ":" + d.Minutes + ":" + d.Seconds + ":" + d.Milliseconds);
        Debug.Log(d.TotalMilliseconds.ToString());
    }

    void LoadData()
    {
        string saveKey = GeneralDataManager.Instance.KidID + "_HideAndSeekCurrentLevel";
        actualStage = PlayerPrefs.GetInt(saveKey);
        Debug.Log("Actual Stage:" + actualStage);
    }

    void CollectStages()
    {
        for (int i = 0; i < stagesGameObject.transform.childCount; i++)
        {
            var stage = stagesGameObject.transform.GetChild(i).gameObject;
            if (stage.name.Contains("Stage_"))
                stagesList.Add(stagesGameObject.transform.GetChild(i).gameObject);
        }
    }

    public void OnButtonPressed()
    {
        if (btnPushed) return;
        btnPushed = true;
        CollectTreesForStage();


        /*   
        if (realFirstRun)
        {
            StartCoroutine(PlayWelcomeAudio());
            realFirstRun = false;
            isGameStarted = false;
        }
        else
        {
            if (firstRun)
            {
                Debug.Log("powinno odpalić startLevel");
                firstRun = false;

                //StopAnimateButton();
                //
                StartLevel();
            }
            else
            {
                toNextRound = true;
            }
        }
        */
        Debug.Log("ButtonPressed");
        PushButton();
    }

    void SetActiveStage(int i)
    {

        foreach (var stage in stagesList)
        {
            stage.SetActive(false);
        }

        stagesList[i].SetActive(true);
        uiForStages.SetActive(true);

        if (i == 0)
        {
            levelInfoText.text = "Poziom I - 4 drzewa";
        }
        else if (i == 1)
        {
            levelInfoText.text = "Poziom II - 6 drzew";
        }
        else if (i == 2)
        {
            levelInfoText.text = "Poziom III - 8 drzew";
        }
        else if (i == 3)
        {
            levelInfoText.text = "Poziom IV - 12 drzew";
        }
    }

    void CollectTreesForStage()
    {
        Debug.Log("CollectTreesForStage");
        treesOnStage = new List<HideAndSeekTree>();
        for (int i = 0; i < stagesList[actualStage].transform.childCount; i++)
        {
            treesOnStage.Add(stagesList[actualStage].transform.GetChild(i).gameObject.GetComponent<HideAndSeekTree>());
        }
    }

    private void OnEnable()
    {
        canAnimate = true;
        isPressed = false;
        isBusy = true;

        _congratsRandomNumbers = new List<byte>();
        _congratsRandomNumbers = TheraplyHelpers.GenerateNumbers(0, congratAudioClips.Count - 1);
    }


    public void StartLevel()
    {
        //Debug.Log(firstRun);
        if (firstRun) return;

        canAnimate = false;
        //StopCoroutine(buttonCoroutine);
        StopAnimateButton();
        treeSelected = false;
        //level have 3 rounds
        //1#: 3 attempts with 3 cuckoo
        //2#: 3 attempts with 2 cuckoo
        //3#: 3 attempts with 1 cuckoo

        if (isGameStarted == false)
        {
            Debug.Log("new StartLevel");
            statText.gameObject.transform.parent.gameObject.SetActive(false);
            answerTimes = new List<float>();
            toNextRound = true;
            StartCoroutine(StartLevelCoroutine());
        }

        isGameStarted = true;
        isPressed = true;
    }

    IEnumerator PlayWelcomeAudio()
    {
        StopAnimateButton();
        yield return new WaitForSeconds(0.5f);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Welcome")));
        //yield return new WaitWhile(()=>VirtualFriend.Instance.IsTalking);
        //AnimateButton();
        EnableButton();
    }

    IEnumerator StartLevelCoroutine()
    {
        if (isInGame) yield break;
        _seekAndHideData = new SeekAndHideData();
        _levelData = new SeekAndHideLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        isInGame = true;

        SetActiveStage(actualStage);
        CollectTreesForStage();

        yield return null;

        randomNumbers = new List<byte>();
        randomNumbers = TheraplyHelpers.GenerateNumbers(0, treesOnStage.Count);

        byte gameCounts = 0;
        Debug.Log("___Before while");
        //while (correctAnswers < 6)
        {
            if (cuckooCoroutine != null) StopCuckooCoroutine();
            if (_roundCoroutine != null) StopCoroutine(_roundCoroutine);
            //Debug.Log("startLevelCoroutine in whileLoop");
            gameCounts++;

            if (gameCounts == 4)
            {
                audioSource.clip = gameOverAudio;
                audioSource.Play();
                yield return new WaitForSeconds(5);
                RestartScene();
            }

            //correctAnswers = 0;

            for (int i = 3; i > 0; i--)
            {
                yield return new WaitUntil(() => toNextRound);
                Debug.Log("Cuckoo nr: " + i);
                //Debug.Log("Next round");
                PlayRound(i);
            }
            yield return null;
        }

        if (actualStage < stagesList.Count)
        {
            if (correctAnswers < 6)
            {
                badRoundsInRow++;
            }

            isInGame = false;
        }

        if (badRoundsInRow == 2)
        {
            StartCoroutine(EndTest());
        }
        Debug.Log("Koniec levelu");
    }

    IEnumerator EndTest()
    {
        //yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return new WaitForSeconds(1);
        yield return new WaitWhile(() => virtualFriendIsBusy);
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Thanks")));

        yield return null;

        CollectData();
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

    void PlayRound(int cuckooSounds)
    {
        roundIndex++;
        _roundCoroutine = StartCoroutine(PlayRoundCoroutine(cuckooSounds));
    }

    IEnumerator PlayRoundCoroutine(int cuckooSounds)
    {
        _lastAnswer = false;
        toNextRound = false;
        //round have 3 attempts
        for (int i = 1; i < 4; i++)
        {
            isBusy = true;
            //start cuckoo
            yield return new WaitUntil(() => isPressed);
            cuckooCoroutine = StartCoroutine(CuckooCoroutine(cuckooSounds));
            yield return new WaitWhile(() => isBusy);
            yield return new WaitForSeconds(timeBetweenAttempts);
            //AnimateButton();
            if (i == 3)
                _lastAnswer = true;
        }
        Debug.Log("PlayRoundCoroutine - done");


        if (cuckooSounds == 1 && _lastAnswer)
        {
            Debug.Log("Ostatnia odpowiedz");
            Debug.Log("dobrych odpowiedzi: " + correctAnswers);

            isBusy = false;
            if (correctAnswers >= 6)
            {
                levelInfoText.text = "Brawo poziom " + (actualStage + 1) + " ukończony!";
                //ShowStats();
                CollectData();
                audioSource.clip = levelCompletedAudio;
                audioSource.Play();
                yield return new WaitWhile(() => audioSource.isPlaying);
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("LevelCompleted")));
                //yield return new WaitWhile(()=> VirtualFriend.Instance.IsTalking);
                correctAnswers = 0;

                //ShowUIMenu();
                NextLevel();
                ShowStartBtn();
                //actualStage++;
            }
        }

        if (roundIndex == 3)
        {
            isGameStarted = false;
            roundIndex = 0;
        }
        toNextRound = true;
        yield return null;
    }

    void ShowUIMenu()
    {
        uiMenu.gameObject.SetActive(true);

        if (actualStage < stagesList.Count - 1)
        {
            uiMenu.nextLevelObj.gameObject.SetActive(true);
        }
        else
        {
            uiMenu.nextLevelObj.gameObject.SetActive(false);
        }
    }

    public void NextLevel()
    {
        SaveData();
        actualStage++;
        if (actualStage >= stages.Length) return;   // if (actualStage == 4) return;
        SetActiveStage(actualStage);

        uiMenu.gameObject.SetActive(false);
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

    void ShowQuestionAndCaption(bool state)
    {
        SetActiveStage(actualStage);
        if (state == true)
        {
            captionLevelInfo.SetActive(true);
            actualLevelInfo.SetActive(true);
        }
        else
        {
            captionLevelInfo.SetActive(false);
            actualLevelInfo.SetActive(false);
        }
    }

    void SaveData(int stage = -1)
    {
        string saveKey = GeneralDataManager.Instance.KidID + "_HideAndSeekCurrentLevel";

        if (stage == -1)
            stage = actualStage;

        PlayerPrefs.SetInt(saveKey, stage);
        PlayerPrefs.Save();
        Debug.Log("Save data, actualLevel: " + actualStage);
    }

    IEnumerator CuckooCoroutine(int cuckooSounds)
    {
        Debug.Log("coś mnie triggeruje");
        //StopCoroutine(buttonCoroutine);

        //UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
        //actualTree =  UnityEngine.Random.Range(0, treesOnStage.Count - 1);

        actualTree = TheraplyHelpers.GetIntFromList(randomNumbers);
        Debug.Log("wylosowano: " + actualTree);

        oldTreeIndex = actualTree;

        answersNr++;
        isPressed = false;
        StartStopwatch();

        for (int i = 1; i <= cuckooSounds; i++)
        {
            ResetTrees();
            treesOnStage[actualTree].PlayCuckoo();
            yield return new WaitWhile(() => treesOnStage[actualTree].treeAudio.isPlaying);
        }

        yield return new WaitForSeconds(remainingTime);
        WrongAnswer();
    }

    public void StopCuckooCoroutine()
    {
        if (cuckooCoroutine != null)
            StopCoroutine(cuckooCoroutine);
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

    public void SetIsBusy(bool state)
    {
        isBusy = state;
    }

    public void CorrectAnswer()
    {
        StartCoroutine(CorrectAnswerCoroutine());
    }

    IEnumerator CorrectAnswerCoroutine()
    {
        correctAnswers++;
        Debug.Log("Correct Answer");
        answerTimes.Add(GetStopwatchTime());

        //Comment out issue: Po udanym trafieniu w grze “zabawa w chowanego” zlikwidowałabym ten dźwięk, który się za
        //                   każdym razem powtarza (taka jakby trąbka). W zupełności wystarczy, że wiewiórka mówi
        //                   “dobrze” i jest podświetlenie na zielono. Zrobi nam się wtedy gra bardziej przyjazna sensorycznie.
        //
        //audioSource.clip = correctAnswerSound;
        //audioSource.Play();
        //yield return new WaitForSeconds(1.5f);


        string key = congratAudioClips[TheraplyHelpers.GetIntFromList(_congratsRandomNumbers)].name;

        if (!_lastAnswer)
        {
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
            yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking(key)));
            //yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        }

        //isBusy = false;
        //isBusy moved to AnimateButton
        canAnimate = true;
        //EnableButton();
        //AnimateButton();
        _lastAnswer = false;
        timeForAnswer = false;

        yield return new WaitForSeconds(1);

        EnableButton();
    }

    public void WrongAnswer()
    {
        StartCoroutine(WrongAnswerCoroutine());
    }

    IEnumerator WrongAnswerCoroutine()
    {
        answerTimes.Add(GetStopwatchTime());
        Debug.Log("Wrong Answer");
        treesOnStage[actualTree].ShowAnswer();
        //OnWrongAnswer?.Invoke();
        audioSource.clip = wrongAnswerSound;
        audioSource.Play();
        yield return new WaitWhile(() => audioSource.isPlaying);
        yield return new WaitForSeconds(1);

        //isBusy = false;
        //isBusy moved to AnimateButton
        canAnimate = true;
        //EnableButton();
    }

    void ResetTrees()
    {
        treeSelected = false;
        foreach (var tree in treesOnStage)
        {
            tree.ColorizeTree(Color.white);
            tree.treeAudio.Stop();
        }
    }

    void CollectData()
    {
        _levelData.endDate = TheraplyHelpers.DateTimeNowToString();
        _levelData.level = actualStage;
        _levelData.properAnswers = correctAnswers;
        _levelData.answersCount = answersNr;
        _levelData.bestTime = answerTimes.Min() * 0.001f;//TheraplyHelpers.TimeSpanFromMiliseconds(answerTimes.Min());
        _levelData.averageTime = answerTimes.Average() * 0.001f;//TheraplyHelpers.TimeSpanFromMiliseconds(answerTimes.Average());
        if (GeneralDataManager.Instance)
        {
            _seekAndHideData.kidID = GeneralDataManager.Instance.KidID;
            _seekAndHideData.therapistID = GeneralDataManager.Instance.TherapistsID;
        }
        _seekAndHideData.levelData.Add(_levelData);
        Debug.Log(JsonUtility.ToJson(_seekAndHideData));
        SaveData(actualStage - 1);
        GeneralDataManager.Instance.SaveDataToServer<SeekAndHideData>(_seekAndHideData);
        ClearAnswers();

        var gameTime = DateTimeOffset.Parse(_levelData.endDate).UtcDateTime -
                       DateTimeOffset.Parse(_levelData.startDate).UtcDateTime;

        HidingGameCommunication pc = FindFirstObjectByType<HidingGameCommunication>();
        pc.SetData((double)gameTime.Seconds, _levelData.answersCount - _levelData.properAnswers);

        hidingGameCommunication.OnGameFinished("GameFinished:Zabawa w chowanego");

        firstLevelRun = true;
    }

    void ClearAnswers()
    {
        answerTimes = new List<float>();
        answersNr = 0;
        correctAnswers = 0;
    }

    void ShowStats()
    {
        /*
        Debug.Log("-------------------------------------------------------");
        Debug.Log("Actual level:" + actualStage);
        Debug.LogFormat("Correct answers: {0} / {1}", correctAnswers, answersNr.ToString());
        Debug.Log("Best time: " + answerTimes.Min() + "ms. or " + answerTimes.Min() *  0.001f + "sec.");
        Debug.Log("Average time: " + answerTimes.Average() + "ms. or " + answerTimes.Average() *  0.001f + "sec.");
        Debug.Log("-------------------------------------------------------");
        */

        string text = "Actual level: " + (actualStage) + "\n";
        text += "Correct answers: " + correctAnswers + " / " + answersNr + "\n";
        text += "Best time: " + answerTimes.Min() * 0.001f + "sec.\n";
        text += "Average time: " + answerTimes.Average() * 0.001f + "sec.";

        statText.gameObject.transform.parent.gameObject.SetActive(true);

        statText.text = text;
    }

    public void AnimateButton()
    {
        buttonCoroutine = StartCoroutine(AnimateButtonCoroutine());
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

    public void StopAnimateButton()
    {
        if (buttonCoroutine != null)
            StopCoroutine(buttonCoroutine);
        startBtn.GetComponent<MeshRenderer>().material.SetFloat("_ON", 0);
    }

    public void RunPretest()
    {
        testGameObject.SetActive(true);
        stagesGameObject.SetActive(false);
        VirtualFriend.Instance.Teleport(testPositionDummy.transform);
        testGameObject.GetComponent<TargetShieldsController>().SetPreTest();
    }

    public void RunGame()
    {
        testGameObject.SetActive(false);
        stagesGameObject.SetActive(true);
        ShowQuestionAndCaption(true);
        VirtualFriend.Instance.Teleport(stagesPositionDummy.transform);
    }

    public void RunPosttest()
    {
        testGameObject.SetActive(true);
        stagesGameObject.SetActive(false);

        testGameObject.GetComponent<TargetShieldsController>().SetPostTest();
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public GameObject[] stages;

    private byte minGoodAnswers = 6;       //Minimum of good answers to go to the next level
    private const byte _nrOfCuckooInRound = 3;    //Start decreasing number of cuckooSound played on each try in a round from 3 to 1
    public byte nrOfCuckooInRound = 3;
    private const byte _numberOfTriesInRound = 3; //Number of tries
    public byte numberOfTriesInRound = 3; //Number of tries
    private byte nuberOfRetries = 1;
    public bool timeForAnswer = false;
    private bool firstPushOfButton = true; //First push of button that execute WelcomeAudio
    private bool firstLevelRun = true;     //First push of button that execute PlayActualStage
    public bool virtualFriendIsBusy = false;

    void DisableButton()
    {
        Debug.Log("Disable button");
        //if (startBtn.GetComponent<PhysicsGadgetButton>())
        //startBtn.GetComponent<PhysicsGadgetButton>().enabled = false;
        StopAnimateButton();
    }

    public void EnableButton()
    {
        Debug.Log("Enable button");
        AnimateButton();

        //startBtn.GetComponent<PhysicsGadgetButton>().enabled = true;
        startPGB.Unlock();
        btnPushed = false;
    }

    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.L))
            PushButton();
        */
    }

    void PushButton()
    {
        Debug.Log("PushButton");
        DisableButton();
        if (firstPushOfButton)               //Play WelcomeAudio
        {
            firstPushOfButton = false;
            StartCoroutine(PlayWelcomeAudio());
        }
        else
        {
            Debug.Log("else");
            if (firstLevelRun)              //PlayActualStage
            {
                Debug.Log("firstRun");

                SetActualStage();
                ResetValues();
                CollectTreesForStage();

                firstLevelRun = false;
                PlayCuckoo();

                _seekAndHideData = new SeekAndHideData();
                _seekAndHideData.levelData = new List<SeekAndHideLevelData>();
                _levelData = new SeekAndHideLevelData();
                _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
            }
            else                            //Next try
            {
                PlayCuckoo();
            }
        }
    }

    void SetActualStage()
    {
        Debug.Log("SetActualStage");
        for (int i = 0; i < stages.Length; i++)
        {
            if (i == actualStage)
            {
                stages[i].SetActive(true);
            }
            else
            {
                stages[i].SetActive(false);
            }
        }

        answerTimes = new List<float>();
        randomNumbers = new List<byte>();
        randomNumbers = TheraplyHelpers.GenerateNumbers(0, treesOnStage.Count);
    }
    void ResetValues()
    {
        Debug.Log("ResetValues");
        nrOfCuckooInRound = _nrOfCuckooInRound;
        numberOfTriesInRound = _numberOfTriesInRound;
    }
    void PlayCuckoo()
    {
        ShowQuestionAndCaption(false);
        Debug.Log("play cuckoo");
        if (timeForAnswer) return;

        timeForAnswer = true;

        if (nrOfCuckooInRound != 0)
        {
            cuckooCoroutine = StartCoroutine(PlayCuckooCoroutine());
        }
    }

    public void UpdateStageCounter()
    {
        StartCoroutine(UpdateStageCounterCoroutine());
    }

    IEnumerator UpdateStageCounterCoroutine()
    {
        yield return null;
        if (numberOfTriesInRound == 0) //Decrease cuckoo count
        {
            nrOfCuckooInRound--;
            numberOfTriesInRound = _numberOfTriesInRound;
        }

        if (nrOfCuckooInRound == 0)
        {
            //End Stage
            Debug.Log("End of Stage");
            ResetValues();

            if (correctAnswers >= minGoodAnswers)
            {
                yield return new WaitWhile(() => virtualFriendIsBusy);

                //startBtn.gameObject.SetActive(false);
                startBtnMain.SetActive(false);

                if (actualStage < stages.Length)
                {
                    actualStage++;

                    firstLevelRun = true;
                }

                CollectData();

                _lastAnswer = true;
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                audioSource.clip = levelCompletedAudio;
                audioSource.Play();
                yield return new WaitWhile(() => audioSource.isPlaying);
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("LevelCompleted")));
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

                //ShowUIMenu();

                //NextLevel();
                ShowStartBtn();
            }
            else
            {
                if (nuberOfRetries > 0)
                {
                    DisableButton();
                    nuberOfRetries--;
                    firstLevelRun = true;
                    yield return new WaitForSeconds(3);
                    yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Retry")));
                    //yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                    ClearAnswers();
                }
                else
                {
                    StartCoroutine(EndTest());
                }
            }
        }
    }

    /*
    //nowy do testów -> dodany ambient noise
    IEnumerator UpdateStageCounterCoroutine()
    {
        yield return null;

        // Po 3 próbach w rundzie zmniejszamy liczbę kukulec i resetujemy liczbę prób
        if (numberOfTriesInRound == 0)
        {
            nrOfCuckooInRound--;
            numberOfTriesInRound = _numberOfTriesInRound;
        }

        // Koniec etapu, gdy wyczerpaliśmy wszystkie kukuu (3→2→1→0)
        if (nrOfCuckooInRound == 0)
        {
            Debug.Log("End of Stage");
            ResetValues(); // przygotuj wartości na kolejny etap (3 kukuu, 3 próby)

            if (correctAnswers >= minGoodAnswers)
            {
                // ########## ETAP ZALICZONY ##########
                // schowaj start i poczekaj aż VirtualFriend zwolni się
                startBtnMain.SetActive(false);
                yield return new WaitWhile(() => virtualFriendIsBusy);

                // przejście na kolejny etap + zapis danych
                if (actualStage < stages.Length)
                {
                    actualStage++;
                    firstLevelRun = true;
                }

                CollectData(); // używa correctAnswers/answersNr/answerTimes itd.

                _lastAnswer = true;

                // Sygnał dźwiękowy zaliczenia + komunikat przyjaciela
                if (levelCompletedAudio != null)
                {
                    audioSource.clip = levelCompletedAudio;
                    audioSource.Play();
                    yield return new WaitWhile(() => audioSource.isPlaying);
                }

                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("LevelCompleted"));
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

                // --- Specjalna sekwencja po Stage_4 (indeks 3 ukończony) ---
                int finishedStage = actualStage - 1;
                if (finishedStage == 3)
                {
                    // przerwa 1s → Welcome2Audio → uruchom Ambient → dopiero potem pokaż Start
                    startBtnMain.SetActive(false);
                    yield return new WaitForSeconds(1f);
                    yield return StartCoroutine(VirtualFriend.Instance.FriendTalking(welcome2Key));
                    if (ambientNoise && !ambientNoise.isPlaying)
                        ambientNoise.Play();
                }

                // Jeżeli to był ostatni etap (Stage_8 → index 7 po inkrementacji mamy == stages.Length)
                if (actualStage >= stages.Length)
                {
                    startBtnMain.SetActive(false); // koniec gry – nie pokazuj startu
                    yield break;
                }

                // standardowo pokaż przycisk start do kolejnego etapu
                ShowStartBtn();
            }
            else
            {
                // ########## ETAP NIEZALICZONY ##########
                if (nuberOfRetries > 0)
                {
                    // jedna powtórka całego etapu
                    //DisableButton();
                    nuberOfRetries--;
                    firstLevelRun = true;

                    yield return new WaitForSeconds(3);
                    yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                    yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Retry"));
                    ClearAnswers();          // zerowanie liczników/statystyk etapu

                    // po Retry pokaż od razu Start
                    ShowStartBtn();
                }
                else
                {
                    // brak retry – koniec testu
                    StartCoroutine(EndTest());
                }
            }
        }
    }
    */

    public void ShowStartBtn()
    {
        //ShowQuestionAndCaption(true);
        //startBtn.gameObject.SetActive(true);
        startBtnMain.SetActive(true);
        uiMenu.gameObject.SetActive(false);
    }

    IEnumerator PlayCuckooCoroutine()
    {
        if (nrOfCuckooInRound == 0) yield break;

        actualTree = TheraplyHelpers.GetIntFromList(randomNumbers);

        oldTreeIndex = actualTree;

        answersNr++;
        numberOfTriesInRound--;
        isPressed = false;
        StartStopwatch();

        for (int i = 1; i <= nrOfCuckooInRound; i++)
        {
            ResetTrees();
            treesOnStage[actualTree].PlayCuckoo();
            yield return new WaitWhile(() => treesOnStage[actualTree].treeAudio.isPlaying);
        }

        timeForAnswer = true;
        EnableTrees();

        yield return new WaitForSeconds(remainingTime);
        WrongAnswer();
    }

    public void DisableTrees()
    {
        foreach (var tree in treesOnStage)
        {
            tree.UIActivator.gameObject.SetActive(false);
        }
    }

    void EnableTrees()
    {
        foreach (var tree in treesOnStage)
        {
            tree.UIActivator.gameObject.SetActive(true);
        }
    }
















}

[Serializable]
public class SeekAndHideData
{
    public string kidID;
    public string therapistID;
    public List<SeekAndHideLevelData> levelData;
}

[Serializable]
public class SeekAndHideLevelData
{
    public string startDate;
    public string endDate;
    public int level;
    public int properAnswers;
    public int answersCount;
    public float bestTime;
    public float averageTime;
    public string description;
}
