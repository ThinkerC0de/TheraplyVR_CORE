using UnityEngine;
using TheraplyVR.Pairs.Basic;

public class TestStartButtonBasic : MonoBehaviour
{
    public BasicSessionManager manager;
    
    void OnGUI()
    {
        if (GUILayout.Button("Start Basic Session", GUILayout.Width(200), GUILayout.Height(50)))
        {
            if (manager != null)
            {
                manager.StartSession();
            }
        }
        
        if (GUILayout.Button("Stop Basic Session", GUILayout.Width(200), GUILayout.Height(50)))
        {
            if (manager != null)
            {
                manager.StopSession();
            }
        }
    }
}

