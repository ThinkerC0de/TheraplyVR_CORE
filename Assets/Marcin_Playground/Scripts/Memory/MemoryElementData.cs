using UnityEngine;

public enum ElementType { Emotion, Number, Letter, Syllable }
public enum SunflowersNumber { Six = 6, Eight = 8, Ten = 10, Twelve = 12 }

[CreateAssetMenu(fileName = "NewMemoryElementData", menuName = "Theraply/MemoryGame/MemoryElementData", order = 1)]
public class MemoryElementData : ScriptableObject
{
    public string elementName; // Nazwa elementu (np. "happy", "1", "A", "ka")
    public AudioClip audioClip; // Dźwięk odtwarzany przy dotknięciu słonecznika
    public Texture2D texture; // Opcjonalnie: tekstura do wyświetlania na słoneczniku
    public ElementType elementType; // Typ elementu (Emotion, Number, Letter, Syllable)
}