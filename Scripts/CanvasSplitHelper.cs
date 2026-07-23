using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Holds references after splitting one Canvas into Static + Dynamic canvases.
///
/// Why: Unity rebuilds an entire Canvas mesh when any child Graphic dirties.
/// Keeping moving UI (card, sliders) on a Dynamic Canvas isolates rebuild cost
/// from static backgrounds / labels on a Static Canvas — big win on low-end Android.
///
/// Manual setup (if not using the Editor menu):
/// 1. Duplicate your root Canvas → rename to "StaticCanvas" and "DynamicCanvas".
/// 2. Match Canvas Scaler settings on both (same reference resolution / match).
/// 3. StaticCanvas: backgrounds, logos, static TMP labels, non-moving frames.
///    Optional: remove GraphicRaycaster if nothing is clickable there.
/// 4. DynamicCanvas: swipeable card, stat sliders, choice hints, particles that follow UI.
///    Keep GraphicRaycaster here. sortingOrder = StaticCanvas.sortingOrder + 1.
/// 5. Assign both canvases below; call ApplyRecommendedSettings() or use the menu.
/// </summary>
public class CanvasSplitHelper : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private Canvas staticCanvas;
    [SerializeField] private Canvas dynamicCanvas;

    [Header("Typical dynamic roots (optional wiring)")]
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private RectTransform statsHudRoot;
    [SerializeField] private RectTransform inventoryBarRoot;

    public Canvas StaticCanvas => staticCanvas;
    public Canvas DynamicCanvas => dynamicCanvas;

    /// <summary>
    /// Applies mobile-friendly Canvas flags. Safe to call at runtime or from the Editor menu.
    /// </summary>
    [ContextMenu("Apply Recommended Settings")]
    public void ApplyRecommendedSettings()
    {
        ConfigureCanvas(staticCanvas, sortingOrder: 0, needsRaycaster: false);
        ConfigureCanvas(dynamicCanvas, sortingOrder: 1, needsRaycaster: true);

        // Pixel-perfect often costs more than it helps on dense Android UI.
        if (staticCanvas != null)
            staticCanvas.pixelPerfect = false;
        if (dynamicCanvas != null)
            dynamicCanvas.pixelPerfect = false;
    }

    /// <summary>
    /// Moves known dynamic roots under the Dynamic Canvas (keeps world pose).
    /// </summary>
    [ContextMenu("Reparent Wired Dynamic Roots")]
    public void ReparentWiredDynamicRoots()
    {
        if (dynamicCanvas == null)
        {
            Debug.LogWarning("CanvasSplitHelper: Dynamic Canvas is not assigned.");
            return;
        }

        Transform parent = dynamicCanvas.transform;
        Reparent(cardRoot, parent);
        Reparent(statsHudRoot, parent);
        Reparent(inventoryBarRoot, parent);
    }

    private static void ConfigureCanvas(Canvas canvas, int sortingOrder, bool needsRaycaster)
    {
        if (canvas == null)
            return;

        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (needsRaycaster)
        {
            if (raycaster == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }
        else if (raycaster != null)
        {
            // Static canvas should not participate in raycasts.
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(raycaster);
            else
#endif
                Object.Destroy(raycaster);
        }

        // Keep scalers in sync if both present.
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            if (scaler.referenceResolution.x < 1f || scaler.referenceResolution.y < 1f)
                scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }
    }

    private static void Reparent(RectTransform child, Transform parent)
    {
        if (child == null || parent == null || child.parent == parent)
            return;

        child.SetParent(parent, worldPositionStays: true);
    }
}
