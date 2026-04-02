using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Autohand;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

[RequireComponent(typeof(AudioSource))]
public class CodingMasterGameManager : MonoBehaviour
{
    public static CodingMasterGameManager Instance;
    public GameObject player;
    public AutoHandPlayer autoHandPlayer;
    private TubularBellController _tubularBellController;
    private NumbersController _coconutController;
    private MorseRadioController _morseRadioController;

    public GameObject morseLookAtDummy;
    public GameObject numbersLookAtDummy;
    public GameObject pianoLookAtDummy;

    public float morsePause = 0.5f;
    public float numbersPause = 2.0f;
    public float pianoPause = 0.5f;
    public float timeBetweenTask = 1f;

    public bool canInteract = false;

    public Game currentGame;
    public Level currentLevelIndex = Level.Poziom1;

    public GameObject stats;

    [SerializeField] private AudioSource _audioSource;
    public AudioClip morseWelcomeVoice;
    public AudioClip numberWelcomeVoice;
    public AudioClip pianoWelcomeVoice;
    public AudioClip morseGameOverVoice;
    public AudioClip numberGameOverVoice;
    public AudioClip pianoGameOverVoice;
    public List<AudioClip> numbersAudioClips;
    public List<AudioClip> pianoAudioClips;
    public List<AudioClip> morseAudioClips;
    // public List<AudioClip> congratAudioClips;
    public List<string> congratsAudioKeys;
    public List<AudioClip> soundCountersClips;
    public List<String> soundCountersKeys;

    public List<AudioClip> numberCountersClips;
    public AudioClip correctAnswerSound;
    public AudioClip badAnswerSound;
    public AudioClip nextLevelSound;
    public AudioClip prevLevelSound;
    private byte attempt = 0;
    private string answer;
    private int answerIndex = 0;
    public int[] answers = new int[3] { 0, 0, 0 };
    public int actualQuestion = 1;
    private bool canAnswer = false;
    private bool isGenerating = false;
    private string enteredAnswer = "";
    public bool haveBadAnswer = false;
    private int badAnswersInRow = 0;
    private int gridIndex = 0;
    private int pointsIndex = 0;
    private Level morseLevel = Level.Poziom1;
    private Level numberLevel = Level.Poziom1;
    private Level pianoLevel = Level.Poziom1;
    private List<float> _answerTimes;
    private List<byte> _randomMorseList;
    private List<byte> _randomNumbersList;
    private List<byte> _randomPianoList;
    private List<byte> _randomCongratsList;
    private Stopwatch stopwatch;
    private Stopwatch _timer;

    private bool firstRun = true;

    public byte maxWrongRounds = 3;
    public byte tryCount = 5;
    public byte wrongRounds = 0;
    public byte correctRounds = 0;

    private CodingMasterData _codingMasterData;
    private CodingMasterLevelData _levelData;

    public CodingGameCommunication codingGameCommunication;

