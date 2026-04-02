using System;
using System.Collections;
using System.Collections.Generic;
using Dreamteck.Splines.Primitives;
using UnityEngine;

public class AwardCollider : MonoBehaviour
{
    public List<GameObject> butterflies;
    public bool isStarted = false;
    private SceneManager_Butterflies sm;
    private int index = 0;

    private List<Collider> _colliders;
    
    private void OnEnable()
    {
        sm = SceneManager_Butterflies.Instance;
        isStarted = false;
        index = sm.levelIndex-1;
        sm.AwardButterfly(butterflies[index],ButterfliesCount());
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.name.Contains("RobotHand")) return;
        Debug.Log(other.gameObject.name);
        index = sm.levelIndex - 1;
        
        //if (SceneManager_Butterflies.Instance.award)
        {
            //if (_colliders.Contains(other)) return;
            
            //_colliders.Add(other);
            
            if (!isStarted)
            {
                isStarted = true;
                //sm.ClearScene();
                
                StartCoroutine(GetButterfly(other.gameObject));
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.gameObject.name.Contains("RobotHand")) return;
        if (sm != null && sm.award) return;
        isStarted = false;
        var awardPosition = other.GetComponent<AwardPosition>();
        if (awardPosition == null || awardPosition.award == null) return;
        awardPosition.award.FlyAway();
        awardPosition.award = null;
    }

    int ButterfliesCount()
    {
        return 1;
    }

    IEnumerator GetButterfly(GameObject obj)
    {
        yield return new WaitForSeconds(0.5f);

        // Find any live (active) butterfly — the hardcoded "Butterfly_2" name
        // fails after ClearScene destroys all butterflies from the previous run.
        ButterflyController awardButterfly = null;
        var all = GameObject.FindObjectsByType<ButterflyController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var bc in all)
        {
            if (!bc.isCatched && !bc.isCollected)
            {
                awardButterfly = bc;
                break;
            }
        }

        if (awardButterfly == null)
        {
            Debug.LogWarning("[AwardCollider] No free butterfly found for award animation.");
            yield break;
        }

        var awardPosition = obj.GetComponent<AwardPosition>();
        if (awardPosition == null)
        {
            isStarted = false;
            yield break;
        }

        awardPosition.award = awardButterfly;
        awardPosition.award.FlyTo(obj);
        while (sm != null && sm.award && awardPosition.award == awardButterfly)
        {
            yield return null;
        }

        isStarted = false;
    }
}
