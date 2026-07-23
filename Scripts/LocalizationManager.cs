using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class LocalizationEntry
{
    public string key;
    public string value;
}

[Serializable]
public class LocalizationTable
{
    public LocalizationEntry[] strings;
}

/// <summary>
/// Loads JSON string tables (en/es/sw/…) and resolves localization keys at runtime.
/// </summary>
public class LocalizationManager : MonoBehaviour
{
    public const string PrefsLanguageCode = "Locale_LanguageCode";

    public static LocalizationManager Instance { get; private set; }

    /// <summary>Fired after the active language table is replaced.</summary>
    public static event Action OnLanguageChanged;

    [SerializeField] private string resourcesFolder = "Localization";
    [SerializeField] private string defaultLanguageCode = "en";
    [SerializeField] private bool dontDestroyOnLoad = true;

    private readonly Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguageCode { get; private set; } = "en";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        string saved = PlayerPrefs.GetString(PrefsLanguageCode, string.Empty);
        string code = string.IsNullOrWhiteSpace(saved)
            ? ResolveDeviceLanguageCode()
            : saved;

        SetLanguage(code, savePreference: false);
    }

    /// <summary>
    /// Maps Unity <see cref="Application.systemLanguage"/> to a short locale code.
    /// </summary>
    public string ResolveDeviceLanguageCode()
    {
        return Application.systemLanguage switch
        {
            SystemLanguage.Spanish => "es",
            SystemLanguage.Swahili => "sw",
            SystemLanguage.French => "fr",
            SystemLanguage.German => "de",
            SystemLanguage.Portuguese => "pt",
            SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional => "zh",
            SystemLanguage.Japanese => "ja",
            SystemLanguage.Korean => "ko",
            SystemLanguage.Arabic => "ar",
            SystemLanguage.Russian => "ru",
            _ => defaultLanguageCode
        };
    }

    /// <summary>
    /// Loads Resources/<folder>/<code>.json and makes it the active table.
    /// Falls back to <see cref="defaultLanguageCode"/> when the file is missing.
    /// </summary>
    public void SetLanguage(string languageCode, bool savePreference = true)
    {
        string code = string.IsNullOrWhiteSpace(languageCode)
            ? defaultLanguageCode
            : languageCode.Trim().ToLowerInvariant();

        if (!TryLoadTable(code))
        {
            if (!string.Equals(code, defaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"LocalizationManager: Missing '{code}.json' — falling back to '{defaultLanguageCode}'.");
                code = defaultLanguageCode;
                TryLoadTable(code);
            }
        }

        CurrentLanguageCode = code;

        if (savePreference)
        {
            PlayerPrefs.SetString(PrefsLanguageCode, code);
            PlayerPrefs.Save();
        }

        OnLanguageChanged?.Invoke();
    }

    public static string Get(string key, string fallback = null)
    {
        if (Instance == null)
            EnsureExists();

        if (Instance == null)
            return string.IsNullOrEmpty(fallback) ? key : fallback;

        return Instance.GetInternal(key, fallback);
    }

    private static void EnsureExists()
    {
        LocalizationManager existing = FindObjectOfType<LocalizationManager>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        var go = new GameObject(nameof(LocalizationManager));
        go.AddComponent<LocalizationManager>();
    }

    public string GetInternal(string key, string fallback = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback ?? string.Empty;

        if (table.TryGetValue(key, out string value) && value != null)
            return value;

        return fallback ?? key;
    }

    public bool HasKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && table.ContainsKey(key);
    }

    private bool TryLoadTable(string languageCode)
    {
        string path = $"{resourcesFolder}/{languageCode}";
        TextAsset asset = Resources.Load<TextAsset>(path);
        if (asset == null)
            return false;

        LocalizationTable parsed = JsonUtility.FromJson<LocalizationTable>(asset.text);
        table.Clear();

        if (parsed?.strings == null)
            return true;

        foreach (LocalizationEntry entry in parsed.strings)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                continue;

            table[entry.key.Trim()] = entry.value ?? string.Empty;
        }

        Debug.Log($"LocalizationManager: Loaded {table.Count} strings for '{languageCode}'.");
        return true;
    }
}
