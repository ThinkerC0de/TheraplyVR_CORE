using UnityEngine;
using System.Collections.Generic;

public class RewardManager : MonoBehaviour
{
    [SerializeField] private TreasureChest treasureChest;
    [SerializeField] private int sessionsUntilChestOpens = 4;
    [SerializeField] private RewardDatabase rewardDatabase;
    [SerializeField] private GameObject rewardBoardObject;

    private int currentSessionCount = 0;

    private void Start()
    {
        if (rewardDatabase != null)
        {
            treasureChest.InitializeRewards(rewardDatabase.rewards);
        }
        else
        {
            Debug.LogError("RewardDatabase is not assigned in RewardManager!");
        }
    }

    public void StartNewSession()
    {
        if (treasureChest.playSkrzyniaAudio.isOpen) return;

        currentSessionCount++;

        if (IsChestReadyToOpen())
        {
            Debug.Log("Chest is ready to open");
            treasureChest.OpenChest();
        }
    }

    public void GrantReward(RewardCategory category)
    {
        treasureChest.GrantReward(category);
        DisplayRewardBoard();
    }

    public void DisplayRewardBoard()
    {
        rewardBoardObject.SetActive(true);
    }

    public void HideRewardBoard()
    {
        rewardBoardObject.SetActive(false);
    }

    public void HandleRewardInteraction(Reward reward)
    {
        treasureChest.OnRewardTouch(reward);
    }

    public void TherapistSelectRewardCategory(RewardCategory category)
    {
        GrantReward(category);
    }

    public bool IsChestReadyToOpen()
    {
        return currentSessionCount % sessionsUntilChestOpens == 0;
    }

    public void ResetSessionCount()
    {
        currentSessionCount = 0;
    }

    public int GetCurrentSessionCount()
    {
        return currentSessionCount;
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            StartNewSession();
        }
    }
}