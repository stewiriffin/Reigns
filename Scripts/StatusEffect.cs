using System;
using UnityEngine;

/// <summary>
/// A lasting effect that modifies one kingdom stat by <see cref="valuePerTurn"/>
/// each turn for <see cref="duration"/> turns.
/// </summary>
[Serializable]
public class StatusEffect
{
    [Tooltip("Religion, People, Army, or Wealth")]
    public string targetStat = nameof(StatType.Religion);

    public int valuePerTurn = 0;
    public int duration = 1;

    public bool TryGetTargetStat(out StatType stat)
    {
        return Enum.TryParse(targetStat, ignoreCase: true, out stat);
    }

    public StatusEffect Clone()
    {
        return new StatusEffect
        {
            targetStat = targetStat,
            valuePerTurn = valuePerTurn,
            duration = duration
        };
    }

    public bool IsValid =>
        duration > 0 &&
        valuePerTurn != 0 &&
        TryGetTargetStat(out _);
}
