using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public enum Level
{
    Poziom1,
    Poziom2,
    Poziom3,
    Poziom4,
    Poziom5,
    Poziom6
}

public enum Game
{
    Numbers,
    Piano,
    Morse
}

public class CodingMasterGameManagerOld : MonoBehaviour
{
    public static CodingMasterGameManagerOld Instance;
    public bool showCards = true;
    public float pauseTime = 2f;
    public float morsePause = 0.5f;
    public float numbersPause = 2.0f;
    public float pianoPause = 0.5f;
    public float timeBetweenTask = 1f;
    public Game currentGame;
    public Level currentLevelIndex = Level.Poziom1;
    public GameObject levelProgressbar;
    public GameObject pointsProgressbar;
    public GameObject levelsGameObject;
    public List<GameObject> levels;
    
    public List<AudioClip> numbersAudioClips;
    public List<AudioClip> pianoAudioClips;
    public List<AudioClip> morseAudioClips;

    public KeyboardsController numbersKeyboard;
    public KeyboardsController pianoKeyboard;
    public KeyboardsController morseKeyboard;

    public AudioClip keyboardUISound;
    public AudioClip correctAnswerSound;
    public AudioClip badAnswerSound;
    public AudioClip nextLevelSound;
    public AudioClip prevLevelSound;

    public AudioClip morseWelcomeVoice;
    public AudioClip numberWelcomeVoice;
    public AudioClip pianoWelcomeVoice;

    public AudioClip morseGameOverVoice;
    public AudioClip numberGameOverVoice;
    public AudioClip pianoGameOverVoice;

    public List<AudioClip> congratAudioClips;

    public bool playOnEachAnswer = true;

    public GameObject pointGrid;

    private bool isPropperLength = false;
    private int answerIndex = 0;
    private string answer;
    private int pointsIndex = 0;

    private AudioSource _audioSource;

    private bool canAnswer = false;
    private bool isGenerating = false;
    private string enteredAnswer = "";

    private int badAnswersInRow = 0;

    private Level morseLevel = Level.Poziom1;
    private Level numberLevel = Level.Poziom1;
    private Level pianoLevel = Level.Poziom1;

