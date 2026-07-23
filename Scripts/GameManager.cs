using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Main Reigns loop: draws cards, applies swipe choices to kingdom stats,
/// discards the card, then either continues or shows Game Over.
/// Tracks Years Ruled (score) and Longest Reign (PlayerPrefs high score).
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

    [Header("Deck")]
    [SerializeField] private string cardsResourcePath = "Cards/event_cards";

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
    private bool secondChanceUsedThisDeath;
    private bool gameOverSequenceRunning;
    private bool hasActiveRun;
    private string lastCardId;
    private string forcedNextCardId;
    private DeathCause pendingDeathCause = DeathCause.None;

    /// <summary>Current run score — one year per successfully resolved card.</summary>
    public int YearsRuled { get; private set; }

    /// <summary>Best Years Ruled across runs, loaded from PlayerPrefs.</summary>
    public int LongestReign { get; private set; }

    /// <summary>True when an in-progress run should be auto-saved.</summary>
    public bool CanAutoSave =>
        hasActiveRun &&
        !kingdomStats.IsGameOver &&
        !gameOverSequenceRunning &&
        currentCard != null;

    private void Awake()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (cardSwipe == null)
            cardSwipe = FindObjectOfType<CardSwipeHandler>();

        if (uiManager == null)
            uiManager = FindObjectOfType<UIManager>();

        if (adManager == null)
            adManager = AdManager.Instance != null ? AdManager.Instance : FindObjectOfType<AdManager>();

        if (atmosphere == null)
            atmosphere = FindObjectOfType<KingdomAtmosphere>();

        if (cardVoicePlayer == null)
            cardVoicePlayer = FindObjectOfType<CardVoicePlayer>();

        if (inventoryManager == null)
            inventoryManager = FindObjectOfType<InventoryManager>();

        if (saveManager == null)
            saveManager = SaveManager.Instance != null ? SaveManager.Instance : FindObjectOfType<SaveManager>();

        LongestReign = PlayerPrefs.GetInt(LongestReignPrefsKey, 0);
        deathMessages = DeathMessageLoader.Load(deathMessagesResourcePath);

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.BindPlayAgain(PlayAgain);
            uiManager.BindSecondChance(OnSecondChanceClicked);
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
            return;
        }

        StartNewGame();
    }

    /// <summary>
    /// Hook for the Start Menu Play button.
    /// </summary>
    public void OnStartMenuPlayPressed()
    {
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
    /// Play Again: resets stats to 50, years to 0, and draws a fresh card.
    /// </summary>
    public void PlayAgain()
    {
        if (saveManager != null)
            saveManager.DeleteSave();

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
    /// Resets stats and score, reloads the deck, and shows the first card.
    /// </summary>
    public void StartNewGame()
    {
        StopAllCoroutines();
        isResolvingChoice = false;
        gameOverSequenceRunning = false;
        secondChanceUsedThisDeath = false;
        lastCardId = null;
        currentCard = null;
        forcedNextCardId = null;
        pendingDeathCause = DeathCause.None;
        YearsRuled = 0;
        skipStatusTickOnce = true;
        hasActiveRun = false;
        statusEffects.Clear();

        if (inventoryManager != null)
            inventoryManager.ClearInventory();

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.ClearStatusEffectIcons();
        }

        kingdomStats.ResetStats();
        RefreshStatHud(immediate: true);
        RefreshScoreHud();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();

        LoadDeck();
        RefillDrawPile();

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        ShowNextCard();
        hasActiveRun = currentCard != null;

        if (saveManager != null)
            saveManager.SaveGame();
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
            currentCardId = currentCard.id
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
        secondChanceUsedThisDeath = false;
        forcedNextCardId = null;
        pendingDeathCause = DeathCause.None;
        skipStatusTickOnce = true;

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.ClearStatusEffectIcons();
        }

        LoadDeck();
        RefillDrawPile();

        Card card = CardLoader.FindById(cardCatalog, data.currentCardId);
        if (card == null)
        {
            Debug.LogWarning($"GameManager: Saved card '{data.currentCardId}' missing from catalog.");
            return false;
        }

        // Avoid drawing the restored card immediately again from the random pile.
        RemoveFromDrawPileById(card.id);

        YearsRuled = Mathf.Max(0, data.yearsRuled);
        kingdomStats.LoadState(data.religion, data.people, data.army, data.wealth);
        statusEffects.LoadFromSave(data.statusEffects);

        if (inventoryManager != null)
            inventoryManager.RestoreFromSave(data.inventoryItemIds);

        RefreshStatHud(immediate: true);
        RefreshScoreHud();
        RefreshStatusEffectUi();

        if (atmosphere != null)
            atmosphere.RefreshImmediate();

        DisplayCard(card);

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        hasActiveRun = true;
        return true;
    }

    /// <summary>
    /// Loads the base deck, then injects unlockable-pool cards whose prerequisite flags are set.
    /// Also builds a full catalog so chained NextCardID lookups can resolve any card by ID.
    /// </summary>
    private void LoadDeck()
    {
        CardDatabase database = CardLoader.LoadDatabase(cardsResourcePath);
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

    private void RefillDrawPile()
    {
        drawPile.Clear();
        drawPile.AddRange(deck);
        Shuffle(drawPile);
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
    }

    private void HandleSwipeLeft()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySwipeLeft();

        ResolveChoice(isLeft: true);
    }

    private void HandleSwipeRight()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySwipeRight();

        ResolveChoice(isLeft: false);
    }

    private void ResolveChoice(bool isLeft)
    {
        if (isResolvingChoice || currentCard == null || kingdomStats.IsGameOver)
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

        // Grant before applying mods so a newly received charm can save this same choice.
        if (inventoryManager != null)
            inventoryManager.GrantItem(grantItemId);

        int beforeReligion = kingdomStats.Religion;
        int beforePeople = kingdomStats.People;
        int beforeArmy = kingdomStats.Army;
        int beforeWealth = kingdomStats.Wealth;

        if (StatFeedbackParticles.Instance != null)
            StatFeedbackParticles.Instance.PlayChoiceFeedback(modifiers);

        modifiers?.Apply(kingdomStats);
        statusEffects.AddRange(grantedEffects);
        TryGrantMetaFlag(unlockFlag);
        QueueForcedNextCard(nextCardId);
        RefreshStatHud(immediate: false);
        RefreshStatusEffectUi();
        EvaluateDangerShake(beforeReligion, beforePeople, beforeArmy, beforeWealth);

        YearsRuled++;
        RefreshScoreHud();

        if (cardSwipe != null)
            cardSwipe.DiscardCard(isLeft, OnDiscardComplete);
        else
            OnDiscardComplete();
    }

    private void QueueForcedNextCard(string nextCardId)
    {
        forcedNextCardId = string.IsNullOrWhiteSpace(nextCardId) ? null : nextCardId.Trim();
    }

    private void OnDiscardComplete()
    {
        if (kingdomStats.IsGameOver)
        {
            DeathCause cause = pendingDeathCause != DeathCause.None
                ? pendingDeathCause
                : kingdomStats.LastDeathCause;
            BeginGameOverSequence(cause);
            return;
        }

        ShowNextCard();
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
        secondChanceUsedThisDeath = false;
        pendingDeathCause = cause;

        if (saveManager != null)
            saveManager.DeleteSave();

        TryUpdateHighScore();

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
                    secondChanceAvailable: !secondChanceUsedThisDeath);
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

    private void OnSecondChanceClicked()
    {
        if (secondChanceUsedThisDeath || kingdomStats == null || !kingdomStats.IsGameOver)
            return;

        if (uiManager != null)
            uiManager.SetSecondChanceAvailable(false);

        if (adManager == null)
        {
            Debug.LogWarning("GameManager: No AdManager — granting Second Chance without ad (dev fallback).");
            ApplySecondChance();
            return;
        }

        adManager.ShowRewarded(
            onRewarded: ApplySecondChance,
            onFailedOrSkipped: () =>
            {
                if (uiManager != null)
                    uiManager.SetSecondChanceAvailable(!secondChanceUsedThisDeath);
            });
    }

    /// <summary>
    /// Restores the failing stat to 50 and continues the run at the same Years Ruled.
    /// </summary>
    private void ApplySecondChance()
    {
        if (secondChanceUsedThisDeath)
            return;

        DeathCause failed = pendingDeathCause != DeathCause.None
            ? pendingDeathCause
            : kingdomStats.LastDeathCause;

        if (!kingdomStats.GrantSecondChance())
        {
            Debug.LogWarning("GameManager: Second Chance failed — no valid death cause.");
            return;
        }

        secondChanceUsedThisDeath = true;
        pendingDeathCause = DeathCause.None;
        isResolvingChoice = false;

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

        Debug.Log($"GameManager: Second Chance — restored {failed} to 50 at year {YearsRuled}.");

        skipStatusTickOnce = true;
        hasActiveRun = true;
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
            uiManager.UpdateSwipeFeedback(normalized);
    }

    private void RefreshScoreHud()
    {
        if (uiManager == null)
            return;

        uiManager.UpdateYearsRuled(YearsRuled);
        uiManager.UpdateLongestReign(LongestReign);
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
