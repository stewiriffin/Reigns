using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slide-out quest drawer + completion banner for <see cref="QuestManager"/>.
/// Auto-builds UI when scene references are missing.
/// </summary>
public class QuestUI : MonoBehaviour
{
    [Header("Optional scene wiring")]
    [SerializeField] private RectTransform drawerRoot;
    [SerializeField] private CanvasGroup drawerGroup;
    [SerializeField] private Button toggleButton;
    [SerializeField] private Transform listContent;
    [SerializeField] private RectTransform bannerRoot;
    [SerializeField] private CanvasGroup bannerGroup;
    [SerializeField] private TextMeshProUGUI bannerTitle;
    [SerializeField] private TextMeshProUGUI bannerBody;
    [SerializeField] private Canvas targetCanvas;

    [Header("Motion")]
    [SerializeField] private float drawerSlideDuration = 0.28f;
    [SerializeField] private float bannerDuration = 2.8f;
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private float drawerWidth = 420f;

    private readonly List<QuestRowView> rows = new List<QuestRowView>();
    private readonly Queue<ActiveQuest> bannerQueue = new Queue<ActiveQuest>();
    private bool uiBuilt;
    private bool drawerOpen;
    private bool bannerPlaying;
    private Coroutine drawerRoutine;
    private Coroutine bannerRoutine;
    private QuestManager quests;

    public bool IsDrawerOpen => drawerOpen;

    private void Awake()
    {
        EnsureQuests();
        if (buildUiIfMissing)
            EnsureUi();
        Wire();
        HideDrawerImmediate();
        HideBannerImmediate();
    }

    private void OnEnable()
    {
        EnsureQuests();
        if (quests != null)
        {
            quests.OnActiveQuestsChanged -= RefreshDrawer;
            quests.OnActiveQuestsChanged += RefreshDrawer;
            quests.OnQuestCompleted -= EnqueueBanner;
            quests.OnQuestCompleted += EnqueueBanner;
        }
    }

    private void OnDisable()
    {
        if (quests != null)
        {
            quests.OnActiveQuestsChanged -= RefreshDrawer;
            quests.OnQuestCompleted -= EnqueueBanner;
        }
    }

    public void ToggleDrawer()
    {
        if (drawerOpen)
            CloseDrawer();
        else
            OpenDrawer();
    }

    public void OpenDrawer()
    {
        EnsureUi();
        RefreshDrawer();
        drawerOpen = true;
        if (drawerRoutine != null)
            StopCoroutine(drawerRoutine);
        drawerRoutine = StartCoroutine(SlideDrawer(true));

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public void CloseDrawer()
    {
        drawerOpen = false;
        if (drawerRoutine != null)
            StopCoroutine(drawerRoutine);
        drawerRoutine = StartCoroutine(SlideDrawer(false));
    }

    public void RefreshDrawer()
    {
        EnsureUi();
        EnsureQuests();
        if (listContent == null || quests == null)
            return;

        IReadOnlyList<ActiveQuest> active = quests.ActiveQuests;
        while (rows.Count < active.Count)
            rows.Add(CreateRow(listContent));

        for (int i = 0; i < rows.Count; i++)
        {
            if (i >= active.Count)
            {
                rows[i].gameObject.SetActive(false);
                continue;
            }

            rows[i].gameObject.SetActive(true);
            rows[i].Bind(active[i]);
        }
    }

    public void ShowCompletionBanner(ActiveQuest quest)
    {
        if (quest == null)
            return;

        bannerQueue.Enqueue(quest);
        if (!bannerPlaying)
            bannerRoutine = StartCoroutine(PlayBannerQueue());
    }

    private void EnqueueBanner(ActiveQuest quest) => ShowCompletionBanner(quest);

    private IEnumerator PlayBannerQueue()
    {
        bannerPlaying = true;
        EnsureUi();

        while (bannerQueue.Count > 0)
        {
            ActiveQuest quest = bannerQueue.Dequeue();
            if (quest?.definition == null)
                continue;

            if (bannerRoot != null)
                bannerRoot.gameObject.SetActive(true);

            if (bannerTitle != null)
                bannerTitle.text = "Quest Complete";

            if (bannerBody != null)
            {
                string reward = string.IsNullOrWhiteSpace(quest.definition.rewardDescription)
                    ? quest.definition.description
                    : quest.definition.description + "\n" + quest.definition.rewardDescription;
                bannerBody.text = reward;
            }

            if (bannerGroup != null)
            {
                bannerGroup.alpha = 0f;
                float fadeIn = 0.25f;
                float elapsed = 0f;
                while (elapsed < fadeIn)
                {
                    elapsed += Time.unscaledDeltaTime;
                    bannerGroup.alpha = Mathf.Clamp01(elapsed / fadeIn);
                    yield return null;
                }

                bannerGroup.alpha = 1f;
                yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, bannerDuration));

                float fadeOut = 0.35f;
                elapsed = 0f;
                while (elapsed < fadeOut)
                {
                    elapsed += Time.unscaledDeltaTime;
                    bannerGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
                    yield return null;
                }

                bannerGroup.alpha = 0f;
            }
            else
            {
                yield return new WaitForSecondsRealtime(bannerDuration);
            }
        }

