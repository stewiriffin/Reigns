using System;
using UnityEngine;

/// <summary>
/// Daily Challenge Mode: shared UTC-date seed so every player draws the same shuffled deck,
/// one attempt per calendar day, no Second Chance / rewinds.
/// </summary>
public class DailyChallengeManager : MonoBehaviour
{
    public static DailyChallengeManager Instance { get; private set; }

    private const string PrefsScorePrefix = "DailyScore_";
    private const string PrefsAttemptPrefix = "DailyAttempt_";

    [SerializeField] private bool logSeedInEditor = true;

    /// <summary>True while an in-progress Daily run is active.</summary>
    public bool IsDailyRunActive { get; private set; }

    /// <summary>True if the run that just ended (or is ending) was a Daily attempt.</summary>
    public bool LastRunWasDaily { get; private set; }

    public int TodaySeed => GetUtcDateSeed(DateTime.UtcNow);
    public string TodayKey => TodaySeed.ToString();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Integer seed from UTC calendar date: YYYYMMDD (e.g. 20260723).
    /// </summary>
    public static int GetUtcDateSeed(DateTime utc)
    {
        utc = utc.ToUniversalTime();
        return utc.Year * 10000 + utc.Month * 100 + utc.Day;
    }

    public bool HasPlayedToday()
    {
        string key = TodayKey;
        return PlayerPrefs.HasKey(PrefsAttemptPrefix + key) ||
               PlayerPrefs.HasKey(PrefsScorePrefix + key);
    }

    public bool HasScoreToday()
    {
        return PlayerPrefs.HasKey(PrefsScorePrefix + TodayKey);
    }

    public int GetTodayScore()
    {
        return PlayerPrefs.GetInt(PrefsScorePrefix + TodayKey, -1);
    }

    public int GetScoreForSeed(int seed)
    {
        return PlayerPrefs.GetInt(PrefsScorePrefix + seed, -1);
    }

    /// <summary>
    /// Locks today's attempt and seeds UnityEngine.Random for a shared deck order.
    /// Returns false if the player already used today's challenge.
    /// </summary>
    public bool TryBeginDailyRun()
    {
        if (HasPlayedToday())
            return false;

        int seed = TodaySeed;
        Random.InitState(seed);

        PlayerPrefs.SetInt(PrefsAttemptPrefix + TodayKey, 1);
        PlayerPrefs.Save();

        IsDailyRunActive = true;
        LastRunWasDaily = true;

#if UNITY_EDITOR
        if (logSeedInEditor)
            Debug.Log($"DailyChallengeManager: Daily run started — UTC seed {seed}.");
#endif
        return true;
    }

    /// <summary>
    /// Persists years ruled for today and ends Daily mode (call on final Game Over).
    /// </summary>
    public void RecordDailyScore(int yearsRuled)
    {
        if (!IsDailyRunActive && !LastRunWasDaily)
            return;

        string key = TodayKey;
        int previous = PlayerPrefs.GetInt(PrefsScorePrefix + key, -1);
        int score = Mathf.Max(0, yearsRuled);

        // Keep the best score if somehow recorded twice.
        if (previous < 0 || score > previous)
            PlayerPrefs.SetInt(PrefsScorePrefix + key, score);

        PlayerPrefs.SetInt(PrefsAttemptPrefix + key, 1);
        PlayerPrefs.Save();

        IsDailyRunActive = false;
        RestoreGlobalRandom();

#if UNITY_EDITOR
        if (logSeedInEditor)
            Debug.Log($"DailyChallengeManager: Daily score saved — {score} years (seed {key}).");
#endif
    }

    /// <summary>Leaves Daily mode without writing a score (e.g. aborted before play).</summary>
    public void AbortDailyRunWithoutScore()
    {
        IsDailyRunActive = false;
        RestoreGlobalRandom();
    }

    /// <summary>Clears the daily flag when starting a normal (non-daily) run.</summary>
    public void BeginNormalRun()
    {
        if (IsDailyRunActive)
            AbortDailyRunWithoutScore();

        LastRunWasDaily = false;
        IsDailyRunActive = false;
    }

    public static void RestoreGlobalRandom()
    {
        Random.InitState(Environment.TickCount);
    }

    public string GetStatusLabel()
    {
        if (HasScoreToday())
            return $"Daily done — {GetTodayScore()} yrs";

        if (HasPlayedToday())
            return "Daily — attempt used";

        return $"Daily — {TodayKey}";
    }
}
