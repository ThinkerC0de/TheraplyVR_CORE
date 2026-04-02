using UnityEngine;
using UnityEditor;


[CustomEditor(typeof(NetworkCommandListener))]
[CanEditMultipleObjects]
public class NetworkCommandListenerEditor : Editor
{
    protected override void OnHeaderGUI()
    {
        var comp = target as NetworkCommandListener;

        string naglowek = @"test";

        // Pobieramy ikonkę skryptu
        GUIContent scriptIcon = EditorGUIUtility.ObjectContent(comp, typeof(NetworkCommandListener));

        // Rysujemy w jednej linii: ikona + tekst
        GUILayout.BeginHorizontal();
        //GUILayout.Label(scriptIcon.image, GUILayout.Width(32), GUILayout.Height(32));
        GUILayout.Label(naglowek, EditorStyles.boldLabel, GUILayout.Height(32));
        GUILayout.EndHorizontal();
    }

    // Pozostała część inspektora – standardowo
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}