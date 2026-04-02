using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SweetsBox : MonoBehaviour
{
    [SerializeField] int numberOfSweets = 0;
    [SerializeField] private TextMeshPro infoText;
    [SerializeField] private int sweetsToCollect = 4;
    [SerializeField] private PiniataLevelManager _piniataLevelManager;
    [SerializeField] private PiniataGame _piniataGame;
    [SerializeField] private PresentsManager _presentsManager;
    [SerializeField] private PiniataCommunication _piniataCommunication;

    [SerializeField] private List<GameObject> _countedSweets = new List<GameObject>();
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("OnTriggerEnter");
        if (other.CompareTag("Sweet"))
        {
            if (_countedSweets.Contains(other.gameObject))
            {
                return;
            }

            Debug.Log("OnTriggerEnter Sweet");
            _countedSweets.Add(other.gameObject);
            Debug.Log("OnTriggerEnter Sweet 2");

            LegacyInteractionTelemetry.EmitOutcome(
                "piniata",
                "piniata_sweet_placed",
                "InProgress",
                "CORRECT",
                "SWEET_PLACED_IN_BOX",
                nameof(SweetsBox),
                targetId: other.gameObject.name,
                targetName: other.gameObject.name,
                inputValue: _countedSweets.Count);

            // numberOfSweets++;
            infoText.text = numberOfSweets.ToString();
            other.gameObject.SetActive(false);
            StartSweetCollection();
        }
    }

    public void StartSweetCollection()
    {
        if (_countedSweets.Count == sweetsToCollect)
        {
            LegacyInteractionTelemetry.EmitOutcome(
                "piniata",
                "piniata_sweets_complete",
                "Completed",
                "CORRECT",
                "ALL_SWEETS_COLLECTED",
                nameof(SweetsBox));

            // _piniataGame.GetPiniataLevel.SingleGameFinalData();

            _piniataLevelManager.ShowMenu();
            _piniataGame.PlayFinishedAudio();


            VirtualFriend.Instance.GoodJob();

            if (_piniataLevelManager.GetCurrentLevelIndex() >= 0 && _piniataLevelManager.GetCurrentLevelIndex() <= 4)
            {
                StartCoroutine(VirtualFriend.Instance.FriendTalking("Intro_Tutorial"));
            }
            else if (_piniataLevelManager.GetCurrentLevelIndex() >= LevelProgressManager.Instance.LastLevelIndex())
            {
                StartCoroutine(VirtualFriend.Instance.FriendTalking("Last_Present"));
            }
            else
            {
                StartCoroutine(VirtualFriend.Instance.FriendTalking("Sweets_Done"));
            }


            numberOfSweets = 0;
            _countedSweets.Clear();
            _presentsManager.ShowPresent();
            _piniataCommunication.OnGameFinished("GameFinished:Piniata");
        }
    }
}
