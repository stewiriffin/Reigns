using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Fades between major UI screens (Start Menu, Gameplay, Game Over) via CanvasGroups.
/// </summary>
public class UIFadeTransition : MonoBehaviour
{
    public enum ScreenId
    {
        StartMenu,
        Gameplay,
        GameOver
    }

    public static UIFadeTransition Instance { get; private set; }

    [Header("Screens (CanvasGroups)")]
    [SerializeField] private CanvasGroup startMenuGroup;
    [SerializeField] private CanvasGroup gameplayGroup;
    [SerializeField] private CanvasGroup gameOverGroup;

    [Header("Timing")]
    [SerializeField] private float fadeDuration = 0.4f;
    [SerializeField] private CanvasGroup fullScreenFader;

    private Coroutine transitionRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (fullScreenFader == null)
            EnsureFullscreenFader();

        // Default boot: only start menu visible if assigned.
        if (startMenuGroup != null)
        {
            CurrentScreen = ScreenId.StartMenu;
            SetGroupState(startMenuGroup, true, 1f);
            SetGroupState(gameplayGroup, false, 0f);
            SetGroupState(gameOverGroup, false, 0f);
            if (AdManager.Instance != null)
                AdManager.Instance.NotifyUiScreen(ScreenId.StartMenu);
        }
    }

    /// <summary>
    /// Crossfades to the requested screen. Invokes <paramref name="onMidpoint"/>
    /// when the screen is fully covered (safe place to swap logic).
    /// </summary>
    public void TransitionTo(ScreenId screen, Action onMidpoint = null, Action onComplete = null)
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        transitionRoutine = StartCoroutine(TransitionRoutine(screen, onMidpoint, onComplete));
    }

    public void SnapTo(ScreenId screen)
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        ShowOnly(screen);
        if (fullScreenFader != null)
        {
            fullScreenFader.alpha = 0f;
            fullScreenFader.blocksRaycasts = false;
        }
    }

    /// <summary>Last screen shown via TransitionTo / SnapTo.</summary>
    public ScreenId CurrentScreen { get; private set; } = ScreenId.StartMenu;

    private IEnumerator TransitionRoutine(ScreenId screen, Action onMidpoint, Action onComplete)
    {
        EnsureFullscreenFader();
        float duration = Mathf.Max(0.05f, fadeDuration);

        // Fade to black
        yield return FadeCanvas(fullScreenFader, fullScreenFader.alpha, 1f, duration * 0.5f);
        fullScreenFader.blocksRaycasts = true;

        ShowOnly(screen);
        onMidpoint?.Invoke();

        // Fade from black
        yield return FadeCanvas(fullScreenFader, 1f, 0f, duration * 0.5f);
        fullScreenFader.blocksRaycasts = false;

        transitionRoutine = null;
        onComplete?.Invoke();
    }

    private void ShowOnly(ScreenId screen)
    {
        CurrentScreen = screen;
        SetGroupState(startMenuGroup, screen == ScreenId.StartMenu, screen == ScreenId.StartMenu ? 1f : 0f);
        SetGroupState(gameplayGroup, screen == ScreenId.Gameplay, screen == ScreenId.Gameplay ? 1f : 0f);
        SetGroupState(gameOverGroup, screen == ScreenId.GameOver, screen == ScreenId.GameOver ? 1f : 0f);

        if (AdManager.Instance != null)
            AdManager.Instance.NotifyUiScreen(screen);
    }

    private static void SetGroupState(CanvasGroup group, bool interactable, float alpha)
    {
        if (group == null)
            return;

        group.alpha = alpha;
        group.interactable = interactable;
        group.blocksRaycasts = interactable;
        if (!group.gameObject.activeSelf && alpha > 0f)
            group.gameObject.SetActive(true);
    }

    private IEnumerator FadeCanvas(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        float elapsed = 0f;
        group.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3f - 2f * t);
            group.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        group.alpha = to;
    }

    private void EnsureFullscreenFader()
    {
        if (fullScreenFader != null)
            return;

        var go = new GameObject("ScreenFader", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
        Transform parent = transform;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            parent = canvas.transform;

        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = Color.black;
        image.raycastTarget = true;

        fullScreenFader = go.GetComponent<CanvasGroup>();
        fullScreenFader.alpha = 0f;
        fullScreenFader.blocksRaycasts = false;
        go.transform.SetAsLastSibling();
    }
}
