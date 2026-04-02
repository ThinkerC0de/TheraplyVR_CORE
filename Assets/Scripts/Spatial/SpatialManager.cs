using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
// using static UnityEditor.Progress;

public class SpatialManager : MonoBehaviour
{
    [SerializeField] private SpatialTrainingCommunication spatialCommunication;
    [SerializeField] private AudioSource fanfare;
    [SerializeField] private TMP_Text nicoText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text bestTimeText;
    [SerializeField] private TMP_Text actualScoreText;
    [SerializeField] private TMP_Text actualTimeText;
    [SerializeField] private Transform niko;
    [SerializeField] private List<CubeGenerator> cubeGenerators;
    [SerializeField] private List<CubeButton> cubeButtons;
    [SerializeField] private int level;

    private int buttonCount;
    private int mistakes;
    private int success;
    private float startTime;
    private float bestTime = 0;
    private float actualTime = 0;
    private bool firstRun = true;
    private HashSet<int> commonCubes;
    private HashSet<int> pressedButtons;
    private SpatialData gameData;


    public class SpatialData
    {
        public int studentLevel;
        public float bestTime;
        public List<Result> results;
    }

    public class Result
    {
        public int level;
        public float elapsedTime;
        public int misstakes;

        public Result(int level, float elapsedTime, int misstakes)
        {
            this.level = level;
            this.elapsedTime = elapsedTime;
            this.misstakes = misstakes;
        }
    }


    private void Awake()
    {
        //TODO: skasować po połączeniu z mobilką
        LoadData();
    }

    void Start()
    {
        commonCubes = new HashSet<int>();
        pressedButtons = new HashSet<int>();

        bestTimeText.transform.parent.gameObject.SetActive(false);
        actualTimeText.transform.parent.gameObject.SetActive(false);
        actualScoreText.transform.parent.gameObject.SetActive(false);

        if (gameData == null)
        {
            gameData = new SpatialData();
            gameData.results = new List<Result>();

            StartSession();
        }
    }

    public void StartSession()
    {
        if (level <= 0)
        {
            StartCoroutine(Tutorial());
        }
        else
        {
            StartCoroutine(GameLevel());
        }
    }

    private void GenerateCubesForGenerators()
    {
        Debug.Log("Start level " + level);
        
        commonCubes.Clear();

        // Set the size range depending on the level
        int minCount = 2 + level;
        int maxCount = 5 + level;

        if (level == 10)
        {
            minCount += 2;
            maxCount += 2;
        }

        // Generate the desired cube size
        int commonCount = Random.Range(minCount, maxCount + 1);

        //  List for storing unique sizes
        HashSet<int> uniqueCounts = new HashSet<int> { commonCount };

        // Generate cube size for generators
        for (int i = 2; i < cubeGenerators.Count; i++)
        {
            int uniqueCount;
            do
            {
                uniqueCount = Random.Range(minCount, maxCount + 1);
            } while (!uniqueCounts.Add(uniqueCount));
        }

        List<int> counts = new List<int>(uniqueCounts);
        counts.Add(commonCount);


        // Mixing the list
        for (int i = 0; i < counts.Count; i++)
        {
            int randomIndex = Random.Range(0, counts.Count);
            int temp = counts[i];
            counts[i] = counts[randomIndex];
            counts[randomIndex] = temp;
        }

        //  Generating cubes for each generator
        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            cubeGenerators[i].GenerateCubes(counts[i]);

            if (counts[i] == commonCount)
            {
                commonCubes.Add(i);
            }
        }