    private int _startingLevel = 0;
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(transform.gameObject);
        }

        _audioSource = GetComponent<AudioSource>();
    }

    IEnumerator Start()
    {
        // VirtualFriend.Instance.transform.LookAt(player.transform, Vector3.up);
    
        _tubularBellController = TubularBellController.Instance;
        _coconutController = NumbersController.Instance;
        _morseRadioController = MorseRadioController.Instance;

        _randomCongratsList = new List<byte>();
        _randomMorseList = new List<byte>();
        _randomNumbersList = new List<byte>();
        _randomPianoList = new List<byte>();

        _randomCongratsList = TheraplyHelpers.GenerateNumbers(0, congratsAudioKeys.Count - 1);
        _randomMorseList = TheraplyHelpers.GenerateNumbers(0, 2, true);
        _randomNumbersList = TheraplyHelpers.GenerateNumbers(0, 9);
        _randomPianoList = TheraplyHelpers.GenerateNumbers(0, 7);

        _codingMasterData = new CodingMasterData();
        _codingMasterData.levelData = new List<CodingMasterLevelData>();

        yield return new WaitForSeconds(3.0f);
        
        StartCoroutine(PlayWelcomeAudio());
    }

    IEnumerator PlayWelcomeAudio()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("WelcomeAudio")));
        //stats.SetActive(true);
    }

    public void CheckAnswer(string key)
    {
        if (canInteract == false) return;
        enteredAnswer += key;
        if (enteredAnswer.Length == GetNumberOfDigits())
        {
            canInteract = false;

            attempt++;
            if (enteredAnswer == answer)
            {
                Debug.Log("CORRECT: \nenteredAnswer: " + enteredAnswer + " vs answer: " + answer);
                CorrectAnswer();
            }
            else
            {
                Debug.Log("WRONG: \nenteredAnswer: " + enteredAnswer + " vs answer: " + answer);
                haveBadAnswer = true;
                BadAnswer(true);
            }
            if (haveBadAnswer) answers[actualQuestion - 1] = 1;
            else answers[actualQuestion - 1] = 0;

            if (actualQuestion < 4)
                actualQuestion++;

            answer = "";
            enteredAnswer = "";
            answerIndex = 0;
            haveBadAnswer = false;
        }
    }
  
    public void PlayMorseSound(int i)
    {
        _audioSource.clip = morseAudioClips[i];

        //numbersKeyboard.LigthUpButton(int.Parse(n));

        _audioSource.Play();
    }

    public void PlayKeyboardSound(int i)
    {
        _coconutController.PlaySound(i);
    }

    public void ChangeToMorse()
    {
        currentGame = Game.Morse;
        string saveKey = GeneralDataManager.Instance.KidID + "_" + currentGame.ToString() + "CurrentLevel";
        Debug.Log("loaded savekey: " + saveKey + PlayerPrefs.GetInt(saveKey));
        
        _startingLevel = PlayerPrefs.GetInt(saveKey);
        if (_startingLevel > 0)
            _startingLevel--;
        
        morseLevel = (Level)_startingLevel;
        Debug.Log(morseLevel);
        //StopAllCoroutines();
        StartCoroutine(ChangeToMorseCoroutine());
        _answerTimes = new List<float>();
        
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        _timer = new Stopwatch();
        _timer.Start();
    }

    IEnumerator ChangeToMorseCoroutine()
    {
        yield return null;
        VirtualFriend.Instance.Teleport(morseLookAtDummy.transform);

        canInteract = false;
        answers = new[] { 0, 0, 0 };
        currentLevelIndex = morseLevel;

        _morseRadioController.SetKinematic(true);
        SaveLevelForGame();
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("MorseWelcome")));

        isGenerating = false;
        currentGame = Game.Morse;



        currentLevelIndex = morseLevel;
        Debug.Log("ten kasuje");
        _morseRadioController.ResetRadio();

        answer = "";
        answerIndex = 0;
        answers = new int[3] { 0, 0, 0 };
        actualQuestion = 1;
        canAnswer = false;
        isGenerating = false;
        enteredAnswer = "";
        haveBadAnswer = false;
        badAnswersInRow = 0;
        gridIndex = 0;
        pointsIndex = 0;
        SetLevel(true);
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
    }

    public void ChangeToNumbers()
    {
        currentGame = Game.Numbers;
        string saveKey = GeneralDataManager.Instance.KidID + "_" + currentGame.ToString() + "CurrentLevel";
        
        _startingLevel = PlayerPrefs.GetInt(saveKey);
        if (_startingLevel > 0)
            _startingLevel--;
        
        numberLevel = (Level)_startingLevel;
        
        Debug.Log(numberLevel);
        
        StartCoroutine(ChangeToNumbersCoroutine());
        _answerTimes = new List<float>();
        
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        _timer = new Stopwatch();
        _timer.Start();
    }

    IEnumerator ChangeToNumbersCoroutine()
    {
        yield return null;
        VirtualFriend.Instance.Teleport(numbersLookAtDummy.transform);

        _coconutController.DisableCollisions();

        canInteract = false;
        answers = new[] { 0, 0, 0 };
        currentLevelIndex = numberLevel;

        SaveLevelForGame();
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Numbers_Welcome")));

        isGenerating = false;
        currentGame = Game.Numbers;

        answer = "";
        answerIndex = 0;
        answers = new int[3] { 0, 0, 0 };
        actualQuestion = 1;
        canAnswer = false;
        isGenerating = false;
        enteredAnswer = "";
        haveBadAnswer = false;
        badAnswersInRow = 0;
        gridIndex = 0;
        pointsIndex = 0;
        SetLevel(true);
        
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
    }

    public void ChangeToPiano()
    {
        currentGame = Game.Piano;
        string saveKey = GeneralDataManager.Instance.KidID + "_" + currentGame.ToString() + "CurrentLevel";
        //string saveKey = "test_" + currentGame.ToString() + "CurrentLevel";
        
        _startingLevel = PlayerPrefs.GetInt(saveKey);
        if (_startingLevel > 0)
            _startingLevel--;
        
        pianoLevel = (Level)_startingLevel;
        
        Debug.Log(pianoLevel);
        //StopAllCoroutines();
        StartCoroutine(ChangeToPianoCoroutine());
        _answerTimes = new List<float>();
        
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        _timer = new Stopwatch();
        _timer.Start();
    }

    IEnumerator ChangeToPianoCoroutine()
    {
        yield return null;
        VirtualFriend.Instance.Teleport(pianoLookAtDummy.transform);

        canInteract = false;
        answers = new[] { 0, 0, 0 };

        _tubularBellController.SetHeight();

        SaveLevelForGame();

        isGenerating = false;
        currentGame = Game.Piano;
        currentLevelIndex = pianoLevel;

        Debug.Log("powinno być o pałce");
        //StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_Welcome"));
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_Welcome")));
        yield return null;

        answer = "";
        answerIndex = 0;
        answers = new int[3] { 0, 0, 0 };
        actualQuestion = 1;
        canAnswer = false;
        isGenerating = false;
        enteredAnswer = "";
        haveBadAnswer = false;
        badAnswersInRow = 0;
        gridIndex = 0;
        pointsIndex = 0;
        SetLevel(true);
        yield return null;
        
        _levelData = new CodingMasterLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
    }



    IEnumerator PlaySequenceCoroutine(bool newInvoke = false)
    {
        //Debug.Log("granie ile będzie sygnałów");

        yield return new WaitForSeconds(timeBetweenTask);

        if (currentGame == Game.Piano)
        {
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
            yield return new WaitWhile(() => _tubularBellController.endAnimateShells == false);
            //Debug.Log(_tubularBellController.seashellIndex.ToString());
            if (_tubularBellController.seashellIndex == 3)
            {
                _tubularBellController.ResetTubularBells();
            }
        }

        if (currentGame == Game.Morse)
        {
            if (firstRun)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("MorseInfo")));
                firstRun = false;
            }
            if (actualQuestion == 1)
            {
                if (currentLevelIndex == Level.Poziom1)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("2Sounds")));
                }
                else if (currentLevelIndex == Level.Poziom2)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("3Sounds")));
                }
                else if (currentLevelIndex == Level.Poziom3)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("4Sounds")));
                }
                else if (currentLevelIndex == Level.Poziom4)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("5Sounds")));
                }
                else if (currentLevelIndex == Level.Poziom5)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("6Sounds")));
                }
                else if (currentLevelIndex == Level.Poziom6)
                {
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("7Sounds")));
                }
            }
        }
        else if (currentGame == Game.Piano)
        {
            if (currentLevelIndex == Level.Poziom1)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("2Sounds")));
            }
            else if (currentLevelIndex == Level.Poziom2)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("3Sounds")));
            }
            else if (currentLevelIndex == Level.Poziom3)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("4Sounds")));
            }
            else if (currentLevelIndex == Level.Poziom4)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("5Sounds")));
            }
            else if (currentLevelIndex == Level.Poziom5)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("6Sounds")));
            }
            else if (currentLevelIndex == Level.Poziom6)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("7Sounds")));
            }
        }

        else if (currentGame == Game.Numbers)
        {
            if (firstRun)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("CoconutInfo")));
                firstRun = false;
            }
            if (currentLevelIndex == Level.Poziom1)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("2Numbers")));
            }
            else if (currentLevelIndex == Level.Poziom2)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("3Numbers")));
            }
            else if (currentLevelIndex == Level.Poziom3)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("4Numbers")));
            }
            else if (currentLevelIndex == Level.Poziom4)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("5Numbers")));
            }
            else if (currentLevelIndex == Level.Poziom5)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("6Numbers")));
            }
            else if (currentLevelIndex == Level.Poziom6)
            {
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("7Numbers")));
            }

        }

        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        answer = "";

        //Game = Numbers
        if (currentGame == Game.Numbers)
        {
            StartCoroutine(GenerateNumberSoundsCoroutine(newInvoke));
        }

        //Game = Piano
        else if (currentGame == Game.Piano)
        {
            _tubularBellController.ResetTubularBellsColors();
            StartCoroutine(GeneratePianoSoundsCoroutine(newInvoke));
        }

        //Game = Morse
        else if (currentGame == Game.Morse)
        {
            _morseRadioController.SetKinematic(true);
            StartCoroutine(GenerateMorseSoundsCoroutine(newInvoke));
        }

        yield return null;
    }

    IEnumerator GenerateNumberSoundsCoroutine(bool newInvoke = false)
    {
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return null;
        Debug.Log("odpalone losowanie dla " + GetNumberOfDigits());
        //yield return null;
        if (isGenerating) yield break;

        isGenerating = true;

        answer = "";
        string n;

        _audioSource.Stop();
        for (int i = 0; i < GetNumberOfDigits(); i++)
        {
            if (i == 0)
            {
                n = TheraplyHelpers.GetIntFromList(_randomNumbersList).ToString();
                answer = n;
            }
            else
            {
                n = TheraplyHelpers.GetIntFromList(_randomNumbersList).ToString();
                answer += n;
            }


            //var talking = 
            yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking(n)));

            _coconutController.PlaySound(int.Parse(n));

            yield return null;

            // while (_audioSource.isPlaying)
            // {
            //     yield return null;
            // }
            //yield return talking;

            if (i < GetNumberOfDigits() - 1)
                yield return new WaitForSeconds(numbersPause);
        }
        StartStopwatch();
        _coconutController.EnableCollisions();
        if (newInvoke)
        {
            Debug.Log("newInvoke");
            //_levelData = new CodingMasterLevelData();
            yield return null;
            //_levelData.startDate = TheraplyHelpers.DateTimeNowToString();
            //Debug.Log(_levelData.startDate);
        }
        isGenerating = false;
        canAnswer = true;
        canInteract = true;
    }

    IEnumerator GeneratePianoSoundsCoroutine(bool newInvoke = false)
    {
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return null;
        if (isGenerating) yield break;
        isGenerating = true;

        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        answer = "";
        string n;
        for (int i = 0; i < GetNumberOfDigits(); i++)
        {
            n = TheraplyHelpers.GetIntFromList(_randomPianoList).ToString();

            _tubularBellController.PlaySound(int.Parse(n));

            answer += n;
            yield return null;
            while (_tubularBellController.isPlaying)
            {
                yield return null;
            }

            if (i != GetNumberOfDigits() - 1)
                yield return new WaitForSeconds(pianoPause);
        }
        StartStopwatch();
        if (newInvoke)
        {
            Debug.Log("newInvoke");
            //_levelData = new CodingMasterLevelData();
            yield return null;
            //_levelData.startDate = TheraplyHelpers.DateTimeNowToString();
            //Debug.Log(_levelData.startDate);
        }
        isGenerating = false;
        canAnswer = true;
        canInteract = true;
    }

    int GetNumberOfDigits()
    {
        switch (currentLevelIndex)
        {
            case Level.Poziom1:
                return 2;
            case Level.Poziom2:
                return 3;
            case Level.Poziom3:
                return 4;
            case Level.Poziom4:
                return 5;
            case Level.Poziom5:
                return 6;
            case Level.Poziom6:
                return 7;
        }

        return 0;
    }

    IEnumerator GenerateMorseSoundsCoroutine(bool newInvoke = false)
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return null;
        if (isGenerating) yield break;
        isGenerating = true;

        answer = "";
        string n;

        for (int i = 0; i < GetNumberOfDigits(); i++)
        {
            int v = TheraplyHelpers.GetIntFromList(_randomMorseList);

            n = (v % 2 == 0) ? "0" : "1";

            _audioSource.clip = morseAudioClips[int.Parse(n)];

            _audioSource.Play();

            _morseRadioController.PlaySound(int.Parse(n));

            answer += n;
            yield return null;
            while (_audioSource.isPlaying)
            {
                yield return null;
            }

            if (i != GetNumberOfDigits() - 1)
                yield return new WaitForSeconds(morsePause);
        }

        yield return new WaitForSeconds(0.5f);

        Debug.Log("prawidłowa odpowiedź: " + answer);
        StartStopwatch();
        if (newInvoke)
        {
            Debug.Log("newInvoke");
            //_levelData = new CodingMasterLevelData();
            yield return null;
            //_levelData.startDate = TheraplyHelpers.DateTimeNowToString();
            //Debug.Log(_levelData.startDate);
        }
        isGenerating = false;
        canAnswer = true;
        canInteract = true;

        if (currentGame == Game.Morse)
            _morseRadioController.SetKinematic(false);
        if (currentGame == Game.Numbers)
            _coconutController.EnableCollisions();
        if (currentGame == Game.Piano)
            _tubularBellController.EnableTubularBells();
    }

    void StartStopwatch()
    {
        stopwatch = new Stopwatch();
        stopwatch.Start();
    }

    float GetStopwatchTime()
    {
        stopwatch.Stop();
        float t = stopwatch.ElapsedMilliseconds;
        stopwatch.Reset();
        return t;
    }
    void SaveLevelForGame()
    {
        if (currentGame == Game.Morse)
            morseLevel = currentLevelIndex;
        else if (currentGame == Game.Numbers)
            numberLevel = currentLevelIndex;
        else if (currentGame == Game.Piano)
            pianoLevel = currentLevelIndex;
    }

    void SetLevel(bool newInvoke = false)
    {
        PlaySequence(newInvoke);
    }

    void PlaySequence(bool newInvoke)
    {
        StartCoroutine(PlaySequenceCoroutine(newInvoke));
    }

    void EndGame()
    {
        //StopAllCoroutines();

        if ((int)currentLevelIndex > 0)
            currentLevelIndex--;

        StartCoroutine(EndGameCoroutine());
    }

    IEnumerator EndGameCoroutine()
    {
        yield return new WaitForSeconds(3);

        SceneManager.LoadScene(0);
    }

    void CorrectAnswer()
    {
        StartCoroutine(CorrectAnswerCoroutine());
    }

    IEnumerator CorrectAnswerCoroutine()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return null;

        while (_audioSource.isPlaying)
        {
            yield return null;
        }

        canAnswer = false;
        answerIndex = 0;

        //if (!haveBadAnswer)//if (IsGoodAnswer())
        if (answers.Sum() == 0)
        {
            Debug.Log("Correct answer");
            _answerTimes.Add(GetStopwatchTime());

            //_audioSource.clip = correctAnswerSound;
            //_audioSource.Play();

            yield return new WaitWhile(() => _audioSource.isPlaying);

            if (answers.Sum() == 0)
            {
                //if (playOnEachAnswer)
                {
                    if (currentGame == Game.Piano)
                    {
                        yield return new WaitWhile(() => _tubularBellController.gameObject.GetComponent<AudioSource>().isPlaying);
                    }

                    //Debug.Log("Dobra odpowiedź");

                    if (currentGame == Game.Morse)
                    {
                        if (actualQuestion == 2)
                        {
                            yield return (StartCoroutine(
                                VirtualFriend.Instance.FriendTalking(
                                    congratsAudioKeys[Random.Range(0, congratsAudioKeys.Count - 1)])));
                        }

                    }
                    else
                    {
                        yield return (StartCoroutine(
                            VirtualFriend.Instance.FriendTalking(
                                congratsAudioKeys[Random.Range(0, congratsAudioKeys.Count - 1)])));
                    }

                    if (currentGame == Game.Morse)
                    {
                        _morseRadioController.Answer(true);
                    }
                    else if (currentGame == Game.Numbers)
                    {
                        _coconutController.Answer(true);
                    }
                    else if (currentGame == Game.Piano)
                    {
                        _tubularBellController.Answer(true);
                    }

                    //yield return talking;
                }
            }
        }
        else
        {
            if (currentGame == Game.Morse)
            {
                _morseRadioController.Answer(answers[2] != 1);
            }
            else if (currentGame == Game.Numbers)
            {
                _coconutController.Answer(answers[2] != 1);
            }
            else if (currentGame == Game.Piano)
            {
                _tubularBellController.Answer(answers[2] != 1);
            }
        }

        AddPoint();
        enteredAnswer = "";
        answer = "";
    }

    void BadAnswer(bool end)
    {
        StartCoroutine(BadAnswerCoroutine(end));
    }

    bool IsGoodAnswer()
    {
        Debug.Log("actualQuestion " + actualQuestion);
        if (answers[actualQuestion - 2] == 1) return false;
        else return true;
    }

    IEnumerator BadAnswerCoroutine(bool end)
    {
        yield return null;
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        while (_audioSource.isPlaying)
        {
            yield return null;
        }

        haveBadAnswer = true;

        //_audioSource.clip = badAnswerSound;
        //_audioSource.Play();

        if (end)
        {
            canAnswer = false;
            answerIndex = 0;

            if (currentGame == Game.Morse)
            {
                Debug.Log("bad answer from morse");
                _morseRadioController.Answer(false);
            }
            else if (currentGame == Game.Numbers)
            {
                _coconutController.Answer(false);
            }
            else if (currentGame == Game.Piano)
            {
                _tubularBellController.Answer(false);
            }

            //Debug.Log("Bad answer, the correct answer is: " + answer + " but you've entered: " + enteredAnswer);

            SubtractPoint();
            enteredAnswer = "";
            answer = "";
        }

    }

    void AddPoint()
    {
        UpdatePoints();
    }

    void SubtractPoint()
    {
        UpdatePoints();
    }

    void UpdatePoints()
    {
        StartCoroutine(UpdatePointsCoroutine());
    }

    IEnumerator MorseLevelUp()
    {
        // CollectData(); // wykomentowane przez kubę
        yield return null;
        attempt = 0;
        gridIndex = 0;
        currentLevelIndex++;
        pointsIndex = 0;
        _audioSource.clip = nextLevelSound;
        _audioSource.Play();
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("MorseNextLevel"));

        actualQuestion = 1;
        answerIndex = 0;
        gridIndex = 0;
        tryCount = 5;
        wrongRounds = 0;
        correctRounds = 0;
        answers = new[] { 0, 0, 0 };

        SetLevel(true);
        yield return new WaitForSeconds(1);
        _morseRadioController.ResetRadio();
        Debug.Log("MorseLevelUp - Congrats!!! Current Game Complited " + currentLevelIndex);
        
        if (currentLevelIndex == Level.Poziom6)
            CollectData();
        
        yield break;
    }

    IEnumerator UpdatePointsMorseCoroutine()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return null;
        int wrongAnswers = 0;

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == 1) wrongAnswers++;
        }

        haveBadAnswer = false;

        // end round
        if (actualQuestion == 3)
        {
            tryCount--;
            answer = "";

            //bad round
            if (wrongAnswers > 0)
            {
                wrongRounds++;
            }

            //good round
            if (wrongAnswers == 0)
            {
                if (tryCount == 4)
                {
                    StartCoroutine(MorseLevelUp());
                    yield break;
                }
                correctRounds++;
            }

            //level up
            if (correctRounds >= 3)
            {
                if ((int)currentLevelIndex < 7)
                {
                    StartCoroutine(MorseLevelUp());
                }
                else
                {
                    actualQuestion = 1;
                    answerIndex = 0;
                    gridIndex = 0;
                    answers = new[] { 0, 0, 0 };

                    SetLevel();
                    yield return new WaitForSeconds(1);
                    _morseRadioController.ResetRadio();
                    yield break;
                }
            }

            //end game
            if (wrongRounds >= 3)
            {
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_Over")));

                EndGame();

                yield break;
            }

            yield return new WaitForSeconds(1);

            _morseRadioController.ResetRadio();

            actualQuestion = 1;
            answerIndex = 0;
            gridIndex = 0;

            answers = new[] { 0, 0, 0 };
            yield return new WaitWhile(() => _audioSource.isPlaying);
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

            SetLevel();
        }
        else
        {
            SetLevel();
        }
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
        SceneManager.LoadScene("kodowanie");
    }
    
    IEnumerator UpdatePointsCoroutine()
    {
        canAnswer = false;
        canInteract = false;

        if (currentGame == Game.Morse)
            _morseRadioController.SetKinematic(true);
        if (currentGame == Game.Numbers)
            _coconutController.DisableCollisions();
        if (currentGame == Game.Piano)
            _tubularBellController.DisableTubularBells();

        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return null;
        int wrongAnswers = 0;

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == 1) wrongAnswers++;
        }

        haveBadAnswer = false;
        tryCount--;
        answer = "";

        if (actualQuestion == 4)
        {
            if (answers.Sum() == 0)
            {
                //Level up
                StartCoroutine(LevelUpCoroutine());
            }
            else if (wrongAnswers == maxWrongRounds)
            {
                //End game 
                //StartCoroutine(EndGamesCoroutine());
                //Changed to reset level
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("GoBack")));
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                CollectData();
                OVRScreenFade.instance.FadeOut();
                yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
                SceneManager.LoadScene("kodowanie");
            }
            else
            {
                wrongRounds++;
                //Retry
                if (wrongRounds == maxWrongRounds)
                {
                    //Final round
                    //End game
                    StartCoroutine(EndGamesCoroutine());
                }
                else
                {
                    //Retry
                    StartCoroutine(RetryCoroutine());
                }
            }
        }
        else
        {
            SetLevel();
        }
        //_levelData = new CodingMasterLevelData();
    }

    IEnumerator RetryCoroutine()
    {
        //yield return new WaitForSeconds(1);

        Debug.Log("retryCoroutine");

        if (currentGame == Game.Morse)
            _morseRadioController.ResetRadio();
        else if (currentGame == Game.Piano)
            _tubularBellController.ResetTubularBells();
        else if (currentGame == Game.Numbers)
            _coconutController.ResetStarfishs();

        actualQuestion = 1;
        answerIndex = 0;
        gridIndex = 0;

        answers = new[] { 0, 0, 0 };
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        SetLevel();
    }

    IEnumerator EndGamesCoroutine()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("GoBack")));
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        //CollectData();
        OVRScreenFade.instance.FadeOut();
        yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
        SceneManager.LoadScene("kodowanie");
        
        yield break;
    }

    IEnumerator LevelUpCoroutine()
    {
        // CollectData();// wykomentowane przez kubę
        yield return null;
        attempt = 0;
        gridIndex = 0;
        currentLevelIndex++;
        pointsIndex = 0;
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        _audioSource.clip = nextLevelSound;
        _audioSource.Play();
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        if (answers.Sum() == 0)
        {
            if (currentGame == Game.Morse)
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("MorseNextLevel"));
            else if (currentGame == Game.Numbers)
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_LevelCompleted"));
            else if (currentGame == Game.Piano)
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_LevelCompleted"));
        }

        actualQuestion = 1;
        answerIndex = 0;
        gridIndex = 0;
        tryCount = 5;
        wrongRounds = 0;
        correctRounds = 0;
        answers = new[] { 0, 0, 0 };

        SetLevel(true);
        yield return new WaitForSeconds(1);

        if (currentGame == Game.Morse)
            _morseRadioController.ResetRadio();
        else if (currentGame == Game.Piano)
            _tubularBellController.ResetTubularBells();
        else if (currentGame == Game.Numbers)
            _coconutController.ResetStarfishs();

        Debug.Log("LevelUpCoroutine - Congrats!!! Current Game Complited " + currentLevelIndex);
        if ((int)currentLevelIndex + 1 == 7)
        {
            CollectData();
            RestartScene();
        }
    }

    IEnumerator PianoLevelUp()
    {
        // CollectData();// wykomentowane przez kubę
        yield return null;
        attempt = 0;
        gridIndex = 0;
        currentLevelIndex++;
        pointsIndex = 0;
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        _audioSource.clip = nextLevelSound;
        _audioSource.Play();
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        if (answers.Sum() == 0)
        {
            yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Piano_LevelCompleted"));
        }

        actualQuestion = 1;
        answerIndex = 0;
        gridIndex = 0;
        tryCount = 5;
        wrongRounds = 0;
        correctRounds = 0;
        answers = new[] { 0, 0, 0 };

        SetLevel(true);
        yield return new WaitForSeconds(1);
        Debug.Log("PianoLevelUp - Congrats!!! Current Game Complited " + currentLevelIndex);
        yield break;
    }

    IEnumerator UpdatePointsPianoCoroutine()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return null;
        int wrongAnswers = 0;

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == 1) wrongAnswers++;
        }

        haveBadAnswer = false;

        // end round
        if (actualQuestion == 4)
        {
            tryCount--;
            answer = "";

            //bad round
            if (wrongAnswers > 0)
            {
                wrongRounds++;
            }
            if (wrongAnswers == 3)
            {
                if (tryCount == 4)
                {
                    StartCoroutine(PianoLevelUp());
                    yield break;
                }
                correctRounds++;
            }
            //good round
            if (wrongAnswers == 0)
            {
                //if (tryCount == 4)
                {
                    StartCoroutine(PianoLevelUp());
                    yield break;
                }
            }

            //level up
            if (correctRounds >= 3)
            {
                if ((int)currentLevelIndex < 7)
                {
                    StartCoroutine(PianoLevelUp());
                }
                else
                {
                    actualQuestion = 1;
                    answerIndex = 0;
                    gridIndex = 0;
                    answers = new[] { 0, 0, 0 };

                    SetLevel();
                    yield return new WaitForSeconds(1);
                    _tubularBellController.ResetTubularBells();
                    yield break;
                }
            }

            //end game
            if (wrongRounds >= maxWrongRounds)
            {

                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("GoBack")));
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
                CollectData();
                OVRScreenFade.instance.FadeOut();
                yield return new WaitForSeconds(OVRScreenFade.instance.fadeTime + 0.1f);
                SceneManager.LoadScene("kodowanie");
                
                yield break;
            }

            yield return new WaitForSeconds(1);

            _tubularBellController.ResetTubularBells();

            actualQuestion = 1;
            answerIndex = 0;
            gridIndex = 0;

            answers = new[] { 0, 0, 0 };
            yield return new WaitWhile(() => _audioSource.isPlaying);
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

            SetLevel();
        }
        else
        {
            SetLevel();
        }

    }
    IEnumerator PointsLevelUp()
    {
        // CollectData();// wykomentowane przez kubę
        yield return null;
        attempt = 0;
        gridIndex = 0;
        currentLevelIndex++;
        pointsIndex = 0;
        _audioSource.clip = nextLevelSound;
        _audioSource.Play();
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("MorseNextLevel"));

        actualQuestion = 1;
        answerIndex = 0;
        gridIndex = 0;
        tryCount = 5;
        wrongRounds = 0;
        correctRounds = 0;
        answers = new[] { 0, 0, 0 };

        SetLevel(true);
        yield return new WaitForSeconds(1);
        _coconutController.ResetStarfishs();
        Debug.Log("PointsLevelUp - Congrats!!! Current Game Complited " + currentLevelIndex);
        yield break;
    }
    IEnumerator UpdatePointsNumberCoroutine()
    {
        yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        yield return null;
        int wrongAnswers = 0;

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == 1) wrongAnswers++;
        }

        haveBadAnswer = false;

        // end round
        if (actualQuestion == 4)
        {
            tryCount--;
            answer = "";

            //bad round
            if (wrongAnswers > 0)
            {
                wrongRounds++;
            }

            //good round
            if (wrongAnswers == 0)
            {
                if (tryCount == 4)
                {
                    StartCoroutine(PointsLevelUp());
                    yield break;
                }
                correctRounds++;
            }

            //level up
            if (correctRounds >= 3)
            {
                if ((int)currentLevelIndex < 7)
                {
                    StartCoroutine(PointsLevelUp());
                }
                else
                {
                    actualQuestion = 1;
                    answerIndex = 0;
                    gridIndex = 0;
                    answers = new[] { 0, 0, 0 };

                    SetLevel();
                    yield return new WaitForSeconds(1);
                    _coconutController.ResetStarfishs();
                    yield break;
                }
            }

            //end game
            if (wrongRounds >= 3)
            {
                // CollectData();// wykomentowane przez kubę
 
                yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

                yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_Over")));

                EndGame();

                yield break;
            }

            yield return new WaitForSeconds(1);

            _coconutController.ResetStarfishs();

            actualQuestion = 1;
            answerIndex = 0;
            gridIndex = 0;

            answers = new[] { 0, 0, 0 };
            yield return new WaitWhile(() => _audioSource.isPlaying);
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);

            SetLevel();
        }
        else
        {
            SetLevel();
        }
    }
    public void TeleportPlayerTo(GameObject dummy)
    {
        autoHandPlayer.SetPosition(dummy.transform.position);

        Vector3 relativePos = dummy.transform.GetChild(0).gameObject.transform.position - dummy.transform.position;
        autoHandPlayer.SetRotation(Quaternion.LookRotation(relativePos, Vector3.up));
    }
    byte GetCurrentLevelFromCurrentGame()
    {
        if (currentGame == Game.Morse)
        {
            morseLevel = currentLevelIndex;
            return (byte)morseLevel;
        }
        if (currentGame == Game.Numbers)
        {
            numberLevel = currentLevelIndex;
            return (byte)numberLevel;
        }
        if (currentGame == Game.Piano)
        {
            pianoLevel = currentLevelIndex;
            return (byte)pianoLevel;
        }
        return 0;
    }

    string GetCompletionTime(string startDate, string endDate)
    {
        var parsedStartDate = DateTime.Parse(startDate);
        var parsedEndDate = DateTime.Parse(endDate);

        return parsedEndDate.Subtract(parsedStartDate).ToString();
    }

    void CollectData()
    {
        Debug.Log("#1");
        SaveData();
        Debug.Log("#2");
        //_levelData = new CodingMasterLevelData();
        _timer.Stop();
        Debug.Log("#3");
        _levelData.gameMode = currentGame;
        Debug.Log("#4");
        _levelData.endDate = TheraplyHelpers.DateTimeNowToString();
        Debug.Log("#5");
        _levelData.level = GetCurrentLevelFromCurrentGame();
        Debug.Log("#6");
        _levelData.levelCompletionTime = GetCompletionTime(_levelData.startDate, _levelData.endDate);
        Debug.Log("#7");
        _levelData.attempts = attempt;
        Debug.Log("#8");
        if (_answerTimes.Count > 0)
        {
            _levelData.bestTime = _answerTimes.Min() * 0.001f;
            Debug.Log("#9");
            _levelData.averageTime = _answerTimes.Average() * 0.001f;
        }

        Debug.Log("#10");
        if (GeneralDataManager.Instance)
        {
            Debug.Log("#11");
            _codingMasterData.kidID = GeneralDataManager.Instance.KidID;
            Debug.Log("#12");
            _codingMasterData.therapistID = GeneralDataManager.Instance.TherapistsID;
        }
        Debug.Log("#13");
        _codingMasterData.levelData.Add(_levelData);
        Debug.Log("#14");
        var gameTime = DateTimeOffset.Parse(_levelData.endDate).UtcDateTime -
                       DateTimeOffset.Parse(_levelData.startDate).UtcDateTime;
        Debug.Log("#15");
        CodingGameCommunication pc = FindFirstObjectByType<CodingGameCommunication>();
        Debug.Log("#16");
        pc.SetData(_timer.Elapsed.TotalSeconds, _levelData.level, GetCurrentLevelFromCurrentGame() - _startingLevel);
        Debug.Log("#17");
        pc.OnGameFinished("GameFinished:Kodowanie");
        Debug.Log("#18");
        Debug.Log(JsonUtility.ToJson(_codingMasterData));
        _timer.Reset();
        Debug.Log("#19");
        GeneralDataManager.Instance.SaveDataToServer<CodingMasterData>(_codingMasterData);
        Debug.Log("#20");
    }

    void SaveData()
    {
        string saveKey = GeneralDataManager.Instance.KidID + "_" + currentGame.ToString() + "CurrentLevel";
        //Debug.Log("savekey: " + saveKey);
        PlayerPrefs.SetInt(saveKey, (int)currentLevelIndex);
        
        /*
        string bestTimeKey = GeneralDataManager.Instance.KidID + "_" + currentGame.ToString() + "BestTime";
        if (_answerTimes.Count > 0)
        {
            PlayerPrefs.SetFloat(bestTimeKey ,_answerTimes.Min());
        }
        */

        PlayerPrefs.Save();
        //Debug.Log("savekey after save: " + PlayerPrefs.GetInt(saveKey));
    }
    
    private void OnApplicationQuit()
    {
        Debug.Log("Save on Exit");
        PlayerPrefs.Save();
    }

    private void OnDisable()
    {
        Debug.Log("Save on Exit");
        PlayerPrefs.Save();
    }

}

[Serializable]
public class CodingMasterData
{
    public string kidID;
    public string therapistID;
    public List<CodingMasterLevelData> levelData;
}

[Serializable]
public class CodingMasterLevelData
{
    public Game gameMode;
    public string startDate;
    public string endDate;
    public string levelCompletionTime;
    public byte level;
    public byte attempts;
    public float bestTime;
    public float averageTime;
}
