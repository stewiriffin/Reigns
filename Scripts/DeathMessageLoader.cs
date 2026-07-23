using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DeathMessageEntry
{
    public string cause;
    public string message;
}

[Serializable]
public class DeathMessageCollection
{
    public DeathMessageEntry[] entries;
}

/// <summary>
/// Loads per-death-cause messages from JSON into a lookup dictionary.
/// </summary>
public static class DeathMessageLoader
{
    private const string DefaultResourcePath = "Deaths/death_messages";

    public static Dictionary<DeathCause, string> Load(string resourcePath = DefaultResourcePath)
    {
        var map = CreateFallbackMessages();

        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"DeathMessageLoader: Missing Resources/{resourcePath}.json — using fallbacks.");
            return map;
        }

        DeathMessageCollection collection = JsonUtility.FromJson<DeathMessageCollection>(asset.text);
        if (collection?.entries == null)
            return map;

        foreach (DeathMessageEntry entry in collection.entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.cause))
                continue;

            if (Enum.TryParse(entry.cause, ignoreCase: true, out DeathCause cause) && cause != DeathCause.None)
                map[cause] = entry.message ?? string.Empty;
        }

        return map;
    }

    public static string GetMessage(IReadOnlyDictionary<DeathCause, string> map, DeathCause cause)
    {
        if (map != null && map.TryGetValue(cause, out string message) && !string.IsNullOrEmpty(message))
            return message;

        return "Your reign has ended.";
    }

    private static Dictionary<DeathCause, string> CreateFallbackMessages()
    {
        return new Dictionary<DeathCause, string>
        {
            { DeathCause.ReligionEmpty, "The faithful abandon the crown. Without the Church, your legitimacy collapses." },
            { DeathCause.ReligionFull, "The High Priest declares you a living saint — then rules in your name." },
            { DeathCause.PeopleEmpty, "The people rise in open revolt. The palace gates do not hold." },
            { DeathCause.PeopleFull, "Adored beyond reason, the mob crowns a favorite and forgets you." },
            { DeathCause.ArmyEmpty, "With no soldiers left, invaders seize the throne unopposed." },
            { DeathCause.ArmyFull, "The army staged a military coup." },
            { DeathCause.WealthEmpty, "The treasury is empty. Merchants flee, and famine follows." },
            { DeathCause.WealthFull, "Gold corrupts the court. Your richest vassal buys the crown." }
        };
    }
}
