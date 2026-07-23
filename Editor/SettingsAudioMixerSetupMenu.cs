#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Quick reference for wiring SettingsManager to an AudioMixer.
/// Unity mixer assets are created via the Project window (Create → Audio Mixer).
/// </summary>
public static class SettingsAudioMixerSetupMenu
{
    [MenuItem("Reigns/Settings/Audio Mixer Setup Guide")]
    public static void ShowGuide()
    {
        EditorUtility.DisplayDialog(
            "Audio Mixer Setup",
            "1. Project window → Create → Audio Mixer → name it MainMixer.\n" +
            "2. In the Mixer window, add groups: Master → BGM, Master → SFX.\n" +
            "3. Expose parameters (right-click Volume → Expose):\n" +
            "   - MasterVolume\n" +
            "   - BGMVolume\n" +
            "   - SFXVolume\n" +
            "4. Assign MainMixer + BGM/SFX groups on the SettingsManager component.\n" +
            "5. Play — volume sliders drive the mixer immediately and save to PlayerPrefs.",
            "OK");
    }
}
#endif
