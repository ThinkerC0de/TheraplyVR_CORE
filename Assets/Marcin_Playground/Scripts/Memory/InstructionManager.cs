using UnityEngine;
using UnityEngine.UI;

public class InstructionManager : MonoBehaviour
{
    public Text instructionText;

    public void DisplayInstructions(string text)
    {
        instructionText.text = text;
    }

    public void PlayInstructionsAudio(string text)
    {
        AudioClip instructionClip = AudioManager.Instance.GetAudioClip(text);
        AudioManager.Instance.PlayAudio(instructionClip);
    }
}