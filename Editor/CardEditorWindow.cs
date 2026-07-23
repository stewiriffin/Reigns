#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for Reigns card JSON: create, edit, delete, duplicate, and save.
/// Menu: Window → Reign Card Editor
/// </summary>
public class CardEditorWindow : EditorWindow
{
    private const string DefaultRelativePath = "Resources/Cards/event_cards.json";
    private const string PrefsJsonPath = "Reigns.CardEditor.JsonPath";

    private string jsonPath = string.Empty;
    private CardDatabase database;
    private readonly List<CardListItem> flatList = new List<CardListItem>();

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedIndex = -1;
    private string searchFilter = string.Empty;
    private bool dirty;
    private string statusMessage = string.Empty;

    private enum DestinationKind
    {
        BaseDeck = 0,
        UnlockablePool = 1
    }

    [MenuItem("Window/Reign Card Editor")]
    public static void Open()
    {
        var window = GetWindow<CardEditorWindow>("Reign Card Editor");
        window.minSize = new Vector2(920f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        jsonPath = EditorPrefs.GetString(PrefsJsonPath, string.Empty);
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            jsonPath = ResolveDefaultJsonPath();

        if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
            LoadFromDisk();
        else
            EnsureEmptyDatabase();
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawCardList(GUILayout.Width(Mathf.Clamp(position.width * 0.34f, 260f, 420f)));
            DrawCardDetail();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                if (!dirty || EditorUtility.DisplayDialog(
                        "Reload Cards",
                        "Discard unsaved changes and reload from disk?",
                        "Reload",
                        "Cancel"))
                {
                    LoadFromDisk();
                }
            }

            if (GUILayout.Button("Save to JSON", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                SaveToJson();

            GUILayout.Space(8f);

            if (GUILayout.Button("New Card", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                CreateCard();

            using (new EditorGUI.DisabledScope(selectedIndex < 0 || selectedIndex >= flatList.Count))
            {
                if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    DuplicateSelected();

                if (GUILayout.Button("Delete", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    DeleteSelected();
            }

            GUILayout.FlexibleSpace();

            if (dirty)
                GUILayout.Label("● Unsaved", EditorStyles.miniLabel);

            GUILayout.Label(flatList.Count + " cards", EditorStyles.miniLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("JSON Path");
            EditorGUI.BeginChangeCheck();
            jsonPath = EditorGUILayout.TextField(jsonPath);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefsJsonPath, jsonPath ?? string.Empty);

            if (GUILayout.Button("Browse…", GUILayout.Width(70f)))
                BrowseForJson();
        }
    }

    private void DrawCardList(params GUILayoutOption[] options)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, options))
        {
            EditorGUILayout.LabelField("Cards", EditorStyles.boldLabel);
            searchFilter = EditorGUILayout.TextField("Search", searchFilter);

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < flatList.Count; i++)
            {
                CardListItem item = flatList[i];
                if (item?.card == null)
                    continue;

                if (!PassesFilter(item))
                    continue;

                string label = string.IsNullOrWhiteSpace(item.card.id) ? "(no id)" : item.card.id;
                if (!string.IsNullOrEmpty(item.poolId))
                    label = "[" + item.poolId + "] " + label;

                bool selected = i == selectedIndex;
                Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (selected)
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.90f, 0.35f));

                if (GUI.Button(row, label, EditorStyles.label))
                    selectedIndex = i;
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawCardDetail()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (selectedIndex < 0 || selectedIndex >= flatList.Count || flatList[selectedIndex]?.card == null)
            {
                EditorGUILayout.LabelField("Select a card, or click New Card.");
                return;
            }

            CardListItem item = flatList[selectedIndex];
            Card card = item.card;

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            card.id = EditorGUILayout.TextField("ID", card.id ?? string.Empty);
            card.scenarioText = EditorGUILayout.TextField("Scenario Text / Loc Key", card.scenarioText ?? string.Empty);
            card.prerequisiteFlag = EditorGUILayout.TextField("Prerequisite Flag", card.prerequisiteFlag ?? string.Empty);
            card.era = EditorGUILayout.IntSlider(new GUIContent("Era Filter", "0 = any era, 1/2/3 = only that era"), card.era, 0, 3);
            card.portraitResourcePath = EditorGUILayout.TextField("Portrait Resource Path", card.portraitResourcePath ?? string.Empty);
            card.voiceResourcePath = EditorGUILayout.TextField("Voice Resource Path", card.voiceResourcePath ?? string.Empty);

            EditorGUILayout.Space(8f);
            DrawDestination(item);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Left Choice", EditorStyles.boldLabel);
            card.leftChoiceText = EditorGUILayout.TextField("Text / Loc Key", card.leftChoiceText ?? string.Empty);
            EnsureModifiers(ref card.leftChoiceModifiers);
            DrawStatModifiers(card.leftChoiceModifiers);
            card.leftChoiceUnlockFlag = EditorGUILayout.TextField("Unlock Flag", card.leftChoiceUnlockFlag ?? string.Empty);
            card.leftChoiceGrantItem = EditorGUILayout.TextField("Grant Item", card.leftChoiceGrantItem ?? string.Empty);
            card.NextCardID_Left = EditorGUILayout.TextField("Next Card ID", card.NextCardID_Left ?? string.Empty);
            DrawStatusEffects("Status Effects", ref card.leftChoiceStatusEffects);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Right Choice", EditorStyles.boldLabel);
            card.rightChoiceText = EditorGUILayout.TextField("Text / Loc Key", card.rightChoiceText ?? string.Empty);
            EnsureModifiers(ref card.rightChoiceModifiers);
            DrawStatModifiers(card.rightChoiceModifiers);
            card.rightChoiceUnlockFlag = EditorGUILayout.TextField("Unlock Flag", card.rightChoiceUnlockFlag ?? string.Empty);
            card.rightChoiceGrantItem = EditorGUILayout.TextField("Grant Item", card.rightChoiceGrantItem ?? string.Empty);
            card.NextCardID_Right = EditorGUILayout.TextField("Next Card ID", card.NextCardID_Right ?? string.Empty);
            DrawStatusEffects("Status Effects", ref card.rightChoiceStatusEffects);

            if (EditorGUI.EndChangeCheck())
                MarkDirty("Edited card.");

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawDestination(CardListItem item)
    {
        EditorGUILayout.LabelField("Deck Placement", EditorStyles.boldLabel);

        DestinationKind kind = string.IsNullOrEmpty(item.poolId)
            ? DestinationKind.BaseDeck
            : DestinationKind.UnlockablePool;

        EditorGUI.BeginChangeCheck();
        kind = (DestinationKind)EditorGUILayout.EnumPopup("Destination", kind);

        string poolId = item.poolId;
        if (kind == DestinationKind.UnlockablePool)
        {
            EnsurePools();
            string[] poolIds = GetPoolIds();
            int poolIndex = Mathf.Max(0, Array.IndexOf(poolIds, poolId));
            if (poolIds.Length == 0)
            {
                EditorGUILayout.HelpBox("No unlockable pools yet. Creating one named 'new_pool'.", MessageType.Info);
                if (GUILayout.Button("Create Unlockable Pool"))
                {
                    AddPool("new_pool");
                    poolIds = GetPoolIds();
                    poolIndex = 0;
                    MarkDirty("Created unlockable pool.");
                }
            }

            if (poolIds.Length > 0)
            {
                poolIndex = EditorGUILayout.Popup("Pool", Mathf.Clamp(poolIndex, 0, poolIds.Length - 1), poolIds);
                poolId = poolIds[poolIndex];
            }
            else
            {
                poolId = "new_pool";
            }
        }
        else
        {
            poolId = string.Empty;
        }

        if (EditorGUI.EndChangeCheck())
            MoveCard(item, poolId);
    }

    private static void DrawStatModifiers(StatModifiers mods)
    {
        EditorGUI.indentLevel++;
        mods.religion = EditorGUILayout.IntField("Religion", mods.religion);
        mods.people = EditorGUILayout.IntField("People", mods.people);
        mods.army = EditorGUILayout.IntField("Army", mods.army);
        mods.wealth = EditorGUILayout.IntField("Wealth", mods.wealth);
        EditorGUI.indentLevel--;
    }

    private void DrawStatusEffects(string label, ref StatusEffect[] effects)
    {
        if (effects == null)
            effects = Array.Empty<StatusEffect>();

        EditorGUILayout.LabelField(label + " (" + effects.Length + ")");
        EditorGUI.indentLevel++;

        for (int i = 0; i < effects.Length; i++)
        {
            StatusEffect effect = effects[i] ?? (effects[i] = new StatusEffect());
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                effect.targetStat = EditorGUILayout.TextField("Target Stat", effect.targetStat ?? "Religion");
                effect.valuePerTurn = EditorGUILayout.IntField("Value / Turn", effect.valuePerTurn);
                effect.duration = EditorGUILayout.IntField("Duration", effect.duration);
                if (GUILayout.Button("Remove Effect", GUILayout.Width(120f)))
                {
                    effects = RemoveAt(effects, i);
                    MarkDirty("Removed status effect.");
                    GUIUtility.ExitGUI();
                }
            }
        }

        if (GUILayout.Button("Add Status Effect", GUILayout.Width(140f)))
        {
            var list = new List<StatusEffect>(effects)
            {
                new StatusEffect { targetStat = "Religion", valuePerTurn = 0, duration = 1 }
            };
            effects = list.ToArray();
            MarkDirty("Added status effect.");
        }

        EditorGUI.indentLevel--;
    }

    private bool PassesFilter(CardListItem item)
    {
        if (string.IsNullOrWhiteSpace(searchFilter))
            return true;

        string q = searchFilter.Trim();
        Card c = item.card;
        return Contains(c.id, q)
               || Contains(c.scenarioText, q)
               || Contains(c.leftChoiceText, q)
               || Contains(c.rightChoiceText, q)
               || Contains(item.poolId, q);
    }

    private static bool Contains(string value, string query)
    {
        return !string.IsNullOrEmpty(value)
               && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CreateCard()
    {
        EnsureEmptyDatabase();
        var card = CreateBlankCard("new_card_" + (flatList.Count + 1));
        InsertCard(card, poolId: string.Empty);
        RebuildFlatList();
        selectedIndex = FindIndexByCard(card);
        MarkDirty("Created new card.");
    }

    private void DuplicateSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= flatList.Count)
            return;

        CardListItem sourceItem = flatList[selectedIndex];
        Card clone = CloneCard(sourceItem.card);
        clone.id = MakeUniqueId(string.IsNullOrWhiteSpace(clone.id) ? "card" : clone.id + "_copy");
        InsertCard(clone, sourceItem.poolId);
        RebuildFlatList();
        selectedIndex = FindIndexByCard(clone);
        MarkDirty("Duplicated card '" + clone.id + "'.");
    }

