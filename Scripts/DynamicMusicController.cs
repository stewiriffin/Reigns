using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Dual-layer adaptive BGM: Normal State and Crisis State play in sync.
/// When any kingdom stat drops below the threshold, smoothly crossfades to Crisis
/// over 1 second; returns to Normal once all stats are safely above the threshold.
/// </summary>
public class DynamicMusicController : MonoBehaviour
{
    public static DynamicMusicController Instance { get; private set; }

    [Header("Tracks (assign matching-length / tempo-aligned loops)")]
    [SerializeField] private AudioClip normalStateClip;
    [SerializeField] private AudioClip crisisStateClip;

    [Header("Mood")]
    [Tooltip("Enter crisis when the lowest stat is strictly below this value.")]
    [SerializeField] private int crisisStatThreshold = 20;
    [SerializeField] private float moodCrossfadeSeconds = 1f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool keepLayersTimeSynced = true;

    [Header("Optional wiring")]
    [SerializeField] private KingdomStats kingdomStats;
    [SerializeField] private AudioMixerGroup bgmMixerGroup;

    private AudioSource normalSource;
    private AudioSource crisisSource;
    private Coroutine moodRoutine;
    private float crisisBlend; // 0 = full Normal, 1 = full Crisis
    private bool targetCrisis;
    private bool layersStarted;

