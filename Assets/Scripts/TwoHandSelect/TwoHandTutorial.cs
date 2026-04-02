using System.Collections;
using TheraplyVR.Pairs.Basic;
using UnityEngine;

public class TwoHandTutorial : MonoBehaviour
{
    [SerializeField] private BasicSessionManager manager;
    [SerializeField] private bool clockSession;
    private TwoHandData game;

    public void StartTutorial(TwoHandData gameData, bool isOneSymbol)
    {
        game = gameData;

        if(clockSession)
        {
            StartCoroutine(TutorialClock());
        }
        else if (isOneSymbol)
        {
            StartCoroutine(TutorialOneObject());
        }
        else
        {
            StartCoroutine(TutorialTwoObject());
        }

        Debug.Log("Tutorial started");
    }

    private IEnumerator TutorialOneObject()
    {
        manager.ActivateMarkers(false);

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_board"));

        for (int i = 0; i < game.symbolsDrawn.Length; i++)
        {
            game.symbols[i].Show();
            //game.symbols[i].SetAsPattern(true);
        }

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols"));

        if (game.isTimedSession)
        {
            yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_time"));
            yield return new WaitForSeconds(2);
        }

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_left"));
        var count = game.rows * 4 + 1;
        game.symbols[0].Show();
        //game.symbols[0].SetAsPattern(true);
        yield return new WaitForSeconds(2f);
        for (int i = 1; i < count; i++)
        {
            game.symbols[i].Show();
        }

        yield return new WaitForSeconds(3f);

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_right"));
        game.symbols[0].Show();
        //game.symbols[1].SetAsPattern(true);
        game.symbols[0].SetAsPattern(true);
        yield return new WaitForSeconds(2f);
        for (int i = count; i < game.symbols.Count; i++)
        {
            game.symbols[i].Show();
        }

        yield return new WaitForSeconds(2f);
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_touch"));

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_faster"));
        manager.ActivateMarkers(true);
    }

    private IEnumerator TutorialTwoObject()
    {
        manager.ActivateMarkers(false);

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_board"));

        for (int i = 0; i < game.symbolsDrawn.Length; i++)
        {
            game.symbols[i].Show();
            //game.symbols[i].SetAsPattern(true);
        }

        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols"));

        if (game.isTimedSession)
        {
            yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_time"));
            yield return new WaitForSeconds(2);
        }

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_left"));
        var count = game.rows * 4 + 2;
        game.symbols[0].Show();
        game.symbols[0].SetAsPattern(true);
        yield return new WaitForSeconds(2f);
        for (int i = 2; i < count; i++)
        {
            game.symbols[i].Show();
        }

        yield return new WaitForSeconds(3f);

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_twoSymbols_right"));
        game.symbols[1].Show();
        game.symbols[1].SetAsPattern(true);
        yield return new WaitForSeconds(2f);
        for (int i = count; i < game.symbols.Count; i++)
        {
            game.symbols[i].Show();
        }

        yield return new WaitForSeconds(2f);
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_touch"));

        StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_faster"));
        manager.ActivateMarkers(true);
    }

    private IEnumerator TutorialClock()
    {
        yield return new WaitForSeconds(1);
    }
}
