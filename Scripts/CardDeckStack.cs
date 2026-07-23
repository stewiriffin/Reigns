using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if DOTWEEN
using DG.Tweening;
#endif

/// <summary>
/// Visual deck depth: 2 dummy cards behind the interactive swipe card.
/// On discard, dummies advance forward while a new back card fades in.
/// </summary>
[DisallowMultipleComponent]
public class CardDeckStack : MonoBehaviour
{
    public static CardDeckStack Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CardSwipeHandler frontCard;
    [SerializeField] private RectTransform stackRoot;

    [Header("Depth look")]
    [SerializeField] private int dummyCount = 2;
    [SerializeField] private Vector2 offsetPerLayer = new Vector2(10f, -14f);
    [SerializeField] private float scalePerLayer = 0.955f;
    [SerializeField] private float backCardAlpha = 0.92f;
    [SerializeField] private Color dummyTint = new Color(0.88f, 0.84f, 0.78f, 1f);

    [Header("Advance animation")]
    [SerializeField] private float promoteDuration = 0.38f;
    [SerializeField] private float settleDuration = 0.22f;

    private readonly List<DummyCard> dummies = new List<DummyCard>(4);
    private Coroutine advanceRoutine;

    private struct DummyCard
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Image image;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (frontCard == null)
            frontCard = GetComponent<CardSwipeHandler>();
        if (frontCard == null)
            frontCard = FindObjectOfType<CardSwipeHandler>();

        if (stackRoot == null && frontCard != null)
            stackRoot = frontCard.transform.parent as RectTransform;

