using UnityEngine;

public class HiddenBTN : MonoBehaviour
{
    [SerializeField] private GameObject target;
    [SerializeField] private float t = 0.0f;
    private void Update()
    {
        if (OVRInput.Get(OVRInput.Button.One))
        {
            if (t < 2.0f)
            {
                t += Time.deltaTime;
            }
            else
            {
                target.SetActive(true);
            }
        }
        else
        {
            t = 0.0f;
            target.SetActive(false);
        }
    }
}
