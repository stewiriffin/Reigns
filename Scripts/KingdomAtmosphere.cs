using UnityEngine;

/// <summary>
/// Slowly shifts the camera background color based on the kingdom's lowest (most endangered) stat.
/// </summary>
[RequireComponent(typeof(Camera))]
public class KingdomAtmosphere : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KingdomStats kingdomStats;
    [SerializeField] private Camera targetCamera;

    [Header("Colors")]
    [SerializeField] private Color neutralColor = new Color(0.12f, 0.14f, 0.18f);
    [SerializeField] private Color religionLowColor = new Color(0.35f, 0.28f, 0.45f);
    [SerializeField] private Color peopleLowColor = new Color(0.45f, 0.22f, 0.18f);
    [SerializeField] private Color armyLowColor = new Color(0.48f, 0.12f, 0.12f);
    [SerializeField] private Color wealthLowColor = new Color(0.38f, 0.32f, 0.10f);

    [Header("Tuning")]
    [Tooltip("Below this value, the lowest-stat tint begins to appear.")]
    [SerializeField] [Range(1, 50)] private int dangerThreshold = 35;

    [Tooltip("How quickly the background lerps toward the target tint.")]
    [SerializeField] private float transitionSpeed = 1.25f;

    [Tooltip("Max blend strength when a stat is at 0.")]
    [SerializeField] [Range(0.1f, 1f)] private float maxTintStrength = 0.85f;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (targetCamera != null)
            targetCamera.backgroundColor = neutralColor;
    }

    private void LateUpdate()
    {
        if (targetCamera == null || kingdomStats == null)
            return;

        Color target = EvaluateTargetColor();
        targetCamera.backgroundColor = Color.Lerp(
            targetCamera.backgroundColor,
            target,
            1f - Mathf.Exp(-transitionSpeed * Time.deltaTime));
    }

    /// <summary>
    /// Snaps immediately to the neutral / current danger tint (e.g. after Play Again).
    /// </summary>
    public void RefreshImmediate()
    {
        if (targetCamera == null)
            return;

        targetCamera.backgroundColor = EvaluateTargetColor();
    }

    private Color EvaluateTargetColor()
    {
        GetLowestStat(out StatType lowest, out int value);

        if (value >= dangerThreshold)
            return neutralColor;

        float danger = 1f - (value / (float)dangerThreshold);
        float strength = Mathf.Clamp01(danger) * maxTintStrength;
        Color accent = GetAccentColor(lowest);
        return Color.Lerp(neutralColor, accent, strength);
    }

    private void GetLowestStat(out StatType type, out int value)
    {
        type = StatType.Religion;
        value = kingdomStats.Religion;

        if (kingdomStats.People < value)
        {
            type = StatType.People;
            value = kingdomStats.People;
        }

        if (kingdomStats.Army < value)
        {
            type = StatType.Army;
            value = kingdomStats.Army;
        }

        if (kingdomStats.Wealth < value)
        {
            type = StatType.Wealth;
            value = kingdomStats.Wealth;
        }
    }

    private Color GetAccentColor(StatType stat)
    {
        return stat switch
        {
            StatType.Religion => religionLowColor,
            StatType.People => peopleLowColor,
            StatType.Army => armyLowColor,
            StatType.Wealth => wealthLowColor,
            _ => neutralColor
        };
    }
}
