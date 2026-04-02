using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization.Settings;

public class VirtualFriend : MonoBehaviour
{
    public static VirtualFriend Instance;
    
    [SerializeField] private Animator mainAnimator;
    [SerializeField] private AudioSource audioSource;

    [SerializeField] private string tableReference;
    [SerializeField] private bool isTalking = false;
    public bool IsTalking => isTalking;
        
    AudioClip ac;

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

    public void Talk(string key)
    {
        StartCoroutine(FriendTalking(key));
    }
    
    public IEnumerator FriendTalking(string dialogKey, float waitTime = 0.0f)
    {
        dialogKey = String.Concat(dialogKey.Where(c => !Char.IsWhiteSpace(c)));
        
        if (string.IsNullOrEmpty(tableReference)) yield break;
        isTalking = true;
        
        // if (audioSource.isPlaying)
        // {
        //     audioSource.Stop();
        //     StopCoroutine(TalkingCorutine);
        //     StopCoroutine(TalkingAnimCorutine);
        // }
        
        var tac = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<AudioClip>(tableReference,
            dialogKey);
        
        while (!tac.IsDone)
        {
            yield return null;
        }
        
        if (tac.IsDone)
        {
            // TalkingCorutine = StartCoroutine(TalkingAnimation(cl, waitTime));
            // yield return TalkingCorutine;
            
            AudioClip cl = tac.Result;
            audioSource.clip = cl;
            yield return new WaitForSeconds(waitTime);
            audioSource.Play();
            yield return new WaitForSeconds(cl.length + 0.03f);
        }
        
        isTalking = false;
    }

    public IEnumerator FriendTalkingOtherTable(string tableReference, string dialogKey, float waitTime = 0.0f)
    {
        dialogKey = String.Concat(dialogKey.Where(c => !Char.IsWhiteSpace(c)));

        if (string.IsNullOrEmpty(tableReference)) yield break;
        isTalking = true;

        // if (audioSource.isPlaying)
        // {
        //     audioSource.Stop();
        //     StopCoroutine(TalkingCorutine);
        //     StopCoroutine(TalkingAnimCorutine);
        // }

        var tac = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<AudioClip>(tableReference,
            dialogKey);

        while (!tac.IsDone)
        {
            yield return null;
        }

        if (tac.IsDone)
        {
            // TalkingCorutine = StartCoroutine(TalkingAnimation(cl, waitTime));
            // yield return TalkingCorutine;

            AudioClip cl = tac.Result;
            audioSource.clip = cl;
            yield return new WaitForSeconds(waitTime);
            audioSource.Play();
            yield return new WaitForSeconds(cl.length + 0.03f);
        }

        isTalking = false;
    }

    private Coroutine TalkingCorutine;
    private Coroutine TalkingAnimCorutine;
    
    IEnumerator TalkingAnimation(AudioClip clip, float waitTime = 0.0f)
    {
        yield return new WaitForSeconds(waitTime);
        audioSource.clip = clip;
        audioSource.Play();
        TalkingAnimCorutine = StartCoroutine(TalkingEnum(clip.length + 0.03f));
        yield return TalkingAnimCorutine;
    }

    IEnumerator TalkingEnum(float t)
    {
        yield return new WaitForSeconds(t);
    }

    public void GoodJob()
    {
        mainAnimator.SetTrigger("GoodJob");
    }

    public IEnumerator Hide()
    {
        mainAnimator.SetTrigger("Hide");
        yield return new WaitForSeconds(0.7f);
        mainAnimator.gameObject.transform.GetChild(1).GetComponent<SkinnedMeshRenderer>().enabled = false;
    }
    
    public IEnumerator Show()
    {
        yield return new WaitForSeconds(0.3f);
        mainAnimator.gameObject.transform.GetChild(1).GetComponent<SkinnedMeshRenderer>().enabled = true;
        mainAnimator.SetTrigger("Show");
    }

    public void Teleport(Transform newPosition)
    {
        transform.position = newPosition.position;
        transform.rotation = newPosition.rotation;
    }
    
    public IEnumerator FingerPoint()
    {
        mainAnimator.SetTrigger("FingerPoint");
        yield return new WaitForSeconds(4.0f);
    }

    public IEnumerator Wave()
    {
        mainAnimator.SetTrigger("Wave");
        yield return new WaitForSeconds(4.0f);
    }

    bool AnimatorIsPlaying(string animationName){
        return mainAnimator.GetCurrentAnimatorStateInfo(0).IsName(animationName) && (mainAnimator.GetCurrentAnimatorStateInfo(0).length >
               mainAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime);
    }
}
