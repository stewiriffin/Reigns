using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Collectible Legendary Ending badges for Dynasty Hall and/or Start Menu.
/// Locked badges show a dimmed glyph; unlocked show title on tap/refresh.
/// </summary>
public class LegendaryEndingsUI : MonoBehaviour
{
    [Header("Optional wiring")]
    [SerializeField] private Transform badgeRow;
    [SerializeField] private TextMeshProUGUI summaryLabel;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private bool attachToDynastyHall = true;
    [SerializeField] private bool attachToStartMenu = true;

    private readonly List<BadgeView> badges = new List<BadgeView>(8);
    private bool uiBuilt;
    private StoryArcManager arcs;

    private struct BadgeView
    {
        public GameObject root;
        public Image frame;
        public TextMeshProUGUI glyph;
        public TextMeshProUGUI title;
        public string arcId;
    }

    private void Awake()
    {
        EnsureArcs();
        if (buildUiIfMissing)
            EnsureUi();
        Refresh();
    }

    private void OnEnable()
    {
        EnsureArcs();
        if (arcs != null)
            arcs.OnEndingUnlocked += HandleEndingUnlocked;
        if (arcs != null)
            arcs.OnProgressChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (arcs != null)
        {
            arcs.OnEndingUnlocked -= HandleEndingUnlocked;
            arcs.OnProgressChanged -= Refresh;
        }
    }

    private void HandleEndingUnlocked(StoryArcDefinition _)
    {
        Refresh();
    }

    public void Refresh()
    {
        EnsureArcs();
        EnsureUi();
        if (arcs == null)
            return;

        IReadOnlyList<StoryArcProgress> list = arcs.Arcs;
        while (badges.Count < list.Count)
            badges.Add(CreateBadge(badgeRow));

        int unlocked = 0;
        for (int i = 0; i < badges.Count; i++)
        {
            if (i >= list.Count)
            {
                badges[i].root.SetActive(false);
                continue;
            }

            StoryArcProgress state = list[i];
            StoryArcDefinition def = state.definition;
            bool isUnlocked = arcs.IsEndingUnlocked(state.arcId);
            if (isUnlocked)
                unlocked++;

            BadgeView view = badges[i];
            view.root.SetActive(true);
            view.arcId = state.arcId;

            if (view.glyph != null)
            {
                view.glyph.text = def != null && !string.IsNullOrEmpty(def.badgeGlyph)
                    ? def.badgeGlyph
                    : "◆";
                view.glyph.color = isUnlocked
                    ? new Color(0.95f, 0.88f, 0.55f, 1f)
                    : new Color(0.35f, 0.33f, 0.3f, 0.85f);
            }

            if (view.title != null)
            {
                view.title.text = isUnlocked
                    ? (def != null ? def.displayName : state.arcId)
                    : "???";
                view.title.color = isUnlocked
                    ? new Color(0.9f, 0.86f, 0.75f, 1f)
                    : new Color(0.45f, 0.42f, 0.38f, 1f);
            }

            if (view.frame != null)
            {
                view.frame.color = isUnlocked
                    ? new Color(0.35f, 0.28f, 0.12f, 0.95f)
                    : new Color(0.12f, 0.11f, 0.1f, 0.8f);
            }

            badges[i] = view;
        }

        if (summaryLabel != null)
        {
            summaryLabel.text = list.Count == 0
                ? "Legendary Endings"
                : $"Legendary Endings  {unlocked}/{list.Count}";
        }
    }

    private void EnsureArcs()
    {
        arcs = StoryArcManager.Instance != null
            ? StoryArcManager.Instance
            : FindObjectOfType<StoryArcManager>();

        if (arcs == null)
            arcs = new GameObject("StoryArcManager").AddComponent<StoryArcManager>();
    }

    private void EnsureUi()
    {
        if (uiBuilt && badgeRow != null)
            return;

        if (badgeRow != null)
        {
            uiBuilt = true;
            return;
        }

        if (!buildUiIfMissing)
            return;

        if (attachToDynastyHall)
            TryAttachToDynastyHall();

        if (badgeRow == null && attachToStartMenu)
            TryAttachToStartMenu();

        if (badgeRow == null)
            BuildStandalonePanel();

        uiBuilt = true;
    }

