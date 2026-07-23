using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One pooled ambient particle layer (snow / rain / embers / dust).
/// Intensity 0–1 scales emission for smooth crossfades without Instantiate/Destroy.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class AmbientWeatherLayer : MonoBehaviour
{
    private ParticleSystem ps;
    private ParticleSystem.EmissionModule emission;
    private ParticleSystemRenderer psRenderer;
    private float baseRate;
    private Color baseStartColor = Color.white;
    private float intensity;
    private bool leased;
    private ObjectPool ownerPool;
    private bool initialized;

    public EnvironmentWeather Weather { get; private set; }
    public float Intensity => intensity;
    public bool IsLeased => leased;

    public void Initialize(ObjectPool pool, EnvironmentWeather weather, Material sharedMaterial, int sortingOrder)
    {
        if (initialized)
            return;

        ownerPool = pool;
        Weather = weather;
        ps = GetComponent<ParticleSystem>();
        psRenderer = GetComponent<ParticleSystemRenderer>();
        emission = ps.emission;
        baseRate = emission.rateOverTime.constant;

        var main = ps.main;
        baseStartColor = main.startColor.color;
        main.playOnAwake = false;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        if (psRenderer != null)
        {
            psRenderer.sortingOrder = sortingOrder;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            psRenderer.allowOcclusionWhenDynamic = false;
            if (sharedMaterial != null)
                psRenderer.sharedMaterial = sharedMaterial;
        }

        intensity = 0f;
        ApplyIntensity(0f);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
        initialized = true;
    }

    public void Lease()
    {
        leased = true;
        gameObject.SetActive(true);
        if (!ps.isPlaying)
            ps.Play(true);
    }

    public void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
        ApplyIntensity(intensity);

        if (intensity <= 0.001f)
        {
            if (ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        else if (!ps.isPlaying)
        {
            ps.Play(true);
        }
    }

    public void ReturnToPool()
    {
        leased = false;
        intensity = 0f;
        ApplyIntensity(0f);
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
        ownerPool?.Release(gameObject);
    }

    private void ApplyIntensity(float value)
    {
        if (ps == null)
            return;

        emission = ps.emission;
        emission.rateOverTime = baseRate * value;

        var main = ps.main;
        Color c = baseStartColor;
        c.a = baseStartColor.a * value;
        main.startColor = c;
    }
}
