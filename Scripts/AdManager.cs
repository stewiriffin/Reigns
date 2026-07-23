using System;
using System.Collections;
using UnityEngine;
#if UNITY_ADS
using UnityEngine.Advertisements;
#endif

/// <summary>
/// Ads-only monetization hub (Unity Ads). Singleton initializes on startup and exposes
/// banner, interstitial, and rewarded formats with load/fail/reward callbacks.
///
/// Setup: install the Advertisement package, then add scripting define <c>UNITY_ADS</c>.
/// Without the define, a mock provider keeps Editor/CI stable.
/// </summary>
public class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }

    [Header("Unity Ads — Game IDs")]
    [SerializeField] private string androidGameId = "YOUR_ANDROID_GAME_ID";
    [SerializeField] private string iOSGameId = "YOUR_IOS_GAME_ID";
    [SerializeField] private bool testMode = true;

    [Header("Ad Unit IDs")]
    [SerializeField] private string bannerAdUnitId = "Banner_Android";
    [SerializeField] private string interstitialAdUnitId = "Interstitial_Android";
    [SerializeField] private string rewardedAdUnitId = "Rewarded_Android";

    [Header("Banner")]
    [SerializeField] private bool showBannerDuringGameplay = true;
    [SerializeField] private bool loadBannerOnInit = true;
#if UNITY_ADS
    [SerializeField] private BannerPosition bannerPosition = BannerPosition.BOTTOM_CENTER;
#endif

    [Header("Interstitial Frequency Cap")]
    [Tooltip("Show an interstitial only on every Nth Game Over (e.g. 3 = 3rd, 6th, 9th…).")]
    [SerializeField] private int interstitialEveryNthGameOver = 3;
    [Tooltip("Minimum real-time seconds between any full-screen ads (interstitial or rewarded).")]
    [SerializeField] private float fullscreenAdCooldownSeconds = 180f;

    [Header("Connectivity")]
    [Tooltip("How often to poll reachability and prefetch ads when online.")]
    [SerializeField] private float connectivityPollSeconds = 5f;

    [Header("Fullscreen Ad Presentation")]
    [SerializeField] private float failedLoadRetrySeconds = 15f;
    [Tooltip("Mute BGM via AudioManager while a full-screen ad plays.")]
    [SerializeField] private bool muteBgmDuringFullscreenAds = true;
    [Tooltip("Pause Time.timeScale (gameplay / animations) while a full-screen ad plays.")]
    [SerializeField] private bool pauseTimeScaleDuringAds = true;
    [Tooltip("Also pause AudioListener (mutes SFX). BGM mute above is preferred for music.")]
    [SerializeField] private bool pauseAudioListenerDuringAds = false;

    /// <summary>Fired when any ad unit finishes loading successfully.</summary>
    public event Action<string> OnAdLoaded;

    /// <summary>Fired when any ad unit fails to load. Args: adUnitId, error.</summary>
    public event Action<string, string> OnAdFailedToLoad;

    /// <summary>Fired when a rewarded video grants its reward (user finished watching).</summary>
    public event Action<string> OnUserEarnedReward;

    /// <summary>Fired when an interstitial/rewarded finishes showing. Args: adUnitId, completed.</summary>
    public event Action<string, bool> OnAdShowComplete;

    /// <summary>Fired when rewarded availability for UI (e.g. Second Chance) changes.</summary>
    public event Action<bool> OnRewardedAvailabilityChanged;

    public bool IsInitialized { get; private set; }
    public bool IsShowingAd { get; private set; }
    public bool IsBannerVisible { get; private set; }
    public bool IsBannerReady { get; private set; }
    public bool IsInterstitialReady => interstitialReady;
    public bool IsRewardedReady => rewardedReady;

    /// <summary>True when the device reports a usable network path.</summary>
    public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;

    /// <summary>Rewarded is loaded and the device is online — safe to show Second Chance.</summary>
    public bool CanOfferRewardedAd => IsOnline && IsInitialized && rewardedReady;

    /// <summary>Interstitial is loaded and the device is online.</summary>
    public bool CanOfferInterstitialAd => IsOnline && IsInitialized && interstitialReady;

    private bool interstitialReady;
    private bool rewardedReady;
    private bool bannerLoadRequested;
    private bool bannerAllowedForCurrentScreen;
    private float previousTimeScale = 1f;
    private bool wasOnline;
    private bool lastReportedRewardedAvailability;

    private int gameOverCount;
    private long lastFullscreenAdUnix;

    private Action pendingRewardedSuccess;
    private Action pendingRewardedFail;
    private Action pendingInterstitialComplete;

    private const string PrefsGameOverCount = "Ads_GameOverCount";
    private const string PrefsLastFullscreenAdUnix = "Ads_LastFullscreenAdUnix";

    private Coroutine bannerRetryRoutine;
    private Coroutine interstitialRetryRoutine;
    private Coroutine rewardedRetryRoutine;
    private Coroutine connectivityRoutine;

