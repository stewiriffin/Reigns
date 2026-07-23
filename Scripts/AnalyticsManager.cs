using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_CLOUD_SERVICES_ANALYTICS
using UnityEngine.Analytics;
#endif
#if FIREBASE_ANALYTICS
using Firebase;
using Firebase.Analytics;
#endif

/// <summary>
/// Initializes Unity Analytics and/or Firebase Analytics and sends privacy-gated custom events.
/// Opt-out is persisted in PlayerPrefs and editable from <see cref="SettingsMenu"/>.
///
/// Setup:
/// - Unity Analytics: enable the Analytics module (Project Settings → Services / Analytics).
/// - Firebase: install the Firebase Analytics SDK and add scripting define <c>FIREBASE_ANALYTICS</c>.
/// </summary>
public class AnalyticsManager : MonoBehaviour
{
    public const string EventPlayerDeath = "PlayerDeath";
    public const string EventCardChoice = "CardChoice";

    public const string ParamYearsSurvived = "YearsSurvived";
    public const string ParamDeathReason = "DeathReason";
    public const string ParamLastCardId = "LastCardID";
    public const string ParamCardId = "CardID";
    public const string ParamDirection = "Direction";

    public const string PrefsOptOut = "Analytics_OptOut";

    public static AnalyticsManager Instance { get; private set; }

    public enum AnalyticsBackend
    {
        Auto = 0,
        UnityAnalytics = 1,
        Firebase = 2,
        DebugLogOnly = 3
    }

    [Header("Backend")]
    [SerializeField] private AnalyticsBackend backend = AnalyticsBackend.Auto;
    [SerializeField] private bool initializeOnAwake = true;
    [SerializeField] private bool logEventsInEditor = true;

    [Header("Pivotal story cards (CardChoice)")]
    [Tooltip("Only these card IDs emit CardChoice events.")]
    [SerializeField] private string[] pivotalCardIds =
    {
        "traveling_hunter",
        "dragon_expedition_report",
        "dragon_tribute",
        "dragon_hoard"
    };

    private readonly HashSet<string> pivotalLookup = new HashSet<string>(StringComparer.Ordinal);
    private bool initialized;
    private bool unityReady;
    private bool firebaseReady;
    private AnalyticsBackend resolvedBackend;

