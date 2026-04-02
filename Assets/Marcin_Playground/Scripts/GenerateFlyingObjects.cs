using Dreamteck.Splines;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class GenerateFlyingObjects : MonoBehaviour
{
    public GameObject flyingObjectPrefab;
    public BoxCollider _volume;

    [SerializeField] private float speed = 2.0f;
    // Start is called before the first frame update
    void Start()
    {
        if (_volume == null)
            _volume = GetComponent<BoxCollider>();
    }

    public void SpawnFlyingObjects(int quantity)
    {

        Debug.Log("Spawning: " + quantity);

        StartCoroutine(SpawnCoroutine(quantity));
        /*
        for (int i = 0; i < quantity; i++)
        {
            GameObject fo = Instantiate(flyingObjectPrefab);
            fo.name = flyingObjectPrefab.name + "_" + i.ToString();
            fo.GetComponent<FlyingObjectController>().volume = _volume;
            //fo.GetComponent<SplineFollower>().Restart();
        }*/
    }

    IEnumerator SpawnCoroutine(int v)
    {
        for (int i = 1; i <= v; i++)
        {

            var temp = Instantiate(flyingObjectPrefab, TheraplyHelpers.GetPointInBoxVolume(_volume), Quaternion.identity);

            yield return null;
            if (!temp) yield break;
            FlyingObjectController bc = temp.GetComponent<FlyingObjectController>();

            //Set name
            bc.name = flyingObjectPrefab.name + "_" + i;

            GameObject sc = new GameObject();
            sc.name = bc.name + "_Spline";
            sc.AddComponent<SplineComputer>();

            yield return null;

            bc.splineComputer = sc.GetComponent<SplineComputer>();
            //bc._splineComputer.multithreaded = true;
            //bc._splineComputer.type = Spline.Type.Linear;
            //bc._splineComputer.updateMode = SplineComputer.UpdateMode.LateUpdate;
            //bc._splineComputer.sampleRate = 2;
            //bc._splineComputer.sampleMode = SplineComputer.SampleMode.Optimized;
            bc.points[0].position = TheraplyHelpers.GetPointInBoxVolume(_volume);
            bc.points[1].position = TheraplyHelpers.GetPointInBoxVolume(_volume);
            bc.points[2].position = TheraplyHelpers.GetPointInBoxVolume(_volume);
            //bc.points[3].position = TheraplyHelpers.GetPointInBoxVolume(_volume);

            bc.volume = _volume;

            bc.splineComputer.SetPoints(bc.points);
            //bc._splineComputer.Close();
            yield return null;
            if (!bc) yield break;
            bc.splineFollower = bc.GetComponent<SplineFollower>();
            bc.splineFollower.spline = sc.GetComponent<SplineComputer>();

            bc.splineFollower.onEndReached += bc.RegeneratePoints;
            yield return null;
            if (!bc) yield break;

            yield return null;
            if (!temp) yield break;

            yield return null;
            if (!bc) yield break;
            //Set speed
            bc.splineComputer.space = SplineComputer.Space.World;
            bc.splineFollower.followSpeed = speed;
            bc.splineFollower.follow = enabled;
            bc.splineFollower.wrapMode = SplineFollower.Wrap.PingPong;
            bc.splineComputer.Rebuild(true);
            bc.splineComputer.RebuildImmediate(true);
        }
    }

    public void ChangeParticles()
    {
        var particleObjects = FindObjectsByType<ChangeParticle>(FindObjectsSortMode.None);

        foreach (var particleObject in particleObjects)
        {
            particleObject.ToggleObjects();
        }
    }

    public void SpeedUpParticles(float speed)
    {
        var particleObjects = FindObjectsByType<FlyingObjectController>(FindObjectsSortMode.None);

        Debug.Log("speedUp");

        foreach (var particleObject in particleObjects)
        {
            particleObject.splineFollower.followMode = SplineFollower.FollowMode.Uniform;
            particleObject.splineFollower.followSpeed = 10;
        }
    }
}
