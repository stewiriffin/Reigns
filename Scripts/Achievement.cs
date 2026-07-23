using UnityEngine;

/// <summary>
/// Achievement definition. Runtime unlock state is synced from PlayerPrefs by
/// <see cref="AchievementManager"/> so asset files stay clean in version control.
/// </summary>
[CreateAssetMenu(fileName = "Achievement", menuName = "Reigns/Achievement", order = 10)]
public class Achievement : ScriptableObject
{
    [Tooltip("Stable key used for PlayerPrefs persistence.")]
    public string id;

    public string title;

    [TextArea(2, 4)]
    public string description;

    public Sprite icon;

    /// <summary>
    /// Runtime unlock flag. Persisted via AchievementManager, not the asset file.
    /// </summary>
    [System.NonSerialized]
    public bool IsUnlocked;
}
