using System.Collections;
using UnityEngine;
#if DOTWEEN
using DG.Tweening;
#endif

/// <summary>
/// Lightweight card "juice" via DOTween (free Demigiant plugin).
/// Add scripting define <c>DOTWEEN</c> after importing DOTween.
/// Without the define, equivalent coroutine fallbacks keep Editor builds working.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CardUIJuice : MonoBehaviour
{
    [Header("Draw appear")]
    [SerializeField] private float drawFromScale = 0.88f;
    [SerializeField] private float drawToScale = 1f;
    [SerializeField] private float drawDuration = 0.4f;

    [Header("Blocked-swipe shake")]
    [SerializeField] private float shakeDuration = 0.28f;
    [SerializeField] private float shakeStrength = 18f;
    [SerializeField] private int shakeVibrato = 18;
    [SerializeField] private float shakeCooldown = 0.45f;

    private RectTransform rectTransform;
    private Vector3 baseScale = Vector3.one;
    private Vector2 baseAnchoredPos;
    private float lastShakeTime = -999f;
    private Coroutine fallbackRoutine;

#if DOTWEEN
    private Tween activeTween;
#endif

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        baseScale = rectTransform.localScale;
        if (baseScale.sqrMagnitude < 0.0001f)
            baseScale = Vector3.one;
        baseAnchoredPos = rectTransform.anchoredPosition;
    }

    private void OnDisable()
    {
        KillActive();
        rectTransform.localScale = baseScale;
        rectTransform.anchoredPosition = baseAnchoredPos;
    }

    /// <summary>
    /// Call when a new card is shown: scales up into place with an OutBack overshoot (~1.05x peak).
    /// </summary>
    public void PlayDrawAppear()
    {
        KillActive();
        baseAnchoredPos = rectTransform.anchoredPosition;
        rectTransform.localScale = baseScale * drawFromScale;

#if DOTWEEN
        // OutBack overshoots past drawToScale (visually ~1.05x) then settles on 1.
        activeTween = rectTransform
            .DOScale(baseScale * drawToScale, drawDuration)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);
#else
        fallbackRoutine = StartCoroutine(FallbackDrawAppear());
#endif
    }

    /// <summary>
    /// Subtle shake when game logic blocks a swipe attempt (e.g. tutorial direction lock).
    /// </summary>
    public void PlayBlockedSwipeShake()
    {
        if (Time.unscaledTime - lastShakeTime < shakeCooldown)
            return;

        lastShakeTime = Time.unscaledTime;
        KillActive(preserveScale: true);
        rectTransform.anchoredPosition = baseAnchoredPos;

#if DOTWEEN
        activeTween = rectTransform
            .DOShakeAnchorPos(shakeDuration, shakeStrength, shakeVibrato, 90f, snapping: false, fadeOut: true)
            .SetUpdate(true)
            .OnComplete(() => rectTransform.anchoredPosition = baseAnchoredPos);
#else
        fallbackRoutine = StartCoroutine(FallbackShake());
#endif
    }

    public void CaptureRestPose()
    {
        baseAnchoredPos = rectTransform.anchoredPosition;
        baseScale = rectTransform.localScale.sqrMagnitude > 0.0001f
            ? rectTransform.localScale
            : Vector3.one;
    }

    private void KillActive(bool preserveScale = false)
    {
#if DOTWEEN
        if (activeTween != null && activeTween.IsActive())
            activeTween.Kill();
        activeTween = null;
        rectTransform.DOKill(complete: false);
#endif
        if (fallbackRoutine != null)
        {
            StopCoroutine(fallbackRoutine);
            fallbackRoutine = null;
        }

        if (!preserveScale)
            rectTransform.localScale = baseScale;
    }

#if !DOTWEEN
    private IEnumerator FallbackDrawAppear()
    {
        // Approximate Ease.OutBack overshoot toward 1.05x then settle at 1.
        float duration = Mathf.Max(0.01f, drawDuration);
        float elapsed = 0f;
        Vector3 from = baseScale * drawFromScale;
        Vector3 to = baseScale * drawToScale;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutBack(t);
            rectTransform.localScale = Vector3.LerpUnclamped(from, to, eased);
            yield return null;
        }

        rectTransform.localScale = to;
        fallbackRoutine = null;
    }

    private IEnumerator FallbackShake()
    {
        float duration = Mathf.Max(0.01f, shakeDuration);
        float elapsed = 0f;
        Vector2 origin = baseAnchoredPos;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float damper = 1f - t;
            float ox = (Mathf.PerlinNoise(elapsed * 28f, 0.3f) * 2f - 1f) * shakeStrength * damper;
            float oy = (Mathf.PerlinNoise(0.7f, elapsed * 28f) * 2f - 1f) * shakeStrength * 0.35f * damper;
            rectTransform.anchoredPosition = origin + new Vector2(ox, oy);
            yield return null;
        }

        rectTransform.anchoredPosition = origin;
        fallbackRoutine = null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
#endif
}
