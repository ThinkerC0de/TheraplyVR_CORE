using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using System.Collections;


public class PiorkowskiAparatus : MonoBehaviour
{
    [SerializeField] private List<SingleAparatusGame> _aparatusGames;
    [SerializeField] private int _choosenAparatusGame;

    [SerializeField] private Transform _aparatusButton;
    [SerializeField] private Transform _aparatusDiode;
    [SerializeField] private Transform _aparatusLever;
    [SerializeField] private Transform _aparatusButtonInfo;
    [SerializeField] private Transform _aparatusButtonMenu;
    bool _canStart = true;
    public bool CanStart => _canStart;

    float startButtonScale = 0.0f;
    float startDiodeScale = 0.0f;
    float startLeverScale = 0.0f;
    float startButtonInfoScale = 0.0f;
    float startButtonMenuScale = 0.0f;

    private void Start()
    {
        startButtonScale = _aparatusButton.localScale.x;
        startDiodeScale = _aparatusDiode.localScale.x;
        startLeverScale = _aparatusLever.localScale.x;
        startButtonInfoScale = _aparatusButtonInfo.localScale.x;
        startButtonMenuScale = _aparatusButtonMenu.localScale.x;
        StartCoroutine(OgnikIntroTalk());
    }

    IEnumerator OgnikIntroTalk()
    {
        yield return new WaitForSeconds(3.0f);
        OgnikController.Instance.Happy();
        OgnikController.Instance.Talk("Welcome_dialog");
    }

    public void OgnikInfoTalk()
    {
        if (OgnikController.Instance.IsTalking) return;
        if (!_canStart)
        {
            return;
        }

        StartCoroutine(OgnikInfoTalkEnumerator());
    }
    IEnumerator OgnikInfoTalkEnumerator()
    {
        yield return new WaitForSeconds(0.5f);
        OgnikController.Instance.Talk("info_dialog");
    }

    public void BackToMenu()
    {
        if (!_canStart)
        {
            return;
        }
        FindFirstObjectByType<BackToMenu>().GoBack();
    }


    public void OgnikCongratsTalk()
    {
        StartCoroutine(OgnikCongratsTalkEnumerator());
    }
    IEnumerator OgnikCongratsTalkEnumerator()
    {
        yield return new WaitForSeconds(0.5f);
        OgnikController.Instance.Happy();
        OgnikController.Instance.Talk("congrats_dialog");
    }

    public void OgnikHighscoreTalk()
    {
        StartCoroutine(OgnikHighscoreTalkEnumerator());
    }
    IEnumerator OgnikHighscoreTalkEnumerator()
    {
        yield return new WaitForSeconds(0.5f);
        //OgnikController.Instance.Happy();
        OgnikController.Instance.Talk("highscore_dialog");
    }

    public void StartGame()
    {
        if (!_canStart)
        {
            return;
        }
        _canStart = false;
        StartCoroutine(StartGameCoroutine());
    }

    private IEnumerator StartGameCoroutine()
    {
        _aparatusButton.DOScale(0.0f, 0.5f);
        _aparatusDiode.DOScale(0.0f, 0.5f);
        _aparatusLever.DOScale(0.001f, 0.5f);
        _aparatusButtonInfo.DOScale(0.0f, 0.5f);
        _aparatusButtonMenu.DOScale(0.0f, 0.5f);

        yield return new WaitForSeconds(1.0f);
        Debug.Log("Start Game");
        Debug.Log("Game Index: " + _choosenAparatusGame);
        Debug.Log("Game Length: " + _aparatusGames[_choosenAparatusGame]._aparatusButtons.Count);

        _aparatusGames[_choosenAparatusGame].StartGame();
    }

    public void SetGame(int gameIndex)
    {
        if (!_canStart)
        {
            return;
        }
        _choosenAparatusGame = gameIndex;

    }

    public int GetChosenGameIndex()
    {
        return _choosenAparatusGame;
    }

    public SingleAparatusGame GetCurrentGame()
    {
        if (_aparatusGames == null || _aparatusGames.Count == 0) return null;
        if (_choosenAparatusGame < 0 || _choosenAparatusGame >= _aparatusGames.Count) return null;
        return _aparatusGames[_choosenAparatusGame];
    }

    public void ShowAparatus()
    {
        StartCoroutine(ShowAparatusEnumerator());
    }

    public IEnumerator ShowAparatusEnumerator()
    {
        _aparatusButton.DOScale(startButtonScale, 0.5f);
        _aparatusDiode.DOScale(startDiodeScale, 0.5f);
        _aparatusLever.DOScale(startLeverScale, 0.5f);
        _aparatusButtonInfo.DOScale(startButtonInfoScale, 0.5f);
        _aparatusButtonMenu.DOScale(startButtonMenuScale, 0.5f);
        yield return new WaitForSeconds(1.0f);
        _canStart = true;
    }



}
