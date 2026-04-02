using System;
using System.Collections;
using System.Collections.Generic;
using Autohand;
using Autohand.Demo;
using Unity.VisualScripting;
//using UnityEditor.Localization.Plugins.XLIFF.V12;
using UnityEngine;
using UnityEngine.Analytics;

public enum stickColor
{
    random,
    blue,
    red
}

public struct closestButterflyStruct
{
    public GameObject butterfly;
    public float distance;
}
public class MagicStickPoint : MonoBehaviour
{
    public stickColor _stickColor;
    public bool isActive = false;
    public closestButterflyStruct closestButterfly;
    public float actionDistance = 0.5f;
    public bool haveButterfly = false;
    public GameObject orb;
    public GameObject butterfly;
    private GameObject[] butterflies;
    private float distance;
    private float val;
    private Vector3 orbSize;
    private GameObject hand;
    private float _lastObstacleMissAt = -10f;
    public GrabbablePose grabPose;
    public Autohand.Finger[] temp = new Autohand.Finger[5];
    private const float ObstacleMissCooldownSeconds = 0.15f;

    private void Awake()
    {
        closestButterfly = new closestButterflyStruct();
        closestButterfly.butterfly = null;
        closestButterfly.distance = 10000;
    }

    void Start()
    {
        butterflies = GameObject.FindGameObjectsWithTag("Butterfly");
        orbSize = orb.transform.localScale;
        orb.transform.localScale = Vector3.zero;
    }
    
    void Update()
    {
        /*
        if (Input.GetKey(KeyCode.A))
        {
            OnRelease(this.gameObject.transform.parent.gameObject);
        }
        */
        /*
        if (closestButterfly.butterfly)
        //foreach (var b in butterflies)
        {
            //if (b.GetComponent<ButterflyController>().isCatched) return;
            
            distance = Vector3.Distance(closestButterfly.butterfly.transform.position, this.transform.position);
            if (distance < actionDistance)
            {
                val = actionDistance - distance;
                gameObject.transform.localScale = new Vector3(val, val, val);
            }
            else
            {
                gameObject.transform.localScale = Vector3.zero;
            }
        }
        */
    }

    public void ScaleUpOrb(float t)
    {
        StartCoroutine((ScaleOrb(true, t)));
    }

    public void ScaleDownOrb(float t)
    {
        StartCoroutine((ScaleOrb(false, t)));
    }

