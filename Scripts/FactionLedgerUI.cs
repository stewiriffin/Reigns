using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Secret Factions ledger — view hidden sub-faction loyalties.
/// Auto-builds UI when scene references are missing.
/// </summary>
public class FactionLedgerUI : MonoBehaviour
{
    [Header("Optional scene wiring")]
    [SerializeField] private GameObject ledgerPanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private Transform listContent;
    [SerializeField] private Canvas targetCanvas;

    [Header("Auto UI")]
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private float rowHeight = 168f;

    private readonly List<GameObject> rowPool = new List<GameObject>();
    private bool uiBuilt;
    private FactionRelationshipManager factions;

    public bool IsOpen => ledgerPanel != null && ledgerPanel.activeSelf;

    private void Awake()
    {
        EnsureFactions();
        if (buildUiIfMissing)
            EnsureUi();
        WireButtons();
        Hide();
    }

    private void OnEnable()
    {
        EnsureFactions();
        if (factions != null)
            factions.OnRelationshipsChanged += Refresh;
    }

    private void OnDisable()
    {
        if (factions != null)
            factions.OnRelationshipsChanged -= Refresh;
    }

    public void Show()
    {
        EnsureUi();
        EnsureFactions();
        Refresh();
        if (ledgerPanel != null)
            ledgerPanel.SetActive(true);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public void Hide()
    {
        if (ledgerPanel != null)
            ledgerPanel.SetActive(false);
    }

    public void Toggle()
    {
        if (IsOpen)
            Hide();
        else
            Show();
    }

    public void Refresh()
    {
        EnsureFactions();
        if (factions == null)
            return;

        if (subtitleText != null)
            subtitleText.text = "Sealed relationships — not shown on the throne room dials.";

        RebuildRows();
    }

    private void EnsureFactions()
    {
        factions = FactionRelationshipManager.Instance != null
            ? FactionRelationshipManager.Instance
            : FindObjectOfType<FactionRelationshipManager>();

        if (factions == null)
            factions = new GameObject("FactionRelationshipManager").AddComponent<FactionRelationshipManager>();
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
    }

    private void RebuildRows()
    {
        if (listContent == null || factions == null)
            return;

        IReadOnlyList<FactionLoyaltyState> states = factions.States;
        Transform emptyHint = listContent.Find("EmptyHint");
        if (emptyHint != null)
            emptyHint.gameObject.SetActive(states.Count == 0);

        int needed = states.Count;
        while (rowPool.Count < needed)
            rowPool.Add(CreateRow(listContent));

        for (int i = 0; i < rowPool.Count; i++)
        {
            if (i >= needed)
            {
                rowPool[i].SetActive(false);
                continue;
            }

            rowPool[i].SetActive(true);
            BindRow(rowPool[i], states[i]);
        }
    }

    private void BindRow(GameObject row, FactionLoyaltyState state)
    {
        if (row == null || state == null)
            return;

        var view = row.GetComponent<FactionRowView>();
        if (view == null)
            view = row.AddComponent<FactionRowView>();
        view.Bind(state);
    }

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (ledgerPanel != null && listContent != null)
        {
            uiBuilt = true;
            WireButtons();
            return;
        }

        if (!buildUiIfMissing)
            return;

        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject(
                "FactionLedgerCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 92;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.GetComponent<RectTransform>();

        // Discreet seal button — bottom-left, away from Dynasty / Quests.
        if (openButton == null)
        {
            var openGo = CreatePanel(
                "FactionsButton",
                canvasRt,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(24f, 120f),
                new Vector2(150f, 64f),
                new Color(0.1f, 0.09f, 0.08f, 0.75f));
            openButton = openGo.AddComponent<Button>();
            CreateLabel(openGo.transform, "◈ Ledger", 22f, FontStyles.Bold);
        }

        var dimGo = CreatePanel(
            "FactionLedger",
            canvasRt,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            new Color(0f, 0f, 0f, 0.78f));
        Stretch(dimGo.GetComponent<RectTransform>());
        ledgerPanel = dimGo;
        if (dimGo.GetComponent<AccessibleBackground>() == null)
            dimGo.AddComponent<AccessibleBackground>();

        var window = CreatePanel(
            "Window",
            dimGo.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(920f, 1280f),
            new Color(0.09f, 0.1f, 0.11f, 0.98f));
        if (window.GetComponent<AccessibleBackground>() == null)
            window.AddComponent<AccessibleBackground>();

        CreateLabel(
            window.transform,
            "Factions",
            40f,
            FontStyles.Bold,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -28f),
            new Vector2(0f, 56f));

        subtitleText = CreateLabel(
            window.transform,
            "Sealed relationships — not shown on the throne room dials.",
            22f,
            FontStyles.Normal,
            new Vector2(0.05f, 1f),
            new Vector2(0.95f, 1f),
            new Vector2(0f, -92f),
            new Vector2(0f, 48f));
        subtitleText.color = new Color(0.75f, 0.72f, 0.65f, 1f);
        subtitleText.alignment = TextAlignmentOptions.Center;

        var closeGo = CreatePanel(
            "Close",
            window.transform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-16f, -16f),
            new Vector2(72f, 72f),
            new Color(0.22f, 0.2f, 0.18f, 1f));
        closeButton = closeGo.AddComponent<Button>();
        CreateLabel(closeGo.transform, "X", 34f, FontStyles.Bold);

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(window.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.05f, 0.06f);
        scrollRt.anchorMax = new Vector2(0.95f, 0.78f);
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;
        scrollGo.GetComponent<Image>().color = new Color(0.07f, 0.07f, 0.08f, 0.9f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 12f;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;

        listContent = contentRt;

        var empty = CreateLabel(
            listContent,
            "No sealed factions recorded.",
            26f,
            FontStyles.Italic,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -20f),
            new Vector2(0f, 60f));
        empty.gameObject.name = "EmptyHint";
        empty.alignment = TextAlignmentOptions.Center;
        empty.color = new Color(0.6f, 0.58f, 0.52f, 1f);

