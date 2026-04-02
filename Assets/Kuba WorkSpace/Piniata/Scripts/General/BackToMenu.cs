using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BackToMenu : MonoBehaviour
{
    [SerializeField] private OVRScreenFade screenFade;

    [SerializeField] private Image background;
    [SerializeField] private GameObject child;
    private void Awake()
    {
        screenFade = FindFirstObjectByType<OVRScreenFade>();
    }

    public void GoBack()
    {
        StartCoroutine(GoBackEnumerator());
    }

    IEnumerator GoBackEnumerator()
    {
        if (screenFade != null)
        {
            screenFade.FadeOut();
            var t = screenFade.fadeTime + 0.5f;
            yield return new WaitForSeconds(t);
            SceneManager.LoadScene(0);
        }
        else
        {
            SceneManager.LoadScene(0);
        }
    }

    public void ButtonVisibility(bool state)
    {
        child.SetActive(state);
        background.enabled = state;
    }
}
