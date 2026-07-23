using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
#if DOTWEEN
using DG.Tweening;
#endif

/// <summary>
/// One pooled floating "+15" / "-20" label used by <see cref="FloatingStatText"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(TextMeshProUGUI))]
public class FloatingStatPopup : MonoBehaviour
{
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private TextMeshProUGUI label;
    private ObjectPool ownerPool;
    private bool leased;
    private Coroutine fallbackRoutine;
    private static readonly StringBuilder SharedBuilder = new StringBuilder(8);

#if DOTWEEN
    private Sequence activeSequence;
#endif

    public void Initialize(ObjectPool pool)
    {
        if (ownerPool == null)
            ownerPool = pool;

        if (rectTransform == null)
            rectTransform = transform as RectTransform;
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (label == null)
            label = GetComponent<TextMeshProUGUI>();

        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Overflow;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    public void Play(
        RectTransform canvasRoot,
        Vector2 anchoredStart,
        int delta,
        Color color,
        float fontSize,
        float duration,
        float riseDistance)
    {
        if (delta == 0 || canvasRoot == null)
        {
            ReleaseToPool();
            return;
        }

        KillActive();
        leased = true;

        transform.SetParent(canvasRoot, false);
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(160f, 64f);
        rectTransform.anchoredPosition = anchoredStart;
        rectTransform.localScale = Vector3.one;

        label.fontSize = fontSize;
        label.color = color;
        canvasGroup.alpha = 1f;
        SetDeltaText(delta);

        gameObject.SetActive(true);

        duration = Mathf.Max(0.05f, duration);
        Vector2 end = anchoredStart + new Vector2(0f, riseDistance);

#if DOTWEEN
        activeSequence = DOTween.Sequence().SetUpdate(true);
        activeSequence.Join(rectTransform.DOAnchorPos(end, duration).SetEase(Ease.OutQuad));
        activeSequence.Join(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
        activeSequence.Join(rectTransform.DOScale(1.08f, duration * 0.35f).SetEase(Ease.OutBack));
        activeSequence.OnComplete(ReleaseToPool);
#else
        fallbackRoutine = StartCoroutine(AnimateFallback(anchoredStart, end, duration));
#endif
    }

    private void SetDeltaText(int delta)
    {
        SharedBuilder.Length = 0;
        if (delta > 0)
            SharedBuilder.Append('+');
        SharedBuilder.Append(delta);
        label.SetText(SharedBuilder);
    }

#if !DOTWEEN
    private IEnumerator AnimateFallback(Vector2 start, Vector2 end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - (1f - t) * (1f - t);
            float easeIn = t * t;

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(start, end, easeOut);
            canvasGroup.alpha = 1f - easeIn;

            float punch = t < 0.35f ? Mathf.Lerp(1f, 1.08f, t / 0.35f) : 1.08f;
            rectTransform.localScale = new Vector3(punch, punch, 1f);
            yield return null;
        }

        fallbackRoutine = null;
        ReleaseToPool();
    }
#endif

    private void KillActive()
    {
#if DOTWEEN
        if (activeSequence != null && activeSequence.IsActive())
            activeSequence.Kill();
        activeSequence = null;
        rectTransform.DOKill();
        canvasGroup.DOKill();
#endif
        if (fallbackRoutine != null)
        {
            StopCoroutine(fallbackRoutine);
            fallbackRoutine = null;
        }
    }

    private void ReleaseToPool()
    {
        KillActive();

        if (!leased)
        {
            gameObject.SetActive(false);
            return;
        }

        leased = false;
        if (ownerPool != null)
            ownerPool.Release(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        KillActive();
    }
}
