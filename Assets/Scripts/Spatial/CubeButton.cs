using System.Collections.Generic;
using UnityEngine;

public class CubeButton : MonoBehaviour
{
    [SerializeField] private SpatialManager spatialManager;
    [SerializeField] private new Renderer renderer;
    [SerializeField] private List<Material> materials;
    [SerializeField] private MaterialPulse materialPulse;
    [SerializeField] private int buttonNumber;

    private bool status;
    private bool isActive = true;

    public void ButtonSwitch()
    {
        if (isActive)
        {


            if (status)
            {
                status = false;
                renderer.material = materials[0];
            }
            else
            {
                status = true;
                renderer.material = materials[1];
            }

            spatialManager.ActivateButton(status, buttonNumber);
        }
    }

    public void ButtonOff()
    {
        status = false;
        renderer.material = materials[0];
    }

    public void ButtonActivate(bool state)
    {
        isActive = state;
    }

    public void StartPulsing()
    {
        materialPulse.StartPulsing();
    }

    public void StopPulsing()
    {
        materialPulse.StopPulsing();
    }

}
