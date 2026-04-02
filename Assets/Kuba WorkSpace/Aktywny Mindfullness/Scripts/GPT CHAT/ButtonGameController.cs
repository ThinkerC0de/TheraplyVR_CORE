using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonGameController : MonoBehaviour {
    public Button[] availableButtons;
    public float buttonOnTime = 0.75f;
    public float buttonOffTime = 0.25f;
    public int startingLevel = 2;
    public int maxLevel = 5;
    public int sequenceLength = 2;

    private List<int> sequence;
    private int currentLevel;
    private int currentSequenceIndex;
    private bool waitingForInput;
    private int currentButtonPressedCount;

    void Start() {
        currentLevel = startingLevel;
        GenerateSequence();
        StartCoroutine(PlaySequence());
    }

    void GenerateSequence() {
        sequence = new List<int>();
        for (int i = 0; i < sequenceLength; i++) {
            sequence.Add(Random.Range(0, availableButtons.Length));
        }
        currentSequenceIndex = 0;
        waitingForInput = true;
        currentButtonPressedCount = 0;
    }

    IEnumerator PlaySequence() {
        for (int i = 0; i < sequence.Count; i++) {
            int buttonIndex = sequence[i];
            Button button = availableButtons[buttonIndex];
            button.image.color = Color.white;
            yield return new WaitForSeconds(buttonOnTime);
            button.image.color = Color.grey;
            yield return new WaitForSeconds(buttonOffTime);
            button.image.color = Color.white;
        }
        waitingForInput = true;
    }

    public void ButtonPressed(int buttonIndex) {
        if (!waitingForInput) {
            return;
        }
        int expectedButtonIndex = sequence[currentSequenceIndex];
        if (buttonIndex == expectedButtonIndex) {
            currentSequenceIndex++;
            currentButtonPressedCount++;
            if (currentSequenceIndex >= sequence.Count) {
                if (currentLevel < maxLevel) {
                    currentLevel++;
                }
                Debug.Log("Congratulations, level up to " + currentLevel);
                GenerateSequence();
                StartCoroutine(PlaySequence());
            }
        } else {
            if (currentButtonPressedCount > 0) {
                currentLevel--;
            }
            Debug.Log("Incorrect, level down to " + currentLevel);
            GenerateSequence();
            StartCoroutine(PlaySequence());
        }
        if (currentLevel <= 0) {
            currentLevel = startingLevel;
        }
    }
}