#if UNITY_ADS
    private UnityAdsBridge adsBridge;
#endif

#if !UNITY_ADS
    private GameObject mockBanner;
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
        ApplyPlatformAdUnitDefaults();
        LoadFrequencyCapState();
        bannerAllowedForCurrentScreen = false;
    }

    private void Start()
    {
        wasOnline = IsOnline;
        InitializeAds();
        connectivityRoutine = StartCoroutine(ConnectivityMonitor());
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
            HandleConnectivityChanged(forcePrefetch: true);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            HandleConnectivityChanged(forcePrefetch: true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (connectivityRoutine != null)
            StopCoroutine(connectivityRoutine);

        HideBanner();
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
        HandleInitialized();
#endif
    }

    /// <summary>
    /// True when this Game Over is eligible for an interstitial:
    /// every Nth death, cooldown elapsed, online, and a cached ad is ready.
    /// Never blocks the game — returns false when offline or unloaded.
    /// </summary>
    public bool ShouldShowGameOverInterstitial()
    {
        int everyNth = Mathf.Max(1, interstitialEveryNthGameOver);
        gameOverCount++;
        PlayerPrefs.SetInt(PrefsGameOverCount, gameOverCount);
        PlayerPrefs.Save();

        if (gameOverCount % everyNth != 0)
        {
            Debug.Log($"AdManager: Skip interstitial — Game Over #{gameOverCount} (need every {everyNth}).");
            return false;
        }

        if (!IsFullscreenCooldownElapsed())
        {
            float remaining = GetFullscreenCooldownRemainingSeconds();
            Debug.Log($"AdManager: Skip interstitial — cooldown {remaining:0}s remaining.");
            return false;
        }

        if (!IsOnline)
        {
            Debug.Log("AdManager: Skip interstitial — device offline.");
            return false;
        }

        if (!CanOfferInterstitialAd)
        {
            Debug.Log("AdManager: Skip interstitial — not cached yet (prefetching in background).");
            PrefetchAdsIfOnline();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sync banner visibility to the active UI screen. Banners only during gameplay.
    /// </summary>
    public void NotifyUiScreen(UIFadeTransition.ScreenId screen)
    {
        bool gameplay = screen == UIFadeTransition.ScreenId.Gameplay;
        SetBannerAllowed(gameplay);
    }

    public void SetBannerAllowed(bool allowed)
    {
        bannerAllowedForCurrentScreen = allowed;
        if (allowed)
            ShowBanner();
        else
            HideBanner();
    }

    public float GetFullscreenCooldownRemainingSeconds()
    {
        if (lastFullscreenAdUnix <= 0)
            return 0f;

        long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        float elapsed = now - lastFullscreenAdUnix;
        return Mathf.Max(0f, fullscreenAdCooldownSeconds - elapsed);
    }

    public bool IsFullscreenCooldownElapsed()
    {
        return GetFullscreenCooldownRemainingSeconds() <= 0.01f;
    }

    // -------------------------------------------------------------------------
    // Banner
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads a bottom-anchored banner (does not show until <see cref="ShowBanner"/>).
    /// </summary>
    public void LoadBanner()
    {
        bannerLoadRequested = true;

        if (!IsInitialized || !IsOnline)
            return;

#if UNITY_ADS
        if (adsBridge == null)
            return;

        adsBridge.LoadBanner(bannerAdUnitId, bannerPosition);
#else
        IsBannerReady = true;
        OnAdLoaded?.Invoke(bannerAdUnitId);
#endif
    }

    /// <summary>
    /// Shows the banner at the bottom during gameplay. No-ops safely if offline/not loaded.
    /// </summary>
    public void ShowBanner()
    {
        if (!showBannerDuringGameplay || !bannerAllowedForCurrentScreen || !IsOnline)
        {
            HideBanner();
            return;
        }

        if (!IsInitialized)
        {
            Debug.LogWarning("AdManager: ShowBanner skipped — SDK not initialized.");
            return;
        }

        if (!IsBannerReady)
        {
            LoadBanner();
#if !UNITY_ADS
            if (bannerAllowedForCurrentScreen && IsOnline)
            {
                ShowMockBanner();
                IsBannerVisible = true;
            }
#endif
            return;
        }

#if UNITY_ADS
        try
        {
            Advertisement.Banner.Show(bannerAdUnitId);
            IsBannerVisible = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("AdManager: ShowBanner failed — " + e.Message);
            OnAdFailedToLoad?.Invoke(bannerAdUnitId, e.Message);
        }
#else
        ShowMockBanner();
        IsBannerVisible = true;
#endif
    }

    /// <summary>
    /// Hides the banner (e.g. Game Over / menus) without destroying the loaded creative.
    /// </summary>
    public void HideBanner()
    {
#if UNITY_ADS
        try
        {
            if (Advertisement.Banner.isLoaded)
                Advertisement.Banner.Hide(false);
        }
        catch (Exception e)
        {
            Debug.LogWarning("AdManager: HideBanner failed — " + e.Message);
        }
#else
        if (mockBanner != null)
            mockBanner.SetActive(false);
#endif
        IsBannerVisible = false;
    }

    // -------------------------------------------------------------------------
    // Interstitial
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full-screen interstitial for non-disruptive transitions (e.g. Game Over).
    /// Always invokes <paramref name="onComplete"/> immediately when offline/unready
    /// so the player never sits on a black loading screen.
    /// </summary>
    public void ShowInterstitial(Action onComplete = null)
    {
        pendingInterstitialComplete = onComplete;

        if (!IsOnline)
        {
            Debug.Log("AdManager: Interstitial suppressed — offline.");
            FinishInterstitial(completed: false);
            return;
        }

        if (!IsInitialized || !interstitialReady)
        {
            Debug.LogWarning("AdManager: Interstitial not ready — skipping (game continues).");
            OnAdFailedToLoad?.Invoke(interstitialAdUnitId, "Interstitial not loaded");
            FinishInterstitial(completed: false);
            PrefetchAdsIfOnline();
            return;
        }

#if UNITY_ADS
        BeginAdPresentation();
        adsBridge.Show(interstitialAdUnitId);
#else
        StartCoroutine(MockShowFullscreen(interstitialAdUnitId, grantReward: false));
#endif
    }

    public void LoadInterstitial()
    {
        if (!IsInitialized || !IsOnline)
            return;

#if UNITY_ADS
        if (adsBridge != null)
            adsBridge.Load(interstitialAdUnitId);
#else
        SetInterstitialReady(true);
        OnAdLoaded?.Invoke(interstitialAdUnitId);
#endif
    }

    /// <summary>
    /// Rewarded video. Offline / unload paths fail fast with no UI stall.
    /// </summary>
    public void ShowRewarded(Action onRewarded, Action onFailedOrSkipped = null)
    {
        pendingRewardedSuccess = onRewarded;
        pendingRewardedFail = onFailedOrSkipped;

        if (!IsOnline)
        {
            Debug.Log("AdManager: Rewarded suppressed — offline.");
            OnAdFailedToLoad?.Invoke(rewardedAdUnitId, "Device offline");
            InvokeRewardedFail();
            return;
        }

        if (!IsInitialized || !rewardedReady)
        {
            Debug.LogWarning("AdManager: Rewarded ad not ready — benefit not granted.");
            OnAdFailedToLoad?.Invoke(rewardedAdUnitId, "Rewarded ad not loaded");
            InvokeRewardedFail();
            PrefetchAdsIfOnline();
            return;
        }

#if UNITY_ADS
        BeginAdPresentation();
        adsBridge.Show(rewardedAdUnitId);
#else
        StartCoroutine(MockShowFullscreen(rewardedAdUnitId, grantReward: true));
#endif
    }

    public void LoadRewarded()
    {
        if (!IsInitialized || !IsOnline)
            return;

#if UNITY_ADS
        if (adsBridge != null)
            adsBridge.Load(rewardedAdUnitId);
#else
        SetRewardedReady(true);
        OnAdLoaded?.Invoke(rewardedAdUnitId);
#endif
    }

    /// <summary>
    /// Background prefetch while online so Game Over can show ads instantly.
    /// </summary>
    public void PrefetchAdsIfOnline()
    {
        if (!IsOnline || !IsInitialized)
            return;

        if (!interstitialReady)
            LoadInterstitial();
        if (!rewardedReady)
            LoadRewarded();
        if ((loadBannerOnInit || bannerLoadRequested) && !IsBannerReady)
            LoadBanner();
    }

    // -------------------------------------------------------------------------
    // Internal handlers
    // -------------------------------------------------------------------------

    internal void HandleInitialized()
    {
        IsInitialized = true;
        Debug.Log("AdManager: Initialization complete.");

        LoadInterstitial();
        LoadRewarded();

        if (loadBannerOnInit || bannerLoadRequested)
            LoadBanner();
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
            SetInterstitialReady(true);
        else if (adUnitId == rewardedAdUnitId)
            SetRewardedReady(true);
        else if (adUnitId == bannerAdUnitId)
            IsBannerReady = true;

        Debug.Log($"AdManager: OnAdLoaded — {adUnitId}");
        OnAdLoaded?.Invoke(adUnitId);

        if (adUnitId == bannerAdUnitId && bannerAllowedForCurrentScreen && showBannerDuringGameplay && !IsBannerVisible)
            ShowBanner();
    }

    internal void HandleAdFailedToLoad(string adUnitId, string error)
    {
        if (adUnitId == interstitialAdUnitId)
        {
            SetInterstitialReady(false);
            if (IsOnline)
                ScheduleRetry(ref interstitialRetryRoutine, LoadInterstitial);
        }
        else if (adUnitId == rewardedAdUnitId)
        {
            SetRewardedReady(false);
            if (IsOnline)
                ScheduleRetry(ref rewardedRetryRoutine, LoadRewarded);
        }
        else if (adUnitId == bannerAdUnitId)
        {
            IsBannerReady = false;
            if (IsOnline)
                ScheduleRetry(ref bannerRetryRoutine, LoadBanner);
        }

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
            SetInterstitialReady(false);
            FinishInterstitial(completed);
            PrefetchAdsIfOnline();
            return;
        }

        if (adUnitId == rewardedAdUnitId)
        {
            SetRewardedReady(false);

            if (rewarded && completed)
            {
                OnUserEarnedReward?.Invoke(adUnitId);
                Action success = pendingRewardedSuccess;
                ClearRewardedCallbacks();
                success?.Invoke();
            }
            else
            {
                InvokeRewardedFail();
            }

            PrefetchAdsIfOnline();
        }
    }

    private IEnumerator ConnectivityMonitor()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(1f, connectivityPollSeconds));
        while (true)
        {
            HandleConnectivityChanged(forcePrefetch: false);
            yield return wait;
        }
    }

    private void HandleConnectivityChanged(bool forcePrefetch)
    {
        bool online = IsOnline;
        if (online != wasOnline || forcePrefetch)
        {
            if (online && (!wasOnline || forcePrefetch))
            {
                Debug.Log("AdManager: Online — prefetching ads into cache.");
                PrefetchAdsIfOnline();
            }
            else if (!online)
            {
                Debug.Log("AdManager: Offline — suppressing ad loads/shows.");
                HideBanner();
                SetInterstitialReady(false);
                SetRewardedReady(false);
                IsBannerReady = false;
            }

            wasOnline = online;
        }
        else if (online)
        {
            // Keep cache warm while online.
            PrefetchAdsIfOnline();
        }

        NotifyRewardedAvailabilityChanged();
    }

    private void SetInterstitialReady(bool ready)
    {
        interstitialReady = ready;
    }

    private void SetRewardedReady(bool ready)
    {
        if (rewardedReady == ready)
        {
            NotifyRewardedAvailabilityChanged();
            return;
        }

        rewardedReady = ready;
        NotifyRewardedAvailabilityChanged();
    }

    private void NotifyRewardedAvailabilityChanged()
    {
        bool available = CanOfferRewardedAd;
        if (available == lastReportedRewardedAvailability)
            return;

        lastReportedRewardedAvailability = available;
        OnRewardedAvailabilityChanged?.Invoke(available);
    }

    private void FinishInterstitial(bool completed)
    {
        Action callback = pendingInterstitialComplete;
        pendingInterstitialComplete = null;
        try
        {
            callback?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void InvokeRewardedFail()
    {
        Action fail = pendingRewardedFail;
        ClearRewardedCallbacks();
        try
        {
            fail?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void ClearRewardedCallbacks()
    {
        pendingRewardedSuccess = null;
        pendingRewardedFail = null;
    }

    private void BeginAdPresentation()
    {
        IsShowingAd = true;
        previousTimeScale = Time.timeScale;

        MarkFullscreenAdShown();

        if (pauseTimeScaleDuringAds)
            Time.timeScale = 0f;

        if (muteBgmDuringFullscreenAds && AudioManager.Instance != null)
            AudioManager.Instance.SetMutedForFullscreenAd(true);

        if (pauseAudioListenerDuringAds)
            AudioListener.pause = true;
    }

    private void EndAdPresentation()
    {
        IsShowingAd = false;

        if (pauseTimeScaleDuringAds)
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;

        if (muteBgmDuringFullscreenAds && AudioManager.Instance != null)
            AudioManager.Instance.SetMutedForFullscreenAd(false);

        if (pauseAudioListenerDuringAds)
            AudioListener.pause = false;
    }

    private void MarkFullscreenAdShown()
    {
        lastFullscreenAdUnix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        PlayerPrefs.SetString(PrefsLastFullscreenAdUnix, lastFullscreenAdUnix.ToString());
        PlayerPrefs.Save();
    }

    private void LoadFrequencyCapState()
    {
        gameOverCount = PlayerPrefs.GetInt(PrefsGameOverCount, 0);
        string raw = PlayerPrefs.GetString(PrefsLastFullscreenAdUnix, "0");
        if (!long.TryParse(raw, out lastFullscreenAdUnix))
            lastFullscreenAdUnix = 0;
    }

    private void ScheduleRetry(ref Coroutine routine, Action loadAction)
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(RetryLoadAfterDelay(loadAction));
    }

    private IEnumerator RetryLoadAfterDelay(Action loadAction)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, failedLoadRetrySeconds));
        if (IsOnline)
            loadAction?.Invoke();
    }

    private void ApplyPlatformAdUnitDefaults()
    {
#if UNITY_IOS
        if (bannerAdUnitId == "Banner_Android")
            bannerAdUnitId = "Banner_iOS";
        if (rewardedAdUnitId == "Rewarded_Android")
            rewardedAdUnitId = "Rewarded_iOS";
        if (interstitialAdUnitId == "Interstitial_Android")
            interstitialAdUnitId = "Interstitial_iOS";
#endif
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
    private IEnumerator MockShowFullscreen(string adUnitId, bool grantReward)
    {
        BeginAdPresentation();
        yield return new WaitForSecondsRealtime(0.75f);
        HandleAdShowComplete(adUnitId, completed: true, rewarded: grantReward);
    }

    private void ShowMockBanner()
    {
        if (mockBanner == null)
        {
            mockBanner = new GameObject("MockAdBannerCanvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var canvas = mockBanner.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            var bar = new GameObject("Banner", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            bar.transform.SetParent(mockBanner.transform, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 100f);
            rt.anchoredPosition = Vector2.zero;
            bar.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            labelGo.transform.SetParent(bar.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<UnityEngine.UI.Text>();
            text.text = "Ad Banner (Mock)";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.raycastTarget = false;
        }

        mockBanner.SetActive(true);
    }
#endif

#if UNITY_ADS
    private sealed class UnityAdsBridge :
        IUnityAdsInitializationListener,
        IUnityAdsLoadListener,
        IUnityAdsShowListener
    {
        private readonly AdManager owner;

        public UnityAdsBridge(AdManager owner)
        {
            this.owner = owner;
        }

        public void Initialize(string gameId, bool testMode)
        {
            Advertisement.Initialize(gameId, testMode, this);
        }

        public void Load(string adUnitId)
        {
            Advertisement.Load(adUnitId, this);
        }

        public void Show(string adUnitId)
        {
            Advertisement.Show(adUnitId, this);
        }

        public void LoadBanner(string adUnitId, BannerPosition position)
        {
            Advertisement.Banner.SetPosition(position);

            var options = new BannerLoadOptions
            {
                loadCallback = () => owner.HandleAdLoaded(adUnitId),
                errorCallback = message => owner.HandleAdFailedToLoad(adUnitId, message ?? "Banner load error")
            };

            Advertisement.Banner.Load(adUnitId, options);
        }

        public void OnInitializationComplete()
        {
            owner.HandleInitialized();
        }

        public void OnInitializationFailed(UnityAdsInitializationError error, string message)
        {
            owner.HandleInitializationFailed($"{error}: {message}");
        }

        public void OnUnityAdsAdLoaded(string adUnitId)
        {
            owner.HandleAdLoaded(adUnitId);
        }

        public void OnUnityAdsFailedToLoad(string adUnitId, UnityAdsLoadError error, string message)
        {
            owner.HandleAdFailedToLoad(adUnitId, $"{error}: {message}");
        }

        public void OnUnityAdsShowFailure(string adUnitId, UnityAdsShowError error, string message)
        {
            Debug.LogWarning($"AdManager: Show failure {adUnitId} — {error}: {message}");
            owner.HandleAdShowComplete(adUnitId, completed: false, rewarded: false);
        }

        public void OnUnityAdsShowStart(string adUnitId) { }

        public void OnUnityAdsShowClick(string adUnitId) { }

        public void OnUnityAdsShowComplete(string adUnitId, UnityAdsShowCompletionState showCompletionState)
        {
            bool completed = showCompletionState == UnityAdsShowCompletionState.COMPLETED;
            owner.HandleAdShowComplete(adUnitId, completed, rewarded: completed);
        }
    }
#endif
}
