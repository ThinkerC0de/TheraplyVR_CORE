using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Photon multiplayer lobby removed — not used in therapy workflow.
// Kept as stub to preserve any scene references.
public class MenuManager : MonoBehaviour
{
    [SerializeField]
    private GameObject userNameScreen, connectScreen;

    [SerializeField]
    private GameObject createUserNameButton;

    [SerializeField]
    private TMP_InputField userNameInput, createRoomInput, joinRoomInput;

    #region UIMethods

    public void OnClick_CreateNameBtn()
    {
        if (userNameScreen != null) userNameScreen.SetActive(false);
        if (connectScreen != null) connectScreen.SetActive(true);
    }

    public void OnNameField_Changed()
    {
        if (createUserNameButton == null || userNameInput == null) return;
        createUserNameButton.SetActive(userNameInput.text.Length >= 2);
    }

    public void OnClick_JoinRoom() { }

    public void OnClick_CreateRoom() { }

    #endregion
}
