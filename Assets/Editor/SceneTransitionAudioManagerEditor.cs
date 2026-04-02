using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System.Linq;
using System.Collections.Generic;

[CustomEditor(typeof(SceneTransitionAudioManager))]
public class SceneTransitionAudioManagerEditor : Editor
{
    private string[] tableCollectionNames;
    private Dictionary<string, AssetTable> tableReferences;
    private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

    private void OnEnable()
    {
        LoadAvailableTableCollections();
        RefreshSceneNames();
        EditorBuildSettings.sceneListChanged += OnSceneListChanged;
    }

    private void OnDisable()
    {
        EditorBuildSettings.sceneListChanged -= OnSceneListChanged;
    }

    private void OnSceneListChanged()
    {
        RefreshSceneNames();
        Repaint();
    }

    private void LoadAvailableTableCollections()
    {
        var collections = LocalizationEditorSettings.GetAssetTableCollections();
        List<string> names = new List<string>();
        tableReferences = new Dictionary<string, AssetTable>();

        names.Add("None");
        
        foreach (var collection in collections)
        {
            names.Add(collection.TableCollectionName);
            
            if (collection.Tables.Count > 0)
            {
                var tableRef = collection.Tables[0];
                var loadedTable = tableRef.asset;
                
                if (loadedTable is AssetTable assetTable)
                {
                    tableReferences[collection.TableCollectionName] = assetTable;
                }
            }
        }

        tableCollectionNames = names.ToArray();
    }

    public override void OnInspectorGUI()
    {
        SceneTransitionAudioManager manager = (SceneTransitionAudioManager)target;
        serializedObject.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Transition Audio Configuration", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Active Scenes: {manager.sceneDataList.Count}", EditorStyles.miniLabel);
        EditorGUILayout.Space();
        
        for (int i = 0; i < manager.sceneDataList.Count; i++)
        {
            SceneData sceneData = manager.sceneDataList[i];
            
            if (!foldoutStates.ContainsKey(sceneData.sceneName))
            {
                foldoutStates[sceneData.sceneName] = false;
            }
            
            EditorGUILayout.BeginVertical("box");
            
            string summaryInfo = "";
            if (!string.IsNullOrEmpty(sceneData.assetTableName))
            {
                summaryInfo = $" → {sceneData.assetTableName}";
                if (!string.IsNullOrEmpty(sceneData.localizationKey))
                {
                    summaryInfo += $" / {sceneData.localizationKey}";
                }
                if (sceneData.delayInSeconds > 0)
                {
                    summaryInfo += $" ({sceneData.delayInSeconds:F1}s delay)";
                }
            }
            

            foldoutStates[sceneData.sceneName] = EditorGUILayout.Foldout(
                foldoutStates[sceneData.sceneName], 
                $"[{i}] {sceneData.sceneName}{summaryInfo}", 
                true
            );
            
            if (foldoutStates[sceneData.sceneName])
            {
                EditorGUI.indentLevel++;

                int currentTableIndex = 0;
                if (!string.IsNullOrEmpty(sceneData.assetTableName))
                {
                    currentTableIndex = System.Array.IndexOf(tableCollectionNames, sceneData.assetTableName);
                    if (currentTableIndex < 0) currentTableIndex = 0;
                }

                int newTableIndex = EditorGUILayout.Popup("Audio Table", currentTableIndex, tableCollectionNames);
                if (newTableIndex == 0)
                {
                    sceneData.assetTableName = "";
                }
                else if (newTableIndex > 0 && newTableIndex < tableCollectionNames.Length)
                {
                    sceneData.assetTableName = tableCollectionNames[newTableIndex];
                }

                if (!string.IsNullOrEmpty(sceneData.assetTableName) && 
                    tableReferences.ContainsKey(sceneData.assetTableName))
                {
                    AssetTable referenceTable = tableReferences[sceneData.assetTableName];
                    var keys = referenceTable.Values
                        .Select(entry => entry.Key)
                        .ToArray();
                    
                    if (keys.Length > 0)
                    {
                        int currentIndex = System.Array.IndexOf(keys, sceneData.localizationKey);
                        if (currentIndex < 0) currentIndex = 0;
                        
                        int newIndex = EditorGUILayout.Popup("Audio Clip Key", currentIndex, keys);
                        sceneData.localizationKey = keys[newIndex];
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Table has no audio keys", MessageType.Info);
                        sceneData.localizationKey = "";
                    }
                }
                else if (!string.IsNullOrEmpty(sceneData.assetTableName))
                {
                    EditorGUILayout.HelpBox("Audio table not found", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField("Audio Clip Key", "Select audio table first");
                }

                // Delay slider
                sceneData.delayInSeconds = EditorGUILayout.Slider(
                    "Delay (in sec)", 
                    sceneData.delayInSeconds, 
                    0f, 
                    10f
                );
                
                if (sceneData.delayInSeconds > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Audio will play {sceneData.delayInSeconds:F1}s before transitioning to this scene", 
                        MessageType.Info
                    );
                }

                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        serializedObject.ApplyModifiedProperties();
        
        if (GUI.changed)
        {
            EditorUtility.SetDirty(manager);
        }
    }

    private void RefreshSceneNames()
    {
        SceneTransitionAudioManager manager = (SceneTransitionAudioManager)target;
        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
        
        var activeSceneNames = buildScenes
            .Where(scene => scene.enabled)
            .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
            .ToList();

        manager.sceneDataList.RemoveAll(data => !activeSceneNames.Contains(data.sceneName));

        foreach (string sceneName in activeSceneNames)
        {
            if (!manager.sceneDataList.Any(data => data.sceneName == sceneName))
            {
                manager.sceneDataList.Add(new SceneData { sceneName = sceneName });
            }
        }

        manager.sceneDataList = manager.sceneDataList
            .OrderBy(data => activeSceneNames.IndexOf(data.sceneName))
            .ToList();

        EditorUtility.SetDirty(manager);
    }
}