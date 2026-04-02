using UnityEngine;
using System.IO;

public class Screenshot : MonoBehaviour
{
    [Header("Ustawienia Screenshotów")]
    public string screenshotFolder = "C:\\Users\\Public\\Pictures"; // Możesz wkleić własną ścieżkę
    public string screenshotBaseFilename = "screenshot";
    public int screenshotWidth = 2560;
    public int screenshotHeight = 1920;
    private int screenshotCount = 0;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            CaptureScreenshot();
        }
    }

    void CaptureScreenshot()
    {
        if (string.IsNullOrEmpty(screenshotFolder))
        {
            Debug.LogWarning("Nie ustawiono folderu do zapisu!");
            return;
        }

        // Sprawdzenie i tworzenie folderu, jeśli nie istnieje
        if (!Directory.Exists(screenshotFolder))
        {
            Directory.CreateDirectory(screenshotFolder);
            Debug.Log("Utworzono folder: " + screenshotFolder);
        }

        string screenshotName = $"{screenshotBaseFilename}_{screenshotCount + 1}.png";
        string screenshotPath = Path.Combine(screenshotFolder, screenshotName);

        RenderTexture renderTexture = new RenderTexture(screenshotWidth, screenshotHeight, 24);
        Camera.main.targetTexture = renderTexture;
        Camera.main.Render();

        Texture2D screenshot = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, screenshotWidth, screenshotHeight), 0, 0);
        screenshot.Apply();

        Camera.main.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);

        byte[] bytes = screenshot.EncodeToPNG();
        File.WriteAllBytes(screenshotPath, bytes);
        screenshotCount++;

        Debug.Log($"Screenshot #{screenshotCount} zapisany: {screenshotPath}");
    }
}
