using System.Collections;
using TheraplyVR.DateEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private int chosenSceneNumber = 9999;
    [SerializeField] private OVRScreenFade screenFade;
    [SerializeField] private LegacyBundledSceneRouter transitionRouter;
    // [SerializeField] private Button startButton;
    // [SerializeField] private UserManagement _userManagement;

    private void Awake()
    {
        if (transitionRouter == null)
        {
            transitionRouter = FindFirstObjectByType<LegacyBundledSceneRouter>();
        }
    }

    public void ChangeScene(int no)
    {
        chosenSceneNumber = no;
    }

    public void ChangeSceneWithNumber(int no)
    {
        chosenSceneNumber = no;
        OpenScene();
    }

    public void OpenScene()
    {
        // if(chosenSceneNumber == 9999) return;
        //
        // if (!_userManagement.CheckIfCanStart()) return;

        // startButton.interactable = false;
        StartCoroutine(LoadLevel(chosenSceneNumber));
    }

    IEnumerator LoadLevel(int number)
    {
        if (transitionRouter == null)
        {
            transitionRouter = FindFirstObjectByType<LegacyBundledSceneRouter>();
        }

        if (transitionRouter != null)
        {
            int targetSceneNumber = number;
            if (DateEventSceneRouter.Instance != null)
            {
                targetSceneNumber = DateEventSceneRouter.Instance.GetTargetSceneIndex(number);
            }

            transitionRouter.LoadSceneByBuildIndex(targetSceneNumber, playTransitionNarration: false);
            yield break;
        }

        screenFade.FadeOut();
        var t = screenFade.fadeTime;
        t += 0.5f;
        yield return new WaitForSeconds(t);

        if (DateEventSceneRouter.Instance == null)
        {
            Debug.LogWarning("DateEventSceneRouter instance not found. Loading scene without routing.");
            SceneManager.LoadScene(number);
            yield break;
        }
        else
            DateEventSceneRouter.Instance.LoadScene(number);
    }

    public void LoadAdditionalScene(int number)
    {
        StartCoroutine(LoadLevel(number));
    }
}
