using UnityEngine;

/// <summary>
/// Spawns short colored particle bursts near stat sliders (green = gain, red = loss).
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
    [SerializeField] private Color gainColor = new Color(0.35f, 0.9f, 0.45f, 1f);
    [SerializeField] private Color lossColor = new Color(0.95f, 0.25f, 0.22f, 1f);

    [SerializeField] private UIManager uiManager;
    [SerializeField] private Canvas parentCanvas;

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
        SpawnBurst(anchor, color, Mathf.Min(particleCount, 8 + Mathf.Abs(delta)));
    }

    private RectTransform ResolveAnchor(StatType stat)
    {
        RectTransform explicitAnchor = stat switch
        {
            StatType.Religion => religionAnchor,
            StatType.People => peopleAnchor,
            StatType.Army => armyAnchor,
            StatType.Wealth => wealthAnchor,
            _ => null
        };

        if (explicitAnchor != null)
            return explicitAnchor;

        return uiManager != null ? uiManager.GetStatSliderRect(stat) : null;
    }

    private void SpawnBurst(RectTransform anchor, Color color, int count)
    {
        GameObject host = new GameObject("StatParticleBurst", typeof(RectTransform), typeof(ParticleSystem));
        host.transform.SetParent(anchor, false);

        RectTransform rect = host.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, 12f);
        rect.sizeDelta = Vector2.zero;

        ParticleSystem ps = host.GetComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = lifetime;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = 8f;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = count;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = burstRadius * 0.15f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = 50;
        if (parentCanvas != null)
            renderer.sortingLayerID = parentCanvas.sortingLayerID;

        // UI-friendly material if available; otherwise default particle material.
        if (renderer.sharedMaterial == null)
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit")
                                            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                            ?? Shader.Find("Sprites/Default"));

        ps.Play();
        Destroy(host, lifetime + 0.15f);
    }
}
