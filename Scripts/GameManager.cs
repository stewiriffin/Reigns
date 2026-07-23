using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main Reigns loop: draws cards, applies swipe choices to kingdom stats,
/// discards the card, then either continues or shows Game Over.
/// Tracks Years Ruled, eras, difficulty scaling, and Longest Reign (PlayerPrefs).
/// </summary>
public class GameManager : MonoBehaviour
{
    private const string LongestReignPrefsKey = "LongestReign";

    [Header("Systems")]
    [SerializeField] private KingdomStats kingdomStats;
    [SerializeField] private CardSwipeHandler cardSwipe;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AdManager adManager;
    [SerializeField] private KingdomAtmosphere atmosphere;
    [SerializeField] private CardVoicePlayer cardVoicePlayer;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private SaveManager saveManager;
    [SerializeField] private AchievementManager achievementManager;
    [SerializeField] private AnalyticsManager analyticsManager;
    [SerializeField] private PlayServicesManager playServicesManager;
    [SerializeField] private NetworkManager networkManager;

    [Header("Deck")]
    [SerializeField] private string cardsResourcePath = "Cards/event_cards";

    [Header("Eras & Difficulty")]
    [Tooltip("Years 0 through this value are Era 1.")]
    [SerializeField] private int era1MaxYear = 10;
    [Tooltip("Years (era1Max+1) through this value are Era 2; above is Era 3.")]
    [SerializeField] private int era2MaxYear = 25;
    [Tooltip("Stat impact grows by this fraction every N years (0.1 = +10%).")]
    [SerializeField] private float difficultyIncreasePerDecade = 0.1f;
    [SerializeField] private int difficultyYearsPerStep = 10;

    [Header("Death Messages")]
    [SerializeField] private string deathMessagesResourcePath = "Deaths/death_messages";

    private readonly List<Card> deck = new List<Card>();
    private readonly List<Card> drawPile = new List<Card>();
    private readonly List<Card> cardCatalog = new List<Card>();
    private readonly StatusEffectTracker statusEffects = new StatusEffectTracker();
    private Dictionary<DeathCause, string> deathMessages;

    private Card currentCard;
    private bool isResolvingChoice;
    private bool skipStatusTickOnce;
    private bool secondChanceUsedThisRun;
    private bool gameOverSequenceRunning;
    private bool hasActiveRun;
    private bool inTutorial;
    private bool miniGameActive;
    private bool legendaryEndingQueued;
    private int tutorialStep;
    private string lastCardId;
    private string forcedNextCardId;
    private DeathCause pendingDeathCause = DeathCause.None;

    /// <summary>Current run score — one year per successfully resolved card.</summary>
    public int YearsRuled { get; private set; }

    /// <summary>Best Years Ruled across runs, loaded from PlayerPrefs.</summary>
    public int LongestReign { get; private set; }

    /// <summary>Current era (1–3) derived from <see cref="YearsRuled"/>.</summary>
    public int CurrentEra => EraProgression.GetEra(YearsRuled, era1MaxYear, era2MaxYear);

    /// <summary>
    /// Multiplier applied to choice stat deltas. Grows +10% every 10 years by default.
    /// </summary>
    public float DifficultyScale =>
        EraProgression.GetDifficultyScale(YearsRuled, difficultyIncreasePerDecade, difficultyYearsPerStep);

    /// <summary>True when an in-progress run should be auto-saved.</summary>
    public bool CanAutoSave =>
        hasActiveRun &&
        !kingdomStats.IsGameOver &&
        !gameOverSequenceRunning &&
        currentCard != null &&
        (DailyChallengeManager.Instance == null || !DailyChallengeManager.Instance.IsDailyRunActive);

    /// <summary>True when inventory relics may be tapped on the action bar.</summary>
    public bool CanUseInventoryItems =>
        hasActiveRun &&
        !inTutorial &&
        !isResolvingChoice &&
        !miniGameActive &&
        !gameOverSequenceRunning &&
        currentCard != null &&
        kingdomStats != null &&
        !kingdomStats.IsGameOver &&
        (cardSwipe == null || !cardSwipe.IsDiscarding);

    /// <summary>True while a card mini-game overlay owns input.</summary>
    public bool IsMiniGameActive => miniGameActive;

    /// <summary>Refresh stat sliders after an inventory item is used mid-turn.</summary>
    public void RefreshHudAfterItemUse()
    {
        RefreshStatHud(immediate: false);
        if (atmosphere != null)
            atmosphere.RefreshImmediate();
    }

    private void Awake()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (cardSwipe == null)
            cardSwipe = FindObjectOfType<CardSwipeHandler>();

        if (uiManager == null)
            uiManager = FindObjectOfType<UIManager>();

        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance != null
                ? InventoryManager.Instance
                : FindObjectOfType<InventoryManager>();

        if (inventoryManager == null)
            inventoryManager = new GameObject("InventoryManager").AddComponent<InventoryManager>();

        if (adManager == null)
            adManager = AdManager.Instance != null ? AdManager.Instance : FindObjectOfType<AdManager>();

        if (adManager == null)
            adManager = new GameObject("AdManager").AddComponent<AdManager>();

        if (atmosphere == null)
            atmosphere = FindObjectOfType<KingdomAtmosphere>();

        if (cardVoicePlayer == null)
            cardVoicePlayer = FindObjectOfType<CardVoicePlayer>();

        if (saveManager == null)
            saveManager = SaveManager.Instance != null ? SaveManager.Instance : FindObjectOfType<SaveManager>();

        if (achievementManager == null)
            achievementManager = AchievementManager.Instance != null
                ? AchievementManager.Instance
                : FindObjectOfType<AchievementManager>();

        if (achievementManager == null)
            achievementManager = new GameObject("AchievementManager").AddComponent<AchievementManager>();

        if (analyticsManager == null)
            analyticsManager = AnalyticsManager.Instance != null
                ? AnalyticsManager.Instance
                : FindObjectOfType<AnalyticsManager>();

        if (analyticsManager == null)
            analyticsManager = new GameObject("AnalyticsManager").AddComponent<AnalyticsManager>();

        if (playServicesManager == null)
            playServicesManager = PlayServicesManager.Instance != null
                ? PlayServicesManager.Instance
                : FindObjectOfType<PlayServicesManager>();

        if (playServicesManager == null)
            playServicesManager = new GameObject("PlayServicesManager").AddComponent<PlayServicesManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Instance != null
                ? NetworkManager.Instance
                : FindObjectOfType<NetworkManager>();

        if (networkManager == null)
            networkManager = new GameObject("NetworkManager").AddComponent<NetworkManager>();

        if (SettingsManager.Instance == null && FindObjectOfType<SettingsManager>() == null)
            new GameObject("SettingsManager").AddComponent<SettingsManager>();

        if (AccessibilityManager.Instance == null && FindObjectOfType<AccessibilityManager>() == null)
            new GameObject("AccessibilityManager").AddComponent<AccessibilityManager>();

