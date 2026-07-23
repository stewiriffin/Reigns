using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient environmental particles behind the card UI (snow, rain, embers, dust).
/// Layers are object-pooled, mobile-tuned, and crossfaded over 1 second when a card is drawn.
/// </summary>
public class EnvironmentManager : MonoBehaviour
{
    public static EnvironmentManager Instance { get; private set; }

    [Header("Transition")]
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private int sortingOrder = -20;
    [SerializeField] private int poolSizePerWeather = 1;

    [Header("Mobile limits")]
    [SerializeField] private int maxParticlesSnow = 48;
    [SerializeField] private int maxParticlesRain = 56;
    [SerializeField] private int maxParticlesEmbers = 36;
    [SerializeField] private int maxParticlesDust = 28;

    [Header("Optional")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Material particleMaterial;

    private readonly Dictionary<EnvironmentWeather, ObjectPool> pools =
        new Dictionary<EnvironmentWeather, ObjectPool>();

    private readonly List<AmbientWeatherLayer> activeLayers = new List<AmbientWeatherLayer>(4);
    private Transform poolRoot;
    private Coroutine fadeRoutine;
    private EnvironmentWeather currentWeather = EnvironmentWeather.None;

    public EnvironmentWeather CurrentWeather => currentWeather;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (targetCamera == null)
            targetCamera = Camera.main;

        EnsureHierarchy();
        BuildPools();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Reads <see cref="Card.weather"/> and fades to the matching ambient layer.</summary>
    public void ApplyCardEnvironment(Card card)
    {
        EnvironmentWeather weather = ParseWeather(card != null ? card.weather : null);
        TransitionTo(weather);
    }

    public void TransitionTo(EnvironmentWeather weather)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeToWeather(weather));
    }

    public void ClearEnvironment()
    {
        TransitionTo(EnvironmentWeather.None);
    }

    public static EnvironmentWeather ParseWeather(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return EnvironmentWeather.None;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "snow":
            case "winter":
                return EnvironmentWeather.Snow;
            case "rain":
            case "storm":
            case "drizzle":
                return EnvironmentWeather.Rain;
            case "embers":
            case "fire":
            case "ash":
                return EnvironmentWeather.Embers;
            case "dust":
            case "sand":
            case "motes":
                return EnvironmentWeather.Dust;
            case "none":
            case "clear":
            case "calm":
                return EnvironmentWeather.None;
            default:
                Debug.LogWarning($"EnvironmentManager: Unknown weather '{raw}' — using None.");
                return EnvironmentWeather.None;
        }
    }

    private IEnumerator FadeToWeather(EnvironmentWeather next)
    {
        currentWeather = next;

        AmbientWeatherLayer incoming = null;
        if (next != EnvironmentWeather.None)
            incoming = AcquireLayer(next);

        // Snapshot outgoing intensities.
        var outgoing = new List<AmbientWeatherLayer>(activeLayers.Count);
        for (int i = 0; i < activeLayers.Count; i++)
        {
            AmbientWeatherLayer layer = activeLayers[i];
            if (layer == null)
                continue;
            if (incoming != null && layer == incoming)
                continue;
            outgoing.Add(layer);
        }

        if (incoming != null && !activeLayers.Contains(incoming))
            activeLayers.Add(incoming);

        float[] startOut = new float[outgoing.Count];
        for (int i = 0; i < outgoing.Count; i++)
            startOut[i] = outgoing[i].Intensity;

        float startIn = incoming != null ? incoming.Intensity : 0f;
        float duration = Mathf.Max(0.01f, fadeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);

            for (int i = 0; i < outgoing.Count; i++)
            {
                if (outgoing[i] != null)
                    outgoing[i].SetIntensity(Mathf.Lerp(startOut[i], 0f, t));
            }

            if (incoming != null)
                incoming.SetIntensity(Mathf.Lerp(startIn, 1f, t));

            yield return null;
        }

        for (int i = 0; i < outgoing.Count; i++)
        {
            AmbientWeatherLayer layer = outgoing[i];
            if (layer == null)
                continue;
            layer.SetIntensity(0f);
            activeLayers.Remove(layer);
            layer.ReturnToPool();
        }

        if (incoming != null)
            incoming.SetIntensity(1f);

        fadeRoutine = null;
    }

    private AmbientWeatherLayer AcquireLayer(EnvironmentWeather weather)
    {
        // Reuse an already-active layer of the same type during rapid redraws.
        for (int i = 0; i < activeLayers.Count; i++)
        {
            if (activeLayers[i] != null && activeLayers[i].Weather == weather)
                return activeLayers[i];
        }

        if (!pools.TryGetValue(weather, out ObjectPool pool) || pool == null)
            return null;

        GameObject go = pool.Get();
        var layer = go.GetComponent<AmbientWeatherLayer>();
        if (layer == null)
            layer = go.AddComponent<AmbientWeatherLayer>();

        layer.Initialize(pool, weather, particleMaterial, sortingOrder);
        layer.Lease();
        AttachToCamera(go.transform);
        return layer;
    }

    private void EnsureHierarchy()
    {
        poolRoot = new GameObject("AmbientWeatherPool").transform;
        poolRoot.SetParent(transform, false);
    }

    private void BuildPools()
    {
        EnsureParticleMaterial();

        CreatePool(EnvironmentWeather.Snow, maxParticlesSnow, ConfigureSnow);
        CreatePool(EnvironmentWeather.Rain, maxParticlesRain, ConfigureRain);
        CreatePool(EnvironmentWeather.Embers, maxParticlesEmbers, ConfigureEmbers);
        CreatePool(EnvironmentWeather.Dust, maxParticlesDust, ConfigureDust);
    }

    private void CreatePool(EnvironmentWeather weather, int maxParticles, System.Action<ParticleSystem, int> configure)
    {
        GameObject template = new GameObject(
            weather + "Ambient",
            typeof(ParticleSystem),
            typeof(AmbientWeatherLayer));
        template.transform.SetParent(poolRoot, false);

        var ps = template.GetComponent<ParticleSystem>();
        configure(ps, maxParticles);

        var layer = template.GetComponent<AmbientWeatherLayer>();
        template.SetActive(false);

        var pool = new ObjectPool(template, poolRoot, Mathf.Max(1, poolSizePerWeather));
        for (int i = 0; i < poolSizePerWeather; i++)
        {
            GameObject go = pool.Get();
            var warm = go.GetComponent<AmbientWeatherLayer>();
            warm.Initialize(pool, weather, particleMaterial, sortingOrder);
            pool.Release(go);
        }

        // Keep inactive template as the pool prefab (do not Destroy).
        template.SetActive(false);
        pools[weather] = pool;
    }

    private void EnsureParticleMaterial()
    {
        if (particleMaterial != null)
            return;

        Shader shader = Shader.Find("Particles/Standard Unlit")
                        ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader != null)
            particleMaterial = new Material(shader);
    }

    private void AttachToCamera(Transform layerTransform)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        layerTransform.SetParent(targetCamera.transform, false);
        layerTransform.localPosition = new Vector3(0f, 0f, 6f);
        layerTransform.localRotation = Quaternion.identity;
        layerTransform.localScale = Vector3.one;
    }

    private static void ConfigureSnow(ParticleSystem ps, int maxParticles)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor = new Color(0.92f, 0.95f, 1f, 0.85f);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.15f;

        var emission = ps.emission;
        emission.rateOverTime = 18f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(10f, 0.2f, 1f);
        shape.position = new Vector3(0f, 5f, 0f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.5f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private static void ConfigureRain(ParticleSystem ps, int maxParticles)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSize3D = true;
        main.startSizeX = 0.015f;
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
        main.startSizeZ = 0.015f;
        main.startColor = new Color(0.65f, 0.75f, 0.9f, 0.55f);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.8f;

        var emission = ps.emission;
        emission.rateOverTime = 42f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(11f, 0.15f, 1f);
        shape.position = new Vector3(0f, 5.5f, 0f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.2f;
        renderer.velocityScale = 0.08f;
    }

    private static void ConfigureEmbers(ParticleSystem ps, int maxParticles)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new Color(1f, 0.45f, 0.12f, 0.9f);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.05f;

        var emission = ps.emission;
        emission.rateOverTime = 14f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 0.4f, 1f);
        shape.position = new Vector3(0f, -3.5f, 0f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = g;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private static void ConfigureDust(ParticleSystem ps, int maxParticles)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
        main.startColor = new Color(0.78f, 0.72f, 0.58f, 0.45f);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 8f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 7f, 1f);
        shape.position = Vector3.zero;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.2f;
        noise.scrollSpeed = 0.1f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }
}
