using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sliding settings panel opened from the Start Menu or Pause Screen.
/// Binds controls to <see cref="SettingsManager"/> so tweaks apply immediately and persist.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    [Header("Optional scene wiring")]
    [SerializeField] private RectTransform settingsPanelRoot;
    [SerializeField] private CanvasGroup settingsCanvasGroup;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimmerCloseButton;
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Toggle vibrationToggle;
    [SerializeField] private Toggle highFrameRateToggle;
    [SerializeField] private Button textSizeButton;
    [SerializeField] private TextMeshProUGUI textSizeLabel;
    [SerializeField] private Toggle highContrastToggle;
    [SerializeField] private Toggle colorblindToggle;
    [SerializeField] private Toggle analyticsOptOutToggle;
    [SerializeField] private TextMeshProUGUI optOutLabel;
    [SerializeField] private Canvas targetCanvas;

    [Header("Auto UI")]
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private float slideDuration = 0.28f;

    private bool uiBuilt;
    private bool isOpen;
    private Coroutine slideRoutine;
    private SettingsManager settings;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        EnsureSettingsManager();

        if (buildUiIfMissing)
            EnsureUi();

        WireControls();
        RefreshFromSettings();

        HideImmediate();
    }

    private void OnDestroy()
    {
        if (settings != null)
            settings.OnSettingsChanged -= RefreshFromSettings;
    }

    public void Show()
    {
        EnsureUi();
        EnsureSettingsManager();
        RefreshFromSettings();

        if (slideRoutine != null)
            StopCoroutine(slideRoutine);

        isOpen = true;
        slideRoutine = StartCoroutine(SlidePanel(open: true));

        if (AccessibilityManager.Instance != null)
            AccessibilityManager.Instance.Refresh();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public void Hide()
    {
        if (!isOpen && (settingsPanelRoot == null || !settingsPanelRoot.gameObject.activeSelf))
            return;

        if (slideRoutine != null)
            StopCoroutine(slideRoutine);

        isOpen = false;
        slideRoutine = StartCoroutine(SlidePanel(open: false));
    }

    public void Toggle()
    {
        if (isOpen)
            Hide();
        else
            Show();
    }

    private void EnsureSettingsManager()
    {
        settings = SettingsManager.Instance != null
            ? SettingsManager.Instance
            : FindObjectOfType<SettingsManager>();

        if (settings == null)
            settings = new GameObject("SettingsManager").AddComponent<SettingsManager>();

        settings.OnSettingsChanged -= RefreshFromSettings;
        settings.OnSettingsChanged += RefreshFromSettings;
    }

    private void WireControls()
    {
        if (openButton != null)
        {
            openButton.onClick.RemoveListener(Show);
            openButton.onClick.AddListener(Show);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
            closeButton.onClick.AddListener(Hide);
        }

        if (dimmerCloseButton != null)
        {
            dimmerCloseButton.onClick.RemoveListener(Hide);
            dimmerCloseButton.onClick.AddListener(Hide);
        }

        BindSlider(masterVolumeSlider, OnMasterChanged);
        BindSlider(bgmVolumeSlider, OnBgmChanged);
        BindSlider(sfxVolumeSlider, OnSfxChanged);
        BindToggle(vibrationToggle, OnVibrationChanged);
        BindToggle(highFrameRateToggle, OnHighFpsChanged);
        BindToggle(highContrastToggle, OnHighContrastChanged);
        BindToggle(colorblindToggle, OnColorblindChanged);
        BindToggle(analyticsOptOutToggle, OnOptOutToggled);

        if (textSizeButton != null)
        {
            textSizeButton.onClick.RemoveListener(OnTextSizeClicked);
            textSizeButton.onClick.AddListener(OnTextSizeClicked);
        }
    }

    private static void BindSlider(Slider slider, UnityEngine.Events.UnityAction<float> handler)
    {
        if (slider == null)
            return;
        slider.onValueChanged.RemoveListener(handler);
        slider.onValueChanged.AddListener(handler);
    }

    private static void BindToggle(Toggle toggle, UnityEngine.Events.UnityAction<bool> handler)
    {
        if (toggle == null)
            return;
        toggle.onValueChanged.RemoveListener(handler);
        toggle.onValueChanged.AddListener(handler);
    }

    private void RefreshFromSettings()
    {
        if (settings == null)
            return;

        SetSlider(masterVolumeSlider, settings.MasterVolume);
        SetSlider(bgmVolumeSlider, settings.BgmVolume);
        SetSlider(sfxVolumeSlider, settings.SfxVolume);
        SetToggle(vibrationToggle, settings.VibrationEnabled);
        SetToggle(highFrameRateToggle, settings.HighFrameRate);
        SetToggle(highContrastToggle, settings.HighContrastMode);
        SetToggle(colorblindToggle, settings.ColorblindMode);
        UpdateTextSizeLabel(settings.TextSize);

        if (analyticsOptOutToggle != null)
        {
            analyticsOptOutToggle.SetIsOnWithoutNotify(AnalyticsManager.IsOptedOut);
            UpdateOptOutLabel(AnalyticsManager.IsOptedOut);
        }
    }

    private void UpdateTextSizeLabel(TextSizeOption size)
    {
        if (textSizeLabel != null)
            textSizeLabel.text = "Text Size: " + AccessibilityManager.GetTextSizeLabel(size);
    }

    private static void SetSlider(Slider slider, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(Mathf.Clamp01(value));
    }

    private static void SetToggle(Toggle toggle, bool value)
    {
        if (toggle != null)
            toggle.SetIsOnWithoutNotify(value);
    }

    private void OnMasterChanged(float value)
    {
        settings?.SetMasterVolume(value);
    }

    private void OnBgmChanged(float value)
    {
        settings?.SetBgmVolume(value);
    }

    private void OnSfxChanged(float value)
    {
        settings?.SetSfxVolume(value);
    }

    private void OnVibrationChanged(bool enabled)
    {
        settings?.SetVibrationEnabled(enabled);
        if (enabled)
            HapticFeedback.PlayLight();
    }

    private void OnHighFpsChanged(bool highFps)
    {
        settings?.SetHighFrameRate(highFps);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    private void OnTextSizeClicked()
    {
        settings?.CycleTextSize();
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    private void OnHighContrastChanged(bool enabled)
    {
        settings?.SetHighContrastMode(enabled);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    private void OnColorblindChanged(bool enabled)
    {
        settings?.SetColorblindMode(enabled);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    private void OnOptOutToggled(bool optedOut)
    {
        AnalyticsManager.IsOptedOut = optedOut;
        UpdateOptOutLabel(optedOut);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    private void UpdateOptOutLabel(bool optedOut)
    {
        if (optOutLabel == null)
            return;

        optOutLabel.text = optedOut
            ? "Analytics: Opted out"
            : "Opt out of analytics";
    }

    private IEnumerator SlidePanel(bool open)
    {
        if (settingsPanelRoot == null || settingsCanvasGroup == null)
            yield break;

        settingsPanelRoot.gameObject.SetActive(true);
        settingsCanvasGroup.blocksRaycasts = open;
        settingsCanvasGroup.interactable = open;

        float width = ((RectTransform)settingsPanelRoot.parent).rect.width;
        if (width < 1f)
            width = Screen.width;

        Vector2 hidden = new Vector2(width, 0f);
        Vector2 shown = Vector2.zero;
        Vector2 from = open ? hidden : settingsPanelRoot.anchoredPosition;
        Vector2 to = open ? shown : hidden;
        float fromAlpha = open ? 0f : settingsCanvasGroup.alpha;
        float toAlpha = open ? 1f : 0f;

        float duration = Mathf.Max(0.05f, slideDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            settingsPanelRoot.anchoredPosition = Vector2.Lerp(from, to, t);
            settingsCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }

        settingsPanelRoot.anchoredPosition = to;
        settingsCanvasGroup.alpha = toAlpha;

        if (!open)
            settingsPanelRoot.gameObject.SetActive(false);

        slideRoutine = null;
    }

    private void HideImmediate()
    {
        isOpen = false;
        if (settingsPanelRoot != null)
        {
            settingsPanelRoot.gameObject.SetActive(false);
            settingsPanelRoot.anchoredPosition = new Vector2(2000f, 0f);
        }

        if (settingsCanvasGroup != null)
        {
            settingsCanvasGroup.alpha = 0f;
            settingsCanvasGroup.blocksRaycasts = false;
            settingsCanvasGroup.interactable = false;
        }
    }

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (settingsPanelRoot != null && masterVolumeSlider != null)
        {
            uiBuilt = true;
            return;
        }

        if (!buildUiIfMissing)
            return;

        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 110;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.GetComponent<RectTransform>();

        if (openButton == null)
        {
            var openGo = CreatePanel(
                "SettingsButton",
                canvasRt,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                new Vector2(160f, 72f),
                new Color(0.12f, 0.11f, 0.1f, 0.9f));
            openButton = openGo.AddComponent<Button>();
            CreateLabel(openGo.transform, "Settings", 24f, FontStyles.Bold);
        }

        var root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        root.transform.SetParent(canvasRt, false);
        settingsPanelRoot = root.GetComponent<RectTransform>();
        settingsCanvasGroup = root.GetComponent<CanvasGroup>();
        Stretch(settingsPanelRoot);
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        if (root.GetComponent<AccessibleBackground>() == null)
            root.AddComponent<AccessibleBackground>();
        dimmerCloseButton = root.GetComponent<Button>();
        dimmerCloseButton.transition = Selectable.Transition.None;

        var window = CreatePanel(
            "Window",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(860f, 1280f),
            new Color(0.1f, 0.09f, 0.08f, 0.98f));
        // Block clicks on the window from closing via dimmer.
        window.AddComponent<Button>().transition = Selectable.Transition.None;

        CreateLabel(window.transform, "Settings", 40f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), new Vector2(0f, 56f));

        var closeGo = CreatePanel(
            "Close",
            window.transform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-16f, -16f),
            new Vector2(72f, 72f),
            new Color(0.25f, 0.18f, 0.15f, 1f));
        closeButton = closeGo.AddComponent<Button>();
        CreateLabel(closeGo.transform, "X", 34f, FontStyles.Bold);

        float y = 420f;
        masterVolumeSlider = CreateVolumeRow(window.transform, "Master Volume", ref y);
        bgmVolumeSlider = CreateVolumeRow(window.transform, "Music (BGM)", ref y);
        sfxVolumeSlider = CreateVolumeRow(window.transform, "Sound Effects", ref y);

        vibrationToggle = CreateToggleRow(window.transform, "Vibration", ref y, out _);
        highFrameRateToggle = CreateToggleRow(window.transform, "Smooth Mode (60 FPS)", ref y, out _);

        textSizeButton = CreateTextSizeRow(window.transform, ref y, out textSizeLabel);
        highContrastToggle = CreateToggleRow(window.transform, "High Contrast Mode", ref y, out _);
        colorblindToggle = CreateToggleRow(window.transform, "Colorblind Mode", ref y, out _);

        analyticsOptOutToggle = CreateToggleRow(window.transform, "Opt out of analytics", ref y, out optOutLabel);

        CreateLabel(
            window.transform,
            "Accessibility options apply immediately and save for next launch.",
            20f,
            FontStyles.Normal,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 28f),
            new Vector2(-48f, 48f),
            TextAlignmentOptions.Center).color = new Color(0.75f, 0.72f, 0.66f, 1f);

        WireControls();
        uiBuilt = true;

        if (AccessibilityManager.Instance != null)
            AccessibilityManager.Instance.Refresh();
    }

    private static Button CreateTextSizeRow(Transform parent, ref float y, out TextMeshProUGUI label)
    {
        var row = CreatePanel(
            "TextSizeRow",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, y),
            new Vector2(760f, 100f),
            new Color(0.14f, 0.13f, 0.12f, 1f));

        label = CreateLabel(
            row.transform,
            "Text Size: Medium",
            26f,
            FontStyles.Bold,
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(28f, 0f),
            new Vector2(-200f, 40f),
            TextAlignmentOptions.Left);

        var buttonGo = CreatePanel(
            "Cycle",
            row.transform,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(-20f, 0f),
            new Vector2(160f, 64f),
            new Color(0.25f, 0.2f, 0.16f, 1f));
        var button = buttonGo.AddComponent<Button>();
        CreateLabel(buttonGo.transform, "Change", 24f, FontStyles.Bold);

        y -= 116f;
        return button;
    }

    private static Slider CreateVolumeRow(Transform parent, string label, ref float y)
    {
        var row = CreatePanel(
            label + "Row",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, y),
            new Vector2(760f, 110f),
            new Color(0.14f, 0.13f, 0.12f, 1f));

        CreateLabel(row.transform, label, 26f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -8f), new Vector2(-24f, 36f),
            TextAlignmentOptions.Left);

        Slider slider = CreateSlider(row.transform);
        y -= 128f;
        return slider;
    }

    private static Toggle CreateToggleRow(Transform parent, string label, ref float y, out TextMeshProUGUI labelTmp)
    {
        var row = CreatePanel(
            label + "Row",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, y),
            new Vector2(760f, 100f),
            new Color(0.14f, 0.13f, 0.12f, 1f));

        labelTmp = CreateLabel(
            row.transform,
            label,
            26f,
            FontStyles.Normal,
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(28f, 0f),
            new Vector2(-140f, 40f),
            TextAlignmentOptions.Left);

        Toggle toggle = CreateToggle(row.transform);
        y -= 116f;
        return toggle;
    }

    private static Slider CreateSlider(Transform parent)
    {
        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(parent, false);
        var rt = sliderGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 18f);
        rt.sizeDelta = new Vector2(-48f, 36f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderGo.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(0.25f, 0.24f, 0.22f, 1f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRt);
        fillAreaRt.offsetMin = new Vector2(8f, 8f);
        fillAreaRt.offsetMax = new Vector2(-8f, -8f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch(fill.GetComponent<RectTransform>());
        fill.GetComponent<Image>().color = new Color(0.72f, 0.58f, 0.32f, 1f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>());

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(28f, 48f);
        handle.GetComponent<Image>().color = new Color(0.95f, 0.92f, 0.86f, 1f);

        var slider = sliderGo.GetComponent<Slider>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRt;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;
        return slider;
    }

    private static Toggle CreateToggle(Transform parent)
    {
        var toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
        toggleGo.transform.SetParent(parent, false);
        var rt = toggleGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(64f, 64f);
        rt.anchoredPosition = new Vector2(-28f, 0f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(toggleGo.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(0.25f, 0.24f, 0.22f, 1f);

        var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(bg.transform, false);
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0.2f, 0.2f);
        checkRt.anchorMax = new Vector2(0.8f, 0.8f);
        checkRt.offsetMin = Vector2.zero;
        checkRt.offsetMax = Vector2.zero;
        check.GetComponent<Image>().color = new Color(0.72f, 0.86f, 0.45f, 1f);

        var toggle = toggleGo.GetComponent<Toggle>();
        toggle.targetGraphic = bg.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = false;
        return toggle;
    }

    private static GameObject CreatePanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 size,
        Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        if (go.GetComponent<AccessibleBackground>() == null)
            go.AddComponent<AccessibleBackground>();
        return go;
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent,
        string text,
        float fontSize,
        FontStyles style,
        Vector2? anchorMin = null,
        Vector2? anchorMax = null,
        Vector2? anchoredPos = null,
        Vector2? sizeDelta = null,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin ?? Vector2.zero;
        rt.anchorMax = anchorMax ?? Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos ?? Vector2.zero;
        rt.sizeDelta = sizeDelta ?? Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
