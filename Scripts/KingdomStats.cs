using System;
using UnityEngine;

/// <summary>
/// Manages the four core kingdom stats for a Reigns-style game.
/// A game-over condition is raised when any stat reaches 0 or 100,
/// with a distinct <see cref="DeathCause"/> for each extreme.
/// </summary>
public class KingdomStats : MonoBehaviour
{
    public const int MinStat = 0;
    public const int MaxStat = 100;
    public const int DefaultStat = 50;

    [SerializeField] private int religion = DefaultStat;
    [SerializeField] private int people = DefaultStat;
    [SerializeField] private int army = DefaultStat;
    [SerializeField] private int wealth = DefaultStat;

    /// <summary>
    /// Fired when any stat hits 0 or 100. Payload identifies which of the 8 death states occurred.
    /// </summary>
    public event Action<DeathCause> OnGameOver;

    /// <summary>
    /// Optional hook (InventoryManager). Return true if the death was prevented (stat already restored).
    /// </summary>
    public Func<DeathCause, bool> TryPreventDeath;

    public int Religion => religion;
    public int People => people;
    public int Army => army;
    public int Wealth => wealth;

    public bool IsGameOver { get; private set; }

    /// <summary>The specific death state that ended the current reign, or <see cref="DeathCause.None"/>.</summary>
    public DeathCause LastDeathCause { get; private set; }

    private void Awake()
    {
        ResetStats();
    }

    /// <summary>
    /// Resets all stats to the default starting value and clears the game-over flag.
    /// </summary>
    public void ResetStats()
    {
        religion = DefaultStat;
        people = DefaultStat;
        army = DefaultStat;
        wealth = DefaultStat;
        IsGameOver = false;
        LastDeathCause = DeathCause.None;
    }

    /// <summary>
    /// Restores exact stat values from a save without firing game-over checks.
    /// </summary>
    public void LoadState(int religionValue, int peopleValue, int armyValue, int wealthValue)
    {
        religion = ClampStat(religionValue);
        people = ClampStat(peopleValue);
        army = ClampStat(armyValue);
        wealth = ClampStat(wealthValue);
        IsGameOver = false;
        LastDeathCause = DeathCause.None;
    }

    /// <summary>
    /// Applies deltas to each kingdom stat, clamps them to [0, 100],
    /// and raises <see cref="OnGameOver"/> if any stat hits either extreme.
    /// </summary>
    public void ModifyStats(int religionDelta, int peopleDelta, int armyDelta, int wealthDelta)
    {
        if (IsGameOver)
            return;

        religion = ClampStat(religion + religionDelta);
        people = ClampStat(people + peopleDelta);
        army = ClampStat(army + armyDelta);
        wealth = ClampStat(wealth + wealthDelta);

        CheckGameOver();
    }

    /// <summary>
    /// Applies a single-stat delta (used by status effect ticks).
    /// </summary>
    public void ApplyStatDelta(StatType stat, int delta)
    {
        if (IsGameOver || delta == 0)
            return;

        switch (stat)
        {
            case StatType.Religion:
                ModifyStats(delta, 0, 0, 0);
                break;
            case StatType.People:
                ModifyStats(0, delta, 0, 0);
                break;
            case StatType.Army:
                ModifyStats(0, 0, delta, 0);
                break;
            case StatType.Wealth:
                ModifyStats(0, 0, 0, delta);
                break;
        }
    }

    /// <summary>
    /// Returns the first extreme among the eight death states, or <see cref="DeathCause.None"/>.
    /// Priority order: Religion, People, Army, Wealth — empty (0) before full (100).
    /// </summary>
    public DeathCause EvaluateDeathCause()
    {
        if (religion <= MinStat) return DeathCause.ReligionEmpty;
        if (religion >= MaxStat) return DeathCause.ReligionFull;
        if (people <= MinStat) return DeathCause.PeopleEmpty;
        if (people >= MaxStat) return DeathCause.PeopleFull;
        if (army <= MinStat) return DeathCause.ArmyEmpty;
        if (army >= MaxStat) return DeathCause.ArmyFull;
        if (wealth <= MinStat) return DeathCause.WealthEmpty;
        if (wealth >= MaxStat) return DeathCause.WealthFull;
        return DeathCause.None;
    }

    private static int ClampStat(int value)
    {
        return Mathf.Clamp(value, MinStat, MaxStat);
    }

    private void CheckGameOver()
    {
        // Multiple extremes can exist after one ModifyStats; allow item saves in sequence.
        for (int safety = 0; safety < 8; safety++)
        {
            DeathCause cause = EvaluateDeathCause();
            if (cause == DeathCause.None)
                return;

            if (TryPreventDeath != null && TryPreventDeath(cause))
                continue;

            TriggerGameOver(cause);
            return;
        }
    }

    /// <summary>
    /// Restores the stat associated with a death cause to the default (50). Does not clear game over.
    /// </summary>
    public void RestoreStatForDeathCause(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.ReligionEmpty:
            case DeathCause.ReligionFull:
                religion = DefaultStat;
                break;
            case DeathCause.PeopleEmpty:
            case DeathCause.PeopleFull:
                people = DefaultStat;
                break;
            case DeathCause.ArmyEmpty:
            case DeathCause.ArmyFull:
                army = DefaultStat;
                break;
            case DeathCause.WealthEmpty:
            case DeathCause.WealthFull:
                wealth = DefaultStat;
                break;
        }
    }

    private void TriggerGameOver(DeathCause cause)
    {
        if (IsGameOver)
            return;

        IsGameOver = true;
        LastDeathCause = cause;
        HapticFeedback.PlayHeavy();
        OnGameOver?.Invoke(cause);
        Debug.Log($"Game Over — {cause}");
    }

    /// <summary>
    /// Second Chance: restores the failing stat to 50 and clears game over so the run can continue.
    /// </summary>
    public bool GrantSecondChance()
    {
        if (!IsGameOver || LastDeathCause == DeathCause.None)
            return false;

        switch (LastDeathCause)
        {
            case DeathCause.ReligionEmpty:
            case DeathCause.ReligionFull:
                religion = DefaultStat;
                break;
            case DeathCause.PeopleEmpty:
            case DeathCause.PeopleFull:
                people = DefaultStat;
                break;
            case DeathCause.ArmyEmpty:
            case DeathCause.ArmyFull:
                army = DefaultStat;
                break;
            case DeathCause.WealthEmpty:
            case DeathCause.WealthFull:
                wealth = DefaultStat;
                break;
            default:
                return false;
        }

        IsGameOver = false;
        LastDeathCause = DeathCause.None;
        return true;
    }
}
