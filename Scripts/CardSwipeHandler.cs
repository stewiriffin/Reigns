using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Android-optimized card swipe: primary-finger only, touch deadzone, and Input.GetTouch deltas.
/// Mouse input remains supported for Editor play mode.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CardSwipeHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    private const int MousePointerId = -1;
    private const int NoFinger = int.MinValue;

    [Header("Swipe")]
    [Tooltip("Horizontal distance (in UI units) required to confirm a swipe.")]
    [SerializeField] private float swipeThreshold = 200f;

    [Tooltip("Max horizontal travel while dragging (clamps the card).")]
    [SerializeField] private float maxDragDistance = 350f;

    [Tooltip("Screen-pixel deadzone before the card starts moving (resting-thumb filter).")]
    [SerializeField] private float touchDeadzonePixels = 20f;

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
    private bool isDragging;
    private bool isSnappingBack;
    private bool choiceCommitted;
    private bool inputEnabled = true;
    private bool crossedDecisionThreshold;
    private bool deadzoneUnlocked;
    private SwipeDirectionLock directionLock = SwipeDirectionLock.Both;
    private int activeFingerId = NoFinger;
    private float accumulatedScreenDeltaX;
    private float lastMouseScreenX;
    private Canvas parentCanvas;
    private float canvasScaleFactor = 1f;
    private Coroutine discardRoutine;
    private Vector2 dragPositionScratch;
    private Vector2 discardTargetScratch;
    private CardUIJuice cardJuice;
    private bool blockedShakeFiredThisDrag;

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

        cardJuice = GetComponent<CardUIJuice>();
        if (cardJuice == null)
            cardJuice = gameObject.AddComponent<CardUIJuice>();
    }

    private void Update()
    {
        if (isSnappingBack)
            UpdateSnapBack();

        if (!isDragging || choiceCommitted || IsDiscarding)
            return;

        // Prefer native touch deltas for the locked primary finger.
        if (activeFingerId != MousePointerId && activeFingerId != NoFinger)
        {
            UpdateFromPrimaryTouch();
            return;
        }

        // Editor / mouse fallback.
        if (activeFingerId == MousePointerId)
            UpdateFromMouse();
    }

    private void UpdateSnapBack()
    {
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

    /// <summary>
    /// Tracks only the active finger via Input.GetTouch. Extra fingers are ignored.
    /// </summary>
    private void UpdateFromPrimaryTouch()
    {
        if (!TryGetActiveTouch(out Touch touch))
        {
            // Primary finger vanished without an end event — cancel cleanly.
            FinishDrag();
            return;
        }

        switch (touch.phase)
        {
            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                // deltaPosition is hardware touch movement since last frame (Android-friendly).
                ApplyScreenDelta(touch.deltaPosition.x);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                FinishDrag();
                break;
        }
    }

    private void UpdateFromMouse()
    {
        if (!Input.GetMouseButton(0))
        {
            FinishDrag();
            return;
        }

        float mouseX = Input.mousePosition.x;
        ApplyScreenDelta(mouseX - lastMouseScreenX);
        lastMouseScreenX = mouseX;
    }

    private bool TryGetActiveTouch(out Touch touch)
    {
        touch = default;

        // Secondary fingers may exist (touchCount > 1); we still only resolve our fingerId.
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch candidate = Input.GetTouch(i);
            if (candidate.fingerId == activeFingerId)
            {
                touch = candidate;
                return true;
            }
        }

        return false;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
    }

    /// <summary>
    /// Locks commits to one direction (tutorial) or allows both.
    /// </summary>
    public void SetDirectionLock(SwipeDirectionLock lockMode)
    {
        directionLock = lockMode;
    }

    public SwipeDirectionLock DirectionLock => directionLock;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!inputEnabled || choiceCommitted || IsDiscarding)
            return;

        // Already locked to the first finger — ignore additional contacts.
        if (isDragging)
            return;

        BeginTracking(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!inputEnabled || choiceCommitted || IsDiscarding)
        {
            // Resolving / locked out — still give feedback if they try to swipe.
            if (!inputEnabled && cardJuice != null)
                cardJuice.PlayBlockedSwipeShake();
            return;
        }

        if (isDragging)
        {
            // Multi-touch: reject any drag that isn't the primary finger.
            if (eventData.pointerId != activeFingerId)
                return;
            return;
        }

        BeginTracking(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Movement is driven by Input.GetTouch / mouse in Update.
        // Still gate EventSystem callbacks so secondary pointers do nothing.
        if (!isDragging || eventData.pointerId != activeFingerId)
            return;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.pointerId != activeFingerId)
            return;

        // Touch path usually ends in Update; this covers mouse / EventSystem end.
        if (activeFingerId == MousePointerId)
            FinishDrag();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isDragging || eventData.pointerId != activeFingerId)
            return;

        if (activeFingerId == MousePointerId)
            FinishDrag();
    }

    private void BeginTracking(PointerEventData eventData)
    {
        isDragging = true;
        isSnappingBack = false;
        crossedDecisionThreshold = false;
        deadzoneUnlocked = false;
        blockedShakeFiredThisDrag = false;
        accumulatedScreenDeltaX = 0f;
        activeFingerId = eventData.pointerId;
        lastMouseScreenX = eventData.position.x;

        ApplyVisualDrag(0f);
    }

    /// <summary>
    /// Accumulates horizontal screen-pixel movement, applies deadzone, then moves the card.
    /// </summary>
    private void ApplyScreenDelta(float deltaScreenX)
    {
        if (Mathf.Abs(deltaScreenX) < 0.01f)
            return;

        accumulatedScreenDeltaX += deltaScreenX;

        float visualScreenX = ApplyDeadzone(accumulatedScreenDeltaX);
        float uiDeltaX = visualScreenX / Mathf.Max(0.0001f, canvasScaleFactor);
        ApplyVisualDrag(uiDeltaX);
    }

    private float ApplyDeadzone(float accumulatedScreenX)
    {
        float abs = Mathf.Abs(accumulatedScreenX);
        if (!deadzoneUnlocked)
        {
            if (abs < touchDeadzonePixels)
                return 0f;

            deadzoneUnlocked = true;
        }

        // Keep a deadzone offset so unlocking doesn't pop the card by 20px.
        return accumulatedScreenX - Mathf.Sign(accumulatedScreenX) * touchDeadzonePixels;
    }

    private void ApplyVisualDrag(float uiDeltaX)
    {
        float attemptedDelta = uiDeltaX;

        // Soft-clamp drag into the allowed tutorial direction.
        if (directionLock == SwipeDirectionLock.RightOnly && uiDeltaX < 0f)
            uiDeltaX = 0f;
        else if (directionLock == SwipeDirectionLock.LeftOnly && uiDeltaX > 0f)
            uiDeltaX = 0f;

        // Player tried a forbidden swipe direction — juice feedback once per drag.
        if (!Mathf.Approximately(attemptedDelta, uiDeltaX) &&
            Mathf.Abs(attemptedDelta) > swipeThreshold * 0.25f)
        {
            TryBlockedSwipeShake();
        }

        float clampedX = Mathf.Clamp(uiDeltaX, -maxDragDistance, maxDragDistance);

        dragPositionScratch.x = startAnchoredPosition.x + clampedX;
        dragPositionScratch.y = startAnchoredPosition.y;
        rectTransform.anchoredPosition = dragPositionScratch;
        ApplyTiltFromDrag(clampedX);

        NormalizedSwipe = Mathf.Clamp(clampedX / swipeThreshold, -1f, 1f);
        UpdateDecisionThresholdHaptic(Mathf.Abs(clampedX));
        OnSwipeProgress?.Invoke(NormalizedSwipe);
    }

    private void TryBlockedSwipeShake()
    {
        if (blockedShakeFiredThisDrag)
            return;

        blockedShakeFiredThisDrag = true;
        if (cardJuice != null)
            cardJuice.PlayBlockedSwipeShake();
    }

    private void FinishDrag()
    {
        if (!isDragging || choiceCommitted || IsDiscarding)
        {
            ClearFingerLock();
            return;
        }

        isDragging = false;

        float visualScreenX = deadzoneUnlocked
            ? accumulatedScreenDeltaX - Mathf.Sign(accumulatedScreenDeltaX) * touchDeadzonePixels
            : 0f;
        float uiDeltaX = visualScreenX / Mathf.Max(0.0001f, canvasScaleFactor);

        // Enforce direction lock on commit.
        if (directionLock == SwipeDirectionLock.RightOnly && uiDeltaX < 0f)
            uiDeltaX = 0f;
        else if (directionLock == SwipeDirectionLock.LeftOnly && uiDeltaX > 0f)
            uiDeltaX = 0f;

        ClearFingerLock();

        if (uiDeltaX <= -swipeThreshold)
        {
            if (directionLock == SwipeDirectionLock.RightOnly)
            {
                TryBlockedSwipeShake();
                ResetCardPosition();
            }
            else
                CommitSwipeLeft();
        }
        else if (uiDeltaX >= swipeThreshold)
        {
            if (directionLock == SwipeDirectionLock.LeftOnly)
            {
                TryBlockedSwipeShake();
                ResetCardPosition();
            }
            else
                CommitSwipeRight();
        }
        else
        {
            ResetCardPosition();
        }
    }

    private void ClearFingerLock()
    {
        activeFingerId = NoFinger;
        accumulatedScreenDeltaX = 0f;
        deadzoneUnlocked = false;
    }

    private void UpdateDecisionThresholdHaptic(float absDeltaX)
    {
        bool pastThreshold = absDeltaX >= swipeThreshold;

        if (pastThreshold && !crossedDecisionThreshold)
        {
            crossedDecisionThreshold = true;
            HapticFeedback.PlayLight();
        }
        else if (!pastThreshold && crossedDecisionThreshold)
        {
            crossedDecisionThreshold = false;
        }
    }

    public void CommitSwipeLeft()
    {
        if (choiceCommitted)
            return;

        choiceCommitted = true;
        isDragging = false;
        ClearFingerLock();
        NormalizedSwipe = -1f;
        OnSwipeProgress?.Invoke(NormalizedSwipe);
        OnSwipeLeft?.Invoke();
        onSwipeLeft?.Invoke();
    }

    public void CommitSwipeRight()
    {
        if (choiceCommitted)
            return;

        choiceCommitted = true;
        isDragging = false;
        ClearFingerLock();
        NormalizedSwipe = 1f;
        OnSwipeProgress?.Invoke(NormalizedSwipe);
        OnSwipeRight?.Invoke();
        onSwipeRight?.Invoke();
    }

    public void ResetCardPosition()
    {
        isDragging = false;
        isSnappingBack = true;
        choiceCommitted = false;
        ClearFingerLock();
    }

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
        rectTransform.localRotation = startLocalRotation * Quaternion.Euler(0f, 0f, -tiltDegrees);
    }

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
        ClearFingerLock();

        Vector2 from = rectTransform.anchoredPosition;
        float direction = toTheLeft ? -1f : 1f;
        discardTargetScratch.x = startAnchoredPosition.x + direction * discardDistance;
        discardTargetScratch.y = startAnchoredPosition.y;
        Vector2 to = discardTargetScratch;
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

    public void PrepareForNextCard()
    {
        isDragging = false;
        isSnappingBack = false;
        choiceCommitted = false;
        crossedDecisionThreshold = false;
        IsDiscarding = false;
        inputEnabled = true;
        blockedShakeFiredThisDrag = false;
        ClearFingerLock();
        NormalizedSwipe = 0f;
        rectTransform.anchoredPosition = startAnchoredPosition;
        rectTransform.localRotation = startLocalRotation;
        OnSwipeProgress?.Invoke(0f);

        if (cardJuice != null)
        {
            cardJuice.CaptureRestPose();
            cardJuice.PlayDrawAppear();
        }
    }
}
