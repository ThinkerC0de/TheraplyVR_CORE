using System.Collections;
using UnityEngine;

public class Symbol : MonoBehaviour
{
    //public GameObject shadowObject;
    public ParticleSystem glowParticle;
    public int symbolNumber;
    public TwoHands twoHands;
    public bool isLeft;
    
    [SerializeField] float delayTime;
    [SerializeField] Color normalColor;
    [SerializeField] Color badColor;
    [SerializeField] Color goodColor;

    private bool block;
    private Coroutine coroutine;

    private void Start()
    {
        if (glowParticle != null)
        {
            glowParticle.Stop();
        }
    }

    //Show one time
    public void Show()
    {
        if (!block && glowParticle != null)
        {
            ChangeVFXColor(normalColor);
            StartCoroutine(ShowVFX());
        }
    }

    //Show VFX
    private IEnumerator ShowVFX()
    {
        glowParticle.Play();

        yield return new WaitForSeconds(delayTime);
        
        glowParticle.Stop();
    }

    //Activate VFX and set normal color
    public void Activate()
    {
        if (!block && glowParticle != null)
        {
            if (coroutine != null)
            {
                Debug.Log("Finish coroutine on active: " + gameObject.name);
                StopCoroutine(coroutine);
            }

            ChangeVFXColor(normalColor);
            glowParticle.Play();

            coroutine = StartCoroutine(twoHands.Check(isLeft, this));
        }
    }

    //Deactivate VFX
    public void Deactivate()
    {
        if (glowParticle != null)
        {
            glowParticle.Stop();
            
            if (coroutine != null)
            {
                Debug.Log("Finish coroutine: " + transform.parent.name);
                StopCoroutine(coroutine);
                coroutine = null;
            }
            
            //Debug.Log("----> Deactivate " + symbolNumber);
            twoHands.SetSelected(null, isLeft);
        }
    }

    //Change VFX color
    private void ChangeVFXColor(Color color)
    {
        if (glowParticle != null)
        {
            var main = glowParticle.main;
            main.startColor = color;
        }
    }

    //Show good or bad move
    public IEnumerator ChangeColorAfterDelay(bool result, bool toSlow)
    {
        Color finalColor = result ? badColor : goodColor;
        ChangeVFXColor(finalColor);

        yield return new WaitForSeconds(1);

        if (result)
        {
            twoHands.Congrats();
        }
        else
        {
            if (toSlow)
            {
                StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_oneHand"));
            }
        }


            Debug.Log("----> Deactivate afterDelay " + transform.parent.name);
        Deactivate();
    }

    //Set this symbol as pattern
    public int SetAsPattern(bool isBlocked)
    {
        block = isBlocked;
        return symbolNumber;
    }

    private void OnDestroy()
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
        }
    }
}