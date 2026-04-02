using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility to find and remove missing script components from all scenes.
/// Run via menu: Theraply > Remove Missing Scripts (All Scenes)
/// </summary>
public static class MissingScriptCleaner
{
    [MenuItem("Theraply/Remove Missing Scripts (Current Scene)")]
    public static void RemoveMissingScriptsCurrentScene()
    {
        int removed = 0;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        Debug.Log($"[MissingScriptCleaner] Removed {removed} missing script(s) from current scene.");
        if (removed > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
    }

    [MenuItem("Theraply/Remove Missing Scripts (All Build Scenes)")]
    public static void RemoveMissingScriptsAllScenes()
    {
        int totalRemoved = 0;
        var currentPath = SceneManager.GetActiveScene().path;

        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled) continue;

            var openedScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scene.path, UnityEditor.SceneManagement.OpenSceneMode.Single);

            int removed = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

            if (removed > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(openedScene);
                Debug.Log($"[MissingScriptCleaner] {scene.path}: removed {removed} missing script(s) — SAVED");
            }
            else
            {
                Debug.Log($"[MissingScriptCleaner] {scene.path}: clean");
            }

            totalRemoved += removed;
        }

        Debug.Log($"[MissingScriptCleaner] Done. Total removed: {totalRemoved}");

        // Re-open original scene
        if (!string.IsNullOrEmpty(currentPath))
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(currentPath);
    }
}
