using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;


public class SingleAparatusGame : MonoBehaviour
{
    public List<SingleAparatusButton> _aparatusButtons;
    private int _gameIndex = 0;
    [SerializeField] private int _gameLength = 10;
    [SerializeField] private float _timeToNextButton = 1.0f;
    private DateTime _startTime;
    private DateTime _endTime;
    private int _lastPickedIndex = -1;
    private int _consecutivePickCount = 0;
    private int _lastPlacementIndex = -1;
    private bool _isRunning = false;

    public bool IsRunning => _isRunning;
    public float GetElapsedSeconds()
    {
        if (_isRunning)
        {
            return (float)(DateTime.Now - _startTime).TotalSeconds;
        }
        if (_startTime == default(DateTime)) return 0f;
        return (float)(_endTime - _startTime).TotalSeconds;
    }

    private PiorkowskiAparatus _aparatus;


    private void Start()
    {
        _aparatus = FindFirstObjectByType<PiorkowskiAparatus>();
    }

    [ContextMenu("StartGame")]
    public void StartGame()
    {
        _gameIndex = 0;
        _lastPickedIndex = -1;
        _consecutivePickCount = 0;
        StartCoroutine(StartGameCoroutine());
    }

    IEnumerator StartGameCoroutine()
    {
        foreach (SingleAparatusButton button in _aparatusButtons)
        {
            button.ShowButton();
        }
        yield return new WaitForSeconds(_timeToNextButton);
        _startTime = DateTime.Now;
        _isRunning = true;
        PickNextRandomButton();
    }

    public void PickNextRandomButton()
    {
        if (_gameIndex >= _gameLength)
        {
            _endTime = DateTime.Now;
            _isRunning = false;
            Debug.Log("Game ended");
            Debug.Log("Time: " + (_endTime - _startTime).TotalSeconds);
            float elapsedSeconds = (float)(_endTime - _startTime).TotalSeconds;
            if (_aparatus != null)
            {
                int levelIndex = _aparatus.GetChosenGameIndex();
                _lastPlacementIndex = HighscoreManager.SubmitScore(levelIndex, elapsedSeconds);
            }
            foreach (SingleAparatusButton button in _aparatusButtons)
            {
                button.HideButton();
            }
            StartCoroutine(EndGameCoroutine());
            return;
        }

        if (_aparatusButtons == null || _aparatusButtons.Count == 0)
        {
            return;
        }

        int buttonsCount = _aparatusButtons.Count;
        int randomIndex;

        if (_lastPickedIndex != -1 && _consecutivePickCount >= 2 && buttonsCount > 1)
        {
            do
            {
                randomIndex = UnityEngine.Random.Range(0, buttonsCount);
            } while (randomIndex == _lastPickedIndex);
        }
        else
        {
            randomIndex = UnityEngine.Random.Range(0, buttonsCount);
        }

        if (randomIndex == _lastPickedIndex)
        {
            _consecutivePickCount++;
        }
        else
        {
            _lastPickedIndex = randomIndex;
            _consecutivePickCount = 1;
        }

        _aparatusButtons[randomIndex].turnDiodeOn();
        _gameIndex++;
    }

    IEnumerator EndGameCoroutine()
    {
        _aparatus.OgnikCongratsTalk();
        if (OgnikController.Instance != null)
        {
            while (OgnikController.Instance.IsTalking)
            {
                yield return null;
            }
        }

        if (_lastPlacementIndex >= 0 && _lastPlacementIndex < 3)
        {
            _aparatus.OgnikHighscoreTalk();
            if (OgnikController.Instance != null)
            {
                while (OgnikController.Instance.IsTalking)
                {
                    yield return null;
                }
            }
        }

        _aparatus.ShowAparatus();
    }
}
