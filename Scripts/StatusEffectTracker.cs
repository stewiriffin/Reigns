using System.Collections.Generic;

/// <summary>
/// Tracks active status effects, ticks them each turn, and reports which stats are affected.
/// </summary>
public class StatusEffectTracker
{
    private readonly List<StatusEffect> activeEffects = new List<StatusEffect>();

    public IReadOnlyList<StatusEffect> ActiveEffects => activeEffects;

    public int Count => activeEffects.Count;

    public void Clear()
    {
        activeEffects.Clear();
    }

    /// <summary>
    /// Snapshot of active effects for save serialization.
    /// </summary>
    public StatusEffect[] ToSaveArray()
    {
        var snapshot = new StatusEffect[activeEffects.Count];
        for (int i = 0; i < activeEffects.Count; i++)
            snapshot[i] = activeEffects[i].Clone();
        return snapshot;
    }

    /// <summary>
    /// Replaces all active effects (used when loading a save).
    /// </summary>
    public void LoadFromSave(StatusEffect[] effects)
    {
        Clear();
        if (effects == null || effects.Length == 0)
            return;

        // Bypass IsValid briefly so mid-duration effects with remaining turns restore correctly.
        foreach (StatusEffect effect in effects)
        {
            if (effect == null || effect.duration <= 0 || !effect.TryGetTargetStat(out _))
                continue;

            activeEffects.Add(effect.Clone());
        }
    }

    public void Add(StatusEffect effect)
    {
        if (effect == null || !effect.IsValid)
            return;

        activeEffects.Add(effect.Clone());
    }

    public void AddRange(IEnumerable<StatusEffect> effects)
    {
        if (effects == null)
            return;

        foreach (StatusEffect effect in effects)
            Add(effect);
    }

    /// <summary>
    /// Applies each active effect once, then decrements duration and removes expired ones.
    /// Returns true if a game-over condition was triggered during the tick.
    /// </summary>
    public bool Tick(KingdomStats stats)
    {
        if (stats == null || stats.IsGameOver)
            return stats != null && stats.IsGameOver;

        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            StatusEffect effect = activeEffects[i];
            if (!effect.TryGetTargetStat(out StatType stat))
            {
                activeEffects.RemoveAt(i);
                continue;
            }

            stats.ApplyStatDelta(stat, effect.valuePerTurn);
            effect.duration--;

            if (effect.duration <= 0)
                activeEffects.RemoveAt(i);

            if (stats.IsGameOver)
                return true;
        }

        return false;
    }

    public bool HasEffectOn(StatType stat)
    {
        for (int i = 0; i < activeEffects.Count; i++)
        {
            if (activeEffects[i].TryGetTargetStat(out StatType target) && target == stat)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Net value-per-turn for a stat across all active effects (positive = buff).
    /// </summary>
    public int GetNetValuePerTurn(StatType stat)
    {
        int total = 0;
        for (int i = 0; i < activeEffects.Count; i++)
        {
            if (activeEffects[i].TryGetTargetStat(out StatType target) && target == stat)
                total += activeEffects[i].valuePerTurn;
        }

        return total;
    }

    public void GetBuffDebuffFlags(StatType stat, out bool hasBuff, out bool hasDebuff)
    {
        hasBuff = false;
        hasDebuff = false;

        for (int i = 0; i < activeEffects.Count; i++)
        {
            if (!activeEffects[i].TryGetTargetStat(out StatType target) || target != stat)
                continue;

            if (activeEffects[i].valuePerTurn > 0)
                hasBuff = true;
            else if (activeEffects[i].valuePerTurn < 0)
                hasDebuff = true;
        }
    }
}
