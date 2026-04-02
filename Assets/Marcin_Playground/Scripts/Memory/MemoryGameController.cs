using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;


public class MemoryGameController : MonoBehaviour
{
    public static MemoryGameController Instance;
    public GameObject smallSunflowerPrefab;
    public GameObject mediumSunflowerPrefab;
    public GameObject bigSunflowerPrefab;
    public GameObject wordBoardPrefab;
    public Transform sunflowersSpawnPointsRoot;
    public Transform wordBoardSpawnPointsRoot;
    public MemoryGameConfig gameConfig;
    public bool canTurn = true;
    public bool isAnswering = false;

    public float minRadius = 1.0f;
    public float maxRadius = 1.5f;
    public Transform[] sunflowersSpawnPoints;
    public Transform[] wordBoardsSpawnPoints;
    public List<GameObject> sunflowers = new List<GameObject>();
    private MemoryElementData[] elements;
    private int numberOfSunflowers;
    private ElementType currentElementType;

    private SunflowerController firstSelected = null;
    private SunflowerController secondSelected = null;

    private List<WordElementData> selectedWords;
    private List<MemoryElementData> selectedLetters;
    private List<MemoryElementData> selectedNumbers;
    private List<MemoryElementData> selectedEmoticons;

    private AudioSource _audioSource;

    private int _totalTouches = 0;
    private Stopwatch _timer;
    private List<double> _timesList;
    private List<double> _sesionTimesList;
    private bool _isSessionEnded = true;
    SessionMemoryData sessionMemoryData;