    /// <summary>True when the player has opted out of analytics.</summary>
    public static bool IsOptedOut
    {
        get => PlayerPrefs.GetInt(PrefsOptOut, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PrefsOptOut, value ? 1 : 0);
            PlayerPrefs.Save();
            if (Instance != null)
                Instance.HandleOptOutChanged(value);
        }
    }

    /// <summary>Inverse of opt-out — preferred for a "Share analytics" style toggle.</summary>
    public static bool AnalyticsEnabled
    {
        get => !IsOptedOut;
        set => IsOptedOut = !value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        RebuildPivotalLookup();

        if (initializeOnAwake)
            Initialize();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnValidate()
    {
        RebuildPivotalLookup();
    }

    public void Initialize()
    {
        if (initialized)
            return;

        resolvedBackend = ResolveBackend(backend);

        if (IsOptedOut)
        {
            initialized = true;
            Debug.Log("AnalyticsManager: player opted out — SDK init skipped until re-enabled.");
            return;
        }

        InitializeBackend(resolvedBackend);
        initialized = true;
    }

    /// <summary>
    /// Game Over: YearsSurvived, DeathReason, LastCardID.
    /// </summary>
    public void LogPlayerDeath(int yearsSurvived, DeathCause cause, string lastCardId)
    {
        if (!CanSend())
            return;

        EnsureInitialized();

        var parameters = new Dictionary<string, object>
        {
            { ParamYearsSurvived, yearsSurvived },
            { ParamDeathReason, FormatDeathReason(cause) },
            { ParamLastCardId, string.IsNullOrEmpty(lastCardId) ? "none" : lastCardId }
        };

        SendEvent(EventPlayerDeath, parameters);
    }

    /// <summary>
    /// Logs Left/Right swipe for configured pivotal story cards only.
    /// </summary>
    public void LogCardChoice(string cardId, bool choseLeft)
    {
        if (!CanSend())
            return;

        if (string.IsNullOrEmpty(cardId) || !pivotalLookup.Contains(cardId))
            return;

        EnsureInitialized();

        var parameters = new Dictionary<string, object>
        {
            { ParamCardId, cardId },
            { ParamDirection, choseLeft ? "Left" : "Right" }
        };

        SendEvent(EventCardChoice, parameters);
    }

    public bool IsPivotalCard(string cardId)
    {
        return !string.IsNullOrEmpty(cardId) && pivotalLookup.Contains(cardId);
    }

    public static string FormatDeathReason(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.ReligionEmpty: return "Religion = 0";
            case DeathCause.ReligionFull: return "Religion = 100";
            case DeathCause.PeopleEmpty: return "People = 0";
            case DeathCause.PeopleFull: return "People = 100";
            case DeathCause.ArmyEmpty: return "Army = 0";
            case DeathCause.ArmyFull: return "Army = 100";
            case DeathCause.WealthEmpty: return "Wealth = 0";
            case DeathCause.WealthFull: return "Wealth = 100";
            default: return cause.ToString();
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized)
            Initialize();
        else if (!IsOptedOut && !unityReady && !firebaseReady && resolvedBackend != AnalyticsBackend.DebugLogOnly)
            InitializeBackend(resolvedBackend);
    }

    private bool CanSend()
    {
        if (IsOptedOut)
            return false;

#if UNITY_EDITOR
        if (!logEventsInEditor && resolvedBackend != AnalyticsBackend.DebugLogOnly)
            return false;
#endif
        return true;
    }

    private void HandleOptOutChanged(bool optedOut)
    {
#if ENABLE_CLOUD_SERVICES_ANALYTICS
        if (unityReady)
            Analytics.enabled = !optedOut;
#endif
#if FIREBASE_ANALYTICS
        if (firebaseReady)
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(!optedOut);
#endif

        if (optedOut)
        {
            Debug.Log("AnalyticsManager: opted out — further events suppressed.");
            return;
        }

        // Player re-enabled analytics; initialize providers now if needed.
        initialized = false;
        Initialize();
    }

    private void RebuildPivotalLookup()
    {
        pivotalLookup.Clear();
        if (pivotalCardIds == null)
            return;

        for (int i = 0; i < pivotalCardIds.Length; i++)
        {
            string id = pivotalCardIds[i];
            if (!string.IsNullOrWhiteSpace(id))
                pivotalLookup.Add(id.Trim());
        }
    }

    private static AnalyticsBackend ResolveBackend(AnalyticsBackend requested)
    {
        if (requested != AnalyticsBackend.Auto)
            return requested;

#if FIREBASE_ANALYTICS
        return AnalyticsBackend.Firebase;
#elif ENABLE_CLOUD_SERVICES_ANALYTICS
        return AnalyticsBackend.UnityAnalytics;
#else
        return AnalyticsBackend.DebugLogOnly;
#endif
    }

    private void InitializeBackend(AnalyticsBackend target)
    {
        switch (target)
        {
            case AnalyticsBackend.UnityAnalytics:
                InitializeUnityAnalytics();
                break;
            case AnalyticsBackend.Firebase:
                InitializeFirebaseAnalytics();
                break;
            default:
                unityReady = false;
                firebaseReady = false;
                Debug.Log("AnalyticsManager: using DebugLogOnly backend (no analytics SDK detected).");
                break;
        }
    }

    private void InitializeUnityAnalytics()
    {
#if ENABLE_CLOUD_SERVICES_ANALYTICS
        try
        {
            Analytics.enabled = true;
            Analytics.deviceStatsEnabled = true;
            unityReady = true;
            Debug.Log("AnalyticsManager: Unity Analytics initialized.");
        }
        catch (Exception e)
        {
            unityReady = false;
            Debug.LogWarning("AnalyticsManager: Unity Analytics init failed — " + e.Message);
        }
#else
        unityReady = false;
        Debug.LogWarning(
            "AnalyticsManager: ENABLE_CLOUD_SERVICES_ANALYTICS is not defined. " +
            "Enable Unity Analytics in Project Settings, or switch backend to DebugLogOnly / Firebase.");
#endif
    }

    private void InitializeFirebaseAnalytics()
    {
#if FIREBASE_ANALYTICS
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                firebaseReady = false;
                Debug.LogWarning("AnalyticsManager: Firebase dependency check failed.");
                return;
            }

            if (task.Result == DependencyStatus.Available)
            {
                firebaseReady = true;
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(!IsOptedOut);
                Debug.Log("AnalyticsManager: Firebase Analytics initialized.");
            }
            else
            {
                firebaseReady = false;
                Debug.LogWarning("AnalyticsManager: Firebase dependencies unavailable — " + task.Result);
            }
        });
