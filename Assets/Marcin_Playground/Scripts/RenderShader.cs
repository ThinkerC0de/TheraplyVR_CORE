using UnityEngine;

public class RenderShader : MonoBehaviour
{
    public Material mat;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (mat == null)
        {
            Graphics.Blit(src, dest);
            return;
        }
        
        Graphics.Blit(src, dest, mat);
    }
}