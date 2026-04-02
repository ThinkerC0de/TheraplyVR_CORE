using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class LocController : MonoBehaviour
{
    private bool active = false;
    [SerializeField] private string currentLocaleString = "pl";
    
    private IEnumerator Start()
    {
        // currentLocale = PlayerPrefs.GetInt("CurrentLocale", 0);
        
        currentLocaleString = PlayerPrefs.GetString("CurrentLocaleString", "pl");
        
        
        yield return SetLocaleString(currentLocaleString);
        // yield return SetLocale(currentLocale);
        // dropdown.SetValueWithoutNotify(currentLocale);
        
        // var locales = LocalizationSettings.AvailableLocales.Locales;
        // foreach (var locale in locales)
        // {
        //     Debug.Log(locale.Identifier.Code);
        // }
    }

    public IEnumerator ChangeLocaleString(string languageCode)
    {
        if (active) yield break;
        StartCoroutine(SetLocaleString(languageCode));
    }
    
    IEnumerator SetLocaleString(string languageCode)
    {
        // Retrieve the available locales from the localization settings
        var locales = LocalizationSettings.AvailableLocales.Locales;
        
        // Find the locale with the matching language code
        Locale targetLocale = null;
        foreach (var locale in locales)
        {
            if (locale.Identifier.Code.Contains(languageCode))
            {
                targetLocale = locale;
                break;
            }
        }

        // If no matching locale is found, return or handle the error
        if (targetLocale == null)
        {
            Debug.LogError($"Locale with code '{languageCode}' not found.");
            yield break;
        }

        // Update the current locale
        currentLocaleString = languageCode;
        PlayerPrefs.SetString("CurrentLocaleString", currentLocaleString);

        active = true;
        yield return LocalizationSettings.InitializationOperation;
        LocalizationSettings.SelectedLocale = targetLocale;
        active = false;
    }
    
    // public void ChangeLocale(int id)
    // {
    //     if(active) return;
    //     StartCoroutine(SetLocale(id));
    // }

    // IEnumerator SetLocale(int id)
    // {
    //     currentLocale = id;
    //     PlayerPrefs.SetInt("CurrentLocale", currentLocale);
    //     
    //     active = true;
    //     yield return LocalizationSettings.InitializationOperation;
    //     LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales[id];
    //     active = false;
    // }
}   