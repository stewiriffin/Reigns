using System;
using UnityEngine;

/// <summary>
/// Integer deltas applied to the four kingdom stats when a choice is selected.
/// </summary>
[Serializable]
public class StatModifiers
{
    public int religion;
    public int people;
    public int army;
    public int wealth;

    public void Apply(KingdomStats stats)
    {
        if (stats == null)
            return;

        stats.ModifyStats(religion, people, army, wealth);
    }

    public bool ModifiesReligion => religion != 0;
    public bool ModifiesPeople => people != 0;
    public bool ModifiesArmy => army != 0;
    public bool ModifiesWealth => wealth != 0;
}

/// <summary>
/// A single Reigns-style event card with left and right choices.
/// Unlockable cards set <see cref="prerequisiteFlag"/>; choices may grant meta flags via unlock fields.
/// </summary>
[Serializable]
public class Card
{
    public string id;
    public string scenarioText;

    /// <summary>
    /// If set, this card is only added to the active deck when the flag is true in PlayerPrefs.
    /// Leave empty for base-deck cards that are always available.
    /// </summary>
    public string prerequisiteFlag;

    public string leftChoiceText;
    public StatModifiers leftChoiceModifiers;
    public StatusEffect[] leftChoiceStatusEffects;
    /// <summary>Meta flag granted when the player picks the left choice (persists across runs).</summary>
    public string leftChoiceUnlockFlag;

    public string rightChoiceText;
    public StatModifiers rightChoiceModifiers;
    public StatusEffect[] rightChoiceStatusEffects;
    /// <summary>Meta flag granted when the player picks the right choice (persists across runs).</summary>
    public string rightChoiceUnlockFlag;
}

/// <summary>
/// A named pool of unlockable cards gated by prerequisite flags.
/// </summary>
[Serializable]
public class UnlockableCardPool
{
    public string id;
    public Card[] cards;
}

/// <summary>
/// Full card database: always-available base deck plus unlockable pools.
/// </summary>
[Serializable]
public class CardDatabase
{
    public Card[] baseDeck;
    public UnlockableCardPool[] unlockablePools;
}

/// <summary>
/// Legacy wrapper kept for older { "cards": [ ... ] } JSON files.
/// </summary>
[Serializable]
public class CardCollection
{
    public Card[] cards;
}
