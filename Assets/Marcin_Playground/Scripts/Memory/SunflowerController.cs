using System.Collections;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class SunflowerController : MonoBehaviour
{
    public MemoryElementData elementData;
    public TMP_Text elementText;
    public bool isOpen = false;

    private Animator animator;
    private string currentState;
    private AudioSource _audioSource;
    
    
    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        elementText.gameObject.SetActive(false);
        animator = gameObject.transform.root.GetComponent<Animator>();
    }

    public void AssignValue(MemoryElementData newElementData)
    {
        StartCoroutine(AssignValueCoroutine(newElementData));
    }

    IEnumerator AssignValueCoroutine(MemoryElementData newElementData)
    {
        yield return new WaitWhile(() => newElementData == null);
        Debug.Log("Przypisuję: " + newElementData.elementName);
        elementData = newElementData;
        yield return new WaitWhile(() => elementText == null);

        if (MemoryGameController.Instance.gameConfig.elementType != ElementType.Emotion)
        {
            elementText.text = newElementData.elementName;
            if (elementText.text == "")
                Debug.LogWarning("Błąd przypisania");
        }
        else
        {
            transform.GetChild(1).GetComponent<SkinnedMeshRenderer>().materials[1].mainTexture = newElementData.texture;
            elementText.text = "";
        }
    }
    
    public void OnTouch()
    {
        StartCoroutine(OnTouchCoroutine());
    }

    IEnumerator OnTouchCoroutine()
    {
        if (!MemoryGameController.Instance.canTurn) yield break;
        //if (!isOpen)
        {
            //isOpen = true;
            //AudioManager.Instance.PlayAudio(elementData.audioClip);
            //Debug.Log(elementData.elementName);
        }
        
        if (isOpen)
            yield break;; 
        
        MemoryGameController.Instance.IncreaseTouchCounter();
        elementText.gameObject.SetActive(true);
        ChangeAnimationState("WAVE");
        StartCoroutine(PlayElementAudioCoroutine());
        isOpen = true;
        if (!MemoryGameController.Instance.isAnswering)
        {
            MemoryGameController.Instance.isAnswering = true;
            MemoryGameController.Instance.StartTimer();
        }
        else
        {
            MemoryGameController.Instance.isAnswering = false;
            yield return null;
        }
        MemoryGameController.Instance.CheckMatch(this);
    }

    IEnumerator PlayElementAudioCoroutine()
    {
        //yield return new WaitForSeconds(1);
        if (elementData && elementData.audioClip)
        {
            _audioSource.clip = elementData.audioClip;
            _audioSource.Play();
        }

        yield return null;
    }
    
    public void ChangeAnimationState(string newState)
    {
        //stop the same animation from interrupting itself
        if (currentState == newState) return;
        
        // Play the animation
        if (animator)
            animator.CrossFade(newState, 0.1f);

        currentState = newState;
    }

    private bool AllPrefabsHidden()
    {
        // Znajdź wszystkie obiekty z komponentem SunflowerController
        SunflowerController[] allSunflowers = FindObjectsByType<SunflowerController>(FindObjectsSortMode.None);
        foreach (SunflowerController sunflower in allSunflowers)
        {
            if (sunflower.gameObject.activeSelf)
            {
                return false; // Jeśli jakikolwiek prefab jest nadal aktywny, gra trwa
            }
        }
        return true; // Wszystkie prefaby są ukryte, gra się kończy
    }
}