using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Modular settings hub: volumes, vibration, and frame-rate mode.
/// Persists to PlayerPrefs, applies immediately, and drives an optional AudioMixer
/// (plus AudioManager as a fallback / companion bus).
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    // Shared with AudioManager so volumes stay consistent across systems.
    public const string PrefsMasterVolume = "Audio_MasterVolume";
    public const string PrefsBgmVolume = "Audio_BgmVolume";
    public const string PrefsSfxVolume = "Audio_SfxVolume";
    public const string PrefsVibration = "Settings_VibrationEnabled";
    public const string PrefsHighFrameRate = "Settings_HighFrameRate";
    public const string PrefsTextSize = "Settings_TextSize";
    public const string PrefsHighContrast = "Settings_HighContrast";
    public const string PrefsColorblind = "Settings_ColorblindMode";

    public const int PowerSavingFps = 30;
    public const int SmoothModeFps = 60;

    [Header("Audio Mixer (optional)")]
    [Tooltip("Assign your MainMixer asset. Expose float params matching the names below.")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterMixerParam = "MasterVolume";
    [SerializeField] private string bgmMixerParam = "BGMVolume";
    [SerializeField] private string sfxMixerParam = "SFXVolume";
    [SerializeField] private AudioMixerGroup bgmMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    [Header("Defaults")]
    [SerializeField] [Range(0f, 1f)] private float defaultMasterVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float defaultBgmVolume = 0.7f;
    [SerializeField] [Range(0f, 1f)] private float defaultSfxVolume = 1f;
    [SerializeField] private bool defaultVibrationEnabled = true;
    [SerializeField] private bool defaultHighFrameRate = false;
    [SerializeField] private TextSizeOption defaultTextSize = TextSizeOption.Medium;
    [SerializeField] private bool defaultHighContrast = false;
    [SerializeField] private bool defaultColorblindMode = false;

    private float masterVolume;
    private float bgmVolume;
    private float sfxVolume;
    private bool vibrationEnabled;
    private bool highFrameRate;
    private TextSizeOption textSize;
    private bool highContrastMode;
    private bool colorblindMode;

    /// <summary>Fired after any setting is changed and applied.</summary>
    public event Action OnSettingsChanged;

    public float MasterVolume => masterVolume;
    public float BgmVolume => bgmVolume;
    public float SfxVolume => sfxVolume;
    public bool VibrationEnabled => vibrationEnabled;
    public bool HighFrameRate => highFrameRate;
    public TextSizeOption TextSize => textSize;
    public bool HighContrastMode => highContrastMode;
    public bool ColorblindMode => colorblindMode;
    public int TargetFrameRate => highFrameRate ? SmoothModeFps : PowerSavingFps;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadFromPrefs();
        ApplyAll(save: false);
    }

    private void Start()
    {
        // Re-apply once AudioManager / MobileOptimizer have likely woken up.
        ApplyAll(save: false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Reload prefs from disk and re-apply (e.g. after a debug wipe).</summary>
    public void Reload()
    {
        LoadFromPrefs();
        ApplyAll(save: false);
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        SaveVolumes();
        NotifyChanged();
    }

    public void SetBgmVolume(float value)
    {
        bgmVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        SaveVolumes();
        NotifyChanged();
    }

    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        SaveVolumes();
        NotifyChanged();
    }

    public void SetVibrationEnabled(bool enabled)
    {
        vibrationEnabled = enabled;
        ApplyVibration();
        PlayerPrefs.SetInt(PrefsVibration, vibrationEnabled ? 1 : 0);
        PlayerPrefs.Save();
        NotifyChanged();
    }

    /// <summary>
    /// True = 60 FPS Smooth Mode, false = 30 FPS Power Saving.
    /// </summary>
    public void SetHighFrameRate(bool enabled)
    {
        highFrameRate = enabled;
        ApplyFrameRate();
        PlayerPrefs.SetInt(PrefsHighFrameRate, highFrameRate ? 1 : 0);
        PlayerPrefs.Save();
        NotifyChanged();
    }

    public void SetTextSize(TextSizeOption size)
    {
        textSize = size;
        PlayerPrefs.SetInt(PrefsTextSize, (int)textSize);
        PlayerPrefs.Save();
        NotifyChanged();
    }

    public void CycleTextSize()
    {
        int next = ((int)textSize + 1) % 3;
        SetTextSize((TextSizeOption)next);
    }

    public void SetHighContrastMode(bool enabled)
    {
        highContrastMode = enabled;
        PlayerPrefs.SetInt(PrefsHighContrast, highContrastMode ? 1 : 0);
        PlayerPrefs.Save();
        NotifyChanged();
    }

    public void SetColorblindMode(bool enabled)
    {
        colorblindMode = enabled;
        PlayerPrefs.SetInt(PrefsColorblind, colorblindMode ? 1 : 0);
        PlayerPrefs.Save();
        NotifyChanged();
    }

    /// <summary>Applies every setting without rewriting prefs (startup path).</summary>
    public void ApplyAll(bool save = false)
    {
        ApplyVolumes();
        ApplyVibration();
        ApplyFrameRate();

        if (save)
        {
            SaveVolumes();
            PlayerPrefs.SetInt(PrefsVibration, vibrationEnabled ? 1 : 0);
            PlayerPrefs.SetInt(PrefsHighFrameRate, highFrameRate ? 1 : 0);
            PlayerPrefs.SetInt(PrefsTextSize, (int)textSize);
            PlayerPrefs.SetInt(PrefsHighContrast, highContrastMode ? 1 : 0);
            PlayerPrefs.SetInt(PrefsColorblind, colorblindMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        NotifyChanged();
    }

    private void LoadFromPrefs()
    {
        masterVolume = PlayerPrefs.GetFloat(PrefsMasterVolume, defaultMasterVolume);
        bgmVolume = PlayerPrefs.GetFloat(PrefsBgmVolume, defaultBgmVolume);
        sfxVolume = PlayerPrefs.GetFloat(PrefsSfxVolume, defaultSfxVolume);
        vibrationEnabled = PlayerPrefs.GetInt(PrefsVibration, defaultVibrationEnabled ? 1 : 0) == 1;
        highFrameRate = PlayerPrefs.GetInt(PrefsHighFrameRate, defaultHighFrameRate ? 1 : 0) == 1;
        textSize = (TextSizeOption)Mathf.Clamp(
            PlayerPrefs.GetInt(PrefsTextSize, (int)defaultTextSize), 0, 2);
        highContrastMode = PlayerPrefs.GetInt(PrefsHighContrast, defaultHighContrast ? 1 : 0) == 1;
        colorblindMode = PlayerPrefs.GetInt(PrefsColorblind, defaultColorblindMode ? 1 : 0) == 1;
    }

    private void SaveVolumes()
    {
        PlayerPrefs.SetFloat(PrefsMasterVolume, masterVolume);
        PlayerPrefs.SetFloat(PrefsBgmVolume, bgmVolume);
        PlayerPrefs.SetFloat(PrefsSfxVolume, sfxVolume);
        PlayerPrefs.Save();
    }

    private void ApplyVolumes()
    {
        ApplyMixerVolumes();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.BindMixerGroups(bgmMixerGroup, sfxMixerGroup);
            AudioManager.Instance.ApplyVolumeLevels(masterVolume, bgmVolume, sfxVolume, persist: false);
        }
    }

    private void ApplyMixerVolumes()
    {
        if (audioMixer == null)
            return;

        SetMixerLinear(masterMixerParam, masterVolume);
        SetMixerLinear(bgmMixerParam, bgmVolume);
        SetMixerLinear(sfxMixerParam, sfxVolume);
    }

    private void SetMixerLinear(string paramName, float linear01)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            return;

        // Unity mixers use decibels; map 0..1 → -80..0 dB (0 linear = silence).
        float dB = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
        audioMixer.SetFloat(paramName, dB);
    }

    private void ApplyVibration()
    {
        HapticFeedback.Enabled = vibrationEnabled;
    }

    private void ApplyFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;

        MobileOptimizer optimizer = FindObjectOfType<MobileOptimizer>();
        if (optimizer != null)
            optimizer.NotifyExternalFrameRate(TargetFrameRate);
    }

    private void NotifyChanged()
    {
        OnSettingsChanged?.Invoke();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        bgmVolume = Mathf.Clamp01(bgmVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
    }
#endif
}
