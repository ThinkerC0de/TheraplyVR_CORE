using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

#if UNITY_EDITOR
[CustomEditor(typeof(RewardDatabase))]
public class RewardDatabaseEditor : Editor
{
    private RewardDatabase database;
    private bool showRewards = true;

    private void OnEnable()
    {
        database = (RewardDatabase)target;
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Reward Database", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Add New Reward"))
        {
            database.rewards.Add(new Reward());
        }

        showRewards = EditorGUILayout.Foldout(showRewards, "Rewards");

        if (showRewards)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < database.rewards.Count; i++)
            {
                DrawRewardEditor(i);
            }
            EditorGUI.indentLevel--;
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(database);
        }
    }

    private void DrawRewardEditor(int index)
    {
        Reward reward = database.rewards[index];

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField($"Reward {index + 1}", EditorStyles.boldLabel);

        reward.name = EditorGUILayout.TextField("Name", reward.name);
        reward.description = EditorGUILayout.TextField("Description", reward.description);
        reward.category = (RewardCategory)EditorGUILayout.EnumPopup("Category", reward.category);
        reward.type = (RewardType)EditorGUILayout.EnumPopup("Type", reward.type);
        reward.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", reward.prefab, typeof(GameObject), false);
        reward.audioDescription = EditorGUILayout.TextField("Description", reward.audioDescription);

        if (GUILayout.Button("Remove Reward"))
        {
            database.rewards.RemoveAt(index);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }
}
#endif