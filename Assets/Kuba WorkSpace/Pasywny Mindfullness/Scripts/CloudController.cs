using System;
using DG.Tweening;
using UnityEngine;
using Random = UnityEngine.Random;

public class CloudController : MonoBehaviour
{
    [SerializeField] private Color startColor;
    [SerializeField] private Color finalColor;
    [SerializeField] private Material mat;
    
    private void Awake()
    {
        foreach (Transform ch in transform)
        {
            ch.GetComponent<Renderer>().material = mat;
            ch.DOScale(0.0f, 0.0f);
        }
    }

    [ContextMenu("KOLOR")]
    public void ChangeColor()
    {
        mat.DOColor(finalColor, "_Color_Peak", 3.0f);
    }

    [ContextMenu("SHOW")]
    void ShowCloud()
    {
        int index = 0;
        foreach (Transform ch in transform)
        {
            if (index != 0)
            {
                Vector3 pos = new Vector3(ch.position.x + Random.Range(-0.4f, 0.4f),ch.position.y + Random.Range(-0.4f, 0.4f),ch.position.z + Random.Range(-0.2f, 0.2f));
                ch.position = pos;
            }
            
            ch.DOScale(Random.Range(0.5f, 1.0f), Random.Range(1.0f, 2.1f));
            index++;
        }
    }

    [ContextMenu("HIDE")]
    public void HideCloud()
    {
        foreach (Transform ch in transform)
        {
            ch.DOScale(0.0f, 1.0f);
        }
    }

    private void OnEnable()
    {
        ShowCloud();
    }

    private void OnDisable()
    {
        foreach (Transform ch in transform)
        {
            ch.DOScale(0.0f, 0.0f);
        }
        mat.DOColor(Color.white, "_Color_Peak", 0.0f);
    }
}