        if (DynastyHistoryManager.Instance == null && FindObjectOfType<DynastyHistoryManager>() == null)
            new GameObject("DynastyHistoryManager").AddComponent<DynastyHistoryManager>();

        if (FindObjectOfType<DynastyHallUI>() == null)
            new GameObject("DynastyHallUI").AddComponent<DynastyHallUI>();

        if (FindObjectOfType<SettingsMenu>() == null)
            new GameObject("SettingsMenu").AddComponent<SettingsMenu>();

        if (FindObjectOfType<FloatingStatText>() == null)
            new GameObject("FloatingStatText").AddComponent<FloatingStatText>();

        if (EnvironmentManager.Instance == null && FindObjectOfType<EnvironmentManager>() == null)
            new GameObject("EnvironmentManager").AddComponent<EnvironmentManager>();

        if (DailyChallengeManager.Instance == null && FindObjectOfType<DailyChallengeManager>() == null)
            new GameObject("DailyChallengeManager").AddComponent<DailyChallengeManager>();

        if (FindObjectOfType<DailyChallengeUI>() == null)
            new GameObject("DailyChallengeUI").AddComponent<DailyChallengeUI>();

        if (QuestManager.Instance == null && FindObjectOfType<QuestManager>() == null)
            new GameObject("QuestManager").AddComponent<QuestManager>();

        if (FindObjectOfType<QuestUI>() == null)
            new GameObject("QuestUI").AddComponent<QuestUI>();

        if (FactionRelationshipManager.Instance == null && FindObjectOfType<FactionRelationshipManager>() == null)
            new GameObject("FactionRelationshipManager").AddComponent<FactionRelationshipManager>();

        if (FindObjectOfType<FactionLedgerUI>() == null)
            new GameObject("FactionLedgerUI").AddComponent<FactionLedgerUI>();

        if (SeasonManager.Instance == null && FindObjectOfType<SeasonManager>() == null)
            new GameObject("SeasonManager").AddComponent<SeasonManager>();

        if (DuelController.Instance == null && FindObjectOfType<DuelController>() == null)
            new GameObject("DuelController").AddComponent<DuelController>();

        if (StoryArcManager.Instance == null && FindObjectOfType<StoryArcManager>() == null)
            new GameObject("StoryArcManager").AddComponent<StoryArcManager>();

        if (FindObjectOfType<LegendaryEndingsUI>() == null)
            new GameObject("LegendaryEndingsUI").AddComponent<LegendaryEndingsUI>();

        if (FindObjectOfType<AndroidSystemHandler>() == null)
            new GameObject("AndroidSystemHandler").AddComponent<AndroidSystemHandler>();