    private const float GOLDEN_ANGLE = 137.5f * Mathf.Deg2Rad; // Golden angle in radians
    private int pointCount = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        ApplySettings(gameConfig);
        this.AddComponent<AudioSource>();
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = false;
        _timesList = new List<double>();
        _sesionTimesList = new List<double>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            StartSession();
        }
    }

    public void StartSession()
    {
        if (_isSessionEnded)
        {
            _isSessionEnded = false;
            sessionMemoryData = new SessionMemoryData();
            sessionMemoryData.gameType = gameConfig.elementType;
            sessionMemoryData.touchPairAverageTimeList = new List<KeyValuePair<int, double>>();
        }

        StartCoroutine(StartSessionCoroutine());
    }

    IEnumerator StartSessionCoroutine()
    {
        sunflowersSpawnPoints = new Transform[sunflowersSpawnPointsRoot.transform.childCount];

        for (int i = 0; i < sunflowersSpawnPointsRoot.transform.childCount; i++)
        {
            sunflowersSpawnPoints[i] = sunflowersSpawnPointsRoot.transform.GetChild(i).transform;
            yield return null;
        }

        wordBoardsSpawnPoints = new Transform[wordBoardSpawnPointsRoot.transform.childCount];

        for (int i = 0; i < wordBoardSpawnPointsRoot.transform.childCount; i++)
        {
            wordBoardsSpawnPoints[i] = wordBoardSpawnPointsRoot.transform.GetChild(i).transform;
            yield return null;
        }

        ClearSunflowers();

        yield return new WaitForSeconds(0.1f);
    }

    private List<WordElementData> SelectWords()
    {
        List<WordElementData> words = new List<WordElementData>();
        selectedWords = new List<WordElementData>();
        words = GetWordListItem();
        for (int i = 0; i < numberOfSunflowers / 2; i++)
        {
            int nr = Random.Range(0, words.Count - 1);
            selectedWords.Add(words[nr]);

            Debug.Log(words[nr].word);

            words.RemoveAt(nr);
        }

        for (int i = 0; i < selectedWords.Count; i++)
        {
            var wordTable = Instantiate(wordBoardPrefab, wordBoardsSpawnPoints[i].position, wordBoardsSpawnPoints[i].rotation);
            WordBoardController wbc = wordTable.GetComponent<WordBoardController>();
            wbc.SetText(selectedWords[i].word);
            wbc.elementData = selectedWords[i];
            wbc.GrowUp();
        }

        return selectedWords;
    }

    private List<MemoryElementData> SelectLetters()
    {
        List<MemoryElementData> letters = new List<MemoryElementData>();
        selectedLetters = new List<MemoryElementData>();
        letters = GetLetterListItem();
        for (int i = 0; i < numberOfSunflowers / 2; i++)
        {
            int nr = Random.Range(0, letters.Count - 1);
            selectedLetters.Add(letters[nr]);
            selectedLetters.Add(letters[nr]);
            Debug.Log(letters[nr].elementName);

            letters.RemoveAt(nr);
        }

        return selectedLetters;
    }

    private List<MemoryElementData> SelectNumbers()
    {
        List<MemoryElementData> numbers = new List<MemoryElementData>();
        selectedNumbers = new List<MemoryElementData>();
        numbers = GetNumberListItem();
        for (int i = 0; i < numberOfSunflowers / 2; i++)
        {
            int nr = Random.Range(0, numbers.Count - 1);
            selectedNumbers.Add(numbers[nr]);
            selectedNumbers.Add(numbers[nr]);
            Debug.Log(numbers[nr].elementName);

            numbers.RemoveAt(nr);
        }

        return selectedNumbers;
    }

    private List<MemoryElementData> SelectEmoticons()
    {
        List<MemoryElementData> emoticons = new List<MemoryElementData>();
        selectedEmoticons = new List<MemoryElementData>();
        emoticons = GetEmotionsListItem();
        for (int i = 0; i < numberOfSunflowers / 2; i++)
        {
            int nr = Random.Range(0, emoticons.Count - 1);
            selectedEmoticons.Add(emoticons[nr]);
            selectedEmoticons.Add(emoticons[nr]);
            Debug.Log(emoticons[nr].elementName);

            emoticons.RemoveAt(nr);
        }

        return selectedEmoticons;
    }

    private List<MemoryElementData> ConvertWordListToSyllablesList()
    {
        List<MemoryElementData> syllablesList = new List<MemoryElementData>();

        foreach (var elementData in SelectWords())
        {
            syllablesList.Add(elementData.firstSyllable);
            syllablesList.Add(elementData.secondSyllable);
        }

        for (int i = syllablesList.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            MemoryElementData temp = syllablesList[i];
            syllablesList[i] = syllablesList[j];
            syllablesList[j] = temp;
        }

        return syllablesList;
    }



    List<WordElementData> GetWordListItem()
    {
        List<WordElementData> wordsList = new List<WordElementData>();
        /*#if UNITY_EDITOR
                string[] guids = AssetDatabase.FindAssets("t:WordElementData");

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    WordElementData so = AssetDatabase.LoadAssetAtPath<WordElementData>(path);
                    wordsList.Add(so);
                
                }
        #else*/
        WordElementData[] wordsArray = Resources.LoadAll<WordElementData>("Memory");
        foreach (var word in wordsArray)
        {
            wordsList.Add(word);
        }
        //#endif
        return wordsList;
    }

    List<MemoryElementData> GetNumberListItem()
    {
        List<MemoryElementData> numberList = new List<MemoryElementData>();
        /*
#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:MemoryElementData");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MemoryElementData so = AssetDatabase.LoadAssetAtPath<MemoryElementData>(path);
            if (so.elementType == ElementType.Number)
                numberList.Add(so);
        }
#else*/
        MemoryElementData[] wordsArray = Resources.LoadAll<MemoryElementData>("Memory");
        foreach (var word in wordsArray)
        {
            if (word.elementType == ElementType.Number)
                numberList.Add(word);
        }
        //#endif
        return numberList;
    }

    List<MemoryElementData> GetEmotionsListItem()
    {
        List<MemoryElementData> numberList = new List<MemoryElementData>();
        /*
#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:MemoryElementData");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MemoryElementData so = AssetDatabase.LoadAssetAtPath<MemoryElementData>(path);
            if (so.elementType == ElementType.Emotion)
                numberList.Add(so);
        }
#else*/
        MemoryElementData[] wordsArray = Resources.LoadAll<MemoryElementData>("Memory");
        foreach (var word in wordsArray)
        {
            if (word.elementType == ElementType.Emotion)
                numberList.Add(word);
        }
        //#endif

        return numberList;
    }

    List<MemoryElementData> GetLetterListItem()
    {
        List<MemoryElementData> letterList = new List<MemoryElementData>();

        /*
#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:MemoryElementData");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MemoryElementData so = AssetDatabase.LoadAssetAtPath<MemoryElementData>(path);
            if (so.elementType == ElementType.Letter)
                letterList.Add(so);
        }
#else*/

        MemoryElementData[] wordsArray = Resources.LoadAll<MemoryElementData>("Memory");
        foreach (var word in wordsArray)
        {
            if (word.elementType == ElementType.Letter)
                letterList.Add(word);
        }
        //#endif
        return letterList;
    }

    private void GenerateSunflowers()
    {
        StartCoroutine(GenerateSunflowersCoroutine());
    }
    /*
        private Vector3 GenerateRandomPointOnCircle()
        {
            /*
            float angle = pointCount * GOLDEN_ANGLE;

            // Generate a random radius between minRadius and maxRadius
            float randomRadius = Random.Range(minRadius, maxRadius);

            // Get the main camera position
            Vector3 cameraPosition = Camera.main.transform.position;

            // Calculate the point relative to the camera position
            float x = cameraPosition.x + randomRadius * Mathf.Cos(angle);
            float z = cameraPosition.z + randomRadius * Mathf.Sin(angle);

            pointCount++;

            return new Vector3(x, 0, z);


            // Adjust angle to generate points within a half-circle from -90 to 90 degrees relative to the player position
            float angle = Mathf.PI / 2 - (pointCount * GOLDEN_ANGLE);
            angle = Random.Range(-Mathf.PI / 2, Mathf.PI / 2);
            // Generate a random radius between minRadius and maxRadius
            float randomRadius = Random.Range(minRadius, maxRadius);

            // Get the main camera position
            Vector3 cameraPosition = Camera.main.transform.position;

            // Calculate the point relative to the camera position within the half-circle range
            //float x = cameraPosition.x + randomRadius * Mathf.Cos(angle);
            //float z = cameraPosition.z + randomRadius * Mathf.Sin(angle);

            //pointCount++;

            //return new Vector3(x, 0, z);

            // Oblicz wektor kierunku na podstawie kąta
            // Najpierw obliczamy przesunięcie w prawo/lewo i do przodu
            Vector3 direction = (Camera.main.transform.forward * Mathf.Cos(angle)) + (Camera.main.transform.right * Mathf.Sin(angle));

            // Oblicz końcowy punkt względem pozycji kamery, przesuwając o wektor kierunku pomnożony przez losowy promień
            Vector3 pointPosition = cameraPosition + direction * randomRadius;

            pointCount++;

            return new Vector3(pointPosition.x, 0, pointPosition.z);
        }
        */


    public Vector3 GetPointOnSemiCircle(int x, int y)
    {
        /*
        // Oblicz równomiernie rozłożony kąt dla punktu x z y punktów
        // Zakładamy, że kąt rozłożony jest od -π/2 do π/2, czyli półokrąg
        float angle = Mathf.Lerp(-Mathf.PI / 2, Mathf.PI / 2, (float)x / (float)(y - 1));

        // Ustal losowy promień między minRadius a maxRadius
        float randomRadius = Random.Range(minRadius, maxRadius);

        // Pobierz pozycję kamery
        Vector3 cameraPosition = Camera.main.transform.position;

        // Oblicz wektor kierunku na podstawie kąta
        Vector3 direction = (Camera.main.transform.forward * Mathf.Cos(angle)) + (Camera.main.transform.right * Mathf.Sin(angle));

        // Oblicz końcowy punkt względem pozycji kamery, przesuwając o wektor kierunku pomnożony przez losowy promień
        Vector3 pointPosition = cameraPosition + direction * randomRadius;

        // Zwracamy współrzędne punktu na płaszczyźnie XZ
        return new Vector3(pointPosition.x, 0, pointPosition.z);
        */
        // Oblicz równomiernie rozłożony kąt dla punktu x z y punktów
        // Zakładamy, że kąt rozłożony jest od -π/2 do π/2, czyli półokrąg
        float angle = Mathf.Lerp(-Mathf.PI / 2, Mathf.PI / 2, (float)x / (float)(y - 1));

        // Ustal równomierny promień dla każdego punktu
        float radius = Mathf.Lerp(minRadius, maxRadius, (float)x / (float)(y - 1));

        // Pobierz pozycję kamery
        Vector3 cameraPosition = Camera.main.transform.position;

        // Oblicz wektor kierunku na podstawie kąta
        Vector3 direction = (Camera.main.transform.forward * Mathf.Cos(angle)) + (Camera.main.transform.right * Mathf.Sin(angle));

        // Oblicz końcowy punkt względem pozycji kamery, przesuwając o wektor kierunku pomnożony przez obliczony promień
        Vector3 pointPosition = cameraPosition + direction * radius;

        // Zwracamy współrzędne punktu na płaszczyźnie XZ
        return new Vector3(pointPosition.x, 0, pointPosition.z);
    }

    private GameObject GetRandomSunflowerPrefab()
    {
        int randomIndex = Random.Range(0, 3);
        switch (randomIndex)
        {
            case 0:
                return smallSunflowerPrefab;
            case 1:
                return mediumSunflowerPrefab;
            case 2:
                return bigSunflowerPrefab;
            default:
                return smallSunflowerPrefab; // Fallback to small sunflower
        }
    }

    private IEnumerator GenerateSunflowersCoroutine()
    {
        yield return new WaitForSeconds(3);

        List<MemoryElementData> memoryElementDatas = new List<MemoryElementData>();
        if (gameConfig.elementType == ElementType.Syllable)
        {
            memoryElementDatas = ConvertWordListToSyllablesList();
        }
        else if (gameConfig.elementType == ElementType.Number)
        {
            memoryElementDatas = GetRandomNumbers();
        }
        else if (gameConfig.elementType == ElementType.Letter)
        {
            memoryElementDatas = GetRandomLetters();
        }
        else if (gameConfig.elementType == ElementType.Emotion)
        {
            memoryElementDatas = GetRandomEmoticons();
        }

        yield return null;

        List<int> indices = new List<int>();
        for (int i = 0; i < numberOfSunflowers; i++)
        {
            indices.Add(i);
        }

        while (indices.Count > 0)
        {
            int randomIndex = Random.Range(0, indices.Count); // Losowy indeks z listy
            int i = indices[randomIndex]; // Wybierz indeks
            indices.RemoveAt(randomIndex); // Usuń wybrany indeks z listy

            Transform spawnPoint = sunflowersSpawnPoints[i];// % sunflowersSpawnPoints.Length];
            yield return new WaitWhile(() => spawnPoint == null);
            GameObject selectedPrefab = GetRandomSunflowerPrefab();
            Vector3 spawnPosition = GetPointOnSemiCircle(i, numberOfSunflowers);
            Vector3 directionToCamera = Camera.main.transform.position - spawnPosition;
            directionToCamera.y = 0; // Ignore vertical difference
            Quaternion rotation = Quaternion.LookRotation(directionToCamera);
            GameObject sunflower = Instantiate(selectedPrefab, spawnPosition, rotation);
            MemoryElementData randomElement = memoryElementDatas[i];//GetRandomElement();
            yield return null;
            SunflowerController sfc = sunflower.GetComponent<SunflowerController>();
            yield return null;
            sfc.AssignValue(randomElement);
            //sfc.elementText.text = randomElement.elementName;
            sunflower.name = "sunflower_" + i;
            sunflowers.Add(sunflower);
            yield return new WaitForSeconds(Random.Range(0.1f, 0.5f));
        }
    }

    List<MemoryElementData> GetRandomLetters()
    {
        return Randomize(SelectLetters());
    }

    List<MemoryElementData> GetRandomNumbers()
    {
        return Randomize(SelectNumbers());
    }

    List<MemoryElementData> GetRandomEmoticons()
    {
        return Randomize(SelectEmoticons());
    }

    List<MemoryElementData> Randomize(List<MemoryElementData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            MemoryElementData temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        return list;
    }

    private MemoryElementData GetRandomElement()
    {
        MemoryElementData[] filteredElements = elements.Where(e => e.elementType == currentElementType).ToArray();
        return filteredElements[Random.Range(0, filteredElements.Length)];
    }

    private void ClearSunflowers()
    {
        foreach (GameObject sunflower in sunflowers)
        {
            Destroy(sunflower);
        }
        sunflowers.Clear();
        _timesList = new List<double>();
        _totalTouches = 0;
        foreach (WordBoardController board in FindObjectsOfType<WordBoardController>())
        {
            Destroy(board.gameObject);
        }

        pointCount = 0; // Reset the point count when starting a new session
        GenerateSunflowers();
    }

    public void EndSession()
    {
        // Tutaj można zaimplementować logikę kończenia sesji i zapisywania danych.
        Debug.Log("Session Ended");
    }

    public void ApplySettings(MemoryGameConfig config)
    {
        elements = config.elements;
        numberOfSunflowers = (int)config.numberOfSunflowers;
        currentElementType = config.elementType;

        StartSession();
    }

    public void CheckMatch(SunflowerController sunflowerController)
    {
        StartCoroutine(CheckMatchCoroutine(sunflowerController));
    }

    private IEnumerator CheckMatchCoroutine(SunflowerController sunflowerController)
    {
        if (firstSelected == null)
        {
            firstSelected = sunflowerController;
        }
        else
        {
            canTurn = false;
            secondSelected = sunflowerController;
        }
        if ((firstSelected != null && secondSelected != null))
        {
            yield return new WaitForSeconds(1);

            //////////////////////////////////////////////////////////////////////////////////////
            if (gameConfig.elementType == ElementType.Letter || gameConfig.elementType == ElementType.Number || gameConfig.elementType == ElementType.Emotion)
            {
                if (firstSelected.elementData.elementName == secondSelected.elementData.elementName)
                {
                    firstSelected.gameObject.SetActive(false);
                    secondSelected.gameObject.SetActive(false);
                    if (firstSelected.elementData.audioClip || secondSelected.elementData.audioClip)
                    {
                        if (firstSelected.elementData.audioClip)
                        {
                            _audioSource.clip = firstSelected.elementData.audioClip;
                        }
                        else
                        {
                            _audioSource.clip = secondSelected.elementData.audioClip;
                        }

                        _audioSource.Play();

                        _timesList.Add(_timer.ElapsedMilliseconds);

                        Debug.Log((_timer.Elapsed.Seconds - 1) + ":" + _timer.Elapsed.Milliseconds);
                    }
                }
                else
                {
                    firstSelected.isOpen = false;
                    secondSelected.isOpen = false;
                }
            }
            else if (gameConfig.elementType == ElementType.Syllable)
            {
                if (CheckCombination(selectedWords, firstSelected.elementData, secondSelected.elementData))
                {
                    firstSelected.gameObject.SetActive(false);
                    secondSelected.gameObject.SetActive(false);

                    yield return new WaitForSeconds(0.5f);

                    var wordBoards = FindObjectsOfType<WordBoardController>();
                    for (int i = 0; i < wordBoards.Length; i++)
                    {
                        if (wordBoards[i].word == firstSelected.elementData.elementName + secondSelected.elementData.elementName)
                            wordBoards[i].ShrinkDown();
                    }

                }
                else
                {
                    firstSelected.isOpen = false;
                    secondSelected.isOpen = false;
                }
            }
            //////////////////////////////////////////////////////////////

            firstSelected.ChangeAnimationState("ZAKRYCIE");
            secondSelected.ChangeAnimationState("ZAKRYCIE");

            // Zresetuj wybory
            firstSelected = null;
            secondSelected = null;
            yield return null;
            canTurn = true;

            SunflowerController[] sunflowers = FindObjectsByType<SunflowerController>(FindObjectsSortMode.None);
            if (sunflowers.Length == 0)
                CollectData();
        }
    }

    public bool CheckCombination(List<WordElementData> wordElementDataList, MemoryElementData firstClick, MemoryElementData secondClick)
    {
        bool temp = false;
        foreach (var wordData in wordElementDataList)
        {
            // Sprawdzenie czy firstClick odpowiada firstSyllable i secondClick odpowiada secondSyllable
            if (firstClick == wordData.firstSyllable && secondClick == wordData.secondSyllable)
            {
                wordElementDataList.Remove(wordData);
                _timesList.Add(_timer.ElapsedMilliseconds);
                Debug.Log(_timer.Elapsed.Seconds + ":" + _timer.Elapsed.Milliseconds);
                return true;
            }
            // Sprawdzenie czy tylko firstClick odpowiada firstSyllable
            else if (firstClick == wordData.firstSyllable && secondClick != wordData.secondSyllable)
            {
                temp = false;
            }
            // Sprawdzenie czy tylko secondClick odpowiada secondSyllable
            else if (firstClick != wordData.firstSyllable && secondClick == wordData.secondSyllable)
            {
                temp = false;
            }
        }
        // Jeśli żadna kombinacja nie pasuje
        return temp;
    }

    public void StartTimer()
    {
        _timer = new Stopwatch();
        _timer.Start();
    }

    public int StopTimer()
    {
        _timer.Stop();
        return _timer.Elapsed.Milliseconds;
    }

    public void IncreaseTouchCounter()
    {
        _totalTouches++;
    }

    void CollectData()
    {
        //Debug.Log("touches: " + _totalTouches);
        //Debug.Log("averageTime: " + FormatTime(_timesList.Average()));
        gameConfig.numberOfRounds--;
        KeyValuePair<int, double> touchPairAverageTime = new KeyValuePair<int, double>(_totalTouches, _timesList.Average());
        sessionMemoryData.touchPairAverageTimeList.Add(touchPairAverageTime);

        _sesionTimesList.Add(_timesList.Average());
        sessionMemoryData.sessionAverageTime = _sesionTimesList.Average();


        if (gameConfig.numberOfRounds > 0)
        {
            StartSession();
        }

        if (gameConfig.numberOfRounds == 0)
        {
            _isSessionEnded = true;
            sessionMemoryData.numberOfSunflowers = numberOfSunflowers;
            Debug.Log(JsonUtility.ToJson(sessionMemoryData));


            // Debug touchPairAverageTimeList elements
            for (int i = 0; i < sessionMemoryData.touchPairAverageTimeList.Count; i++)
            {
                var pair = sessionMemoryData.touchPairAverageTimeList[i];
                Debug.Log(pair.Key + " " + pair.Value);
            }
        }
    }

    public string FormatTime(double milliseconds)
    {
        int seconds = (int)(milliseconds / 1000);
        int remainingMilliseconds = (int)(milliseconds % 1000);

        return (seconds + ":" + remainingMilliseconds);
    }
}

[Serializable]
public class SessionMemoryData
{
    public ElementType gameType;
    public int numberOfSunflowers;
    public List<KeyValuePair<int, double>> touchPairAverageTimeList;
    public double sessionAverageTime;
}