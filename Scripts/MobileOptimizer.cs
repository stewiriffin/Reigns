using UnityEngine;

/// <summary>
/// Battery-friendly defaults for a UI-heavy 2D Android card game.
/// Caps FPS, disables vSync override fights, and turns off unused motion sensors.
/// </summary>
public class MobileOptimizer : MonoBehaviour
{
    [SerializeField] private int targetFrameRate = 30;
    [SerializeField] private bool dontDestroyOnLoad = true;

    private void Awake()
    {
        ApplyOptimizations();

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Applies low-power rendering and input settings suitable for tap-only UI games.
    /// </summary>
    public void ApplyOptimizations()
    {
        // vSync must be off or Android may ignore targetFrameRate.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(15, targetFrameRate);

        DisableUnusedSensors();

        Debug.Log(
            $"MobileOptimizer: targetFrameRate={Application.targetFrameRate}, " +
            $"vSyncCount={QualitySettings.vSyncCount}, gyro={Input.gyro.enabled}, compass={Input.compass.enabled}");
    }

    private static void DisableUnusedSensors()
    {
        // Gyroscope / compass are unnecessary for a screen-tap card game and waste power if left on.
        if (SystemInfo.supportsGyroscope)
            Input.gyro.enabled = false;

        Input.compass.enabled = false;
    }
}
