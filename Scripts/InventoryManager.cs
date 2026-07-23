using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks held items, renders their icons, and consumes death-prevention items when needed.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private string itemsResourcePath = "Items/items";

    [Header("UI")]
    [Tooltip("Parent transform along the bottom of the screen for item icons.")]
    [SerializeField] private Transform inventoryBarRoot;

    [Tooltip("Prefab with an Image used to show each held item icon.")]
    [SerializeField] private GameObject itemIconPrefab;

    [SerializeField] private KingdomStats kingdomStats;

    private readonly List<Item> catalog = new List<Item>();
    private readonly List<Item> heldItems = new List<Item>();
    private readonly List<GameObject> iconInstances = new List<GameObject>();

    public IReadOnlyList<Item> HeldItems => heldItems;

    private void Awake()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        LoadCatalog();
    }

    private void OnEnable()
    {
        if (kingdomStats != null)
            kingdomStats.TryPreventDeath = TryPreventDeath;
    }

    private void OnDisable()
    {
        if (kingdomStats != null)
            kingdomStats.TryPreventDeath = null;
    }

    public void LoadCatalog()
    {
        catalog.Clear();
        catalog.AddRange(ItemLoader.LoadItems(itemsResourcePath));
    }

    public void ClearInventory()
    {
        heldItems.Clear();
        RebuildIcons();
    }

    /// <summary>
    /// Rebuilds the inventory from saved item IDs (order preserved).
    /// </summary>
    public void RestoreFromSave(string[] itemIds)
    {
        ClearInventory();
        if (itemIds == null)
            return;

        foreach (string id in itemIds)
            GrantItem(id);
    }

    /// <summary>
    /// Grants an item by definition ID (from a card choice). Ignores unknown IDs.
    /// </summary>
    public bool GrantItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        Item definition = ItemLoader.FindById(catalog, itemId);
        if (definition == null)
        {
            Debug.LogWarning($"InventoryManager: Unknown item id '{itemId}'.");
            return false;
        }

        heldItems.Add(definition.Clone());
        RebuildIcons();
        Debug.Log($"InventoryManager: Granted item '{itemId}'.");
        return true;
    }

    public bool HasItem(string itemId)
    {
        return FindHeldIndex(itemId) >= 0;
    }

    /// <summary>
    /// If the player holds an item that blocks this death cause, consume it and restore the stat to 50.
    /// </summary>
    public bool TryPreventDeath(DeathCause cause)
    {
        if (cause == DeathCause.None || kingdomStats == null)
            return false;

        int index = FindPreventingItemIndex(cause);
        if (index < 0)
            return false;

        Item consumed = heldItems[index];
        heldItems.RemoveAt(index);
        RebuildIcons();

        kingdomStats.RestoreStatForDeathCause(cause);
        Debug.Log($"InventoryManager: Consumed '{consumed.id}' to prevent {cause}. Stat restored to 50.");
        return true;
    }

    private int FindPreventingItemIndex(DeathCause cause)
    {
        for (int i = 0; i < heldItems.Count; i++)
        {
            if (heldItems[i] != null && heldItems[i].Prevents(cause))
                return i;
        }

        return -1;
    }

    private int FindHeldIndex(string itemId)
    {
        for (int i = 0; i < heldItems.Count; i++)
        {
            if (heldItems[i] != null && heldItems[i].id == itemId)
                return i;
        }

        return -1;
    }

    private void RebuildIcons()
    {
        for (int i = 0; i < iconInstances.Count; i++)
        {
            if (iconInstances[i] != null)
                Destroy(iconInstances[i]);
        }

        iconInstances.Clear();

        if (inventoryBarRoot == null)
            return;

        foreach (Item item in heldItems)
        {
            GameObject iconObject = CreateIconObject(item);
            if (iconObject != null)
                iconInstances.Add(iconObject);
        }
    }

    private GameObject CreateIconObject(Item item)
    {
        GameObject instance;
        if (itemIconPrefab != null)
        {
            instance = Instantiate(itemIconPrefab, inventoryBarRoot);
        }
        else
        {
            instance = new GameObject(item.id + "_Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            instance.transform.SetParent(inventoryBarRoot, false);
            var rect = instance.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(64f, 64f);
        }

        instance.name = $"Item_{item.id}";
        instance.SetActive(true);

        Image image = instance.GetComponent<Image>();
        if (image == null)
            image = instance.GetComponentInChildren<Image>();

        if (image != null)
        {
            image.sprite = item.icon;
            image.enabled = item.icon != null;
            image.preserveAspect = true;
        }

        return instance;
    }
}
