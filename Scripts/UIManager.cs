using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if DOTWEEN
using DG.Tweening;
#endif

/// <summary>
/// Bridges kingdom / card logic to on-screen UI:
/// stat sliders, card copy, swipe choice hints, and stat-change indicators.
/// </summary>
public class UIManager : MonoBehaviour
{
    private const float StatMin = 0f;
    private const float StatMax = 100f;
    private const float DefaultLerpDuration = 0.4f;

    /// <summary>Normalized swipe magnitude at which choice hints / indicators appear.</summary>
    public const float HintRevealNormalized = 0.3f;

    [Header("Stat Sliders")]
    [SerializeField] private Slider religionSlider;
    [SerializeField] private Slider peopleSlider;
    [SerializeField] private Slider armySlider;
    [SerializeField] private Slider wealthSlider;

    [Header("Stat Change Indicators (above each slider)")]
    [Tooltip("Small icons that turn on when the hovered choice will change that stat.")]
    [SerializeField] private GameObject religionChangeIcon;
    [SerializeField] private GameObject peopleChangeIcon;
    [SerializeField] private GameObject armyChangeIcon;
    [SerializeField] private GameObject wealthChangeIcon;

    [Header("Status Effect Icons (near each slider)")]
    [Tooltip("Shown while a lasting buff/debuff is active on that stat.")]
    [SerializeField] private GameObject religionBuffIcon;
    [SerializeField] private GameObject religionDebuffIcon;
    [SerializeField] private GameObject peopleBuffIcon;
    [SerializeField] private GameObject peopleDebuffIcon;
    [SerializeField] private GameObject armyBuffIcon;
    [SerializeField] private GameObject armyDebuffIcon;
    [SerializeField] private GameObject wealthBuffIcon;
    [SerializeField] private GameObject wealthDebuffIcon;

    [Header("Card Text")]
    [SerializeField] private TextMeshProUGUI scenarioText;
    [SerializeField] private TextMeshProUGUI leftChoiceText;
    [SerializeField] private TextMeshProUGUI rightChoiceText;

    [Header("Card Portrait")]
    [SerializeField] private Image characterPortraitImage;
    [SerializeField] private bool hidePortraitWhenMissing = true;

    [Header("Score")]
    [SerializeField] private TextMeshProUGUI yearsRuledText;
    [SerializeField] private TextMeshProUGUI longestReignText;
    [SerializeField] private string yearsRuledFormat = "Year {0}";
    [SerializeField] private string longestReignFormat = "Longest Reign: {0}";

