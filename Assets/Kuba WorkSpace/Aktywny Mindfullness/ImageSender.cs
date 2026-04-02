using System.Collections;
using UnityEngine;
using System.IO;
using System.Net.Sockets;
using System.Threading;

public class ImageSender : MonoBehaviour
{
    public RenderTexture texture;
    public Texture2D _texture;
    private TcpClient client;
    private Thread thread;
    private bool isSending = false;

    public float multiplayer = 1f;
    public int width = -1;
    public int height = -1;
    
    public int sendFrequency = 30; // częstotliwość wysyłania obrazów na sekundę
    private float sendInterval;

    private void Start()
    {
        StartCoroutine(InitializeAndRun());
    }

    IEnumerator InitializeAndRun()
    {
        if (width == -1)
        {
            GetSize();
            yield return null;
        }
        
        //texture.width = Mathf.RoundToInt(width * multiplayer);
        //texture.height = Mathf.RoundToInt(height * multiplayer);
        
        sendInterval = 1.0f / (float)sendFrequency;
        
        thread = new Thread(new ThreadStart(ConnectAndSend));
        thread.Start();
    }

    void GetSize()
    {
        width = Camera.main.pixelWidth;
        height = Camera.main.pixelHeight;
    }

    private void Update()
    {
        if (isSending)
        {
            sendInterval -= Time.deltaTime;
            if (sendInterval <= 0f)
            {
                sendInterval = 1.0f / (float)sendFrequency;
                SendImage();
            }
        }
    }

    private void ConnectAndSend()
    {
        client = new TcpClient("192.168.0.20", 8000); // adres i port serwera
        isSending = true;
    }

    private void SendImage()
    {
        StartCoroutine(TakeSnapshot(texture.width, texture.height));
    }
    
    WaitForEndOfFrame frameEnd = new WaitForEndOfFrame();

    public IEnumerator TakeSnapshot(int _width, int _height)
    {
        yield return frameEnd;

        Texture2D _texture = new Texture2D(_width, _height, TextureFormat.RGB24, false);
        _texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        _texture.Apply();
        byte[] buffer = _texture.EncodeToJPG(); // kodowanie obrazu do formatu JPG
        NetworkStream stream = client.GetStream();
        stream.Write(buffer, 0, buffer.Length);
    }

    private void OnApplicationQuit()
    {
        isSending = false;
        thread.Abort();
        if (client != null) client.Close();
    }
}