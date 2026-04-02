using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class WaveController : MonoBehaviour
{
    public float distance = 5f;
    public float maxScale = 1.2f;
    private Vector3 _startPos;

    private void Start()
    {
        _startPos = this.transform.position;

        StartCoroutine(StartWaves());
    }

    IEnumerator StartWaves()
    {
        do
        {
            this.transform.position = _startPos;
            this.transform.DOScaleY(0, 0.01f);
            this.transform.DOMove(GetDestination(0.25f), 3);
            this.transform.DOScaleY(maxScale, 3);
            yield return new WaitForSeconds(3);
            this.transform.DOMove(GetDestination(0.5f), 3);
            yield return new WaitForSeconds(3);
            this.transform.DOMove(GetDestination(1f), 6);
            this.transform.DOScaleY(0, 6);
            yield return new WaitForSeconds(6);
            yield return null;
        } while (true);
    }

    Vector3 GetDestination(float distancePercentagePosition)
    {
        return new Vector3(_startPos.x, _startPos.y, _startPos.z + (distance * distancePercentagePosition));
    }
}