    [Header("Game Over Panel")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI deathMessageText;
    [SerializeField] private TextMeshProUGUI gameOverYearsText;
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button secondChanceButton;
    [SerializeField] private Button leaderboardButton;

    [Header("Choice Hint Fade")]
    [Tooltip("Optional. If unset, the TMP text color alpha is faded instead.")]
    [SerializeField] private CanvasGroup leftChoiceGroup;
    [SerializeField] private CanvasGroup rightChoiceGroup;

    [Header("Animation")]
    [SerializeField] private float sliderLerpDuration = DefaultLerpDuration;

    private Coroutine sliderLerpRoutine;
    private Card currentCard;
    private Color leftChoiceBaseColor = Color.white;
    private Color rightChoiceBaseColor = Color.white;

#if DOTWEEN
    private Tween religionTween;
    private Tween peopleTween;
    private Tween armyTween;
    private Tween wealthTween;
#endif

    // Cached once — avoids GetComponent during particle / HUD lookups on the resolve path.
    private RectTransform religionSliderRect;
    private RectTransform peopleSliderRect;
    private RectTransform armySliderRect;
    private RectTransform wealthSliderRect;

    private int lastYearsRuled = int.MinValue;
    private int lastLongestReign = int.MinValue;

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged()
    {
        if (currentCard != null)
            UpdateCardUI(currentCard);
    }

    private void Awake()
    {
        ConfigureSlider(religionSlider);
        ConfigureSlider(peopleSlider);
        ConfigureSlider(armySlider);
        ConfigureSlider(wealthSlider);

        CacheSliderRects();
        CacheChoiceColors();
        ClearSwipeFeedback();
        ClearStatusEffectIcons();
        HideGameOver();
    }

    private void CacheSliderRects()
    {
        religionSliderRect = religionSlider != null ? religionSlider.transform as RectTransform : null;
        peopleSliderRect = peopleSlider != null ? peopleSlider.transform as RectTransform : null;
        armySliderRect = armySlider != null ? armySlider.transform as RectTransform : null;
        wealthSliderRect = wealthSlider != null ? wealthSlider.transform as RectTransform : null;
    }

    /// <summary>
    /// Wires the Play Again button to restart the run (e.g. GameManager.StartNewGame).
    /// </summary>
    public void BindPlayAgain(UnityEngine.Events.UnityAction onPlayAgain)
    {
        if (playAgainButton == null || onPlayAgain == null)
            return;

        playAgainButton.onClick.RemoveAllListeners();
        playAgainButton.onClick.AddListener(PlayButtonClickSfx);
        playAgainButton.onClick.AddListener(onPlayAgain);
    }

    /// <summary>
    /// Wires the Rewarded Video "Watch Ad to Continue" button.
    /// </summary>
    public void BindSecondChance(UnityEngine.Events.UnityAction onSecondChance)
    {
        EnsureSecondChanceButton();

        if (secondChanceButton == null || onSecondChance == null)
            return;

        TextMeshProUGUI label = secondChanceButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = "Watch Ad to Continue";

        secondChanceButton.onClick.RemoveAllListeners();
        secondChanceButton.onClick.AddListener(PlayButtonClickSfx);
        secondChanceButton.onClick.AddListener(onSecondChance);
    }

    /// <summary>
    /// Enables/disables the Second Chance button. When unavailable, the button is hidden
    /// so the panel shows standard Game Over (Play Again only).
    /// </summary>
    public void SetSecondChanceAvailable(bool available)
    {
        EnsureSecondChanceButton();
        if (secondChanceButton == null)
            return;

        // Hide completely (not just non-interactable) so Game Over layout stays clean offline.
        secondChanceButton.gameObject.SetActive(available);
        secondChanceButton.interactable = available;

        var layout = secondChanceButton.GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout != null)
        {
            layout.ignoreLayout = !available;
            layout.preferredHeight = available ? 84f : 0f;
        }
    }