        startTime = Time.time;
    }

    private void GenerateCubesForGenerators(bool tutorial)
    {
        Debug.Log("Start tutorial");

        List<int> counts = new List<int>{2,4,3,4};

        commonCubes.Add(1);
        commonCubes.Add(3);

        //  Generating cubes for each generator
        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            cubeGenerators[i].GenerateCubes(counts[i]);
        }
    }

    // Activate the button check the result
    public void ActivateButton(bool butonActive, int buttonNumber)
    {
        if (butonActive)
        {
            buttonCount++;
            pressedButtons.Add(buttonNumber - 1);
        }
        else
        {
            buttonCount--;
            pressedButtons.Remove(buttonNumber - 1);
        }

        if (buttonCount == 2)
        {
            CheckResult();
        }
    }

    // Checking the game result
    private void CheckResult()
    {
        buttonCount = 0;

        foreach (CubeButton button in cubeButtons)
        {
            button.ButtonOff();
        }

        if (pressedButtons.SetEquals(commonCubes))
        {
            float time = Time.time - startTime;

            gameData.results.Add(new Result(level, time, mistakes));
            mistakes = 0;

            if (level >= 10)
            {
                actualTime = time;
                actualTimeText.text = actualTime.ToString("0.00");

                if (bestTime > actualTime || bestTime == 0)
                {
                    bestTime = actualTime;
                    bestTimeText.text = bestTime.ToString("0.00");
                }
            }

            VirtualFriend.Instance.GoodJob();
            if (success == 2)
            {
                nicoText.text = "Brawo, poziom zaliczony przechodzisz dalej";
            }
            else
            {
                nicoText.text = "Brawo!";
            }

            Regenerate();
        }
        else
        {
            mistakes++;

            switch (mistakes)
            {
                case 1:
                    nicoText.text = "To nie te konstrukcje.\nPolicz klocki i znajdź dwie konstrukcje z taką samą liczbą klocków.\nSpróbuj jeszcze raz! ";
                    break;

                case 2:
                    if (level < 10)
                    {
                        nicoText.text = "To nie te konstrukcje.\nSpójrz podświetliły się dwie konstrukcje, które mają tyle samo klocków.\nTo jest dobra odpowiedź.";
                        ShowCorrect();
                    }
                    else
                    {
                        nicoText.text = "To nie te konstrukcje.\nPolicz klocki i znajdź dwie konstrukcje z taką samą liczbą klocków.\nSpróbuj jeszcze raz! ";
                    }
                    break;

                default:
                    if (level <= 0)
                    {
                        StartCoroutine(TutorialTwo());
                    }
                    else
                    {
                        nicoText.text = "Na dziś chyba wystarczy nam ćwiczeń z wyobraźni przestrzennej.\nPobawimy się dalej następnym razem.";
                        StartCoroutine(LoadMainScene());
                    }
                    break;
            }
        }

        pressedButtons.Clear();

    }

    // Generate the next game
    public void Regenerate()
    {
        
        StartCoroutine(Success());

        if (level <= 0)
        { 
            StartCoroutine(TutorialTwo());
            level++;
        }
        else
        {
            success++;
            actualScoreText.text = success.ToString();

            if (level < 9)
            {
                if (success >= 3)
                {
                    success = 0;
                    //TODO: zamienić po połączeniu z mobilką
                    SaveData();
                    //SaveStudentData();
                    level++;
                }
            }
            else if (level == 9)
            {
                if (success >= 3)
                {
                    success = 0;
                    level++;
                    //TODO: zamienić po połączeniu z mobilką
                    SaveData();
                    //SaveStudentData();
                }
            }

            StartCoroutine(GameLevel());
        }
    }

    private void ColorMix(bool state)
    {
        foreach (CubeGenerator generator in cubeGenerators)
        {
            generator.SwitchColorMix(false);
        }
    }

    private IEnumerator Tutorial()
    {
        foreach (CubeButton button in cubeButtons)
        {
            button.ButtonActivate(false);
        }

        yield return new WaitForSeconds(6f);

        nicoText.text = "Wyobraźnia przestrzenna to ważna i przydatna umiejętność. Świetnie że chcesz ją poćwiczyć i zostać Mistrzem Wyobraźni Przestrzennej.";
        yield return new WaitForSeconds(6f);
        //yield return new WaitWhile(() => audioSource.isPlaying);
        //yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
        //yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("LevelCompleted")));

        nicoText.text = "Zabawa jest prosta. Będziesz mieć zawsze 4 konstrukcje składające się z takich samych sześcianów - takich samych małych klocków.";
        GenerateCubesForGenerators(true);
        yield return new WaitForSeconds(6f);

        nicoText.text = "Konstrukcje różnią się od siebie ilością małych klocków. Tylko dwie mają zawsze taką samą liczbę małych klocków.";
        yield return new WaitForSeconds(6f);

        nicoText.text = "Konstrukcje możesz obracać we wszystkich kierunkach aby łatwiej policzyć ile mają klocków.";

        niko.transform.rotation = Quaternion.Euler(0, -200, 0);
        StartCoroutine(VirtualFriend.Instance.FingerPoint());
        

        foreach (CubeGenerator generator in cubeGenerators)
        {
            StartCoroutine(generator.RotateRandomly());
        }
        yield return new WaitForSeconds(4f);
        niko.transform.rotation = Quaternion.Euler(0, -230, 0);
        yield return new WaitForSeconds(2f);
        
        nicoText.text = "W tym przykładzie widzisz, że pierwsza konstrukcja składa się z dwóch małych klocków, druga ma cztery małe klocki, trzecia ma trzy klocki a czwarta cztery klocki czyli tyle samo co druga.";

        niko.transform.rotation = Quaternion.Euler(0, -200, 0);
        StartCoroutine(VirtualFriend.Instance.FingerPoint());
        ShowCorrect();

        yield return new WaitForSeconds(4f);
        niko.transform.rotation = Quaternion.Euler(0, -230, 0);
        yield return new WaitForSeconds(2f);

        nicoText.text = "Jak już policzysz klocki i odnajdziesz te dwie konstrukcje które mają tyle samo klocków musisz jak najszybciej wcisnąć przyciski pod tymi samymi konstrukcjami";
        yield return new WaitForSeconds(4f);

        HideAll();

        yield return new WaitForSeconds(2f);
        GenerateCubesForGenerators();

        foreach (CubeButton button in cubeButtons)
        {
            button.ButtonActivate(true);
        }

        nicoText.text = "Spróbuj teraz zrobić to samodzielnie - policz klocki i znajdź dwie konstrukcje z taką samą liczbą klocków.";
        yield return new WaitForSeconds(6f);

        nicoText.text = "Wciśnij przyciski pod konstrukcjami które mają tyle samo klocków. ";
    }

    private IEnumerator TutorialTwo()
    {
        yield return new WaitForSeconds(3.1f);

        nicoText.text = "Spróbuj teraz zrobić to zadanie gdy klocki będą miały ten sam kolor trochę trudniej je policzyć ale spróbuj.\nPolicz klocki i znajdź dwie konstrukcje z taką samą liczbą klocków.";
        ColorMix(false);
        GenerateCubesForGenerators();
        yield return new WaitForSeconds(6f);

        nicoText.text = "Wciśnij przyciski pod konstrukcjami które mają tyle samo klocków.";
    }

    private IEnumerator GameLevel()
    {
        yield return new WaitForSeconds(2f);

        foreach (CubeGenerator generator in cubeGenerators)
        {
            generator.gameObject.SetActive(true);
        }

        levelText.text = level.ToString();
        actualScoreText.text = success.ToString();
        
        
        if (firstRun)
        {
            if (level < 10)
            {
                nicoText.text = "Witaj w grze. Zabawa jest prosta.\nBędziesz mieć zawsze 4 konstrukcje składające się z takich samych małych klocków.";
                actualScoreText.transform.parent.gameObject.SetActive(true);
            }
            else
            {
                nicoText.text = "Witaj Mistrzu Wyobraźni Przestrzennej!\nSpróbuj pobić rekord czasu znalezienia pary brył o takiej samej liczbie klocków.";
                bestTimeText.text = bestTime.ToString("0.00");
                bestTimeText.transform.parent.gameObject.SetActive(true);
                actualTimeText.transform.parent.gameObject.SetActive(true);
            }

            yield return new WaitForSeconds(4f);

            ColorMix(false);
            firstRun = false;
        }

        yield return new WaitForSeconds(2f);

        GenerateCubesForGenerators();

        nicoText.text = "Policz klocki i znajdź dwie konstrukcje z taką samą liczbą klocków.";
    }

    public IEnumerator LoadMainScene()
    {
        yield return new WaitForSeconds(6f);

        SceneManager.LoadScene(0);
    }

    private void LoadData()
    {
        if (GeneralDataManager.Instance == null)
        {
            Debug.Log("GeneralDataManager.Instance.KidID == null");
            return;
        }

        string saveKey = GeneralDataManager.Instance.KidID + "_SpatialCurrentLevel";
        level = PlayerPrefs.GetInt(saveKey);
        Debug.Log("Actual Stage:" + level);
    }

    private void SaveData()
    {
        if (GeneralDataManager.Instance == null)
        {
            Debug.Log("GeneralDataManager.Instance.KidID == null");
            return;
        }

        string saveKey = GeneralDataManager.Instance.KidID + "_SpatialCurrentLevel";

        PlayerPrefs.SetInt(saveKey, level);
        PlayerPrefs.Save();
    }

    public void LoadStudentData(SpatialData spatialData)
    {
        gameData = spatialData;
        level = spatialData.studentLevel;
        bestTime = spatialData.bestTime;
        Debug.Log("Actual Stage:" + level);
    }

    public void SaveStudentData()
    {
        gameData.studentLevel = level;
        gameData.bestTime = bestTime;
        spatialCommunication.SetResult(gameData);
    }

    [ContextMenu("Reset Student Level")]
    public void ResetData()
    {
        if (GeneralDataManager.Instance == null)
        {
            Debug.Log("GeneralDataManager.Instance.KidID == null");
            return;
        }

        string saveKey = GeneralDataManager.Instance.KidID + "_SpatialCurrentLevel";

        PlayerPrefs.SetInt(saveKey, 0);
        PlayerPrefs.Save();
    }

    private void ShowCorrect()
    {
        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            if (commonCubes.Contains(i))
            {
                //cubeButtons[i].StartPulsing();
                cubeGenerators[i].StartPulsing();
            }

            cubeGenerators[i].ShowNumber(true);
        }
    }

    private void HideAll()
    {
        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            cubeGenerators[i].ParticlePlay();
            cubeGenerators[i].ShowNumber(false);
        }
    }

    private IEnumerator Success()
    {
        fanfare.Play();

        foreach (var item in cubeGenerators)
        {
            item.ShowNumber(true);
        }

        yield return new WaitForSeconds(2f);

        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            if (!commonCubes.Contains(i))
            {
                cubeGenerators[i].ParticlePlay();
                cubeGenerators[i].ShowNumber(false);
            }
        }

        yield return new WaitForSeconds(1f);

        for (int i = 0; i < cubeGenerators.Count; i++)
        {
            if (commonCubes.Contains(i))
            {
                cubeGenerators[i].ParticlePlay();
                cubeGenerators[i].ShowNumber(false);
            }
        }
    }
}

public class Result
{
    public int level;
    public float time;
}