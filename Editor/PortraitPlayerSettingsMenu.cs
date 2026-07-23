#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click Player Settings for Android portrait-only builds.
/// </summary>
public static class PortraitPlayerSettingsMenu
{
    [MenuItem("Reigns/Apply Android Portrait Player Settings")]
    public static void Apply()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;

        // Android-specific presentation defaults useful for tall phones.
        PlayerSettings.Android.renderOutsideSafeArea = true;
        PlayerSettings.Android.resizableWindow = false;

        Debug.Log("Reigns: Android Player Settings set to Portrait-only. Also enable 'Render outside safe area' is ON so SafeAreaFitter can inset UI itself.");
        AssetDatabase.SaveAssets();
    }
}
#endif