        EnsureDummies();
        ApplyDepthPoses(immediate: true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Call when the front card begins flying off-screen.</summary>
    public void PlayAdvance()
    {
        if (advanceRoutine != null)
            StopCoroutine(advanceRoutine);
        advanceRoutine = StartCoroutine(AdvanceRoutine());
    }

    /// <summary>Call after the interactive card has reset to center as the new top card.</summary>
    public void SettleBehindFront()
    {
        if (advanceRoutine != null)
        {
            StopCoroutine(advanceRoutine);
            advanceRoutine = null;
            TrimToDepthLayers();
        }

        RefreshDummyFaces();
        StartCoroutine(SettleRoutine());
    }

    private void RefreshDummyFaces()
    {
        for (int i = 0; i < dummies.Count; i++)
        {
            if (dummies[i].image != null)
                TryCopyFrontVisual(dummies[i].image);
        }
    }

    private IEnumerator AdvanceRoutine()
    {
        EnsureDummies();

        if (dummies.Count == 0)
        {
            advanceRoutine = null;
            yield break;
        }

        // Targets: each dummy moves one layer closer to the front (layer 0 → front rest).
        Vector2 frontPos = GetFrontRestPosition();
        Vector3 frontScale = Vector3.one;

        float duration = Mathf.Max(0.05f, promoteDuration);

#if DOTWEEN
        var sequence = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < dummies.Count; i++)
        {
            int layerTarget = i - 1; // 0 → -1 (front), 1 → 0 (mid)
            Vector2 toPos = layerTarget < 0 ? frontPos : GetLayerPosition(layerTarget);
            Vector3 toScale = layerTarget < 0 ? frontScale : GetLayerScale(layerTarget);
            float toAlpha = layerTarget < 0 ? 1f : GetLayerAlpha(layerTarget);
            int capture = i;

            sequence.Join(dummies[i].rect.DOAnchorPos(toPos, duration).SetEase(Ease.OutCubic));
            sequence.Join(dummies[i].rect.DOScale(toScale, duration).SetEase(Ease.OutCubic));
            if (dummies[i].group != null)
            {
                CanvasGroup g = dummies[capture].group;
                sequence.Join(DOTween.To(() => g.alpha, a => g.alpha = a, toAlpha, duration));
            }
        }

        // New back card appears at the deepest layer.
        DummyCard newborn = SpawnDummy();
        dummies.Add(newborn);
        int backLayer = dummyCount - 1;
        newborn.rect.anchoredPosition = GetLayerPosition(backLayer);
        newborn.rect.localScale = GetLayerScale(backLayer) * 0.92f;
        if (newborn.group != null)
            newborn.group.alpha = 0f;
        sequence.Join(newborn.rect.DOScale(GetLayerScale(backLayer), duration).SetEase(Ease.OutBack));
        if (newborn.group != null)
        {
            CanvasGroup ng = newborn.group;
            float targetAlpha = GetLayerAlpha(backLayer);
            sequence.Join(DOTween.To(() => ng.alpha, a => ng.alpha = a, targetAlpha, duration));
        }

        yield return sequence.WaitForCompletion();
#else
        var starts = new Vector2[dummies.Count];
        var startScales = new Vector3[dummies.Count];
        var startAlphas = new float[dummies.Count];
        for (int i = 0; i < dummies.Count; i++)
        {
            starts[i] = dummies[i].rect.anchoredPosition;
            startScales[i] = dummies[i].rect.localScale;
            startAlphas[i] = dummies[i].group != null ? dummies[i].group.alpha : 1f;
        }

        DummyCard newborn = SpawnDummy();
        dummies.Add(newborn);
        int backLayer = dummyCount - 1;
        newborn.rect.anchoredPosition = GetLayerPosition(backLayer);
        newborn.rect.localScale = GetLayerScale(backLayer) * 0.92f;
        if (newborn.group != null)
            newborn.group.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            for (int i = 0; i < starts.Length; i++)
            {
                int layerTarget = i - 1;
                Vector2 toPos = layerTarget < 0 ? frontPos : GetLayerPosition(layerTarget);
                Vector3 toScale = layerTarget < 0 ? frontScale : GetLayerScale(layerTarget);
                float toAlpha = layerTarget < 0 ? 1f : GetLayerAlpha(layerTarget);

                dummies[i].rect.anchoredPosition = Vector2.LerpUnclamped(starts[i], toPos, ease);
                dummies[i].rect.localScale = Vector3.LerpUnclamped(startScales[i], toScale, ease);
                if (dummies[i].group != null)
                    dummies[i].group.alpha = Mathf.Lerp(startAlphas[i], toAlpha, ease);
            }

            // Newborn is last in list.
            int ni = dummies.Count - 1;
            dummies[ni].rect.localScale = Vector3.LerpUnclamped(
                GetLayerScale(backLayer) * 0.92f,
                GetLayerScale(backLayer),
                ease);
            if (dummies[ni].group != null)
                dummies[ni].group.alpha = Mathf.Lerp(0f, GetLayerAlpha(backLayer), ease);

            yield return null;
        }
#endif

