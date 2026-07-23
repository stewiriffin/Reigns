using System;
using UnityEngine;

/// <summary>
/// Calendar seasons for a reign. One season advances per year (card resolve);
/// a full Spring→Winter cycle completes every 4 years.
/// </summary>
public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3
}

/// <summary>
/// Tracks the current season from years ruled, applies passive seasonal stat
/// multipliers, and gates season-locked event cards.
/// </summary>
public class SeasonManager : MonoBehaviour
{
    public const float WinterPeoplePenaltyMultiplier = 1.5f;
    public const float AutumnWealthBonusMultiplier = 1.2f;

    public static SeasonManager Instance { get; private set; }

    [Header("HUD")]
    [SerializeField] private bool buildHudIfMissing = true;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private UnityEngine.UI.Image seasonIcon;
    [SerializeField] private TMPro.TextMeshProUGUI seasonLabel;

    public Season CurrentSeason { get; private set; } = Season.Spring;

    /// <summary>Fired whenever the displayed season changes.</summary>
    public event Action<Season> OnSeasonChanged;

    private Season lastBroadcast = (Season)(-1);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (buildHudIfMissing)
            EnsureHud();

        SyncFromYearsRuled(0);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Maps years ruled → season. Year 0 Spring, 1 Summer, 2 Autumn, 3 Winter, 4 Spring…
    /// </summary>
    public static Season SeasonFromYearsRuled(int yearsRuled)
    {
        yearsRuled = Mathf.Max(0, yearsRuled);
        return (Season)(yearsRuled % 4);
    }

    public void SyncFromYearsRuled(int yearsRuled)
    {
        Season next = SeasonFromYearsRuled(yearsRuled);
        bool changed = next != CurrentSeason;
        CurrentSeason = next;
        RefreshHud();

        if (changed || lastBroadcast != CurrentSeason)
        {
            lastBroadcast = CurrentSeason;
            OnSeasonChanged?.Invoke(CurrentSeason);
        }
    }

    public void ResetSeason()
    {
        SyncFromYearsRuled(0);
    }

    public static string GetDisplayName(Season season)
    {
        return season switch
        {
            Season.Spring => "Spring",
            Season.Summer => "Summer",
            Season.Autumn => "Autumn",
            Season.Winter => "Winter",
            _ => season.ToString()
        };
    }

    public static string GetIconGlyph(Season season)
    {
        return season switch
        {
            Season.Spring => "❀",
            Season.Summer => "☀",
            Season.Autumn => "🍂",
            Season.Winter => "❄",
            _ => "·"
        };
    }

    public static Color GetSeasonColor(Season season)
    {
        return season switch
        {
            Season.Spring => new Color(0.45f, 0.75f, 0.4f, 1f),
            Season.Summer => new Color(0.95f, 0.78f, 0.25f, 1f),
            Season.Autumn => new Color(0.85f, 0.45f, 0.2f, 1f),
            Season.Winter => new Color(0.55f, 0.72f, 0.9f, 1f),
            _ => Color.white
        };
    }

    /// <summary>
    /// Parses card JSON <c>season</c> / <c>requiredSeason</c> values.
    /// Empty / "Any" / "All" / "0" → unrestricted.
    /// </summary>
    public static bool TryParseSeasonRequirement(string raw, out Season season)
    {
        season = Season.Spring;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string key = raw.Trim();
        if (key.Equals("Any", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            key == "0")
            return false;

        if (Enum.TryParse(key, ignoreCase: true, out season))
            return true;

        Debug.LogWarning($"SeasonManager: Unknown season requirement '{raw}'.");
        return false;
    }

    public bool IsCardAvailable(Card card)
    {
        if (card == null)
            return false;

        if (!TryParseSeasonRequirement(card.requiredSeason, out Season required))
            return true;

        return required == CurrentSeason;
    }

    /// <summary>
    /// Returns a new modifier set with Winter people-penalty and Autumn wealth-bonus applied.
    /// </summary>
    public StatModifiers ApplySeasonalModifiers(StatModifiers source)
    {
        return ApplySeasonalModifiers(source, CurrentSeason);
    }

    public static StatModifiers ApplySeasonalModifiers(StatModifiers source, Season season)
    {
        if (source == null)
            return null;

        var result = new StatModifiers
        {
            religion = source.religion,
            people = source.people,
            army = source.army,
            wealth = source.wealth
        };

        if (season == Season.Winter && result.people < 0)
        {
            result.people = ScaleNegative(result.people, WinterPeoplePenaltyMultiplier);
        }

        if (season == Season.Autumn && result.wealth > 0)
        {
            result.wealth = ScalePositive(result.wealth, AutumnWealthBonusMultiplier);
        }

        return result;
    }

    private static int ScaleNegative(int value, float multiplier)
    {
        if (value >= 0)
            return value;

        int scaled = Mathf.RoundToInt(value * multiplier);
        return scaled >= 0 ? -1 : scaled;
    }

    private static int ScalePositive(int value, float multiplier)
    {
        if (value <= 0)
            return value;

        int scaled = Mathf.RoundToInt(value * multiplier);
        return scaled <= 0 ? 1 : scaled;
    }

    private void RefreshHud()
    {
        if (seasonIcon != null)
            seasonIcon.color = GetSeasonColor(CurrentSeason);

        if (seasonLabel != null)
        {
            seasonLabel.text = $"{GetIconGlyph(CurrentSeason)}  {GetDisplayName(CurrentSeason)}";
            seasonLabel.color = GetSeasonColor(CurrentSeason);
        }
    }

    private void EnsureHud()
    {
        if (seasonLabel != null && seasonIcon != null)
            return;

        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject(
                "SeasonHudCanvas",
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 35;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.transform as RectTransform;

        var root = new GameObject("SeasonHud", typeof(RectTransform));
        root.transform.SetParent(canvasRt, false);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(1f, 1f);
        rootRt.anchorMax = new Vector2(1f, 1f);
        rootRt.pivot = new Vector2(1f, 1f);
        rootRt.anchoredPosition = new Vector2(-28f, -120f);
        rootRt.sizeDelta = new Vector2(220f, 64f);

        var iconGo = new GameObject("SeasonIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        iconGo.transform.SetParent(root.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(52f, 52f);
        seasonIcon = iconGo.GetComponent<UnityEngine.UI.Image>();
        seasonIcon.color = GetSeasonColor(CurrentSeason);
        seasonIcon.raycastTarget = false;

        var labelGo = new GameObject("SeasonLabel", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        labelGo.transform.SetParent(root.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.offsetMin = new Vector2(60f, 0f);
        labelRt.offsetMax = Vector2.zero;
        seasonLabel = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
        seasonLabel.fontSize = 26f;
        seasonLabel.fontStyle = TMPro.FontStyles.Bold;
        seasonLabel.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        seasonLabel.raycastTarget = false;
        seasonLabel.text = $"{GetIconGlyph(CurrentSeason)}  {GetDisplayName(CurrentSeason)}";
        seasonLabel.color = GetSeasonColor(CurrentSeason);
    }
}
