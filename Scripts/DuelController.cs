using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sword duel mini-game: best-of-three Parry / Thrust / Dodge (RPS) overlay.
/// Triggered when a card with <c>miniGame: "SwordDuel"</c> is drawn.
/// </summary>
public class DuelController : MonoBehaviour
{
    public const string MiniGameId = "SwordDuel";

    public enum DuelMove
    {
        Parry = 0,
        Thrust = 1,
        Dodge = 2
    }

    public static DuelController Instance { get; private set; }

    [Header("Optional scene wiring")]
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI turnLabel;
    [SerializeField] private TextMeshProUGUI playerMoveLabel;
    [SerializeField] private TextMeshProUGUI enemyMoveLabel;
    [SerializeField] private TextMeshProUGUI resultLabel;
    [SerializeField] private TextMeshProUGUI scoreLabel;
    [SerializeField] private Button parryButton;
    [SerializeField] private Button thrustButton;
    [SerializeField] private Button dodgeButton;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool buildUiIfMissing = true;

    [Header("Flow")]
    [SerializeField] private int turnsToWin = 2;
    [SerializeField] private int maxTurns = 3;
    [SerializeField] private float revealHoldSeconds = 0.85f;
    [SerializeField] private float betweenTurnDelay = 0.35f;

    [Header("Victory rewards (applied immediately on win)")]
    [SerializeField] private int victoryArmy = 15;
    [SerializeField] private int victoryPeople = 5;
    [SerializeField] private int victoryReligion;
    [SerializeField] private int victoryWealth = -5;

    [Header("Outcome cards")]
    [SerializeField] private string victoryCardId = "duel_victory";
    [SerializeField] private string deathCardId = "duel_death";

    private bool uiBuilt;
    private bool duelActive;
    private bool inputLocked;
    private int playerWins;
    private int enemyWins;
    private int turnIndex;
    private Card sourceCard;
    private Action<bool> onComplete;
    private Coroutine flowRoutine;
    private CanvasGroup overlayGroup;

