/// <summary>
/// Android GC checklist for the Reigns swipe / resolve loop.
/// Keep allocations off Update, drag, and discard coroutines.
/// </summary>
public static class AndroidHotPathChecklist
{
    /*
     DO on the swipe hot path:
     - Reuse one card RectTransform (CardSwipeHandler already does).
     - Update TMP via SetText / SetText(format, int) / SetText(StringBuilder) — not string.Format or $"...".
     - Pool particles and UI icons (ObjectPool / CardViewPool / StatFeedbackParticles).
     - Cache GetComponent results in Awake (slider RectTransforms, Canvas scale).
     - Skip redundant HUD writes when the displayed value did not change.
     - Gate Debug.Log behind UNITY_EDITOR || DEVELOPMENT_BUILD (logging allocates heavily).

     DO NOT on the swipe hot path:
     - Instantiate / Destroy / new GameObject
     - string.Format, interpolation, ToString, Trim on every frame
     - LINQ in Update
     - GetComponent / FindObjectOfType per frame
     - new Material / new Gradient / new arrays when replaying VFX
     - UnityEvent spam; prefer cached Action delegates already used by CardSwipeHandler

     VERIFY in a Development Android build:
     - Profiler → Memory → GC Alloc on CardSwipeHandler.Update / ApplyVisualDrag
     - Deep Profile briefly; target 0 B on drag frames
     - Fixed 30 FPS via MobileOptimizer reduces GC frequency pressure
    */
}
