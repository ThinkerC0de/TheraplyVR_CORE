using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    private Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        LoadAudioClips();
    }

    private void LoadAudioClips()
    {
        // Load your audio clips here and add them to the dictionary
        // Example:
        audioClips.Add("happy", Resources.Load<AudioClip>("Audio/Emotions/happy"));
        audioClips.Add("sad", Resources.Load<AudioClip>("Audio/Emotions/sad"));
        // Add more as needed
    }

    public AudioClip GetAudioClip(string key)
    {
        if (audioClips.ContainsKey(key))
        {
            return audioClips[key];
        }
        return null;
    }

    public void PlayAudio(AudioClip clip)
    {
        if (clip != null)
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position);
        }
    }
}