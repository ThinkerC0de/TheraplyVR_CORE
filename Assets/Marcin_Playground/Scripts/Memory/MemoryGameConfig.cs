using UnityEngine;

[CreateAssetMenu(fileName = "NewMemoryGameConfig", menuName = "Theraply/MemoryGame/MemoryGameConfig", order = 2)]
public class MemoryGameConfig : ScriptableObject
{
    public MemoryElementData[] elements;
    public SunflowersNumber numberOfSunflowers;
    public int numberOfRounds = 4;
    public ElementType elementType;
}