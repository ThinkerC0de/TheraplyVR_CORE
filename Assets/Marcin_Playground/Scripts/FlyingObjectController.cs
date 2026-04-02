using Dreamteck.Splines;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SplineFollower))]
public class FlyingObjectController : MonoBehaviour
{
    public BoxCollider volume;
    public float speed = 5f;
    private Vector3 _actualPoint;
    private Vector3 _direction;
    public SplineComputer splineComputer;
    public SplineFollower splineFollower;
    public SplinePoint[] points = new SplinePoint[3];

    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(GenerateFlyingObject());
    }

    IEnumerator GenerateFlyingObject()
    {
        yield return new WaitUntil(() => volume != null);
        splineFollower = GetComponent<SplineFollower>();
        Debug.Log(volume.gameObject.name);
        _actualPoint = TheraplyHelpers.GetPointInBoxVolume(volume);

        /*
        //yield return new WaitForSeconds(1);
        yield return new WaitUntil(() => GetComponent<SplineFollower>());
        
        _splineFollower = GetComponent<SplineFollower>();
        Debug.Log(volume.gameObject.name);
        _actualPoint = TheraplyHelpers.GetPointInBoxVolume(volume);

        //yield return null;
        //yield return new WaitForSeconds(1);

        if (!_splineFollower.spline)
        {
            GameObject sc = new GameObject();
            sc.name = name + "_Spline";
            sc.AddComponent<SplineComputer>();
            yield return null;
            _splineComputer = sc.GetComponent<SplineComputer>();
            yield return null;
            _points[0].position = TheraplyHelpers.GetPointInBoxVolume(volume);
            yield return null;
            _points[1].position = TheraplyHelpers.GetPointInBoxVolume(volume);
            yield return null;
            _points[2].position = TheraplyHelpers.GetPointInBoxVolume(volume);
            yield return null;
            _splineComputer.SetPoints(_points);
            yield return null;
            //_splineComputer.multithreaded = true;

            //_splineFollower.follow = true;
            //yield return null;

        }
        _splineFollower.spline = _splineComputer;

        //_splineFollower.spline.RebuildImmediate(true);

        //_splineComputer.Rebuild(true);
        Debug.Log("-----------------------");
        _splineFollower.onEndReached += RegeneratePoints;*/
    }

    public void Regenerate()
    {
        RegeneratePoints(0);
    }

    public void RegeneratePoints(double d)
    {
        StartCoroutine(RegeneratePointsCoroutine());
    }

    IEnumerator RegeneratePointsCoroutine()
    {
        splineFollower.follow = false;
        points[0].position = transform.position;

        points[1].position = TheraplyHelpers.GetPointInBoxVolume(volume);

        points[2].position = TheraplyHelpers.GetPointInBoxVolume(volume);

        //points[3].position = GetPointInVolume();

        splineComputer.SetPointPosition(3, transform.position);

        yield return null;

        splineComputer.SetPoints(points);

        splineComputer.RebuildImmediate(true);
        splineFollower.Restart();
        splineFollower.follow = true;
        splineFollower.spline = splineComputer;
        splineComputer.RebuildImmediate(true);
        splineComputer.Rebuild(true);
        splineFollower.RebuildImmediate();
    }
}
