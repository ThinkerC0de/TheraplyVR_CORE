using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public Text trialCounterText;
    public Text sessionResultText;

    public void UpdateTrialCounter(int trialsLeft)
    {
        trialCounterText.text = "Trials Left: " + trialsLeft;
    }

    public void DisplaySessionResults(int totalTouches, float averageTime)
    {
        sessionResultText.text = $"Session Results:\nTotal Touches: {totalTouches}\nAverage Time: {averageTime:F2} seconds";
    }
}