    public bool IsActive => duelActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (buildUiIfMissing)
            EnsureUi();
        WireButtons();
        HideImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Starts a duel overlay for the given story card. Invokes <paramref name="onComplete"/> with win/loss.
    /// </summary>
    public void BeginDuel(Card card, Action<bool> onCompleteCallback)
    {
        if (duelActive)
            return;

        EnsureUi();
        WireButtons();

        sourceCard = card;
        onComplete = onCompleteCallback;
        playerWins = 0;
        enemyWins = 0;
        turnIndex = 0;
        duelActive = true;
        inputLocked = false;

        if (overlayRoot != null)
            overlayRoot.SetActive(true);

        if (overlayGroup != null)
        {
            if (flowRoutine != null)
                StopCoroutine(flowRoutine);
            flowRoutine = StartCoroutine(FadeOverlay(0f, 1f, 0.28f));
        }

        SetButtonsInteractable(true);
        UpdateHud("Choose your stance", "—", "—", string.Empty);
        RefreshScore();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public void AbortDuel()
    {
        if (!duelActive)
            return;

        Finish(false);
    }

    private void WireButtons()
    {
        BindMoveButton(parryButton, DuelMove.Parry);
        BindMoveButton(thrustButton, DuelMove.Thrust);
        BindMoveButton(dodgeButton, DuelMove.Dodge);
    }

    private void BindMoveButton(Button button, DuelMove move)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnPlayerChose(move));
    }

    private void OnPlayerChose(DuelMove playerMove)
    {
        if (!duelActive || inputLocked)
            return;

        if (flowRoutine != null)
            StopCoroutine(flowRoutine);
        flowRoutine = StartCoroutine(ResolveTurn(playerMove));
    }

    private IEnumerator ResolveTurn(DuelMove playerMove)
    {
        inputLocked = true;
        SetButtonsInteractable(false);

        DuelMove enemyMove = RollEnemyMove(playerMove);
        turnIndex++;

        UpdateHud($"Turn {turnIndex} — Ready…", MoveName(playerMove), "?", string.Empty);

        // Short timing beat before the clash reveal.
        float windup = 0.45f;
        float elapsed = 0f;
        while (elapsed < windup)
        {
            elapsed += Time.unscaledDeltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(elapsed * 18f);
            if (resultLabel != null)
            {
                resultLabel.text = pulse > 0.5f ? "…" : "·";
                resultLabel.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            }

            yield return null;
        }

        string clash = EvaluateClash(playerMove, enemyMove, out int playerDelta);
        if (playerDelta > 0)
            playerWins++;
        else if (playerDelta < 0)
            enemyWins++;

        UpdateHud(
            $"Turn {turnIndex}",
            MoveName(playerMove),
            MoveName(enemyMove),
            clash);
        RefreshScore();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();

        yield return new WaitForSecondsRealtime(revealHoldSeconds);

        if (playerWins >= turnsToWin || enemyWins >= turnsToWin || turnIndex >= maxTurns)
        {
            bool won = playerWins > enemyWins;
            string finale = won
                ? "Victory! The challenger falls."
                : playerWins == enemyWins
                    ? "A draw — the challenger presses the advantage!"
                    : "Defeat! Steel finds its mark.";

            // Draws count as loss for a deadly duel (story stakes).
            if (playerWins == enemyWins)
                won = false;

            UpdateHud("Duel Over", MoveName(playerMove), MoveName(enemyMove), finale);
            yield return new WaitForSecondsRealtime(1.1f);
            Finish(won);
            yield break;
        }

        yield return new WaitForSecondsRealtime(betweenTurnDelay);
        UpdateHud($"Turn {turnIndex + 1} — Choose", "—", "—", "Parry beats Thrust · Thrust beats Dodge · Dodge beats Parry");
        SetButtonsInteractable(true);
        inputLocked = false;
        flowRoutine = null;
    }

    private void Finish(bool won)
    {
        duelActive = false;
        inputLocked = true;
        SetButtonsInteractable(false);

        if (flowRoutine != null)
        {
            StopCoroutine(flowRoutine);
            flowRoutine = null;
        }

        Action<bool> callback = onComplete;
        onComplete = null;
        Card card = sourceCard;
        sourceCard = null;

        StartCoroutine(CloseAndReport(won, callback, card));
    }

    private IEnumerator CloseAndReport(bool won, Action<bool> callback, Card card)
    {
        if (overlayGroup != null)
            yield return FadeOverlay(overlayGroup.alpha, 0f, 0.3f);

        HideImmediate();
        callback?.Invoke(won);
        _ = card;
    }

    private DuelMove RollEnemyMove(DuelMove playerMove)
    {
        // Mostly random; slight bias toward countering the player's last-ish habits.
        int roll = UnityEngine.Random.Range(0, 100);
        if (roll < 20)
            return CounterMove(playerMove);
        return (DuelMove)UnityEngine.Random.Range(0, 3);
    }

    private static DuelMove CounterMove(DuelMove playerMove)
    {
        // What beats the player's move.
        return playerMove switch
        {
            DuelMove.Parry => DuelMove.Dodge,
            DuelMove.Thrust => DuelMove.Parry,
            DuelMove.Dodge => DuelMove.Thrust,
            _ => DuelMove.Parry
        };
    }

    /// <returns>Clash text. playerDelta: +1 win, 0 draw, -1 loss.</returns>
    private static string EvaluateClash(DuelMove player, DuelMove enemy, out int playerDelta)
    {
        if (player == enemy)
        {
            playerDelta = 0;
            return "Clash! Blades lock.";
        }

        if (Beats(player, enemy))
        {
            playerDelta = 1;
            return $"{MoveName(player)} bests {MoveName(enemy)}!";
        }

        playerDelta = -1;
        return $"{MoveName(enemy)} bests {MoveName(player)}!";
    }

    public static bool Beats(DuelMove a, DuelMove b)
    {
        return (a == DuelMove.Parry && b == DuelMove.Thrust) ||
               (a == DuelMove.Thrust && b == DuelMove.Dodge) ||
               (a == DuelMove.Dodge && b == DuelMove.Parry);
    }

    public static string MoveName(DuelMove move)
    {
        return move switch
        {
            DuelMove.Parry => "Parry",
            DuelMove.Thrust => "Thrust",
            DuelMove.Dodge => "Dodge",
            _ => "?"
        };
    }

    public string VictoryCardId => victoryCardId;
    public string DeathCardId => deathCardId;

    public StatModifiers CreateVictoryRewards()
    {
        return new StatModifiers
        {
            religion = victoryReligion,
            people = victoryPeople,
            army = victoryArmy,
            wealth = victoryWealth
        };
    }

    private void UpdateHud(string turn, string playerMove, string enemyMove, string result)
    {
        if (titleLabel != null)
            titleLabel.text = sourceCard != null && !string.IsNullOrWhiteSpace(sourceCard.GetScenarioText())
                ? "Sword Duel"
                : "Sword Duel";

        if (turnLabel != null)
            turnLabel.text = turn;
        if (playerMoveLabel != null)
            playerMoveLabel.text = $"You: {playerMove}";
        if (enemyMoveLabel != null)
            enemyMoveLabel.text = $"Foe: {enemyMove}";
        if (resultLabel != null)
        {
            resultLabel.text = result;
            resultLabel.color = new Color(0.92f, 0.88f, 0.75f, 1f);
        }
    }

    private void RefreshScore()
    {
        if (scoreLabel != null)
            scoreLabel.text = $"You {playerWins}  —  {enemyWins} Foe   (first to {turnsToWin})";
    }

    private void SetButtonsInteractable(bool enabled)
    {
        if (parryButton != null)
            parryButton.interactable = enabled;
        if (thrustButton != null)
            thrustButton.interactable = enabled;
        if (dodgeButton != null)
            dodgeButton.interactable = enabled;
    }

    private void HideImmediate()
    {
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        if (overlayGroup != null)
            overlayGroup.alpha = 0f;
    }

    private IEnumerator FadeOverlay(float from, float to, float duration)
    {
        if (overlayGroup == null)
            yield break;

        overlayGroup.blocksRaycasts = to > 0.5f;
        overlayGroup.interactable = to > 0.5f;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            overlayGroup.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        overlayGroup.alpha = to;
        overlayGroup.blocksRaycasts = to > 0.01f;
        overlayGroup.interactable = to > 0.01f;
    }

    private void EnsureUi()
    {
        if (uiBuilt && overlayRoot != null)
            return;

        if (overlayRoot != null && parryButton != null)
        {
            overlayGroup = overlayRoot.GetComponent<CanvasGroup>();
            if (overlayGroup == null)
                overlayGroup = overlayRoot.AddComponent<CanvasGroup>();
            uiBuilt = true;
            return;
        }

        if (!buildUiIfMissing)
            return;

        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject(
                "DuelCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 120;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.transform as RectTransform;

        var root = CreatePanel(
            "DuelOverlay",
            canvasRt,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            new Color(0.02f, 0.03f, 0.05f, 0.88f));
        Stretch(root.GetComponent<RectTransform>());
        overlayRoot = root;
        overlayGroup = root.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        if (root.GetComponent<AccessibleBackground>() == null)
            root.AddComponent<AccessibleBackground>();

        var window = CreatePanel(
            "Window",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(920f, 1180f),
            new Color(0.1f, 0.09f, 0.1f, 0.98f));

        titleLabel = CreateLabel(
            window.transform, "Sword Duel", 44f, FontStyles.Bold,
            new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.98f));

        turnLabel = CreateLabel(
            window.transform, "Turn 1 — Choose", 28f, FontStyles.Normal,
            new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.88f));
        turnLabel.color = new Color(0.8f, 0.76f, 0.65f, 1f);

        scoreLabel = CreateLabel(
            window.transform, "You 0  —  0 Foe", 26f, FontStyles.Bold,
            new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.8f));

        playerMoveLabel = CreateLabel(
            window.transform, "You: —", 30f, FontStyles.Bold,
            new Vector2(0.08f, 0.58f), new Vector2(0.48f, 0.7f));
        playerMoveLabel.alignment = TextAlignmentOptions.Center;

        enemyMoveLabel = CreateLabel(
            window.transform, "Foe: —", 30f, FontStyles.Bold,
            new Vector2(0.52f, 0.58f), new Vector2(0.92f, 0.7f));
        enemyMoveLabel.alignment = TextAlignmentOptions.Center;

        resultLabel = CreateLabel(
            window.transform,
            "Parry beats Thrust · Thrust beats Dodge · Dodge beats Parry",
            24f,
            FontStyles.Italic,
            new Vector2(0.08f, 0.42f),
            new Vector2(0.92f, 0.56f));
        resultLabel.color = new Color(0.85f, 0.8f, 0.7f, 1f);

        parryButton = CreateMoveButton(window.transform, "Parry", new Vector2(0.08f, 0.12f), new Vector2(0.32f, 0.36f),
            new Color(0.25f, 0.35f, 0.55f, 1f));
        thrustButton = CreateMoveButton(window.transform, "Thrust", new Vector2(0.38f, 0.12f), new Vector2(0.62f, 0.36f),
            new Color(0.55f, 0.28f, 0.25f, 1f));
        dodgeButton = CreateMoveButton(window.transform, "Dodge", new Vector2(0.68f, 0.12f), new Vector2(0.92f, 0.36f),
            new Color(0.3f, 0.5f, 0.35f, 1f));

        uiBuilt = true;
    }

    private static Button CreateMoveButton(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        var go = CreatePanel($"Btn_{label}", parent, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, color);
        var button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        var text = CreateLabel(go.transform, label, 30f, FontStyles.Bold, Vector2.zero, Vector2.one);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static GameObject CreatePanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent,
        string text,
        float fontSize,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.92f, 0.9f, 0.84f, 1f);
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
