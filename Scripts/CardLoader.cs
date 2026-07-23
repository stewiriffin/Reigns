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
    /// Convenience: load JSON and build the active deck in one call.
    /// </summary>
    public static List<Card> LoadActiveDeck(string resourcePath = DefaultResourcePath)
    {
        return BuildActiveDeck(LoadDatabase(resourcePath));
    }

    /// <summary>
    /// Legacy helper — returns base deck cards only (no unlock injection).
    /// Prefer <see cref="LoadActiveDeck"/>.
    /// </summary>
    public static List<Card> LoadCards(string resourcePath = DefaultResourcePath)
    {
        CardDatabase database = LoadDatabase(resourcePath);
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
