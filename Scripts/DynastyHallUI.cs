using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dynasty Hall — scrollable list of past monarchs with death-cause icons and summary stats.
/// Auto-builds UI if scene references are missing.
/// </summary>
public class DynastyHallUI : MonoBehaviour
{
    [Header("Optional scene wiring")]
    [SerializeField] private GameObject hallPanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI totalKingsText;
    [SerializeField] private TextMeshProUGUI averageReignText;
    [SerializeField] private Transform listContent;
    [SerializeField] private Canvas targetCanvas;

    [Header("Auto UI")]
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private float rowHeight = 120f;

    private readonly List<GameObject> rowPool = new List<GameObject>();
    private bool uiBuilt;
    private DynastyHistoryManager history;

    public bool IsOpen => hallPanel != null && hallPanel.activeSelf;

    private void Awake()
    {
        EnsureHistory();
        if (buildUiIfMissing)
            EnsureUi();
        WireButtons();
        Hide();
    }

    private void OnEnable()
    {
        EnsureHistory();
        if (history != null)
            history.OnHistoryChanged += Refresh;
    }

    private void OnDisable()
    {
        if (history != null)
            history.OnHistoryChanged -= Refresh;
    }

    public void Show()
    {
        EnsureUi();
        EnsureHistory();
        Refresh();
        if (hallPanel != null)
            hallPanel.SetActive(true);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public void Hide()
    {
        if (hallPanel != null)
            hallPanel.SetActive(false);
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
        EnsureHistory();
        if (history == null)
            return;

        int total = history.TotalMonarchs;
        float avg = history.AverageReignDuration;

        if (totalKingsText != null)
            totalKingsText.text = total == 1
                ? "Total Kings Ruled: 1"
                : $"Total Kings Ruled: {total}";

        if (averageReignText != null)
            averageReignText.text = total == 0
                ? "Average Reign: —"
                : $"Average Reign: {avg:0.0} years";

        RebuildRows();
    }

    private void EnsureHistory()
    {
        history = DynastyHistoryManager.Instance != null
            ? DynastyHistoryManager.Instance
            : FindObjectOfType<DynastyHistoryManager>();

        if (history == null)
            history = new GameObject("DynastyHistoryManager").AddComponent<DynastyHistoryManager>();
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
        if (listContent == null || history == null)
            return;

        IReadOnlyList<MonarchRecord> records = history.Records;

        Transform emptyHint = listContent.Find("EmptyHint");
        if (emptyHint != null)
            emptyHint.gameObject.SetActive(records.Count == 0);

        // Newest first.
        int needed = records.Count;
        while (rowPool.Count < needed)
            rowPool.Add(CreateRow(listContent));

        for (int i = 0; i < rowPool.Count; i++)
        {
            if (i >= needed)
            {
                rowPool[i].SetActive(false);
                continue;
            }

            // Display newest at top.
            MonarchRecord record = records[records.Count - 1 - i];
            rowPool[i].SetActive(true);
            BindRow(rowPool[i], record);
        }
    }

    private void BindRow(GameObject row, MonarchRecord record)
    {
        if (row == null || record == null)
            return;

        var view = row.GetComponent<DynastyRowView>();
        if (view == null)
            view = row.AddComponent<DynastyRowView>();
        view.Bind(record);
    }

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (hallPanel != null && listContent != null && totalKingsText != null)
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
            var canvasGo = new GameObject("DynastyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 95;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.GetComponent<RectTransform>();

        if (openButton == null)
        {
            var openGo = CreatePanel(
                "DynastyButton",
                canvasRt,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(200f, -24f),
                new Vector2(170f, 72f),
                new Color(0.12f, 0.11f, 0.1f, 0.9f));
            openButton = openGo.AddComponent<Button>();
            CreateLabel(openGo.transform, "Dynasty", 24f, FontStyles.Bold);
        }

        var dimGo = CreatePanel(
            "DynastyHall",
            canvasRt,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            new Color(0f, 0f, 0f, 0.72f));
        Stretch(dimGo.GetComponent<RectTransform>());
        hallPanel = dimGo;
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
            new Color(0.1f, 0.09f, 0.08f, 0.98f));
        if (window.GetComponent<AccessibleBackground>() == null)
            window.AddComponent<AccessibleBackground>();

        CreateLabel(window.transform, "Dynasty Hall", 40f, FontStyles.Bold,
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

        // Summary strip
        var summary = CreatePanel(
            "Summary",
            window.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -100f),
            new Vector2(840f, 120f),
            new Color(0.14f, 0.13f, 0.12f, 1f));

        totalKingsText = CreateLabel(
            summary.transform,
            "Total Kings Ruled: 0",
            28f,
            FontStyles.Bold,
            new Vector2(0f, 0.55f),
            new Vector2(1f, 1f),
            Vector2.zero,
            Vector2.zero,
            TextAlignmentOptions.Center);

        averageReignText = CreateLabel(
            summary.transform,
            "Average Reign: —",
            26f,
            FontStyles.Normal,
            new Vector2(0f, 0f),
            new Vector2(1f, 0.55f),
            Vector2.zero,
            Vector2.zero,
            TextAlignmentOptions.Center);
        averageReignText.color = new Color(0.82f, 0.78f, 0.7f, 1f);

        // Scroll list
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(window.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(28f, 28f);
        scrollRt.offsetMax = new Vector2(-28f, -240f);
        scrollGo.GetComponent<Image>().color = new Color(0.07f, 0.06f, 0.05f, 1f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(scrollRt, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        Stretch(viewportRt);
        viewportGo.GetComponent<Image>().color = Color.white;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportRt, false);
        listContent = contentGo.transform;
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var empty = CreateLabel(
            contentGo.transform,
            "No monarchs yet.\nFinish a reign to begin the dynasty.",
            24f,
            FontStyles.Normal,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0f, 160f),
            TextAlignmentOptions.Center);
        empty.color = new Color(0.7f, 0.66f, 0.6f, 1f);
        empty.gameObject.name = "EmptyHint";
        var emptyLe = empty.gameObject.AddComponent<LayoutElement>();
        emptyLe.preferredHeight = 160f;

        WireButtons();
        uiBuilt = true;
    }

    private GameObject CreateRow(Transform parent)
    {
        var row = CreatePanel(
            "MonarchRow",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(0f, rowHeight),
            new Color(0.16f, 0.14f, 0.12f, 1f));

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;
        le.minHeight = rowHeight;

        var view = row.AddComponent<DynastyRowView>();
        view.Build();
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

/// <summary>Bound view for a single monarch row in Dynasty Hall.</summary>
public class DynastyRowView : MonoBehaviour
{
    private Image iconBackground;
    private TextMeshProUGUI iconGlyph;
    private TextMeshProUGUI nameLabel;
    private TextMeshProUGUI yearsLabel;
    private TextMeshProUGUI causeLabel;
    private TextMeshProUGUI dateLabel;
    private bool built;

    public void Build()
    {
        if (built)
            return;

        iconBackground = CreateIcon(transform);
        iconGlyph = iconBackground.GetComponentInChildren<TextMeshProUGUI>();

        nameLabel = CreateTmp(transform, "Name", 28f, FontStyles.Bold,
            new Vector2(0f, 0.55f), new Vector2(1f, 1f), new Vector2(120f, -8f), new Vector2(-140f, 0f));
        yearsLabel = CreateTmp(transform, "Years", 24f, FontStyles.Normal,
            new Vector2(0f, 0.28f), new Vector2(0.55f, 0.55f), new Vector2(120f, 0f), new Vector2(-20f, 0f));
        yearsLabel.color = new Color(0.85f, 0.8f, 0.7f, 1f);
        causeLabel = CreateTmp(transform, "Cause", 22f, FontStyles.Normal,
            new Vector2(0.45f, 0.28f), new Vector2(1f, 0.55f), new Vector2(0f, 0f), new Vector2(-24f, 0f));
        causeLabel.color = new Color(0.8f, 0.75f, 0.68f, 1f);
        dateLabel = CreateTmp(transform, "Date", 20f, FontStyles.Normal,
            new Vector2(0f, 0f), new Vector2(1f, 0.28f), new Vector2(120f, 6f), new Vector2(-24f, 0f));
        dateLabel.color = new Color(0.65f, 0.62f, 0.56f, 1f);

        built = true;
    }

    public void Bind(MonarchRecord record)
    {
        Build();
        if (record == null)
            return;

        DeathCause cause = record.GetDeathCause();
        nameLabel.text = record.monarchName;
        yearsLabel.text = record.yearsRuled == 1
            ? "Ruled 1 year"
            : $"Ruled {record.yearsRuled} years";
        causeLabel.text = DynastyHistoryManager.FormatDeathCauseShort(cause);

        DateTime utc = record.GetDeathDateUtc();
        dateLabel.text = utc == DateTime.MinValue
            ? ""
            : utc.ToLocalTime().ToString("dd MMM yyyy");

        if (iconBackground != null)
            iconBackground.color = DynastyHistoryManager.GetDeathIconColor(cause);
        if (iconGlyph != null)
            iconGlyph.text = DynastyHistoryManager.GetDeathIconGlyph(cause);
    }

    private static Image CreateIcon(Transform parent)
    {
        var go = new GameObject("DeathIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(72f, 72f);
        rt.anchoredPosition = new Vector2(20f, 0f);
        var image = go.GetComponent<Image>();
        image.color = Color.gray;

        var labelGo = new GameObject("Glyph", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 26f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.08f, 0.07f, 0.06f, 1f);
        tmp.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateTmp(
        Transform parent,
        string name,
        float size,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        tmp.raycastTarget = false;
        return tmp;
    }
}
