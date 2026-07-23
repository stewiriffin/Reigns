using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Android system interactions: hardware Back (KeyCode.Escape), pause overlay,
/// native quit confirmation on the Start Menu, and OS-level app pause handling.
/// </summary>
public class AndroidSystemHandler : MonoBehaviour
{
    public static AndroidSystemHandler Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CardSwipeHandler cardSwipe;
    [SerializeField] private Canvas targetCanvas;

    [Header("Pause Menu")]
    [SerializeField] private RectTransform pausePanelRoot;
    [SerializeField] private CanvasGroup pauseCanvasGroup;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitToMenuButton;
    [SerializeField] private float pauseSlideDuration = 0.28f;

    [Header("Auto UI")]
    [SerializeField] private bool buildUiIfMissing = true;

    private bool pauseMenuOpen;
    private bool appPausedByOs;
    private bool userPaused;
    private float timeScaleBeforePause = 1f;
    private Coroutine slideRoutine;
    private bool quitDialogVisible;
    private bool uiBuilt;

    public bool IsPauseMenuOpen => pauseMenuOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (cardSwipe == null)
            cardSwipe = FindObjectOfType<CardSwipeHandler>();

        if (buildUiIfMissing)
            EnsurePauseUi();

        WireButtons();
        HidePauseImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        HandleBackPressed();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        HandleOsPause(pauseStatus);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // Treat focus loss like pause on some Android paths.
        if (!hasFocus)
            HandleOsPause(true);
        else if (!userPaused)
            HandleOsPause(false);
    }

    /// <summary>Hardware Back / Escape routing.</summary>
    public void HandleBackPressed()
    {
        if (quitDialogVisible)
            return;

        if (pauseMenuOpen)
        {
            ResumeFromPauseMenu();
            return;
        }

        if (SettingsMenuVisible())
        {
            var settings = FindObjectOfType<SettingsMenu>();
            if (settings != null)
                settings.Hide();
            return;
        }

        if (DynastyHallVisible())
        {
            var hall = FindObjectOfType<DynastyHallUI>();
            if (hall != null)
                hall.Hide();
            return;
        }

        UIFadeTransition.ScreenId screen = ResolveCurrentScreen();

        switch (screen)
        {
            case UIFadeTransition.ScreenId.Gameplay:
                OpenPauseMenu();
                break;
            case UIFadeTransition.ScreenId.StartMenu:
                ShowNativeQuitConfirmation();
                break;
            case UIFadeTransition.ScreenId.GameOver:
                // Prefer returning toward start rather than quitting mid-result.
                if (DynastyHistoryManager.Instance != null)
                    DynastyHistoryManager.Instance.CommitPendingRecord();
                if (UIFadeTransition.Instance != null)
                    UIFadeTransition.Instance.SnapTo(UIFadeTransition.ScreenId.StartMenu);
                break;
        }
    }

    public void OpenPauseMenu()
    {
        if (pauseMenuOpen)
            return;

        EnsurePauseUi();
        userPaused = true;
        pauseMenuOpen = true;
        ApplyGameplayPause(true);

        if (slideRoutine != null)
            StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlidePauseMenu(open: true));
    }

    public void ResumeFromPauseMenu()
    {
        if (!pauseMenuOpen)
            return;

        userPaused = false;
        pauseMenuOpen = false;

        if (slideRoutine != null)
            StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlidePauseMenu(open: false, onComplete: () =>
        {
            if (!appPausedByOs)
                ApplyGameplayPause(false);
        }));
    }

    private void HandleOsPause(bool paused)
    {
        appPausedByOs = paused;

        if (paused)
        {
            if (Time.timeScale > 0f)
                timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;

            if (AudioManager.Instance != null)
                AudioManager.Instance.SetMutedForAppPause(true);

            if (cardSwipe != null)
                cardSwipe.SetInputEnabled(false);
        }
        else
        {
            // OS resumed — only thaw if the in-game pause menu is not holding the freeze.
            if (!userPaused)
                ApplyGameplayPause(false);
        }
    }

    private void ApplyGameplayPause(bool paused)
    {
        if (paused)
        {
            if (Time.timeScale > 0f)
                timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;

            if (AudioManager.Instance != null)
                AudioManager.Instance.SetMutedForAppPause(true);

            if (cardSwipe != null)
                cardSwipe.SetInputEnabled(false);
        }
        else
        {
            Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;

            if (AudioManager.Instance != null)
                AudioManager.Instance.SetMutedForAppPause(false);

            if (cardSwipe != null && ResolveCurrentScreen() == UIFadeTransition.ScreenId.Gameplay)
                cardSwipe.SetInputEnabled(true);
        }
    }

    private UIFadeTransition.ScreenId ResolveCurrentScreen()
    {
        if (UIFadeTransition.Instance != null)
            return UIFadeTransition.Instance.CurrentScreen;

        return UIFadeTransition.ScreenId.Gameplay;
    }

    private static bool SettingsMenuVisible()
    {
        var settings = FindObjectOfType<SettingsMenu>();
        return settings != null && settings.IsOpen;
    }

    private static bool DynastyHallVisible()
    {
        var hall = FindObjectOfType<DynastyHallUI>();
        return hall != null && hall.IsOpen;
    }

    private void ShowNativeQuitConfirmation()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (quitDialogVisible)
            return;

        quitDialogVisible = true;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", activity);
                    builder.Call<AndroidJavaObject>("setTitle", "Quit");
                    builder.Call<AndroidJavaObject>("setMessage", "Do you want to quit the game?");
                    builder.Call<AndroidJavaObject>("setCancelable", true);
                    builder.Call<AndroidJavaObject>(
                        "setPositiveButton",
                        "Yes",
                        new DialogClickListener(() =>
                        {
                            quitDialogVisible = false;
                            Application.Quit();
                        }));
                    builder.Call<AndroidJavaObject>(
                        "setNegativeButton",
                        "No",
                        new DialogClickListener(() => { quitDialogVisible = false; }));
                    AndroidJavaObject dialog = builder.Call<AndroidJavaObject>("create");
                    dialog.Call("show");
                }));
            }
        }
        catch (System.Exception e)
        {
            quitDialogVisible = false;
            Debug.LogWarning("AndroidSystemHandler: quit dialog failed — " + e.Message);
            Application.Quit();
        }
