using UnityEngine;

/// <summary>
/// Pooled one-shot ParticleSystem used by <see cref="StatFeedbackParticles"/>.
/// Configure once; replay color/count without allocating new systems or materials.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class PooledParticleBurst : MonoBehaviour
{
    private ParticleSystem ps;
    private ParticleSystemRenderer psRenderer;
    private ObjectPool ownerPool;
    private Gradient reusableGradient;
    private GradientColorKey[] colorKeys;
    private GradientAlphaKey[] alphaKeys;
    private ParticleSystem.Burst[] burstBuffer;
    private float returnAfter;
    private bool leased;
    private bool initialized;

    public void Initialize(ObjectPool pool, Material sharedMaterial, int sortingOrder, int sortingLayerId)
    {
        if (initialized)
            return;

        ownerPool = pool;
        ps = GetComponent<ParticleSystem>();
        psRenderer = GetComponent<ParticleSystemRenderer>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;

        reusableGradient = new Gradient();
        colorKeys = new GradientColorKey[2];
        colorKeys[0] = new GradientColorKey(Color.white, 0f);
        colorKeys[1] = new GradientColorKey(Color.white, 1f);
        alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1f, 0f);
        alphaKeys[1] = new GradientAlphaKey(0f, 1f);
        burstBuffer = new ParticleSystem.Burst[1];

        if (psRenderer != null)
        {
            psRenderer.sortingOrder = sortingOrder;
            psRenderer.sortingLayerID = sortingLayerId;
            if (sharedMaterial != null)
                psRenderer.sharedMaterial = sharedMaterial;
        }

        initialized = true;
        gameObject.SetActive(false);
    }

    public void Play(
        Transform parent,
        Vector2 anchoredPosition,
        Color color,
        int count,
        float lifetime,
        float speed,
        float radius)
    {
        leased = true;
        transform.SetParent(parent, false);

        var rect = transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        var main = ps.main;
        main.duration = lifetime;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = 8f;
        main.startColor = color;
        main.maxParticles = count;

        burstBuffer[0] = new ParticleSystem.Burst(0f, (short)count);
        var emission = ps.emission;
        emission.SetBursts(burstBuffer);

        var shape = ps.shape;
        shape.radius = radius;

        colorKeys[0].color = color;
        colorKeys[1].color = color;
        reusableGradient.SetKeys(colorKeys, alphaKeys);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.color = reusableGradient;

        gameObject.SetActive(true);
        ps.Clear(true);
        ps.Play(true);
        returnAfter = Time.unscaledTime + lifetime + 0.15f;
    }

    private void Update()
    {
        if (!leased)
            return;

        if (Time.unscaledTime < returnAfter && ps != null && ps.IsAlive(true))
            return;

        ReturnToPool();
    }

    public void ReturnToPool()
    {
        if (!leased)
            return;

        leased = false;
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        gameObject.SetActive(false);
        ownerPool?.Release(gameObject);
    }
}
