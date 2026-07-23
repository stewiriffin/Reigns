using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Initializes Unity Ads on startup and exposes interstitial + rewarded video APIs.
/// Define scripting symbol UNITY_ADS after installing the Advertisement package for device builds.
/// Without UNITY_ADS, a mock provider is used so Editor / CI still compile and can test flow.
/// </summary>
public class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }

    [Header("Unity Ads")]
    [SerializeField] private string androidGameId = "YOUR_ANDROID_GAME_ID";
    [SerializeField] private string iOSGameId = "YOUR_IOS_GAME_ID";
    [SerializeField] private bool testMode = true;
    [SerializeField] private string interstitialAdUnitId = "Interstitial_Android";
    [SerializeField] private string rewardedAdUnitId = "Rewarded_Android";

    [Header("Game Over Interstitial")]
    [Range(0f, 1f)]
    [SerializeField] private float gameOverInterstitialChance = 0.3f;

    [Header("Audio / Pause")]
    [SerializeField] private bool pauseAudioDuringAds = true;
    [SerializeField] private bool pauseTimeScaleDuringAds = true;

    /// <summary>Fired when an ad unit finishes loading successfully.</summary>
    public event Action<string> OnAdLoaded;

    /// <summary>Fired when an ad unit fails to load. Args: adUnitId, error.</summary>
    public event Action<string, string> OnAdFailedToLoad;

    /// <summary>Fired when an ad finishes showing (completed, skipped, or failed). Args: adUnitId, completed.</summary>
    public event Action<string, bool> OnAdShowComplete;

    public bool IsInitialized { get; private set; }
    public bool IsShowingAd { get; private set; }

    private bool interstitialReady;
    private bool rewardedReady;
    private float previousTimeScale = 1f;
    private bool previousAudioPause;

    private Action pendingRewardedSuccess;
    private Action pendingRewardedFail;
    private Action pendingInterstitialComplete;

#if UNITY_ADS
    private UnityAdsBridge adsBridge;
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

#if UNITY_IOS
        if (rewardedAdUnitId == "Rewarded_Android")
            rewardedAdUnitId = "Rewarded_iOS";
        if (interstitialAdUnitId == "Interstitial_Android")
            interstitialAdUnitId = "Interstitial_iOS";
#endif
    }

    private void Start()
    {
        InitializeAds();
    }

    /// <summary>
    /// Initializes the ad SDK. Safe to call once at startup.
    /// </summary>
    public void InitializeAds()
    {
        if (IsInitialized)
            return;

        string gameId = GetPlatformGameId();
        if (string.IsNullOrWhiteSpace(gameId) || gameId.StartsWith("YOUR_"))
            Debug.LogWarning("AdManager: Replace placeholder Game IDs with your Unity Ads dashboard IDs.");

#if UNITY_ADS
        adsBridge = new UnityAdsBridge(this);
        adsBridge.Initialize(gameId, testMode);
#else
        Debug.Log("AdManager: UNITY_ADS not defined — using mock ads (Editor/dev).");
        IsInitialized = true;
        LoadInterstitial();
        LoadRewarded();
#endif
    }

    public bool ShouldShowGameOverInterstitial()
    {
        return UnityEngine.Random.value < gameOverInterstitialChance;
    }

    /// <summary>
    /// Shows an interstitial if loaded; always invokes <paramref name="onComplete"/> when finished or skipped.
    /// </summary>
    public void ShowInterstitial(Action onComplete = null)
    {
        pendingInterstitialComplete = onComplete;

        if (!IsInitialized || !interstitialReady)
        {
            Debug.LogWarning("AdManager: Interstitial not ready — skipping.");
            FinishInterstitial(completed: false);
            return;
        }

#if UNITY_ADS
        BeginAdPresentation(interstitialAdUnitId);
        adsBridge.Show(interstitialAdUnitId);
#else
        StartCoroutine(MockShowAd(interstitialAdUnitId, grantReward: false, isInterstitial: true));
#endif
    }

    /// <summary>
    /// Shows a rewarded video. <paramref name="onRewarded"/> runs only if the user earns the reward.
    /// </summary>
    public void ShowRewarded(Action onRewarded, Action onFailedOrSkipped = null)
    {
        pendingRewardedSuccess = onRewarded;
        pendingRewardedFail = onFailedOrSkipped;

        if (!IsInitialized || !rewardedReady)
        {
            Debug.LogWarning("AdManager: Rewarded ad not ready.");
            OnAdFailedToLoad?.Invoke(rewardedAdUnitId, "Rewarded ad not loaded");
            pendingRewardedFail?.Invoke();
            ClearRewardedCallbacks();
            return;
        }

#if UNITY_ADS
        BeginAdPresentation(rewardedAdUnitId);
        adsBridge.Show(rewardedAdUnitId);
#else
        StartCoroutine(MockShowAd(rewardedAdUnitId, grantReward: true, isInterstitial: false));
#endif
    }

    public void LoadInterstitial()
    {
#if UNITY_ADS
        if (adsBridge != null)
            adsBridge.Load(interstitialAdUnitId);
#else
        interstitialReady = true;
        OnAdLoaded?.Invoke(interstitialAdUnitId);
#endif
    }

    public void LoadRewarded()
    {
#if UNITY_ADS
        if (adsBridge != null)
            adsBridge.Load(rewardedAdUnitId);
#else
        rewardedReady = true;
        OnAdLoaded?.Invoke(rewardedAdUnitId);
#endif
    }

    internal void HandleInitialized()
    {
        IsInitialized = true;
        Debug.Log("AdManager: Initialization complete.");
        LoadInterstitial();
        LoadRewarded();
    }

    internal void HandleInitializationFailed(string message)
    {
        IsInitialized = false;
        Debug.LogError($"AdManager: Initialization failed — {message}");
        OnAdFailedToLoad?.Invoke("sdk", message);
    }

    internal void HandleAdLoaded(string adUnitId)
    {
        if (adUnitId == interstitialAdUnitId)
            interstitialReady = true;
        if (adUnitId == rewardedAdUnitId)
            rewardedReady = true;

        Debug.Log($"AdManager: OnAdLoaded — {adUnitId}");
        OnAdLoaded?.Invoke(adUnitId);
    }

    internal void HandleAdFailedToLoad(string adUnitId, string error)
    {
        if (adUnitId == interstitialAdUnitId)
            interstitialReady = false;
        if (adUnitId == rewardedAdUnitId)
            rewardedReady = false;

        Debug.LogWarning($"AdManager: OnAdFailedToLoad — {adUnitId}: {error}");
        OnAdFailedToLoad?.Invoke(adUnitId, error);
    }

    internal void HandleAdShowComplete(string adUnitId, bool completed, bool rewarded)
    {
        EndAdPresentation();

        Debug.Log($"AdManager: OnAdShowComplete — {adUnitId}, completed={completed}, rewarded={rewarded}");
        OnAdShowComplete?.Invoke(adUnitId, completed);

        if (adUnitId == interstitialAdUnitId)
        {
            interstitialReady = false;
            FinishInterstitial(completed);
            LoadInterstitial();
            return;
        }

        if (adUnitId == rewardedAdUnitId)
        {
            rewardedReady = false;
            if (rewarded && completed)
                pendingRewardedSuccess?.Invoke();
            else
                pendingRewardedFail?.Invoke();

            ClearRewardedCallbacks();
            LoadRewarded();
        }
    }

    private void FinishInterstitial(bool completed)
    {
        Action callback = pendingInterstitialComplete;
        pendingInterstitialComplete = null;
        callback?.Invoke();
    }

    private void ClearRewardedCallbacks()
    {
        pendingRewardedSuccess = null;
        pendingRewardedFail = null;
    }

    private void BeginAdPresentation(string adUnitId)
    {
        IsShowingAd = true;
        previousTimeScale = Time.timeScale;
        previousAudioPause = AudioListener.pause;

        if (pauseTimeScaleDuringAds)
            Time.timeScale = 0f;

        if (pauseAudioDuringAds)
            AudioListener.pause = true;
    }

    private void EndAdPresentation()
    {
        IsShowingAd = false;

        if (pauseTimeScaleDuringAds)
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;

        if (pauseAudioDuringAds)
            AudioListener.pause = previousAudioPause;
    }

    private string GetPlatformGameId()
    {
#if UNITY_IOS
        return iOSGameId;
#else
        return androidGameId;
#endif
    }

