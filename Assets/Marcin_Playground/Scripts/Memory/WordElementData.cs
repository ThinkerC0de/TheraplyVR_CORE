using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMemoryElementData", menuName = "Theraply/MemoryGame/WordElementData", order = 2)]
public class WordElementData : ScriptableObject
{
    public string word;
    public AudioClip audioClip;
    public MemoryElementData firstSyllable;
    public MemoryElementData secondSyllable;
}