    private void DeleteSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= flatList.Count)
            return;

        CardListItem item = flatList[selectedIndex];
        string id = item.card != null ? item.card.id : "(null)";
        if (!EditorUtility.DisplayDialog("Delete Card", "Delete '" + id + "'?", "Delete", "Cancel"))
            return;

        RemoveCard(item);
        RebuildFlatList();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, flatList.Count - 1);
        if (flatList.Count == 0)
            selectedIndex = -1;
        MarkDirty("Deleted card '" + id + "'.");
    }

    private void MoveCard(CardListItem item, string newPoolId)
    {
        if (item == null || item.card == null)
            return;

        if ((item.poolId ?? string.Empty) == (newPoolId ?? string.Empty))
            return;

        Card card = item.card;
        RemoveCard(item);
        InsertCard(card, newPoolId ?? string.Empty);
        RebuildFlatList();
        selectedIndex = FindIndexByCard(card);
        MarkDirty("Moved card to " + (string.IsNullOrEmpty(newPoolId) ? "base deck" : newPoolId) + ".");
    }

    private void InsertCard(Card card, string poolId)
    {
        EnsureEmptyDatabase();
        NormalizeCard(card);

        if (string.IsNullOrEmpty(poolId))
        {
            var list = new List<Card>(database.baseDeck ?? Array.Empty<Card>()) { card };
            database.baseDeck = list.ToArray();
            return;
        }

        EnsurePools();
        UnlockableCardPool pool = FindPool(poolId);
        if (pool == null)
        {
            pool = AddPool(poolId);
        }

        var poolCards = new List<Card>(pool.cards ?? Array.Empty<Card>()) { card };
        pool.cards = poolCards.ToArray();
    }

    private void RemoveCard(CardListItem item)
    {
        if (item == null || item.card == null || database == null)
            return;

        if (string.IsNullOrEmpty(item.poolId))
        {
            var list = new List<Card>(database.baseDeck ?? Array.Empty<Card>());
            list.Remove(item.card);
            database.baseDeck = list.ToArray();
            return;
        }

        UnlockableCardPool pool = FindPool(item.poolId);
        if (pool?.cards == null)
            return;

        var poolCards = new List<Card>(pool.cards);
        poolCards.Remove(item.card);
        pool.cards = poolCards.ToArray();
    }

    private UnlockableCardPool FindPool(string poolId)
    {
        if (database?.unlockablePools == null)
            return null;

        for (int i = 0; i < database.unlockablePools.Length; i++)
        {
            UnlockableCardPool pool = database.unlockablePools[i];
            if (pool != null && pool.id == poolId)
                return pool;
        }

        return null;
    }

    private UnlockableCardPool AddPool(string poolId)
    {
        EnsurePools();
        var pools = new List<UnlockableCardPool>(database.unlockablePools)
        {
            new UnlockableCardPool { id = poolId, cards = Array.Empty<Card>() }
        };
        database.unlockablePools = pools.ToArray();
        return database.unlockablePools[database.unlockablePools.Length - 1];
    }

    private void EnsurePools()
    {
        EnsureEmptyDatabase();
        if (database.unlockablePools == null)
            database.unlockablePools = Array.Empty<UnlockableCardPool>();
    }

    private string[] GetPoolIds()
    {
        EnsurePools();
        var ids = new List<string>();
        for (int i = 0; i < database.unlockablePools.Length; i++)
        {
            UnlockableCardPool pool = database.unlockablePools[i];
            if (pool != null && !string.IsNullOrWhiteSpace(pool.id))
                ids.Add(pool.id);
        }

        return ids.ToArray();
    }

    private void LoadFromDisk()
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            statusMessage = "JSON file not found. Browse to event_cards.json.";
            EnsureEmptyDatabase();
            return;
        }

        try
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            database = CardLoader.ParseDatabase(json);
            NormalizeDatabase(database);
            RebuildFlatList();
            selectedIndex = flatList.Count > 0 ? 0 : -1;
            dirty = false;
            EditorPrefs.SetString(PrefsJsonPath, jsonPath);
            statusMessage = "Loaded " + flatList.Count + " cards from " + jsonPath;
        }
        catch (Exception e)
        {
            statusMessage = "Failed to load: " + e.Message;
            Debug.LogException(e);
        }
    }

    private void SaveToJson()
    {
        if (database == null)
        {
            statusMessage = "Nothing to save.";
            return;
        }

        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            BrowseForJson();
            if (string.IsNullOrWhiteSpace(jsonPath))
                return;
        }

        string directory = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        try
        {
            NormalizeDatabase(database);
            string json = JsonUtility.ToJson(database, true);
            File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AssetDatabase.Refresh();
            dirty = false;
            EditorPrefs.SetString(PrefsJsonPath, jsonPath);
            statusMessage = "Saved " + flatList.Count + " cards → " + jsonPath;
            Debug.Log("Reign Card Editor: saved " + jsonPath);
        }
        catch (Exception e)
        {
            statusMessage = "Save failed: " + e.Message;
            Debug.LogException(e);
        }
    }

    private void BrowseForJson()
    {
        string startDir = !string.IsNullOrWhiteSpace(jsonPath)
            ? Path.GetDirectoryName(jsonPath)
            : Application.dataPath;

        string picked = EditorUtility.SaveFilePanel(
            "Card Deck JSON",
            startDir,
            "event_cards",
            "json");

        if (string.IsNullOrEmpty(picked))
            return;

        jsonPath = picked;
        EditorPrefs.SetString(PrefsJsonPath, jsonPath);
    }

    private void RebuildFlatList()
    {
        flatList.Clear();
        if (database == null)
            return;

        if (database.baseDeck != null)
        {
            for (int i = 0; i < database.baseDeck.Length; i++)
            {
                if (database.baseDeck[i] == null)
                    continue;
                flatList.Add(new CardListItem { poolId = string.Empty, card = database.baseDeck[i] });
            }
        }

        if (database.unlockablePools == null)
            return;

        for (int p = 0; p < database.unlockablePools.Length; p++)
        {
            UnlockableCardPool pool = database.unlockablePools[p];
            if (pool?.cards == null)
                continue;

            string poolId = pool.id ?? string.Empty;
            for (int c = 0; c < pool.cards.Length; c++)
            {
                if (pool.cards[c] == null)
                    continue;
                flatList.Add(new CardListItem { poolId = poolId, card = pool.cards[c] });
            }
        }
    }

    private int FindIndexByCard(Card card)
    {
        for (int i = 0; i < flatList.Count; i++)
        {
            if (flatList[i].card == card)
                return i;
        }

        return -1;
    }

    private string MakeUniqueId(string desired)
    {
        string baseId = string.IsNullOrWhiteSpace(desired) ? "card" : desired.Trim();
        string candidate = baseId;
        int n = 2;
        while (IdExists(candidate))
        {
            candidate = baseId + "_" + n;
            n++;
        }

        return candidate;
    }

    private bool IdExists(string id)
    {
        for (int i = 0; i < flatList.Count; i++)
        {
            Card c = flatList[i].card;
            if (c != null && string.Equals(c.id, id, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void MarkDirty(string message)
    {
        dirty = true;
        statusMessage = message;
    }

    private void EnsureEmptyDatabase()
    {
        if (database != null)
            return;

        database = new CardDatabase
        {
            baseDeck = Array.Empty<Card>(),
            unlockablePools = Array.Empty<UnlockableCardPool>()
        };
        RebuildFlatList();
    }

    private static void NormalizeDatabase(CardDatabase db)
    {
        if (db == null)
            return;

        db.baseDeck ??= Array.Empty<Card>();
        db.unlockablePools ??= Array.Empty<UnlockableCardPool>();

        for (int i = 0; i < db.baseDeck.Length; i++)
            NormalizeCard(db.baseDeck[i]);

        for (int p = 0; p < db.unlockablePools.Length; p++)
        {
            UnlockableCardPool pool = db.unlockablePools[p];
            if (pool == null)
                continue;

            pool.id ??= string.Empty;
            pool.cards ??= Array.Empty<Card>();
            for (int c = 0; c < pool.cards.Length; c++)
                NormalizeCard(pool.cards[c]);
        }
    }

    private static void NormalizeCard(Card card)
    {
        if (card == null)
            return;

        card.id ??= string.Empty;
        card.scenarioText ??= string.Empty;
        card.prerequisiteFlag ??= string.Empty;
        card.portraitResourcePath ??= string.Empty;
        card.voiceResourcePath ??= string.Empty;
        card.leftChoiceText ??= string.Empty;
        card.rightChoiceText ??= string.Empty;
        card.leftChoiceUnlockFlag ??= string.Empty;
        card.rightChoiceUnlockFlag ??= string.Empty;
        card.leftChoiceGrantItem ??= string.Empty;
        card.rightChoiceGrantItem ??= string.Empty;
        card.NextCardID_Left ??= string.Empty;
        card.NextCardID_Right ??= string.Empty;
        EnsureModifiers(ref card.leftChoiceModifiers);
        EnsureModifiers(ref card.rightChoiceModifiers);
        card.leftChoiceStatusEffects ??= Array.Empty<StatusEffect>();
        card.rightChoiceStatusEffects ??= Array.Empty<StatusEffect>();
    }

    private static void EnsureModifiers(ref StatModifiers mods)
    {
        if (mods == null)
            mods = new StatModifiers();
    }

    private static Card CreateBlankCard(string id)
    {
        return new Card
        {
            id = id,
            scenarioText = string.Empty,
            prerequisiteFlag = string.Empty,
            era = 0,
            portraitResourcePath = string.Empty,
            voiceResourcePath = string.Empty,
            leftChoiceText = string.Empty,
            leftChoiceModifiers = new StatModifiers(),
            leftChoiceStatusEffects = Array.Empty<StatusEffect>(),
            leftChoiceUnlockFlag = string.Empty,
            leftChoiceGrantItem = string.Empty,
            rightChoiceText = string.Empty,
            rightChoiceModifiers = new StatModifiers(),
            rightChoiceStatusEffects = Array.Empty<StatusEffect>(),
            rightChoiceUnlockFlag = string.Empty,
            rightChoiceGrantItem = string.Empty,
            NextCardID_Left = string.Empty,
            NextCardID_Right = string.Empty
        };
    }

    private static Card CloneCard(Card source)
    {
        if (source == null)
            return CreateBlankCard("card_copy");

        // Round-trip through JsonUtility for a deep copy of serializable fields.
        string json = JsonUtility.ToJson(source);
        var clone = JsonUtility.FromJson<Card>(json);
        NormalizeCard(clone);
        return clone;
    }

    private static StatusEffect[] RemoveAt(StatusEffect[] source, int index)
    {
        if (source == null || index < 0 || index >= source.Length)
            return source ?? Array.Empty<StatusEffect>();

        var list = new List<StatusEffect>(source);
        list.RemoveAt(index);
        return list.ToArray();
    }

    private static string ResolveDefaultJsonPath()
    {
        string[] candidates =
        {
            Path.Combine(Application.dataPath, "Resources", "Cards", "event_cards.json"),
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Resources", "Cards", "event_cards.json")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), DefaultRelativePath))
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
                return candidates[i];
        }

        return candidates[0];
    }

    [Serializable]
    private sealed class CardListItem
    {
        public string poolId;
        public Card card;
    }
}
#endif
