using UnityEngine;
using TMPro;

public class TimerDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text targetText;
    [SerializeField] private PiorkowskiAparatus aparatus;

    private void Reset()
    {
        if (targetText == null) targetText = GetComponent<TMP_Text>();
        if (aparatus == null) aparatus = FindFirstObjectByType<PiorkowskiAparatus>();
    }

    private void Awake()
    {
        if (targetText == null) targetText = GetComponent<TMP_Text>();
        if (aparatus == null) aparatus = FindFirstObjectByType<PiorkowskiAparatus>();
    }

    private void Update()
    {
        if (targetText == null)
        {
            return;
        }

        SingleAparatusGame game = aparatus != null ? aparatus.GetCurrentGame() : null;
        if (game == null)
        {
            targetText.text = "0.00";
            return;
        }

        float seconds = game.GetElapsedSeconds();
        targetText.text = HighscoreManager.FormatTime(seconds);
    }
}


