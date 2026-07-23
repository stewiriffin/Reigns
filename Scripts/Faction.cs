using System;
using UnityEngine;

/// <summary>
/// Per-choice loyalty change for a hidden sub-faction.
/// JSON: { "factionId": "merchant_guild", "delta": 5 }
/// </summary>
[Serializable]
public class FactionDelta
{
    public string factionId;
    public int delta;
}

/// <summary>
/// Static definition for a sub-faction (loaded from Resources JSON).
/// </summary>
[Serializable]
public class FactionDefinition
{
    public string id;
    public string displayName;
    public string description;

    /// <summary>Optional linked kingdom stat for UI flavor (Wealth, Religion, Army, People).</summary>
    public string linkedStat;

    /// <summary>Card injected when loyalty rises above the high threshold.</summary>
    public string highLoyaltyCardId;

    /// <summary>Card injected when loyalty falls below the low threshold.</summary>
    public string lowLoyaltyCardId;
}

[Serializable]
public class FactionDefinitionCollection
{
    public FactionDefinition[] factions;
}

/// <summary>
/// Runtime loyalty for one faction during a reign.
/// </summary>
[Serializable]
public class FactionLoyaltyState
{
    public string factionId;
    public int loyalty = FactionRelationshipManager.DefaultLoyalty;
    public bool highThresholdLatched;
    public bool lowThresholdLatched;

    [NonSerialized] public FactionDefinition definition;

    public string DisplayName =>
        definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
            ? definition.displayName
            : factionId;

    public string Description =>
        definition != null ? definition.description ?? string.Empty : string.Empty;

    public string LinkedStat =>
        definition != null ? definition.linkedStat ?? string.Empty : string.Empty;

    public FactionStanding Standing
    {
        get
        {
            if (loyalty > FactionRelationshipManager.HighThreshold)
                return FactionStanding.Devoted;
            if (loyalty < FactionRelationshipManager.LowThreshold)
                return FactionStanding.Hostile;
            if (loyalty >= 60)
                return FactionStanding.Favorable;
            if (loyalty <= 40)
                return FactionStanding.Wary;
            return FactionStanding.Neutral;
        }
    }
}

public enum FactionStanding
{
    Hostile,
    Wary,
    Neutral,
    Favorable,
    Devoted
}

/// <summary>Serializable slice for run saves.</summary>
[Serializable]
public class FactionLoyaltySave
{
    public string factionId;
    public int loyalty;
    public bool highThresholdLatched;
    public bool lowThresholdLatched;
}
