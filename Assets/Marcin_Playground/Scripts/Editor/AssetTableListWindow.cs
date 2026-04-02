using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEditor.Localization;

public class AssetTableListWindow : EditorWindow
{
    [MenuItem("Window/Localization/Show AssetTables")]
    public static void ShowWindow()
    {
        GetWindow<AssetTableListWindow>("AssetTables List");
    }

    private void OnGUI()
    {
        GUILayout.Label("Available AssetTables", EditorStyles.boldLabel);

        // Pobierz wszystkie kolekcje AssetTable
        var assetTableCollections = LocalizationEditorSettings.GetAssetTableCollections();

        if (assetTableCollections == null || assetTableCollections.Count == 0)
        {
            GUILayout.Label("No AssetTables found.");
            return;
        }

        // Iteruj przez każdą kolekcję tabel zasobów
        foreach (var assetTableCollection in assetTableCollections)
        {
            GUILayout.Label($"Collection Name: {assetTableCollection.TableCollectionName}");
            /*
                        // Iteruj przez każdą tabelę w kolekcji
                        foreach (var table in assetTableCollection.Tables)
                        {
                            if (table.asset is AssetTable assetTable)
                            {
                                GUILayout.Label($"  Table Name: {assetTable.name}, Table GUID: {assetTableCollection.SharedData.TableCollectionNameGuid}");
                            }
                        }*/
        }
    }
}
