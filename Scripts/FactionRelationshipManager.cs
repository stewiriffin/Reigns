using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hidden sub-faction loyalties (0–100). Choice deltas update scores; extreme
/// highs/lows inject specialized event cards into the draw flow.
/// </summary>
public class FactionRelationshipManager : MonoBehaviour
{
    public const int MinLoyalty = 0;
    public const int MaxLoyalty = 100;
    public const int DefaultLoyalty = 50;
    public const int HighThreshold = 80;
    public const int LowThreshold = 20;

    private const string DefaultResourcePath = "Factions/factions";

    public static FactionRelationshipManager Instance { get; private set; }

    [SerializeField] private string factionsResourcePath = DefaultResourcePath;

    private readonly List<FactionLoyaltyState> states = new List<FactionLoyaltyState>(8);
    private readonly Dictionary<string, FactionLoyaltyState> byId =
        new Dictionary<string, FactionLoyaltyState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fired after any loyalty change or full reset/load.</summary>
    public event Action OnRelationshipsChanged;

    /// <summary>Fired when a threshold injects an event card id.</summary>
    public event Action<string, string> OnFactionEventQueued;

    public IReadOnlyList<FactionLoyaltyState> States => states;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadDefinitionsAndReset();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ResetRelationships()
    {
        for (int i = 0; i < states.Count; i++)
        {
            states[i].loyalty = DefaultLoyalty;
            states[i].highThresholdLatched = false;
            states[i].lowThresholdLatched = false;
        }

        OnRelationshipsChanged?.Invoke();
    }

    /// <summary>
    /// Applies left/right choice faction deltas. Returns true if anything changed.
    /// </summary>
    public bool ApplyDeltas(FactionDelta[] deltas)
    {
        if (deltas == null || deltas.Length == 0)
            return false;

        bool changed = false;
        for (int i = 0; i < deltas.Length; i++)
        {
            FactionDelta delta = deltas[i];
            if (delta == null || string.IsNullOrWhiteSpace(delta.factionId) || delta.delta == 0)
                continue;

            if (ApplyDelta(delta.factionId, delta.delta))
                changed = true;
        }

        if (changed)
            OnRelationshipsChanged?.Invoke();

        return changed;
    }

    public bool ApplyDelta(string factionId, int delta)
    {
        if (string.IsNullOrWhiteSpace(factionId) || delta == 0)
            return false;

        if (!byId.TryGetValue(factionId.Trim(), out FactionLoyaltyState state))
        {
            Debug.LogWarning($"FactionRelationshipManager: Unknown faction '{factionId}'.");
            return false;
        }

        int before = state.loyalty;
        state.loyalty = Mathf.Clamp(state.loyalty + delta, MinLoyalty, MaxLoyalty);
        if (state.loyalty == before)
            return false;

        EvaluateThresholds(state, before);
        return true;
    }

    public int GetLoyalty(string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return DefaultLoyalty;

        return byId.TryGetValue(factionId.Trim(), out FactionLoyaltyState state)
            ? state.loyalty
            : DefaultLoyalty;
    }

    public FactionLoyaltyState GetState(string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return null;

        byId.TryGetValue(factionId.Trim(), out FactionLoyaltyState state);
        return state;
    }

    public FactionLoyaltySave[] CaptureSave()
    {
        var saves = new FactionLoyaltySave[states.Count];
        for (int i = 0; i < states.Count; i++)
        {
            FactionLoyaltyState s = states[i];
            saves[i] = new FactionLoyaltySave
            {
                factionId = s.factionId,
                loyalty = s.loyalty,
                highThresholdLatched = s.highThresholdLatched,
                lowThresholdLatched = s.lowThresholdLatched
            };
        }

        return saves;
    }

    public void LoadSave(FactionLoyaltySave[] saves)
    {
        ResetRelationships();
        if (saves == null)
        {
            OnRelationshipsChanged?.Invoke();
            return;
        }

        for (int i = 0; i < saves.Length; i++)
        {
            FactionLoyaltySave save = saves[i];
            if (save == null || string.IsNullOrWhiteSpace(save.factionId))
                continue;

            if (!byId.TryGetValue(save.factionId.Trim(), out FactionLoyaltyState state))
                continue;

            state.loyalty = Mathf.Clamp(save.loyalty, MinLoyalty, MaxLoyalty);
            state.highThresholdLatched = save.highThresholdLatched;
            state.lowThresholdLatched = save.lowThresholdLatched;
        }

        OnRelationshipsChanged?.Invoke();
    }

