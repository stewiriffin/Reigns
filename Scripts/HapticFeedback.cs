using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Cross-platform mobile haptics (light tick vs heavy impact).
/// Android uses VibrationEffect; iOS uses UIImpactFeedbackGenerator via a native plugin.
/// </summary>
public static class HapticFeedback
{
    public enum Intensity
    {
        Light,
        Medium,
        Heavy
    }

    private static bool hapticsEnabled = true;

    public static bool Enabled
    {
        get => hapticsEnabled;
        set => hapticsEnabled = value;
    }

    public static void PlayLight()
    {
        Play(Intensity.Light);
    }

    public static void PlayHeavy()
    {
        Play(Intensity.Heavy);
    }

    public static void Play(Intensity intensity)
    {
        if (!hapticsEnabled)
            return;

#if UNITY_EDITOR
        // No device vibrator in Editor — keep silent to avoid spam.
        return;
#elif UNITY_ANDROID
        PlayAndroid(intensity);
#elif UNITY_IOS
        PlayIOS(intensity);
#else
        if (intensity == Intensity.Heavy)
            Handheld.Vibrate();
#endif
    }

#if UNITY_ANDROID
    private static void PlayAndroid(Intensity intensity)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vibrator == null)
                    return;

                bool hasVibrator = vibrator.Call<bool>("hasVibrator");
                if (!hasVibrator)
                    return;

                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdkInt = version.GetStatic<int>("SDK_INT");
                    if (sdkInt >= 29)
                    {
                        // EFFECT_TICK = 2, EFFECT_CLICK = 0, EFFECT_HEAVY_CLICK = 5
                        int effectId = intensity switch
                        {
                            Intensity.Light => 2,
                            Intensity.Medium => 0,
                            Intensity.Heavy => 5,
                            _ => 0
                        };

                        using (var vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                                   "createPredefined", effectId))
                        {
                            vibrator.Call("vibrate", effect);
                        }
                    }
                    else if (sdkInt >= 26)
                    {
                        long millis = intensity == Intensity.Heavy ? 40L : 12L;
                        int amp = intensity == Intensity.Heavy ? 255 : 80;
                        using (var vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                                   "createOneShot", millis, amp))
                        {
                            vibrator.Call("vibrate", effect);
                        }
                    }
                    else
                    {
                        long millis = intensity == Intensity.Heavy ? 40L : 12L;
                        vibrator.Call("vibrate", millis);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"HapticFeedback Android failed: {e.Message}");
        }
    }
#endif

#if UNITY_IOS
    private static void PlayIOS(Intensity intensity)
    {
        switch (intensity)
        {
            case Intensity.Light:
                _HapticLight();
                break;
            case Intensity.Medium:
                _HapticMedium();
                break;
            case Intensity.Heavy:
                _HapticHeavy();
                break;
        }
    }

    [DllImport("__Internal")]
    private static extern void _HapticLight();

    [DllImport("__Internal")]
    private static extern void _HapticMedium();

    [DllImport("__Internal")]
    private static extern void _HapticHeavy();
#endif
}
