using UnityEngine;

/// <summary>
/// Fits a RectTransform to <see cref="Screen.safeArea"/> so UI stays clear of
/// notches, cutouts, and the Android gesture/navigation bar.
///
/// Setup:
/// 1. Canvas → Scale With Screen Size (e.g. 1080x1920, Match 0.5).
/// 2. Create a child panel stretched to the full canvas (anchors min 0,0 max 1,1; offsets 0).
/// 3. Add this component to that panel.
/// 4. Parent all HUD / card UI under the safe-area panel (not directly under Canvas).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("Re-apply when the safe area or screen size changes (rotation, foldables, multi-window).")]
    [SerializeField] private bool updateWhenScreenChanges = true;

    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private ScreenOrientation lastOrientation;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    private void OnEnable()
    {
        ApplySafeArea();
    }

    private void Update()
    {
        if (!updateWhenScreenChanges)
            return;

        if (HasScreenChanged())
            ApplySafeArea();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        // Keep the scene view roughly correct while editing.
        if (rectTransform != null)
            ApplySafeArea();
    }
#endif

    /// <summary>
    /// Reads Screen.safeArea and converts it into anchor/offset values in canvas space.
    /// </summary>
    public void ApplySafeArea()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (rectTransform == null)
            return;

        Rect safeArea = Screen.safeArea;
        lastSafeArea = safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastOrientation = Screen.orientation;

        // Normalize safe area into 0-1 anchors relative to the full screen/canvas.
        float minX = safeArea.xMin / Screen.width;
        float maxX = safeArea.xMax / Screen.width;
        float minY = safeArea.yMin / Screen.height;
        float maxY = safeArea.yMax / Screen.height;

        // Guard against zero-size screens during early init / editor oddities.
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            minX = 0f;
            maxX = 1f;
            minY = 0f;
            maxY = 1f;
        }

        rectTransform.anchorMin = new Vector2(minX, minY);
        rectTransform.anchorMax = new Vector2(maxX, maxY);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private bool HasScreenChanged()
    {
        return Screen.safeArea != lastSafeArea
               || Screen.width != lastScreenSize.x
               || Screen.height != lastScreenSize.y
               || Screen.orientation != lastOrientation;
    }
}
