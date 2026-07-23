using UnityEngine;

/// <summary>
/// Pooled colored particle bursts near stat sliders (green = gain, red = loss).
/// Prewarms systems once — no Instantiate/Destroy during the swipe resolve path.
/// </summary>
public class StatFeedbackParticles : MonoBehaviour
{
    public static StatFeedbackParticles Instance { get; private set; }

    [Header("Anchors (optional — falls back to UIManager sliders)")]
    [SerializeField] private RectTransform religionAnchor;
    [SerializeField] private RectTransform peopleAnchor;
    [SerializeField] private RectTransform armyAnchor;
    [SerializeField] private RectTransform wealthAnchor;

    [Header("Burst")]
    [SerializeField] private int particleCount = 14;
    [SerializeField] private float burstRadius = 36f;
    [SerializeField] private float lifetime = 0.45f;
    [SerializeField] private float speed = 80f;
    [SerializeField] private int poolSize = 8;
    [SerializeField] private Color gainColor = new Color(0.35f, 0.9f, 0.45f, 1f);
    [SerializeField] private Color lossColor = new Color(0.95f, 0.25f, 0.22f, 1f);

    [SerializeField] private UIManager uiManager;
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private Material particleMaterial;

    private ObjectPool burstPool;
    private Transform poolRoot;
    private static readonly Vector2 BurstAnchoredOffset = new Vector2(0f, 12f);

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

        BuildPool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Plays feedback for each non-zero modifier near its slider.
    /// </summary>
    public void PlayChoiceFeedback(StatModifiers modifiers)
    {
        if (modifiers == null)
            return;

        Burst(StatType.Religion, modifiers.religion);
        Burst(StatType.People, modifiers.people);
        Burst(StatType.Army, modifiers.army);
        Burst(StatType.Wealth, modifiers.wealth);
    }

    public void Burst(StatType stat, int delta)
    {
        if (delta == 0)
            return;

        RectTransform anchor = ResolveAnchor(stat);
        if (anchor == null)
            return;

        Color color = delta > 0 ? gainColor : lossColor;
        int count = Mathf.Min(particleCount, 8 + Mathf.Abs(delta));
        SpawnBurst(anchor, color, count);
    }

    private void BuildPool()
    {
        if (burstPool != null)
            return;

        poolRoot = new GameObject("StatParticlePool").transform;
        poolRoot.SetParent(transform, false);

        if (particleMaterial == null)
        {
            Shader shader = Shader.Find("Particles/Standard Unlit")
                            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Sprites/Default");
            if (shader != null)
                particleMaterial = new Material(shader);
        }

        GameObject template = CreateBurstTemplate();
        template.SetActive(false);

        int sortingOrder = 50;
        int sortingLayerId = parentCanvas != null ? parentCanvas.sortingLayerID : 0;

        burstPool = new ObjectPool(template, poolRoot, Mathf.Max(2, poolSize));

        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = burstPool.Get();
            EnsureBurstInitialized(go, sortingOrder, sortingLayerId);
            burstPool.Release(go);
        }

        // Template was only used for Instantiate; remove it from the hierarchy.
        Destroy(template);
    }

    private GameObject CreateBurstTemplate()
    {
        var go = new GameObject(
            "StatParticleBurst",
            typeof(RectTransform),
            typeof(ParticleSystem),
            typeof(PooledParticleBurst));
        go.transform.SetParent(poolRoot, false);
        return go;
    }

    private void EnsureBurstInitialized(GameObject go, int sortingOrder, int sortingLayerId)
    {
        var burst = go.GetComponent<PooledParticleBurst>();
        if (burst == null)
            burst = go.AddComponent<PooledParticleBurst>();
        burst.Initialize(burstPool, particleMaterial, sortingOrder, sortingLayerId);
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

    private void SpawnBurst(RectTransform anchor, Color color, int count)
    {
        if (burstPool == null)
            BuildPool();

        int sortingOrder = 50;
        int sortingLayerId = parentCanvas != null ? parentCanvas.sortingLayerID : 0;

        GameObject host = burstPool.Get();
        EnsureBurstInitialized(host, sortingOrder, sortingLayerId);

        var burst = host.GetComponent<PooledParticleBurst>();
        burst.Play(
            anchor,
            BurstAnchoredOffset,
            color,
            count,
            lifetime,
            speed,
            burstRadius * 0.15f);
    }
}
