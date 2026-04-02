using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "RewardDatabase", menuName = "Theraply/MagicChest/Reward Database")]
public class RewardDatabase : ScriptableObject
{
    public List<Reward> rewards = new List<Reward>();
}