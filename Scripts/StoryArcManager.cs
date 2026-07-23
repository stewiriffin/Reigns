using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks multi-step story arcs and unlocks Legendary Endings when an arc completes.
/// Progress is run-scoped (saved with the reign); unlocked endings persist forever.
/// Named story flags (e.g. Crusade_Initiated) mirror <see cref="MetaProgression"/>.
/// </summary>
public class StoryArcManager : MonoBehaviour
{
    private const string EndingPrefsPrefix = "StoryEnding_";
    private const string DefaultResourcePath = "StoryArcs/story_arcs";

    public static StoryArcManager Instance { get; private set; }

    [SerializeField] private string arcsResourcePath = DefaultResourcePath;

    private readonly List<StoryArcProgress> arcs = new List<StoryArcProgress>(8);
    private readonly Dictionary<string, StoryArcProgress> byId =
        new Dictionary<string, StoryArcProgress>(StringComparer.OrdinalIgnoreCase);

    private string pendingLegendaryEndingId;

    /// <summary>Fired when arc progress changes.</summary>
    public event Action OnProgressChanged;

    /// <summary>Fired the first time a Legendary Ending badge unlocks.</summary>
    public event Action<StoryArcDefinition> OnEndingUnlocked;

    /// <summary>Fired when an arc hits its final step and should end the reign.</summary>
    public event Action<StoryArcDefinition> OnLegendaryEndingReady;

    public IReadOnlyList<StoryArcProgress> Arcs => arcs;

    public string PendingLegendaryEndingId => pendingLegendaryEndingId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        LoadDefinitions();
        RefreshUnlockedFromPrefs();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ResetRunProgress()
    {
        pendingLegendaryEndingId = null;
        for (int i = 0; i < arcs.Count; i++)
            arcs[i].progress = 0;

        OnProgressChanged?.Invoke();
    }

    public int GetProgress(string arcId)
    {
        return byId.TryGetValue(arcId ?? string.Empty, out StoryArcProgress p) ? p.progress : 0;
    }

    public bool IsEndingUnlocked(string arcId)
    {
        if (string.IsNullOrWhiteSpace(arcId))
            return false;

        if (byId.TryGetValue(arcId.Trim(), out StoryArcProgress p) && p.endingUnlocked)
            return true;

        return PlayerPrefs.GetInt(EndingKey(arcId), 0) == 1;
    }

