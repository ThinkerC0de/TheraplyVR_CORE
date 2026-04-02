using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIPointsController : MonoBehaviour
{
    public GameObject pointPrefab;
    public float scaleMultiplayer = 0.001f;
    public int pointsInCollumn = 7;
    public float distanceBetweenColumns = 10;
    public float distanceBetweenPoints = 10;
    private int nrOfColumns = 3;
    private Vector3 controllerPosition;
    private void Start()
    {
        controllerPosition = GetComponent<RectTransform>().position;
        controllerPosition *= scaleMultiplayer;
        AddPointsIndicators();
    }

    public void AddPointsIndicators()
    {
        CreateColumn();
    }
    
    private void CreateColumn()
    {
        StartCoroutine(CreateColumnCoroutine());
    }

    IEnumerator CreateColumnCoroutine()
    {
        for (int i = 0; i < nrOfColumns; i++)
        {
            GameObject go = new GameObject();
            go.AddComponent<RectTransform>();
            yield return null;
            RectTransform column = go.GetComponent<RectTransform>();
            column.SetParent(transform);
            column.name = (i + 1).ToString();
            column.localScale = Vector3.one;
            yield return null;
            
            column.position = new Vector3((i * distanceBetweenColumns)*scaleMultiplayer,0,0);
            
            //DuplicatePointsInColumn(go);
        }
    }

    private void DuplicatePointsInColumn(GameObject column)
    {
        for (int i = 0; i < pointsInCollumn; i++)
        {
            GameObject point = Instantiate(pointPrefab, column.transform);
            point.GetComponent<RectTransform>().transform.position = new Vector3(0, (i * distanceBetweenPoints)* scaleMultiplayer);//new Vector3((i* distanceBetweenColumns), (i * distanceBetweenPoints));
            point.name = (i + 1).ToString();
        }
    }
}