    public GameObject grid;
    public int gridIndex = 0;
    public bool haveBadAnswer = false;
    public int[] answers = new int[3]{0,0,0};
    public int actualQuestion = 1;
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(transform.gameObject);
        }

        levels = new List<GameObject>();

        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                gameObject.AddComponent<AudioSource>();
                _audioSource = GetComponent<AudioSource>();
            }
        }
        //morseKeyboard.gameObject.SetActive(false);
        //numbersKeyboard.gameObject.SetActive(false);
        //pianoKeyboard.gameObject.SetActive(false);
    }
    
    private void Start()
    {
        //PrepareController();
        //GenerateGridPoints();
    }

    private void GenerateGridPoints()
    {
        StartCoroutine(GenerateGridPointsCoroutine());
    }
    
    IEnumerator GenerateGridPointsCoroutine()
    {

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < (int)currentLevelIndex + 2; j++)
            {
                var point = Instantiate(pointGrid, grid.transform);
                yield return null;
                point.transform.localPosition = new Vector3(i* point.GetComponent<RectTransform>().sizeDelta.x, j * point.GetComponent<RectTransform>().sizeDelta.y, 0);
            }
        }
    }
    
    
    void PrepareController()
    {
        AddLevelsToList();
        //SetLevel();
    }
    
    void AddLevelsToList()
    {
        for (int i = 0; i < levelsGameObject.transform.childCount; i++)
        {
            levels.Add(levelsGameObject.transform.GetChild(i).gameObject);
            if (showCards == false)
            {
                for (int j = 0; j < levels[i].transform.childCount; j++)
                {
                    levels[i].transform.GetChild(j).gameObject.GetComponent<Image>().enabled = false;
                    levels[i].transform.GetChild(j).gameObject.transform.GetChild(0).GetComponent<TMP_Text>().enabled = false;
                }
            }
            levels[i].SetActive(false);
        }
    }
    
    public void PlaySequence()
    {
        StartCoroutine(PlaySequenceCoroutine());
    }
    
    void LightUpLevelProgressbar()
    {
        for (int i = 0; i < (int)currentLevelIndex+1; i++)
        {
            levelProgressbar.transform.GetChild(i).gameObject.GetComponent<Image>().color = Color.green;
        }
    }
    
    void LightOffLevelProgressbar()
    {
        for (int i = 0; i < levelProgressbar.transform.childCount; i++)
        {
            levelProgressbar.transform.GetChild(i).gameObject.GetComponent<Image>().color = Color.grey;
        }
    }
    
    void LightUpPointsProgressbar()
    {
        for (int i = 0; i < pointsIndex; i++)
        {
            pointsProgressbar.transform.GetChild(i).gameObject.GetComponent<Image>().color = Color.green;
        }
    }
    
    void LightOffPointsProgressbar()
    {
        for (int i = 0; i < pointsProgressbar.transform.childCount; i++)
        {
            pointsProgressbar.transform.GetChild(i).gameObject.GetComponent<Image>().color = Color.white;
        }
    }
    
    void SetLevel()
    {
        foreach (var level in levels)
        {
            level.SetActive(false);
        }
        
        LightOffLevelProgressbar();
        LightUpLevelProgressbar();
        LightOffPointsProgressbar();
        LightUpPointsProgressbar();
        if ((int)currentLevelIndex < levels.Count)
           levels[(int)currentLevelIndex].SetActive(true);
        ClearCards();
        PlaySequence();
    }

    IEnumerator PlaySequenceCoroutine()
    {
        yield return new WaitWhile(() => _audioSource.isPlaying);
        yield return new WaitForSeconds(timeBetweenTask);

        //Game = Numbers
        if (currentGame == Game.Numbers)
        {
            StartCoroutine(GenerateNumberSoundsCoroutine());
        }
        
        //Game = Piano
        else if (currentGame == Game.Piano)
        {
            StartCoroutine(GeneratePianoSoundsCoroutine());
        }
        
        //Game = Morse
        else if (currentGame == Game.Morse)
        {
            StartCoroutine(GenerateMorseSoundsCoroutine());
        }

        yield return null;
    }
    
    IEnumerator GenerateNumberSoundsCoroutine()
    {
        yield return null;
        /*
        if (isGenerating) yield break;
        isGenerating = true;
        ClearCards();
        answer = "";
        string n;
        
        _audioSource.Stop();
        //Debug.Log("Do wygenerowania " + levels[(int)currentLevelIndex].transform.childCount + " kart");
        for (int i = 0; i < levels[(int)currentLevelIndex].transform.childCount; i++)
        {
            if (i == 0)
            {
                n = TheraplyHelpers.GetRandomInt(1, 9).ToString();
            }
            else
            {
                n = TheraplyHelpers.GetRandomInt(0,9).ToString();
            }
            
            answer += n;
            levels[(int)currentLevelIndex].transform.GetChild(i).GetChild(0).GetComponent<TMP_Text>().text = n;
            _audioSource.clip = numbersAudioClips[int.Parse(n)];
            
            //numbersKeyboard.LigthUpButton(int.Parse(n));
            
            _audioSource.Play();
            
            yield return null;
            
            while (_audioSource.isPlaying)
            {
                yield return null;
            }
            
            if (i != levels[(int)currentLevelIndex].transform.childCount - 1)
                yield return new WaitForSeconds(numbersPause);
        }
        isGenerating = false;
        canAnswer = true;
        */
    }
    
    IEnumerator GeneratePianoSoundsCoroutine()
    {
        Debug.Log("test");
        yield return null;
        
        /*
        if (isGenerating) yield break;
        isGenerating = true;
        ClearCards();
        answer = "";
        string n;
        //Debug.Log("Do wygenerowania " + levels[(int)currentLevelIndex].transform.childCount + " kart");
        for (int i = 0; i < levels[(int)currentLevelIndex].transform.childCount; i++)
        {
            n = TheraplyHelpers.GetRandomInt(0, 7).ToString();
            levels[(int)currentLevelIndex].transform.GetChild(i).GetChild(0).GetComponent<TMP_Text>().text = n;
            _audioSource.clip = pianoAudioClips[int.Parse(n)];
            pianoKeyboard.LigthUpButton(int.Parse(n));
            _audioSource.Play();
            answer += n;
            yield return null;
            while (_audioSource.isPlaying)
            {
                yield return null;
            }

            if (i != levels[(int)currentLevelIndex].transform.childCount - 1)
                yield return new WaitForSeconds(pianoPause);
        }
        isGenerating = false;
        canAnswer = true;
        */
    }
    
    IEnumerator GenerateMorseSoundsCoroutine()
    {
        yield return null;
        /*
        if (isGenerating) yield break;
        isGenerating = true;
        ClearCards();
        answer = "";
        string n;

        for (int i = 0; i < levels[(int)currentLevelIndex].transform.childCount; i++)
        {
            int v = TheraplyHelpers.GetRandomInt(0, 10);
            
            n = (v % 2 == 0) ? "0" : "1";
            
            levels[(int)currentLevelIndex].transform.GetChild(i).GetChild(0).GetComponent<TMP_Text>().text = n;
            _audioSource.clip = morseAudioClips[int.Parse(n)];
            
            //morseKeyboard.LigthUpButton(int.Parse(n));
            
            _audioSource.Play();
            answer += n;
            yield return null;
            while (_audioSource.isPlaying)
            {
                yield return null;
            }

            if (i != levels[(int)currentLevelIndex].transform.childCount - 1)
                yield return new WaitForSeconds(morsePause);
        }
        isGenerating = false;
        canAnswer = true;
        */
    }
    
    void ClearCards()
    {
        for (int i = 0; i < levels[(int)currentLevelIndex].transform.childCount; i++)
        {
            levels[(int)currentLevelIndex].transform.GetChild(i).GetChild(0).GetComponent<TMP_Text>().text = "";
        }
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            PrepareController();
            PlaySequence();
        }

        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            CheckAnswer("0");
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            CheckAnswer("1");
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            CheckAnswer("2");
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            CheckAnswer("3");
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            CheckAnswer("4");
        }

        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            CheckAnswer("5");
        }

        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            CheckAnswer("6");
        }

        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            CheckAnswer("7");
        }

        if (Input.GetKeyDown(KeyCode.Alpha8))
        {
            CheckAnswer("8");
        }

        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            CheckAnswer("9");
        }
    }

    string DecodeKeyToSymbol(string key)
    {
        if (currentGame == Game.Morse)
        {
            if (key == "0") return ".";
            else if (key == "1") return "-";
        }
        else if (currentGame == Game.Numbers)
        {
            return key;
        }
        else if (currentGame == Game.Piano)
        {
            if (key == "0") return "Do";
            else if (key == "1") return "Re";
            else if (key == "2") return "Mi";
            else if (key == "3") return "Fa";
            else if (key == "4") return "So";
            else if (key == "5") return "La";
            else if (key == "6") return "Si";
            else if (key == "7") return "Do";
        }

        return "";
    }
    public void CheckAnswer(string key)
    {
        Debug.Log("CheckAnswer:" + key);
        if (!canAnswer) return;

        enteredAnswer = key;

        grid.transform.GetChild(gridIndex).GetChild(1).transform.GetChild(0).GetComponent<TMP_Text>().text = DecodeKeyToSymbol(key);
        
        if (answerIndex < answer.Length - 1)
        {
            if (key == answer[answerIndex].ToString())
            {
                answerIndex++;
                ColorizeGridPoint(Color.green);
            }
            else
            {
                answerIndex++;
                BadAnswer(false);
                ColorizeGridPoint(Color.red);
                haveBadAnswer = true;
            }
        }
        else
        {
            if (key == answer[answerIndex].ToString())
            {
                CorrectAnswer();
                ColorizeGridPoint(Color.green);
            }
            else
            {
                haveBadAnswer = true;
                BadAnswer(true);
                ColorizeGridPoint(Color.red);
            }
            
            if (haveBadAnswer) answers[actualQuestion - 1] = 1;
            else answers[actualQuestion - 1] = 0;

            if (actualQuestion<4)
                actualQuestion++;
        }

        if (gridIndex < grid.transform.childCount - 1)
        {
            gridIndex++;
        }

        
    }
    
    /*
    public void CheckAnswer(string key)
    {
        if (!canAnswer) return;
        
        //enteredAnswer += key;
        
        enteredAnswer = key;
        
        if (answerIndex < answer.Length - 1)
        {
            if (key == answer[answerIndex].ToString())
            {
                answerIndex++;
                ColorizeGridPoint(Color.green);
            }
            else
            {
                answerIndex++;
                BadAnswer(false);
                ColorizeGridPoint(Color.red);
            }
        }
        else
        {
            if (key == answer[answerIndex].ToString())
            {
                CorrectAnswer();
                ColorizeGridPoint(Color.green);
            }
            else
            {
                BadAnswer(true);
                ColorizeGridPoint(Color.red);
            }
        }

        if (gridIndex < grid.transform.childCount - 1)
        {
            gridIndex++;
        }
    }
    */
    void CorrectAnswer()
    {
        StartCoroutine(CorrectAnswerCoroutine());
    }

    void ColorizeGridPoint(Color color)
    {
        var point = grid.transform.GetChild(gridIndex);
        point.transform.GetChild(0).GetComponent<Image>().color = color;
    }

    IEnumerator CorrectAnswerCoroutine()
    {
        yield return null;
        
        while (_audioSource.isPlaying)
        {
            yield return null;
        }
        
        canAnswer = false;
        answerIndex = 0;

        if (IsGoodAnswer())
        {
            Debug.Log("Correct answer");

            _audioSource.clip = correctAnswerSound;
            _audioSource.Play();

            yield return new WaitWhile(() => _audioSource.isPlaying);

            if (!haveBadAnswer)
            {
                if (playOnEachAnswer)
                {
                    _audioSource.clip = congratAudioClips[Random.Range(0, congratAudioClips.Count - 1)];
                    _audioSource.Play();

                    yield return new WaitWhile(() => _audioSource.isPlaying);
                }
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
        
        while (_audioSource.isPlaying)
        {
            yield return null;
        }

        haveBadAnswer = true;
        
        _audioSource.clip = badAnswerSound;
        _audioSource.Play();
        if (end)
        {
            canAnswer = false;
            answerIndex = 0;

            Debug.Log("Bad answer, the correct answer is: " + answer + " but you've entered: " + enteredAnswer);
            SubtractPoint();
            enteredAnswer = "";
            answer = "";
        }
    }

    void AddPoint()
    {
        //pointsIndex++;
        //badAnswersInRow = 0;
        //haveBadAnswer = false;
        UpdatePoints();
    }

    void SubtractPoint()
    {
        /*
        if (pointsIndex >= 0)
        {
            pointsIndex--;
        }
        else
        {
            pointsIndex = -1;
        }

        badAnswersInRow++;
        */
        UpdatePoints();
    }

    void UpdatePoints()
    {
        StartCoroutine(UpdatePointsCoroutine());
    }

    IEnumerator UpdatePointsCoroutine()
    {
        yield return null;
        int wrongAnswers = 0;

        for (int i = 0; i < answers.Length; i++)
        {
            if (answers[i] == 1) wrongAnswers++;
        }

        haveBadAnswer = false;
        
        // level up
        if (actualQuestion == 4)
        {
            if (wrongAnswers == 0)
            {
                if ((int)currentLevelIndex < levels.Count)
                {
                    gridIndex = 0;
                    currentLevelIndex++;
                    pointsIndex = 0;
                    _audioSource.clip = nextLevelSound;
                    _audioSource.Play();

                    while (_audioSource.isPlaying)
                    {
                        yield return null;
                    }

                    for (int i = 0; i < grid.transform.childCount; i++)
                    {
                        Destroy(grid.transform.GetChild(i).gameObject);
                    }

                    answers = new []{0,0,0};
                    
                    GenerateGridPoints();
                    SetLevel();
                }

                Debug.Log("Congrats!!! Current Game Complited");
            }

            // end game
            else if (wrongAnswers == 3)
            {
                if (currentGame == Game.Morse)
                {
                    _audioSource.clip = morseGameOverVoice;
                    _audioSource.Play();
                }
                else if (currentGame == Game.Numbers)
                {
                    _audioSource.clip = numberGameOverVoice;
                    _audioSource.Play();
                }
                else if (currentGame == Game.Piano)
                {
                    _audioSource.clip = pianoGameOverVoice;
                    _audioSource.Play();
                }

                yield return new WaitWhile(() => _audioSource.isPlaying);

                EndGame();
            }

            //repeat level
            else
            {
                gridIndex = 0;
                pointsIndex = 0;
                //_audioSource.clip = nextLevelSound;
                //_audioSource.Play();

                while (_audioSource.isPlaying)
                {
                    yield return null;
                }

                for (int i = 0; i < grid.transform.childCount; i++)
                {
                    Destroy(grid.transform.GetChild(i).gameObject);
                }

                GenerateGridPoints();
                SetLevel();
                answers = new []{0,0,0};
            }

            actualQuestion = 1;
            answerIndex = 0;
            gridIndex = 0;
        }
        else
            SetLevel();
    }

    public void PlayPianoSound(int i)
    {
        _audioSource.clip = pianoAudioClips[i];
        _audioSource.Play();
    }
    
    public void PlayMorseSound(int i)
    {
        _audioSource.clip = morseAudioClips[i];
        _audioSource.Play();
    }
    
    public void PlayKeyboardSound()
    {
        _audioSource.clip = keyboardUISound;
        _audioSource.Play();
    }

    public void ChangeToMorse()
    {
        //StartCoroutine(ChangeToMorseCoroutine());
        /*
        for (int i = 0; i < grid.transform.childCount; i++)
        {
            Destroy(grid.transform.GetChild(i).gameObject);
        }
        */
        answers = new []{0,0,0};
        
        SaveLevelForGame();
        StopAllCoroutines();
        
        _audioSource.clip = numberWelcomeVoice;
        _audioSource.Play();

        isGenerating = false;
        currentGame = Game.Morse;
        morseKeyboard.gameObject.SetActive(true);
        numbersKeyboard.gameObject.SetActive(false);
        pianoKeyboard.gameObject.SetActive(false);
        currentLevelIndex = morseLevel;
        
        
            for (int i = 0; i < grid.transform.childCount; i++)
            {
                Destroy(grid.transform.GetChild(i).gameObject);
            }
            GenerateGridPoints();
        
        SetLevel();
    }

    IEnumerator ChangeToMorseCoroutine()
    {
        SaveLevelForGame();
        StopAllCoroutines();
        
        _audioSource.clip = numberWelcomeVoice;
        _audioSource.Play();

        isGenerating = false;
        currentGame = Game.Morse;
        morseKeyboard.gameObject.SetActive(true);
        numbersKeyboard.gameObject.SetActive(false);
        pianoKeyboard.gameObject.SetActive(false);
        currentLevelIndex = morseLevel;
        SetLevel();
        yield return null;
    }

    public void ChangeToNumbers()
    {
        //StartCoroutine(ChangeToNumbersCoroutine());
        
        /**/
        answers = new []{0,0,0};
        
        SaveLevelForGame();
        StopAllCoroutines();
        
        _audioSource.clip = numberWelcomeVoice;
        _audioSource.Play();

        isGenerating = false;
        currentGame = Game.Numbers;
        morseKeyboard.gameObject.SetActive(false);
        numbersKeyboard.gameObject.SetActive(true);
        pianoKeyboard.gameObject.SetActive(false);
        currentLevelIndex = numberLevel;

        
            for (int i = 0; i < grid.transform.childCount; i++)
            {
                Destroy(grid.transform.GetChild(i).gameObject);
            }
            GenerateGridPoints();
        
        SetLevel();
    }

    IEnumerator ChangeToNumbersCoroutine()
    {
        SaveLevelForGame();
        StopAllCoroutines();
        
        _audioSource.clip = numberWelcomeVoice;
        _audioSource.Play();

        isGenerating = false;
        currentGame = Game.Numbers;
        morseKeyboard.gameObject.SetActive(false);
        numbersKeyboard.gameObject.SetActive(true);
        pianoKeyboard.gameObject.SetActive(false);
        currentLevelIndex = numberLevel;
        SetLevel();
        yield return null;
    }

    public void ChangeToPiano()
    {
        //StartCoroutine(ChangeToPianoCoroutine());
        /*for (int i = 0; i < grid.transform.childCount; i++)
        {
            Destroy(grid.transform.GetChild(i).gameObject);
        }*/
        answers = new []{0,0,0};
        
        SaveLevelForGame();
        StopAllCoroutines();
        isGenerating = false;
        currentGame = Game.Piano;
        morseKeyboard.gameObject.SetActive(false);
        numbersKeyboard.gameObject.SetActive(false);
        pianoKeyboard.gameObject.SetActive(true);
        _audioSource.clip = pianoWelcomeVoice;
        _audioSource.Play();
        currentLevelIndex = pianoLevel;
        
        
            for (int i = 0; i < grid.transform.childCount; i++)
            {
                Destroy(grid.transform.GetChild(i).gameObject);
            }
            GenerateGridPoints();
        
        SetLevel();
    }

    IEnumerator ChangeToPianoCoroutine()
    {
        SaveLevelForGame();
        StopAllCoroutines();
        isGenerating = false;
        currentGame = Game.Piano;
        morseKeyboard.gameObject.SetActive(false);
        numbersKeyboard.gameObject.SetActive(false);
        pianoKeyboard.gameObject.SetActive(true);
        _audioSource.clip = pianoWelcomeVoice;
        _audioSource.Play();
        
        currentLevelIndex = pianoLevel;
        SetLevel();
        yield return null;
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

    void EndGame()
    {
        StopAllCoroutines();
        Debug.Log("Game Over...");
        if ((int)currentLevelIndex > 0)
            currentLevelIndex--;
    }
}