    IEnumerator ScaleOrb(bool scaleUp, float t)
    {
        float time = 0;
        Vector3 size = new Vector3(0.1f, 0.1f, 0.1f);
        if (scaleUp)
        {
            while (time < t)
            {
                orb.transform.localScale = Vector3.Lerp(Vector3.zero, size, time / t);
                time += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            while (time < t)
            {
                orb.transform.localScale = Vector3.Lerp(size, Vector3.zero, time / t);
                time += Time.deltaTime;
                yield return null;
            }
            orb.transform.localScale = Vector3.zero;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        /*
        Debug.Log(other.gameObject.name);
        if (other.gameObject.tag == "Balloon")
        {
            other.GetComponent<BalloonController>().ScareButterflies();
        }
        */
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || !isActive)
        {
            return;
        }

        if (Time.unscaledTime - _lastObstacleMissAt < ObstacleMissCooldownSeconds)
        {
            return;
        }

        if (!TryResolveObstacleHit(collision, out var obstacleType, out var targetObject))
        {
            return;
        }

        if (SceneManager_Butterflies.Instance == null)
        {
            return;
        }

        SceneManager_Butterflies.Instance.badAnswers++;

        var targetCategory = obstacleType;
        var targetInstanceId = targetObject != null ? targetObject.GetInstanceID().ToString() : string.Empty;
        var targetAppearedAtUtc = string.Empty;
        var targetAppearedAtElapsedSec = -1f;

        if (targetObject != null)
        {
            var balloonController = targetObject.GetComponent<BalloonController>()
                ?? targetObject.GetComponentInParent<BalloonController>();
            if (balloonController != null)
            {
                targetInstanceId = balloonController.TargetInstanceId;
                targetAppearedAtUtc = balloonController.TargetAppearedAtUtc;
                targetAppearedAtElapsedSec = balloonController.TargetAppearedAtElapsedSec;
            }
            else if (string.Equals(obstacleType, "TREE", StringComparison.Ordinal))
            {
                targetAppearedAtUtc = SceneManager_Butterflies.Instance.GameplayAppearedAtUtc;
                targetAppearedAtElapsedSec = SceneManager_Butterflies.Instance.GameplayAppearedAtElapsedSec;
            }
        }

        var details = new Dictionary<string, object>
        {
            { "targetCategory", targetCategory },
            { "stickColor", _stickColor.ToString() },
            { "stickActive", isActive },
        };
        LegacyInteractionTelemetry.AddTargetTimingDetails(
            details,
            targetCategory,
            targetInstanceId,
            targetAppearedAtUtc,
            targetAppearedAtElapsedSec,
            LegacyInteractionTelemetry.ComputeResponseSec(targetAppearedAtElapsedSec));

        LegacyInteractionTelemetry.EmitOutcome(
            "butterflies",
            obstacleType == "BALLOON" ? "balloon_hit" : "tree_hit",
            SceneManager_Butterflies.Instance.IsGameplayPaused ? "Paused" : "Playing",
            "INCORRECT",
            obstacleType == "BALLOON" ? "BALLOON_HIT" : "TREE_HIT",
            nameof(MagicStickPoint),
            targetId: targetObject != null ? targetObject.GetInstanceID().ToString() : string.Empty,
            targetName: targetObject != null ? targetObject.name : string.Empty,
            inputHand: ResolveInputHand(),
            inputSource: "MAGIC_STICK_" + _stickColor.ToString().ToUpperInvariant(),
            extraDetails: details);

        _lastObstacleMissAt = Time.unscaledTime;
    }

    private void OnCollisionExit(Collision other)
    {
        StartCoroutine(OnCollisionExitCoroutine(other));
    }

    IEnumerator OnCollisionExitCoroutine(Collision other)
    {
        
        if (other.gameObject.layer == 12)
        {
            var bf = other.gameObject.GetComponent<ButterflyController>();
            if (bf.isInAviary == false) yield break;

            if (bf.startSittingOnStick) yield break;
            bf.isCatched = false;
            bf.holdingStick = null;
            bf.isInAviary = false;

            other.gameObject.GetComponent<Rigidbody>().isKinematic = false;
            
            other.gameObject.transform.parent = null;
            butterfly = null;
            haveButterfly = false;
            
            ScaleDownOrb(0.5f);
        }
    }

    public void OnGrab(GameObject gObj)
    {
        hand = gObj.GetComponent<Grabbable>().GetHeldBy()[0].gameObject;
        var h = hand.GetComponent<Hand>();
        //gObj.GetComponent<CapsuleCollider>().enabled = false;
        // grabPose = h.GetGrabPose(gObj.transform, gObj.GetComponent<Grabbable>());
        //grabPose.GetHandPoseData(h);
        //Debug.Log(grabPose.poseName);
        //Debug.Log(h.poseIndex.ToString());
        hand.GetComponent<XRHandControllerLink>().enabled = false;
    }

    public void OnRelease(GameObject gObj)
    {
        /*
        gObj.transform.parent = hand.transform;
        
        Hand h = hand.GetComponent<Hand>();
        
        //h.CloseHand();
        //grabPose.SetHandPose(h, true);
        //h.SetGrip(1);
        Debug.Log(h.poseIndex.ToString());
        grabPose.poseEnabled = true;
        //h.SetHandPose(gObj.GetComponent<GrabbablePoseAdvanced>().GetHandPoseData(h));
        
        /*for (int i = 0; i < h.fingers.Length; i++)
        {
            h.fingers[i].bendOffset = 1;
        }
        */
        /*
        hand = gObj.GetComponent<Grabbable>().lastHeldBy.gameObject;
        Debug.Log(hand.name);
        
        //hand.GetComponent<Hand>().CancelInvoke();
        var h = hand.GetComponent<Hand>();
        h.holdingObj = gObj.GetComponent<Grabbable>();
        h.enableMovement = true;
        h.SetIsGrabbing(true);
        
        gObj.transform.parent = hand.transform;
        gObj.transform.localPosition = new Vector3(0,0.1f,0.3f);
        gObj.transform.localRotation = Quaternion.Euler(-50,0,0);
        //gObj.GetComponent<CapsuleCollider>().enabled = false;
        h.Grab();
        */
    }

    private bool TryResolveObstacleHit(
        Collision collision,
        out string obstacleType,
        out GameObject targetObject)
    {
        obstacleType = string.Empty;
        targetObject = null;

        var otherObject = collision.gameObject;
        if (otherObject == null)
        {
            return false;
        }

        var balloonController = otherObject.GetComponent<BalloonController>()
            ?? otherObject.GetComponentInParent<BalloonController>();
        if (balloonController != null)
        {
            obstacleType = "BALLOON";
            targetObject = balloonController.gameObject;
            return true;
        }

        var sceneManager = SceneManager_Butterflies.Instance;
        if (sceneManager != null && sceneManager.gameObject != null)
        {
            var treeObject = sceneManager.TreeObject;
            if (treeObject != null &&
                (ReferenceEquals(otherObject, treeObject) ||
                 otherObject.transform.IsChildOf(treeObject.transform)))
            {
                obstacleType = "TREE";
                targetObject = otherObject;
                return true;
            }
        }

        return false;
    }

    private string ResolveInputHand()
    {
        if (hand == null)
        {
            return string.Empty;
        }

        var autoHand = hand.GetComponent<Hand>();
        if (autoHand == null)
        {
            return string.Empty;
        }

        return autoHand.left ? "LEFT" : "RIGHT";
    }
}
