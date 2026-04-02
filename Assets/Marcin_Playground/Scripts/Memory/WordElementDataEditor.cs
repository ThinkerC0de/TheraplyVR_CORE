using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
//using UnityEngine.Localization.Tables;
//using UnityEditor.Localization;
#if UNITY_EDITOR
[CustomEditor(typeof(WordElementData))]
public class WordElementaDataEditor : Editor
{
    List<MemoryElementData> availableSyllableElements;
    public override void OnInspectorGUI()
    {
        WordElementData myScriptableObject = (WordElementData)target;
        availableSyllableElements = new List<MemoryElementData>();
        // Wywołaj domyślny interfejs GUI inspektora
        DrawDefaultInspector();

        // Sprawdź, czy nazwa obiektu ScriptableObject została zmieniona
        if (myScriptableObject.name != myScriptableObject.word)
        {
            // Zaktualizuj wartość, aby odpowiadała nazwie
            myScriptableObject.word = myScriptableObject.name;

            // Zaznacz obiekt jako zmodyfikowany, aby zmiany zostały zapisane
            EditorUtility.SetDirty(myScriptableObject);
        }

        // Przypisz ScriptableObject na podstawie wartości `value`
        if (myScriptableObject.firstSyllable == null || myScriptableObject.firstSyllable.name != myScriptableObject.word)
        {
            // Wyszukaj wszystkie ScriptableObject w projekcie
            string[] guids = AssetDatabase.FindAssets("t:MemoryElementData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MemoryElementData so = AssetDatabase.LoadAssetAtPath<MemoryElementData>(path);
                if (so.elementType == ElementType.Syllable)
                    availableSyllableElements.Add(so);
            }
        }

        List<List<MemoryElementData>> possibleSplits = SplitWordIntoSyllables(myScriptableObject.word, availableSyllableElements, 2);

        if (possibleSplits.Count > 0)
        {
            myScriptableObject.firstSyllable = possibleSplits[0][0];
            myScriptableObject.secondSyllable = possibleSplits[0][1];
        }
    }

    public List<List<MemoryElementData>> SplitWordIntoSyllables(string word, List<MemoryElementData> availableElements, int desiredSyllableCount)
    {
        List<List<MemoryElementData>> results = new List<List<MemoryElementData>>();
        SplitRecursive(word, availableElements, desiredSyllableCount, new List<MemoryElementData>(), results);
        return results;
    }

    private void SplitRecursive(string word, List<MemoryElementData> availableElements, int remainingSyllables, List<MemoryElementData> currentSplit, List<List<MemoryElementData>> results)
    {
        // Jeśli słowo zostało całkowicie podzielone i uzyskano odpowiednią liczbę sylab, dodaj wynik do listy wyników
        if (string.IsNullOrEmpty(word) && remainingSyllables == 0)
        {
            results.Add(new List<MemoryElementData>(currentSplit));
            return;
        }

        // Jeśli brak już sylab do podziału lub nie ma już wystarczającej liczby sylab do podziału, zakończ rekursję
        if (remainingSyllables <= 0 || string.IsNullOrEmpty(word))
        {
            return;
        }

        // Przeszukanie dostępnych sylab
        foreach (var element in availableElements)
        {
            if (word.StartsWith(element.elementName))
            {
                currentSplit.Add(element);
                SplitRecursive(word.Substring(element.elementName.Length), availableElements, remainingSyllables - 1, currentSplit, results);
                currentSplit.RemoveAt(currentSplit.Count - 1); // Cofnięcie się do poprzedniego stanu
            }
        }
    }
}
#endif