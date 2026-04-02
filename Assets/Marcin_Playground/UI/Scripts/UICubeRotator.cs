using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class UICubeRotator : MonoBehaviour
{
    public void RotateLeft()
    {
        var endRot = Quaternion.Euler(0, transform.rotation.y - 90, 0);
        transform.DORotate(endRot.eulerAngles, 1);
    }

    public void RotateRight()
    {
        var endRot = Quaternion.Euler(0, transform.rotation.y + 90, 0);
        transform.DORotate(endRot.eulerAngles, 1);
    }
}
