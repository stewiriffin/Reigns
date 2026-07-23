#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Reminder helpers for enabling DOTween juice after importing Demigiant DOTween.
/// </summary>
public static class DOTweenJuiceSetupMenu
{
    [MenuItem("Reigns/Juice/Enable DOTWEEN Scripting Define")]
    public static void EnableDotweenDefine()
    {
        AddDefine("DOTWEEN");
        EditorUtility.DisplayDialog(
            "DOTween Juice",
            "Added scripting define DOTWEEN for the active build target.\n\n" +
            "1. Import free DOTween from the Asset Store (Demigiant).\n" +
            "2. Run DOTween Utility Panel → Setup DOTween (enable UI module for Slider.DOValue).\n" +
            "3. Enter Play Mode to feel card draw bounce, slider lerps, and blocked-swipe shake.",
            "OK");
    }

    private static void AddDefine(string symbol)
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        if (defines.Contains(symbol))
            return;

        if (string.IsNullOrEmpty(defines))
            defines = symbol;
        else
            defines += ";" + symbol;

        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
    }
}
#endif
