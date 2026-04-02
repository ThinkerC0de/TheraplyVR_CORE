using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Autohand.Demo;
using Dreamteck.Splines;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;


public class SceneManager_Butterflies : MonoBehaviour
{
    public static SceneManager_Butterflies Instance;
    
    public UnityEvent updateEvent = new UnityEvent();
    
    [SerializeField] private GameObject butterfly;
    [SerializeField] private GameObject bubble;
    [SerializeField] private GameObject ladybug;
    [SerializeField] private GameObject balloon;
    [SerializeField] private GameObject tree;
    [SerializeField] private GameObject magicStick_BluePrefab;
    [SerializeField] private GameObject magicStick_RedPrefab;
    public Material magicStickGrayMaterial;
    private Material magicStickRedMaterial;
    private Material magicStickBlueMaterial;
    private Material magicStick0;
    private Material magicStick1;
    public GameObject powerHeart;
    public GameObject awardGameObject;
    public int levelIndex = 1;
    public bool runOnStart = true;
    public int butterfliesCount = 9; //8-20
    public int balloonsCount = 3;
    public bool randomColors = false;
    public bool oneColor = false;
    public int collectedBubblesCount = 0;
    public int bubblesCountToCollect = 0;
    public int collectedButterfliesCount = 0;
    public int butterfliesCountToCollect = 0;
    public int collectedLadybugsCount = 0;
    public int ladybugsCountToCollect = 0;
    public AudioClip collected;
    public AudioClip levelComplitedSound;
    public AudioClip levelComplitedText;
    public AudioClip welcomeAudio;
    private AudioSource audioSource;
    public BoxCollider butterflyVolume;
    public List<GameObject> magicSticks;
    public GameObject blueStickSpawnPoint;
    public GameObject redStickSpawnPoint;

    public BoxCollider spaceToFly;
    public float speed;
    public XRHandControllerLink leftHand;
    public XRHandControllerLink rightHand;
    private System.DateTime startTime;
    public Vector3 butterflyVolumePosition;

    [SerializeField] private List<GameObject> listOfObjects = new List<GameObject>();
    [SerializeField] private List<GameObject> listOfBalloons;

    public PanelManager therapistPanelManager;
    public PanelManager kidPanelManager;

    private int sceneIndex = 1;

    private Stopwatch _stopwatch;
    private ButterfliesData _butterfliesData;
    public int badAnswers = 0;
    private ButterfliesLevelData _levelData;

    public AudioClip[] levelAudioDescritions;

    public GameObject[] balloons;

    public bool award = false;
    public bool firstGrab = true;
    private bool canChange = true;
    public ButterfliesGameCommunicator butterfliesGameCommunicator;
    public bool isScarred = false;
    public bool isSticksHaveButterfly = false;
    private Coroutine _awardCoroutine;
    private Coroutine _spawnButterflyCoroutine;
    private Coroutine _spawnLadybugCoroutine;
    private bool _gameFinishing = false; // true from CollectData until next UpdateLevel
    private bool _gameStarting;
    private bool _clearSceneInProgress;
    private bool _gameplayPaused;
    private readonly Dictionary<MagicStickPoint, bool> _pausedStickStates = new Dictionary<MagicStickPoint, bool>();
    private string _gameplayAppearedAtUtc = string.Empty;
    private float _gameplayAppearedAtElapsedSec = 0f;
    private bool _completionSnapshotCaptured;
    private string _completionEndDate = string.Empty;
    private float _completionDurationSec = 0f;