    public int CountUnlockedEndings()
    {
        int count = 0;
        for (int i = 0; i < arcs.Count; i++)
        {
            if (IsEndingUnlocked(arcs[i].arcId))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Sets a narrative flag (persists via MetaProgression). Used for gates like Crusade_Initiated.
    /// </summary>
    public void SetStoryFlag(string flag, bool value = true)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return;

        MetaProgression.SetFlag(flag, value);
        OnProgressChanged?.Invoke();
    }

    public bool HasStoryFlag(string flag) => MetaProgression.HasFlag(flag);

    /// <summary>
    /// Applies a choice arc delta. Returns true if progress changed.
    /// When an arc reaches required steps, unlocks the ending and queues a legendary finale.
    /// </summary>
    public bool ApplyArcDelta(string arcId, int delta)
    {
        if (string.IsNullOrWhiteSpace(arcId) || delta == 0)
            return false;

        if (!byId.TryGetValue(arcId.Trim(), out StoryArcProgress state))
        {
            Debug.LogWarning($"StoryArcManager: Unknown arc '{arcId}'.");
            return false;
        }

        int required = state.definition != null
            ? Mathf.Max(1, state.definition.requiredSteps)
            : 5;

        int before = state.progress;
        state.progress = Mathf.Clamp(state.progress + delta, 0, required);
        if (state.progress == before)
            return false;

        // Keep a mirrored integer flag for debugging / external tools.
        if (state.definition != null && !string.IsNullOrWhiteSpace(state.definition.flagKey))
            PlayerPrefs.SetInt("StoryArcFlag_" + state.definition.flagKey, state.progress);

        OnProgressChanged?.Invoke();

        if (state.progress >= required)
            TryCompleteArc(state);

        return true;
    }

    public StoryArcProgressSave[] CaptureSave()
    {
        var saves = new StoryArcProgressSave[arcs.Count];
        for (int i = 0; i < arcs.Count; i++)
        {
            saves[i] = new StoryArcProgressSave
            {
                arcId = arcs[i].arcId,
                progress = arcs[i].progress
            };
        }

        return saves;
    }

    public void LoadSave(StoryArcProgressSave[] saves)
    {
        ResetRunProgress();
        if (saves == null)
            return;

        for (int i = 0; i < saves.Length; i++)
        {
            StoryArcProgressSave save = saves[i];
            if (save == null || string.IsNullOrWhiteSpace(save.arcId))
                continue;

            if (!byId.TryGetValue(save.arcId.Trim(), out StoryArcProgress state))
                continue;

            int required = state.definition != null
                ? Mathf.Max(1, state.definition.requiredSteps)
                : 5;
            state.progress = Mathf.Clamp(save.progress, 0, required);
        }

        OnProgressChanged?.Invoke();
    }

    public StoryArcDefinition GetDefinition(string arcId)
    {
        if (byId.TryGetValue(arcId ?? string.Empty, out StoryArcProgress p))
            return p.definition;
        return null;
    }

    public StoryArcDefinition ConsumePendingLegendaryEnding()
    {
        if (string.IsNullOrEmpty(pendingLegendaryEndingId))
            return null;

        string id = pendingLegendaryEndingId;
        pendingLegendaryEndingId = null;
        return GetDefinition(id);
    }

    public bool HasPendingLegendaryEnding() => !string.IsNullOrEmpty(pendingLegendaryEndingId);

    private void TryCompleteArc(StoryArcProgress state)
    {
        if (state == null || state.definition == null)
            return;

        bool newlyUnlocked = !state.endingUnlocked;
        if (newlyUnlocked)
        {
            state.endingUnlocked = true;
            PlayerPrefs.SetInt(EndingKey(state.arcId), 1);
            PlayerPrefs.Save();
            OnEndingUnlocked?.Invoke(state.definition);
            Debug.Log($"StoryArcManager: Unlocked legendary ending '{state.definition.endingTitle}'.");
        }

        // Queue finale even on repeat completions so the player sees the ending again.
        pendingLegendaryEndingId = state.arcId;
        OnLegendaryEndingReady?.Invoke(state.definition);
    }

    private void LoadDefinitions()
    {
        arcs.Clear();
        byId.Clear();

        TextAsset asset = Resources.Load<TextAsset>(arcsResourcePath);
        StoryArcDefinition[] defs = null;
        if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
        {
            StoryArcDefinitionCollection collection =
                JsonUtility.FromJson<StoryArcDefinitionCollection>(asset.text);
            defs = collection?.arcs;
        }

        if (defs == null || defs.Length == 0)
        {
            Debug.LogWarning("StoryArcManager: No arcs loaded — using built-in defaults.");
            defs = CreateBuiltinDefaults();
        }

        for (int i = 0; i < defs.Length; i++)
        {
            StoryArcDefinition def = defs[i];
            if (def == null || string.IsNullOrWhiteSpace(def.id))
                continue;

            string id = def.id.Trim();
            if (byId.ContainsKey(id))
                continue;

            if (def.requiredSteps <= 0)
                def.requiredSteps = 5;

            var state = new StoryArcProgress
            {
                arcId = id,
                progress = 0,
                endingUnlocked = PlayerPrefs.GetInt(EndingKey(id), 0) == 1,
                definition = def
            };
            arcs.Add(state);
            byId[id] = state;
        }
    }

    private void RefreshUnlockedFromPrefs()
    {
        for (int i = 0; i < arcs.Count; i++)
            arcs[i].endingUnlocked = PlayerPrefs.GetInt(EndingKey(arcs[i].arcId), 0) == 1;
    }

    private static string EndingKey(string arcId) => EndingPrefsPrefix + arcId.Trim();

    private static StoryArcDefinition[] CreateBuiltinDefaults()
    {
        return new[]
        {
            new StoryArcDefinition
            {
                id = "empire_unifier",
                flagKey = "Empire_Unifier_Progress",
                displayName = "Unified the Empire",
                badgeGlyph = "♛",
                requiredSteps = 5,
                endingTitle = "Unified the Empire",
                endingBody = "The realm endures as one empire."
            },
            new StoryArcDefinition
            {
                id = "cult_overthrow",
                flagKey = "Cult_Overthrow_Progress",
                displayName = "Overthrown by Cult",
                badgeGlyph = "⛧",
                requiredSteps = 5,
                endingTitle = "Overthrown by Cult",
                endingBody = "The coven claims the throne."
            },
            new StoryArcDefinition
            {
                id = "dragon_slayer",
                flagKey = "DragonSlayer_Progress",
                displayName = "Dragon's Bane",
                badgeGlyph = "🐉",
                requiredSteps = 5,
                endingTitle = "Dragon's Bane",
                endingBody = "The dragon is ash."
            },
            new StoryArcDefinition
            {
                id = "eternal_crusade",
                flagKey = "Crusade_Initiated",
                displayName = "Eternal Crusade",
                badgeGlyph = "✝",
                requiredSteps = 5,
                endingTitle = "Eternal Crusade",
                endingBody = "The crusade never ends."
            },
            new StoryArcDefinition
            {
                id = "merchant_crown",
                flagKey = "Merchant_Crown_Progress",
                displayName = "Crowns of Coin",
                badgeGlyph = "⚜",
                requiredSteps = 5,
                endingTitle = "Crowns of Coin",
                endingBody = "Gold writes the law."
            }
        };
    }
}
