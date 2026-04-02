using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioListener))]
public class ApplicationAudioCapture : MonoBehaviour
{
    private float _lastPeakLevel = 0f;

    // Event to subscribe to for getting audio data
    public event Action<float[], int> OnAudioDataCapture;
    
    // Buffer size
    [SerializeField] private int _bufferSize = 1024;
    [SerializeField] private bool _debugInConsole = true;
    
    // Audio data buffer
    private float[] _audioBuffer;
    private int _audioLogCounter = 0;
    
    private readonly Queue<(float[],int)> _pending = new Queue<(float[],int)>();
    
    private void Awake()
    {
        _audioBuffer = new float[_bufferSize];
    }
    
    private void OnAudioFilterRead(float[] data, int channels) {
        // kopiowanie i enque’owanie – bez żadnych Debug.Log ani Time.xxx
        var copy = new float[data.Length];
        Array.Copy(data, copy, data.Length);
        lock(_pending) { _pending.Enqueue((copy, channels)); }
    }
    
    /*     
    private void OnAudioFilterRead(float[] data, int channels)
    {
        _audioLogCounter++;

        // Check if we have valid data with actual content
        bool hasAudioContent = false;
        float maxAmplitude = 0f;

        for (int i = 0; i < Mathf.Min(data.Length, 100); i++)
        {
            float abs = Mathf.Abs(data[i]);
            maxAmplitude = Mathf.Max(maxAmplitude, abs);
            if (abs > 0.01f)
            {
                hasAudioContent = true;
                break;
            }
        }

        if (_debugInConsole && _audioLogCounter % 300 == 0)
        {
            Debug.Log($"[ApplicationAudioCapture] Max amplitude: {maxAmplitude}, Has content: {hasAudioContent}");
        }

        int samplesToCopy = Mathf.Min(data.Length, _bufferSize);
        Array.Copy(data, _audioBuffer, samplesToCopy);

        // Only invoke the event if there's actual audio content to process

        if (_debugInConsole && _audioLogCounter % 300 == 0)
        {
            Debug.Log($"[ApplicationAudioCapture] Max amplitude: {maxAmplitude}, Has content: {hasAudioContent}");
        }

        OnAudioDataCapture?.Invoke(data, channels);
    }*/

    private void Update()
    {
        lock(_pending) {
            while(_pending.Count > 0) {
                var (samples, ch) = _pending.Dequeue();
                OnAudioDataCapture?.Invoke(samples, ch);
            }
        }
        
        //if (_debugInConsole && Time.frameCount % 100 == 0)
        {
            //Debug.Log($"[ApplicationAudioCapture] Peak audio level: {_lastPeakLevel}");
        }
    }
}