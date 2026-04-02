using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testScript : MonoBehaviour
{
    public List<Texture2D> textures;
    [SerializeField] private SkinnedMeshRenderer _mainMesh;
    [SerializeField] private Material _targetMaterial;
/*
    private void Start()
    {
        if (_mainMesh == null)
        {
            Debug.LogWarning("Brak przypisanego mainMesh");
            return;
        }
        _targetMaterial = _mainMesh.material;
    }
*/
    public void ChangeTextureTo(int index)
    {
        if (textures.Count == 0 || _targetMaterial == null)
        {
            Debug.LogWarning("Brak tekstur lub materiału do zmiany!\nSprawdź czy komponent <Renderer> posiada przypisany materiał");
            return;
        }
        _targetMaterial.mainTexture = textures[index];
    }
}
