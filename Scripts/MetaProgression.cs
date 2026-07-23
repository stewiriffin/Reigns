using UnityEngine;

/// <summary>
/// Persistent boolean meta-progression flags stored in PlayerPrefs
/// (e.g. "Discovered_Dragon" unlocked after a specific choice).
/// </summary>
public static class MetaProgression
{
    private const string FlagPrefix = "MetaFlag_";

    public static bool HasFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return false;

        return PlayerPrefs.GetInt(FlagKey(flag), 0) == 1;
    }

    public static void SetFlag(string flag, bool value = true)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return;

        PlayerPrefs.SetInt(FlagKey(flag), value ? 1 : 0);
        PlayerPrefs.Save();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"MetaProgression: '{flag}' = {value}");
#endif
    }

    public static void ClearFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return;

        PlayerPrefs.DeleteKey(FlagKey(flag));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Debug/helper: wipe all known meta flags by deleting keys with the meta prefix.
    /// PlayerPrefs has no key enumeration on all platforms, so pass the flags you care about.
    /// </summary>
    public static void ClearFlags(params string[] flags)
    {
        if (flags == null)
            return;

        foreach (string flag in flags)
            ClearFlag(flag);
    }

    private static string FlagKey(string flag)
    {
        return FlagPrefix + flag.Trim();
    }
}
