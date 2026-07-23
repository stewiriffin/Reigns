using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks reign milestones, persists unlocks, shows a top toast, and hosts a
/// scrollable achievements panel.
/// </summary>
public class AchievementManager : MonoBehaviour
{
    public const string IdSurvive10Years = "survive_10_years";
    public const string IdDieArmyEmpty = "die_army_empty";
    public const string IdUnlock10Cards = "unlock_10_cards";
    public const string IdMaxWealth = "max_wealth";

    private const string PrefsPrefix = "Achievement_";
    private const string SeenCardsPrefsKey = "Achievement_SeenCardIds";
    private const string ResourcesFolder = "Achievements";

    public static AchievementManager Instance { get; private set; }

    [Header("Catalog")]
    [SerializeField] private List<Achievement> achievements = new List<Achievement>();

    [Header("Optional scene UI (auto-built if empty)")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private RectTransform toastRoot;
    [SerializeField] private TextMeshProUGUI toastTitle;
    [SerializeField] private TextMeshProUGUI toastDescription;
    [SerializeField] private Image toastIcon;
    [SerializeField] private CanvasGroup toastGroup;
    [SerializeField] private GameObject achievementsPanel;
    [SerializeField] private Transform achievementsContent;
    [SerializeField] private Button openPanelButton;
    [SerializeField] private Button closePanelButton;

    [Header("Toast")]
    [SerializeField] private float toastDuration = 3.2f;
    [SerializeField] private float toastFadeDuration = 0.35f;

    private readonly HashSet<string> seenCardIds = new HashSet<string>();
    private readonly Queue<Achievement> toastQueue = new Queue<Achievement>();
    private readonly List<AchievementRowView> rowViews = new List<AchievementRowView>();
    private bool toastPlaying;
    private bool uiBuilt;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        EnsureCatalog();
        LoadUnlockStates();
        LoadSeenCards();
        EnsureUi();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Call after YearsRuled increments (non-tutorial).</summary>
    public void NotifyYearsRuled(int years)
    {
        if (years >= 10)
            Unlock(IdSurvive10Years);
    }

    /// <summary>Call when a run ends with a death cause.</summary>
    public void NotifyDeath(DeathCause cause)
    {
        if (cause == DeathCause.ArmyEmpty)
            Unlock(IdDieArmyEmpty);

        // Wealth at 100 also ends the run — still award the max-wealth milestone.
        if (cause == DeathCause.WealthFull)
            Unlock(IdMaxWealth);
    }

    /// <summary>Call when Wealth reaches the maximum (100).</summary>
    public void NotifyWealthMaxed(int wealth)
    {
        if (wealth >= 100)
            Unlock(IdMaxWealth);
    }

    /// <summary>Call when a card is displayed so unique cards can be counted.</summary>
    public void NotifyCardSeen(Card card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.id))
            return;

        if (!seenCardIds.Add(card.id.Trim()))
            return;

        SaveSeenCards();

