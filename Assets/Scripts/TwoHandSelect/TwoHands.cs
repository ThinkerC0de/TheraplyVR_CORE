using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TheraplyVR.Pairs.Basic;
using TMPro;
using UnityEngine;

public class TwoHandData
{
    public int rows;
    public int repeatCount;
    public int[] symbolsDrawn;
    public bool isTimedSession;
    public List<Symbol> symbols;
}

public abstract class TwoHands : MonoBehaviour
{
    public List<AudioClip> congratAudioClips;

    [HideInInspector] public int markerGrabbed;

    [SerializeField] protected bool isSingleObjectSession;
    [SerializeField] protected GameObject bookmark;
    [SerializeField] protected int badMovesCount;
    [SerializeField] protected int totalBadMovesCount;
    [SerializeField] protected BasicSessionManager manager;
    [SerializeField] protected TwoHandTutorial tutorial;
    [SerializeField] protected Transform symbol_middle;
    [SerializeField] protected Transform symbol_left;
    [SerializeField] protected Transform symbol_right;
    [SerializeField] protected Transform[] placeL;
    [SerializeField] protected Transform[] placeR;
    [SerializeField] protected List<GameObject> objectSets;
    [SerializeField] protected float showDelay = 10f;
    [SerializeField] protected float maxSelectionDelay = 1f;
    [SerializeField] protected float checkingTime = 3f;
    [SerializeField] protected TextMeshProUGUI label;

    protected Symbol symbol;
    protected OneSymbol oneSymbol;
    protected bool grabOneHand;
    protected bool gameActive;
    protected bool firstCheck = true;
    protected bool firstRun = true;
    protected bool firstMemoryRun = false;
    protected float lastEventTime;
    protected float elapsedTime;
    protected TwoHandData gameData;
    protected int actualRow;
    protected float timeSum;
    protected int gameCount;
    protected BasicSessionResult result;
    protected float rightTime;
    protected float leftTime;
    protected Symbol selectedLeft;
    protected Symbol selectedRight;

    protected int selectedSymbols;
    protected bool badMoveActive;
    protected float bestTime;
    protected int patternsCount;

    /// <summary>
    /// Prepar game data
    /// </summary>
    public void StartSession(BasicSessionConfig config)
    {
        gameData = new TwoHandData();
        gameData.symbols = new List<Symbol>();
        gameData.isTimedSession = config.memoryMode;
        gameData.repeatCount = config.repetitions;
        gameData.rows = config.rows;

        result = new BasicSessionResult();
        timeSum = 0;
        actualRow = 0;
        selectedLeft = null;
        selectedRight = null;
        totalBadMovesCount = 0;
        firstMemoryRun = gameData.isTimedSession;
        gameCount = gameData.repeatCount;

        Debug.Log("Game Left: " + gameCount);

        if (markerGrabbed >= 2)
        {
            gameActive = true;
            Session();
        }
    }

    /// <summary>
    /// Prepar game
    /// </summary>
    protected virtual void Session()
    {
        gameCount--;
        Debug.Log("Game Left: " + gameCount);
        badMovesCount = 0;
        actualRow = 0;

        RandomizeDisplayedObjects();
        RandomizeSideObjects(gameData.rows);
        ShowTemplate(true);

        if (gameData.isTimedSession)
        {
            if (firstMemoryRun)
            {
                StartCoroutine(HideAndShowObjects());
                tutorial.StartTutorial(gameData, isSingleObjectSession);
                firstMemoryRun = false;
            }
            else
            {
                StartCoroutine(HideAndShowObjects());
                manager.ActivateMarkers(true);
            }
        }
        else
        {
            if (firstRun)
            {
                tutorial.StartTutorial(gameData, isSingleObjectSession);
                firstRun = false;
            }
            else
            {
                manager.ActivateMarkers(true);
            }
        }

        ResetTimer();
        BlockSymbolsRange();
    }

