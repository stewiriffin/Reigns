using System.Collections;
using UnityEngine;

/// <summary>
/// Coroutine-based camera shake for danger beats and game over.
/// </summary>
public class ScreenShake : MonoBehaviour
{
    public static ScreenShake Instance { get; private set; }

    [SerializeField] private Transform target;
    [SerializeField] private float dangerMagnitude = 0.12f;
    [SerializeField] private float dangerDuration = 0.25f;
    [SerializeField] private float gameOverMagnitude = 0.28f;
    [SerializeField] private float gameOverDuration = 0.55f;

    private Vector3 homePosition;
    private Coroutine shakeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (target == null && Camera.main != null)
            target = Camera.main.transform;

        if (target != null)
            homePosition = target.localPosition;
    }

    private void OnDisable()
    {
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        ResetPose();
    }

    public void ShakeDanger()
    {
        Shake(dangerMagnitude, dangerDuration);
    }

    public void ShakeGameOver()
    {
        Shake(gameOverMagnitude, gameOverDuration);
    }

    public void Shake(float magnitude, float duration)
    {
        if (target == null)
            return;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        shakeRoutine = StartCoroutine(ShakeRoutine(magnitude, duration));
    }

    private IEnumerator ShakeRoutine(float magnitude, float duration)
    {
        homePosition = target.localPosition;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float falloff = 1f - Mathf.Clamp01(elapsed / duration);
            Vector2 offset = Random.insideUnitCircle * (magnitude * falloff);
            target.localPosition = homePosition + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        ResetPose();
        shakeRoutine = null;
    }

    private void ResetPose()
    {
        if (target != null)
            target.localPosition = homePosition;
    }
}