    /// <summary>
    /// Shows the Game Over panel with Play Again and optional Watch Ad to Continue.
    /// </summary>
    public void ShowGameOver(string deathMessage, int yearsRuled, int longestReign, bool secondChanceAvailable = true)
    {
        if (deathMessageText != null)
            deathMessageText.SetText(deathMessage ?? string.Empty);

        NoAllocText.SetGameOverYears(gameOverYearsText, yearsRuled, longestReign);

        if (playAgainButton != null)
            playAgainButton.gameObject.SetActive(true);

        SetSecondChanceAvailable(secondChanceAvailable);

        if (leaderboardButton != null)
            leaderboardButton.gameObject.SetActive(true);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);
    }

    /// <summary>
    /// Wires the Google Play leaderboard button on the Game Over panel.
    /// </summary>
    public void BindLeaderboard(UnityEngine.Events.UnityAction onShowLeaderboard)
    {
        EnsureLeaderboardButton();

        if (leaderboardButton == null || onShowLeaderboard == null)
            return;

        leaderboardButton.onClick.RemoveAllListeners();
        leaderboardButton.onClick.AddListener(PlayButtonClickSfx);
        leaderboardButton.onClick.AddListener(onShowLeaderboard);
        leaderboardButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// Shows/hides the leaderboard button (e.g. Android-only).
    /// </summary>
    public void SetLeaderboardButtonVisible(bool visible)
    {
        EnsureLeaderboardButton();
        if (leaderboardButton != null)
            leaderboardButton.gameObject.SetActive(visible);
    }

    private static void PlayButtonClickSfx()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    /// <summary>
    /// Hides the Game Over panel.
    /// </summary>
    public void HideGameOver()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    /// <summary>
    /// Returns the RectTransform for a kingdom stat slider (used by feedback FX).
    /// </summary>
    public RectTransform GetStatSliderRect(StatType stat)
    {
        switch (stat)
        {
            case StatType.Religion: return religionSliderRect;
            case StatType.People: return peopleSliderRect;
            case StatType.Army: return armySliderRect;
            case StatType.Wealth: return wealthSliderRect;
            default: return null;
        }
    }

    /// <summary>
    /// Smoothly lerps all four stat sliders to the given values over 0.4 seconds (DOTween when available).
    /// </summary>
    public void UpdateStatSliders(int religion, int people, int army, int wealth)
    {
        float r = ClampStat(religion);
        float p = ClampStat(people);
        float a = ClampStat(army);
        float w = ClampStat(wealth);
        float duration = Mathf.Max(0.01f, sliderLerpDuration);

#if DOTWEEN
        KillSliderTweens();
        religionTween = TweenSlider(religionSlider, r, duration);
        peopleTween = TweenSlider(peopleSlider, p, duration);
        armyTween = TweenSlider(armySlider, a, duration);
        wealthTween = TweenSlider(wealthSlider, w, duration);
#else
        if (sliderLerpRoutine != null)
            StopCoroutine(sliderLerpRoutine);

        sliderLerpRoutine = StartCoroutine(LerpStatSliders(r, p, a, w));
#endif
    }

    /// <summary>
    /// Instantly sets slider values with no animation (e.g. new game start).
    /// </summary>
    public void SetStatSlidersImmediate(int religion, int people, int army, int wealth)
    {
#if DOTWEEN
        KillSliderTweens();
#else
        if (sliderLerpRoutine != null)
        {
            StopCoroutine(sliderLerpRoutine);
            sliderLerpRoutine = null;
        }
#endif

        SetSliderValue(religionSlider, ClampStat(religion));
        SetSliderValue(peopleSlider, ClampStat(people));
        SetSliderValue(armySlider, ClampStat(army));
        SetSliderValue(wealthSlider, ClampStat(wealth));
    }

    /// <summary>
    /// Fills scenario, choices, and character portrait from the drawn card, then hides swipe hints.
    /// </summary>
    public void UpdateCardUI(Card card)
    {
        currentCard = card;

        // Localized strings are table lookups (no per-call Format). SetText copies into TMP buffers.
        if (scenarioText != null)
            scenarioText.SetText(card != null ? card.GetScenarioText() : string.Empty);

        if (leftChoiceText != null)
            leftChoiceText.SetText(card != null ? card.GetLeftChoiceText() : string.Empty);

        if (rightChoiceText != null)
            rightChoiceText.SetText(card != null ? card.GetRightChoiceText() : string.Empty);

        UpdatePortrait(card);
        ClearSwipeFeedback();
    }

    private void UpdatePortrait(Card card)
    {
        if (characterPortraitImage == null)
            return;

        Sprite portrait = card != null ? card.portrait : null;
        characterPortraitImage.sprite = portrait;
        characterPortraitImage.enabled = portrait != null || !hidePortraitWhenMissing;

        if (portrait == null)
            characterPortraitImage.color = hidePortraitWhenMissing
                ? new Color(1f, 1f, 1f, 0f)
                : Color.white;
        else
            characterPortraitImage.color = Color.white;
    }

    /// <summary>
    /// Updates the Years Ruled (score) display.
    /// </summary>
    public void UpdateYearsRuled(int yearsRuled)
    {
        if (yearsRuledText == null || yearsRuled == lastYearsRuled)
            return;

        lastYearsRuled = yearsRuled;
        NoAllocText.SetFormatted(yearsRuledText, yearsRuledFormat, yearsRuled);
    }

    /// <summary>
    /// Updates the Longest Reign (high score) display, if assigned.
    /// </summary>
    public void UpdateLongestReign(int longestReign)
    {
        if (longestReignText == null || longestReign == lastLongestReign)
            return;

        lastLongestReign = longestReign;
        NoAllocText.SetFormatted(longestReignText, longestReignFormat, longestReign);
    }

    /// <summary>
    /// Updates choice-text fade and stat-change icons from the current drag amount.
    /// <paramref name="normalizedSwipe"/> is in [-1, 1] (negative = left).
    /// Hints appear once |normalized| passes 30% of the swipe threshold.
    /// </summary>
    public void UpdateSwipeFeedback(float normalizedSwipe)
    {
        float abs = Mathf.Abs(normalizedSwipe);
        bool pastHintThreshold = abs >= HintRevealNormalized;

        // Fade from 0 at 30% threshold to 1 at full threshold.
        float fade = pastHintThreshold
            ? Mathf.InverseLerp(HintRevealNormalized, 1f, abs)
            : 0f;

        bool draggingLeft = normalizedSwipe < 0f;
        bool draggingRight = normalizedSwipe > 0f;

        SetChoiceHintAlpha(left: true, draggingLeft ? fade : 0f);
        SetChoiceHintAlpha(left: false, draggingRight ? fade : 0f);

        StatModifiers preview = null;
        if (pastHintThreshold && currentCard != null)
        {
            preview = draggingLeft
                ? currentCard.leftChoiceModifiers
                : draggingRight
                    ? currentCard.rightChoiceModifiers
                    : null;
        }

        SetStatChangeIndicators(preview);
    }

    /// <summary>
    /// Hides choice hints and all stat-change indicators.
    /// </summary>
    public void ClearSwipeFeedback()
    {
        SetChoiceHintAlpha(left: true, 0f);
        SetChoiceHintAlpha(left: false, 0f);
        SetStatChangeIndicators(null);
    }

    /// <summary>
    /// Toggles buff/debuff icons near each slider based on active status effects.
    /// </summary>
    public void UpdateStatusEffectIcons(StatusEffectTracker tracker)
    {
        if (tracker == null)
        {
            ClearStatusEffectIcons();
            return;
        }

        UpdateStatStatusIcons(StatType.Religion, tracker, religionBuffIcon, religionDebuffIcon);
        UpdateStatStatusIcons(StatType.People, tracker, peopleBuffIcon, peopleDebuffIcon);
        UpdateStatStatusIcons(StatType.Army, tracker, armyBuffIcon, armyDebuffIcon);
        UpdateStatStatusIcons(StatType.Wealth, tracker, wealthBuffIcon, wealthDebuffIcon);
    }

    public void ClearStatusEffectIcons()
    {
        SetIconActive(religionBuffIcon, false);
        SetIconActive(religionDebuffIcon, false);
        SetIconActive(peopleBuffIcon, false);
        SetIconActive(peopleDebuffIcon, false);
        SetIconActive(armyBuffIcon, false);
        SetIconActive(armyDebuffIcon, false);
        SetIconActive(wealthBuffIcon, false);
        SetIconActive(wealthDebuffIcon, false);
    }

    private static void UpdateStatStatusIcons(
        StatType stat,
        StatusEffectTracker tracker,
        GameObject buffIcon,
        GameObject debuffIcon)
    {
        tracker.GetBuffDebuffFlags(stat, out bool hasBuff, out bool hasDebuff);
        SetIconActive(buffIcon, hasBuff);
        SetIconActive(debuffIcon, hasDebuff);
    }

    private void SetStatChangeIndicators(StatModifiers modifiers)
    {
        SetIconActive(religionChangeIcon, modifiers != null && modifiers.ModifiesReligion);
        SetIconActive(peopleChangeIcon, modifiers != null && modifiers.ModifiesPeople);
        SetIconActive(armyChangeIcon, modifiers != null && modifiers.ModifiesArmy);
        SetIconActive(wealthChangeIcon, modifiers != null && modifiers.ModifiesWealth);
    }

    private void SetChoiceHintAlpha(bool left, float alpha)
    {
        CanvasGroup group = left ? leftChoiceGroup : rightChoiceGroup;
        TextMeshProUGUI label = left ? leftChoiceText : rightChoiceText;
        Color baseColor = left ? leftChoiceBaseColor : rightChoiceBaseColor;

        if (group != null)
        {
            group.alpha = alpha;
            return;
        }

        if (label != null)
        {
            Color c = baseColor;
            c.a = baseColor.a * alpha;
            label.color = c;
        }
    }

    private void CacheChoiceColors()
    {
        if (leftChoiceText != null)
            leftChoiceBaseColor = leftChoiceText.color;
        if (rightChoiceText != null)
            rightChoiceBaseColor = rightChoiceText.color;
    }

    private IEnumerator LerpStatSliders(float religion, float people, float army, float wealth)
    {
        float startReligion = GetSliderValue(religionSlider);
        float startPeople = GetSliderValue(peopleSlider);
        float startArmy = GetSliderValue(armySlider);
        float startWealth = GetSliderValue(wealthSlider);

        float duration = Mathf.Max(0.01f, sliderLerpDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            SetSliderValue(religionSlider, Mathf.Lerp(startReligion, religion, t));
            SetSliderValue(peopleSlider, Mathf.Lerp(startPeople, people, t));
            SetSliderValue(armySlider, Mathf.Lerp(startArmy, army, t));
            SetSliderValue(wealthSlider, Mathf.Lerp(startWealth, wealth, t));

            yield return null;
        }

        SetSliderValue(religionSlider, religion);
        SetSliderValue(peopleSlider, people);
        SetSliderValue(armySlider, army);
        SetSliderValue(wealthSlider, wealth);

        sliderLerpRoutine = null;
    }

#if DOTWEEN
    private static Tween TweenSlider(Slider slider, float target, float duration)
    {
        if (slider == null)
            return null;

        return slider
            .DOValue(target, duration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    private void KillSliderTweens()
    {
        religionTween?.Kill();
        peopleTween?.Kill();
        armyTween?.Kill();
        wealthTween?.Kill();
        religionTween = peopleTween = armyTween = wealthTween = null;

        if (religionSlider != null) religionSlider.DOKill();
        if (peopleSlider != null) peopleSlider.DOKill();
        if (armySlider != null) armySlider.DOKill();
        if (wealthSlider != null) wealthSlider.DOKill();
    }

    private void OnDestroy()
    {
        KillSliderTweens();
    }
#endif

    private static void ConfigureSlider(Slider slider)
    {
        if (slider == null)
            return;

        slider.minValue = StatMin;
        slider.maxValue = StatMax;
        slider.wholeNumbers = true;
        slider.interactable = false;
    }

    private static void SetIconActive(GameObject icon, bool active)
    {
        if (icon != null && icon.activeSelf != active)
            icon.SetActive(active);
    }

    private static float GetSliderValue(Slider slider)
    {
        return slider != null ? slider.value : StatMin;
    }

    private static void SetSliderValue(Slider slider, float value)
    {
        if (slider != null)
            slider.value = value;
    }

    private static float ClampStat(int value)
    {
        return Mathf.Clamp(value, StatMin, StatMax);
    }

    private void EnsureLeaderboardButton()
    {
        if (leaderboardButton != null || gameOverPanel == null)
            return;

        leaderboardButton = CreateGameOverButton(
            "LeaderboardButton",
            "Leaderboard",
            new Vector2(0f, 36f),
            new Color(0.16f, 0.28f, 0.22f, 0.95f));
    }

    private void EnsureSecondChanceButton()
    {
        if (secondChanceButton != null || gameOverPanel == null)
            return;

        // Place above Play Again / Leaderboard so both actions are visible together.
        secondChanceButton = CreateGameOverButton(
            "SecondChanceButton",
            "Watch Ad to Continue",
            new Vector2(0f, 140f),
            new Color(0.28f, 0.22f, 0.12f, 0.95f));
    }

    private Button CreateGameOverButton(string objectName, string labelText, Vector2 anchoredPos, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(gameOverPanel.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(480f, 84f);
        rt.anchoredPosition = anchoredPos;

        go.GetComponent<Image>().color = color;
        var button = go.GetComponent<Button>();

        var layout = go.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.preferredHeight = 84f;
        layout.flexibleWidth = 1f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 30f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        label.raycastTarget = false;

        return button;
    }
}
