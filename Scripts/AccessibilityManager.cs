using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Applies accessibility visuals: text size scaling, high-contrast outlines/backgrounds,
/// and exposes colorblind mode for shape-based stat feedback.
/// </summary>
public class AccessibilityManager : MonoBehaviour
{
    public static AccessibilityManager Instance { get; private set; }

    public const float TextScaleSmall = 0.85f;
    public const float TextScaleMedium = 1f;
    public const float TextScaleLarge = 1.25f;

    public const float HighContrastOutlineWidth = 0.28f;
    public static readonly Color HighContrastOutlineColor = new Color(0f, 0f, 0f, 1f);
    public static readonly Color HighContrastTextColor = new Color(1f, 1f, 1f, 1f);

    [SerializeField] private float refreshIntervalSeconds = 0.5f;

    private readonly Dictionary<int, float> baseFontSizes = new Dictionary<int, float>();
    private readonly Dictionary<int, float> baseOutlineWidths = new Dictionary<int, float>();
    private readonly Dictionary<int, Color> baseOutlineColors = new Dictionary<int, Color>();
    private readonly Dictionary<int, Color> baseTextColors = new Dictionary<int, Color>();
    private readonly HashSet<AccessibleBackground> backgrounds = new HashSet<AccessibleBackground>();

    private float lastRefreshTime = -999f;
    private TextSizeOption appliedTextSize = TextSizeOption.Medium;
    private bool appliedHighContrast;

    public TextSizeOption TextSize =>
        SettingsManager.Instance != null ? SettingsManager.Instance.TextSize : TextSizeOption.Medium;

    public bool HighContrastMode =>
        SettingsManager.Instance != null && SettingsManager.Instance.HighContrastMode;

    public bool ColorblindMode =>
        SettingsManager.Instance != null && SettingsManager.Instance.ColorblindMode;

    public float TextScale => GetScale(TextSize);

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

    private void OnEnable()
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.OnSettingsChanged += HandleSettingsChanged;
    }

    private void Start()
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OnSettingsChanged -= HandleSettingsChanged;
            SettingsManager.Instance.OnSettingsChanged += HandleSettingsChanged;
        }

        ApplyAll();
    }

    private void OnDisable()
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.OnSettingsChanged -= HandleSettingsChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void HandleSettingsChanged()
    {
        ApplyAll();
    }

    public void RegisterBackground(AccessibleBackground background)
    {
        if (background == null)
            return;

        backgrounds.Add(background);
        background.ApplyHighContrast(HighContrastMode);
    }

    public void UnregisterBackground(AccessibleBackground background)
    {
        if (background != null)
            backgrounds.Remove(background);
    }

    /// <summary>
    /// Re-scans TMP labels (useful after auto-built UI appears) and reapplies settings.
    /// </summary>
    public void Refresh()
    {
        ApplyAll(forceRescan: true);
    }

    public void ApplyAll(bool forceRescan = false)
    {
        if (!forceRescan && Time.unscaledTime - lastRefreshTime < refreshIntervalSeconds * 0.25f)
        {
            // Still apply when toggles change even if recently refreshed.
        }

        lastRefreshTime = Time.unscaledTime;
        AutoRegisterBackgrounds();
        ApplyTextSize(TextSize, forceRescan);
        ApplyHighContrast(HighContrastMode, forceRescan);
        appliedTextSize = TextSize;
        appliedHighContrast = HighContrastMode;
    }

    public static float GetScale(TextSizeOption size)
    {
        return size switch
        {
            TextSizeOption.Small => TextScaleSmall,
            TextSizeOption.Large => TextScaleLarge,
            _ => TextScaleMedium
        };
    }

    public static string GetTextSizeLabel(TextSizeOption size)
    {
        return size switch
        {
            TextSizeOption.Small => "Small",
            TextSizeOption.Large => "Large",
            _ => "Medium"
        };
    }

    private void ApplyTextSize(TextSizeOption size, bool forceRescan)
    {
        float scale = GetScale(size);
        float previousScale = GetScale(appliedTextSize);
        if (previousScale < 0.01f)
            previousScale = 1f;

        if (forceRescan)
        {
            // Keep known bases; only discover newly created labels below.
        }

        TextMeshProUGUI[] labels = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();

        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI tmp = labels[i];
            if (tmp == null || !IsSceneObject(tmp.gameObject))
                continue;

            int id = tmp.GetInstanceID();
            if (!baseFontSizes.ContainsKey(id))
                baseFontSizes[id] = tmp.fontSize / previousScale;

            tmp.fontSize = baseFontSizes[id] * scale;
        }
    }

    private void ApplyHighContrast(bool enabled, bool _)
    {
        TextMeshProUGUI[] labels = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI tmp = labels[i];
            if (tmp == null || !IsSceneObject(tmp.gameObject))
                continue;

            int id = tmp.GetInstanceID();
            if (!baseOutlineWidths.ContainsKey(id))
            {
                baseOutlineWidths[id] = tmp.outlineWidth;
                baseOutlineColors[id] = tmp.outlineColor;
                baseTextColors[id] = tmp.color;
            }

            if (enabled)
            {
                tmp.outlineWidth = Mathf.Max(baseOutlineWidths[id], HighContrastOutlineWidth);
                tmp.outlineColor = HighContrastOutlineColor;

                Color c = baseTextColors[id];
                if (c.a > 0.15f)
                {
                    float luminance = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    if (luminance < 0.85f)
                        tmp.color = new Color(HighContrastTextColor.r, HighContrastTextColor.g, HighContrastTextColor.b, c.a);
                }
            }
            else
            {
                tmp.outlineWidth = baseOutlineWidths[id];
                tmp.outlineColor = baseOutlineColors[id];
                tmp.color = baseTextColors[id];
            }
        }

        foreach (AccessibleBackground bg in backgrounds)
        {
            if (bg != null)
                bg.ApplyHighContrast(enabled);
        }
    }

    private void AutoRegisterBackgrounds()
    {
        AccessibleBackground[] found = Resources.FindObjectsOfTypeAll<AccessibleBackground>();
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && IsSceneObject(found[i].gameObject))
                backgrounds.Add(found[i]);
        }
    }

    private static bool IsSceneObject(GameObject go)
    {
        if (go == null)
            return false;
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(go.scene.name))
            return false;
#endif
        return go.scene.IsValid();
    }
}

/// <summary>Player-facing text size presets for accessibility.</summary>
public enum TextSizeOption
{
    Small = 0,
    Medium = 1,
    Large = 2
}
