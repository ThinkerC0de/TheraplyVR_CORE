using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class MagicSphere_Controller : MonoBehaviour
{
    public enum DevelopmentArea
    {
        Emotional,
        Social,
        SelfEsteem
    }

    [System.Serializable]
    public class Message
    {
        public string localizationKey;
        public bool isWish;
        public DevelopmentArea area;
    }

    [System.Serializable]
    public class ProgressData
    {
        public List<int> usedMessageIndices = new List<int>();
        public int wishCount;
        public int parableCount;
    }

    public List<Message> messages = new List<Message>();
    public GameObject postItPrefab;
    public Transform corkboardTransform;
    public XRController leftController;
    public XRController rightController;
    public ParticleSystem sphereEffects;
    public AudioSource audioSource;
    public Color[] postItColors;

    private ProgressData progressData = new ProgressData();

    private void Start()
    {
        LoadMessages();
        LoadProgress();
    }

    public void ActivateSphere()
    {
        StartCoroutine(ActivateSphereCoroutine());
    }

    private IEnumerator ActivateSphereCoroutine()
    {
        // Vibration effect
        leftController.SendHapticImpulse(0.5f, 1f);
        rightController.SendHapticImpulse(0.5f, 1f);

        // Visual effect
        sphereEffects.Play();

        yield return new WaitForSeconds(1f);

        Message selectedMessage = GetNextMessage();
        if (selectedMessage != null)
        {
            PlayMessage(selectedMessage);
            CreatePostIt(selectedMessage);
            SaveProgress();
        }
    }

    private Message GetNextMessage()
    {
        bool shouldBeWish = (progressData.wishCount * 2 <= progressData.parableCount);

        List<int> availableIndices = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (!progressData.usedMessageIndices.Contains(i) && messages[i].isWish == shouldBeWish)
            {
                availableIndices.Add(i);
            }
        }

        if (availableIndices.Count == 0) return null;

        int randomIndex = availableIndices[Random.Range(0, availableIndices.Count)];
        progressData.usedMessageIndices.Add(randomIndex);

        if (shouldBeWish) progressData.wishCount++;
        else progressData.parableCount++;

        return messages[randomIndex];
    }

    private void PlayMessage(Message message)
    {
        string localizedContent = LocalizationSettings.StringDatabase.GetLocalizedString("Messages", message.localizationKey);

        AudioClip clip = Resources.Load<AudioClip>($"Audio/{message.localizationKey}");
        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
        else
        {
            Debug.LogWarning($"Audio clip not found for key: {message.localizationKey}");
        }

        Debug.Log($"Playing message: {localizedContent}");
    }

    private void CreatePostIt(Message message)
    {
        GameObject postIt = Instantiate(postItPrefab, corkboardTransform);

        Renderer postItRenderer = postIt.GetComponent<Renderer>();
        postItRenderer.material.color = postItColors[(int)message.area];

        TextMesh textMesh = postIt.GetComponentInChildren<TextMesh>();
        if (textMesh != null)
        {
            string localizedContent = LocalizationSettings.StringDatabase.GetLocalizedString("Messages", message.localizationKey);
            textMesh.text = localizedContent;
        }

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable interactable = postIt.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        interactable.addDefaultGrabTransformers = false;
        interactable.selectEntered.AddListener((_) => PlayMessage(message));
    }

    private void LoadMessages()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "messages.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            messages = JsonUtility.FromJson<List<Message>>(json);
        }
        else
        {
            Debug.LogError("Messages file not found!");
        }
    }

    private void SaveProgress()
    {
        string path = Path.Combine(Application.persistentDataPath, "progress.json");
        string json = JsonUtility.ToJson(progressData);
        File.WriteAllText(path, json);
    }

    private void LoadProgress()
    {
        string path = Path.Combine(Application.persistentDataPath, "progress.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            progressData = JsonUtility.FromJson<ProgressData>(json);
        }
    }
}
