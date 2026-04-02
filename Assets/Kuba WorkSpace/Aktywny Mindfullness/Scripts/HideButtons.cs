using UnityEngine;

public class HideButtons : MonoBehaviour
{
    [SerializeField] private GameObject placeholder;
    [SerializeField] private GameObject mainButton;

    private void Awake()
    {
        MainButtonState(false);
    }

    public void MainButtonState(bool state)
    {
        placeholder.SetActive(!state);
        mainButton.SetActive(state);
    }
}
