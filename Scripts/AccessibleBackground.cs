using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marks a UI <see cref="Image"/> whose color should darken in High Contrast Mode.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class AccessibleBackground : MonoBehaviour
{
    [SerializeField] private Image target;
    [Tooltip("Multiplies RGB when high contrast is on (alpha unchanged).")]
    [SerializeField] [Range(0.05f, 1f)] private float darkenFactor = 0.35f;

    private Color originalColor = Color.white;
    private bool captured;

    private void Awake()
    {
        if (target == null)
            target = GetComponent<Image>();
        CaptureOriginal();
    }

    private void OnEnable()
    {
        if (AccessibilityManager.Instance != null)
            AccessibilityManager.Instance.RegisterBackground(this);
    }

    private void OnDisable()
    {
        if (AccessibilityManager.Instance != null)
            AccessibilityManager.Instance.UnregisterBackground(this);
    }

    public void CaptureOriginal()
    {
        if (target == null)
            target = GetComponent<Image>();
        if (target == null)
            return;

        originalColor = target.color;
        captured = true;
    }

    public void ApplyHighContrast(bool enabled)
    {
        if (target == null)
            return;

        if (!captured)
            CaptureOriginal();

        if (!enabled)
        {
            target.color = originalColor;
            return;
        }

        Color c = originalColor;
        c.r *= darkenFactor;
        c.g *= darkenFactor;
        c.b *= darkenFactor;
        // Keep dimmers readable but push solid panels nearly black.
        if (c.a >= 0.5f)
        {
            c.r = Mathf.Min(c.r, 0.12f);
            c.g = Mathf.Min(c.g, 0.11f);
            c.b = Mathf.Min(c.b, 0.1f);
        }

        target.color = c;
    }
}