    private void TryAttachToDynastyHall()
    {
        var hall = FindObjectOfType<DynastyHallUI>();
        if (hall == null)
            return;

        // Prefer parenting under the Dynasty window if it exists.
        Transform hallPanel = hall.transform;
        GameObject panel = GameObject.Find("DynastyHall");
        if (panel != null)
        {
            Transform window = panel.transform.Find("Window");
            if (window != null)
                hallPanel = window;
        }

        var section = new GameObject("LegendaryEndings", typeof(RectTransform));
        section.transform.SetParent(hallPanel, false);
        var sectionRt = section.GetComponent<RectTransform>();
        sectionRt.anchorMin = new Vector2(0.05f, 0f);
        sectionRt.anchorMax = new Vector2(0.95f, 0f);
        sectionRt.pivot = new Vector2(0.5f, 0f);
        sectionRt.anchoredPosition = new Vector2(0f, 36f);
        sectionRt.sizeDelta = new Vector2(0f, 150f);

        // Shrink monarch scroll if present so badges fit.
        Transform scroll = hallPanel.Find("Scroll");
        if (scroll != null)
        {
            var scrollRt = scroll as RectTransform;
            if (scrollRt != null)
                scrollRt.offsetMin = new Vector2(scrollRt.offsetMin.x, 200f);
        }

        summaryLabel = CreateLabel(
            section.transform,
            "Legendary Endings  0/5",
            22f,
            FontStyles.Bold,
            new Vector2(0f, 0.72f),
            new Vector2(1f, 1f));

        var row = new GameObject("BadgeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(section.transform, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 0f);
        rowRt.anchorMax = new Vector2(1f, 0.72f);
        rowRt.offsetMin = Vector2.zero;
        rowRt.offsetMax = Vector2.zero;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.padding = new RectOffset(4, 4, 4, 4);

        badgeRow = row.transform;
    }

    private void TryAttachToStartMenu()
    {
        if (UIFadeTransition.Instance == null)
            return;

        // Start menu CanvasGroup may live on a scene object — find a child named StartMenu.
        GameObject start = GameObject.Find("StartMenu");
        if (start == null)
            start = GameObject.Find("Start Menu");

        Transform parent = start != null ? start.transform : null;
        if (parent == null)
        {
            if (targetCanvas == null)
                targetCanvas = FindObjectOfType<Canvas>();
            if (targetCanvas == null)
                return;
            parent = targetCanvas.transform;
        }

        var strip = new GameObject("StartMenuEndings", typeof(RectTransform));
        strip.transform.SetParent(parent, false);
        var stripRt = strip.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0.5f, 0f);
        stripRt.anchorMax = new Vector2(0.5f, 0f);
        stripRt.pivot = new Vector2(0.5f, 0f);
        stripRt.anchoredPosition = new Vector2(0f, 48f);
        stripRt.sizeDelta = new Vector2(900f, 140f);

        summaryLabel = CreateLabel(
            strip.transform,
            "Legendary Endings  0/5",
            22f,
            FontStyles.Bold,
            new Vector2(0f, 0.7f),
            new Vector2(1f, 1f));

        var row = new GameObject("BadgeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(strip.transform, false);
        Stretch(row.GetComponent<RectTransform>());
        row.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
        row.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0.7f);

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        badgeRow = row.transform;
    }

    private void BuildStandalonePanel()
    {
        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject(
                "LegendaryEndingsCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 50;
        }

        TryAttachToStartMenu();
        if (badgeRow == null)
        {
            var row = new GameObject("BadgeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(targetCanvas.transform, false);
            badgeRow = row.transform;
        }
    }

    private BadgeView CreateBadge(Transform parent)
    {
        if (parent == null)
            parent = transform;

        var go = new GameObject("EndingBadge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 96f;
        le.flexibleWidth = 1f;

        var frame = go.GetComponent<Image>();
        frame.color = new Color(0.12f, 0.11f, 0.1f, 0.8f);

        var glyph = CreateLabel(go.transform, "◆", 34f, FontStyles.Bold,
            new Vector2(0f, 0.35f), new Vector2(1f, 1f));
        var title = CreateLabel(go.transform, "???", 14f, FontStyles.Normal,
            new Vector2(0.02f, 0f), new Vector2(0.98f, 0.38f));
        title.enableAutoSizing = true;
        title.fontSizeMin = 10f;
        title.fontSizeMax = 14f;

        return new BadgeView
        {
            root = go,
            frame = frame,
            glyph = glyph,
            title = title
        };
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent,
        string text,
        float size,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.9f, 0.86f, 0.75f, 1f);
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
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
