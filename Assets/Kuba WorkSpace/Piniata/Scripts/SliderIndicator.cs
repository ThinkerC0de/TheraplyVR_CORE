using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class SliderIndicator : MonoBehaviour
{    
    [SerializeField] private Slider uiSlider;
    [SerializeField] private float maxValue = 5;

    [Space(20)] 
    [SerializeField] private Image background;
    [SerializeField] private Image fill;
    
    [Space(20)] 
    [SerializeField] private Sprite sweetBackground;
    [SerializeField] private Sprite sweetFill;
    
    [Space(20)] 
    [SerializeField] private Sprite presentBackground;
    [SerializeField] private Sprite presentFill;

    
    public float MaxValue
    {
        get => maxValue;
        set
        {
            uiSlider.maxValue = value;
            maxValue = value;
        }
    }

    [SerializeField] private float currentValue;


    public void SetSweet()
    {
        background.sprite = sweetBackground;
        fill.sprite = sweetFill;
    }
    
    
    public void SetPresent() 
    {
        background.sprite = presentBackground;
        fill.sprite = presentFill;
    }
    
    private void OnCurrentValueSet()
    {
        uiSlider.DOValue(currentValue, 0.2f);
    }
    
    public float CurrentValue
    {
        get => currentValue;
        set
        {
            currentValue = value;
            OnCurrentValueSet();
            // uiSlider.fillRect.GetComponent<Image>().color = Color.Lerp(Color.red, Color.green, ((float)_lifesLeft / (float)maxLife));
            // uiSlider.value = value;
        }
    }
}
