using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Loads the card database and builds the active deck from base + unlocked pools.
/// </summary>
public static class CardLoader
{
    private const string DefaultResourcePath = "Cards/event_cards";

    /// <summary>
    /// Loads the full card database from Resources (no file extension).
    /// </summary>
    public static CardDatabase LoadDatabase(string resourcePath = DefaultResourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogError($"CardLoader: Could not find JSON at Resources/{resourcePath}.json");
            return new CardDatabase
            {
                baseDeck = new Card[0],
                unlockablePools = new UnlockableCardPool[0]
            };
        }

        return ParseDatabase(asset.text);
    }

    /// <summary>
    /// Parses base deck + unlockable pools. Also accepts legacy { "cards": [ ... ] } as baseDeck.
    /// </summary>
    public static CardDatabase ParseDatabase(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("CardLoader: JSON string is empty.");
            return new CardDatabase
            {
                baseDeck = new Card[0],
                unlockablePools = new UnlockableCardPool[0]
            };
        }

        CardDatabase database = JsonUtility.FromJson<CardDatabase>(json);
        if (database == null)
            database = new CardDatabase();

        database.baseDeck ??= new Card[0];
        database.unlockablePools ??= new UnlockableCardPool[0];

        // Legacy format: { "cards": [ ... ] } → treat as base deck.
        if (database.baseDeck.Length == 0)
        {
            CardCollection legacy = JsonUtility.FromJson<CardCollection>(json);
            if (legacy?.cards != null && legacy.cards.Length > 0)
                database.baseDeck = legacy.cards;
        }

        return database;
    }

    /// <summary>
    /// Loads Sprite / AudioClip references on every card via Resources paths.
    /// </summary>
    public static void ResolveAllAssets(CardDatabase database)
    {
        if (database == null)
            return;

        if (database.baseDeck != null)
        {
            foreach (Card card in database.baseDeck)
                card?.ResolveAssets();
        }

        if (database.unlockablePools == null)
            return;

        foreach (UnlockableCardPool pool in database.unlockablePools)
        {
            if (pool?.cards == null)
                continue;

            foreach (Card card in pool.cards)
                card?.ResolveAssets();
        }
    }

    /// <summary>
    /// Builds the active draw deck: all base cards, plus unlockable cards whose
    /// <see cref="Card.prerequisiteFlag"/> is unlocked (or empty).
    /// </summary>
    public static List<Card> BuildActiveDeck(CardDatabase database)
    {
        var activeDeck = new List<Card>();
        if (database == null)
            return activeDeck;

        if (database.baseDeck != null)
        {
            foreach (Card card in database.baseDeck)
            {
                if (card == null)
                    continue;

                // Base cards with a prerequisite still respect the flag (optional gating).
                if (IsCardUnlocked(card))
                    activeDeck.Add(card);
            }
        }

        if (database.unlockablePools == null)
            return DeduplicateById(activeDeck);

        foreach (UnlockableCardPool pool in database.unlockablePools)
        {
            if (pool?.cards == null)
                continue;

            foreach (Card card in pool.cards)
            {
                if (card == null)
                    continue;

                if (IsCardUnlocked(card))
                    activeDeck.Add(card);
            }
        }

        return DeduplicateById(activeDeck);
    }

    /// <summary>
    /// Convenience: load JSON, resolve art/audio, and build the active deck.
    /// </summary>
    public static List<Card> LoadActiveDeck(string resourcePath = DefaultResourcePath)
    {
        CardDatabase database = LoadDatabase(resourcePath);
        ResolveAllAssets(database);
        return BuildActiveDeck(database);
    }

    /// <summary>
    /// Flattens every card in the database (base + all unlockable pools), regardless of unlock flags.
    /// Used to resolve forced follow-up cards by ID.
    /// </summary>
    public static List<Card> FlattenCatalog(CardDatabase database)
    {
        var catalog = new List<Card>();
        if (database == null)
            return catalog;

        if (database.baseDeck != null)
        {
            foreach (Card card in database.baseDeck)
            {
                if (card != null)
                    catalog.Add(card);
            }
        }

        if (database.unlockablePools == null)
            return DeduplicateById(catalog);

        foreach (UnlockableCardPool pool in database.unlockablePools)
        {
            if (pool?.cards == null)
                continue;

            foreach (Card card in pool.cards)
            {
                if (card != null)
                    catalog.Add(card);
            }
        }

        return DeduplicateById(catalog);
    }

    /// <summary>
    /// Loads JSON, resolves assets, and returns the full card catalog (for ID lookups / chains).
    /// </summary>
    public static List<Card> LoadCatalog(string resourcePath = DefaultResourcePath)
    {
        CardDatabase database = LoadDatabase(resourcePath);
        ResolveAllAssets(database);
        return FlattenCatalog(database);
    }

    /// <summary>
    /// Legacy helper — returns base deck cards only (no unlock injection).
    /// Prefer <see cref="LoadActiveDeck"/>.
    /// </summary>
    public static List<Card> LoadCards(string resourcePath = DefaultResourcePath)
    {
        CardDatabase database = LoadDatabase(resourcePath);
        ResolveAllAssets(database);
        if (database.baseDeck == null || database.baseDeck.Length == 0)
            return new List<Card>();

        return database.baseDeck.ToList();
    }

    public static bool IsCardUnlocked(Card card)
    {
        if (card == null)
            return false;

        if (string.IsNullOrWhiteSpace(card.prerequisiteFlag))
            return true;

        return MetaProgression.HasFlag(card.prerequisiteFlag);
    }

    public static Card FindById(IList<Card> cards, string id)
    {
        if (cards == null || string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].id == id)
                return cards[i];
        }

        return null;
    }

    /// <summary>
    /// Appends remote base-deck cards and unlockable pools onto <paramref name="local"/>.
    /// Cards whose <see cref="Card.id"/> already exists locally are skipped (local wins).
    /// </summary>
    public static CardDatabase MergeDatabases(CardDatabase local, CardDatabase remote)
    {
        if (local == null)
            return remote ?? new CardDatabase { baseDeck = new Card[0], unlockablePools = new UnlockableCardPool[0] };
        if (remote == null)
            return local;

        local.baseDeck ??= new Card[0];
        local.unlockablePools ??= new UnlockableCardPool[0];
        remote.baseDeck ??= new Card[0];
        remote.unlockablePools ??= new UnlockableCardPool[0];

        var knownIds = new HashSet<string>();
        CollectIds(local, knownIds);

        var mergedBase = new List<Card>(local.baseDeck);
        for (int i = 0; i < remote.baseDeck.Length; i++)
        {
            Card card = remote.baseDeck[i];
            if (card == null || string.IsNullOrWhiteSpace(card.id))
                continue;
            if (!knownIds.Add(card.id))
                continue;
            mergedBase.Add(card);
        }

        local.baseDeck = mergedBase.ToArray();

        var mergedPools = new List<UnlockableCardPool>(local.unlockablePools);
        for (int p = 0; p < remote.unlockablePools.Length; p++)
        {
            UnlockableCardPool remotePool = remote.unlockablePools[p];
            if (remotePool == null)
                continue;

            UnlockableCardPool target = FindPool(mergedPools, remotePool.id);
            if (target == null)
            {
                // New pool — copy only cards with unique ids.
                var poolCards = new List<Card>();
                if (remotePool.cards != null)
                {
                    for (int c = 0; c < remotePool.cards.Length; c++)
                    {
                        Card card = remotePool.cards[c];
                        if (card == null || string.IsNullOrWhiteSpace(card.id))
                            continue;
                        if (!knownIds.Add(card.id))
                            continue;
                        poolCards.Add(card);
                    }
                }

                mergedPools.Add(new UnlockableCardPool
                {
                    id = remotePool.id ?? string.Empty,
                    cards = poolCards.ToArray()
                });
            }
            else
            {
                var poolCards = new List<Card>(target.cards ?? new Card[0]);
                if (remotePool.cards != null)
                {
                    for (int c = 0; c < remotePool.cards.Length; c++)
                    {
                        Card card = remotePool.cards[c];
                        if (card == null || string.IsNullOrWhiteSpace(card.id))
                            continue;
                        if (!knownIds.Add(card.id))
                            continue;
                        poolCards.Add(card);
                    }
                }

                target.cards = poolCards.ToArray();
            }
        }

        local.unlockablePools = mergedPools.ToArray();
        return local;
    }

    private static void CollectIds(CardDatabase database, HashSet<string> knownIds)
    {
        if (database.baseDeck != null)
        {
            for (int i = 0; i < database.baseDeck.Length; i++)
            {
                Card card = database.baseDeck[i];
                if (card != null && !string.IsNullOrWhiteSpace(card.id))
                    knownIds.Add(card.id);
            }
        }

        if (database.unlockablePools == null)
            return;

        for (int p = 0; p < database.unlockablePools.Length; p++)
        {
            UnlockableCardPool pool = database.unlockablePools[p];
            if (pool?.cards == null)
                continue;

            for (int c = 0; c < pool.cards.Length; c++)
            {
                Card card = pool.cards[c];
                if (card != null && !string.IsNullOrWhiteSpace(card.id))
                    knownIds.Add(card.id);
            }
        }
    }

    private static UnlockableCardPool FindPool(List<UnlockableCardPool> pools, string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i] != null && pools[i].id == id)
                return pools[i];
        }

        return null;
    }

    private static List<Card> DeduplicateById(List<Card> cards)
    {
        var seen = new HashSet<string>();
        var result = new List<Card>(cards.Count);

        foreach (Card card in cards)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
            {
                result.Add(card);
                continue;
            }

            if (seen.Add(card.id))
                result.Add(card);
        }

        return result;
    }
}
