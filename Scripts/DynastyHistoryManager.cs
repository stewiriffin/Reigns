using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Persistent dynasty log: every finished monarch is appended to a JSON file
/// under <see cref="Application.persistentDataPath"/>.
/// </summary>
public class DynastyHistoryManager : MonoBehaviour
{
    public static DynastyHistoryManager Instance { get; private set; }

    private const string FileName = "dynasty_history.json";
    private const int MaxRecords = 200;

    private static readonly string[] RoyalNames =
    {
        "Henry", "Aldric", "Edmund", "Cedric", "Roland",
        "Isolde", "Matilda", "Elara", "Godwin", "Theron",
        "Beatrix", "Osric", "Lior", "Seraphine", "Cassian"
    };

    private readonly List<MonarchRecord> records = new List<MonarchRecord>();
    private MonarchRecord pendingRecord;
    private bool hasPendingRecord;

    private string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    public IReadOnlyList<MonarchRecord> Records => records;
    public int TotalMonarchs => records.Count;

    public float AverageReignDuration
    {
        get
        {
            if (records.Count == 0)
                return 0f;

            long sum = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null)
                    sum += records[i].yearsRuled;
            }

            return sum / (float)records.Count;
        }
    }

    public event Action OnHistoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            CommitPendingRecord();
    }

    private void OnApplicationQuit()
    {
        CommitPendingRecord();
    }

    /// <summary>
    /// Stages a monarch for the Dynasty Hall. Committed when the player leaves
    /// Game Over without a Second Chance, or on app background/quit.
    /// </summary>
    public void StagePendingDeath(int yearsRuled, DeathCause cause)
    {
        if (cause == DeathCause.None)
            return;

        string name = GenerateMonarchName(records.Count + 1);
        pendingRecord = MonarchRecord.Create(name, yearsRuled, cause, DateTime.UtcNow);
        hasPendingRecord = true;
    }

    /// <summary>Second Chance used — do not write this death to history.</summary>
    public void CancelPendingRecord()
    {
        pendingRecord = null;
        hasPendingRecord = false;
    }

    /// <summary>Writes the staged death into the persistent list (idempotent).</summary>
    public void CommitPendingRecord()
    {
        if (!hasPendingRecord || pendingRecord == null)
            return;

        records.Add(pendingRecord);
        TrimToCap();
        Save();

        pendingRecord = null;
        hasPendingRecord = false;
        OnHistoryChanged?.Invoke();
    }

    public void ClearHistory()
    {
        records.Clear();
        CancelPendingRecord();
        Save();
        OnHistoryChanged?.Invoke();
    }

    public static string FormatDeathCauseShort(DeathCause cause)
    {
        return cause switch
        {
            DeathCause.None => "Legendary Ending",
            DeathCause.ReligionEmpty => "Faith Lost",
            DeathCause.ReligionFull => "Theocracy",
            DeathCause.PeopleEmpty => "Revolt",
            DeathCause.PeopleFull => "Mob Rule",
            DeathCause.ArmyEmpty => "Defenseless",
            DeathCause.ArmyFull => "Coup",
            DeathCause.WealthEmpty => "Bankrupt",
            DeathCause.WealthFull => "Bought Out",
            _ => "Unknown"
        };
    }

    public static string GetDeathIconGlyph(DeathCause cause)
    {
        return cause switch
        {
            DeathCause.None => "★",
            DeathCause.ReligionEmpty => "R↓",
            DeathCause.ReligionFull => "R↑",
            DeathCause.PeopleEmpty => "P↓",
            DeathCause.PeopleFull => "P↑",
            DeathCause.ArmyEmpty => "A↓",
            DeathCause.ArmyFull => "A↑",
            DeathCause.WealthEmpty => "W↓",
            DeathCause.WealthFull => "W↑",
            _ => "?"
        };
    }

    public static Color GetDeathIconColor(DeathCause cause)
    {
        return cause switch
        {
            DeathCause.ReligionEmpty or DeathCause.ReligionFull =>
                new Color(0.72f, 0.62f, 0.95f, 1f),
            DeathCause.PeopleEmpty or DeathCause.PeopleFull =>
                new Color(0.55f, 0.82f, 0.55f, 1f),
            DeathCause.ArmyEmpty or DeathCause.ArmyFull =>
                new Color(0.9f, 0.45f, 0.4f, 1f),
            DeathCause.WealthEmpty or DeathCause.WealthFull =>
                new Color(0.95f, 0.82f, 0.35f, 1f),
            _ => new Color(0.6f, 0.6f, 0.6f, 1f)
        };
    }

    private string GenerateMonarchName(int ordinal)
    {
        ordinal = Mathf.Max(1, ordinal);
        string name = RoyalNames[(ordinal - 1) % RoyalNames.Length];
        string title = IsFeminineName(name) ? "Queen" : "King";
        return $"{title} {name} {ToRoman(ordinal)}";
    }

    private static bool IsFeminineName(string name)
    {
        return name is "Isolde" or "Matilda" or "Elara" or "Beatrix" or "Seraphine";
    }

    private static string ToRoman(int number)
    {
        if (number <= 0)
            return "I";

        // Cap display roman numerals for very long dynasties.
        number = Mathf.Min(number, 3999);

        var sb = new StringBuilder(8);
        (int value, string numeral)[] map =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };

        for (int i = 0; i < map.Length; i++)
        {
            while (number >= map[i].value)
            {
                sb.Append(map[i].numeral);
                number -= map[i].value;
            }
        }

        return sb.ToString();
    }

    private void TrimToCap()
    {
        while (records.Count > MaxRecords)
            records.RemoveAt(0);
    }

    private void Load()
    {
        records.Clear();
        try
        {
            if (!File.Exists(FilePath))
                return;

            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return;

            MonarchRecordList wrapper = JsonUtility.FromJson<MonarchRecordList>(json);
            if (wrapper?.records == null)
                return;

            for (int i = 0; i < wrapper.records.Length; i++)
            {
                if (wrapper.records[i] != null)
                    records.Add(wrapper.records[i]);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("DynastyHistoryManager: Failed to load — " + e.Message);
        }
    }

    private void Save()
    {
        try
        {
            var wrapper = new MonarchRecordList { records = records.ToArray() };
            string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
            File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("DynastyHistoryManager: Failed to save — " + e.Message);
        }
    }
}
