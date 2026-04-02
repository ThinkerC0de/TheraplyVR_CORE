using System.Collections;
using System.Collections.Generic;
using UnityEngine;




public class ObeliskColorSelector : MonoBehaviour
{
    private Material material;

    public float[] values;

    public enum Kolor { kolorCzerwony, kolorFioletowy, kolorIndygo, kolorNiebieski, kolorZielony, kolorZolty, kolorPomaranczowy, kolorNormalny }

    private float kolorFioletowy = 0.0f;
    private float kolorIndygo = 0.15f;
    private float kolorNiebieski = 0.3f;
    private float kolorZielony = 0.45f;
    private float kolorZolty = 0.6f;
    private float kolorPomaranczowy = 0.7f;
    private float kolorCzerwony = 0.8f;
    private float kolorNormalny = 1.0f;

    public void Start()
    {
        material = GetComponent<MeshRenderer>().sharedMaterial;
    }
    
        private void ChangeColor(Kolor _value)
    {
        material.SetFloat("_KOLOR", values[(int)_value]);
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            ChangeColor(Kolor.kolorFioletowy);
        }
        if (Input.GetKeyDown(KeyCode.W))
        {
            ChangeColor(Kolor.kolorIndygo);
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            ChangeColor(Kolor.kolorNiebieski);
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            ChangeColor(Kolor.kolorZielony);
        }
        if (Input.GetKeyDown(KeyCode.T))
        {
            ChangeColor(Kolor.kolorZolty);
        }
        if (Input.GetKeyDown(KeyCode.Y))
        {
            ChangeColor(Kolor.kolorPomaranczowy);
        }
        if (Input.GetKeyDown(KeyCode.U))
        {
            ChangeColor(Kolor.kolorCzerwony);
        }
        if (Input.GetKeyDown(KeyCode.I))
        {
            ChangeColor(Kolor.kolorNormalny);
        }
    }
}
