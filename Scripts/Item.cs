using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collectible relic/item: passive death shields and/or active one-shot effects.
/// </summary>
[Serializable]
public class Item
{
    public string id;
    public string displayName;

    /// <summary>Short UI blurb shown under the action-bar icon name.</summary>
    public string description;

    /// <summary>Resources path to the item icon sprite (no extension).</summary>
    public string iconResourcePath;

    /// <summary>Resolved icon for inventory UI.</summary>
    [NonSerialized] public Sprite icon;

    /// <summary>When true, the player can tap the action-bar icon to trigger <see cref="useEffect"/>.</summary>
    public bool isUsable;

    /// <summary>Remove from inventory after a successful active use.</summary>
    public bool consumeOnUse = true;

    /// <summary>Stat deltas applied when the item is actively used.</summary>
    public StatModifiers useEffect;

    public bool PreventsDeathByReligion;
    public bool PreventsDeathByPeople;
    public bool PreventsDeathByArmy;
    public bool PreventsDeathByWealth;

    public bool HasActiveEffect =>
        isUsable && useEffect != null &&
        (useEffect.religion != 0 || useEffect.people != 0 ||
         useEffect.army != 0 || useEffect.wealth != 0);

    public void ResolveAssets()
    {
        icon = null;
        if (!string.IsNullOrWhiteSpace(iconResourcePath))
            icon = Resources.Load<Sprite>(iconResourcePath);
    }

    public bool Prevents(DeathCause cause)
    {
        return cause switch
        {
            DeathCause.ReligionEmpty or DeathCause.ReligionFull => PreventsDeathByReligion,
            DeathCause.PeopleEmpty or DeathCause.PeopleFull => PreventsDeathByPeople,
            DeathCause.ArmyEmpty or DeathCause.ArmyFull => PreventsDeathByArmy,
            DeathCause.WealthEmpty or DeathCause.WealthFull => PreventsDeathByWealth,
            _ => false
        };
    }

    public Item Clone()
    {
        return new Item
        {
            id = id,
            displayName = displayName,
            description = description,
            iconResourcePath = iconResourcePath,
            icon = icon,
            isUsable = isUsable,
            consumeOnUse = consumeOnUse,
            useEffect = CloneModifiers(useEffect),
            PreventsDeathByReligion = PreventsDeathByReligion,
            PreventsDeathByPeople = PreventsDeathByPeople,
            PreventsDeathByArmy = PreventsDeathByArmy,
            PreventsDeathByWealth = PreventsDeathByWealth
        };
    }

    private static StatModifiers CloneModifiers(StatModifiers source)
    {
        if (source == null)
            return null;

        return new StatModifiers
        {
            religion = source.religion,
            people = source.people,
            army = source.army,
            wealth = source.wealth
        };
    }
}

[Serializable]
public class ItemCollection
{
    public Item[] items;
}

/// <summary>
/// Loads item definitions from JSON under Resources.
/// </summary>
public static class ItemLoader
{
    private const string DefaultResourcePath = "Items/items";

    public static List<Item> LoadItems(string resourcePath = DefaultResourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"ItemLoader: Missing Resources/{resourcePath}.json");
            return new List<Item>();
        }

        ItemCollection collection = JsonUtility.FromJson<ItemCollection>(asset.text);
        var list = new List<Item>();
        if (collection?.items == null)
            return list;

        foreach (Item item in collection.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id))
                continue;

            item.ResolveAssets();
            list.Add(item);
        }

        return list;
    }

    public static Item FindById(IList<Item> items, string id)
    {
        if (items == null || string.IsNullOrWhiteSpace(id))
            return null;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].id == id)
                return items[i];
        }

        return null;
    }
}