    /// <summary>
    /// Random pattern symbols and set them in the center
    /// </summary>
    protected void RandomizeDisplayedObjects()
    {
        if (isSingleObjectSession)
        {
            patternsCount = 1;
        }
        else
        {
            patternsCount = 2;
        }

        GameObject[] displayedSymbols = new GameObject[patternsCount];

        int oldRandom = -1;
        int newRandom = -1;

        gameData.symbolsDrawn = new int[patternsCount];
        

        for (int i = 0; i < displayedSymbols.Length; i++)
        {
            while (oldRandom == newRandom)
            {
                newRandom = Random.Range(0, objectSets.Count);
            }

            oldRandom = newRandom;

            if (isSingleObjectSession)
            {
                bookmark.SetActive(false);
                displayedSymbols[i] = Instantiate(objectSets[newRandom], symbol_middle);
                oneSymbol = displayedSymbols[i].GetComponent<OneSymbol>();
            }
            else if (i == 0)
            {
                bookmark.SetActive(true);
                displayedSymbols[i] = Instantiate(objectSets[newRandom], symbol_left);
            }
            else
            {
                displayedSymbols[i] = Instantiate(objectSets[newRandom], symbol_right);
            }

            symbol = displayedSymbols[i].GetComponent<Symbol>();
            symbol.twoHands = this;
            gameData.symbols.Add(symbol);

            gameData.symbolsDrawn[i] = symbol.SetAsPattern(!firstRun);
        }
    }

    /// <summary>
    /// Set symbols on the sides
    /// </summary>
    protected virtual void RandomizeSideObjects(int count)
    {
        List<GameObject> selected = GetRandomPrefabs(count, true);

        for (int i = 0; i < selected.Count; i++)
        {
            var go = Instantiate(selected[i], placeL[i]);
            symbol = go.GetComponent<Symbol>();
            symbol.twoHands = this;
            symbol.isLeft = true;
            gameData.symbols.Add(symbol);
        }

        selected = GetRandomPrefabs(count, false);

        for (int i = 0; i < selected.Count; i++)
        {
            var go = Instantiate(selected[i], placeR[i]);
            symbol = go.GetComponent<Symbol>();
            symbol.twoHands = this;
            symbol.isLeft = false;
            gameData.symbols.Add(symbol);
        }
    }

    /// <summary>
    /// Random prefabs
    /// </summary>
    protected List<GameObject> GetRandomPrefabs(int count, bool left)
    {
        List<GameObject> selected = new List<GameObject>();

        for (int i = 0; i < count; i++)
        {
            //Add template prefabs to the list of random objects
            if (left)
            {
                selected.Add(objectSets[gameData.symbolsDrawn[0]]);
            }
            else
            {
                selected.Add(objectSets[gameData.symbolsDrawn[1]]);
            }

            //Randomize remaining prefabs
            while (selected.Count % 4 != 0)
            {
                int randomIndex = Random.Range(0, objectSets.Count);
                selected.Add(objectSets[randomIndex]);
            }

            MixingList(selected);
        }

        return selected;
    }

    /// <summary>
    /// Mixing prefabs list
    /// </summary>
    protected void MixingList(List<GameObject> list)
    {
        int firstInRow = list.Count - 4;
        GameObject temp = list[firstInRow];
        int randomIndex = Random.Range(firstInRow+1, list.Count);
        list[firstInRow] = list[randomIndex];
        list[randomIndex] = temp;
    }

    /// <summary>
    /// Timed session, hide the teplate objects after the declared time
    /// </summary>
    protected virtual IEnumerator HideAndShowObjects()
    {
        //Show template symbols
        ShowTemplate(true);

        //Hide side symbols
        foreach (Transform place in placeL)
        {
            place.gameObject.SetActive(false);
        }

        foreach (Transform place in placeR)
        {
            place.gameObject.SetActive(false);
        }

        var delay = firstMemoryRun ? 3*showDelay : 0.5f*showDelay;
        yield return new WaitForSeconds(delay);

        //Hide template symbols
        ShowTemplate(false);

        //Show side symbols
        foreach (Transform place in placeL)
        {
            place.gameObject.SetActive(true);
        }

        foreach (Transform place in placeR)
        {
            place.gameObject.SetActive(true);
        }

        ResetTimer();
    }

    /// <summary>
    /// Set Active template objects
    /// </summary>
    protected virtual void ShowTemplate(bool state)
    {
        symbol_middle.gameObject.SetActive(state);
        symbol_left.gameObject.SetActive(state);
        symbol_right.gameObject.SetActive(state);
    }

