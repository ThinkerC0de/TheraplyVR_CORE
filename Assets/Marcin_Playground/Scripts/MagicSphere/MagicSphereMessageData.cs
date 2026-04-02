using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Localization;
#endif

public enum MagicSphereMessageType { Wish, Parable }
public enum DevelopmentArea { Emotional, Social, SelfEsteem }

[CreateAssetMenu(fileName = "NewMagicSphereMessageData", menuName = "Theraply/MagicSphere/MagicSphereMessageData", order = 1)]
public class MagicSphereMessageData : ScriptableObject
{
    public string localizationTableName;
    public string contentKey;
    public MagicSphereMessageType messageType;
    public DevelopmentArea developmentArea;
    public Color colorPostIt;


    public static readonly Color emotionalColor = new Color(1f, 0.5f, 0f);
    public static readonly Color socialColor = Color.blue;
    public static readonly Color selfEsteemColor = Color.green;

    public Color PostItColor
    {
        get
        {
            switch (developmentArea)
            {
                case DevelopmentArea.Emotional: return emotionalColor;
                case DevelopmentArea.Social: return socialColor;
                case DevelopmentArea.SelfEsteem: return selfEsteemColor;
                default: return Color.white;
            }
        }
    }

    public UnityEngine.Object GetLocalizedContent()
    {
        if (string.IsNullOrEmpty(localizationTableName) || string.IsNullOrEmpty(contentKey))
        {
            Debug.LogWarning($"Localization table name or content key is empty for {name}");
            return null;
        }

        var tableReference = new TableReference();
        var entryReference = new TableEntryReference();

        return LocalizationSettings.AssetDatabase.GetLocalizedAsset<UnityEngine.Object>(tableReference, entryReference);
    }

    public string GetLocalizedString()
    {
        var localizedObject = GetLocalizedContent();

        Debug.Log(localizedObject);

        return "No content";
    }

    private void OnValidate()
    {
        colorPostIt = PostItColor;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(MagicSphereMessageData))]
public class MagicSphereMessageDataEditor : Editor
{
    private List<string> assetTableNames = new List<string>();
    private List<string> assetTableKeys = new List<string>();

    private void OnEnable()
    {
        RefreshAssetTableList();
    }

    public override void OnInspectorGUI()
    {
        MagicSphereMessageData myTarget = (MagicSphereMessageData)target;

        EditorGUILayout.LabelField("Localization Settings", EditorStyles.boldLabel);

        int selectedIndex = assetTableNames.IndexOf(myTarget.localizationTableName);
        selectedIndex = EditorGUILayout.Popup("Asset Table", selectedIndex, assetTableNames.ToArray());
        if (selectedIndex >= 0 && selectedIndex < assetTableNames.Count)
        {
            myTarget.localizationTableName = assetTableNames[selectedIndex];
            DisplayAvailableKeys(selectedIndex);
        }

        int selectedContentKey = assetTableNames[selectedIndex].IndexOf(myTarget.contentKey);
        selectedContentKey = EditorGUILayout.Popup("Content Key", selectedContentKey, assetTableKeys.ToArray());
        if (selectedContentKey >= 0 && selectedContentKey < assetTableKeys.Count)
        {
            myTarget.contentKey = assetTableKeys[selectedContentKey];
        }

        EditorGUILayout.Space();

        DrawDefaultInspector();

        if (GUI.changed)
        {
            EditorUtility.SetDirty(myTarget);
        }
    }

    private void RefreshAssetTableList()
    {
        var assetTableCollections = LocalizationEditorSettings.GetAssetTableCollections();

        if (assetTableCollections == null || assetTableCollections.Count == 0)
        {
            GUILayout.Label("No AssetTables found.");
            return;
        }

        foreach (var assetTableCollection in assetTableCollections)
        {
            assetTableNames.Add(assetTableCollection.TableCollectionName);
        }
    }

    private void DisplayAvailableKeys(int index)
    {
        var assetTableCollections = LocalizationEditorSettings.GetAssetTableCollections(); ;
        assetTableKeys = new List<string>();

        foreach (var assetTableCollection in assetTableCollections)
        {
            if (assetTableCollection.TableCollectionName == assetTableNames[index])
            {
                foreach (var tableReference in assetTableCollection.Tables)
                {
                    if (tableReference.asset is AssetTable assetTable)
                    {
                        foreach (var entry in assetTable)
                        {
                            assetTableKeys.Add(assetTableCollection.SharedData.GetEntry(entry.Key).Key);
                        }
                    }
                }
            }
        }
    }

    
    /*
        private AudioClip GetAudioClip(int index, string key)
        {
            var tableEntryReference = new TableEntryReference(); 
            tableEntryReference.SetReference(key); 
            var assetTableCollections = LocalizationEditorSettings.GetAssetTableCollections();;
            assetTableKeys = new List<string>();
            foreach (var assetTableCollection in assetTableCollections)
            {
                if (assetTableCollection.TableCollectionName == assetTableNames[index])
                {
                    foreach (var tableReference in assetTableCollection.Tables)
                    {
                        if (tableReference.asset is AssetTable assetTable)
                        {
                            var entry = assetTable.SharedData.GetEntry(key);
                            if (entry != null)
                            {
                                Debug.Log($"Key: {entry.Key} found.");
                                var audioClip = LocalizationSettings.AssetDatabase.GetLocalizedAsset<AudioClip>(tableReference, tableEntryReference);
                                return audioClip;
                            }
                        }
                    }
                }
            }

            return null;
        }
    */

}
#endif