        HideBannerImmediate();
        bannerPlaying = false;
        bannerRoutine = null;
    }

    private IEnumerator SlideDrawer(bool open)
    {
        if (drawerRoot == null || drawerGroup == null)
            yield break;

        drawerRoot.gameObject.SetActive(true);
        drawerGroup.blocksRaycasts = open;
        drawerGroup.interactable = open;

        Vector2 hidden = new Vector2(drawerWidth + 24f, drawerRoot.anchoredPosition.y);
        Vector2 shown = new Vector2(0f, drawerRoot.anchoredPosition.y);
        Vector2 from = open ? hidden : drawerRoot.anchoredPosition;
        Vector2 to = open ? shown : hidden;
        float fromAlpha = open ? 0f : drawerGroup.alpha;
        float toAlpha = open ? 1f : 0f;

        float duration = Mathf.Max(0.05f, drawerSlideDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            drawerRoot.anchoredPosition = Vector2.Lerp(from, to, t);
            drawerGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }

        drawerRoot.anchoredPosition = to;
        drawerGroup.alpha = toAlpha;
        if (!open)
            drawerRoot.gameObject.SetActive(false);

        drawerRoutine = null;
    }

    private void HideDrawerImmediate()
    {
        drawerOpen = false;
        if (drawerRoot != null)
        {
            drawerRoot.anchoredPosition = new Vector2(drawerWidth + 24f, drawerRoot.anchoredPosition.y);
            drawerRoot.gameObject.SetActive(false);
        }

        if (drawerGroup != null)
        {
            drawerGroup.alpha = 0f;
            drawerGroup.blocksRaycasts = false;
            drawerGroup.interactable = false;
        }
    }

    private void HideBannerImmediate()
    {
        if (bannerRoot != null)
            bannerRoot.gameObject.SetActive(false);
        if (bannerGroup != null)
            bannerGroup.alpha = 0f;
    }

    private void EnsureQuests()
    {
        quests = QuestManager.Instance != null
            ? QuestManager.Instance
            : FindObjectOfType<QuestManager>();
    }

    private void Wire()
    {
        if (toggleButton == null)
            return;

        toggleButton.onClick.RemoveListener(ToggleDrawer);
        toggleButton.onClick.AddListener(ToggleDrawer);
    }

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (drawerRoot != null && listContent != null)
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
            var canvasGo = new GameObject("QuestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 85;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.GetComponent<RectTransform>();

        if (toggleButton == null)
        {
            var toggleGo = CreatePanel(
                "QuestsButton",
                canvasRt,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-12f, 120f),
                new Vector2(120f, 120f),
                new Color(0.12f, 0.14f, 0.16f, 0.92f));
            toggleButton = toggleGo.AddComponent<Button>();
            CreateLabel(toggleGo.transform, "Quests", 22f, FontStyles.Bold);
        }

        var drawerGo = CreatePanel(
            "QuestDrawer",
            canvasRt,
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0.5f),
            new Vector2(drawerWidth + 24f, 0f),
            new Vector2(drawerWidth, 0f),
            new Color(0.08f, 0.07f, 0.06f, 0.96f));
        drawerRoot = drawerGo.GetComponent<RectTransform>();
        drawerRoot.offsetMin = new Vector2(-drawerWidth, 80f);
        drawerRoot.offsetMax = new Vector2(0f, -80f);
        drawerRoot.sizeDelta = new Vector2(drawerWidth, drawerRoot.sizeDelta.y);
        drawerGroup = drawerGo.AddComponent<CanvasGroup>();
        if (drawerGo.GetComponent<AccessibleBackground>() == null)
            drawerGo.AddComponent<AccessibleBackground>();

        CreateLabel(drawerRoot, "Objectives", 34f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -20f), new Vector2(-24f, 48f));

        var closeGo = CreatePanel(
            "Close",
            drawerRoot,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(16f, -16f),
            new Vector2(64f, 64f),
            new Color(0.25f, 0.18f, 0.15f, 1f));
        var closeBtn = closeGo.AddComponent<Button>();
        closeBtn.onClick.AddListener(CloseDrawer);
        CreateLabel(closeGo.transform, "X", 28f, FontStyles.Bold);

        var listGo = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listGo.transform.SetParent(drawerRoot, false);
        listContent = listGo.transform;
        var listRt = listGo.GetComponent<RectTransform>();
        listRt.anchorMin = new Vector2(0f, 0f);
        listRt.anchorMax = new Vector2(1f, 1f);
        listRt.offsetMin = new Vector2(16f, 16f);
        listRt.offsetMax = new Vector2(-16f, -80f);

        var vlg = listGo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        listGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Banner (top overlay)
        var bannerGo = CreatePanel(
            "QuestBanner",
            canvasRt,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -40f),
            new Vector2(920f, 150f),
            new Color(0.12f, 0.2f, 0.14f, 0.96f));
        bannerRoot = bannerGo.GetComponent<RectTransform>();
        bannerGroup = bannerGo.AddComponent<CanvasGroup>();
        bannerGroup.blocksRaycasts = false;
        bannerTitle = CreateLabel(bannerRoot, "Quest Complete", 32f, FontStyles.Bold,
            new Vector2(0f, 0.55f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        bannerBody = CreateLabel(bannerRoot, "", 24f, FontStyles.Normal,
            new Vector2(0f, 0f), new Vector2(1f, 0.55f), Vector2.zero, Vector2.zero);
        bannerBody.color = new Color(0.85f, 0.9f, 0.82f, 1f);

        Wire();
        uiBuilt = true;
        HideBannerImmediate();
    }

    private QuestRowView CreateRow(Transform parent)
    {
        var row = CreatePanel(
            "QuestRow",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(0f, 130f),
            new Color(0.15f, 0.14f, 0.12f, 1f));
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 130f;
        le.preferredHeight = 130f;
        var view = row.AddComponent<QuestRowView>();
        view.Build();
        return view;
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
        Vector2? sizeDelta = null)
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
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }
}

