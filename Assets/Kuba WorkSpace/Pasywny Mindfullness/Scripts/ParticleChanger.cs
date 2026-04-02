
using DG.Tweening;
using UnityEngine;

public class ParticleChanger : MonoBehaviour
{
    [SerializeField] private ParticleSystem firstParticle;
    [SerializeField] private ParticleSystem secondParticle;
    [SerializeField] private float changeTime = 3.0f;
    
    
    [ContextMenu("CHANGE")]
    public void ChangeParticles()
    {
        float rate = 0;
        var firstEmission = firstParticle.emission;
        var secondEmission = secondParticle.emission;
            
        DOTween.To(() => rate, x => rate = x, 15, changeTime)
            .OnUpdate(() => {
                firstEmission.rateOverTime = 15-rate;
                secondEmission.rateOverTime = rate;
            });
    }

    [ContextMenu("CHANGE GRAVITY")]
    public void GoUp()
    {
        ParticleSystem.MainModule mm = secondParticle.main; 
        mm.gravityModifier = -0.13f;
    }
}
