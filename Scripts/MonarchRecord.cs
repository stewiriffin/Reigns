using System;
using UnityEngine;

/// <summary>
/// One finished reign stored in the Dynasty History log.
/// </summary>
[Serializable]
public class MonarchRecord
{
    public string monarchName;
    public int yearsRuled;
    /// <summary>Serialized as the <see cref="DeathCause"/> enum name.</summary>
    public string deathCause;
    /// <summary>UTC timestamp (ISO-8601 / "o" format).</summary>
    public string deathDateUtc;

    public DeathCause GetDeathCause()
    {
        if (string.IsNullOrWhiteSpace(deathCause))
            return DeathCause.None;

        return Enum.TryParse(deathCause, ignoreCase: true, out DeathCause cause)
            ? cause
            : DeathCause.None;
    }

    public DateTime GetDeathDateUtc()
    {
        if (string.IsNullOrWhiteSpace(deathDateUtc))
            return DateTime.MinValue;

        return DateTime.TryParse(
            deathDateUtc,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTime parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    public static MonarchRecord Create(string name, int yearsRuled, DeathCause cause, DateTime utcNow)
    {
        return new MonarchRecord
        {
            monarchName = name ?? "Unknown Monarch",
            yearsRuled = Mathf.Max(0, yearsRuled),
            deathCause = cause.ToString(),
            deathDateUtc = utcNow.ToUniversalTime().ToString("o")
        };
    }
}

/// <summary>
/// JSON wrapper for <see cref="JsonUtility"/> (needs a root object for arrays).
/// </summary>
[Serializable]
public class MonarchRecordList
{
    public MonarchRecord[] records;
}
