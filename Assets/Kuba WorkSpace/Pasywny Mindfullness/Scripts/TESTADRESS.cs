using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class TESTADRESS : MonoBehaviour
{
    public string key;
    AsyncOperationHandle<GameObject> opHandle;
    private GameObject instantiatedObj;

    // public IEnumerator Start()
    // {
    //     opHandle = Addressables.LoadAssetAsync<GameObject>(key);
    //     yield return opHandle;
    //
    //     if (opHandle.Status == AsyncOperationStatus.Succeeded)
    //     {
    //         GameObject obj = opHandle.Result;
    //         Instantiate(obj, transform);
    //     }
    // }

    [ContextMenu("ONE")]
    public void AssetOne()
    {
        LoadAsset("U_1");
    }
    
    [ContextMenu("TWO")]
    public void AssetTwo()
    {
        LoadAsset("U_2");
    }
    
    [ContextMenu("THREE")]
    public void AssetThree()
    {
        LoadAsset("Session_1");
    }
    
    public void LoadAsset(string assetKey)
    {
        StartCoroutine(Load(assetKey));
    }

    IEnumerator Load(string assetKey)
    {
        if (opHandle.IsValid())
        {
            Addressables.Release(opHandle);
            Destroy(instantiatedObj);
        }
        
        opHandle = Addressables.LoadAssetAsync<GameObject>(assetKey);
        yield return opHandle;

        if (opHandle.Status == AsyncOperationStatus.Succeeded)
        {
            GameObject obj = opHandle.Result;
            instantiatedObj=Instantiate(obj, transform);
        }
    }
    
    void OnDestroy()
    {
        Addressables.Release(opHandle);
        Destroy(instantiatedObj);
    }
}
