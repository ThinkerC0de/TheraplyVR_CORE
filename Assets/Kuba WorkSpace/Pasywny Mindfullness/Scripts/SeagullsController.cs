using UnityEngine;
using Dreamteck.Splines;

public class SeagullsController : MonoBehaviour
{
    [SerializeField] private Transform goAwayPoint;
    [SerializeField] private SplineFollower seaGullFollower;
    [SerializeField] private SplineComputer secondSpline;
    [SerializeField] private SplineComputer spline;

    public void SetSecondSpline()
    {
        seaGullFollower.spline = secondSpline;
        seaGullFollower.followSpeed = Random.Range(4.3f, 5.5f);
        seaGullFollower.wrapMode = SplineFollower.Wrap.Loop;
    }

    [ContextMenu("GO AWAY")]
    public void GoAway()
    {
        spline.type = Spline.Type.BSpline;
        spline.space = SplineComputer.Space.World;
        spline.SetPoint(0, new SplinePoint(transform.position));
        spline.SetPoint(1, new SplinePoint(transform.position + transform.forward * 3.0f + transform.up));
        spline.SetPoint(2, new SplinePoint(transform.position + transform.forward * 3.0f + transform.right * 2.0f + transform.up));
        spline.SetPoint(3, new SplinePoint(transform.position + transform.forward * 1.5f + transform.right * 3.0f + transform.up));
        spline.SetPoint(4, new SplinePoint(goAwayPoint.position));
        seaGullFollower.SetDistance(0.0f);
        seaGullFollower.followSpeed = 8.0f;
        seaGullFollower.spline = spline;
        seaGullFollower.wrapMode = SplineFollower.Wrap.Default;
    }
}