    /// <summary>
    /// Check if the selected symbol is a template one
    /// </summary>
    public virtual IEnumerator Check(bool isLeft, Symbol selected)
    {
        bool result = false;
        bool toSlow = false;
        SetSelected(selected, isLeft);

        if (selectedSymbols == 1)
        {
            Debug.Log("First Slection");
        }
        else if (selectedSymbols >= 2)
        {
            Debug.Log("Secound Slection " + selectedSymbols);
            //Second symbol selected
            if (Mathf.Abs(leftTime - rightTime) < maxSelectionDelay)
            {
                Debug.Log("Good selection time - wait");
                //Good time between selected symbols
                yield return new WaitForSeconds(checkingTime);
                Debug.Log("End waiting");

                if (selectedLeft != null && selectedRight != null)
                {
                    Debug.Log("Oba dalej aktywne");
                    //Still two symbols chosen
                    if (selectedLeft.symbolNumber == gameData.symbolsDrawn[0]
                        && selectedRight.symbolNumber == gameData.symbolsDrawn[1])
                    {
                        Debug.Log("Oba dalej prawidłowe: " + gameData.symbolsDrawn[0] + " " + gameData.symbolsDrawn[1]);
                        //Good choice
                        result = true;
                        actualRow++;
                        if (actualRow >= gameData.rows)
                        {
                            Debug.Log("AllGood");
                            AllGood();
                        }
                        else
                        {
                            Debug.Log("Repeat");
                            Repeat();
                            ResetTimer();
                        }
                        badMovesCount = 0;

                        manager.PlayFeedbackSound(true);
                    }
                    else
                    {
                        BadMove();
                    }
                }
            }
            else
            {
                if (selectedLeft != null || selectedRight != null 
                    || selectedLeft.symbolNumber != gameData.symbolsDrawn[0]
                    || selectedRight.symbolNumber != gameData.symbolsDrawn[1])
                {
                    BadMove();
                }
                else
                {
                    Debug.Log("Too long time between selected symbols");
                    //Too long time between selected symbols
                    toSlow = true;
                }
            }

            Debug.Log("Change color ");// + selectedLeft.transform.parent + " " + selectedRight.transform.parent);
            if (selectedLeft != null) StartCoroutine(selectedLeft.ChangeColorAfterDelay(result, toSlow));
            if (selectedRight != null) StartCoroutine(selectedRight.ChangeColorAfterDelay(result, toSlow));

            yield return new WaitForSeconds(1f);
            ShowHint();

            selectedLeft = null;
            selectedRight = null;
            selectedSymbols = 0;
            badMoveActive = false;
        }
    }

    protected virtual void BadMove()
    {
        if (!badMoveActive)
        {
            //Bad choice
            badMoveActive = true;
            Debug.Log("Bad choice");        
            badMovesCount++;
            totalBadMovesCount++;

            manager.PlayFeedbackSound(false);
        }
    }

    /// <summary>
    /// Clear sellected symbols
    /// </summary>
    protected virtual void ClearSelected()
    {
        selectedLeft = null;
        selectedRight = null;
        firstCheck = true;
    }

    /// <summary>
    /// Block only unactive symbols
    /// </summary>
    protected void BlockSymbolsRange()
    {
        int min = actualRow * 4 + patternsCount;    //+2 - dla symoli wzorcowych 
        int max = min + 3;              //+3 - dla kolejnych symboli w rzędzie

        int leftSideCount = gameData.rows * 4;

        for (int i = patternsCount; i < gameData.symbols.Count; i++)
        {
            if(i >= min && i <= max
                || i >= min + leftSideCount && i <= max + leftSideCount)
            {
                gameData.symbols[i].SetAsPattern(false);
            }
            else
            {
                gameData.symbols[i].SetAsPattern(true);
            }   
        }
    }

    /// <summary>
    /// Update lastEventTime for start calculate result of game
    /// </summary>
    public void ResetTimer()
    {
        lastEventTime = Time.time;
    }

