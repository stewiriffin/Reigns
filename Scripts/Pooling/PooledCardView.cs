using UnityEngine;

/// <summary>
/// Marker / reset hook for pooled card face GameObjects.
/// The interactive swipe card is normally a single reused RectTransform;
/// use this pool when spawning extra card faces (stack peek, discard ghost, etc.).
/// </summary>
public class PooledCardView : MonoBehaviour
{
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;

    public RectTransform RectTransform => rectTransform != null ? rectTransform : (RectTransform)transform;

    private void Awake()
    {
        if (rectTransform == null)
            rectTransform = transform as RectTransform;
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    public void OnSpawned()
    {
        gameObject.SetActive(true);
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }

    public void OnDespawned()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
        gameObject.SetActive(false);
    }

    public void ResetPose(Vector2 anchoredPosition, Quaternion localRotation)
    {
        RectTransform rt = RectTransform;
        rt.anchoredPosition = anchoredPosition;
        rt.localRotation = localRotation;
        rt.localScale = Vector3.one;
    }
}