        LongestReign = PlayerPrefs.GetInt(LongestReignPrefsKey, 0);
        deathMessages = DeathMessageLoader.Load(deathMessagesResourcePath);

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.BindPlayAgain(PlayAgain);
            uiManager.BindSecondChance(OnSecondChanceClicked);
            uiManager.BindLeaderboard(OnLeaderboardClicked);
#if UNITY_ANDROID && !UNITY_EDITOR
            uiManager.SetLeaderboardButtonVisible(true);
#else
            // Still visible in Editor so you can verify wiring; Play Services call is stubbed.
            uiManager.SetLeaderboardButtonVisible(true);
#endif
        }
    }

    private void OnEnable()
    {
        if (cardSwipe != null)
        {
            cardSwipe.OnSwipeLeft += HandleSwipeLeft;
            cardSwipe.OnSwipeRight += HandleSwipeRight;
            cardSwipe.OnSwipeProgress += HandleSwipeProgress;
        }

        if (kingdomStats != null)
            kingdomStats.OnGameOver += HandleGameOver;

        if (DynamicMusicController.Instance != null && kingdomStats != null)
            DynamicMusicController.Instance.BindKingdomStats(kingdomStats);

        if (adManager != null)
            adManager.OnRewardedAvailabilityChanged += HandleRewardedAvailabilityChanged;

        if (networkManager != null)
            networkManager.OnRemoteCardsReady += HandleRemoteCardsReady;
    }

    private void OnDisable()
    {
        if (cardSwipe != null)
        {
            cardSwipe.OnSwipeLeft -= HandleSwipeLeft;
            cardSwipe.OnSwipeRight -= HandleSwipeRight;
            cardSwipe.OnSwipeProgress -= HandleSwipeProgress;
        }

        if (kingdomStats != null)
            kingdomStats.OnGameOver -= HandleGameOver;

        if (adManager != null)
            adManager.OnRewardedAvailabilityChanged -= HandleRewardedAvailabilityChanged;

        if (networkManager != null)
            networkManager.OnRemoteCardsReady -= HandleRemoteCardsReady;
    }

    private void Start()
    {
        if (TryResumeFromSave())
        {
            if (UIFadeTransition.Instance != null)
                UIFadeTransition.Instance.SnapTo(UIFadeTransition.ScreenId.Gameplay);
            return;
        }

        // Prefer start menu when a fade controller + start screen exist.
        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.SnapTo(UIFadeTransition.ScreenId.StartMenu);
            if (adManager != null)
                adManager.SetBannerAllowed(false);
            return;
        }

        StartNewGame();
    }

    /// <summary>
    /// Hook for the Start Menu Play button.
    /// </summary>
    public void OnStartMenuPlayPressed()
    {
        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.CommitPendingRecord();

        if (DailyChallengeManager.Instance != null)
            DailyChallengeManager.Instance.BeginNormalRun();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();

        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.TransitionTo(
                UIFadeTransition.ScreenId.Gameplay,
                onMidpoint: StartNewGame);
            return;
        }

        StartNewGame();
    }

    /// <summary>
    /// Hook for the Daily Challenge button — seeded run, one attempt per UTC day.
    /// </summary>
    public void OnDailyChallengePressed()
    {
        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.CommitPendingRecord();

        var daily = DailyChallengeManager.Instance;
        if (daily == null)
            daily = new GameObject("DailyChallengeManager").AddComponent<DailyChallengeManager>();

        if (daily.HasPlayedToday())
        {
            Debug.Log($"GameManager: Daily Challenge already used today (score={daily.GetTodayScore()}).");
            var ui = FindObjectOfType<DailyChallengeUI>();
            if (ui != null)
                ui.RefreshLabel();
            return;
        }

        if (!daily.TryBeginDailyRun())
            return;

        if (saveManager != null)
            saveManager.DeleteSave();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();

        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.TransitionTo(
                UIFadeTransition.ScreenId.Gameplay,
                onMidpoint: StartDailyGame);
            return;
        }

        StartDailyGame();
    }

    /// <summary>
    /// Play Again: resets stats to 50, years to 0, and draws a fresh card.
    /// Daily Challenge returns to the Start Menu instead of allowing a second run.
    /// </summary>
    public void PlayAgain()
    {
        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.CommitPendingRecord();

        bool wasDaily = DailyChallengeManager.Instance != null &&
                        DailyChallengeManager.Instance.LastRunWasDaily;

        if (saveManager != null)
            saveManager.DeleteSave();

        if (wasDaily)
        {
            if (DailyChallengeManager.Instance != null)
                DailyChallengeManager.Instance.BeginNormalRun();

            var dailyUi = FindObjectOfType<DailyChallengeUI>();
            if (dailyUi != null)
                dailyUi.RefreshLabel();

            if (UIFadeTransition.Instance != null)
                UIFadeTransition.Instance.TransitionTo(UIFadeTransition.ScreenId.StartMenu);
            else if (uiManager != null)
                uiManager.HideGameOver();

            return;
        }

        if (DailyChallengeManager.Instance != null)
            DailyChallengeManager.Instance.BeginNormalRun();

        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.TransitionTo(
                UIFadeTransition.ScreenId.Gameplay,
                onMidpoint: StartNewGame);
            return;
        }

        StartNewGame();
    }

    /// <summary>Daily Challenge entry — skips tutorial, uses seeded Random from TryBeginDailyRun.</summary>
    public void StartDailyGame()
    {
        StartNewGame(skipTutorial: true, isDaily: true);
    }

    /// <summary>
    /// Resets stats and score, reloads the deck, and shows the first card.
    /// </summary>
    public void StartNewGame()
    {
        StartNewGame(skipTutorial: false, isDaily: false);
    }

    private void StartNewGame(bool skipTutorial, bool isDaily)
    {
        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.CommitPendingRecord();

        if (!isDaily && DailyChallengeManager.Instance != null)
            DailyChallengeManager.Instance.BeginNormalRun();

        StopAllCoroutines();
        isResolvingChoice = false;
        gameOverSequenceRunning = false;
        secondChanceUsedThisRun = false;
        miniGameActive = false;
        legendaryEndingQueued = false;
        lastCardId = null;
        currentCard = null;
        forcedNextCardId = null;
        pendingDeathCause = DeathCause.None;
        YearsRuled = 0;
        skipStatusTickOnce = true;
        hasActiveRun = false;
        inTutorial = false;
        tutorialStep = 0;
        statusEffects.Clear();

        if (SeasonManager.Instance != null)
            SeasonManager.Instance.ResetSeason();

        if (StoryArcManager.Instance != null)
            StoryArcManager.Instance.ResetRunProgress();

        if (FactionRelationshipManager.Instance != null)
            FactionRelationshipManager.Instance.ResetRelationships();

        if (EnvironmentManager.Instance != null)
            EnvironmentManager.Instance.ClearEnvironment();

        if (inventoryManager != null)
            inventoryManager.ClearInventory();

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.ClearStatusEffectIcons();
        }

        if (cardSwipe != null)
            cardSwipe.SetDirectionLock(SwipeDirectionLock.Both);

        kingdomStats.ResetStats();
        RefreshStatHud(immediate: true);
        RefreshScoreHud();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();

        LoadDeck();

        if (adManager != null)
            adManager.SetBannerAllowed(true);

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        if (!skipTutorial && !isDaily && !TutorialCards.HasCompletedTutorial)
        {
            BeginTutorial();
            return;
        }

        if (QuestManager.Instance != null)
            QuestManager.Instance.BeginRun();

        RefillDrawPile();
        ShowNextCard();
        hasActiveRun = currentCard != null;

        // Daily runs are single-attempt — do not persist mid-run saves for resume exploits.
        if (!isDaily && saveManager != null)
            saveManager.SaveGame();
    }

    private void BeginTutorial()
    {
        inTutorial = true;
        tutorialStep = 0;
        skipStatusTickOnce = true;
        ShowTutorialStep(0);
        hasActiveRun = true;

        if (saveManager != null)
            saveManager.SaveGame();
    }

    private void ShowTutorialStep(int step)
    {
        tutorialStep = step;
        Card card = TutorialCards.Create(step);

        if (cardSwipe != null)
        {
            cardSwipe.SetDirectionLock(TutorialCards.GetRequiredLock(step));
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        DisplayCard(card);
        isResolvingChoice = false;

        if (uiManager != null)
            uiManager.ClearSwipeFeedback();
    }

    private void AdvanceTutorialAfterChoice()
    {
        int next = tutorialStep + 1;
        if (next >= 3)
        {
            CompleteTutorialAndStartMainLoop();
            return;
        }

        ShowTutorialStep(next);
    }

    private void CompleteTutorialAndStartMainLoop()
    {
        inTutorial = false;
        tutorialStep = 0;
        TutorialCards.MarkTutorialCompleted();

        if (cardSwipe != null)
            cardSwipe.SetDirectionLock(SwipeDirectionLock.Both);

        YearsRuled = 0;
        RefreshScoreHud();
        skipStatusTickOnce = true;
        forcedNextCardId = null;

        if (QuestManager.Instance != null)
            QuestManager.Instance.BeginRun();

        if (drawPile.Count == 0)
            RefillDrawPile();

        ShowNextCard();
        hasActiveRun = currentCard != null;

        if (saveManager != null)
            saveManager.SaveGame();

        Debug.Log("GameManager: Tutorial complete — entering main card pool.");
    }

    /// <summary>
    /// Builds a JSON-serializable snapshot of the current run.
    /// </summary>
    public GameSaveData CaptureSaveData()
    {
        if (currentCard == null)
            return null;

        string[] itemIds = null;
        if (inventoryManager != null && inventoryManager.HeldItems.Count > 0)
        {
            itemIds = new string[inventoryManager.HeldItems.Count];
            for (int i = 0; i < inventoryManager.HeldItems.Count; i++)
                itemIds[i] = inventoryManager.HeldItems[i]?.id;
        }

        return new GameSaveData
        {
            yearsRuled = YearsRuled,
            religion = kingdomStats.Religion,
            people = kingdomStats.People,
            army = kingdomStats.Army,
            wealth = kingdomStats.Wealth,
            statusEffects = statusEffects.ToSaveArray(),
            inventoryItemIds = itemIds ?? new string[0],
            currentCardId = currentCard.id,
            secondChanceUsedThisRun = secondChanceUsedThisRun,
            factionLoyalties = FactionRelationshipManager.Instance != null
                ? FactionRelationshipManager.Instance.CaptureSave()
                : null,
            storyArcs = StoryArcManager.Instance != null
                ? StoryArcManager.Instance.CaptureSave()
                : null
        };
    }

    private bool TryResumeFromSave()
    {
        if (saveManager == null)
            saveManager = FindObjectOfType<SaveManager>();

        if (saveManager == null)
            return false;

        GameSaveData data = saveManager.LoadGame();
        if (data == null)
            return false;

        if (!ApplySaveData(data))
        {
            saveManager.DeleteSave();
            return false;
        }

        Debug.Log($"GameManager: Resumed run at year {YearsRuled}, card '{data.currentCardId}'.");
        return true;
    }

    /// <summary>
    /// Restores a saved run: stats, year, effects, inventory, and the card that was on screen.
    /// </summary>
    private bool ApplySaveData(GameSaveData data)
    {
        if (data == null)
            return false;

        StopAllCoroutines();
        isResolvingChoice = false;
        gameOverSequenceRunning = false;
        secondChanceUsedThisRun = data.secondChanceUsedThisRun;
        forcedNextCardId = null;
        pendingDeathCause = DeathCause.None;
        skipStatusTickOnce = true;

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.ClearStatusEffectIcons();
        }

        LoadDeck();

        YearsRuled = Mathf.Max(0, data.yearsRuled);
        if (SeasonManager.Instance != null)
            SeasonManager.Instance.SyncFromYearsRuled(YearsRuled);

        RefillDrawPile();

        Card card = CardLoader.FindById(cardCatalog, data.currentCardId);
        if (card == null)
        {
            Debug.LogWarning($"GameManager: Saved card '{data.currentCardId}' missing from catalog.");
            return false;
        }

        // Avoid drawing the restored card immediately again from the random pile.
        RemoveFromDrawPileById(card.id);

        kingdomStats.LoadState(data.religion, data.people, data.army, data.wealth);
        statusEffects.LoadFromSave(data.statusEffects);

        if (FactionRelationshipManager.Instance != null)
            FactionRelationshipManager.Instance.LoadSave(data.factionLoyalties);

        if (StoryArcManager.Instance != null)
            StoryArcManager.Instance.LoadSave(data.storyArcs);

        if (inventoryManager != null)
            inventoryManager.RestoreFromSave(data.inventoryItemIds);

        RefreshStatHud(immediate: true);
        RefreshScoreHud();
        RefreshStatusEffectUi();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();

        if (adManager != null)
            adManager.SetBannerAllowed(true);

        DisplayCard(card);

        if (TryBeginMiniGame(card))
        {
            hasActiveRun = true;
            return true;
        }

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        hasActiveRun = true;
        return true;
    }

    /// <summary>
    /// Loads the base deck (Resources), merges any remote cards from NetworkManager,
    /// then builds catalog + active draw deck.
    /// </summary>
    private void LoadDeck()
    {
        CardDatabase database;
        if (networkManager != null)
            database = networkManager.LoadMergedDatabase(cardsResourcePath);
        else
            database = CardLoader.LoadDatabase(cardsResourcePath);

        CardLoader.ResolveAllAssets(database);

        cardCatalog.Clear();
        cardCatalog.AddRange(CardLoader.FlattenCatalog(database));

        deck.Clear();
        deck.AddRange(CardLoader.BuildActiveDeck(database));

        if (deck.Count == 0)
            Debug.LogError("GameManager: Active deck is empty. Check Resources path and JSON.");
        else
            Debug.Log($"GameManager: Active deck built with {deck.Count} card(s). Catalog={cardCatalog.Count}.");
    }

    /// <summary>
    /// When remote JSON arrives mid-session, refresh the pool without interrupting the current card.
    /// New cards enter on the next draw-pile refill / new game.
    /// </summary>
    private void HandleRemoteCardsReady(bool success)
    {
        if (!success || networkManager == null || !networkManager.HasRemoteCards)
            return;

        string keepCardId = currentCard != null ? currentCard.id : null;
        LoadDeck();

        // Keep the on-screen card reference valid after catalog rebuild.
        if (!string.IsNullOrEmpty(keepCardId))
        {
            Card refreshed = CardLoader.FindById(cardCatalog, keepCardId);
            if (refreshed != null)
                currentCard = refreshed;
        }

        // Inject any brand-new cards into the remaining draw pile.
        if (drawPile != null && deck.Count > 0)
        {
            var inPile = new HashSet<string>();
            for (int i = 0; i < drawPile.Count; i++)
            {
                if (drawPile[i] != null && !string.IsNullOrEmpty(drawPile[i].id))
                    inPile.Add(drawPile[i].id);
            }

            for (int i = 0; i < deck.Count; i++)
            {
                Card card = deck[i];
                if (card == null || string.IsNullOrEmpty(card.id))
                    continue;
                if (!card.IsAvailableInEra(CurrentEra))
                    continue;
                if (SeasonManager.Instance != null && !card.IsAvailableInSeason(SeasonManager.Instance.CurrentSeason))
                    continue;
                if (inPile.Contains(card.id))
                    continue;
                if (keepCardId != null && card.id == keepCardId)
                    continue;
                drawPile.Add(card);
            }
        }

        Debug.Log("GameManager: Remote cards merged into deck pool.");
    }

    private void RefillDrawPile()
    {
        drawPile.Clear();

        int era = CurrentEra;
        Season season = SeasonManager.Instance != null
            ? SeasonManager.Instance.CurrentSeason
            : SeasonManager.SeasonFromYearsRuled(YearsRuled);

        for (int i = 0; i < deck.Count; i++)
        {
            Card card = deck[i];
            if (card == null || !card.IsAvailableInEra(era))
                continue;
            if (!card.IsAvailableInSeason(season))
                continue;
            drawPile.Add(card);
        }

        // Safety: if an era has no tagged cards yet, fall back to the full unlocked deck.
        if (drawPile.Count == 0)
            drawPile.AddRange(deck);

        Shuffle(drawPile);
    }

    /// <summary>
    /// When the era advances, rebuild the remaining draw pile so new-era cards can appear.
    /// </summary>
    private void OnEraAdvanced(int newEra)
    {
        RefillDrawPileExcludingCurrent();
        Debug.Log($"GameManager: Advanced to {EraProgression.GetEraDisplayName(newEra)} (year {YearsRuled}).");
    }

    private void OnSeasonAdvanced(Season newSeason)
    {
        RefillDrawPileExcludingCurrent();
        Debug.Log(
            $"GameManager: Season → {SeasonManager.GetDisplayName(newSeason)} (year {YearsRuled}).");
    }

    private void RefillDrawPileExcludingCurrent()
    {
        string keepId = currentCard != null ? currentCard.id : null;
        RefillDrawPile();
        if (!string.IsNullOrEmpty(keepId))
            RemoveFromDrawPileById(keepId);
    }

    private void ShowNextCard()
    {
        if (kingdomStats.IsGameOver)
            return;

        if (!skipStatusTickOnce)
        {
            if (ProcessStatusEffects())
                return;
        }
        else
        {
            skipStatusTickOnce = false;
        }

        if (deck.Count == 0 && string.IsNullOrWhiteSpace(forcedNextCardId))
            return;

        Card next = DrawNextCard();
        if (next == null)
        {
            Debug.LogError("GameManager: Failed to draw a card.");
            return;
        }

        DisplayCard(next);

        if (TryBeginMiniGame(next))
            return;

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        isResolvingChoice = false;

        if (uiManager != null)
            uiManager.ClearSwipeFeedback();

        hasActiveRun = currentCard != null;
    }

    /// <summary>
    /// Draws a forced follow-up card when queued; otherwise pulls from the random draw pile.
    /// </summary>
    private Card DrawNextCard()
    {
        if (!string.IsNullOrWhiteSpace(forcedNextCardId))
        {
            string id = forcedNextCardId;
            forcedNextCardId = null;

            Card forced = CardLoader.FindById(cardCatalog, id);
            if (forced != null)
            {
                // Keep the random pile coherent if this card was sitting in it.
                RemoveFromDrawPileById(id);
                return forced;
            }

            Debug.LogWarning($"GameManager: NextCardID '{id}' not found in catalog — falling back to random draw.");
        }

        if (drawPile.Count == 0)
            RefillDrawPile();

        if (drawPile.Count == 0)
            return null;

        return DrawCardAvoidingRepeat();
    }

    private void RemoveFromDrawPileById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        for (int i = drawPile.Count - 1; i >= 0; i--)
        {
            if (drawPile[i] != null && drawPile[i].id == id)
                drawPile.RemoveAt(i);
        }
    }

    private bool ProcessStatusEffects()
    {
        bool causedGameOver = statusEffects.Tick(kingdomStats);

        RefreshStatHud(immediate: false);
        RefreshStatusEffectUi();

        if (!causedGameOver && !kingdomStats.IsGameOver)
            return false;

        DeathCause cause = pendingDeathCause != DeathCause.None
            ? pendingDeathCause
            : kingdomStats.LastDeathCause;
        BeginGameOverSequence(cause);
        return true;
    }

    private void RefreshStatusEffectUi()
    {
        if (uiManager != null)
            uiManager.UpdateStatusEffectIcons(statusEffects);
    }

    private static void TryGrantMetaFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return;

        if (MetaProgression.HasFlag(flag))
            return;

        MetaProgression.SetFlag(flag, true);
    }

    private Card DrawCardAvoidingRepeat()
    {
        if (drawPile.Count == 0)
            RefillDrawPile();

        int index = 0;
        if (drawPile.Count > 1 && !string.IsNullOrEmpty(lastCardId))
        {
            for (int i = 0; i < drawPile.Count; i++)
            {
                if (drawPile[i].id != lastCardId)
                {
                    index = i;
                    break;
                }
            }
        }

        Card card = drawPile[index];
        drawPile.RemoveAt(index);
        return card;
    }

    private void DisplayCard(Card card)
    {
        currentCard = card;
        lastCardId = card?.id;

        if (uiManager != null)
            uiManager.UpdateCardUI(card);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayCardDraw();

        if (cardVoicePlayer != null)
            cardVoicePlayer.PlayCardVoice(card);

        if (EnvironmentManager.Instance != null)
            EnvironmentManager.Instance.ApplyCardEnvironment(card);

        if (!inTutorial)
        {
            if (QuestManager.Instance != null)
                QuestManager.Instance.NotifyCardSeen(card);

            if (achievementManager != null)
                achievementManager.NotifyCardSeen(card);
        }
    }

    private void HandleSwipeLeft()
    {
        if (miniGameActive)
            return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySwipeLeft();

        ResolveChoice(isLeft: true);
    }

    private void HandleSwipeRight()
    {
        if (miniGameActive)
            return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySwipeRight();

        ResolveChoice(isLeft: false);
    }

    /// <summary>
    /// Starts a card-driven mini-game when <see cref="Card.miniGame"/> is set.
    /// </summary>
    private bool TryBeginMiniGame(Card card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.miniGame))
            return false;

        string id = card.miniGame.Trim();
        if (!id.Equals(DuelController.MiniGameId, System.StringComparison.OrdinalIgnoreCase) &&
            !id.Equals("Duel", System.StringComparison.OrdinalIgnoreCase))
            return false;

        var duel = DuelController.Instance != null
            ? DuelController.Instance
            : FindObjectOfType<DuelController>();

        if (duel == null)
            duel = new GameObject("DuelController").AddComponent<DuelController>();

        miniGameActive = true;
        isResolvingChoice = true;

        if (cardSwipe != null)
        {
            cardSwipe.SetInputEnabled(false);
            cardSwipe.PrepareForNextCard();
        }

        if (uiManager != null)
            uiManager.ClearSwipeFeedback();

        duel.BeginDuel(card, OnSwordDuelCompleted);
        return true;
    }

    private void OnSwordDuelCompleted(bool won)
    {
        miniGameActive = false;

        if (kingdomStats != null && kingdomStats.IsGameOver)
        {
            isResolvingChoice = false;
            return;
        }

        var duel = DuelController.Instance;
        if (won)
        {
            StatModifiers rewards = duel != null
                ? duel.CreateVictoryRewards()
                : new StatModifiers { army = 15, people = 5, wealth = -5 };

            if (FloatingStatText.Instance != null)
                FloatingStatText.Instance.PlayChoiceFeedback(rewards);

            rewards.Apply(kingdomStats);
            RefreshStatHud(immediate: false);

            string victoryId = duel != null ? duel.VictoryCardId : "duel_victory";
            QueueForcedNextCard(victoryId);
        }
        else
        {
            string deathId = duel != null ? duel.DeathCardId : "duel_death";
            QueueForcedNextCard(deathId);
        }

        if (!inTutorial)
        {
            int eraBefore = CurrentEra;
            Season seasonBefore = SeasonManager.Instance != null
                ? SeasonManager.Instance.CurrentSeason
                : Season.Spring;

            YearsRuled++;
            RefreshScoreHud();

            if (SeasonManager.Instance != null)
            {
                SeasonManager.Instance.SyncFromYearsRuled(YearsRuled);
                if (SeasonManager.Instance.CurrentSeason != seasonBefore)
                    OnSeasonAdvanced(SeasonManager.Instance.CurrentSeason);
            }

            if (CurrentEra != eraBefore)
                OnEraAdvanced(CurrentEra);

            if (achievementManager != null)
            {
                achievementManager.NotifyYearsRuled(YearsRuled);
                achievementManager.NotifyWealthMaxed(kingdomStats.Wealth);
            }

            if (QuestManager.Instance != null)
                QuestManager.Instance.NotifyYearsRuled(YearsRuled);
        }

        isResolvingChoice = false;

        if (cardSwipe != null)
            cardSwipe.PrepareForNextCard();

        ShowNextCard();
    }

    private void ResolveChoice(bool isLeft)
    {
        if (isResolvingChoice || miniGameActive || currentCard == null || kingdomStats.IsGameOver)
            return;

        isResolvingChoice = true;

        if (cardSwipe != null)
            cardSwipe.SetInputEnabled(false);

        if (uiManager != null)
            uiManager.ClearSwipeFeedback();

        StatModifiers modifiers = isLeft
            ? currentCard.leftChoiceModifiers
            : currentCard.rightChoiceModifiers;

        StatusEffect[] grantedEffects = isLeft
            ? currentCard.leftChoiceStatusEffects
            : currentCard.rightChoiceStatusEffects;

        string unlockFlag = isLeft
            ? currentCard.leftChoiceUnlockFlag
            : currentCard.rightChoiceUnlockFlag;

        string nextCardId = isLeft
            ? currentCard.NextCardID_Left
            : currentCard.NextCardID_Right;

        string grantItemId = isLeft
            ? currentCard.leftChoiceGrantItem
            : currentCard.rightChoiceGrantItem;

        string consumeItemId = isLeft
            ? currentCard.leftChoiceConsumeItem
            : currentCard.rightChoiceConsumeItem;

        StatModifiers itemCost = isLeft
            ? currentCard.leftChoiceItemCost
            : currentCard.rightChoiceItemCost;

        FactionDelta[] factionDeltas = isLeft
            ? currentCard.leftChoiceFactionDeltas
            : currentCard.rightChoiceFactionDeltas;

        string storyArcId = isLeft
            ? currentCard.leftChoiceStoryArcId
            : currentCard.rightChoiceStoryArcId;

        int storyArcDelta = isLeft
            ? currentCard.leftChoiceStoryArcDelta
            : currentCard.rightChoiceStoryArcDelta;

        string storyFlag = isLeft
            ? currentCard.leftChoiceStoryFlag
            : currentCard.rightChoiceStoryFlag;

        // Item trades/grants: pay consume + cost first, then grant (capacity permitting).
        ResolveItemTrade(consumeItemId, itemCost, grantItemId);

        if (!inTutorial && analyticsManager != null)
            analyticsManager.LogCardChoice(currentCard.id, isLeft);

        int beforeReligion = kingdomStats.Religion;
        int beforePeople = kingdomStats.People;
        int beforeArmy = kingdomStats.Army;
        int beforeWealth = kingdomStats.Wealth;

        // Scale choice impact with run length (tutorial stays at 1×), then seasonal passives.
        float scale = inTutorial ? 1f : DifficultyScale;
        StatModifiers scaledModifiers = modifiers != null ? modifiers.CreateScaled(scale) : null;
        if (!inTutorial && SeasonManager.Instance != null)
            scaledModifiers = SeasonManager.Instance.ApplySeasonalModifiers(scaledModifiers);

        if (FloatingStatText.Instance != null)
            FloatingStatText.Instance.PlayChoiceFeedback(scaledModifiers);

        if (StatFeedbackParticles.Instance != null)
            StatFeedbackParticles.Instance.PlayChoiceFeedback(scaledModifiers);

        scaledModifiers?.Apply(kingdomStats);
        statusEffects.AddRange(grantedEffects);
        TryGrantMetaFlag(unlockFlag);
        QueueForcedNextCard(nextCardId);

        // After story follow-ups so faction crises insert behind an explicit NextCardID when both fire.
        if (!inTutorial && FactionRelationshipManager.Instance != null)
            FactionRelationshipManager.Instance.ApplyDeltas(factionDeltas);

        if (!inTutorial)
            ApplyStoryArcChoice(storyArcId, storyArcDelta, storyFlag, unlockFlag);

        RefreshStatHud(immediate: false);
        RefreshStatusEffectUi();
        EvaluateDangerShake(beforeReligion, beforePeople, beforeArmy, beforeWealth);

        if (!inTutorial)
        {
            int eraBefore = CurrentEra;
            Season seasonBefore = SeasonManager.Instance != null
                ? SeasonManager.Instance.CurrentSeason
                : Season.Spring;

            YearsRuled++;
            RefreshScoreHud();

            if (SeasonManager.Instance != null)
            {
                SeasonManager.Instance.SyncFromYearsRuled(YearsRuled);
                if (SeasonManager.Instance.CurrentSeason != seasonBefore)
                    OnSeasonAdvanced(SeasonManager.Instance.CurrentSeason);
            }

            if (CurrentEra != eraBefore)
                OnEraAdvanced(CurrentEra);

            if (achievementManager != null)
            {
                achievementManager.NotifyYearsRuled(YearsRuled);
                achievementManager.NotifyWealthMaxed(kingdomStats.Wealth);
            }

            if (QuestManager.Instance != null)
                QuestManager.Instance.NotifyYearsRuled(YearsRuled);
        }

        if (cardSwipe != null)
            cardSwipe.DiscardCard(isLeft, OnDiscardComplete);
        else
            OnDiscardComplete();
    }

    /// <summary>
    /// Handles grant / trade / cost for a resolved choice.
    /// Missing required trade items skip the grant (choice stats still apply).
    /// </summary>
    private void ResolveItemTrade(string consumeItemId, StatModifiers itemCost, string grantItemId)
    {
        if (inventoryManager == null)
            return;

        bool needsConsume = !string.IsNullOrWhiteSpace(consumeItemId);
        bool needsGrant = !string.IsNullOrWhiteSpace(grantItemId);
        bool needsCost = itemCost != null &&
                         (itemCost.religion != 0 || itemCost.people != 0 ||
                          itemCost.army != 0 || itemCost.wealth != 0);

        if (!needsConsume && !needsGrant && !needsCost)
            return;

        if (needsConsume && !inventoryManager.HasItem(consumeItemId))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"GameManager: Trade skipped — missing required item '{consumeItemId}'.");
