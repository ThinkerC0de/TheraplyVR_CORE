using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Localization.Settings;
using UnityEngine.AI;

public class ZukPlayerTest : MonoBehaviour
{

    public Transform startPosition;
    public Transform closeToPlayerPosition;
    public Transform closeToVinylPlayerPosition;
    [SerializeField] private Animator mainAnimator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string tableReference;
    [SerializeField] private bool isTalking = false;
    public bool IsTalking => isTalking;
    public RigBuilder rigBuilder;
    public Rig rig;
    public Nawigacja cel;
    //public VinylController vinyl;
    public bool canInteract = true;
    public MultiAimConstraint patrzenieNaGracza;

    public List<Transform> points;
    public int index = 0;
    AudioClip ac;
    private Animator animator;

    // public void GoToPlayer()
    // {
    //     index = (index + 1) % points.Count;

    //     cel.GoToPoint(points[index]);
    //     //gameObject.transform.rotation = closeToPlayerPosition.rotation;
        
    //     //StartCoroutine(GoToTargetCoroutine(closeToPlayerPosition));
    // }

    // // Start - Podlot do gracza i przywitanie.
    // void Start()
    // {
    //     animator = GetComponent<Animator>();
    //     // Uruchom animację "Przywitanie" raz po uruchomieniu sceny
    //     animator.SetTrigger("Przywitanie");
    // }
 
    
    // Sekcja Ruchu Postaci
    // void Update()
    // {

    //     if (Input.GetKeyDown(KeyCode.D))
    //     {
    //         GoToPlayer();
    //     }
        
    //     if (Input.GetKeyDown(KeyCode.Q))
    //     {
    //         StopLookingAtPlayer();
    //     }
    //     if (Input.GetKeyDown(KeyCode.W))
    //     {
    //         StartLookingAtPlayer();
    //     }
    //     if (isTalking) {

    //     mainAnimator.SetTrigger("Idle");

    //     }
    //     else {
    //     mainAnimator.SetTrigger("Idle2");
    //     }
    // }

    //Sekcja Gadania
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
            
            Debug.Log(tac.Result);
            mainAnimator.SetTrigger("Mowi");
            AudioClip cl = tac.Result;
            audioSource.clip = cl;
            yield return new WaitForSeconds(waitTime);
            audioSource.Play();
            yield return new WaitForSeconds(cl.length + 0.03f);
        }
        mainAnimator.SetTrigger("Milczy");
        isTalking = false;
    }
   
    public void StopTalking()
    {
        audioSource.Stop();
        audioSource.clip = null;
        mainAnimator.SetTrigger("Milczy");
        isTalking = false;
    }

    // Koniec Sekcji Gadania

  /*  
    //Patrzenie na gracza via obiekt
       private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Gracz"))
        { 
            patrzenieNaGracza.weight = 1f;
        } 
        else
        {
            patrzenieNaGracza.weight = 0f;
        }    
    }
*/
    /*
    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Gracz"))
        { 
            patrzenieNaGracza.weight = 0f;
        } 
    }
    */

    public void StartLookingAtPlayer()
    {
        StartCoroutine(StartLookingAtPlayerCoroutine());
    }

    IEnumerator StartLookingAtPlayerCoroutine()
    {
        while (patrzenieNaGracza.weight < 1f)
        {
            patrzenieNaGracza.weight += Time.deltaTime*3;
            yield return null;
        }
        patrzenieNaGracza.weight = 1f;
    }

    public void StopLookingAtPlayer()
    {
        StartCoroutine(StopLookingAtPlayerCoroutine());
    }

    IEnumerator StopLookingAtPlayerCoroutine()
    {
        while (patrzenieNaGracza.weight > 0f)
        {
            patrzenieNaGracza.weight -= Time.deltaTime*3;
            yield return null;
        }
        patrzenieNaGracza.weight = 0f;
    }
}
