using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class PiniataLevelManager : MonoBehaviour
{
    [SerializeField] private PiniataGame piniataGame;
    [SerializeField] private List<GameObject> levelPrefabs;
    [SerializeField] private Transform levelParent;

    [SerializeField] private GameObject currentLevel;
    [SerializeField] private TextMeshPro txt;
    [SerializeField] private ParticleSystem smokeParticle;
    [SerializeField] private AudioSource appearAudio;
    [SerializeField] private List<Toggle> selectDifficultyToggle;

    [SerializeField] private PiniataGameState gameState;

    [SerializeField] private HittingStick redStick;
    [SerializeField] private HittingStick blueStick;
    [SerializeField] private Transform redStickStartPosition;
    [SerializeField] private Transform blueStickStartPosition;

    [SerializeField] private Transform menuPanel;
    [SerializeField] private Transform scrollPanel;
    [SerializeField] private int currentLevelIndex = 99999;
    [SerializeField] private float menuScaleAtStart;

    [SerializeField] private BackToMenu backMenu;

    private void Awake()
    {
        menuScaleAtStart = menuPanel.transform.localScale.x;
    }

    public void StartTheGame()
    {
        if (currentLevel == null || currentLevel.GetComponent<PiniataLevel>().levelFinished)
        {
            StartCoroutine(VirtualFriend.Instance.FriendTalking("Choose_Level"));
            return;
        }

        piniataGame.SetSliders();

        gameState = PiniataGameState.SelectingSticks;

        menuPanel.DOScale(0.0f, 0.5f);

        backMenu.ButtonVisibility(false);

        piniataGame.GameOver = false;

        StartCoroutine(VirtualFriend.Instance.FriendTalking(currentLevelIndex.ToString()));

        if (piniataGame.GetPiniataLevel.RedStick)
        {
            var transform1 = redStick.transform;
            transform1.localPosition = redStickStartPosition.localPosition;
            transform1.localRotation = redStickStartPosition.localRotation;

            redStick.gameObject.SetActive(true);
        }

        if (piniataGame.GetPiniataLevel.BlueStick)
        {
            var transform1 = blueStick.transform;
            transform1.localPosition = blueStickStartPosition.localPosition;
            transform1.localRotation = blueStickStartPosition.localRotation;

            blueStick.gameObject.SetActive(true);
        }
    }

    public void CheckIfSticksChosen()
    {
        if (piniataGame.GetPiniataLevel.RedStick && redStick.handType == HittingStick.HandType.NotSelected)
        {
            return;
        }
        if (piniataGame.GetPiniataLevel.BlueStick && blueStick.handType == HittingStick.HandType.NotSelected)
        {
            return;
        }

        gameState = PiniataGameState.GameInProgress;
        piniataGame.ShowPoints();
        piniataGame.GetPiniataLevel.SaveStartTime();
    }

    public enum PiniataGameState
    {
        GameMenu,
        SelectingSticks,
        GameInProgress,
        TutorialInProgress
    }


    public void SetLevel(int lvl)
    {
        // if(lvl == currentLevelIndex) return;

        smokeParticle.Play();
        appearAudio.Play();

        if (currentLevel != null)
        {
            Destroy(currentLevel);
        }

        currentLevelIndex = lvl;

        if (txt != null) txt.text = "Wybrany poziom: " + (lvl + 1).ToString();
        currentLevel = Instantiate(levelPrefabs[lvl], levelParent);
        piniataGame.SetPiniataLevel = currentLevel.GetComponent<PiniataLevel>();
    }

    IEnumerator SetLevelEnum(int lvl)
    {
        yield return new WaitForSeconds(0.1f);

        if (currentLevel != null)
        {
            Destroy(currentLevel);
        }

        if (txt != null) txt.text = "Wybrany poziom: " + (lvl + 1).ToString();
        currentLevel = Instantiate(levelPrefabs[lvl], levelParent);
        piniataGame.SetPiniataLevel = currentLevel.GetComponent<PiniataLevel>();
        ResetToggles();
    }

    public void LevelFinished()
    {
        LevelProgressManager.Instance.SetNewCurrentLevel(currentLevelIndex + 1);
    }

    void ResetToggles()
    {
        foreach (Toggle toggle in selectDifficultyToggle)
        {
            toggle.isOn = false;
        }

        selectDifficultyToggle[0].isOn = true;
    }

    public void ResetSticks()
    {
        redStick.OnGameEnded();
        blueStick.OnGameEnded();
    }

    public int GetCurrentLevelIndex()
    {
        return currentLevelIndex;
    }

    public void ShowMenu()
    {
        if (piniataGame.State != PiniataGame.GameState.GameLost)
        {
            currentLevel = null;
        }

        float pos = -math.clamp(currentLevelIndex * (2000 / 14), 0, 1450);
        scrollPanel.DOLocalMoveX(pos, 0.41f);
        menuPanel.DOScale(menuScaleAtStart, 0.4f);

        backMenu.ButtonVisibility(true);
    }
}
