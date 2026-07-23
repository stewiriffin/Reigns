using System;
using UnityEngine;
#if GOOGLE_PLAY_GAMES
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using UnityEngine.SocialPlatforms;
#endif

/// <summary>
/// Google Play Games Services wrapper: silent sign-in, Years Ruled leaderboard submit,
/// and native leaderboard UI.
///
/// Enable by installing the official plugin and adding scripting define <c>GOOGLE_PLAY_GAMES</c>.
/// See the class summary comments / project README section for Play Console setup steps.
/// </summary>
public class PlayServicesManager : MonoBehaviour
{
    public static PlayServicesManager Instance { get; private set; }

    [Header("Leaderboards")]
    [Tooltip("Leaderboard ID from Play Console (or GPGSIds after plugin setup).")]
    [SerializeField] private string yearsRuledLeaderboardId = "CgkIxxxxxxxxxxxxxxxxxxxxAQ";

    [Header("Auth")]
    [SerializeField] private bool authenticateOnStart = true;
    [SerializeField] private bool enableDebugLogs = true;

    public bool IsAuthenticated { get; private set; }
    public string YearsRuledLeaderboardId => yearsRuledLeaderboardId;

    public event Action<bool> OnAuthenticationFinished;

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

    private void Start()
    {
        if (authenticateOnStart)
            AuthenticateSilently();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Activates Play Games and attempts a silent / cached sign-in (no account picker if possible).
    /// </summary>
    public void AuthenticateSilently()
    {
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            PlayGamesPlatform.DebugLogEnabled = enableDebugLogs;
            PlayGamesPlatform.Activate();

            Log("Requesting silent authentication…");
            PlayGamesPlatform.Instance.Authenticate(OnAuthenticateResult);
        }
        catch (Exception e)
        {
            IsAuthenticated = false;
            LogWarning("AuthenticateSilently failed: " + e.Message);
            OnAuthenticationFinished?.Invoke(false);
        }
#elif GOOGLE_PLAY_GAMES && UNITY_EDITOR
        Log("Editor stub — Play Games auth skipped. Use an Android device build.");
        IsAuthenticated = false;
        OnAuthenticationFinished?.Invoke(false);
#else
        Log("GOOGLE_PLAY_GAMES not defined — Play Services stub active.");
        IsAuthenticated = false;
        OnAuthenticationFinished?.Invoke(false);
#endif
    }

    /// <summary>
    /// Shows the interactive sign-in UI if silent auth failed or the player is signed out.
    /// </summary>
    public void AuthenticateInteractively()
    {
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID && !UNITY_EDITOR
        if (PlayGamesPlatform.Instance == null)
        {
            PlayGamesPlatform.Activate();
        }

        PlayGamesPlatform.Instance.ManuallyAuthenticate(OnAuthenticateResult);
#else
        AuthenticateSilently();
#endif
    }

    /// <summary>
    /// Posts the final Years Ruled score to the configured leaderboard.
    /// Call from Game Over (after the run score is final).
    /// </summary>
    public void SubmitYearsRuled(int yearsRuled)
    {
        if (yearsRuled < 0)
            yearsRuled = 0;

        if (string.IsNullOrWhiteSpace(yearsRuledLeaderboardId) ||
            yearsRuledLeaderboardId.StartsWith("CgkIxxxx", StringComparison.Ordinal))
        {
            LogWarning("SubmitYearsRuled: replace yearsRuledLeaderboardId with your Play Console ID.");
        }

#if GOOGLE_PLAY_GAMES && UNITY_ANDROID && !UNITY_EDITOR
        if (!IsAuthenticated)
        {
            LogWarning("SubmitYearsRuled: not authenticated — attempting silent auth then submit.");
            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                OnAuthenticateResult(status);
                if (IsAuthenticated)
                    ReportScoreInternal(yearsRuled);
            });
            return;
        }

        ReportScoreInternal(yearsRuled);
#else
        Log($"Stub SubmitYearsRuled({yearsRuled}) → leaderboard '{yearsRuledLeaderboardId}'.");
#endif
    }

    /// <summary>
    /// Opens the native Google Play leaderboard overlay for Years Ruled (or all boards).
    /// </summary>
    public void ShowYearsRuledLeaderboard()
    {
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID && !UNITY_EDITOR
        if (!IsAuthenticated)
        {
            LogWarning("ShowYearsRuledLeaderboard: not signed in — prompting interactive auth.");
            PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
            {
                OnAuthenticateResult(status);
                if (IsAuthenticated)
                    ShowLeaderboardUiInternal();
            });
            return;
        }

        ShowLeaderboardUiInternal();
#else
        Log("Stub ShowYearsRuledLeaderboard — native UI only on Android with Play Games plugin.");
#endif
    }

    /// <summary>Shows the full Play Games leaderboards list.</summary>
    public void ShowAllLeaderboards()
    {
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID && !UNITY_EDITOR
        if (!IsAuthenticated)
        {
            AuthenticateInteractively();
            return;
        }

        Social.ShowLeaderboardUI();
#else
        Log("Stub ShowAllLeaderboards.");
#endif
    }

#if GOOGLE_PLAY_GAMES
    private void OnAuthenticateResult(SignInStatus status)
    {
        IsAuthenticated = status == SignInStatus.Success;
        if (IsAuthenticated)
            Log("Authenticated as " + Social.localUser.userName);
        else
            LogWarning("Authentication result: " + status);

        OnAuthenticationFinished?.Invoke(IsAuthenticated);
    }

    private void ReportScoreInternal(int yearsRuled)
    {
        // Social.ReportScore uses long; Years Ruled fits comfortably.
        Social.ReportScore(yearsRuled, yearsRuledLeaderboardId, success =>
        {
            if (success)
                Log($"Submitted Years Ruled = {yearsRuled}");
            else
                LogWarning($"Failed to submit Years Ruled = {yearsRuled}");
        });
    }

    private void ShowLeaderboardUiInternal()
    {
        if (!string.IsNullOrWhiteSpace(yearsRuledLeaderboardId) &&
            !yearsRuledLeaderboardId.StartsWith("CgkIxxxx", StringComparison.Ordinal))
        {
            PlayGamesPlatform.Instance.ShowLeaderboardUI(yearsRuledLeaderboardId);
        }
        else
        {
            Social.ShowLeaderboardUI();
        }
    }
#else
    private void OnAuthenticateResult(bool success)
    {
        IsAuthenticated = success;
        OnAuthenticationFinished?.Invoke(success);
    }
#endif

    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log("PlayServicesManager: " + message);
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning("PlayServicesManager: " + message);
    }
}
