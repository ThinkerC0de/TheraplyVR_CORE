using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TwoHandsOneObject : TwoHands
{
    [SerializeField] protected List<GameObject> elementsSets;
    [SerializeField] protected List<GameObject> numberSets;


    protected override void RandomizeSideObjects(int count)
    {
        List<GameObject> selected = GetRandomPrefabsSymbol(count);

        for (int i = 0; i < selected.Count; i++)
        {
            var go = Instantiate(selected[i], placeL[i]);
            symbol = go.GetComponent<Symbol>();
            symbol.twoHands = this;
            symbol.isLeft = true;
            gameData.symbols.Add(symbol);
        }

        selected = GetRandomPrefabsNumber(count);

        for (int i = 0; i < selected.Count; i++)
        {
            var go = Instantiate(selected[i], placeR[i]);
            symbol = go.GetComponent<Symbol>();
            symbol.twoHands = this;
            symbol.isLeft = false;
            gameData.symbols.Add(symbol);
        }
    }

    private List<GameObject> GetRandomPrefabsSymbol(int count)
    {
        List<GameObject> selected = new List<GameObject>();
        var symbolNumber = oneSymbol.GetSymbolNumber();

        for (int i = 0; i < count; i++)
        {
            //Add template prefabs to the list of random objects
            selected.Add(elementsSets[symbolNumber-1]);

            //Randomize remaining prefabs
            while (selected.Count % 4 != 0)
            {
                int randomIndex = Random.Range(0, elementsSets.Count);
                selected.Add(elementsSets[randomIndex]);
            }

            MixingList(selected);
        }

        return selected;
    }

    protected List<GameObject> GetRandomPrefabsNumber(int count)
    {
        List<GameObject> selected = new List<GameObject>();
        var amount = oneSymbol.GetAmount();

        for (int i = 0; i < count; i++)
        {
            //Add template prefabs to the list of random objects
            selected.Add(numberSets[amount-1]);

            //Randomize remaining prefabs
            while (selected.Count % 4 != 0)
            {
                int randomIndex = Random.Range(0, numberSets.Count);
                selected.Add(numberSets[randomIndex]);
            }

            MixingList(selected);
        }

        return selected;
    }

    public override IEnumerator Check(bool isLeft, Symbol selected)
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

                    if (selectedLeft.symbolNumber == oneSymbol.GetSymbolNumber()
                        && selectedRight.symbolNumber == oneSymbol.GetAmount())
                    {
                        Debug.Log("Oba dalej prawid³owe");
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
                    || selectedLeft.symbolNumber != oneSymbol.GetSymbolNumber()
                    || selectedRight.symbolNumber != oneSymbol.GetAmount())
                {
                    BadMove();
                }
                else
                {
                    Debug.Log("Too long time between selected symbols");
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

}
