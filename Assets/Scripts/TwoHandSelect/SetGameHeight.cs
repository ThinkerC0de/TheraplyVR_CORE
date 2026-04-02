using UnityEngine;

public class SetGameHeight : MonoBehaviour
{
    [SerializeField] float maxDiffHeight = 0.1f;
    [SerializeField] float offset;

    void Update()
    {
        var diff = Mathf.Abs(transform.position.y - offset - Camera.main.transform.position.y);

        if (diff > maxDiffHeight)
        {
            transform.position = new Vector3(transform.position.x, Camera.main.transform.position.y + offset, transform.position.z);
        }
    }

}
