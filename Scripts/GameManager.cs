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

    [Header("Deck")]
    [SerializeField] private string cardsResourcePath = "Cards/event_cards";

    [Header("Death Messages")]
    [SerializeField] private string deathMessagesResourcePath = "Deaths/death_messages";

    private readonly List<Card> deck = new List<Card>();
    private readonly List<Card> drawPile = new List<Card>();
    private readonly StatusEffectTracker statusEffects = new StatusEffectTracker();
    private Dictionary<DeathCause, string> deathMessages;

    private Card currentCard;
    private bool isResolvingChoice;
    private bool skipStatusTickOnce;
    private bool secondChanceUsedThisDeath;
    private bool gameOverSequenceRunning;
    private string lastCardId;
    private DeathCause pendingDeathCause = DeathCause.None;

    /// <summary>Current run score — one year per successfully resolved card.</summary>
    public int YearsRuled { get; private set; }

    /// <summary>Best Years Ruled across runs, loaded from PlayerPrefs.</summary>
    public int LongestReign { get; private set; }

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
        StartNewGame();
    }

    /// <summary>
    /// Play Again: resets stats to 50, years to 0, and draws a fresh card.
    /// </summary>
    public void PlayAgain()
    {
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
        pendingDeathCause = DeathCause.None;
        YearsRuled = 0;
        skipStatusTickOnce = true;
        statusEffects.Clear();

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.ClearStatusEffectIcons();
        }

        kingdomStats.ResetStats();
        RefreshStatHud(immediate: true);
        RefreshScoreHud();

        LoadDeck();
        RefillDrawPile();

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        ShowNextCard();
    }

    /// <summary>
    /// Loads the base deck, then injects unlockable-pool cards whose prerequisite flags are set.
    /// </summary>
    private void LoadDeck()
    {
        deck.Clear();
        deck.AddRange(CardLoader.LoadActiveDeck(cardsResourcePath));

        if (deck.Count == 0)
            Debug.LogError("GameManager: Active deck is empty. Check Resources path and JSON.");
        else
            Debug.Log($"GameManager: Active deck built with {deck.Count} card(s).");
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

        if (deck.Count == 0)
            return;

        if (drawPile.Count == 0)
            RefillDrawPile();

        Card next = DrawCardAvoidingRepeat();
        DisplayCard(next);

        if (cardSwipe != null)
        {
            cardSwipe.PrepareForNextCard();
            cardSwipe.SetInputEnabled(true);
        }

        isResolvingChoice = false;

        if (uiManager != null)
            uiManager.ClearSwipeFeedback();
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
    }

    private void HandleSwipeLeft()
    {
        ResolveChoice(isLeft: true);
    }

    private void HandleSwipeRight()
    {
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

        modifiers?.Apply(kingdomStats);
        statusEffects.AddRange(grantedEffects);
        TryGrantMetaFlag(unlockFlag);
        RefreshStatHud(immediate: false);
        RefreshStatusEffectUi();

        YearsRuled++;
        RefreshScoreHud();

        if (cardSwipe != null)
            cardSwipe.DiscardCard(isLeft, OnDiscardComplete);
        else
            OnDiscardComplete();
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
        isResolvingChoice = false;
        secondChanceUsedThisDeath = false;
        pendingDeathCause = cause;

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

        if (uiManager != null)
        {
            uiManager.ShowGameOver(
                deathMessage,
                YearsRuled,
                LongestReign,
                secondChanceAvailable: !secondChanceUsedThisDeath);
        }
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

        if (uiManager != null)
        {
            uiManager.HideGameOver();
            uiManager.SetSecondChanceAvailable(false);
        }

        Debug.Log($"GameManager: Second Chance — restored {failed} to 50 at year {YearsRuled}.");

        skipStatusTickOnce = true;
        ShowNextCard();
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
}
