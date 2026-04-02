using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class GestureDetector : MonoBehaviour
{
    public GameObject point;

    private GameObject entryGO;
    private GameObject exitGO;
    
    public Vector3 entryPoint;
    public Vector3 exitPoint;

    [SerializeField] private bool insideTrigger = false;
    public float time = 0.0f;
    public float triggerTime = 1.0f;

    [SerializeField] private float targetRotation = 0.0f;
    [SerializeField] private Transform obelisk;

    [SerializeField] private bool detectorActive = true;

    [SerializeField] private HandStarter handStarter;
    [SerializeField] private List<ActiveMindfulness> gamesList;
    [SerializeField] private AudioSource obeliskSound;

    [SerializeField] private GameObject particleLeft;
    [SerializeField] private GameObject particleRight;

    public ActiveMindfulness am;
    public ActiveMindfulness.GameType selectedGame;

    [SerializeField] private Collider boxCollider;
    private void Start()
    {
        am = gamesList[0];
        handStarter.SetGame = am;
        selectedGame = ActiveMindfulness.GameType.GameOne;
    }

    public void ActivateDetector()
    {
        boxCollider.enabled = true;
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if(!detectorActive) return;
        if (other.CompareTag("Hand"))
        {
            Destroy(entryGO);
            insideTrigger = true;
            entryGO = Instantiate(point, other.gameObject.transform.position, Quaternion.identity);
            entryPoint = entryGO.transform.position;
        }

    }

    private void Update()
    {
        if(!detectorActive) return;
        if (insideTrigger)
        {
            time += Time.deltaTime;
            if (time > triggerTime)
            {
                insideTrigger = false;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {        
        if(!detectorActive) return;
        if (other.CompareTag("Hand"))
        {
            Destroy(exitGO);
            
            exitGO = Instantiate(point, other.gameObject.transform.position, Quaternion.identity);
            exitPoint = exitGO.transform.position;
            insideTrigger = false;

            Vector3 dir = (exitPoint - entryPoint).normalized;

            float t = Vector3.Dot(dir, transform.right.normalized);

            if (t < -0.90f)
            {
                if (time <= triggerTime)
                {
                    targetRotation += 90.0f;

                    int checkValue = 0;
                    if (targetRotation < 0)
                    {
                        int multi = (int) (-targetRotation / 360) + 1;
                        checkValue = 360 * multi + (int) targetRotation;
                    }
                    else
                    {
                        checkValue = (int)targetRotation;
                    }
                    
                    am = null;
                    switch (checkValue % 360)
                    {
                        case 0:
                            am = gamesList[0];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameOne;

                            break;
                        case 90:
                            am = gamesList[1];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameTwo;

                            break;
                        case 180:
                            am = gamesList[2];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameThree;

                            break;
                        case 270:
                            am = gamesList[3];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameFour;

                            break;
                    }
                    
                    obeliskSound.Play();
                    detectorActive = false;
                    particleRight.SetActive(false);
                    particleRight.SetActive(true);
                    
                    StartCoroutine(MoveObelisk(am));
                }
                else
                {
                    // Debug.Log("ZA DŁUGO!");
                }
            }
            else if (t > 0.90f)
            {
                if (time <= triggerTime)
                {
                    targetRotation -= 90.0f;

                    int checkValue = 0;
                    if (targetRotation < 0)
                    {
                        int multi = (int) (-targetRotation / 360) + 1;
                        checkValue = 360 * multi + (int) targetRotation;
                    }
                    else
                    {
                        checkValue = (int)targetRotation;
                    }

                    am = null;
                    switch (checkValue % 360)
                    {
                        case 0:
                            am = gamesList[0];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameOne;
                            break;
                        case 90:
                            am = gamesList[1];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameTwo;

                            break;
                        case 180:
                            am = gamesList[2];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameThree;
                            break;
                        case 270:
                            am = gamesList[3];
                            handStarter.SetGame = am;
                            selectedGame = ActiveMindfulness.GameType.GameFour;
                            break;
                    }
                    
                    obeliskSound.Play();
                    detectorActive = false;
                    particleLeft.SetActive(false);
                    particleLeft.SetActive(true);
                    StartCoroutine(MoveObelisk(am));
                }
                else
                {
                    // Debug.Log("ZA DŁUGO!");
                }
            }
            else
            {
                // Debug.Log("FAIL");
            }

            time = 0.0f;
        }
    }

    IEnumerator MoveObelisk(ActiveMindfulness am)
    {
        obelisk.DOLocalRotate(new Vector3(0.0f, targetRotation, 0.0f), 4.0f);
        yield return new WaitForSeconds(4.0f);
        Debug.Log("STOP KAMIENIA");
        
        if (!VirtualFriend.Instance.IsTalking)
        {
            string key = "Game_" + (gamesList.IndexOf(am) + 1).ToString();
            Debug.Log(key);
            StartCoroutine(VirtualFriend.Instance.FriendTalking(key));
        }
        
        if (am != null) am.LevelIndicators();
        detectorActive = true;
    }
}
