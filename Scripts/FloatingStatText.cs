using TMPro;
using UnityEngine;

/// <summary>
/// Pooled floating "+15" / "-20" popups above kingdom stat sliders when a choice resolves.
/// Animates upward and fades out over ~0.6s (DOTween when available, coroutine otherwise).
/// Font size scales with the magnitude of the change.
/// </summary>
public class FloatingStatText : MonoBehaviour
{
    public static FloatingStatText Instance { get; private set; }

    [Header("Anchors (optional — falls back to UIManager sliders)")]
    [SerializeField] private RectTransform religionAnchor;
    [SerializeField] private RectTransform peopleAnchor;
    [SerializeField] private RectTransform armyAnchor;
    [SerializeField] private RectTransform wealthAnchor;

    [Header("Look")]
    [SerializeField] private Color gainColor = new Color(0.35f, 0.9f, 0.45f, 1f);
    [SerializeField] private Color lossColor = new Color(0.95f, 0.28f, 0.24f, 1f);
    [SerializeField] private float minFontSize = 28f;
    [SerializeField] private float maxFontSize = 52f;
    [Tooltip("Abs delta at which font size reaches max.")]
    [SerializeField] private int maxMagnitudeForScale = 25;
    [SerializeField] private float duration = 0.6f;
    [SerializeField] private float riseDistance = 72f;
    [SerializeField] private Vector2 anchorOffset = new Vector2(0f, 18f);
    [SerializeField] private int poolSize = 12;

    [Header("References")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private Canvas parentCanvas;

    private ObjectPool pool;
    private RectTransform canvasRect;
    private Transform poolRoot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (uiManager == null)
            uiManager = FindObjectOfType<UIManager>();

        if (parentCanvas == null)
            parentCanvas = GetComponentInParent<Canvas>();

        if (parentCanvas == null)
            parentCanvas = FindObjectOfType<Canvas>();

        if (parentCanvas != null)
            canvasRect = parentCanvas.transform as RectTransform;

        BuildPool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Spawns a floating number above each stat changed by the choice.
    /// </summary>
    public void PlayChoiceFeedback(StatModifiers modifiers)
    {
        if (modifiers == null)
            return;

        Spawn(StatType.Religion, modifiers.religion);
        Spawn(StatType.People, modifiers.people);
        Spawn(StatType.Army, modifiers.army);
        Spawn(StatType.Wealth, modifiers.wealth);
    }

    public void Spawn(StatType stat, int delta)
    {
        if (delta == 0)
            return;

        RectTransform anchor = ResolveAnchor(stat);
        if (anchor == null || canvasRect == null)
            return;

        if (pool == null)
            BuildPool();

        GameObject go = pool.Get();
        FloatingStatPopup popup = EnsurePopup(go);
        // Late-created extras (pool grew) still need Initialize.
        popup.Initialize(pool);

        Vector2 start = WorldToCanvasAnchored(anchor, anchorOffset);
        float fontSize = FontSizeForMagnitude(Mathf.Abs(delta));
        Color color = delta > 0 ? gainColor : lossColor;

        // Colorblind: keep signed numbers (magnitude is readable without hue alone).
        if (AccessibilityManager.Instance != null && AccessibilityManager.Instance.ColorblindMode)
            color = Color.white;

        popup.Play(canvasRect, start, delta, color, fontSize, duration, riseDistance);
    }

    private float FontSizeForMagnitude(int absDelta)
    {
        int cap = Mathf.Max(1, maxMagnitudeForScale);
        float t = Mathf.Clamp01(absDelta / (float)cap);
        // Ease so mid-size hits still feel punchy.
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(minFontSize, maxFontSize, t);
    }

    private void BuildPool()
    {
        if (pool != null)
            return;

        poolRoot = new GameObject("FloatingStatTextPool").transform;
        poolRoot.SetParent(transform, false);

        GameObject template = CreateTemplate();
        template.SetActive(false);

        pool = new ObjectPool(template, poolRoot, Mathf.Max(4, poolSize));

        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = pool.Get();
            EnsurePopup(go).Initialize(pool);
            pool.Release(go);
        }

        Destroy(template);
    }

    private static FloatingStatPopup EnsurePopup(GameObject go)
    {
        var popup = go.GetComponent<FloatingStatPopup>();
        return popup != null ? popup : go.AddComponent<FloatingStatPopup>();
    }

    private GameObject CreateTemplate()
    {
        var go = new GameObject(
            "FloatingStatPopup",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(TextMeshProUGUI),
            typeof(FloatingStatPopup));
        go.transform.SetParent(poolRoot, false);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.outlineWidth = 0.22f;
        label.outlineColor = new Color(0f, 0f, 0f, 0.85f);

        return go;
    }

    private RectTransform ResolveAnchor(StatType stat)
    {
        switch (stat)
        {
            case StatType.Religion:
                if (religionAnchor != null) return religionAnchor;
                break;
            case StatType.People:
                if (peopleAnchor != null) return peopleAnchor;
                break;
            case StatType.Army:
                if (armyAnchor != null) return armyAnchor;
                break;
            case StatType.Wealth:
                if (wealthAnchor != null) return wealthAnchor;
                break;
        }

        return uiManager != null ? uiManager.GetStatSliderRect(stat) : null;
    }

    private Vector2 WorldToCanvasAnchored(RectTransform anchor, Vector2 localOffset)
    {
        Vector3 world = anchor.TransformPoint(new Vector3(localOffset.x, localOffset.y, 0f));
        Camera cam = null;
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = parentCanvas.worldCamera != null ? parentCanvas.worldCamera : Camera.main;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local);
        return local;
    }
}
