
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class AudioTalk : AudioSyncer
{
    [SerializeField] private float minValue = 0.0f;
    [SerializeField] private float maxValue = 1.0f;

    [SerializeField] private Animator player;
    private float v;
    public override void OnUpdate()
    {
        base.OnUpdate();

        if (m_isBeat) return;

        v = Mathf.Lerp(player.GetLayerWeight(1), minValue, restSmoothTime * Time.deltaTime);
        player.SetLayerWeight(1,v);
    }

    public override void OnBeat()
    {
        base.OnBeat();
        StopCoroutine("MoveToScale");
        StartCoroutine("MoveToScale", maxValue);
    }

    private IEnumerator MoveToScale(float t)
    {
        float c = player.GetLayerWeight(1);
        float i = c;
        float _timer = 0;

        while (Math.Abs(c - t) > 0.02f)
        {
            c = Mathf.Lerp(i, t, _timer / timeToBeat);
            _timer += Time.deltaTime;
            
            player.SetLayerWeight(1,c);

            yield return null;
        }

        m_isBeat = false;
    }
}