    public bool IsGameplayPaused => _gameplayPaused;
    public bool IsGameFinishing => _gameFinishing;
    public bool IsGameStarting => _gameStarting;
    public bool IsSceneTransitioning => _gameFinishing || _gameStarting || _clearSceneInProgress;
    public GameObject TreeObject => tree;
    public string GameplayAppearedAtUtc => _gameplayAppearedAtUtc;
    public float GameplayAppearedAtElapsedSec => _gameplayAppearedAtElapsedSec;
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
        }

        _butterfliesData = new ButterfliesData();
        _butterfliesData.levelData = new List<ButterfliesLevelData>();
        butterflyVolumePosition = butterflyVolume.transform.position;
        MarkGameplayAppearance();
    }

    private void Start()
    {
        //awardGameObject.SetActive(true);
        
        //przenieść do miejsca pierwszego złapania motyla
        //startTime = System.DateTime.Now;

        //SpawnButterflies();
        speed = GetSpeed();

        //Debug.Log(speed.ToString());

        if (runOnStart) UpdateLevel();

        if (!audioSource) gameObject.AddComponent<AudioSource>();
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
        audioSource.playOnAwake = false;

        magicStickBlueMaterial = magicStick_BluePrefab.GetComponent<Renderer>().sharedMaterial;
        magicStickRedMaterial = magicStick_RedPrefab.GetComponent<Renderer>().sharedMaterial;
    }

    float GetSpeed()
    {
        var coreSpeedData = butterfliesGameCommunicator != null ? butterfliesGameCommunicator.data : null;
        var hasCoreSpeedData = coreSpeedData != null && coreSpeedData.butterfliesCount > 0;
        int v = hasCoreSpeedData
            ? coreSpeedData.butterfliesSpeed
            : therapistPanelManager.speed.value;
        
        if (v == 0) 
            return 0.5f;
        else if (v == 1) 
            return 0.6f;
        else if (v == 2) 
            return 0.8f;
        return 0.5f;
    }

    public void RunOnStart()
    {
        runOnStart = true;
    }

    void Restart()
    {
        MarkGameplayAppearance();
        _gameFinishing = false;
        _gameStarting = false;
        _gameplayPaused = false;
        _pausedStickStates.Clear();
        _completionSnapshotCaptured = false;
        _completionEndDate = string.Empty;
        _completionDurationSec = 0f;
        runOnStart = true;
        collectedBubblesCount = 0;
        collectedButterfliesCount = 0;
        collectedLadybugsCount = 0;
        badAnswers = 0;
        award = false;
        firstGrab = true;
        canChange = true;
        isScarred = false;
        isSticksHaveButterfly = false;
        awardGameObject.SetActive(false);
        if (_awardCoroutine != null)
            StopCoroutine(_awardCoroutine);
    }

    private void MarkGameplayAppearance()
    {
        _gameplayAppearedAtUtc = LegacyInteractionTelemetry.CurrentUtcIso();
        _gameplayAppearedAtElapsedSec = LegacyInteractionTelemetry.CurrentRealtimeSec();
    }

    private void CaptureCompletionSnapshot()
    {
        if (_completionSnapshotCaptured)
        {
            return;
        }

        if (_stopwatch != null && _stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }

        _completionSnapshotCaptured = true;
        _completionEndDate = TheraplyHelpers.DateTimeNowToString();
        _completionDurationSec = _stopwatch != null
            ? (float)_stopwatch.Elapsed.TotalMilliseconds * 0.001f
            : 0f;
    }

    private void ClearCompletionSnapshot()
    {
        _completionSnapshotCaptured = false;
        _completionEndDate = string.Empty;
        _completionDurationSec = 0f;
    }

    public void PauseGameplay()
    {
        if (_gameplayPaused)
        {
            return;
        }

        _gameplayPaused = true;
        CacheAndDisableMagicSticks();

        foreach (var controller in FindObjectsByType<ButterflyController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(true);
        }

        foreach (var controller in FindObjectsByType<BubbleController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(true);
        }

        foreach (var controller in FindObjectsByType<LadybugController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(true);
        }
    }

    public void ResumeGameplay()
    {
        if (!_gameplayPaused)
        {
            return;
        }

        _gameplayPaused = false;

        foreach (var controller in FindObjectsByType<ButterflyController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(false);
        }

        foreach (var controller in FindObjectsByType<BubbleController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(false);
        }

        foreach (var controller in FindObjectsByType<LadybugController>(FindObjectsSortMode.None))
        {
            controller.SetGameplayPaused(false);
        }

        RestoreMagicSticks();
    }

    public bool SticksIsBusy()
    {
        bool busy = false;
        for (int i = 0; i < magicSticks.Count; i++)
            if (magicSticks[i].transform.GetChild(0).gameObject.GetComponent<MagicStickPoint>().haveButterfly)
                busy = true;
        return busy;
    }

    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.S))
        {
            RunOnStart();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            UpdateLevel();
        }
        */
    }

    public void UpdateLevel()
    {
        if (runOnStart)
        {
            _gameFinishing = false;
            _gameStarting = true;
            StartCoroutine("UpdateSceneCoroutine");
        }
    }


    IEnumerator Switch()
    {
        Restart();
        switch (levelIndex)
        {
            case 1:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_1"));
                    // audioSource.clip = levelAudioDescritions[0];
                    // audioSource.Play();
                    // Empty scene, only butterflies
                    // One hand, colorful butterflies
                    randomColors = true;
                    HideTree();
                    SpawnButterflies();
                    SpawnMagicStickBlue();
                    HideBalloons();
                    break;
                }
            case 2:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_2"));
                    // Empty scene, only butterflies
                    // Two hands, red and blue butterflies
                    HideTree();
                    SpawnButterflies();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 3:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_3"));
                    // Empty scene, butterflies and ladybugs
                    // Two hands, blue butterflies and ladybugs
                    HideTree();
                    SpawnButterflies(true);
                    SpawnLadybugs();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 4:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_4"));
                    // Empty scene, only butterflies and bubbles
                    // Two hands, blue butterflies and 2 x bubbles
                    HideTree();
                    SpawnButterflies(true);
                    SpawnBubbles();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 5:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_1"));
                    // Tree scene, only butterflies
                    // One hand, colorful butterflies
                    randomColors = true;
                    ShowTree();
                    SpawnButterflies();
                    SpawnMagicStickBlue();
                    HideBalloons();
                    break;
                }
            case 6:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_2"));
                    // Tree scene, only butterflies
                    // Two hands, red and blue butterflies
                    ShowTree();
                    SpawnButterflies();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 7:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_3"));
                    // Tree scene, butterflies and ladybugs
                    // Two hands, blue butterflies and ladybugs
                    ShowTree();
                    SpawnButterflies(true);
                    SpawnLadybugs();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 8:
                {
                    StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_4"));
                    // Tree scene, butterflies and bubbles 
                    // Two hands, blue butterflies and 2 x bubbles
                    ShowTree();
                    SpawnButterflies(true);
                    SpawnBubbles();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    HideBalloons();
                    break;
                }
            case 9:
                {
                    powerHeart.SetActive(true);
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_1")));
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("WatchoutToBaloon")));
                    // Empty scene, butterflies + balloons
                    // One hand, colorful butterflies
                    randomColors = true;
                    HideTree();
                    SpawnButterflies();
                    ShowBalloons();
                    SpawnMagicStickBlue();
                    break;
                }
            case 10:
                {
                    powerHeart.SetActive(true);
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_2")));
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("WatchoutToBaloon")));
                    // Empty scene, butterflies + balloons
                    // Two hands, red and blue butterflies
                    HideTree();
                    SpawnButterflies();
                    ShowBalloons();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    break;
                }
            case 11:
                {
                    powerHeart.SetActive(true);
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_3")));
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("WatchoutToBaloon")));
                    // Empty scene, butterflies + balloons + ladybugs
                    // Two hands, blue butterflies and ladybugs
                    HideTree();
                    SpawnButterflies(true);
                    ShowBalloons();
                    SpawnLadybugs();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    break;
                }
            case 12:
                {
                    powerHeart.SetActive(true);
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("Poziom_4")));
                    yield return (StartCoroutine(VirtualFriend.Instance.FriendTalking("WatchoutToBaloon")));
                    // Empty scene, butterflies + balloons + bubbles
                    // Two hands, blue butterflies and 2 x bubbles 
                    HideTree();
                    SpawnButterflies(true);
                    ShowBalloons();
                    SpawnBubbles();
                    SpawnMagicStickBlue();
                    SpawnMagicStickRed();
                    break;
                }
            default:
                {
                    // You should't be here
                    break;
                }
        }
        
        StartData();
        
        
    }

    public void ShowBalloons()
    {
        /*
        foreach (var balloon in listOfBalloons)
        {
            balloon.gameObject.SetActive(true);
            balloon.transform.GetChild(0).GetComponent<Rigidbody>().isKinematic = false;
            balloon.transform.GetChild(0).GetComponent<MeshCollider>().enabled = true;
        }*/
        foreach (var b in balloons)
        {
            GameObject.Instantiate(balloon, b.transform.position, Quaternion.identity);
            //balloon.gameObject.SetActive(true);
            //balloon.transform.GetChild(0).GetComponent<Rigidbody>().isKinematic = false;
            //balloon.transform.GetChild(0).GetComponent<MeshCollider>().enabled = true;
        }
    }

    public void HideBalloons()
    {
        /*
        foreach (var balloon in listOfBalloons)
        {
            balloon.gameObject.SetActive(false);
        }
        */
        
        /*
        foreach (var balloon in balloons)
        {
            balloon.gameObject.SetActive(false);
        }
        */
        
    }

    IEnumerator UpdateSceneCoroutine()
    {
        /*
        foreach (var balloon in balloons)
        {
            balloon.SetActive(false);
        }
        */
        collectedBubblesCount = 0;
        bubblesCountToCollect = 0;
        collectedButterfliesCount = 0;
        butterfliesCountToCollect = 0;
        collectedLadybugsCount = 0;
        ladybugsCountToCollect = 0;
        
        // Prefer CORE/Flutter data when it contains valid settings (butterfliesCount > 0).
        // Fall back to the VR therapist panel only when Flutter hasn't provided a config.
        var coreData = butterfliesGameCommunicator != null ? butterfliesGameCommunicator.data : null;
        var hasCoreData = coreData != null && coreData.butterfliesCount > 0;
        levelIndex = hasCoreData ? coreData.level : (int)therapistPanelManager.levelSlider.value;
        butterfliesCount = hasCoreData ? coreData.butterfliesCount : (int)therapistPanelManager.butterflySlider.value;
        
        kidPanelManager.catchedBubbles.size = 0;
        kidPanelManager.catchedButerflies.size = 0;
        kidPanelManager.catchedLadybugs.size = 0;
        kidPanelManager.desription.text = kidPanelManager.GetDescription();
        
        speed = GetSpeed(); // To zmienić

        while (_clearSceneInProgress)
        {
            yield return null;
        }

        yield return StartCoroutine(ClearSceneCoroutine());
        /*
                foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
                {
                    hand.GetComponent<XRHandControllerLink>().enabled = true;
                }
                */
    }

    public void UpdateKidPanel()
    {
        kidPanelManager.desription.text = kidPanelManager.GetDescription();
    }

    void ShowTree()
    {
        if (tree)
            tree.SetActive(true);
    }

    void HideTree()
    {
        if (tree)
            tree.SetActive(false);
    }

    void SpawnButterflies(bool oneColor = false)
    {
        if (_spawnButterflyCoroutine != null) StopCoroutine(_spawnButterflyCoroutine);
        _spawnButterflyCoroutine = StartCoroutine(SpawnButterfly(oneColor));
    }

    public void AwardButterfly(GameObject prefab, int count)
    {
        _awardCoroutine = StartCoroutine(AwardButterflyCoroutine(prefab, count));
    }
    
    IEnumerator AwardButterflyCoroutine(GameObject prefab, int count)
    {
        const float awardCleanupDelaySeconds = 0.5f;
        GameObject temp;
        BoxCollider tempBox = spaceToFly;
        spaceToFly = GameObject.Find("EndVolume").GetComponent<BoxCollider>();
        int butterfliesToSpawn = Mathf.Max(1, count);
        for (int i = 1; i <= butterfliesToSpawn; i++)
        {
            yield return null;
            temp = Instantiate(prefab, GetPointInVolume(tempBox), Quaternion.identity);
            yield return null;
            if (!temp) yield break;
            ButterflyController bc = temp.GetComponent<ButterflyController>();
            
            //Set name
            bc.name = "Butterfly_" + i;

            GameObject sc = new GameObject();
            sc.name = bc.name + "_Spline";
            sc.AddComponent<SplineComputer>();

            yield return null;

            bc._splineComputer = sc.GetComponent<SplineComputer>();
            bc.points[0].position = GetPointInVolume(spaceToFly);
            bc.points[1].position = GetPointInVolume(spaceToFly);
            bc.points[2].position = GetPointInVolume(spaceToFly);
            bc.points[3].position = GetPointInVolume(spaceToFly);

            bc.volume = spaceToFly;

            bc._splineComputer.SetPoints(bc.points);
            yield return null;
            if (!bc) yield break;
            bc._splineFollower = bc.GetComponent<SplineFollower>();
            bc._splineFollower.spline = sc.GetComponent<SplineComputer>();

            bc._splineFollower.onEndReached += bc.RegeneratePoints;
            yield return null;
            if (!bc) yield break;
            
            yield return null;
            if (!temp) yield break;
            listOfObjects.Add(temp);

            yield return null;
            if (!bc) yield break;
            //Set speed
            bc._splineComputer.space = SplineComputer.Space.World;
            bc._splineFollower.followSpeed = speed;
            bc._splineFollower.follow = enabled;
            bc._splineFollower.wrapMode = SplineFollower.Wrap.PingPong;
            spaceToFly = tempBox;
        }

        yield return new WaitWhile(() => award);

        var bf = FindObjectsByType<ButterflyController>(FindObjectsSortMode.None);

        foreach (var butterflyController in bf)
        {
            butterflyController.FlyAwayForGood();
        }
        yield return new WaitForSeconds(awardCleanupDelaySeconds);

        yield return StartCoroutine(ClearSceneCoroutine(false));
        awardGameObject.SetActive(false);
    }
    
    IEnumerator SpawnButterfly(bool oneColor, GameObject prefab = null)
    {
        GameObject temp;
        butterfliesCountToCollect = butterfliesCount;
        for (int i = 1; i <= butterfliesCount; i++)
        {
            if (prefab)
                temp = Instantiate(prefab, GetPointInVolume(spaceToFly), Quaternion.identity);
            else
                temp = Instantiate(butterfly, GetPointInVolume(spaceToFly), Quaternion.identity);

            yield return null;
            if (!temp) yield break;
            ButterflyController bc = temp.GetComponent<ButterflyController>();

            //Set name
            bc.name = "Butterfly_" + i;

            GameObject sc = new GameObject();
            sc.name = bc.name + "_Spline";
            sc.AddComponent<SplineComputer>();

            yield return null;

            bc._splineComputer = sc.GetComponent<SplineComputer>();
            
            bc.points[0].position = GetPointInVolume(spaceToFly);
            bc.points[1].position = GetPointInVolume(spaceToFly);
            bc.points[2].position = GetPointInVolume(spaceToFly);
            bc.points[3].position = GetPointInVolume(spaceToFly);

            bc.volume = spaceToFly;

            bc._splineComputer.SetPoints(bc.points);
            //bc._splineComputer.Close();
            yield return null;
            if (!bc) yield break;
            bc._splineFollower = bc.GetComponent<SplineFollower>();
            bc._splineFollower.spline = sc.GetComponent<SplineComputer>();

            bc._splineFollower.onEndReached += bc.RegeneratePoints;
            yield return null;
            if (!bc) yield break;
            bc.color = stickColor.random;

            //Set 2 colors
            if (randomColors == false)
            {
                bc.randomColor = false;

                if (i % 2 == 0)
                {
                    bc.transform.GetChild(0).GetComponent<MeshRenderer>().material.SetColor("_KOLOR", Color.red);
                    bc.color = stickColor.red;
                }
                else
                {
                    bc.transform.GetChild(0).GetComponent<MeshRenderer>().material.SetColor("_KOLOR", Color.blue);
                    bc.color = stickColor.blue;
                }

                if (oneColor == true)
                {
                    bc.transform.GetChild(0).GetComponent<MeshRenderer>().material.SetColor("_KOLOR", Color.blue);
                    bc.color = stickColor.blue;
                }
            }

            yield return null;
            if (!temp) yield break;
            listOfObjects.Add(temp);

            yield return null;
            if (!bc) yield break;
            //Set speed
            bc._splineComputer.space = SplineComputer.Space.World;
            bc._splineFollower.followSpeed = speed;
            bc._splineFollower.follow = enabled;
            bc._splineFollower.wrapMode = SplineFollower.Wrap.PingPong;
        }
    }

    void SpawnLadybugs()
    {
        if (_spawnLadybugCoroutine != null) StopCoroutine(_spawnLadybugCoroutine);
        _spawnLadybugCoroutine = StartCoroutine(SpawnLadybug());
    }

    IEnumerator SpawnLadybug()
    {
        GameObject temp;
        ladybugsCountToCollect = butterfliesCountToCollect;
        for (int i = 1; i <= butterfliesCount; i++)
        {
            Vector3 pos = GetPointInVolume(spaceToFly);

            yield return null;

            temp = Instantiate(ladybug, pos, Quaternion.identity);

            LadybugController lc = temp.GetComponent<LadybugController>();

            yield return null;
            //Set name
            lc.name = "Ladybug_" + i;

            lc.volume = spaceToFly;
            GameObject sc = new GameObject();
            sc.name = lc.name + "_Spline";
            sc.AddComponent<SplineComputer>();

            yield return null;

            lc._splineComputer = sc.GetComponent<SplineComputer>();
            lc._splineComputer.multithreaded = true;
            lc.points[0].position = GetPointInVolume(spaceToFly);
            lc.points[1].position = GetPointInVolume(spaceToFly);
            lc.points[2].position = GetPointInVolume(spaceToFly);
            //lc.points[3].position = GetPointInVolume(spaceToFly);
            lc._splineComputer.SetPoints(lc.points);

            yield return null;
            lc._splineFollower = lc.GetComponent<SplineFollower>();
            lc._splineFollower.spline = sc.GetComponent<SplineComputer>();

            lc._splineFollower.onEndReached += lc.RegeneratePoints;
            yield return null;

            listOfObjects.Add(temp);

            yield return null;

            //Set speed
            lc._splineFollower.followSpeed = speed;
        }
    }
    void SpawnBubbles()
    {
        bubblesCountToCollect = butterfliesCount * 2;
        for (int i = 0; i < butterfliesCount * 2; i++)
            listOfObjects.Add(Instantiate(bubble, GetPointInVolume(spaceToFly), Quaternion.identity));
    }

    void SpawnBalloons()
    {
        foreach (GameObject balloon in balloons)
        {
            balloon.SetActive(true);
        }
    }

    IEnumerator ClearSceneCoroutine(bool run = true)
    {
        if (_clearSceneInProgress)
        {
            yield break;
        }

        _clearSceneInProgress = true;

        // Stop any in-progress spawn coroutines so they don't continue spawning
        // objects after we destroy what's already in the scene.
        if (_spawnButterflyCoroutine != null) { StopCoroutine(_spawnButterflyCoroutine); _spawnButterflyCoroutine = null; }
        if (_spawnLadybugCoroutine != null) { StopCoroutine(_spawnLadybugCoroutine); _spawnLadybugCoroutine = null; }

        canChange = true;
        collectedBubblesCount = 0;
        collectedButterfliesCount = 0;
        collectedLadybugsCount = 0;
        randomColors = false;
        magicSticks = new List<GameObject>();
        listOfObjects = new List<GameObject>();

        isScarred = false;
        
        powerHeart.SetActive(false);
        
        var btObj = GameObject.FindObjectsByType<ButterflyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obj in btObj)
        {
            Destroy(obj.gameObject);
        }
        yield return null;

        var stObj = GameObject.FindObjectsByType<MagicStickPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obj in stObj)
        {
            Destroy(obj.transform.parent.gameObject);
        }
        yield return null;

        var sfObj = GameObject.FindObjectsByType<SplineFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obj in sfObj)
        {
            Destroy(obj.gameObject);
        }
        yield return null;

        var scObj = GameObject.FindObjectsByType<SplineComputer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obj in scObj)
        {
            Destroy(obj.gameObject);
        }
        yield return null;

        var bObj = GameObject.FindObjectsByType<BalloonController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obj in bObj)
        {
            Destroy(obj.gameObject);
        }
        yield return null;
        
        //HideBalloons();

        leftHand.enabled = true;
        rightHand.enabled = true;

        butterflyVolume.transform.position = butterflyVolumePosition;
        
        if (run)
        {
            yield return StartCoroutine(Switch());
        }

        _clearSceneInProgress = false;
    }

    private void DestroyObj(GameObject obj)
    {
        if (obj.transform.parent != null)
        {
            DestroyImmediate(obj.transform.parent.gameObject);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    public Vector3 GetPointInVolume(BoxCollider space)
    {
        if (!space) return Vector3.zero;
        Vector3 extents = space.size / 2f;
        Vector3 point = new Vector3(
            Random.Range(-extents.x, extents.x),
            Random.Range(-extents.y, extents.y),
            Random.Range(-extents.z, extents.z)
        ) + space.center;
        return space.transform.TransformPoint(point);
    }

    void SpawnMagicStickBlue()
    {
        magicSticks.Add(Instantiate(magicStick_BluePrefab, blueStickSpawnPoint.transform.position, Quaternion.Euler(90, 0, 0)));
        
        CheckMagicStick();
    }

    void SpawnMagicStickRed()
    {
        magicSticks.Add(Instantiate(magicStick_RedPrefab, redStickSpawnPoint.transform.position, Quaternion.Euler(90, 0, 0)));

        CheckMagicStick();
    }


    void CheckMagicStick()
    {
        StartCoroutine("CheckMagicStickCoroutine");
    }

    IEnumerator CheckMagicStickCoroutine()
    {
        while (magicSticks.Count == 0)
        {
            yield return null;
        }

        if (magicSticks.Count == 1)
        {
            firstGrab = false;
            magicSticks[0].GetComponentInChildren<MagicStickPoint>().isActive = true;
            Debug.Log("isActive: " + magicSticks[0].GetComponentInChildren<MagicStickPoint>().isActive);
        }
        else if (magicSticks.Count == 2)
        {
            magicStick0 = magicSticks[0].GetComponent<Renderer>().material;
            magicStick1 = magicSticks[1].GetComponent<Renderer>().material;
            
            magicSticks[1].GetComponent<Renderer>().material = magicStickGrayMaterial;
        }
    }

    public void SwapMagicStickActivity()
    {
        if (magicSticks.Count == 2)
        {
            if (!canChange) return;
            
            bool temp = magicSticks[0].GetComponentInChildren<MagicStickPoint>().isActive;
            magicSticks[0].GetComponentInChildren<MagicStickPoint>().isActive = !temp;
            magicSticks[1].GetComponentInChildren<MagicStickPoint>().isActive = temp;

            if (magicSticks[0].GetComponentInChildren<MagicStickPoint>().isActive)
            {
                magicSticks[0].GetComponent<Renderer>().material = magicStick0;
                magicSticks[1].GetComponent<Renderer>().material = magicStickGrayMaterial;
            }
            else if (magicSticks[1].GetComponentInChildren<MagicStickPoint>().isActive)
            {
                magicSticks[0].GetComponent<Renderer>().material = magicStickGrayMaterial;
                magicSticks[1].GetComponent<Renderer>().material = magicStick1;
            }

            if ((collectedLadybugsCount >= ladybugsCountToCollect) &&
                (collectedButterfliesCount >= butterfliesCountToCollect)) canChange = false;

        }
    }
    
    public void AddBubble()
    {
        collectedBubblesCount++;
        kidPanelManager.catchedBubbles.size = (float)collectedBubblesCount / butterfliesCount * 2;
        if (collectedBubblesCount == bubblesCountToCollect)
        {
            CheckPoints();
            BubblesCollected();
        }
    }
    

    public void AddButterfly()
    {
        collectedButterfliesCount += 1;

        kidPanelManager.catchedButerflies.size = (float)collectedButterfliesCount / butterfliesCount;
        if (collectedButterfliesCount == butterfliesCountToCollect)
        {
            CheckPoints();
            ButterfliesCollected();
        }
    }

    public void AddLadybug()
    {
        collectedLadybugsCount++;
        kidPanelManager.catchedLadybugs.size = (float)collectedLadybugsCount / butterfliesCount;
        if (collectedLadybugsCount == ladybugsCountToCollect)
        {
            CheckPoints();
            LadybugsCollected();
        }
    }

    private void CheckPoints()
    {
        if (_gameFinishing)
        {
            return;
        }

        StartCoroutine(CheckPointsCoroutine());
    }

    IEnumerator CheckPointsCoroutine()
    {
        //if (collectedButterfliesCount == 0) yield break;
        int condition = 0;

        if (collectedLadybugsCount >= ladybugsCountToCollect)
        {
            condition++;
        }

        if (collectedButterfliesCount >= butterfliesCountToCollect)
        {
            condition++;
        }

        if (collectedBubblesCount >= bubblesCountToCollect)
        {
            condition++;
        }

        Debug.Log("CheckPoints:" + condition);
        if (condition == 3)
        {
            if (_gameFinishing)
            {
                yield break;
            }

            _gameFinishing = true;
            CaptureCompletionSnapshot();
            yield return new WaitForSeconds(1);
            yield return StartCoroutine(ClearSceneCoroutine(false));
            //audioSource.clip = levelComplitedText;
            //audioSource.Play();
            StartCoroutine(VirtualFriend.Instance.FriendTalking("Congrats"));
            yield return new WaitForSeconds(2.5f);
            award = true;
            awardGameObject.SetActive(true);
            yield return new WaitWhile(() => VirtualFriend.Instance.IsTalking);
            yield return new WaitForSeconds(0.2f);
            audioSource.clip = levelComplitedSound;
            audioSource.Play();
            yield return new WaitWhile(() => audioSource.isPlaying);
            award = false;
            yield return new WaitWhile(() => awardGameObject != null && awardGameObject.activeSelf);
            CollectData();
           
            if (therapistPanelManager.levelSlider.value <= 12)
            {
                //therapistPanelManager.levelSlider.value++;
                //levelIndex = (int)therapistPanelManager.levelSlider.value;
                therapistPanelManager.levelSlider.value = levelIndex;
            }
            else
            {
                LoadMainScene();
                yield break;
            }

            kidPanelManager.nextBtn.GetComponent<Button>().interactable = true;
            kidPanelManager.backBtn.GetComponent<Button>().interactable = true;

            collectedBubblesCount = 0;
            collectedButterfliesCount = 0;
            collectedLadybugsCount = 0;

            ClearCompletionSnapshot();
            _gameFinishing = false;
            butterfliesGameCommunicator?.ApplyPendingDataIfAny();
        }
    }

    public void TooMuchFailures()
    {
        if (_gameFinishing)
        {
            return;
        }

        StartCoroutine(TooMuchFailuresCoroutine());
    }

    private IEnumerator TooMuchFailuresCoroutine()
    {
        _gameFinishing = true;
        CaptureCompletionSnapshot();
        yield return StartCoroutine(ClearSceneCoroutine(false));
        CollectData();
        ClearCompletionSnapshot();
        _gameFinishing = false;
        butterfliesGameCommunicator?.ApplyPendingDataIfAny();
    }

    public void NextLevel()
    {
        therapistPanelManager.levelSlider.value++;
        kidPanelManager.desription.text = kidPanelManager.GetDescription();
        kidPanelManager.startBtn.GetComponent<Button>().interactable = true;
        kidPanelManager.nextBtn.GetComponent<Button>().interactable = false;
        kidPanelManager.backBtn.GetComponent<Button>().interactable = true;
    }

    public void ClearScene()
    {
        if (_clearSceneInProgress)
        {
            return;
        }

        StartCoroutine(ClearSceneCoroutine(false));
    }

    public void StartStopwatch()
    {
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
    }

    Stopwatch StopStopwatch()
    {
        _stopwatch.Stop();
        return _stopwatch;
    }

    public void LoadMainScene()
    {
        SceneManager.LoadScene(0);
    }

    private void CacheAndDisableMagicSticks()
    {
        _pausedStickStates.Clear();
        foreach (var stick in FindObjectsByType<MagicStickPoint>(FindObjectsSortMode.None))
        {
            _pausedStickStates[stick] = stick.isActive;
            stick.isActive = false;
        }
    }

    private void RestoreMagicSticks()
    {
        foreach (var entry in _pausedStickStates)
        {
            if (entry.Key != null)
            {
                entry.Key.isActive = entry.Value;
            }
        }

        _pausedStickStates.Clear();
    }

    private void BubblesCollected()
    {
        if (bubblesCountToCollect == 0) return;
        audioSource.clip = collected;
        audioSource.Play();
    }

    private void ButterfliesCollected()
    {
        if (butterfliesCountToCollect == 0) return;
        audioSource.clip = collected;
        audioSource.Play();
    }

    private void LadybugsCollected()
    {
        if (ladybugsCountToCollect == 0) return;
        audioSource.clip = collected;
        audioSource.Play();
    }

    void CollectData() //TODO                                                     
    {
        if (_stopwatch != null && _stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }

        _levelData.level = levelIndex;
        _levelData.endDate = _completionSnapshotCaptured
            ? _completionEndDate
            : TheraplyHelpers.DateTimeNowToString();
        _levelData.levelCompletionTime = _completionSnapshotCaptured
            ? _completionDurationSec
            : (_stopwatch != null ? (float)_stopwatch.Elapsed.TotalMilliseconds * 0.001f : 0f);
        _levelData.numberOfBadChoices = badAnswers;
        
        if (GeneralDataManager.Instance)
        {
            _butterfliesData.kidID = GeneralDataManager.Instance.KidID;
            _butterfliesData.therapistID = GeneralDataManager.Instance.TherapistsID;
        }
        
        _butterfliesData.levelData.Add(_levelData);
        Debug.Log(JsonUtility.ToJson(_butterfliesData));

        GeneralDataManager.Instance.SaveDataToServer<ButterfliesData>(_butterfliesData);
        
        var gameTime = DateTimeOffset.Parse(_levelData.endDate).UtcDateTime -
                       DateTimeOffset.Parse(_levelData.startDate).UtcDateTime;
        
        Scoreboard_Chowany.Instance.bestTime.text = "Twój najlepszy czas gry - " + gameTime.Minutes +","+gameTime.Seconds;

        ButterfliesGameCommunicator pc = FindFirstObjectByType<ButterfliesGameCommunicator>();
        pc.SetData((int)_levelData.levelCompletionTime, badAnswers);
        butterfliesGameCommunicator.OnGameFinished("GameFinished:Łapanie motyli");

        LegacyInteractionTelemetry.EmitOutcome(
            "butterflies",
            "butterflies_complete",
            "Completed",
            "CORRECT",
            "ALL_BUTTERFLIES_COLLECTED",
            nameof(SceneManager_Butterflies),
            inputValue: _levelData.levelCompletionTime,
            extraDetails: new System.Collections.Generic.Dictionary<string, object>
            {
                { "level", levelIndex },
                { "badAnswers", badAnswers },
                { "butterfliesCount", butterfliesCountToCollect },
            });
        
        Debug.Log("game took: " + gameTime.Seconds);
        Debug.Log("game took by stopwatch: " + _levelData.levelCompletionTime);
    }

    void StartData()
    {
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
        _levelData = new ButterfliesLevelData();
        _levelData.startDate = TheraplyHelpers.DateTimeNowToString();
        _levelData.level = levelIndex;
        _levelData.speed = speed;
        _levelData.numberOfButterflies = butterfliesCount;
        _gameStarting = false;
        butterfliesGameCommunicator?.NotifyGameplayReady();
    }

    void LoadData()
    {

    }

    void Award()
    {
        
    }

    public void ResetLifes()
    {
        UI_LifeCounter.Instance.ResetLife();
    }

    void ResetStick(GameObject stick)
    {
        if (stick == null) return;
        Debug.Log("Reset stick: " + stick.name);
        var _stick = GetMagicStickPoint(stick);
        ButterflyController catchedButterfly;

        if (_stick.butterfly)
        {
            catchedButterfly = GetButterflyController(_stick.butterfly);
            catchedButterfly.isCatched = false;
            catchedButterfly.isCollected = false;
            catchedButterfly.Restart();
        }

        _stick.butterfly = null;
        _stick.haveButterfly = false;
    }

    MagicStickPoint GetMagicStickPoint(GameObject stick)
    {
        return stick.GetComponent<MagicStickPoint>();
    }

    ButterflyController GetButterflyController(GameObject butterfly)
    {
        return butterfly.GetComponent<ButterflyController>();
    }

    private void OnApplicationFocus(bool focus)
    {
        /*
        if (focus)
        {
            foreach (var magicStick in magicSticks)
            {
                if (magicStick != null)
                    ResetStick(magicStick);
            }
        }
        */
    }

    void test()
    {
        Debug.Log("test test test");
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            foreach (var magicStick in magicSticks)
            {
                ResetStick(magicStick);
            }
        }
    }

    public void WrongAnswersLostLifes()
    {
        StartCoroutine(VirtualFriend.Instance.FriendTalking("LostLife"));
    }
}

[Serializable]
public class ButterfliesData
{
    public string kidID;
    public string therapistID;
    public List<ButterfliesLevelData> levelData;
}

[Serializable]
public class ButterfliesLevelData
{
    public string startDate;
    public string endDate;
    public float levelCompletionTime;
    public int level;
    public float speed;
    public int numberOfButterflies;
    public int numberOfBadChoices;
}
