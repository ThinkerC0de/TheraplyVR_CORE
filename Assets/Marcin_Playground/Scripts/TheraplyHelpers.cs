using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

public static class TheraplyHelpers
{
    private static List<byte> randomNumbers;
    private static int listCountInit = 500;
    private static int oldNumber = 0;

    /*
    public static int GetInt(int min, int max)
    {
        Debug.Log("Będzie losowanie między " + min + " a " +max);
        if (randomNumbers == null)
        {
            randomNumbers = GenerateNumbers(min, max);
        }
        else if (randomNumbers.Count == 0)
        {
            randomNumbers = GenerateNumbers(min, max);
        }

        if ((int)randomNumbers[0] >= min && (int)randomNumbers[0] != oldNumber)
        {
            int temp;
            if ((int)randomNumbers[0] <= max && (int)randomNumbers[0] != oldNumber)
            {
                temp = (int)randomNumbers[0];
                oldNumber = temp;
                Debug.Log("Generated: " + temp);
                randomNumbers.RemoveAt(0);
                return temp;
            }
            else
            {
                for (int i = 0; i < randomNumbers.Count; i++)
                {
                    if (randomNumbers[i] <= max && (int)randomNumbers[0] != oldNumber)
                    {
                        temp = (int)randomNumbers[i];
                        oldNumber = temp;
                        Debug.Log("Generated: " + temp);
                        randomNumbers.RemoveAt(i);
                        return temp;
                    }
                }
            }
        }

        return 0;
    }
*/
    public static Vector3 GetPointInBoxVolume(BoxCollider volume)
    {
        if (volume == null) return Vector3.zero;
        Vector3 extents = volume.bounds.size / 2f;
        Vector3 point = new Vector3(
            UnityEngine.Random.Range(-extents.x, extents.x),
            UnityEngine.Random.Range(-extents.y, extents.y),
            UnityEngine.Random.Range(-extents.z, extents.z)
        );// + volume.bounds.center;
        return volume.transform.TransformPoint(point);
    }

    public static string TimeSpanFromMiliseconds(float ms)
    {
        TimeSpan t = TimeSpan.FromMilliseconds(ms);
        string answer = string.Format("{0:D2}h:{1:D2}m:{2:D2}s:{3:D3}ms",
                                t.Hours,
                                t.Minutes,
                                t.Seconds,
                                t.Milliseconds);
        return answer;
    }

    public static float StringToFloat(string str)
    {
        float temp;

        float.TryParse(str, out temp);

        return temp;
    }

    public static List<byte> GenerateNumbers(int min, int max, bool canDuplicate = false)
    {
        System.Random random = new System.Random((int)DateTime.Now.Ticks & (0x0000FFFF));
        randomNumbers = new List<byte>();
        byte newNumber = 0;
        byte oldNumber = 0;
        for (int i = 0; i < listCountInit; i++)
        {
            if (i == 0)
            {
                oldNumber = (byte)random.Next(min, max);
                randomNumbers.Add(oldNumber);
            }
            else
            {
                newNumber = (byte)random.Next(min, max);
                if (newNumber != oldNumber)
                {
                    randomNumbers.Add(newNumber);
                    oldNumber = newNumber;
                }
                else
                {
                    if (canDuplicate)
                    {
                        newNumber = (byte)random.Next(min, max);
                    }
                    else
                    {
                        do
                        {
                            newNumber = (byte)random.Next(min, max);
                        } while (newNumber == oldNumber);
                    }

                    randomNumbers.Add(newNumber);
                    oldNumber = newNumber;
                }
            }
        }

        return randomNumbers;
    }

    public static int GetIntFromList(List<byte> list)
    {
        if (list.Count > 0)
        {
            int temp = list[0];
            list.RemoveAt(0);
            return temp;
        }

        Debug.LogError("List is empty - returning 0");
        return 0;
    }

    public static string DateTimeNowToString()
    {
        DateTime dt = DateTime.Now;
        return dt.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'");
    }

    public static DateTime StringToDateTime(string value)
    {
        return DateTime.Parse(value);
    }

    public static long DateTimeNowToBinary()
    {
        DateTime dt = DateTime.Now;
        return dt.ToUniversalTime().ToBinary();
    }

    public static IEnumerator LerpPosition(GameObject go, Vector3 targetPosition, float duration)
    {
        float time = 0;
        Vector3 startPosition = go.transform.position;
        while (time < duration)
        {
            go.transform.position = Vector3.Lerp(startPosition, targetPosition, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        go.transform.position = targetPosition;
    }

    public static IEnumerator LerpScale(GameObject go, Vector3 targetScale, float duration)
    {
        float time = 0;
        Vector3 startScale = go.transform.localScale;
        while (time < duration)
        {
            go.transform.localScale = Vector3.Lerp(startScale, targetScale, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        go.transform.localScale = targetScale;
    }
}


