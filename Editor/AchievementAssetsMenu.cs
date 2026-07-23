#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the four default Reigns achievement ScriptableObjects under Resources/Achievements.
/// </summary>
public static class AchievementAssetsMenu
{
    private const string Folder = "Assets/Resources/Achievements";

    [MenuItem("Reigns/Create Default Achievement Assets")]
    public static void CreateDefaults()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Resources", "Achievements");

        Create(
            AchievementManager.IdSurvive10Years,
            "Decade on the Throne",
            "Survive for 10 years.");
        Create(
            AchievementManager.IdDieArmyEmpty,
            "Defenseless",
            "Die with Army at 0.");
        Create(
            AchievementManager.IdUnlock10Cards,
            "Court Chronicles",
            "Unlock 10 cards.");
        Create(
            AchievementManager.IdMaxWealth,
            "Overflowing Coffers",
            "Max out the Wealth stat.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Reigns: default achievements created in {Folder}");
    }

    private static void Create(string id, string title, string description)
    {
        string path = Path.Combine(Folder, id + ".asset").Replace('\\', '/');
        Achievement existing = AssetDatabase.LoadAssetAtPath<Achievement>(path);
        if (existing != null)
        {
            existing.id = id;
            existing.title = title;
            existing.description = description;
            EditorUtility.SetDirty(existing);
            return;
        }

        Achievement asset = ScriptableObject.CreateInstance<Achievement>();
        asset.id = id;
        asset.title = title;
        asset.description = description;
        AssetDatabase.CreateAsset(asset, path);
    }
}
#endif
