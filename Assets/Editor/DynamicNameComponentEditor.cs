using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR
[CustomEditor(typeof(DynamicNameComponent))]
public class DynamicNameComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DynamicNameComponent component = (DynamicNameComponent)target;
        
        // Wyświetl nazwę komponentu jako nagłówek
        EditorGUILayout.LabelField(component.textField, EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        // Rysuj domyślny inspektor
        DrawDefaultInspector();
        
        // Opcjonalnie: Dodaj przycisk do resetowania nazwy
        EditorGUILayout.Space();
        if (GUILayout.Button("Reset Name"))
        {
            component.textField = "Default Text";
            EditorUtility.SetDirty(component);
        }
    }
}
#endif