        // Trim to dummyCount: drop the visual that reached "front" (interactive card owns that slot).
        TrimToDepthLayers();
        advanceRoutine = null;
    }

    private IEnumerator SettleRoutine()
    {
        EnsureDummies();
        TrimToDepthLayers();

        var starts = new Vector2[dummies.Count];
        var startScales = new Vector3[dummies.Count];
        for (int i = 0; i < dummies.Count; i++)
        {
            starts[i] = dummies[i].rect.anchoredPosition;
            startScales[i] = dummies[i].rect.localScale;
        }

        float duration = Mathf.Max(0.05f, settleDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = t * t * (3f - 2f * t);
            for (int i = 0; i < dummies.Count; i++)
            {
                dummies[i].rect.anchoredPosition = Vector2.Lerp(starts[i], GetLayerPosition(i), ease);
                dummies[i].rect.localScale = Vector3.Lerp(startScales[i], GetLayerScale(i), ease);
                if (dummies[i].group != null)
                    dummies[i].group.alpha = GetLayerAlpha(i);
            }

            yield return null;
        }

        ApplyDepthPoses(immediate: true);
    }

    private void TrimToDepthLayers()
    {
        // Keep only the deepest dummyCount cards; destroy extras that animated to front.
        while (dummies.Count > dummyCount)
        {
            DummyCard extra = dummies[0];
            dummies.RemoveAt(0);
            if (extra.rect != null)
                Destroy(extra.rect.gameObject);
        }

        // Re-sibling: deepest first (drawn behind), closest last among dummies.
        for (int i = 0; i < dummies.Count; i++)
        {
            if (dummies[i].rect != null)
                dummies[i].rect.SetAsFirstSibling();
        }

        if (frontCard != null)
            frontCard.transform.SetAsLastSibling();
    }

    private void EnsureDummies()
    {
        dummyCount = Mathf.Max(1, dummyCount);
        while (dummies.Count < dummyCount)
            dummies.Add(SpawnDummy());

        while (dummies.Count > dummyCount)
        {
            DummyCard extra = dummies[dummies.Count - 1];
            dummies.RemoveAt(dummies.Count - 1);
            if (extra.rect != null)
                Destroy(extra.rect.gameObject);
        }
    }

    private DummyCard SpawnDummy()
    {
        Transform parent = stackRoot != null ? stackRoot : (frontCard != null ? frontCard.transform.parent : transform);
        var go = new GameObject("DeckDummy", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        CopyFrontCardRect(rt);

        var image = go.GetComponent<Image>();
        image.color = dummyTint;
        image.raycastTarget = false;
        TryCopyFrontVisual(image);

        var group = go.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = backCardAlpha;

        // Soft shadow edge via outline-ish darker sibling border.
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(rt, false);
        var borderRt = border.GetComponent<RectTransform>();
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = new Vector2(-4f, -4f);
        borderRt.offsetMax = new Vector2(4f, 4f);
        borderRt.SetAsFirstSibling();
        var borderImg = border.GetComponent<Image>();
        borderImg.color = new Color(0.05f, 0.04f, 0.03f, 0.35f);
        borderImg.raycastTarget = false;

        return new DummyCard { rect = rt, group = group, image = image };
    }

    private void CopyFrontCardRect(RectTransform rt)
    {
        RectTransform src = frontCard != null ? frontCard.RectTransform : null;
        if (src == null)
        {
            rt.sizeDelta = new Vector2(620f, 880f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return;
        }

        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.sizeDelta = src.sizeDelta;
        rt.anchoredPosition = src.anchoredPosition;
    }

    private void TryCopyFrontVisual(Image target)
    {
        if (frontCard == null || target == null)
            return;

        Image[] images = frontCard.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null || img.sprite == null)
                continue;
            // Prefer a large face/background sprite.
            target.sprite = img.sprite;
            target.type = img.type;
            target.preserveAspect = img.preserveAspect;
            Color c = img.color;
            c.a = 1f;
            target.color = Color.Lerp(c, dummyTint, 0.35f);
            return;
        }
    }

    private void ApplyDepthPoses(bool immediate)
    {
        EnsureDummies();
        for (int i = 0; i < dummies.Count; i++)
        {
            dummies[i].rect.anchoredPosition = GetLayerPosition(i);
            dummies[i].rect.localScale = GetLayerScale(i);
            dummies[i].rect.localRotation = Quaternion.identity;
            if (dummies[i].group != null)
                dummies[i].group.alpha = GetLayerAlpha(i);
            dummies[i].rect.SetAsFirstSibling();
        }

        if (frontCard != null)
            frontCard.transform.SetAsLastSibling();
    }

    private Vector2 GetFrontRestPosition()
    {
        return frontCard != null ? frontCard.RestAnchoredPosition : Vector2.zero;
    }

    private Vector2 GetLayerPosition(int layer)
    {
        // layer 0 = closest behind front, higher = deeper.
        int depth = layer + 1;
        return GetFrontRestPosition() + offsetPerLayer * depth;
    }

    private Vector3 GetLayerScale(int layer)
    {
        int depth = layer + 1;
        float s = Mathf.Pow(scalePerLayer, depth);
        return new Vector3(s, s, 1f);
    }

    private float GetLayerAlpha(int layer)
    {
        return Mathf.Lerp(backCardAlpha, 0.75f, layer / (float)Mathf.Max(1, dummyCount));
    }
}
