using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton audio hub with separate BGM and SFX channels, volume buses, and BGM crossfades.
/// Volume persistence is owned by <see cref="SettingsManager"/> when present;
/// this class still applies runtime levels and optional AudioMixer group routing.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public const string BusBgm = "BGM";
    public const string BusSfx = "SFX";
    public const string BusMaster = "Master";

    private const string PrefsMaster = SettingsManager.PrefsMasterVolume;
    private const string PrefsBgm = SettingsManager.PrefsBgmVolume;
    private const string PrefsSfx = SettingsManager.PrefsSfxVolume;

    public static AudioManager Instance { get; private set; }

    [Header("Channels")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioMixerGroup bgmMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

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
    private bool mutedForFullscreenAd;
    private bool mutedForAppPause;

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
        if (SettingsManager.Instance != null)
        {
            masterVolume = SettingsManager.Instance.MasterVolume;
            bgmVolume = SettingsManager.Instance.BgmVolume;
            sfxVolume = SettingsManager.Instance.SfxVolume;
        }
        else
        {
            LoadVolumes();
        }

        activeBgmSource = bgmSourceA;
        idleBgmSource = bgmSourceB;
        ApplyMixerRouting();
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
    /// Routes BGM / SFX sources to mixer groups (null clears routing).
    /// </summary>
    public void BindMixerGroups(AudioMixerGroup bgmGroup, AudioMixerGroup sfxGroup)
    {
        bgmMixerGroup = bgmGroup;
        sfxMixerGroup = sfxGroup;
        ApplyMixerRouting();
    }

    /// <summary>
    /// Applies all three volume buses. When <paramref name="persist"/> is false,
    /// PlayerPrefs are left to SettingsManager.
    /// </summary>
    public void ApplyVolumeLevels(float master, float bgm, float sfx, bool persist = true)
    {
        masterVolume = Mathf.Clamp01(master);
        bgmVolume = Mathf.Clamp01(bgm);
        sfxVolume = Mathf.Clamp01(sfx);

        if (persist)
        {
            PlayerPrefs.SetFloat(PrefsMaster, masterVolume);
            PlayerPrefs.SetFloat(PrefsBgm, bgmVolume);
            PlayerPrefs.SetFloat(PrefsSfx, sfxVolume);
            PlayerPrefs.Save();
        }

        ApplyVolumes();
    }

    /// <summary>
    /// Sets a volume bus. <paramref name="bus"/> is "BGM", "SFX", or "Master" (case-insensitive).
    /// Prefer SettingsManager for UI-driven changes.
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
                if (SettingsManager.Instance != null)
                {
                    SettingsManager.Instance.SetBgmVolume(clamped);
                    return;
                }

                PlayerPrefs.SetFloat(PrefsBgm, bgmVolume);
                break;
            case "SFX":
            case "SOUND":
                sfxVolume = clamped;
                if (SettingsManager.Instance != null)
                {
                    SettingsManager.Instance.SetSfxVolume(clamped);
                    return;
                }

                PlayerPrefs.SetFloat(PrefsSfx, sfxVolume);
                break;
            case "MASTER":
                masterVolume = clamped;
                if (SettingsManager.Instance != null)
                {
                    SettingsManager.Instance.SetMasterVolume(clamped);
                    return;
                }

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

    /// <summary>
    /// Mutes BGM output while a full-screen ad is up without changing saved volume prefs.
    /// </summary>
    public void SetMutedForFullscreenAd(bool muted)
    {
        mutedForFullscreenAd = muted;
        ApplyMuteState();
    }

    /// <summary>
    /// Mutes BGM when the app is backgrounded / phone call / OS pause.
    /// </summary>
    public void SetMutedForAppPause(bool muted)
    {
        mutedForAppPause = muted;
        ApplyMuteState();
    }

    private void ApplyMuteState()
    {
        ApplyVolumes();

        bool muted = mutedForFullscreenAd || mutedForAppPause;
        if (activeBgmSource != null && activeBgmSource.isPlaying)
            activeBgmSource.volume = GetBgmOutputVolume() * bgmFadeMultiplier;
        if (idleBgmSource != null && idleBgmSource.isPlaying)
            idleBgmSource.volume = muted ? 0f : GetBgmOutputVolume() * bgmFadeMultiplier;
    }

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

    private void ApplyMixerRouting()
    {
        if (bgmSourceA != null)
            bgmSourceA.outputAudioMixerGroup = bgmMixerGroup;
        if (bgmSourceB != null)
            bgmSourceB.outputAudioMixerGroup = bgmMixerGroup;
        if (sfxSource != null)
            sfxSource.outputAudioMixerGroup = sfxMixerGroup;
    }

    private void ApplyVolumes()
    {
        if (activeBgmSource != null && activeBgmSource.isPlaying)
            activeBgmSource.volume = GetBgmOutputVolume() * bgmFadeMultiplier;

        if (sfxSource != null)
            sfxSource.volume = 1f;
    }

    private float GetBgmOutputVolume()
    {
        if (mutedForFullscreenAd || mutedForAppPause)
            return 0f;

        // Mixer path: exposed params own Master/BGM; keep source near unity gain.
        if (bgmMixerGroup != null)
            return 1f;

        return masterVolume * bgmVolume;
    }

    private float GetSfxOutputVolume()
    {
        if (sfxMixerGroup != null)
            return 1f;

        return masterVolume * sfxVolume;
    }
}