#endif
            return;
        }

        if (needsConsume)
            inventoryManager.ConsumeItem(consumeItemId);

        if (needsCost)
        {
            if (FloatingStatText.Instance != null)
                FloatingStatText.Instance.PlayChoiceFeedback(itemCost);
            itemCost.Apply(kingdomStats);
        }

        // Grant after consume so a 3/3 inventory can free a slot via trade.
        if (needsGrant)
            inventoryManager.GrantItem(grantItemId);
    }

    private void QueueForcedNextCard(string nextCardId)
    {
        // Empty / whitespace means "no story follow-up" — leave any faction queue intact.
        if (string.IsNullOrEmpty(nextCardId) || IsWhitespaceOnly(nextCardId))
            return;

        if (ContainsWhitespace(nextCardId))
            forcedNextCardId = nextCardId.Trim();
        else
            forcedNextCardId = nextCardId;
    }

    /// <summary>
    /// Queues a faction crisis/offer card: forces next draw when free, otherwise
    /// inserts at the front of the random draw pile so it appears soon.
    /// </summary>
    public void RequestFactionEventCard(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || IsWhitespaceOnly(cardId))
            return;

        string id = ContainsWhitespace(cardId) ? cardId.Trim() : cardId;
        Card card = CardLoader.FindById(cardCatalog, id);
        if (card == null)
        {
            Debug.LogWarning($"GameManager: Faction event card '{id}' not in catalog.");
            return;
        }

        if (string.IsNullOrWhiteSpace(forcedNextCardId))
        {
            forcedNextCardId = id;
            return;
        }

        if (string.Equals(forcedNextCardId, id, System.StringComparison.Ordinal))
            return;

        RemoveFromDrawPileById(id);
        drawPile.Insert(0, card);
    }

    private static bool IsWhitespaceOnly(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
                return false;
        }

        return true;
    }

    private static bool ContainsWhitespace(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
                return true;
        }

        return false;
    }

    private void OnDiscardComplete()
    {
        if (legendaryEndingQueued)
        {
            legendaryEndingQueued = false;
            BeginLegendaryEndingSequence();
            return;
        }

        if (kingdomStats.IsGameOver)
        {
            DeathCause cause = pendingDeathCause != DeathCause.None
                ? pendingDeathCause
                : kingdomStats.LastDeathCause;
            BeginGameOverSequence(cause);
            return;
        }

        if (inTutorial)
        {
            AdvanceTutorialAfterChoice();
            return;
        }

        ShowNextCard();
    }

    private void ApplyStoryArcChoice(string arcId, int delta, string storyFlag, string unlockFlag)
    {
        var arcs = StoryArcManager.Instance;
        if (arcs == null)
            return;

        if (!string.IsNullOrWhiteSpace(storyFlag))
            arcs.SetStoryFlag(storyFlag);

        // Default +1 when an arc id is authored without an explicit delta.
        if (!string.IsNullOrWhiteSpace(arcId) && delta == 0)
            delta = 1;

        if (!string.IsNullOrWhiteSpace(arcId) && delta != 0)
            arcs.ApplyArcDelta(arcId, delta);
        else if (!string.IsNullOrWhiteSpace(unlockFlag))
        {
            // Legacy bridge when the choice has no explicit story-arc fields.
            if (unlockFlag.Trim().Equals("Discovered_Dragon", System.StringComparison.OrdinalIgnoreCase))
                arcs.ApplyArcDelta("dragon_slayer", 1);
            else if (unlockFlag.Trim().Equals("Slayed_Dragon", System.StringComparison.OrdinalIgnoreCase))
                arcs.ApplyArcDelta("dragon_slayer", 2);
        }

        if (arcs.HasPendingLegendaryEnding())
            legendaryEndingQueued = true;
    }

    private void BeginLegendaryEndingSequence()
    {
        if (gameOverSequenceRunning)
            return;

        StartCoroutine(LegendaryEndingSequence());
    }

    private System.Collections.IEnumerator LegendaryEndingSequence()
    {
        gameOverSequenceRunning = true;
        hasActiveRun = false;
        isResolvingChoice = false;
        miniGameActive = false;

        StoryArcDefinition ending = StoryArcManager.Instance != null
            ? StoryArcManager.Instance.ConsumePendingLegendaryEnding()
            : null;

        if (QuestManager.Instance != null)
            QuestManager.Instance.EndRun();

        if (saveManager != null)
            saveManager.DeleteSave();

        TryUpdateHighScore();

        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.StagePendingDeath(YearsRuled, DeathCause.None);

        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
            DailyChallengeManager.Instance.RecordDailyScore(YearsRuled);

        if (adManager != null)
            adManager.SetBannerAllowed(false);

        string title = ending != null && !string.IsNullOrWhiteSpace(ending.endingTitle)
            ? ending.endingTitle
            : "Legendary Ending";
        string body = ending != null && !string.IsNullOrWhiteSpace(ending.endingBody)
            ? ending.endingBody
            : "Your reign becomes legend.";

        string message = $"{title}\n\n{body}";

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayGameOver();

        void ShowPanel()
        {
            if (uiManager != null)
            {
                // Legendary finales are not second-chance deaths.
                uiManager.ShowGameOver(message, YearsRuled, LongestReign, secondChanceAvailable: false);
            }

            var badges = FindObjectOfType<LegendaryEndingsUI>();
            if (badges != null)
                badges.Refresh();
        }

        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.TransitionTo(
                UIFadeTransition.ScreenId.GameOver,
                onMidpoint: ShowPanel);
        }
        else
        {
            ShowPanel();
        }

        gameOverSequenceRunning = false;
        yield break;
    }

    private void HandleGameOver(DeathCause cause)
    {
        pendingDeathCause = cause;

        if (cardSwipe != null)
            cardSwipe.SetInputEnabled(false);

        if (!isResolvingChoice)
            BeginGameOverSequence(cause);
    }

    private void BeginGameOverSequence(DeathCause cause)
    {
        if (gameOverSequenceRunning)
            return;

        StartCoroutine(GameOverSequence(cause));
    }

    /// <summary>
    /// Optional interstitial (30% chance), then Game Over / Play Again UI.
    /// </summary>
    private IEnumerator GameOverSequence(DeathCause cause)
    {
        gameOverSequenceRunning = true;
        hasActiveRun = false;
        isResolvingChoice = false;
        pendingDeathCause = cause;

        if (QuestManager.Instance != null)
            QuestManager.Instance.EndRun();

        if (saveManager != null)
            saveManager.DeleteSave();

        TryUpdateHighScore();

        if (achievementManager != null)
            achievementManager.NotifyDeath(cause);

        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.StagePendingDeath(YearsRuled, cause);

        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
            DailyChallengeManager.Instance.RecordDailyScore(YearsRuled);

        if (analyticsManager != null)
            analyticsManager.LogPlayerDeath(YearsRuled, cause, lastCardId);

        if (playServicesManager != null)
            playServicesManager.SubmitYearsRuled(YearsRuled);

        if (adManager != null)
            adManager.SetBannerAllowed(false);

        bool showInterstitial = adManager != null && adManager.ShouldShowGameOverInterstitial();
        if (showInterstitial)
        {
            bool interstitialDone = false;
            adManager.ShowInterstitial(() => interstitialDone = true);
            while (!interstitialDone)
                yield return null;
        }

        PresentGameOverPanel(cause);
        gameOverSequenceRunning = false;
    }

    private void PresentGameOverPanel(DeathCause cause)
    {
        string deathMessage = DeathMessageLoader.GetMessage(deathMessages, cause);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayGameOver();

        if (ScreenShake.Instance != null)
            ScreenShake.Instance.ShakeGameOver();

        void ShowPanel()
        {
            if (uiManager != null)
            {
                uiManager.ShowGameOver(
                    deathMessage,
                    YearsRuled,
                    LongestReign,
                    secondChanceAvailable: CanOfferSecondChance());
            }
        }

        if (UIFadeTransition.Instance != null)
        {
            UIFadeTransition.Instance.TransitionTo(
                UIFadeTransition.ScreenId.GameOver,
                onMidpoint: ShowPanel);
            return;
        }

        ShowPanel();
    }

    private bool CanOfferSecondChance()
    {
        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
            return false;

        if (secondChanceUsedThisRun)
            return false;

        if (adManager == null)
            return false;

        return adManager.CanOfferRewardedAd;
    }

    private void HandleRewardedAvailabilityChanged(bool available)
    {
        // If the player is sitting on Game Over and connectivity returns with a cached ad, reveal the button.
        if (uiManager == null || secondChanceUsedThisRun)
            return;

        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
            return;

        if (kingdomStats == null || !kingdomStats.IsGameOver)
            return;

        uiManager.SetSecondChanceAvailable(available);
    }

    private void EvaluateDangerShake(int beforeReligion, int beforePeople, int beforeArmy, int beforeWealth)
    {
        if (ScreenShake.Instance == null)
            return;

        const int danger = 15;
        if (DroppedIntoDanger(beforeReligion, kingdomStats.Religion, danger)
            || DroppedIntoDanger(beforePeople, kingdomStats.People, danger)
            || DroppedIntoDanger(beforeArmy, kingdomStats.Army, danger)
            || DroppedIntoDanger(beforeWealth, kingdomStats.Wealth, danger))
        {
            ScreenShake.Instance.ShakeDanger();
        }
    }

    private static bool DroppedIntoDanger(int before, int after, int dangerThreshold)
    {
        if (after >= dangerThreshold)
            return false;

        // Newly entered danger, or got worse while already in danger.
        return before >= dangerThreshold || after < before;
    }

    private void OnLeaderboardClicked()
    {
        if (playServicesManager == null)
            playServicesManager = PlayServicesManager.Instance;

        if (playServicesManager != null)
            playServicesManager.ShowYearsRuledLeaderboard();
        else
            Debug.LogWarning("GameManager: PlayServicesManager missing — cannot open leaderboard.");
    }

    private void OnSecondChanceClicked()
    {
        if (DailyChallengeManager.Instance != null && DailyChallengeManager.Instance.IsDailyRunActive)
        {
            Debug.Log("GameManager: Second Chance disabled in Daily Challenge Mode.");
            if (uiManager != null)
                uiManager.SetSecondChanceAvailable(false);
            return;
        }

        if (secondChanceUsedThisRun || kingdomStats == null || !kingdomStats.IsGameOver)
            return;

        // Disable immediately so the player can't double-tap while the ad loads.
        if (uiManager != null)
            uiManager.SetSecondChanceAvailable(false);

        if (adManager == null)
        {
            Debug.LogWarning("GameManager: No AdManager — Second Chance unavailable.");
            return;
        }

        if (!adManager.IsRewardedReady)
        {
            Debug.LogWarning("GameManager: Rewarded ad not ready — staying on Game Over.");
            // Keep reward disabled for this Game Over (standard Game Over state).
            return;
        }

        adManager.ShowRewarded(
            onRewarded: ApplySecondChance,
            onFailedOrSkipped: () =>
            {
                // Ad failed or closed early — no reward; remain on Game Over with button disabled.
                if (uiManager != null)
                    uiManager.SetSecondChanceAvailable(false);
                Debug.Log("GameManager: Second Chance ad failed/skipped — reward disabled.");
            });
    }

    /// <summary>
    /// After a completed rewarded ad: restore the killing stat to 50 and continue
    /// at the same Years Ruled. Only once per run.
    /// </summary>
    private void ApplySecondChance()
    {
        if (secondChanceUsedThisRun)
            return;

        DeathCause failed = pendingDeathCause != DeathCause.None
            ? pendingDeathCause
            : kingdomStats.LastDeathCause;

        if (!kingdomStats.GrantSecondChance(failed))
        {
            Debug.LogWarning("GameManager: Second Chance failed — no valid death cause.");
            if (uiManager != null)
                uiManager.SetSecondChanceAvailable(false);
            return;
        }

        secondChanceUsedThisRun = true;
        pendingDeathCause = DeathCause.None;
        isResolvingChoice = false;
        gameOverSequenceRunning = false;

        if (DynastyHistoryManager.Instance != null)
            DynastyHistoryManager.Instance.CancelPendingRecord();

        // YearsRuled is intentionally unchanged — resume from the year they died.
        RefreshStatHud(immediate: true);
        RefreshScoreHud();
        RefreshStatusEffectUi();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.SetSecondChanceAvailable(false);
        }

        if (UIFadeTransition.Instance != null)
            UIFadeTransition.Instance.SnapTo(UIFadeTransition.ScreenId.Gameplay);

        if (adManager != null)
            adManager.SetBannerAllowed(true);

        Debug.Log($"GameManager: Second Chance — restored {failed} to 50 at year {YearsRuled}.");

        skipStatusTickOnce = true;
        hasActiveRun = true;

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        ShowNextCard();

        if (saveManager != null)
            saveManager.SaveGame();
    }

    private void TryUpdateHighScore()
    {
        if (YearsRuled <= LongestReign)
            return;

        LongestReign = YearsRuled;
        PlayerPrefs.SetInt(LongestReignPrefsKey, LongestReign);
        PlayerPrefs.Save();

        if (uiManager != null)
            uiManager.UpdateLongestReign(LongestReign);
    }

    private void HandleSwipeProgress(float normalized)
    {
        if (uiManager != null)
            uiManager.UpdateSwipeFeedback(normalized, inTutorial ? 1f : DifficultyScale);
    }

    private void RefreshScoreHud()
    {
        if (uiManager == null)
            return;

        uiManager.UpdateYearsRuled(YearsRuled);
        uiManager.UpdateLongestReign(LongestReign);
        uiManager.UpdateEra(CurrentEra, DifficultyScale);
    }

    private void RefreshStatHud(bool immediate)
    {
        if (kingdomStats == null || uiManager == null)
            return;

        if (immediate)
        {
            uiManager.SetStatSlidersImmediate(
                kingdomStats.Religion,
                kingdomStats.People,
                kingdomStats.Army,
                kingdomStats.Wealth);
        }
        else
        {
            uiManager.UpdateStatSliders(
                kingdomStats.Religion,
                kingdomStats.People,
                kingdomStats.Army,
                kingdomStats.Wealth);
        }
    }

    private static void Shuffle(List<Card> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Card temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Debug-only: jump the run to a specific card by ID.
    /// </summary>
    public bool DebugJumpToCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return false;

        if (cardCatalog.Count == 0)
            LoadDeck();

        Card card = CardLoader.FindById(cardCatalog, cardId.Trim());
        if (card == null)
            return false;

        StopAllCoroutines();
        isResolvingChoice = false;
        gameOverSequenceRunning = false;
        forcedNextCardId = null;
        pendingDeathCause = DeathCause.None;
        skipStatusTickOnce = true;

        if (kingdomStats != null && kingdomStats.IsGameOver)
            kingdomStats.LoadState(kingdomStats.Religion, kingdomStats.People, kingdomStats.Army, kingdomStats.Wealth);

        if (uiManager != null)
            uiManager.HideGameOver();

        RemoveFromDrawPileById(card.id);
        DisplayCard(card);

        if (TryBeginMiniGame(card))
        {
            hasActiveRun = true;
            DebugRefreshHud();
            return true;
        }

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        hasActiveRun = true;
        DebugRefreshHud();
        return true;
    }

    /// <summary>
    /// Debug-only: force a death cause and open the game-over flow.
    /// </summary>
    public void DebugForceDeath(DeathCause cause)
    {
        if (cause == DeathCause.None || kingdomStats == null)
            return;

        isResolvingChoice = false;
        kingdomStats.DebugForceDeath(cause);
        // KingdomStats.OnGameOver → HandleGameOver → BeginGameOverSequence.
    }

    public void DebugRefreshHud()
    {
        RefreshStatHud(immediate: true);
        RefreshScoreHud();
        RefreshStatusEffectUi();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();
    }
#endif
}
