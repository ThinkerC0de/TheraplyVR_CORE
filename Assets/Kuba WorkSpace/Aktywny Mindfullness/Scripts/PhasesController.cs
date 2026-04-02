using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class PhasesController : MonoBehaviour
{

    [SerializeField] private GestureDetector gestureDetector;
    [SerializeField] private GameObject triggerBoxGameStart;
    [SerializeField] private GameObject triggerBoxGameEnd;
    [SerializeField] private BackToMenu _backToMenu;
    [SerializeField] private ActiveMindfulnessCommunication amCommunication;
    
    private DateTime startDateTime;
    private DateTime endDateTime;

    [SerializeField] private Transform obelisk;
    [SerializeField] private GameObject particleLeft;
    [SerializeField] private GameObject particleRight;
    [SerializeField] private ActiveMindfulness _activeMindfulness;
    [SerializeField] private List<ActiveMindfulness> allGames;
    [SerializeField] private AudioSource obeliskSound;
    [SerializeField] private GameObject particleEnd;
    
    public void StartPhase(int phaseIndex)
    {
        switch (phaseIndex)
        {
            case 0:
                startDateTime = DateTime.Now;
                StartCoroutine(PhaseOne());
                // StartCoroutine(PhaseTwo());
                break;
            case 1:
                StartCoroutine(PhaseTwo());
                break;
            case 2:
                StartCoroutine(PhaseThree());
                break;
        }
    }

    [ContextMenu("START GAME")]
    public void STARTGAME()
    {
        StartPhase(0);
    }
    
    IEnumerator PhaseOne()
    {
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Phase_1"));
        StartPhase(1);
    }
    
    IEnumerator PhaseTwo()
    {
        Debug.Log("FAZA 2");
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("AM_phase_2"));
        int level = amCommunication.data.level;
        float targetRotation = 0.0f;
        float rotationTime = 4.0f;
        particleEnd.SetActive(false);
        switch (level)
        {
            case 0:
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("AM_Game_1"));
                rotationTime = 1.0f;
                targetRotation = 0.0f;
                _activeMindfulness = allGames[0];
                break;
            case 1:
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("AM_Game_2"));
                targetRotation = 90.0f;
                particleRight.SetActive(false);
                particleRight.SetActive(true);
                _activeMindfulness = allGames[1];
                break;
            case 2:
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("AM_Game_3"));
                targetRotation = 180.0f;
                particleRight.SetActive(false);
                particleRight.SetActive(true);
                _activeMindfulness = allGames[2];
                break;
            case 3:
                yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("AM_Game_4"));
                targetRotation = -90.0f;
                
                particleLeft.SetActive(false);
                particleLeft.SetActive(true);
                _activeMindfulness = allGames[3];
                break;
        }

        // if (level != 0)
        foreach (var localAM in allGames)
        {
            localAM.ButtonsShower(false);
        }
        if(obelisk.localRotation.y != targetRotation)
        {
            obelisk.DOLocalRotate(new Vector3(0.0f, targetRotation, 0.0f), rotationTime);
            obeliskSound.Play();
        } 
        yield return new WaitForSeconds(rotationTime);
        _activeMindfulness.LevelIndicators();
        foreach (var localAM in allGames)
        {
            localAM.ButtonsShower(true);
        }
        
        _activeMindfulness.SetStartDateTime();
        _activeMindfulness.StartTheGame();  
        
    }

    IEnumerator PhaseThree()
    {
        yield return StartCoroutine(VirtualFriend.Instance.FriendTalking("Phase_3"));
        triggerBoxGameEnd.SetActive(true);
    }

    public void BackToMenu()
    {
        endDateTime = DateTime.Now;
        TimeSpan difference = endDateTime - startDateTime;
        Debug.Log("KONIEC SESJI WYSYŁAM DO APKI: " + difference.TotalSeconds + " sekund trwała gra. Wybrana rozgrywka to: " +gestureDetector.selectedGame  + ". Awansowano o: " + _activeMindfulness.GetLevelProgress());
        // StartCoroutine(Menu()); // usunięte ze względu na sterowanie z telefonu
        amCommunication.SetData(difference.TotalSeconds, _activeMindfulness.Level ,_activeMindfulness.GetLevelProgress());
        amCommunication.OnGameFinished("GameFinished:AM");
        _activeMindfulness.ResetParameters();
    }
    
    IEnumerator Menu()
    {
        yield return new WaitForSeconds(12.0f);
        _backToMenu.GoBack();
    }
    
}
