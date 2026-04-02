using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class ActiveMindfulness : MonoBehaviour
{
    [SerializeField] private PhasesController _phasesController;
    [SerializeField] private GameType gameType;
    [SerializeField] private List<ActiveMindfulnessButton> allButtons;
    [SerializeField] private List<ActiveMindfulnessButton> gameSequence;
    [SerializeField] private List<ColorGame> colorGameSequence;
    [SerializeField] private bool canTouch = false;
    
    public bool CanTouch => canTouch;
    [Space(10)]
    [SerializeField] private int minLevel = 2;
    [SerializeField] private int maxLevel = 7;
    [Range(2,7)]
    [SerializeField] private int level = 2;

    public int Level => level;

    [Space(10)]
    [SerializeField] private int rounds = 0;
    [SerializeField] private int maxRounds = 10;
    
    [Space(10)]
    [SerializeField] private int currentRound = 0;
    [SerializeField] private int points = 0;
    [Space(10)]
    [SerializeField] private int wrongAnswers = 0;
    [SerializeField] private int finalPoints = 0;
    [Space(10)]
    [SerializeField] private bool gameOn = false;
    [Space(10)]
    [SerializeField] private int correctCounter = 0;
    [Space(10)]
    [SerializeField] private List<Material> levelIndicatorsMaterials;

    [Space(10)]
    [SerializeField] private List<Material> colorButtonsMaterials;
    [Space(10)]
    [SerializeField] private List<HideButtons> colorButtons;

    [SerializeField] private ActiveMindfulnessServerData serverData;
    [SerializeField] private ActiveMindfulnessRound serverRound;

    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip rightAnswer;
    [SerializeField] private AudioClip wrongAnswer;
    private int levelOnStart;

    public ActiveMindfulnessCommunication activeMindfulnessCommunication;
    public enum GameType
    {
        GameOne,
        GameTwo,
        GameThree,
        GameFour
    }

    private void Awake()
    {
        foreach (ActiveMindfulnessButton btn in allButtons)
        {
            btn.GameController = this;
        }

        serverData = new ActiveMindfulnessServerData
        {
            rounds = new List<ActiveMindfulnessRound>()
        };
        
        serverRound = new ActiveMindfulnessRound
        {
            clicks = new List<bool>()
        };
    }

    private void Start()
    {
        LoadTheGame();
            
        if (gameType == GameType.GameOne)
        {
            LevelIndicators();
        }
    }

    public void ColorPicker(int index)
    {
        if(!canTouch) return;
        
        
        Debug.Log("INDEX: " + index + " COLOR: " + colorGameSequence[currentRound].colorIndex + " WARUNEK: " + (index == colorGameSequence[currentRound].colorIndex));
        Debug.Log("TYP: " + gameType);
        
        
        if (gameType == GameType.GameThree || gameType == GameType.GameFour)
        {
            Debug.Log("JESTEM W WARUNKU");
            
            canTouch = false;
            bool b = index == colorGameSequence[currentRound].colorIndex;
            Debug.Log(b);
            
            if (b)
            {
                float v = 0.06f + index * 0.125f;
                allButtons[colorGameSequence[currentRound].buttonIndex].SetColor(v);
                
                points++;
                _audioSource.clip = rightAnswer;
                _audioSource.Play();
                Debug.Log("DOBRZE");
                // StartCoroutine(VirtualFriend.Instance.FriendTalking("RightClick"));
            }
            else
            {
                wrongAnswers++;
                _audioSource.clip = wrongAnswer;
                _audioSource.Play();
                
                Debug.Log("ŹLE");
                // StartCoroutine(VirtualFriend.Instance.FriendTalking("WrongClick"));
                allButtons[colorGameSequence[currentRound].buttonIndex].SetColor(1.0f);
                allButtons[colorGameSequence[currentRound].buttonIndex].SetEmission(1.0f);
            }

            currentRound++;
            
            if (currentRound == colorGameSequence.Count)
            {
                StartCoroutine(ResetAllButtons());
                gameOn = false;
                canTouch = false;
                currentRound = 0;

                rounds++;
                
                Debug.Log("KONIEC SEKWENCJI!");
                OnSequenceEnded();
            }
            else
            {
                CristalLightUp();
            }
        }
    }

    IEnumerator ResetColor()
    {
        Debug.Log("RESETUJĘ!!!!!!!!!");
        yield return new WaitForSeconds(0.5f);
        for (int i = 0; i < level; i ++)
        {
            allButtons[i].SetColor(1.0f);
            allButtons[i].SetEmission(1.0f);
        }
    }

    IEnumerator ResetAllButtons()
    {
        yield return new WaitForSeconds(0.5f);
        foreach (ActiveMindfulnessButton btn in allButtons)
        {
            btn.SetColor(1.0f);
            btn.SetEmission(1.0f);
        }
    }
    
    public void CristalLightUp()
    {
        StartCoroutine(LightUp());
    }

    IEnumerator LightUp()
    {
        yield return new WaitForSeconds(0.4f);
        allButtons[colorGameSequence[currentRound].buttonIndex].SetEmissionPower(20.0f);
        canTouch = true;
    }

    public void GenerateSequence()
    {
        canTouch = false;
        gameSequence.Clear();
        for (int i = 0; i < level; i++)
        {
            ActiveMindfulnessButton btn = allButtons[Random.Range(0, allButtons.Count)];
            gameSequence.Add(btn);
        }
    }
   
    public void GenerateColorSequence(bool randomCrystals = false)
    {
        colorGameSequence.Clear();
        
        canTouch = false;
        if (!randomCrystals)
        {
            for (int i = 0; i < level; i++)
            {
                int colorIndex = Random.Range(0, colorButtonsMaterials.Count);
                ColorGame btn = new ColorGame
                {
                    buttonIndex = i,
                    colorValue = 0.06f + colorIndex * 0.125f,
                    colorIndex =  colorIndex
                };
                colorGameSequence.Add(btn);
            }   
        }
        else
        {
            List<int> availableIndexes = new List<int>();
            for (int a = 0; a < allButtons.Count; a++)
            {
                availableIndexes.Add(a);
            }
            
            for (int i = 0; i < level; i++)
            {
                int index = Random.Range(0, availableIndexes.Count);
                
                int colorIndex = Random.Range(0, colorButtonsMaterials.Count);
                ColorGame btn = new ColorGame
                {
                    buttonIndex = availableIndexes[index],
                    colorValue = 0.06f + colorIndex * 0.125f,
                    colorIndex =  colorIndex
                };

                availableIndexes.Remove(availableIndexes[index]);
                
                colorGameSequence.Add(btn);
            }
        }
    }

    [ContextMenu("PLAY")]
    public void PlaySequence()
    {
        if(gameOn) return;
        gameOn = true;
        StartCoroutine(Seq());
    }

    IEnumerator Seq()
    {
        if (gameType == GameType.GameOne)
        {
            foreach (ActiveMindfulnessButton btn in gameSequence)
            {
                btn.OnButtonHighlighted();
                yield return new WaitForSeconds(1.0f);
            }

            canTouch = true;
        }
        else if (gameType == GameType.GameTwo)
        {
            foreach (ActiveMindfulnessButton btn in gameSequence)
            {
                btn.OnButtonHighlighted();
                while (VirtualFriend.Instance.IsTalking)
                {
                    yield return null;
                }
            }

            canTouch = true;
        }
        else if (gameType == GameType.GameThree)
        {
            foreach (ColorGame btn in colorGameSequence)
            {
                allButtons[btn.buttonIndex].BlinkColor(btn.colorValue, 3.0f);
                Debug.Log(btn.buttonIndex);
                yield return new WaitForSeconds(3.0f);
            }

            CristalLightUp();
            canTouch = true;
        }
        else if (gameType == GameType.GameFour)
        {
            foreach (ColorGame btn in colorGameSequence)
            {
                allButtons[btn.buttonIndex].BlinkColor(btn.colorValue, 3.0f);
                Debug.Log(btn.buttonIndex);
                yield return new WaitForSeconds(3.0f);
            }

            CristalLightUp();
            canTouch = true;
        }
    }

    public bool CheckIfCorrect(ActiveMindfulnessButton btn)
    {
        if (btn == gameSequence[currentRound])
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    
    public void SequenceCrystalsOnClicked(ActiveMindfulnessButton btn)
    {
        if(!gameOn) return;

        int index = allButtons.IndexOf(btn);
        
        if (btn == gameSequence[currentRound])
        {
            points++;
        }
        else
        {
            wrongAnswers++;
        }
        currentRound++;
   
        if (currentRound >= gameSequence.Count)
        {
            gameOn = false;
            canTouch = false;
            currentRound = 0;

            rounds++;
            
            OnSequenceEnded();
        }
    }

    void OnSequenceEnded()
    {
        StartCoroutine(OnSequenceEndedEnum());
    }

    IEnumerator OnSequenceEndedEnum()
    {
        if (points == level)
        {
            correctCounter++;
        }
        else
        {
            correctCounter = 0;
        }
        
        if (correctCounter != 3)
        {
            Debug.Log("MOWA NA KONIEC RUNDY");
            if(rounds < maxRounds) yield return StartCoroutine(VirtualFriend.Instance.FriendTalking(points == level ? "Good_round" : "Bad_round"));
        }

        AddClick(points == level, level);
        
        // StartCoroutine(ResetColor());

        finalPoints += points;
        points = 0;
        
       if (rounds < maxRounds)
       {
           if (correctCounter == 3)
           {
               Debug.Log("Idziemy na wyższy poziom!");
               level++;
               RoundFinished();

               sayOnRound = true;
               LevelIndicators();
               rounds = 0;
               correctCounter = 0;
               yield return VirtualFriend.Instance.FriendTalking("Round_finished");
           }

           yield return new WaitForSeconds(1.0f);
           SaveTheGame();
           StartTheGame();
       }
       else
       {
           if (correctCounter == 3)
           {
               Debug.Log("Idziemy na wyższy poziom!");
               level++;
               RoundFinished();
               
               sayOnRound = true;
               LevelIndicators();
               rounds = 0;
               correctCounter = 0;
               yield return VirtualFriend.Instance.FriendTalking("Round_finished");
               
               SaveTheGame();
               StartTheGame();
           }
           else
           {
               StartCoroutine(GameEnded());
               foreach (ActiveMindfulnessButton btn in allButtons)
               {
                   // btn.ToggleGameButton(false);
               }
           }
       }
    }

    [SerializeField] private bool sayOnStart = true;
    [SerializeField] private bool sayOnRound = true;
    
    IEnumerator GameEnded()
    {
        foreach (HideButtons btn in colorButtons)
        {
            btn.MainButtonState(false);
        }
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_Ended"));
        SaveTheGame();
        RoundFinished();
        _phasesController.StartPhase(2);
        // ResetParameters();
        // GeneralDataManager.Instance.SaveDataToServer(serverData);

        ActiveMindfulnessCommunication activeMindfulnessCommunication =
            FindFirstObjectByType<ActiveMindfulnessCommunication>();
        
        // this.activeMindfulnessCommunication.SetData();
        
        //TODO tutaj mogę zapisać dane do fluttera
        // activeMindfulnessCommunication.OnGameFinished("GameFinished:AM");
    }

    [ContextMenu("START THE GAME")]
    public void StartTheGame()
    {
        // foreach (ActiveMindfulnessButton btn in allButtons)
        // {
        //     btn.ToggleGameButton(true);
        // }

        // for (int i = 0; i < allButtons.Count; i++)
        // {
        //     allButtons[i].ToggleGameButton(true);
        // }

        Debug.Log("START THE GAME FUNCKCJA");

        switch (gameType)
        {
            case GameType.GameOne:
                foreach (ActiveMindfulnessButton btn in allButtons)
                {
                    btn.ToggleGameButton(true);
                }
                Debug.Log("GameOne");
                StartCoroutine(GameStartEnum());
                break;
            case GameType.GameTwo:
                foreach (ActiveMindfulnessButton btn in allButtons)
                {
                    
                    btn.ToggleGameButton(true);
                }
                Debug.Log("GameTwo");
                StartCoroutine(GameStartEnum());
                break;
            case GameType.GameThree:
                foreach (HideButtons btn in colorButtons)
                {
                    btn.MainButtonState(true);
                }
                StartCoroutine(GameStartEnum());
                break;
            case GameType.GameFour:
                StartCoroutine(ResetColor());
                foreach (HideButtons btn in colorButtons)
                {
                    btn.MainButtonState(true);
                }
                StartCoroutine(GameStartEnum());
                break;
        }
    }
    
    

    public void ResetParameters()
    {
        rounds = 0;
        currentRound = 0;
        points = 0;
        wrongAnswers = 0;
        finalPoints = 0;
        canTouch = false;
    }

    public void ButtonsShower(bool b)
    {
        foreach (var btn in allButtons)
        {
            btn.ToggleGameButton(b);
        }
    }

    IEnumerator GameStartEnum()
    {
        if (gameType == GameType.GameOne || gameType == GameType.GameTwo)
        {
            GenerateSequence();

            if (sayOnStart)
            {
                sayOnStart = false;
                Debug.Log("SAY ON START");
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_1_instruction"));
            }       
         
            if (sayOnRound)
            {
                Debug.Log("SAY ON ROUND");
                sayOnRound = false;
                string key = level.ToString() + "_crystals";
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking(key));
            }

            yield return new WaitForSeconds(1.0f);
            PlaySequence(); 
        }
        else if(gameType == GameType.GameThree)
        {
            GenerateColorSequence();
            

            if (sayOnStart)
            {
                sayOnStart = false;
                
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_3_instruction"));
                
            }

            if (sayOnRound)
            {
                sayOnRound = false;
                string key = level.ToString() + "_crystals";
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking(key));
            }

            yield return new WaitForSeconds(1.0f);
            PlaySequence();
        }
        else if(gameType == GameType.GameFour)
        {
            GenerateColorSequence(true);

            if (sayOnStart)
            {
                sayOnStart = false;
                
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Game_3_instruction"));
            }

            if (sayOnRound)
            {
                sayOnRound = false;
                string key = level.ToString() + "_crystals";
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking(key));
            }
            
            yield return new WaitForSeconds(1.0f);
            PlaySequence();
        }
    }

    public void LevelIndicators()
    {
        Debug.Log("WSKAZNIKI!");
        foreach (Material mat in levelIndicatorsMaterials)      
        {
            mat.SetFloat("_PULSOWANIE", 0.0f);
        }
        
        for (int i = 0; i < level-1; i++)
        {
            levelIndicatorsMaterials[i].SetFloat("_PULSOWANIE", 1.0f);
        }
    }

    private void SaveTheGame()
    {
        string saveKey = GeneralDataManager.Instance.KidID + "_" + gameType.ToString();
        Debug.Log("ZAPISUJĘ GRĘ: " + saveKey);
        PlayerPrefs.SetInt(saveKey, level);
        
        serverData.kidID = GeneralDataManager.Instance.KidID;
        serverData.therapistID = GeneralDataManager.Instance.TherapistsID;
        serverData.finalPoints = finalPoints;
        serverData.chosenGame = (int)gameType;
        serverData.gameFinishedDate = TheraplyHelpers.DateTimeNowToString();
    }

    public void SetStartDateTime()
    {
        serverData.gameStartDate = TheraplyHelpers.DateTimeNowToString();
    }
    
    private void LoadTheGame()
    {
        string saveKey = GeneralDataManager.Instance.KidID + "_" + gameType.ToString();
        Debug.Log("WCZYTUJĘ GRĘ: " + saveKey);
        level = PlayerPrefs.GetInt(saveKey, 2);
        Debug.Log("LEVEL: " + level);
        levelOnStart = level;
        // LevelIndicators();
    }

    public void AddClick(bool click, int _level)
    {
        serverRound.level = _level;
        serverRound.clicks.Add(click);
    }

    void RoundFinished()
    {
        serverData.rounds.Add(serverRound);
        serverRound = new ActiveMindfulnessRound
        {
            clicks = new List<bool>()
        };
    }
    
    public int GetLevelProgress()
    {
        return level - levelOnStart;
    }
}

[Serializable]
public class ColorGame
{
    public int buttonIndex = 0;
    public float colorValue = 0;
    public int colorIndex = 0;
}

[Serializable]
public class ActiveMindfulnessServerData
{
    public string kidID;
    public string therapistID;
    public int chosenGame;
    public int finalPoints;
    public string gameStartDate;
    public string gameFinishedDate;
    public List<ActiveMindfulnessRound> rounds;
}

[Serializable]
public class ActiveMindfulnessRound
{
    public int level;
    public List<bool> clicks;
}