using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Holds up to 3 active items, renders an action bar under the card UI,
/// and supports tap-to-trigger consumable effects plus passive death shields.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public const int MaxHeldItems = 3;

    public static InventoryManager Instance { get; private set; }

    [Header("Data")]
    [SerializeField] private string itemsResourcePath = "Items/items";

    [Header("UI")]
    [Tooltip("Parent transform along the bottom of the card UI for item slots.")]
    [SerializeField] private Transform inventoryBarRoot;
    [SerializeField] private GameObject itemIconPrefab;
    [SerializeField] private int iconPoolPrewarm = 4;
    [SerializeField] private bool buildBarIfMissing = true;
    [SerializeField] private Canvas targetCanvas;

    [SerializeField] private KingdomStats kingdomStats;

    private readonly List<Item> catalog = new List<Item>();
    private readonly List<Item> heldItems = new List<Item>();
    private readonly List<SlotView> slots = new List<SlotView>(MaxHeldItems);

    private ObjectPool iconPool;
    private Transform poolRoot;
    private GameManager gameManager;
    private TextMeshProUGUI toastLabel;
    private bool barBuilt;

    private static readonly Vector2 DefaultIconSize = new Vector2(96f, 96f);

    /// <summary>Fired after grant, consume, use, clear, or restore.</summary>
    public event Action OnInventoryChanged;

    public IReadOnlyList<Item> HeldItems => heldItems;
    public bool IsFull => heldItems.Count >= MaxHeldItems;

    private struct SlotView
    {
        public GameObject root;
        public Button button;
        public Image icon;
        public Image frame;
        public TextMeshProUGUI label;
        public int index;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        gameManager = FindObjectOfType<GameManager>();
        LoadCatalog();

        if (buildBarIfMissing)
            EnsureActionBar();

        EnsureIconPool();
        RebuildIcons();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        if (kingdomStats != null)
            kingdomStats.TryPreventDeath = TryPreventDeath;
    }

    private void OnDisable()
    {
        if (kingdomStats != null && kingdomStats.TryPreventDeath == TryPreventDeath)
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
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Rebuilds the inventory from saved item IDs (order preserved, capped at max slots).
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
    /// Grants an item by definition ID. Fails silently on unknown IDs; fails when full.
    /// </summary>
    public bool GrantItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        Item definition = ItemLoader.FindById(catalog, itemId);
        if (definition == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"InventoryManager: Unknown item id '{itemId}'.");
#endif
            return false;
        }

        if (heldItems.Count >= MaxHeldItems)
        {
            ShowToast("Inventory full (3/3)");
            return false;
        }

        heldItems.Add(definition.Clone());
        RebuildIcons();
        OnInventoryChanged?.Invoke();
        ShowToast($"Gained: {definition.displayName}");
        return true;
    }

    public bool HasItem(string itemId)
    {
        return FindHeldIndex(itemId) >= 0;
    }

    /// <summary>
    /// Removes one held copy of <paramref name="itemId"/> (e.g. trade cost).
    /// </summary>
    public bool ConsumeItem(string itemId)
    {
        int index = FindHeldIndex(itemId);
        if (index < 0)
            return false;

        heldItems.RemoveAt(index);
        RebuildIcons();
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// True when a card trade can pay its item + optional wealth/stat cost.
    /// </summary>
    public bool CanAffordTrade(string consumeItemId, StatModifiers cost)
    {
        if (!string.IsNullOrWhiteSpace(consumeItemId) && !HasItem(consumeItemId))
            return false;

        if (cost == null || kingdomStats == null)
            return true;

        // Costs are negative deltas; ensure the resulting stats stay in range conceptually.
        // Soft check: if cost would require spending more wealth than held, still allow
        // (stats clamp) — only hard-block missing required items.
        return true;
    }

    /// <summary>
    /// Player tapped an action-bar slot. Applies the item effect and consumes when configured.
    /// </summary>
    public bool TryUseItemAt(int slotIndex)
    {
        if (!CanUseItemsNow())
        {
            ShowToast("Not now");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= heldItems.Count)
            return false;

        Item item = heldItems[slotIndex];
        if (item == null)
            return false;

        if (!item.HasActiveEffect)
        {
            ShowToast(string.IsNullOrWhiteSpace(item.description)
                ? $"{item.displayName}: passive relic"
                : item.description);
            return false;
        }

        if (kingdomStats == null)
            return false;

        StatModifiers effect = item.useEffect;
        if (FloatingStatText.Instance != null)
            FloatingStatText.Instance.PlayChoiceFeedback(effect);

        if (StatFeedbackParticles.Instance != null)
            StatFeedbackParticles.Instance.PlayChoiceFeedback(effect);

        effect.Apply(kingdomStats);

        if (gameManager != null)
            gameManager.RefreshHudAfterItemUse();

        string name = item.displayName;
        if (item.consumeOnUse)
        {
            heldItems.RemoveAt(slotIndex);
            RebuildIcons();
            OnInventoryChanged?.Invoke();
            ShowToast($"Used {name}");
        }
        else
        {
            ShowToast($"Triggered {name}");
        }

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();

        return true;
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

        string name = heldItems[index] != null ? heldItems[index].displayName : "Relic";
        heldItems.RemoveAt(index);
        RebuildIcons();
        OnInventoryChanged?.Invoke();

        kingdomStats.RestoreStatForDeathCause(cause);
        ShowToast($"{name} saved you");
        return true;
    }

    private bool CanUseItemsNow()
    {
        if (kingdomStats == null || kingdomStats.IsGameOver)
            return false;

        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();

        if (gameManager != null && !gameManager.CanUseInventoryItems)
            return false;

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
        if (string.IsNullOrWhiteSpace(itemId))
            return -1;

        for (int i = 0; i < heldItems.Count; i++)
        {
            if (heldItems[i] != null && heldItems[i].id == itemId)
                return i;
        }

        return -1;
    }

    private void EnsureActionBar()
    {
        if (barBuilt && inventoryBarRoot != null)
            return;

        if (inventoryBarRoot != null && inventoryBarRoot.childCount > 0)
        {
            CacheExistingSlots();
            barBuilt = true;
            return;
        }

        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();

        if (targetCanvas == null)
        {
            var canvasGo = new GameObject(
                "InventoryCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            targetCanvas = canvasGo.GetComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 40;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform canvasRt = targetCanvas.transform as RectTransform;

        var barGo = new GameObject("ItemActionBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
        barGo.transform.SetParent(canvasRt, false);
        var barRt = barGo.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0f);
        barRt.anchorMax = new Vector2(0.5f, 0f);
        barRt.pivot = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = new Vector2(0f, 36f);
        barRt.sizeDelta = new Vector2(420f, 140f);
        barGo.GetComponent<Image>().color = new Color(0.08f, 0.07f, 0.06f, 0.72f);

        var layout = barGo.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        inventoryBarRoot = barGo.transform;

        slots.Clear();
        for (int i = 0; i < MaxHeldItems; i++)
            slots.Add(CreateSlot(inventoryBarRoot, i));

        var toastGo = new GameObject("ItemToast", typeof(RectTransform), typeof(TextMeshProUGUI));
        toastGo.transform.SetParent(canvasRt, false);
        var toastRt = toastGo.GetComponent<RectTransform>();
        toastRt.anchorMin = new Vector2(0.5f, 0f);
        toastRt.anchorMax = new Vector2(0.5f, 0f);
        toastRt.pivot = new Vector2(0.5f, 0f);
        toastRt.anchoredPosition = new Vector2(0f, 188f);
        toastRt.sizeDelta = new Vector2(720f, 40f);
        toastLabel = toastGo.GetComponent<TextMeshProUGUI>();
        toastLabel.fontSize = 24f;
        toastLabel.alignment = TextAlignmentOptions.Center;
        toastLabel.color = new Color(0.92f, 0.88f, 0.75f, 0f);
        toastLabel.raycastTarget = false;

        barBuilt = true;
    }

    private void CacheExistingSlots()
    {
        slots.Clear();
        if (inventoryBarRoot == null)
            return;

        Button[] buttons = inventoryBarRoot.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < MaxHeldItems; i++)
        {
            if (i < buttons.Length)
            {
                int index = i;
                var view = new SlotView
                {
                    root = buttons[i].gameObject,
                    button = buttons[i],
                    icon = buttons[i].GetComponent<Image>(),
                    frame = buttons[i].GetComponent<Image>(),
                    index = index
                };
                buttons[i].onClick.RemoveAllListeners();
                buttons[i].onClick.AddListener(() => TryUseItemAt(index));
                slots.Add(view);
            }
            else
            {
                slots.Add(CreateSlot(inventoryBarRoot, i));
            }
        }
    }

    private SlotView CreateSlot(Transform parent, int index)
    {
        var slotGo = new GameObject(
            $"ItemSlot_{index}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        slotGo.transform.SetParent(parent, false);

        var rt = slotGo.GetComponent<RectTransform>();
        rt.sizeDelta = DefaultIconSize;

        var le = slotGo.GetComponent<LayoutElement>();
        le.preferredWidth = DefaultIconSize.x;
        le.preferredHeight = DefaultIconSize.y;

        var frame = slotGo.GetComponent<Image>();
        frame.color = new Color(0.18f, 0.16f, 0.14f, 0.95f);

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(slotGo.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.12f, 0.22f);
        iconRt.anchorMax = new Vector2(0.88f, 0.92f);
        iconRt.offsetMin = Vector2.zero;
        iconRt.offsetMax = Vector2.zero;
        var icon = iconGo.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(slotGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(1f, 0.22f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 14f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.85f, 0.82f, 0.75f, 1f);
        label.text = string.Empty;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10f;
        label.fontSizeMax = 14f;

        var button = slotGo.GetComponent<Button>();
        button.targetGraphic = frame;
        int captured = index;
        button.onClick.AddListener(() => TryUseItemAt(captured));

        return new SlotView
        {
            root = slotGo,
            button = button,
            icon = icon,
            frame = frame,
            label = label,
            index = index
        };
    }

    private void EnsureIconPool()
    {
        if (iconPool != null || inventoryBarRoot == null)
            return;

        // Slots are fixed; pool kept for optional prefab-driven icons.
        poolRoot = new GameObject("InventoryIconPool").transform;
        poolRoot.SetParent(inventoryBarRoot, false);

        GameObject template = itemIconPrefab;
        bool destroyTemplate = false;
        if (template == null)
        {
            template = new GameObject("ItemIconTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            template.transform.SetParent(poolRoot, false);
            template.GetComponent<RectTransform>().sizeDelta = DefaultIconSize;
            destroyTemplate = true;
        }

        iconPool = new ObjectPool(template, poolRoot, iconPoolPrewarm);
        if (destroyTemplate)
            Destroy(template);
    }

    private void RebuildIcons()
    {
        if (buildBarIfMissing)
            EnsureActionBar();

        if (slots.Count == 0)
            return;

        for (int i = 0; i < slots.Count; i++)
        {
            SlotView slot = slots[i];
            bool hasItem = i < heldItems.Count && heldItems[i] != null;
            Item item = hasItem ? heldItems[i] : null;

            if (slot.icon != null)
            {
                if (hasItem && item.icon != null)
                {
                    slot.icon.sprite = item.icon;
                    slot.icon.enabled = true;
                    slot.icon.color = Color.white;
                }
                else if (hasItem)
                {
                    slot.icon.sprite = null;
                    slot.icon.enabled = true;
                    slot.icon.color = ItemFallbackColor(item);
                }
                else
                {
                    slot.icon.sprite = null;
                    slot.icon.enabled = false;
                }
            }

            if (slot.frame != null)
            {
                slot.frame.color = hasItem
                    ? (item != null && item.HasActiveEffect
                        ? new Color(0.32f, 0.28f, 0.18f, 0.98f)
                        : new Color(0.22f, 0.2f, 0.18f, 0.95f))
                    : new Color(0.12f, 0.11f, 0.1f, 0.65f);
            }

            if (slot.label != null)
            {
                if (!hasItem)
                    slot.label.text = "—";
                else if (item.HasActiveEffect)
                    slot.label.text = "USE";
                else
                    slot.label.text = "HOLD";
            }

            if (slot.button != null)
                slot.button.interactable = hasItem;
        }
    }

    private static Color ItemFallbackColor(Item item)
    {
        if (item == null)
            return new Color(0.4f, 0.4f, 0.4f, 1f);

        if (item.id == "potion_of_health")
            return new Color(0.35f, 0.7f, 0.4f, 1f);
        if (item.id == "bribery_coin")
            return new Color(0.85f, 0.7f, 0.25f, 1f);
        if (item.id == "royal_seal")
            return new Color(0.55f, 0.45f, 0.8f, 1f);

        return new Color(0.55f, 0.5f, 0.4f, 1f);
    }

    private Coroutine toastRoutine;

    private void ShowToast(string message)
    {
        if (toastLabel == null || string.IsNullOrEmpty(message))
            return;

        if (toastRoutine != null)
            StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(message));
    }

    private System.Collections.IEnumerator ToastRoutine(string message)
    {
        toastLabel.text = message;
        Color c = toastLabel.color;
        c.a = 1f;
        toastLabel.color = c;

        float hold = 1.35f;
        float fade = 0.45f;
        float elapsed = 0f;
        while (elapsed < hold)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < fade)
        {
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, elapsed / fade);
            toastLabel.color = c;
            yield return null;
        }

        c.a = 0f;
        toastLabel.color = c;
        toastRoutine = null;
    }
}
