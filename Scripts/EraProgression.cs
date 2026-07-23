using UnityEngine;

/// <summary>
/// Maps years ruled → era index and computes the run's difficulty multiplier.
/// Era 1: years 0–10, Era 2: 11–25, Era 3: 26+.
/// DifficultyScale grows +10% every 10 years by default.
/// </summary>
public static class EraProgression
{
    public const int Era1 = 1;
    public const int Era2 = 2;
    public const int Era3 = 3;

    public static int GetEra(int yearsRuled, int era1MaxYear = 10, int era2MaxYear = 25)
    {
        yearsRuled = Mathf.Max(0, yearsRuled);
        if (yearsRuled <= era1MaxYear)
            return Era1;
        if (yearsRuled <= era2MaxYear)
            return Era2;
        return Era3;
    }

    /// <summary>
    /// +<paramref name="increasePerDecade"/> (e.g. 0.1 = +10%) for every full 10 years survived.
    /// Year 0–9 → 1.0, 10–19 → 1.1, 20–29 → 1.2, …
    /// </summary>
    public static float GetDifficultyScale(int yearsRuled, float increasePerDecade = 0.1f, int yearsPerStep = 10)
    {
        yearsRuled = Mathf.Max(0, yearsRuled);
        yearsPerStep = Mathf.Max(1, yearsPerStep);
        int steps = yearsRuled / yearsPerStep;
        return 1f + steps * Mathf.Max(0f, increasePerDecade);
    }

    public static string GetEraDisplayName(int era)
    {
        return era switch
        {
            Era1 => "Era I — Foundation",
            Era2 => "Era II — Unrest",
            Era3 => "Era III — Reckoning",
            _ => $"Era {era}"
        };
    }
}