#if !UNITY_ADS
    private IEnumerator MockShowAd(string adUnitId, bool grantReward, bool isInterstitial)
    {
        BeginAdPresentation(adUnitId);
        // Unscaled wait so pause doesn't freeze the mock.
        yield return new WaitForSecondsRealtime(0.75f);
        HandleAdShowComplete(adUnitId, completed: true, rewarded: grantReward);
    }
#endif

#if UNITY_ADS
    /// <summary>
    /// Thin bridge around UnityEngine.Advertisements so the rest of AdManager stays readable.
    /// </summary>
    private sealed class UnityAdsBridge :
        UnityEngine.Advertisements.IUnityAdsInitializationListener,
        UnityEngine.Advertisements.IUnityAdsLoadListener,
        UnityEngine.Advertisements.IUnityAdsShowListener
    {
        private readonly AdManager owner;

        public UnityAdsBridge(AdManager owner)
        {
            this.owner = owner;
        }

        public void Initialize(string gameId, bool testMode)
        {
            UnityEngine.Advertisements.Advertisement.Initialize(gameId, testMode, this);
        }

        public void Load(string adUnitId)
        {
            UnityEngine.Advertisements.Advertisement.Load(adUnitId, this);
        }

        public void Show(string adUnitId)
        {
            UnityEngine.Advertisements.Advertisement.Show(adUnitId, this);
        }

        public void OnInitializationComplete()
        {
            owner.HandleInitialized();
        }

        public void OnInitializationFailed(
            UnityEngine.Advertisements.UnityAdsInitializationError error,
            string message)
        {
            owner.HandleInitializationFailed($"{error}: {message}");
        }

        public void OnUnityAdsAdLoaded(string adUnitId)
        {
            owner.HandleAdLoaded(adUnitId);
        }

        public void OnUnityAdsFailedToLoad(
            string adUnitId,
            UnityEngine.Advertisements.UnityAdsLoadError error,
            string message)
        {
            owner.HandleAdFailedToLoad(adUnitId, $"{error}: {message}");
        }

        public void OnUnityAdsShowFailure(
            string adUnitId,
            UnityEngine.Advertisements.UnityAdsShowError error,
            string message)
        {
            owner.HandleAdShowComplete(adUnitId, completed: false, rewarded: false);
        }

        public void OnUnityAdsShowStart(string adUnitId) { }

        public void OnUnityAdsShowClick(string adUnitId) { }

        public void OnUnityAdsShowComplete(
            string adUnitId,
            UnityEngine.Advertisements.UnityAdsShowCompletionState showCompletionState)
        {
            bool completed = showCompletionState == UnityEngine.Advertisements.UnityAdsShowCompletionState.COMPLETED;
            bool rewarded = completed; // rewarded placements only grant on COMPLETED
            owner.HandleAdShowComplete(adUnitId, completed, rewarded);
        }
    }
#endif
}