#else
        // Editor / non-Android fallback.
        if (Application.isEditor)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.DisplayDialog(
                    "Quit",
                    "Do you want to quit the game?",
                    "Yes",
                    "No"))
            {
                UnityEditor.EditorApplication.isPlaying = false;
            }
#endif
        }
        else
        {
            Application.Quit();
        }
#endif
    }

    private void WireButtons()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(ResumeFromPauseMenu);
            resumeButton.onClick.AddListener(ResumeFromPauseMenu);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(OpenSettingsFromPause);
        }

        if (quitToMenuButton != null)
        {
            quitToMenuButton.onClick.RemoveAllListeners();
            quitToMenuButton.onClick.AddListener(() =>
            {
                ResumeFromPauseMenu();
                if (UIFadeTransition.Instance != null)
                    UIFadeTransition.Instance.SnapTo(UIFadeTransition.ScreenId.StartMenu);
            });
        }
    }

    private void OpenSettingsFromPause()
    {
        var settings = FindObjectOfType<SettingsMenu>();
        if (settings == null)
            settings = new GameObject("SettingsMenu").AddComponent<SettingsMenu>();
        settings.Show();
    }

    private IEnumerator SlidePauseMenu(bool open, System.Action onComplete = null)
    {
        if (pausePanelRoot == null || pauseCanvasGroup == null)
            yield break;

        pausePanelRoot.gameObject.SetActive(true);
        pauseCanvasGroup.blocksRaycasts = open;
        pauseCanvasGroup.interactable = open;

        float height = ((RectTransform)pausePanelRoot.parent).rect.height;
        if (height < 1f)
            height = Screen.height;

        Vector2 hidden = new Vector2(0f, height);
        Vector2 shown = Vector2.zero;
        Vector2 from = open ? hidden : pausePanelRoot.anchoredPosition;
        Vector2 to = open ? shown : hidden;
        float fromAlpha = open ? 0f : pauseCanvasGroup.alpha;
        float toAlpha = open ? 1f : 0f;

        if (!open)
            from = pausePanelRoot.anchoredPosition;

        float duration = Mathf.Max(0.05f, pauseSlideDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            pausePanelRoot.anchoredPosition = Vector2.Lerp(from, to, t);
            pauseCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }

        pausePanelRoot.anchoredPosition = to;
        pauseCanvasGroup.alpha = toAlpha;

        if (!open)
            pausePanelRoot.gameObject.SetActive(false);

        slideRoutine = null;
        onComplete?.Invoke();
    }

    private void HidePauseImmediate()
    {
        pauseMenuOpen = false;
        userPaused = false;
        if (pausePanelRoot != null)
        {
            pausePanelRoot.gameObject.SetActive(false);
            pausePanelRoot.anchoredPosition = new Vector2(0f, 2000f);
        }

        if (pauseCanvasGroup != null)
        {
            pauseCanvasGroup.alpha = 0f;
            pauseCanvasGroup.blocksRaycasts = false;
            pauseCanvasGroup.interactable = false;
        }
    }

    private void EnsurePauseUi()
    {
        if (uiBuilt && pausePanelRoot != null)
            return;

        if (pausePanelRoot != null && pauseCanvasGroup != null)
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
            var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        var root = new GameObject("PauseMenu", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        root.transform.SetParent(targetCanvas.transform, false);
        pausePanelRoot = root.GetComponent<RectTransform>();
        pauseCanvasGroup = root.GetComponent<CanvasGroup>();
        Stretch(pausePanelRoot);
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        if (root.GetComponent<AccessibleBackground>() == null)
            root.AddComponent<AccessibleBackground>();

        var window = new GameObject("Window", typeof(RectTransform), typeof(Image));
        window.transform.SetParent(pausePanelRoot, false);
        var windowRt = window.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.sizeDelta = new Vector2(720f, 520f);
        window.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.08f, 0.98f);
        if (window.GetComponent<AccessibleBackground>() == null)
            window.AddComponent<AccessibleBackground>();

        CreateLabel(window.transform, "Paused", 44f, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -36f), new Vector2(0f, 60f));

        resumeButton = CreateMenuButton(window.transform, "Resume", new Vector2(0f, 80f));
        settingsButton = CreateMenuButton(window.transform, "Settings", new Vector2(0f, -20f));
        quitToMenuButton = CreateMenuButton(window.transform, "Quit to Menu", new Vector2(0f, -120f));

        WireButtons();
        uiBuilt = true;
    }

    private static Button CreateMenuButton(Transform parent, string label, Vector2 anchoredPos)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(480f, 90f);
        rt.anchoredPosition = anchoredPos;
        go.GetComponent<Image>().color = new Color(0.18f, 0.16f, 0.14f, 1f);
        CreateLabel(go.transform, label, 30f, FontStyles.Bold,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return go.GetComponent<Button>();
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent,
        string text,
        float size,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.92f, 0.86f, 1f);
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

#if UNITY_ANDROID && !UNITY_EDITOR
    private sealed class DialogClickListener : AndroidJavaProxy
    {
        private readonly System.Action onClick;

        public DialogClickListener(System.Action onClick)
            : base("android.content.DialogInterface$OnClickListener")
        {
            this.onClick = onClick;
        }

        // ReSharper disable once InconsistentNaming — Android JNI callback
        public void onClick(AndroidJavaObject dialog, int which)
        {
            onClick?.Invoke();
        }
    }
#endif
}