        if (seenCardIds.Count >= 10)
            Unlock(IdUnlock10Cards);
    }

    public void Unlock(string achievementId)
    {
        Achievement achievement = FindById(achievementId);
        if (achievement == null || achievement.IsUnlocked)
            return;

        achievement.IsUnlocked = true;
        PlayerPrefs.SetInt(PrefsKey(achievement.id), 1);
        PlayerPrefs.Save();

        RefreshPanelRows();
        EnqueueToast(achievement);
        Debug.Log($"Achievement unlocked: {achievement.title}");
    }

    public void ToggleAchievementsPanel()
    {
        if (achievementsPanel == null)
            return;

        bool show = !achievementsPanel.activeSelf;
        achievementsPanel.SetActive(show);
        if (show)
            RefreshPanelRows();
    }

    public void ShowAchievementsPanel(bool show)
    {
        if (achievementsPanel == null)
            return;

        achievementsPanel.SetActive(show);
        if (show)
            RefreshPanelRows();
    }

    public IReadOnlyList<Achievement> GetAchievements() => achievements;

    private void EnsureCatalog()
    {
        if (achievements == null)
            achievements = new List<Achievement>();

        achievements.RemoveAll(a => a == null);

        if (achievements.Count == 0)
        {
            Achievement[] loaded = Resources.LoadAll<Achievement>(ResourcesFolder);
            if (loaded != null && loaded.Length > 0)
                achievements.AddRange(loaded);
        }

        if (achievements.Count == 0)
            CreateRuntimeDefaults();

        SortCatalog();
    }

    private void CreateRuntimeDefaults()
    {
        achievements.Add(CreateRuntime(
            IdSurvive10Years,
            "Decade on the Throne",
            "Survive for 10 years."));
        achievements.Add(CreateRuntime(
            IdDieArmyEmpty,
            "Defenseless",
            "Die with Army at 0."));
        achievements.Add(CreateRuntime(
            IdUnlock10Cards,
            "Court Chronicles",
            "Unlock 10 cards."));
        achievements.Add(CreateRuntime(
            IdMaxWealth,
            "Overflowing Coffers",
            "Max out the Wealth stat."));
    }

    private static Achievement CreateRuntime(string id, string title, string description)
    {
        Achievement a = ScriptableObject.CreateInstance<Achievement>();
        a.id = id;
        a.title = title;
        a.description = description;
        a.name = id;
        return a;
    }

    private void SortCatalog()
    {
        achievements.Sort((a, b) =>
        {
            int ia = CatalogOrder(a != null ? a.id : null);
            int ib = CatalogOrder(b != null ? b.id : null);
            return ia.CompareTo(ib);
        });
    }

    private static int CatalogOrder(string id)
    {
        switch (id)
        {
            case IdSurvive10Years: return 0;
            case IdDieArmyEmpty: return 1;
            case IdUnlock10Cards: return 2;
            case IdMaxWealth: return 3;
            default: return 100;
        }
    }

    private void LoadUnlockStates()
    {
        for (int i = 0; i < achievements.Count; i++)
        {
            Achievement a = achievements[i];
            if (a == null || string.IsNullOrWhiteSpace(a.id))
                continue;

            a.IsUnlocked = PlayerPrefs.GetInt(PrefsKey(a.id), 0) == 1;
        }
    }

    private void LoadSeenCards()
    {
        seenCardIds.Clear();
        string raw = PlayerPrefs.GetString(SeenCardsPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string[] parts = raw.Split('|');
        for (int i = 0; i < parts.Length; i++)
        {
            string id = parts[i].Trim();
            if (!string.IsNullOrEmpty(id))
                seenCardIds.Add(id);
        }
    }

    private void SaveSeenCards()
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (string id in seenCardIds)
        {
            if (!first)
                sb.Append('|');
            sb.Append(id);
            first = false;
        }

        PlayerPrefs.SetString(SeenCardsPrefsKey, sb.ToString());
        PlayerPrefs.Save();
    }

    private Achievement FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        for (int i = 0; i < achievements.Count; i++)
        {
            Achievement a = achievements[i];
            if (a != null && a.id == id)
                return a;
        }

        return null;
    }

    private static string PrefsKey(string id) => PrefsPrefix + id.Trim();

    private void EnqueueToast(Achievement achievement)
    {
        toastQueue.Enqueue(achievement);
        if (!toastPlaying)
            StartCoroutine(PlayToastQueue());
    }

    private IEnumerator PlayToastQueue()
    {
        toastPlaying = true;

        while (toastQueue.Count > 0)
        {
            Achievement next = toastQueue.Dequeue();
            yield return ShowToast(next);
        }

        toastPlaying = false;
    }

    private IEnumerator ShowToast(Achievement achievement)
    {
        if (toastRoot == null || toastGroup == null)
            yield break;

        if (toastTitle != null)
            toastTitle.text = achievement.title;
        if (toastDescription != null)
            toastDescription.text = achievement.description;
        if (toastIcon != null)
        {
            toastIcon.sprite = achievement.icon;
            toastIcon.enabled = achievement.icon != null;
            toastIcon.color = achievement.icon != null
                ? Color.white
                : new Color(1f, 1f, 1f, 0.15f);
        }

        toastRoot.gameObject.SetActive(true);
        toastGroup.alpha = 0f;

        float t = 0f;
        while (t < toastFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            toastGroup.alpha = Mathf.Clamp01(t / toastFadeDuration);
            yield return null;
        }

        toastGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(toastDuration);

        t = 0f;
        while (t < toastFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            toastGroup.alpha = 1f - Mathf.Clamp01(t / toastFadeDuration);
            yield return null;
        }

        toastGroup.alpha = 0f;
        toastRoot.gameObject.SetActive(false);
    }

    private void RefreshPanelRows()
    {
        for (int i = 0; i < rowViews.Count; i++)
            rowViews[i].Refresh();
    }

    private void EnsureUi()
    {
        if (uiBuilt)
            return;

        if (uiCanvas == null)
        {
            uiCanvas = FindObjectOfType<Canvas>();
            if (uiCanvas == null)
            {
                var canvasGo = new GameObject("AchievementCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                uiCanvas = canvasGo.GetComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCanvas.sortingOrder = 80;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        if (toastRoot == null || toastGroup == null)
            BuildToastUi();

        if (achievementsPanel == null || achievementsContent == null)
            BuildPanelUi();

        if (openPanelButton != null)
        {
            openPanelButton.onClick.RemoveListener(ToggleAchievementsPanel);
            openPanelButton.onClick.AddListener(ToggleAchievementsPanel);
        }

        if (closePanelButton != null)
        {
            closePanelButton.onClick.RemoveListener(ClosePanel);
            closePanelButton.onClick.AddListener(ClosePanel);
        }

        if (achievementsPanel != null)
            achievementsPanel.SetActive(false);

        if (toastRoot != null)
            toastRoot.gameObject.SetActive(false);

        uiBuilt = true;
    }

    private void ClosePanel() => ShowAchievementsPanel(false);

    private void BuildToastUi()
    {
        RectTransform canvasRt = uiCanvas.GetComponent<RectTransform>();

        var toastGo = new GameObject("AchievementToast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        toastGo.transform.SetParent(canvasRt, false);
        toastRoot = toastGo.GetComponent<RectTransform>();
        toastGroup = toastGo.GetComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;

        toastRoot.anchorMin = new Vector2(0.5f, 1f);
        toastRoot.anchorMax = new Vector2(0.5f, 1f);
        toastRoot.pivot = new Vector2(0.5f, 1f);
        toastRoot.sizeDelta = new Vector2(920f, 140f);
        toastRoot.anchoredPosition = new Vector2(0f, -36f);

        Image bg = toastGo.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.94f);

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(toastRoot, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.sizeDelta = new Vector2(88f, 88f);
        iconRt.anchoredPosition = new Vector2(28f, 0f);
        toastIcon = iconGo.GetComponent<Image>();
        toastIcon.color = new Color(0.85f, 0.72f, 0.35f, 1f);
        toastIcon.preserveAspect = true;

        toastTitle = CreateTmp("Title", toastRoot, 34f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-140f, -18f), new Vector2(0f, 46f), TextAlignmentOptions.Left);
        toastTitle.margin = new Vector4(140f, 0f, 28f, 0f);

        toastDescription = CreateTmp("Description", toastRoot, 26f, FontStyles.Normal,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(-140f, -12f), new Vector2(0f, -20f), TextAlignmentOptions.Left);
        toastDescription.color = new Color(0.85f, 0.82f, 0.76f, 1f);
        toastDescription.margin = new Vector4(140f, 0f, 28f, 0f);
    }

    private void BuildPanelUi()
    {
        RectTransform canvasRt = uiCanvas.GetComponent<RectTransform>();

        if (openPanelButton == null)
        {
            var openGo = new GameObject("AchievementsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            openGo.transform.SetParent(canvasRt, false);
            var openRt = openGo.GetComponent<RectTransform>();
            openRt.anchorMin = new Vector2(1f, 1f);
            openRt.anchorMax = new Vector2(1f, 1f);
            openRt.pivot = new Vector2(1f, 1f);
            openRt.sizeDelta = new Vector2(160f, 72f);
            openRt.anchoredPosition = new Vector2(-24f, -24f);
            openGo.GetComponent<Image>().color = new Color(0.12f, 0.11f, 0.1f, 0.9f);
            openPanelButton = openGo.GetComponent<Button>();
            CreateTmp("Label", openRt, 24f, FontStyles.Bold,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Center).text = "Awards";
        }

        var panelGo = new GameObject("AchievementsPanel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(canvasRt, false);
        achievementsPanel = panelGo;
        var panelRt = panelGo.GetComponent<RectTransform>();
        StretchFull(panelRt);
        panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var windowGo = new GameObject("Window", typeof(RectTransform), typeof(Image));
        windowGo.transform.SetParent(panelRt, false);
        var windowRt = windowGo.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.sizeDelta = new Vector2(920f, 1200f);
        windowGo.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.08f, 0.98f);

        CreateTmp("Header", windowRt, 42f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(-40f, 60f), TextAlignmentOptions.Center).text = "Achievements";

        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(windowRt, false);
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.sizeDelta = new Vector2(72f, 72f);
        closeRt.anchoredPosition = new Vector2(-12f, -12f);
        closeGo.GetComponent<Image>().color = new Color(0.25f, 0.18f, 0.15f, 1f);
        closePanelButton = closeGo.GetComponent<Button>();
        CreateTmp("X", closeRt, 36f, FontStyles.Bold,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, TextAlignmentOptions.Center).text = "X";

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(windowRt, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(28f, 28f);
        scrollRt.offsetMax = new Vector2(-28f, -100f);
        scrollGo.GetComponent<Image>().color = new Color(0.07f, 0.06f, 0.05f, 1f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(scrollRt, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        StretchFull(viewportRt);
        viewportGo.GetComponent<Image>().color = Color.white;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportRt, false);
        achievementsContent = contentGo.transform;
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.spacing = 12f;
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

        rowViews.Clear();
        for (int i = 0; i < achievements.Count; i++)
        {
            if (achievements[i] == null)
                continue;
            rowViews.Add(AchievementRowView.Create(achievementsContent, achievements[i]));
        }
    }

    private static TextMeshProUGUI CreateTmp(
        string name,
        Transform parent,
        float fontSize,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private sealed class AchievementRowView
    {
        private readonly Achievement achievement;
        private readonly Image background;
        private readonly Image icon;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI description;
        private readonly TextMeshProUGUI status;

        private AchievementRowView(
            Achievement achievement,
            Image background,
            Image icon,
            TextMeshProUGUI title,
            TextMeshProUGUI description,
            TextMeshProUGUI status)
        {
            this.achievement = achievement;
            this.background = background;
            this.icon = icon;
            this.title = title;
            this.description = description;
            this.status = status;
        }

        public static AchievementRowView Create(Transform parent, Achievement achievement)
        {
            var rowGo = new GameObject(achievement.id, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 150f);
            rowGo.GetComponent<LayoutElement>().preferredHeight = 150f;
            Image bg = rowGo.GetComponent<Image>();

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rowRt, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.sizeDelta = new Vector2(96f, 96f);
            iconRt.anchoredPosition = new Vector2(20f, 0f);
            Image icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;

            TextMeshProUGUI title = CreateTmp("Title", rowRt, 30f, FontStyles.Bold,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(40f, -16f), new Vector2(-160f, 40f), TextAlignmentOptions.Left);
            title.margin = new Vector4(130f, 0f, 24f, 0f);

            TextMeshProUGUI description = CreateTmp("Description", rowRt, 22f, FontStyles.Normal,
                new Vector2(0f, 0f), new Vector2(1f, 0.55f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            description.margin = new Vector4(130f, 0f, 24f, 0f);

            TextMeshProUGUI status = CreateTmp("Status", rowRt, 20f, FontStyles.Bold,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -16f), new Vector2(140f, 36f), TextAlignmentOptions.Right);

            var view = new AchievementRowView(achievement, bg, icon, title, description, status);
            view.Refresh();
            return view;
        }

        public void Refresh()
        {
            bool unlocked = achievement != null && achievement.IsUnlocked;
            title.text = achievement != null ? achievement.title : "?";
            description.text = achievement != null ? achievement.description : string.Empty;
            status.text = unlocked ? "UNLOCKED" : "LOCKED";
            status.color = unlocked
                ? new Color(0.72f, 0.86f, 0.45f, 1f)
                : new Color(0.55f, 0.5f, 0.45f, 1f);

            background.color = unlocked
                ? new Color(0.18f, 0.22f, 0.14f, 1f)
                : new Color(0.14f, 0.13f, 0.12f, 1f);

            if (icon != null)
            {
                icon.sprite = achievement != null ? achievement.icon : null;
                icon.enabled = true;
                icon.color = unlocked
                    ? Color.white
                    : new Color(0.25f, 0.25f, 0.25f, 0.85f);
            }

            title.color = unlocked
                ? new Color(0.95f, 0.92f, 0.86f, 1f)
                : new Color(0.55f, 0.52f, 0.48f, 1f);
            description.color = unlocked
                ? new Color(0.82f, 0.78f, 0.7f, 1f)
                : new Color(0.42f, 0.4f, 0.38f, 1f);
        }
    }
}
