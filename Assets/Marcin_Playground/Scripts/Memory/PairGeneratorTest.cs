using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

public class PairGeneratorTest : MonoBehaviour
{
    /*
        public enum SyllableCount
        {
            Six = 6,
            Eight = 8,
            Ten = 10,
            Twelve = 12
        }

        private void Start()
        {
            foreach (var word in SelectWords(3))
            {
                Debug.Log(word.word +" -> " + word.firstSyllable.name+ " + " +word.secondSyllable.name);
            }
        }

        private List<WordElementData> SelectWords(int count)
        {
            List<WordElementData> words = new List<WordElementData>();
            List<WordElementData> selectedWords = new List<WordElementData>();
            words = GetListItem();
            for (int i = 0; i < count; i++)
            {
                int nr = Random.Range(0, words.Count - 1);
                selectedWords.Add(words[nr]);
                words.RemoveAt(nr);
            }

            return selectedWords;
        }

        List<WordElementData> GetListItem()
        {
            List<WordElementData> wordsList = new List<WordElementData>();
            string[] guids = AssetDatabase.FindAssets("t:WordElementData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                WordElementData so = AssetDatabase.LoadAssetAtPath<WordElementData>(path);
                wordsList.Add(so);
            }

            return wordsList;
        }








        public List<string> GenerateSyllables(SyllableCount count)
        {
            int numberOfWords = (int)count / 2; // Potrzebna liczba słów
            List<string> generatedSyllables = new List<string>();
            List<string> availableWords = new List<string>(ValidWords);

            // Losowy wybór słów
            for (int i = 0; i < numberOfWords; i++)
            {
                string word = availableWords[Random.Range(0, availableWords.Count)];
                availableWords.Remove(word);

                // Podział wybranego słowa na sylaby i dodanie ich do listy
                generatedSyllables.AddRange(BreakIntoSyllables(word));
            }

            return generatedSyllables.OrderBy(x => Random.Range(0, 100)).ToList(); // Tasowanie sylab
        }

        private List<string> BreakIntoSyllables(string word)
        {
            // Sylaby znane z listy słów
            List<string> knownSyllables = new List<string>
            {
                "ka", "ma", "na", "ta", "pa", "sa", "ra", "ba", "la", "za",
                "pra", "tra", "kra", "ska", "sta", "bra", "dra", "gra", "pla", "kla"
            };

            List<string> wordSyllables = new List<string>();

            foreach (var syllable in knownSyllables)
            {
                while (word.StartsWith(syllable))
                {
                    wordSyllables.Add(syllable);
                    word = word.Substring(syllable.Length);
                }
            }

            return wordSyllables;
        }



        public List<string> GetFormableWords(List<string> syllables)
        {
            List<string> formableWords = new List<string>();
            List<string> remainingSyllables = new List<string>(syllables);

            foreach (var word in ValidWords)
            {
                List<string> wordSyllables = BreakIntoSyllables(word);

                if (CanFormWord(wordSyllables, remainingSyllables))
                {
                    formableWords.Add(word);
                    foreach (var syllable in wordSyllables)
                    {
                        remainingSyllables.Remove(syllable);
                    }
                }
            }

            return formableWords;
        }

        private bool CanFormWord(List<string> wordSyllables, List<string> availableSyllables)
        {
            List<string> tempSyllables = new List<string>(availableSyllables);
            foreach (var syllable in wordSyllables)
            {
                if (!tempSyllables.Remove(syllable))
                {
                    return false;
                }
            }
            return true;
        }

        // Lista słów
        private List<string> wyrazy = new List<string>
        {
            "mama", "tata", "baba", "mata", "papa", "lala", "masa", "mapa", 
            "tama", "paka", "sala", "rada", "lama", "prasa", "trasa", 
            "krata", "skala", "stara", "brama", "drama", "plama"
        };

        // Lista sylab
        private List<string> sylaby = new List<string>
        {
            "ka", "ma", "na", "ta", "pa", "sa", "ra", "ba", "la", "za",
            "pra", "tra", "kra", "ska", "sta", "bra", "dra", "gra", "pla", "kla"
        };

        public List<string> outputSyllabes;
        void test()
        {
            // Wybieramy losową liczbę słów do przetworzenia (np. 5)
            int n = 5;
            List<string> wybraneSlowa = WybierzLosoweSlowa(wyrazy, n);

            // Tworzymy listę wynikową dla sylab
            outputSyllabes = new List<string>();

            // Dzielimy słowa na sylaby
            foreach (var slowo in wybraneSlowa)
            {
                Debug.Log(slowo);
                foreach (var sylaba in sylaby)
                {
                    if (slowo.Contains(sylaba))// && !outputSyllabes.Contains(sylaba))
                    {
                        outputSyllabes.Add(sylaba);
                    }
                }
            }

            // Wyświetlamy sylaby w konsoli
            Debug.Log("Wybrane sylaby:");
            foreach (var sylaba in outputSyllabes)
            {
                Debug.Log(sylaba);
            }
        }

        private List<string> WybierzLosoweSlowa(List<string> lista, int ilosc)
        {
            List<string> wybrane = new List<string>();
            for (int i = 0; i < ilosc; i++)
            {
                int index = Random.Range(0, lista.Count);
                wybrane.Add(lista[index]);
                lista.RemoveAt(index); // Usuwamy wybrane słowo, aby nie powtarzało się
            }
            return wybrane;
        }*/
}
