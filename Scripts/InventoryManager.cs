using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks held items, renders their icons via an object pool, and consumes death-prevention items.
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

    [SerializeField] private int iconPoolPrewarm = 6;

    [SerializeField] private KingdomStats kingdomStats;

    private readonly List<Item> catalog = new List<Item>();
    private readonly List<Item> heldItems = new List<Item>();
    private readonly List<GameObject> iconInstances = new List<GameObject>();

    private ObjectPool iconPool;
    private Transform poolRoot;
    private static readonly Vector2 DefaultIconSize = new Vector2(64f, 64f);

    public IReadOnlyList<Item> HeldItems => heldItems;

    private void Awake()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        EnsureIconPool();
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

        for (int i = 0; i < itemIds.Length; i++)
            GrantItem(itemIds[i]);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("InventoryManager: Unknown item id.");
#endif
            return false;
        }

        heldItems.Add(definition.Clone());
        RebuildIcons();
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

        heldItems.RemoveAt(index);
        RebuildIcons();

        kingdomStats.RestoreStatForDeathCause(cause);
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

    private void EnsureIconPool()
    {
        if (iconPool != null)
            return;

        if (inventoryBarRoot == null)
            return;

        poolRoot = new GameObject("InventoryIconPool").transform;
        poolRoot.SetParent(inventoryBarRoot, false);
        poolRoot.localScale = Vector3.one;

        GameObject template = itemIconPrefab;
        bool destroyTemplate = false;
        if (template == null)
        {
            template = new GameObject("ItemIconTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            template.transform.SetParent(poolRoot, false);
            var rect = template.GetComponent<RectTransform>();
            rect.sizeDelta = DefaultIconSize;
            destroyTemplate = true;
        }

        iconPool = new ObjectPool(template, inventoryBarRoot, iconPoolPrewarm);

        if (destroyTemplate)
            Destroy(template);
    }

    private void RebuildIcons()
    {
        EnsureIconPool();

        for (int i = 0; i < iconInstances.Count; i++)
        {
            if (iconInstances[i] != null && iconPool != null)
                iconPool.Release(iconInstances[i]);
        }

        iconInstances.Clear();

        if (inventoryBarRoot == null || iconPool == null)
            return;

        for (int i = 0; i < heldItems.Count; i++)
        {
            Item item = heldItems[i];
            if (item == null)
                continue;

            GameObject iconObject = iconPool.Get();
            BindIcon(iconObject, item);
            iconInstances.Add(iconObject);
        }
    }

    private static void BindIcon(GameObject instance, Item item)
    {
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
    }
}
