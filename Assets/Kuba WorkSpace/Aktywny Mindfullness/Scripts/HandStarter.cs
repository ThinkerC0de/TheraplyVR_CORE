using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

public class HandStarter : MonoBehaviour
{
    [SerializeField] private bool isControllerInside;
    [SerializeField] private float timeInside;
    [SerializeField] private ActiveMindfulness _activeMindfulness;
    [SerializeField] private Material sliderMat;
    [SerializeField] private GameObject gesturesDetector;

    [SerializeField] private UnityEvent OnTrigger;
    [SerializeField] private bool doOnce = true;

    
    public ActiveMindfulness SetGame
    {
        set => _activeMindfulness = value;
    }

    public void StartTheGame()
    {
        _activeMindfulness.SetStartDateTime();
        _activeMindfulness.StartTheGame();  
    }
    
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Hand"))
        {
            isControllerInside = true;
            timeInside += Time.deltaTime;
            
            float v = Mathf.Clamp(1.0f - timeInside / 4.0f, 0.0f, 1.0f);
            sliderMat.SetFloat("_ODKRYWANIE", v );
            
            if (timeInside >= 4f)
            {
                // Do something here, e.g. trigger an event
                // gesturesDetector.SetActive(false);
                if (!doOnce) return;
                doOnce = false;
                OnTrigger?.Invoke();
            }
        }
    }

    // public void Show()
    // {
    //     transform.DOScale(0.1835968f, 0.5f);
    //
    // }

    private void OnEnable()
    {
        transform.DOScale(0.1835968f, 0.5f);
    }

    private void OnDisable()
    {
        doOnce = true;
        // particleEnd.SetActive(false);
    }

    public void ScaleDown()
    {
        // gameObject.SetActive(false);
        transform.DOScale(0.0f, 0.5f).OnComplete((() => gameObject.SetActive(false)));
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Hand"))
        {
            isControllerInside = false;
            timeInside = 0f;
            sliderMat.SetFloat("_ODKRYWANIE", 1.0f );
        }
    }
}