/// <summary>One row inside the quest drawer.</summary>
public class QuestRowView : MonoBehaviour
{
    private TextMeshProUGUI description;
    private TextMeshProUGUI progress;
    private Image fill;
    private bool built;

    public void Build()
    {
        if (built)
            return;

        description = CreateTmp(transform, "Description", 22f, FontStyles.Normal,
            new Vector2(0f, 0.4f), new Vector2(1f, 1f), new Vector2(14f, -8f), new Vector2(-14f, -8f));
        description.alignment = TextAlignmentOptions.TopLeft;

        progress = CreateTmp(transform, "Progress", 20f, FontStyles.Bold,
            new Vector2(0f, 0f), new Vector2(1f, 0.35f), new Vector2(14f, 8f), new Vector2(-14f, 0f));
        progress.alignment = TextAlignmentOptions.Left;

        var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(transform, false);
        var bgRt = barBg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.12f);
        bgRt.anchorMax = new Vector2(1f, 0.22f);
        bgRt.offsetMin = new Vector2(14f, 0f);
        bgRt.offsetMax = new Vector2(-14f, 0f);
        barBg.GetComponent<Image>().color = new Color(0.25f, 0.24f, 0.22f, 1f);

        var barFill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barBg.transform, false);
        var fillRt = barFill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        fill = barFill.GetComponent<Image>();
        fill.color = new Color(0.72f, 0.58f, 0.32f, 1f);

        built = true;
    }

    public void Bind(ActiveQuest quest)
    {
        Build();
        if (quest?.definition == null)
            return;

        description.text = quest.definition.description;
        progress.text = quest.ProgressLabel;
        progress.color = quest.failed
            ? new Color(0.9f, 0.4f, 0.35f, 1f)
            : quest.completed
                ? new Color(0.55f, 0.85f, 0.5f, 1f)
                : new Color(0.85f, 0.8f, 0.7f, 1f);

        float t = quest.NormalizedProgress;
        if (fill != null)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }
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
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }
}
