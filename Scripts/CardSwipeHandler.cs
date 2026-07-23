using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Detects left/right swipes on a card UI element via touch or mouse drag.
/// Fires callbacks when the card crosses a horizontal threshold; otherwise snaps back.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CardSwipeHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Swipe")]
    [Tooltip("Horizontal distance (in UI units) required to confirm a swipe.")]
    [SerializeField] private float swipeThreshold = 200f;

    [Tooltip("Max horizontal travel while dragging (clamps the card).")]
    [SerializeField] private float maxDragDistance = 350f;

    [Tooltip("Degrees of Z rotation at full swipe threshold (tilts with drag direction).")]
    [SerializeField] private float maxTiltDegrees = 15f;

    [Tooltip("Extra tilt scale once past the commit threshold (toward max drag).")]
    [SerializeField] [Range(0f, 1f)] private float overdragTiltScale = 0.35f;

    [Header("Snap Back")]
    [SerializeField] private float snapBackSpeed = 12f;

    [Header("Discard")]
    [SerializeField] private float discardDistance = 900f;
    [SerializeField] private float discardDuration = 0.25f;

    [Header("Events")]
    [SerializeField] private UnityEvent onSwipeLeft;
    [SerializeField] private UnityEvent onSwipeRight;

    /// <summary>Normalized horizontal drag in [-1, 1]. Negative = left, positive = right.</summary>
    public event Action<float> OnSwipeProgress;

    /// <summary>Fired once when the left threshold is crossed on release (or commit).</summary>
    public event Action OnSwipeLeft;

    /// <summary>Fired once when the right threshold is crossed on release (or commit).</summary>
    public event Action OnSwipeRight;

    private RectTransform rectTransform;
    private Vector2 startAnchoredPosition;
    private Quaternion startLocalRotation;
    private Vector2 dragStartScreenPos;
    private bool isDragging;
    private bool isSnappingBack;
    private bool choiceCommitted;
    private bool inputEnabled = true;
    private Canvas parentCanvas;
    private float canvasScaleFactor = 1f;
    private Coroutine discardRoutine;

    /// <summary>Current normalized swipe value in [-1, 1].</summary>
    public float NormalizedSwipe { get; private set; }

    /// <summary>True while a discard fly-off animation is playing.</summary>
    public bool IsDiscarding { get; private set; }

    public RectTransform RectTransform => rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        startAnchoredPosition = rectTransform.anchoredPosition;
        startLocalRotation = rectTransform.localRotation;
        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
            canvasScaleFactor = parentCanvas.scaleFactor;
    }

    private void Update()
    {
        if (!isSnappingBack)
            return;

        rectTransform.anchoredPosition = Vector2.Lerp(
            rectTransform.anchoredPosition,
            startAnchoredPosition,
            Time.deltaTime * snapBackSpeed);

        rectTransform.localRotation = Quaternion.Lerp(
            rectTransform.localRotation,
            startLocalRotation,
            Time.deltaTime * snapBackSpeed);

        if (Vector2.Distance(rectTransform.anchoredPosition, startAnchoredPosition) < 0.5f)
        {
            rectTransform.anchoredPosition = startAnchoredPosition;
            rectTransform.localRotation = startLocalRotation;
            NormalizedSwipe = 0f;
            OnSwipeProgress?.Invoke(0f);
            isSnappingBack = false;
        }
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!inputEnabled || choiceCommitted || IsDiscarding)
            return;

        isDragging = true;
        isSnappingBack = false;
        dragStartScreenPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || choiceCommitted || IsDiscarding)
            return;

        float deltaX = (eventData.position.x - dragStartScreenPos.x) / canvasScaleFactor;
        float clampedX = Mathf.Clamp(deltaX, -maxDragDistance, maxDragDistance);

        rectTransform.anchoredPosition = startAnchoredPosition + new Vector2(clampedX, 0f);
        ApplyTiltFromDrag(clampedX);

        NormalizedSwipe = Mathf.Clamp(clampedX / swipeThreshold, -1f, 1f);
        OnSwipeProgress?.Invoke(NormalizedSwipe);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging || choiceCommitted || IsDiscarding)
            return;

        isDragging = false;

        float deltaX = (eventData.position.x - dragStartScreenPos.x) / canvasScaleFactor;

        if (deltaX <= -swipeThreshold)
        {
            CommitSwipeLeft();
        }
        else if (deltaX >= swipeThreshold)
        {
            CommitSwipeRight();
        }
        else
        {
            ResetCardPosition();
        }
    }

    /// <summary>
    /// Confirms a left swipe and invokes listeners.
    /// </summary>
    public void CommitSwipeLeft()
    {
        if (choiceCommitted)
            return;

        choiceCommitted = true;
        NormalizedSwipe = -1f;
        OnSwipeProgress?.Invoke(NormalizedSwipe);
        OnSwipeLeft?.Invoke();
        onSwipeLeft?.Invoke();
    }

    /// <summary>
    /// Confirms a right swipe and invokes listeners.
    /// </summary>
    public void CommitSwipeRight()
    {
        if (choiceCommitted)
            return;

        choiceCommitted = true;
        NormalizedSwipe = 1f;
        OnSwipeProgress?.Invoke(NormalizedSwipe);
        OnSwipeRight?.Invoke();
        onSwipeRight?.Invoke();
    }

    /// <summary>
    /// Smoothly returns the card to its resting position and clears progress.
    /// </summary>
    public void ResetCardPosition()
    {
        isDragging = false;
        isSnappingBack = true;
        choiceCommitted = false;
    }

    /// <summary>
    /// Tilts the card in the drag direction. Primary tilt maps to the swipe threshold;
    /// a lighter secondary tilt continues toward max drag distance.
    /// </summary>
    private void ApplyTiltFromDrag(float clampedDeltaX)
    {
        float threshold = Mathf.Max(1f, swipeThreshold);
        float primary = Mathf.Clamp(clampedDeltaX / threshold, -1f, 1f);
        float remainder = 0f;

        if (Mathf.Abs(clampedDeltaX) > threshold && maxDragDistance > threshold)
        {
            float over = (Mathf.Abs(clampedDeltaX) - threshold) / (maxDragDistance - threshold);
            remainder = Mathf.Sign(clampedDeltaX) * Mathf.Clamp01(over) * overdragTiltScale;
        }

        float tiltNormalized = Mathf.Clamp(primary + remainder, -1f - overdragTiltScale, 1f + overdragTiltScale);
        float tiltDegrees = tiltNormalized * maxTiltDegrees;

        // Positive X (right) → clockwise Z so the card leans into the swipe.
        rectTransform.localRotation = startLocalRotation * Quaternion.Euler(0f, 0f, -tiltDegrees);
    }

    /// <summary>
    /// Flies the card off-screen in the swipe direction, then resets it for reuse.
    /// </summary>
    public Coroutine DiscardCard(bool toTheLeft, Action onComplete = null)
    {
        if (discardRoutine != null)
            StopCoroutine(discardRoutine);

        discardRoutine = StartCoroutine(DiscardRoutine(toTheLeft, onComplete));
        return discardRoutine;
    }

    private IEnumerator DiscardRoutine(bool toTheLeft, Action onComplete)
    {
        IsDiscarding = true;
        isDragging = false;
        isSnappingBack = false;
        inputEnabled = false;

        Vector2 from = rectTransform.anchoredPosition;
        float direction = toTheLeft ? -1f : 1f;
        Vector2 to = startAnchoredPosition + new Vector2(direction * discardDistance, 0f);
        Quaternion fromRot = rectTransform.localRotation;
        Quaternion toRot = startLocalRotation * Quaternion.Euler(0f, 0f, -direction * maxTiltDegrees * 2f);

        float elapsed = 0f;
        while (elapsed < discardDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / discardDuration);
            float eased = t * t * (3f - 2f * t);
            rectTransform.anchoredPosition = Vector2.Lerp(from, to, eased);
            rectTransform.localRotation = Quaternion.Lerp(fromRot, toRot, eased);
            yield return null;
        }

        PrepareForNextCard();
        IsDiscarding = false;
        discardRoutine = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Instantly restores the card and allows a new swipe (e.g. after loading the next event).
    /// </summary>
    public void PrepareForNextCard()
    {
        isDragging = false;
        isSnappingBack = false;
        choiceCommitted = false;
        IsDiscarding = false;
        inputEnabled = true;
        NormalizedSwipe = 0f;
        rectTransform.anchoredPosition = startAnchoredPosition;
        rectTransform.localRotation = startLocalRotation;
        OnSwipeProgress?.Invoke(0f);
    }
}
