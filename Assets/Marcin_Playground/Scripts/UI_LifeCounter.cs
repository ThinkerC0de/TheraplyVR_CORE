using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


public class UI_LifeCounter : MonoBehaviour
{
    public static UI_LifeCounter Instance;

    [SerializeField] public Slider uiSlider;
    [SerializeField] private int maxLife = 5;

    public int MaxLife
    {
        get => maxLife;
        set => maxLife = value;
    }

    public UnityEvent OnLifeSubstract;
    public UnityEvent OnLifesOut;

    private int _lifesLeft;
    private ColorBlock _sliderColors; 
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
        }

        _lifesLeft = maxLife;
        uiSlider.maxValue = maxLife;
        uiSlider.value = _lifesLeft;
        
        //UpdateSlider();
    }

    public void SubstractLife()
    {
        _lifesLeft--;
        UpdateSlider();
        if (_lifesLeft == 0)
        {
            OnLifesOut?.Invoke();
        }
        else
        {
            OnLifeSubstract?.Invoke();
        }
    }

    public void ResetLife()
    {
        _lifesLeft = maxLife;
        UpdateSlider();
    }

    void UpdateSlider()
    {
        uiSlider.DOValue(_lifesLeft, 0.2f);
        //uiSlider.fillRect.GetComponent<Image>().color = Color.Lerp(Color.red, Color.green, ((float)_lifesLeft / (float)maxLife));
    }


    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.KeypadMinus))
            SubstractLife();
            */
    }
}
