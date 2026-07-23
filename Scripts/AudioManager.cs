using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton audio hub with separate BGM and SFX channels, volume buses, and BGM crossfades.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public const string BusBgm = "BGM";
    public const string BusSfx = "SFX";
    public const string BusMaster = "Master";

    private const string PrefsMaster = "Audio_MasterVolume";
    private const string PrefsBgm = "Audio_BgmVolume";
    private const string PrefsSfx = "Audio_SfxVolume";

    public static AudioManager Instance { get; private set; }

    [Header("Channels")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField] private AudioSource sfxSource;

    [Header("BGM")]
    [SerializeField] private float bgmCrossfadeDuration = 1f;
    [SerializeField] private AudioClip defaultBgm;

    [Header("SFX Clips")]
    [SerializeField] private AudioClip cardDrawSfx;
    [SerializeField] private AudioClip swipeLeftSfx;
    [SerializeField] private AudioClip swipeRightSfx;
    [SerializeField] private AudioClip buttonClickSfx;
    [SerializeField] private AudioClip gameOverSfx;

    [Header("Volumes (0-1)")]
    [SerializeField] [Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float bgmVolume = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1f;

    private AudioSource activeBgmSource;
    private AudioSource idleBgmSource;
    private Coroutine crossfadeRoutine;
    private float bgmFadeMultiplier = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureSources();
        LoadVolumes();
        activeBgmSource = bgmSourceA;
        idleBgmSource = bgmSourceB;
        ApplyVolumes();
    }

    private void Start()
    {
        if (defaultBgm != null && activeBgmSource != null && !activeBgmSource.isPlaying)
            PlayBGM(defaultBgm, loop: true);
    }

    private void EnsureSources()
    {
        if (bgmSourceA == null)
            bgmSourceA = CreateChildSource("BGM_A");
        if (bgmSourceB == null)
            bgmSourceB = CreateChildSource("BGM_B");
        if (sfxSource == null)
            sfxSource = CreateChildSource("SFX");

        ConfigureBgmSource(bgmSourceA);
        ConfigureBgmSource(bgmSourceB);

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
    }

    private AudioSource CreateChildSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<AudioSource>();
    }

    private static void ConfigureBgmSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
    }

    /// <summary>
    /// Plays (or crossfades to) a background track.
    /// </summary>
    public void PlayBGM(AudioClip clip, bool loop)
    {
        if (clip == null || activeBgmSource == null)
            return;

        // Same clip already playing — just ensure loop flag.
        if (activeBgmSource.isPlaying && activeBgmSource.clip == clip)
        {
            activeBgmSource.loop = loop;
            return;
        }

        if (crossfadeRoutine != null)
            StopCoroutine(crossfadeRoutine);

        // Nothing playing yet — start immediately.
        if (activeBgmSource.clip == null || !activeBgmSource.isPlaying)
        {
            activeBgmSource.clip = clip;
            activeBgmSource.loop = loop;
            activeBgmSource.volume = GetBgmOutputVolume();
            activeBgmSource.Play();
            bgmFadeMultiplier = 1f;
            return;
        }

        crossfadeRoutine = StartCoroutine(CrossfadeBgm(clip, loop, bgmCrossfadeDuration));
    }

    /// <summary>
    /// Plays a one-shot sound effect on the SFX bus.
    /// </summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null || sfxSource == null)
            return;

        sfxSource.PlayOneShot(clip, GetSfxOutputVolume());
    }

    /// <summary>
    /// Sets a volume bus. <paramref name="bus"/> is "BGM", "SFX", or "Master" (case-insensitive).
    /// </summary>
    public void SetVolume(string bus, float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        if (string.IsNullOrWhiteSpace(bus))
            return;

        switch (bus.Trim().ToUpperInvariant())
        {
            case "BGM":
            case "MUSIC":
                bgmVolume = clamped;
                PlayerPrefs.SetFloat(PrefsBgm, bgmVolume);
                break;
            case "SFX":
            case "SOUND":
                sfxVolume = clamped;
                PlayerPrefs.SetFloat(PrefsSfx, sfxVolume);
                break;
            case "MASTER":
                masterVolume = clamped;
                PlayerPrefs.SetFloat(PrefsMaster, masterVolume);
                break;
            default:
                Debug.LogWarning($"AudioManager: Unknown bus '{bus}'. Use BGM, SFX, or Master.");
                return;
        }

        PlayerPrefs.Save();
        ApplyVolumes();
    }

    public float GetVolume(string bus)
    {
        if (string.IsNullOrWhiteSpace(bus))
            return 0f;

        return bus.Trim().ToUpperInvariant() switch
        {
            "BGM" or "MUSIC" => bgmVolume,
            "SFX" or "SOUND" => sfxVolume,
            "MASTER" => masterVolume,
            _ => 0f
        };
    }

    public void PlayCardDraw() => PlaySFX(cardDrawSfx);
    public void PlaySwipeLeft() => PlaySFX(swipeLeftSfx);
    public void PlaySwipeRight() => PlaySFX(swipeRightSfx);
    public void PlayButtonClick() => PlaySFX(buttonClickSfx);
    public void PlayGameOver() => PlaySFX(gameOverSfx);

    public void StopBGM(bool fade = true)
    {
        if (crossfadeRoutine != null)
        {
            StopCoroutine(crossfadeRoutine);
            crossfadeRoutine = null;
        }

        if (!fade || activeBgmSource == null || !activeBgmSource.isPlaying)
        {
            if (activeBgmSource != null)
                activeBgmSource.Stop();
            if (idleBgmSource != null)
                idleBgmSource.Stop();
            bgmFadeMultiplier = 1f;
            return;
        }

        crossfadeRoutine = StartCoroutine(FadeOutBgm(bgmCrossfadeDuration));
    }

    private IEnumerator CrossfadeBgm(AudioClip nextClip, bool loop, float duration)
    {
        AudioSource from = activeBgmSource;
        AudioSource to = idleBgmSource;

        to.clip = nextClip;
        to.loop = loop;
        to.volume = 0f;
        to.Play();

        float startFrom = from.volume;
        float targetTo = GetBgmOutputVolume();
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Smoothstep
            t = t * t * (3f - 2f * t);

            from.volume = Mathf.Lerp(startFrom, 0f, t);
            to.volume = Mathf.Lerp(0f, targetTo, t);
            yield return null;
        }

        from.Stop();
        from.volume = 0f;
        to.volume = targetTo;

        activeBgmSource = to;
        idleBgmSource = from;
        bgmFadeMultiplier = 1f;
        crossfadeRoutine = null;
    }

    private IEnumerator FadeOutBgm(float duration)
    {
        AudioSource from = activeBgmSource;
        float start = from != null ? from.volume : 0f;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (from != null)
                from.volume = Mathf.Lerp(start, 0f, t);
            yield return null;
        }

        if (from != null)
            from.Stop();
        bgmFadeMultiplier = 1f;
        crossfadeRoutine = null;
    }

    private void LoadVolumes()
    {
        masterVolume = PlayerPrefs.GetFloat(PrefsMaster, masterVolume);
        bgmVolume = PlayerPrefs.GetFloat(PrefsBgm, bgmVolume);
        sfxVolume = PlayerPrefs.GetFloat(PrefsSfx, sfxVolume);
    }

    private void ApplyVolumes()
    {
        if (activeBgmSource != null && activeBgmSource.isPlaying)
            activeBgmSource.volume = GetBgmOutputVolume() * bgmFadeMultiplier;

        if (idleBgmSource != null && idleBgmSource.isPlaying)
        {
            // During crossfade, leave relative mix alone except master/bgm scale via next Apply on end.
        }
    }

    private float GetBgmOutputVolume() => masterVolume * bgmVolume;
    private float GetSfxOutputVolume() => masterVolume * sfxVolume;
}
