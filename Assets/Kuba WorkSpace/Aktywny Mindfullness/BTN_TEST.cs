using UnityEngine;

public class BTN_TEST : MonoBehaviour
{
    [SerializeField] GameObject target;

    public void ChangeState()
    {
        Debug.Log("CLICK");
        target.SetActive(!target.activeSelf);
    }
}
