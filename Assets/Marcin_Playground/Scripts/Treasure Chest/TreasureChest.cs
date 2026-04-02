using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;

public class TreasureChest : MonoBehaviour
{
    [SerializeField] private Transform rewardDisplayBoard;
    [SerializeField] private ParticleSystem celebrationEffect;
    [SerializeField] private AudioSource fanfareSound;
    [SerializeField] private Transform chestCenter;

    public RewardCategory actualCategory;


    private List<Reward> availableRewards;
    private Dictionary<RewardCategory, int> rewardIndex = new Dictionary<RewardCategory, int>();

    public PlaySkrzyniaAudio playSkrzyniaAudio;

    public void InitializeRewards(List<Reward> rewards)
    {
        availableRewards = rewards;
        foreach (RewardCategory category in System.Enum.GetValues(typeof(RewardCategory)))
        {
            rewardIndex[category] = 0;
        }
    }

    public void OpenChest()
    {
        playSkrzyniaAudio.Open();
        GrantReward(actualCategory);
    }

    public void GrantReward(RewardCategory category)
    {
        Reward reward = GetNextReward(category);
        Debug.Log("GrantReward: " + reward.name);
        if (reward != null)
        {
            DisplayReward(reward);
            AnnounceName(reward);
        }
    }

    private Reward GetNextReward(RewardCategory category)
    {
        List<Reward> categoryRewards = availableRewards.FindAll(r => r.category == category);
        if (rewardIndex[category] < categoryRewards.Count)
        {
            return categoryRewards[rewardIndex[category]++];
        }
        return null;
    }

    private void DisplayReward(Reward reward)
    {
        GameObject rewardObject = Instantiate(reward.prefab, chestCenter.position, Quaternion.identity, transform);
        rewardObject.transform.localScale = Vector3.zero;

        // Animacja pojawiania się, obracania i podskakiwania
        Sequence sequence = DOTween.Sequence();


        // Ciągłe obracanie wokół osi Y od samego początku
        rewardObject.transform.DORotate(new Vector3(0, 360, 0), 3f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart);

        // Skalowanie i obracanie
        sequence.Append(rewardObject.transform.DOScale(Vector3.one, 1f))

                .Join(rewardObject.transform.DORotate(new Vector3(0, 360, 0), 1f, RotateMode.FastBeyond360));

        // Podskakiwanie

        sequence.Append(rewardObject.transform.DOJump(

            rewardObject.transform.position + Vector3.up * 0.5f, // Końcowa pozycja (trochę wyżej)

            0.5f, // Wysokość skoku

            2,    // Liczba skoków
            1f    // Czas trwania
        ));


        // Opcjonalnie: delikatne kołysanie po zakończeniu głównej animacji
        sequence.Append(rewardObject.transform.DORotate(new Vector3(5, 0, 5), 0.5f));

        // Instantiate particles after 5 seconds
        sequence.AppendInterval(5f)

                .OnComplete(() =>
                {

                    Instantiate(celebrationEffect, rewardObject.transform.position, Quaternion.identity); // Instantiate particles
                    playSkrzyniaAudio.Close();
                    // Move reward after 0.3 seconds
                    DOVirtual.DelayedCall(0.1f, () =>
                    {
                        rewardObject.transform.position += new Vector3(10, 0, 0); // Move reward 10 meters away

                    });
                });


        sequence.Play();
        /*
        Vector3 randomPosition = new Vector3(
            Random.Range(-rewardDisplayBoard.localScale.x / 2, rewardDisplayBoard.localScale.x / 2),
            Random.Range(-rewardDisplayBoard.localScale.y / 2, rewardDisplayBoard.localScale.y / 2),
            0
        );
        GameObject rewardObject = Instantiate(reward.prefab, rewardDisplayBoard.position + randomPosition, Quaternion.identity, rewardDisplayBoard);
        rewardObject.transform.localScale = Vector3.one;
        // Set position on the board
        */
    }

    private void AnnounceName(Reward reward)
    {
        if (reward.audioDescription != null)
            VirtualFriend.Instance.Talk(reward.audioDescription);
    }

    public void OnRewardTouch(Reward reward)
    {
        AnnounceName(reward);
    }
}