    private void EvaluateThresholds(FactionLoyaltyState state, int loyaltyBefore)
    {
        // Clear latches when returning to the safe band so extremes can fire again later.
        if (state.loyalty >= LowThreshold && state.loyalty <= HighThreshold)
        {
            state.highThresholdLatched = false;
            state.lowThresholdLatched = false;
            return;
        }

        FactionDefinition def = state.definition;
        if (def == null)
            return;

        if (state.loyalty > HighThreshold && !state.highThresholdLatched)
        {
            state.highThresholdLatched = true;
            QueueEventCard(state, def.highLoyaltyCardId, "high");
        }
        else if (state.loyalty < LowThreshold && !state.lowThresholdLatched)
        {
            state.lowThresholdLatched = true;
            QueueEventCard(state, def.lowLoyaltyCardId, "low");
        }

        // Crossing from one extreme to the other in one delta should still clear the opposite latch.
        if (state.loyalty > HighThreshold && loyaltyBefore < LowThreshold)
            state.lowThresholdLatched = false;
        if (state.loyalty < LowThreshold && loyaltyBefore > HighThreshold)
            state.highThresholdLatched = false;
    }

    private void QueueEventCard(FactionLoyaltyState state, string cardId, string band)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return;

        string id = cardId.Trim();
        Debug.Log(
            $"FactionRelationshipManager: {state.DisplayName} loyalty {state.loyalty} ({band}) → queue '{id}'.");

        OnFactionEventQueued?.Invoke(state.factionId, id);

        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm != null)
            gm.RequestFactionEventCard(id);
    }

    private void LoadDefinitionsAndReset()
    {
        states.Clear();
        byId.Clear();

        TextAsset asset = Resources.Load<TextAsset>(factionsResourcePath);
        FactionDefinition[] defs = null;

        if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
        {
            FactionDefinitionCollection collection =
                JsonUtility.FromJson<FactionDefinitionCollection>(asset.text);
            defs = collection?.factions;
        }

        if (defs == null || defs.Length == 0)
        {
            Debug.LogWarning(
                $"FactionRelationshipManager: No factions at Resources/{factionsResourcePath}.json — using built-in defaults.");
            defs = CreateBuiltinDefaults();
        }

        for (int i = 0; i < defs.Length; i++)
        {
            FactionDefinition def = defs[i];
            if (def == null || string.IsNullOrWhiteSpace(def.id))
                continue;

            string id = def.id.Trim();
            if (byId.ContainsKey(id))
                continue;

            var state = new FactionLoyaltyState
            {
                factionId = id,
                loyalty = DefaultLoyalty,
                definition = def
            };
            states.Add(state);
            byId[id] = state;
        }

        OnRelationshipsChanged?.Invoke();
    }

    private static FactionDefinition[] CreateBuiltinDefaults()
    {
        return new[]
        {
            new FactionDefinition
            {
                id = "merchant_guild",
                displayName = "The Merchant Guild",
                description = "Coin-counters and caravan masters who price every crown.",
                linkedStat = "Wealth",
                highLoyaltyCardId = "faction_guild_investment",
                lowLoyaltyCardId = "faction_merchant_rebellion"
            },
            new FactionDefinition
            {
                id = "witches",
                displayName = "The Witches",
                description = "Coven sisters in the fen — favors bought with silence and ash.",
                linkedStat = "Religion",
                highLoyaltyCardId = "faction_witches_boon",
                lowLoyaltyCardId = "faction_witches_curse"
            },
            new FactionDefinition
            {
                id = "foreign_empire",
                displayName = "Foreign Empire",
                description = "Envoys across the mountains who smile while measuring your borders.",
                linkedStat = "Army",
                highLoyaltyCardId = "faction_empire_alliance",
                lowLoyaltyCardId = "faction_empire_ultimatum"
            }
        };
    }
}
