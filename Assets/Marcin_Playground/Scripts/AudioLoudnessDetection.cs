using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class AudioLoudnessDetection : MonoBehaviour
{
    public int sampleWindow = 64;
    private AudioSource audioSource;
    private AudioClip microphoneClip;
    public int audioLength = 3;
    public int frequency = 44100;

    public Slider slider;
    
    public enum State {wdech, wydech, random};
    public State state;
    
    int micIndex = 0;
    private string fileName = "sample";
    private string path = @"c:\test\";
    private string directory = @"random\";
    bool isRecording = false;

    string GetName()
    {
        int index = 0;

        if (Directory.Exists(path))
        {
            //for (int i = 0; i < Directory.GetFiles(path).Length; i++)
            
            while (File.Exists(Path.Combine(Path.Combine(path,directory), fileName + index.ToString()+ ".wav"))==true)
            {
                index++;
            }

            return fileName + index;
        }
        return "";
    }
    
    // Start recording with built-in Microphone and play the recorded audio right away
    void Start()
    {
        
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            if (Microphone.devices[i].Contains("Oculus"))
            {
                micIndex = i;
                Debug.Log("Mic: " + Microphone.devices[micIndex].ToString());
            }
        }
        
        MicrophoneToAudioClip();
        audioSource = GetComponent<AudioSource>();
    }

    private void Update()
    {
        /*
        if (Input.GetKeyDown(KeyCode.R))
        {
            Microphone.End(Microphone.devices[micIndex]);
            audioSource.clip = Microphone.Start(Microphone.devices[micIndex], true, 3, 128000);
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            Microphone.End(Microphone.devices[micIndex]);
            SavWav.Save(@"C:\test\" + GetName(), audioSource.clip);
        }
        */
    }
    
    IEnumerator StartRecordingCoroutine()
    {
        if (isRecording) yield break;
        isRecording = true;
        Microphone.End(Microphone.devices[micIndex]);
        audioSource.clip = Microphone.Start(Microphone.devices[micIndex], true, audioLength, frequency);
        
        slider.value = 0.0f;
        float timeElapsed = 0;
        float startValue=0;
        float endValue=1;
        float valueToLerp;
        
        while (timeElapsed < audioLength)
        {
            valueToLerp = Mathf.Lerp(startValue, endValue, timeElapsed / audioLength);
            timeElapsed += Time.deltaTime;
            slider.value = valueToLerp;
            yield return null;
        }
        
        EndRecording();
    }

    public void StartRecording()
    {
        StartCoroutine(StartRecordingCoroutine());
    }

    public void RecordInhale()
    {
        state = State.wdech;
        StartRecording();
    }

    public void RecordExhale()
    {
        state = State.wydech;
        StartRecording();
    }

    public void RecordRandom()
    {
        state = State.random;
        StartRecording();
    }

    public void EndRecording()
    {
        Microphone.End(Microphone.devices[micIndex]);

        if (state == State.wdech) directory = "wdech";
        else if (state == State.wydech) directory = "wydech";
        else if (state == State.random) directory = "random";
        SavWav.Save(Path.Combine(Path.Combine(path, directory) +"\\"+ GetName()), audioSource.clip);
        isRecording = false;
    }

    public float GetLoudnessFromAudioClip(int clipPosition, AudioClip clip)
    {
        int startPosition = clipPosition - sampleWindow;

        if (startPosition < 0)
            return 0;

        float[] waveData = new float[sampleWindow];
        clip.GetData(waveData, startPosition);

        float totalLoudness = 0;

        for (int i = 0; i < sampleWindow; i++)
        {
            totalLoudness += Mathf.Abs(waveData[i]);
        }

        return totalLoudness / sampleWindow;
    }

    public float GetLoudnessFromMicrophone()
    {
        return GetLoudnessFromAudioClip(Microphone.GetPosition(Microphone.devices[micIndex]), microphoneClip);
    }
    
    public void MicrophoneToAudioClip()
    {
        microphoneClip = Microphone.Start(Microphone.devices[micIndex], true, audioLength, frequency);
    }
}