    public bool IsCrisisMood => targetCrisis;
    public float CrisisBlend => crisisBlend;
    public bool HasClips => normalStateClip != null && crisisStateClip != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureSources();
    }

    private void Start()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (kingdomStats != null)
            kingdomStats.OnStatsChanged += HandleStatsChanged;

        if (AudioManager.Instance != null)
            AudioManager.Instance.RegisterDynamicMusic(this);

        if (playOnStart && HasClips)
            StartLayers();

        EvaluateMood(immediate: true);
    }

    private void OnEnable()
    {
        if (kingdomStats != null)
        {
            kingdomStats.OnStatsChanged -= HandleStatsChanged;
            kingdomStats.OnStatsChanged += HandleStatsChanged;
        }
    }

    private void OnDisable()
    {
        if (kingdomStats != null)
            kingdomStats.OnStatsChanged -= HandleStatsChanged;
    }

    private void OnDestroy()
    {
        if (kingdomStats != null)
            kingdomStats.OnStatsChanged -= HandleStatsChanged;

        if (AudioManager.Instance != null)
            AudioManager.Instance.UnregisterDynamicMusic(this);

        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (!keepLayersTimeSynced || !layersStarted)
            return;

        if (normalSource == null || crisisSource == null)
            return;

        if (!normalSource.isPlaying || !crisisSource.isPlaying)
            return;

        // Re-lock if drift exceeds ~23ms at 44.1kHz.
        if (Mathf.Abs(normalSource.timeSamples - crisisSource.timeSamples) > 1024)
            crisisSource.timeSamples = normalSource.timeSamples;
    }

    /// <summary>Assign clips at runtime (e.g. from addressables) and restart layers.</summary>
    public void SetClips(AudioClip normal, AudioClip crisis, bool restart = true)
    {
        normalStateClip = normal;
        crisisStateClip = crisis;
        if (restart && HasClips)
            StartLayers();
    }

    public void BindKingdomStats(KingdomStats stats)
    {
        if (kingdomStats != null)
            kingdomStats.OnStatsChanged -= HandleStatsChanged;

        kingdomStats = stats;
        if (kingdomStats != null)
            kingdomStats.OnStatsChanged += HandleStatsChanged;

        EvaluateMood(immediate: false);
    }

    public void StartLayers()
    {
        if (!HasClips)
        {
            Debug.LogWarning("DynamicMusicController: Assign Normal State and Crisis State clips.");
            return;
        }

        EnsureSources();
        StopMoodRoutine();

        normalSource.clip = normalStateClip;
        crisisSource.clip = crisisStateClip;
        normalSource.loop = true;
        crisisSource.loop = true;

        float bus = GetBusVolume();
        normalSource.volume = bus * (1f - crisisBlend);
        crisisSource.volume = bus * crisisBlend;

        normalSource.Play();
        crisisSource.timeSamples = 0;
        crisisSource.Play();
        crisisSource.timeSamples = normalSource.timeSamples;

        layersStarted = true;

        if (AudioManager.Instance != null)
            AudioManager.Instance.NotifyDynamicMusicStarted();
    }

    public void StopLayers(bool fade = false)
    {
        StopMoodRoutine();
        layersStarted = false;

        if (!fade)
        {
            if (normalSource != null) normalSource.Stop();
            if (crisisSource != null) crisisSource.Stop();
            return;
        }

        StartCoroutine(FadeOutLayers(moodCrossfadeSeconds));
    }

    /// <summary>Called by AudioManager when master/BGM volume or mute changes.</summary>
    public void RefreshVolumes()
    {
        ApplyBlendVolumes();
    }

    private void HandleStatsChanged()
    {
        EvaluateMood(immediate: false);
    }

    private void EvaluateMood(bool immediate)
    {
        if (kingdomStats == null)
            return;

        bool shouldCrisis = kingdomStats.LowestStat < crisisStatThreshold;
        if (shouldCrisis == targetCrisis && !immediate)
            return;

        targetCrisis = shouldCrisis;

        if (immediate || !layersStarted || !isActiveAndEnabled)
        {
            crisisBlend = shouldCrisis ? 1f : 0f;
            ApplyBlendVolumes();
            return;
        }

        StopMoodRoutine();
        moodRoutine = StartCoroutine(CrossfadeMood(shouldCrisis ? 1f : 0f));
    }

    private IEnumerator CrossfadeMood(float targetBlend)
    {
        float start = crisisBlend;
        float duration = Mathf.Max(0.01f, moodCrossfadeSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            crisisBlend = Mathf.Lerp(start, targetBlend, t);
            ApplyBlendVolumes();
            yield return null;
        }

        crisisBlend = targetBlend;
        ApplyBlendVolumes();
        moodRoutine = null;
    }

    private IEnumerator FadeOutLayers(float duration)
    {
        float startNormal = normalSource != null ? normalSource.volume : 0f;
        float startCrisis = crisisSource != null ? crisisSource.volume : 0f;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (normalSource != null)
                normalSource.volume = Mathf.Lerp(startNormal, 0f, t);
            if (crisisSource != null)
                crisisSource.volume = Mathf.Lerp(startCrisis, 0f, t);
            yield return null;
        }

        if (normalSource != null) normalSource.Stop();
        if (crisisSource != null) crisisSource.Stop();
    }

    private void ApplyBlendVolumes()
    {
        if (normalSource == null || crisisSource == null)
            return;

        float bus = GetBusVolume();
        normalSource.volume = bus * (1f - crisisBlend);
        crisisSource.volume = bus * crisisBlend;
    }

    private float GetBusVolume()
    {
        if (AudioManager.Instance != null)
            return AudioManager.Instance.GetBgmBusOutputVolume();

        if (SettingsManager.Instance != null)
            return SettingsManager.Instance.MasterVolume * SettingsManager.Instance.BgmVolume;

        return 1f;
    }

    private void EnsureSources()
    {
        if (normalSource == null)
            normalSource = CreateChildSource("BGM_NormalState");
        if (crisisSource == null)
            crisisSource = CreateChildSource("BGM_CrisisState");

        Configure(normalSource);
        Configure(crisisSource);

        if (bgmMixerGroup == null && AudioManager.Instance != null)
            bgmMixerGroup = AudioManager.Instance.BgmMixerGroup;

        normalSource.outputAudioMixerGroup = bgmMixerGroup;
        crisisSource.outputAudioMixerGroup = bgmMixerGroup;
    }

    private AudioSource CreateChildSource(string name)
    {
        Transform existing = transform.Find(name);
        if (existing != null)
        {
            var src = existing.GetComponent<AudioSource>();
            if (src != null)
                return src;
        }

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<AudioSource>();
    }

    private static void Configure(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
    }

    private void StopMoodRoutine()
    {
        if (moodRoutine == null)
            return;

        StopCoroutine(moodRoutine);
        moodRoutine = null;
    }
}
