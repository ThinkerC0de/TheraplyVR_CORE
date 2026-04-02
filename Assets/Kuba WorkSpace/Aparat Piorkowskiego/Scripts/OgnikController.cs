using UnityEngine;
using System.Collections;
using System.Linq;
using UnityEngine.Localization.Settings;
using System;
using UnityEngine.Audio;

public class OgnikController : MonoBehaviour
{
    public static OgnikController Instance;

    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string tableReference;
    [SerializeField] private bool isTalking = false;

    public bool IsTalking => isTalking;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);

            return;
        }
        else
        {
            Instance = this;
        }
    }

    public void Sad()
    {
        animator.SetTrigger("sad");
    }

    public void Happy()
    {
        animator.SetTrigger("happy");
    }

    public void Talk(string key)
    {
        StartCoroutine(FriendTalking(key));
    }

    public IEnumerator FriendTalking(string dialogKey, float waitTime = 0.0f)
    {
        dialogKey = String.Concat(dialogKey.Where(c => !Char.IsWhiteSpace(c)));

        if (string.IsNullOrEmpty(tableReference)) yield break;
        isTalking = true;
        animator.SetBool("talking", isTalking);

        var tac = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<AudioClip>(tableReference,
            dialogKey);

        while (!tac.IsDone)
        {
            yield return null;
        }

        if (tac.IsDone)
        {

            AudioClip cl = tac.Result;
            audioSource.clip = cl;
            yield return new WaitForSeconds(waitTime);
            audioSource.Play();
            yield return new WaitForSeconds(cl.length + 0.03f);
        }

        isTalking = false;
        animator.SetBool("talking", isTalking);
    }


}
