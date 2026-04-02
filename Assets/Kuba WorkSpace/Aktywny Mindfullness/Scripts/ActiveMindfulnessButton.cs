using System.Collections;
using UnityEngine;

public class ActiveMindfulnessButton : MonoBehaviour
{
    [SerializeField] private ActiveMindfulness.GameType gameType;
    [SerializeField] private Material btnMaterial;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip cristalTone;
    [SerializeField] private AudioClip wrongAnswer;
    [SerializeField] private string lectorAudioClipKey;
    [SerializeField] private ActiveMindfulness gameController;    
    
    
    [Space(30)]
    [SerializeField] private GameObject placeholder;
    [SerializeField] private GameObject realButton;
    [SerializeField] private bool colorChosen = false;
    public ActiveMindfulness GameController
    {
        get => gameController;
        set => gameController = value;
    }

    private void Awake()
    {
        // realButton.SetActive(true);
        // placeholder.SetActive(false);
        
        audioSource = GetComponent<AudioSource>();
        
        realButton.SetActive(false);
        placeholder.SetActive(true);
    }

    public void OnButtonClicked()
    {
        Debug.Log("CLICKED");
        if(!gameController.CanTouch) return;
        switch (gameType)
        {
            case ActiveMindfulness.GameType.GameOne:
                StartCoroutine(HighlightMaterial());
                PlaySound(gameController.CheckIfCorrect(this) ? cristalTone : wrongAnswer);
                gameController.SequenceCrystalsOnClicked(this);
                break;
            case ActiveMindfulness.GameType.GameTwo:
                StartCoroutine(HighlightMaterial());
                PlaySound(gameController.CheckIfCorrect(this) ? cristalTone : wrongAnswer);
                gameController.SequenceCrystalsOnClicked(this);
                break;
            case ActiveMindfulness.GameType.GameThree:
                PlaySound(cristalTone);
                SetEmissionPower(20.0f);
                break;
            case ActiveMindfulness.GameType.GameFour:
                PlaySound(cristalTone);
                SetEmissionPower(20.0f);
                break;
        }
    }

    public void ToggleGameButton(bool v)
    {
        realButton.SetActive(v);
        placeholder.SetActive(!v);
    }
    
    public void OnButtonHighlighted()
    {
        switch (gameType)
        {
            case ActiveMindfulness.GameType.GameOne:
                StartCoroutine(HighlightMaterial());
                PlaySound(cristalTone);
                break;
            case ActiveMindfulness.GameType.GameTwo:
                StartCoroutine(VirtualFriend.Instance.FriendTalking(lectorAudioClipKey));
                break;
            case ActiveMindfulness.GameType.GameThree:
                
                break;
            case ActiveMindfulness.GameType.GameFour:
                break;
        }
    }
    
    public void ChangeColor(float v)
    {
        btnMaterial.SetFloat("_KOLOR", v);
    }

    public void SetColor(float colorValue)
    {
        btnMaterial.SetFloat("_KOLOR", colorValue);
    }

    public void SetEmission(float emissionValue)
    {
        btnMaterial.SetFloat("_EMISSION_POWER", emissionValue);
    }
    
    public void BlinkColor(float colorValue, float time = 0.75f)
    {
        Debug.Log("SETKOLOR: " + colorValue);
        if(colorChosen) return;
        StartCoroutine(BlinkColorEnum(colorValue, time));
    }

    IEnumerator BlinkColorEnum(float colorValue, float time = 0.75f)
    {
        colorChosen = true;
        SetColor(colorValue);
        SetEmission(20.0f);
        yield return new WaitForSeconds(time);
        SetColor(1.0f);
        SetEmission(1.0f);
        colorChosen = false;
    }
    
    public void SetEmissionPower(float value)
    {
        btnMaterial.SetFloat("_EMISSION_POWER", value);
    }

    IEnumerator HighlightMaterial()
    {
        btnMaterial.SetFloat("_EMISSION_POWER", 20.0f);
        yield return new WaitForSeconds(0.75f);
        btnMaterial.SetFloat("_EMISSION_POWER", 1.0f);
    }

    void PlaySound(AudioClip clip)
    {
        audioSource.clip = clip;
        audioSource.Play();
    }
}
