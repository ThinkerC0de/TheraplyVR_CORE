using UnityEngine;
using DG.Tweening;
using System.Collections;

public class SingleAparatusButton : MonoBehaviour
{
    [SerializeField] private Material _diodeON;
    [SerializeField] private Material _diodeOFF;
    [SerializeField] private Renderer _renderer;
    [SerializeField] private bool _isOn = false;

    [SerializeField] private Transform _button;
    [SerializeField] private Transform _diode;
    float startButtonScale = 0.0f;
    float startDiodeScale = 0.0f;
    private SingleAparatusGame _game;

    public AudioClip _buttonClickSound;
    public AudioClip _buttonClickGoodSound;
    public AudioSource _audioSource;

    private void Start()
    {
        startButtonScale = _button.localScale.x;
        startDiodeScale = _diode.localScale.x;
        _button.localScale = Vector3.zero;
        _diode.localScale = Vector3.zero;
        _game = GetComponentInParent<SingleAparatusGame>();
    }

    public void OnClick()
    {
        if (_isOn)
        {
            _audioSource.clip = _buttonClickGoodSound;
            _audioSource.Play();
            turnDiodeOff();
        }
        else
        {
            _audioSource.clip = _buttonClickSound;
            _audioSource.Play();
        }
        // else
        // {
        //     turnDiodeOn();
        // }
    }

    public void turnDiodeOn()
    {
        _isOn = true;
        _renderer.material = _diodeON;
    }
    public void turnDiodeOff()
    {
        _isOn = false;
        _renderer.material = _diodeOFF;
        StartCoroutine(WaitForNextButton());
    }

    IEnumerator WaitForNextButton()
    {
        yield return new WaitForSeconds(0.2f);
        _game.PickNextRandomButton();
    }

    public void ShowButton()
    {
        _button.DOScale(startButtonScale, 0.5f);
        _diode.DOScale(startDiodeScale, 0.5f);
    }

    public void HideButton()
    {
        _button.DOScale(0.0f, 0.5f);
        _diode.DOScale(0.0f, 0.5f);
    }

}
