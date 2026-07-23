using UnityEngine;

/// <summary>
/// Optional Resources loader for adaptive BGM clips.
/// Place loops at Resources/Audio/bgm_normal and Resources/Audio/bgm_crisis
/// (any Unity audio extension). Assigns them onto <see cref="DynamicMusicController"/> at boot.
/// </summary>
public class DynamicMusicClipBootstrap : MonoBehaviour
{
    [SerializeField] private string normalResourcePath = "Audio/bgm_normal";
    [SerializeField] private string crisisResourcePath = "Audio/bgm_crisis";
    [SerializeField] private DynamicMusicController target;

    private void Start()
    {
        if (target == null)
            target = DynamicMusicController.Instance != null
                ? DynamicMusicController.Instance
                : FindObjectOfType<DynamicMusicController>();

        if (target == null || target.HasClips)
            return;

        AudioClip normal = Resources.Load<AudioClip>(normalResourcePath);
        AudioClip crisis = Resources.Load<AudioClip>(crisisResourcePath);
        if (normal == null || crisis == null)
            return;

        target.SetClips(normal, crisis, restart: true);
    }
}
