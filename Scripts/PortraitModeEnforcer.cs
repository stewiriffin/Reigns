using UnityEngine;

/// <summary>
/// Forces the app into strict Portrait orientation (Android-focused).
/// Place on a bootstrap GameObject that loads with the first scene.
///
/// Also set these in Unity (Player Settings → Android → Resolution and Presentation):
///   Default Orientation = Portrait
///   Allowed Orientations: Portrait ONLY (disable Landscape Left/Right and Portrait Upside Down)
///
/// Canvas recommendations for mobile:
///   Render Mode = Screen Space - Overlay (or Camera)
///   UI Scale Mode = Scale With Screen Size
///   Reference Resolution = 1080 x 1920
///   Screen Match Mode = Match Width Or Height
///   Match = 0.5
///   Put SafeAreaFitter on a full-stretch panel under the Canvas; parent gameplay UI under that panel.
/// </summary>
public class PortraitModeEnforcer : MonoBehaviour
{
    [SerializeField] private bool dontDestroyOnLoad = true;

    private void Awake()
    {
        ApplyPortraitLock();

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            ApplyPortraitLock();
    }

    /// <summary>
    /// Locks autorotation so only upright portrait is allowed.
    /// </summary>
    public static void ApplyPortraitLock()
    {
        Screen.autorotateToPortrait = true;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.orientation = ScreenOrientation.Portrait;
    }
}
