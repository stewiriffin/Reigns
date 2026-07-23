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

    /// <summary>
    /// Returns a new modifier set scaled by <paramref name="scale"/> (difficulty).
    /// Zero stays zero; non-zero values keep their sign after rounding.
    /// </summary>
    public StatModifiers CreateScaled(float scale)
    {
        if (Mathf.Approximately(scale, 1f))
        {
            return new StatModifiers
            {
                religion = religion,
                people = people,
                army = army,
                wealth = wealth
            };
        }

        return new StatModifiers
        {
            religion = ScaleStat(religion, scale),
            people = ScaleStat(people, scale),
            army = ScaleStat(army, scale),
            wealth = ScaleStat(wealth, scale)
        };
    }

    private static int ScaleStat(int value, float scale)
    {
        if (value == 0)
            return 0;

        int scaled = Mathf.RoundToInt(value * scale);
        if (scaled == 0)
            return value > 0 ? 1 : -1;
        return scaled;
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

    /// <summary>
    /// Localization key for the scenario body (resolved via <see cref="LocalizationManager"/>).
    /// Legacy hardcoded English may still live here as a fallback when no table entry exists.
    /// </summary>
    public string scenarioText;

    /// <summary>
    /// If set, this card is only added to the active deck when the flag is true in PlayerPrefs.
    /// Leave empty for base-deck cards that are always available.
    /// </summary>
    public string prerequisiteFlag;

    /// <summary>
    /// Era filter. 0 (or unset) = available in every era.
    /// 1 / 2 / 3 = only drawn during that era (see <see cref="EraProgression"/>).
    /// </summary>
    public int era;

    /// <summary>
    /// Resources path to the character portrait sprite (no extension), e.g. "Characters/Priest/portrait".
    /// Resolved into <see cref="portrait"/> after JSON load.
    /// </summary>
    public string portraitResourcePath;

    /// <summary>
    /// Resources path to the character speaking clip (no extension), e.g. "Characters/Priest/voice".
    /// Resolved into <see cref="speakingSound"/> after JSON load.
    /// </summary>
    public string voiceResourcePath;

    /// <summary>Character portrait shown on the card UI.</summary>
    [NonSerialized] public Sprite portrait;

    /// <summary>Short vocal clip played when this card is drawn.</summary>
    [NonSerialized] public AudioClip speakingSound;

    public string leftChoiceText;
    public StatModifiers leftChoiceModifiers;
    public StatusEffect[] leftChoiceStatusEffects;
    /// <summary>Meta flag granted when the player picks the left choice (persists across runs).</summary>
    public string leftChoiceUnlockFlag;
    /// <summary>Item ID granted when the player picks the left choice.</summary>
    public string leftChoiceGrantItem;

    public string rightChoiceText;
    public StatModifiers rightChoiceModifiers;
    public StatusEffect[] rightChoiceStatusEffects;
    /// <summary>Meta flag granted when the player picks the right choice (persists across runs).</summary>
    public string rightChoiceUnlockFlag;
    /// <summary>Item ID granted when the player picks the right choice.</summary>
    public string rightChoiceGrantItem;

    /// <summary>
    /// Optional. If set, a left swipe queues this card ID as the next draw (skips the random pool).
    /// </summary>
    public string NextCardID_Left;

    /// <summary>
    /// Optional. If set, a right swipe queues this card ID as the next draw (skips the random pool).
    /// </summary>
    public string NextCardID_Right;

    public string GetScenarioText() => LocalizationManager.Get(scenarioText, scenarioText);

    public string GetLeftChoiceText() => LocalizationManager.Get(leftChoiceText, leftChoiceText);

    public string GetRightChoiceText() => LocalizationManager.Get(rightChoiceText, rightChoiceText);

    /// <summary>
    /// Loads <see cref="portrait"/> and <see cref="speakingSound"/> from Resources using the path fields.
    /// </summary>
    public void ResolveAssets()
    {
        portrait = null;
        speakingSound = null;

        if (!string.IsNullOrWhiteSpace(portraitResourcePath))
            portrait = Resources.Load<Sprite>(portraitResourcePath);

        if (!string.IsNullOrWhiteSpace(voiceResourcePath))
            speakingSound = Resources.Load<AudioClip>(voiceResourcePath);
    }

    /// <summary>True when this card may appear in the given era (0 era = always).</summary>
    public bool IsAvailableInEra(int currentEra)
    {
        return era <= 0 || era == currentEra;
    }
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
