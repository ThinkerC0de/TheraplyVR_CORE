using UnityEngine;

public enum RewardCategory
{
    Emotional,
    Social,
    SelfEsteem
}

public enum RewardType
{
    Badge,
    Medal,
    Diploma
}

[System.Serializable]
public class Reward
{
    public string name;
    public string description;
    public RewardCategory category;
    public RewardType type;
    public GameObject prefab;
    public string audioDescription;
}