using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Start-menu entry for Daily Challenge Mode (auto-builds a button if missing).
/// </summary>
public class DailyChallengeUI : MonoBehaviour
{
    [SerializeField] private Button dailyButton;
    [SerializeField] private TextMeshProUGUI dailyLabel;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool buildUiIfMissing = true;
    [SerializeField] private GameManager gameManager;

    private bool uiBuilt;

    private void Awake()
    {
        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();

        if (DailyChallengeManager.Instance == null && FindObjectOfType<DailyChallengeManager>() == null)
            new GameObject("DailyChallengeManager").AddComponent<DailyChallengeManager>();

        if (buildUiIfMissing)
            EnsureUi();

        Wire();
        RefreshLabel();
    }

    private void OnEnable()
    {
        RefreshLabel();
    }

    public void RefreshLabel()
    {
        if (dailyLabel == null)
            return;

        var daily = DailyChallengeManager.Instance;
        dailyLabel.text = daily != null ? daily.GetStatusLabel() : "Daily Challenge";

        if (dailyButton != null && daily != null)
            dailyButton.interactable = !daily.HasPlayedToday();
    }

    private void Wire()
    {
        if (dailyButton == null)
            return;

        dailyButton.onClick.RemoveListener(OnDailyClicked);
        dailyButton.onClick.AddListener(OnDailyClicked);
    }

    private void OnDailyClicked()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();

        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();

        if (gameManager == null)
        {
            Debug.LogWarning("DailyChallengeUI: GameManager missing.");
            return;
        }

        gameManager.OnDailyChallengePressed();
        RefreshLabel();
    }

    private void EnsureUi()
    {
        if (uiBuilt || dailyButton != null)
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
            var canvasGo = new GameObject("DailyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        var go = new GameObject("DailyChallengeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(targetCanvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(520f, 88f);
        rt.anchoredPosition = new Vector2(0f, 160f);
        go.GetComponent<Image>().color = new Color(0.14f, 0.18f, 0.22f, 0.95f);
        if (go.GetComponent<AccessibleBackground>() == null)
            go.AddComponent<AccessibleBackground>();

        dailyButton = go.GetComponent<Button>();

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        dailyLabel = labelGo.GetComponent<TextMeshProUGUI>();
        dailyLabel.fontSize = 30f;
        dailyLabel.fontStyle = FontStyles.Bold;
        dailyLabel.alignment = TextAlignmentOptions.Center;
        dailyLabel.color = new Color(0.95f, 0.92f, 0.86f, 1f);
        dailyLabel.raycastTarget = false;
        dailyLabel.text = "Daily Challenge";

        uiBuilt = true;
    }
}