        uiBuilt = true;
        WireButtons();
    }

    private GameObject CreateRow(Transform parent)
    {
        var row = CreatePanel(
            "FactionRow",
            parent,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero,
            new Vector2(0f, rowHeight),
            new Color(0.14f, 0.13f, 0.12f, 1f));

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = rowHeight;
        le.preferredHeight = rowHeight;

        row.AddComponent<FactionRowView>();
        return row;
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
        Vector2? size = null)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin ?? Vector2.zero;
        rt.anchorMax = anchorMax ?? Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos ?? Vector2.zero;
        rt.sizeDelta = size ?? Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.92f, 0.9f, 0.84f, 1f);
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

/// <summary>Row binder for one faction loyalty entry.</summary>
public class FactionRowView : MonoBehaviour
{
    private TextMeshProUGUI nameLabel;
    private TextMeshProUGUI standingLabel;
    private TextMeshProUGUI descLabel;
    private TextMeshProUGUI valueLabel;
    private Image fillImage;
    private bool built;

    public void Bind(FactionLoyaltyState state)
    {
        EnsureBuilt();
        if (state == null)
            return;

        if (nameLabel != null)
        {
            string link = string.IsNullOrWhiteSpace(state.LinkedStat)
                ? string.Empty
                : $"  ·  {state.LinkedStat}";
            nameLabel.text = state.DisplayName + link;
        }

        if (descLabel != null)
            descLabel.text = state.Description;

        if (standingLabel != null)
            standingLabel.text = StandingLabel(state.Standing);

        if (valueLabel != null)
            valueLabel.text = state.loyalty.ToString();

        if (fillImage != null)
        {
            float t = Mathf.Clamp01(state.loyalty / 100f);
            fillImage.rectTransform.anchorMax = new Vector2(t, 1f);
            fillImage.color = StandingColor(state.Standing);
        }
    }

    private void EnsureBuilt()
    {
        if (built)
            return;

        built = true;
        var root = transform as RectTransform;

        nameLabel = CreateTmp(root, "Name", 28f, FontStyles.Bold,
            new Vector2(0.04f, 0.62f), new Vector2(0.7f, 0.95f));
        nameLabel.alignment = TextAlignmentOptions.Left;

        standingLabel = CreateTmp(root, "Standing", 22f, FontStyles.Italic,
            new Vector2(0.7f, 0.62f), new Vector2(0.96f, 0.95f));
        standingLabel.alignment = TextAlignmentOptions.Right;

        descLabel = CreateTmp(root, "Desc", 18f, FontStyles.Normal,
            new Vector2(0.04f, 0.34f), new Vector2(0.96f, 0.62f));
        descLabel.alignment = TextAlignmentOptions.Left;
        descLabel.color = new Color(0.7f, 0.68f, 0.62f, 1f);

        var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(root, false);
        var barRt = barBg.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.04f, 0.1f);
        barRt.anchorMax = new Vector2(0.82f, 0.3f);
        barRt.offsetMin = Vector2.zero;
        barRt.offsetMax = Vector2.zero;
        barBg.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.9f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(barBg.transform, false);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0.5f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(0.55f, 0.7f, 0.45f, 1f);

        valueLabel = CreateTmp(root, "Value", 24f, FontStyles.Bold,
            new Vector2(0.84f, 0.08f), new Vector2(0.96f, 0.32f));
        valueLabel.alignment = TextAlignmentOptions.Center;
    }

    private static TextMeshProUGUI CreateTmp(
        Transform parent,
        string name,
        float size,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.92f, 0.9f, 0.84f, 1f);
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    private static string StandingLabel(FactionStanding standing)
    {
        return standing switch
        {
            FactionStanding.Hostile => "Hostile",
            FactionStanding.Wary => "Wary",
            FactionStanding.Favorable => "Favorable",
            FactionStanding.Devoted => "Devoted",
            _ => "Neutral"
        };
    }

    private static Color StandingColor(FactionStanding standing)
    {
        return standing switch
        {
            FactionStanding.Hostile => new Color(0.75f, 0.28f, 0.25f, 1f),
            FactionStanding.Wary => new Color(0.78f, 0.55f, 0.28f, 1f),
            FactionStanding.Favorable => new Color(0.45f, 0.68f, 0.4f, 1f),
            FactionStanding.Devoted => new Color(0.35f, 0.72f, 0.55f, 1f),
            _ => new Color(0.55f, 0.55f, 0.5f, 1f)
        };
    }
}
