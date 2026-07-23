using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Pooled colored particle bursts near stat sliders (green = gain, red = loss).
/// In Colorblind Mode, spawns explicit + / − shape labels instead of color-only flashes.
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

    [Header("Colorblind shapes")]
    [SerializeField] private float signLifetime = 0.7f;
    [SerializeField] private float signRise = 48f;
    [SerializeField] private int signPoolSize = 8;

    [SerializeField] private UIManager uiManager;
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private Material particleMaterial;

    private ObjectPool burstPool;
    private Transform poolRoot;
    private readonly Queue<TextMeshProUGUI> signPool = new Queue<TextMeshProUGUI>();
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
        BuildSignPool();
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

        bool colorblind = AccessibilityManager.Instance != null && AccessibilityManager.Instance.ColorblindMode;
        if (colorblind)
        {
            // FloatingStatText already shows signed magnitudes; skip color-only particle bursts.
            return;
        }

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

        Destroy(template);
    }

    private void BuildSignPool()
    {
        if (poolRoot == null)
        {
            poolRoot = new GameObject("StatParticlePool").transform;
            poolRoot.SetParent(transform, false);
        }

        for (int i = 0; i < signPoolSize; i++)
        {
            var go = new GameObject("StatSign", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(CanvasGroup));
            go.transform.SetParent(poolRoot, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 48f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.outlineWidth = 0.3f;
            tmp.outlineColor = Color.black;
            go.SetActive(false);
            signPool.Enqueue(tmp);
        }
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

    private void SpawnSign(RectTransform anchor, bool positive)
    {
        if (signPool.Count == 0)
            BuildSignPool();

        TextMeshProUGUI label = signPool.Count > 0 ? signPool.Dequeue() : null;
        if (label == null)
            return;

        Transform parent = parentCanvas != null ? parentCanvas.transform : anchor;
        label.transform.SetParent(parent, false);
        label.gameObject.SetActive(true);
        label.text = positive ? "+" : "−";
        label.color = Color.white;

        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80f, 80f);

        Vector3 world = anchor.TransformPoint(new Vector3(BurstAnchoredOffset.x, BurstAnchoredOffset.y, 0f));
        if (parent is RectTransform parentRt)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRt,
                RectTransformUtility.WorldToScreenPoint(null, world),
                null,
                out Vector2 local);
            rt.anchoredPosition = local;
        }
        else
        {
            rt.position = world;
        }

        var group = label.GetComponent<CanvasGroup>();
        if (group == null)
            group = label.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;

        StartCoroutine(AnimateSign(label, group, rt.anchoredPosition));
    }

    private IEnumerator AnimateSign(TextMeshProUGUI label, CanvasGroup group, Vector2 start)
    {
        float duration = Mathf.Max(0.1f, signLifetime);
        float elapsed = 0f;
        var rt = label.rectTransform;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.anchoredPosition = start + new Vector2(0f, signRise * t);
            group.alpha = 1f - t;
            yield return null;
        }

        label.gameObject.SetActive(false);
        label.transform.SetParent(poolRoot, false);
        signPool.Enqueue(label);
    }
}
