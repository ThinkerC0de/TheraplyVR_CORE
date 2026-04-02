/*using UnityEngine;
using UnityEditor;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(MagicSphereMessageData))]
public class MagicSphereMessageDataEditor : Editor
{
    private List<string> availableTables = new List<string>();
    private List<string> availableKeys = new List<string>();

    public override void OnInspectorGUI()
    {
        MagicSphereMessageData messageData = (MagicSphereMessageData)target;

        // Wywołaj domyślny interfejs GUI inspektora
        DrawDefaultInspector();

        // Pobierz dostępne tablice assetów
        if (availableTables.Count == 0)
        {
            availableTables = LocalizationEditorSettings.GetAssetTableCollections()
                .Select(collection => collection.TableCollectionName)
                .ToList();
        }

        // Wybór tablicy lokalizacji
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Localization Table");
        int selectedTableIndex = availableTables.IndexOf(messageData.localizationTableName);
        selectedTableIndex = EditorGUILayout.Popup(selectedTableIndex, availableTables.ToArray());
        EditorGUILayout.EndHorizontal();

        if (selectedTableIndex >= 0 && selectedTableIndex < availableTables.Count)
        {
            messageData.localizationTableName = availableTables[selectedTableIndex];
        }

        // Pobierz dostępne klucze dla wybranej tablicy
        if (!string.IsNullOrEmpty(messageData.localizationTableName))
        {
            var tableCollection = LocalizationEditorSettings.GetAssetTableCollection(messageData.localizationTableName);
            if (tableCollection != null)
            {
                availableKeys = tableCollection.SharedData.Entries
                    .Select(entry => entry.Key)
                    .ToList();

                // Wybór klucza treści
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Content Key");
                int selectedKeyIndex = availableKeys.IndexOf(messageData.contentKey);
                selectedKeyIndex = EditorGUILayout.Popup(selectedKeyIndex, availableKeys.ToArray());
                EditorGUILayout.EndHorizontal();

                if (selectedKeyIndex >= 0 && selectedKeyIndex < availableKeys.Count)
                {
                    messageData.contentKey = availableKeys[selectedKeyIndex];
                }
            }
        }

        // Wyświetl podgląd zlokalizowanej treści
        if (!string.IsNullOrEmpty(messageData.localizationTableName) && !string.IsNullOrEmpty(messageData.contentKey))
        {
            EditorGUILayout.LabelField("Localized Content Preview", messageData.GetLocalizedContent()?.ToString() ?? "No content");
        }

        // Wyświetl kolor post-it'a
        EditorGUILayout.ColorField("Post-it Color", messageData.PostItColor);

        // Zaznacz obiekt jako zmodyfikowany, aby zmiany zostały zapisane
        if (GUI.changed)
        {
            EditorUtility.SetDirty(messageData);
        }
    }
}*/