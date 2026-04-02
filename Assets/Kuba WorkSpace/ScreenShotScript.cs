using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScreenShotScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        DontDestroyOnLoad(this.gameObject);

    }

    public int index = 0;

    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyDown("space"))
        {
              ScreenCapture.CaptureScreenshot("Screen_"+index+".png");
              index++;
            Debug.Log("space key was pressed");
        }
    }
}
