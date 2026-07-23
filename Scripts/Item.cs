using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A collectible item that can grant passive protections (e.g. prevent an Army death once).
/// </summary>
[Serializable]
public class Item
{
    public string id;
    public string displayName;

    /// <summary>Resources path to the item icon sprite (no extension).</summary>
    public string iconResourcePath;

    /// <summary>Resolved icon for inventory UI.</summary>
    [NonSerialized] public Sprite icon;

    public bool PreventsDeathByReligion;
    public bool PreventsDeathByPeople;
    public bool PreventsDeathByArmy;
    public bool PreventsDeathByWealth;

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
            iconResourcePath = iconResourcePath,
            icon = icon,
            PreventsDeathByReligion = PreventsDeathByReligion,
            PreventsDeathByPeople = PreventsDeathByPeople,
            PreventsDeathByArmy = PreventsDeathByArmy,
            PreventsDeathByWealth = PreventsDeathByWealth
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
