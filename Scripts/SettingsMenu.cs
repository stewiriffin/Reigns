using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple settings panel with an analytics privacy opt-out toggle.
/// Assign scene references, or leave empty to auto-build a minimal overlay UI.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    [Header("Optional scene wiring")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Toggle analyticsOptOutToggle;
    [SerializeField] private TextMeshProUGUI optOutLabel;
    [SerializeField] private Canvas targetCanvas;

    [Header("Auto UI")]
    [SerializeField] private bool buildUiIfMissing = true;

    private bool uiBuilt;

    private void Awake()
    {
        if (buildUiIfMissing)
            EnsureUi();

        WireButtons();
        RefreshToggleFromPrefs();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    public void Show()
    {
        EnsureUi();
        RefreshToggleFromPrefs();
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    public void Hide()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    public void Toggle()
    {
        if (settingsPanel != null && settingsPanel.activeSelf)
            Hide();
        else
            Show();
    }

    private void WireButtons()
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

        if (analyticsOptOutToggle != null)
        {
            analyticsOptOutToggle.onValueChanged.RemoveListener(OnOptOutToggled);
            analyticsOptOutToggle.onValueChanged.AddListener(OnOptOutToggled);
        }
    }

    private void RefreshToggleFromPrefs()
    {
        if (analyticsOptOutToggle == null)
            return;

        analyticsOptOutToggle.SetIsOnWithoutNotify(AnalyticsManager.IsOptedOut);
        UpdateOptOutLabel(AnalyticsManager.IsOptedOut);
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

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (settingsPanel != null && analyticsOptOutToggle != null)
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
            targetCanvas.sortingOrder = 90;
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

        if (settingsPanel == null)
        {
            var dimGo = CreatePanel(
                "SettingsPanel",
                canvasRt,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.72f));
            Stretch(dimGo.GetComponent<RectTransform>());
            settingsPanel = dimGo;

            var window = CreatePanel(
                "Window",
                dimGo.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(820f, 520f),
                new Color(0.1f, 0.09f, 0.08f, 0.98f));

            CreateLabel(window.transform, "Settings", 40f, FontStyles.Bold, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -28f), new Vector2(0f, 56f));

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

            // Opt-out row
            var row = CreatePanel(
                "AnalyticsOptOutRow",
                window.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 40f),
                new Vector2(720f, 100f),
                new Color(0.14f, 0.13f, 0.12f, 1f));

            analyticsOptOutToggle = CreateToggle(row.transform);
            optOutLabel = CreateLabel(
                row.transform,
                "Opt out of analytics",
                28f,
                FontStyles.Normal,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(40f, 0f),
                new Vector2(-120f, 40f),
                TextAlignmentOptions.Left);

            CreateLabel(
                window.transform,
                "We only collect anonymous death and story-choice data to balance difficulty. You can stop this anytime.",
                22f,
                FontStyles.Normal,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.45f),
                Vector2.zero,
                new Vector2(-64f, 0f),
                TextAlignmentOptions.Center).color = new Color(0.75f, 0.72f, 0.66f, 1f);
        }

        WireButtons();
        uiBuilt = true;
    }

    private static Toggle CreateToggle(Transform parent)
    {
        var toggleGo = new GameObject("OptOutToggle", typeof(RectTransform), typeof(Toggle));
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