#else
        firebaseReady = false;
        Debug.LogWarning(
            "AnalyticsManager: FIREBASE_ANALYTICS define missing. " +
            "Install Firebase Analytics and add the scripting define symbol.");
#endif
    }

    private void SendEvent(string eventName, Dictionary<string, object> parameters)
    {
        if (logEventsInEditor || Application.isEditor || resolvedBackend == AnalyticsBackend.DebugLogOnly)
            Debug.Log($"Analytics event '{eventName}': {FormatParams(parameters)}");

        switch (resolvedBackend)
        {
            case AnalyticsBackend.UnityAnalytics:
                SendUnityEvent(eventName, parameters);
                break;
            case AnalyticsBackend.Firebase:
                SendFirebaseEvent(eventName, parameters);
                break;
        }
    }

    private void SendUnityEvent(string eventName, Dictionary<string, object> parameters)
    {
#if ENABLE_CLOUD_SERVICES_ANALYTICS
        if (!unityReady)
            InitializeUnityAnalytics();

        if (!unityReady)
            return;

        AnalyticsResult result = Analytics.CustomEvent(eventName, parameters);
        if (result != AnalyticsResult.Ok && result != AnalyticsResult.TooManyItems)
            Debug.LogWarning($"AnalyticsManager: Unity CustomEvent '{eventName}' → {result}");
#else
        // Fallback already logged via DebugLog path when backend resolves to DebugLogOnly.
#endif
    }

    private void SendFirebaseEvent(string eventName, Dictionary<string, object> parameters)
    {
#if FIREBASE_ANALYTICS
        if (!firebaseReady)
            return;

        var firebaseParams = new Parameter[parameters.Count];
        int i = 0;
        foreach (KeyValuePair<string, object> pair in parameters)
        {
            if (pair.Value is int intVal)
                firebaseParams[i] = new Parameter(pair.Key, intVal);
            else if (pair.Value is long longVal)
                firebaseParams[i] = new Parameter(pair.Key, longVal);
            else if (pair.Value is double doubleVal)
                firebaseParams[i] = new Parameter(pair.Key, doubleVal);
            else if (pair.Value is float floatVal)
                firebaseParams[i] = new Parameter(pair.Key, floatVal);
            else
                firebaseParams[i] = new Parameter(pair.Key, pair.Value != null ? pair.Value.ToString() : string.Empty);
            i++;
        }

        FirebaseAnalytics.LogEvent(eventName, firebaseParams);
#endif
    }

    private static string FormatParams(Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return "{}";

        var sb = new System.Text.StringBuilder(64);
        sb.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, object> pair in parameters)
        {
            if (!first)
                sb.Append(", ");
            sb.Append(pair.Key).Append('=').Append(pair.Value);
            first = false;
        }

        sb.Append('}');
        return sb.ToString();
    }
}