    /// <summary>
    /// Show hint after bad move
    /// </summary>
    public virtual void ShowHint()
    {
        ClearSelected();
        if (badMovesCount == 1)
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_bad"));
        }
        else if (badMovesCount >= 2)
        {
            badMovesCount = 0;
            DetectSymbolLeft(gameData.symbolsDrawn[0]);
            DetectSymbolRight(gameData.symbolsDrawn[1]);
        }
    }

    /// <summary>
    /// Find symbols with the given number on the left side
    /// </summary>
    protected void DetectSymbolLeft(int symbolNumber)
    {
        foreach (var sym in gameData.symbols)
        {
            if (sym != null && sym.symbolNumber == symbolNumber)
            {
                //Check if symbol is a child of placeL
                if (sym.isLeft)
                {
                    sym.Show();
                }
            }
        }
    }

    /// <summary>
    /// Find symbols with the given number on the right side
    /// </summary>
    protected void DetectSymbolRight(int symbolNumber)
    {
        foreach (var sym in gameData.symbols)
        {
            if (sym != null && sym.symbolNumber == symbolNumber)
            {
                //Check if symbol is a child of placeR
                if (!sym.isLeft)
                {
                    sym.Show();
                }
            }
        }
    }

    /// <summary>
    /// End of the current round and radomize new pattern
    /// </summary>
    protected void AllGood()
    {
        badMovesCount = 0;

        Congrats();

        Debug.Log("----> all good");
        selectedLeft = null;
        selectedRight = null;
        firstCheck = true;

        StartCoroutine(Clear());
        Repeat();
    }

    /// <summary>
    /// Ending the current round
    /// </summary>
    protected virtual void Repeat()
    {
        elapsedTime = Time.time - lastEventTime - checkingTime;
        timeSum += elapsedTime;

        if (actualRow >= gameData.rows && gameCount <= 0)
        {
            FinishGame();
        }
        else
        {          
            BlockSymbolsRange();
        }
    }

    /// <summary>
    /// Ending the current game
    /// </summary>
    private void FinishGame()
    {
        result.badMovesCount = totalBadMovesCount;
        result.averageReactionTime = timeSum / (gameData.repeatCount * gameData.rows);
        manager.GameFinished(result);
        Debug.Log("Super! To już koniec poziomu.");
        label.text = "Twój średni czas: " + result.averageReactionTime;
        StartCoroutine(VirtualFriend.Instance.FriendTalkingOtherTable("chowany", "LevelCompleted"));
    }

    /// <summary>
    /// Clear and repeat session
    /// </summary>
    protected virtual IEnumerator Clear()
    {
        yield return new WaitForSeconds(1f);

        foreach (Symbol symbol in gameData.symbols)
        {
            symbol.gameObject.SetActive(false);
        }

        foreach (Symbol symbol in gameData.symbols)
        {
            Destroy(symbol.gameObject);
        }

        gameData.symbols.Clear();

        if (gameCount > 0)
        {
            manager.ChangePage();
            yield return new WaitForSeconds(2f);

            Session();
        }
        else
        {
            VirtualFriend.Instance.GoodJob();
            gameActive = false;
        }
    }

    /// <summary>
    /// Set selected left or right symbol
    /// </summary>
    public virtual void SetSelected(Symbol symbol,bool isLeft)
    {
        if (symbol == null)
        {
            selectedSymbols--;
            if (selectedSymbols < 0)
            {
                selectedSymbols = 0;
            }
        }
        else
        {
            selectedSymbols++;
        }

        if (isLeft)
        {
            selectedLeft = symbol;
            leftTime = Time.time;
        }
        else
        {
            selectedRight = symbol;
            rightTime = Time.time;
        }
    }

    /// <summary>
    /// Check is all markers grabed
    /// </summary>
    public void MarkerGrab(bool state)
    {
        if (state)
        {
            markerGrabbed++;
            if (grabOneHand && !gameActive)
            {
                //Second grab
                gameActive = true;
                Session();
            }
        }
        else
        {
            markerGrabbed--;
        }

        //First grab
        grabOneHand = state;
    }

    public void Congrats()
    {
        Debug.Log("Correct Answer");

        if (bestTime > elapsedTime || bestTime <= 0)
        {
            bestTime = elapsedTime;
            StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_good"));
        }
        else
        {
            var _congratsRandomNumbers = TheraplyHelpers.GenerateNumbers(0, congratAudioClips.Count - 1);
            string key = congratAudioClips[TheraplyHelpers.GetIntFromList(_congratsRandomNumbers)].name;

            StartCoroutine(VirtualFriend.Instance.FriendTalkingOtherTable("Kodowanie_Audio_Clips", key));
        }

        label.text = "Twój najlepszy czas: " + bestTime;
        label.text += "\nTwój obecny czas: " + elapsedTime;
    }
}


