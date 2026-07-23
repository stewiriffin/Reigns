#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless playtest: runs many random (or lowest-stat) reigns against the card JSON
/// and prints balance stats to the Console.
/// </summary>
public static class DeckSimulationMenu
{
    private const string CardsResourcePath = "Cards/event_cards";
    private const int DefaultRunCount = 1000;
    private const int MaxYearsPerRun = 500;
    private const float UnderusedFractionOfAverage = 0.25f;

    private enum ChoicePolicy
    {
        Random,
        LowestStatPriority
    }

    [MenuItem("Tools/Run Deck Simulation")]
    public static void RunDeckSimulation()
    {
        CardDatabase database = LoadDatabase();
        if (database == null)
            return;

        // Full catalog so unlockable cards are included in balance testing.
        List<Card> catalog = CardLoader.FlattenCatalog(database);
        List<Card> deck = catalog.Where(c => c != null && !string.IsNullOrWhiteSpace(c.id)).ToList();

        if (deck.Count == 0)
        {
            Debug.LogError("Deck Simulation: No cards found in JSON. Check Resources/Cards/event_cards.json.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("========== DECK SIMULATION REPORT ==========");
        report.AppendLine($"Deck cards: {deck.Count} | Runs per policy: {DefaultRunCount} | Year cap: {MaxYearsPerRun}");
        report.AppendLine("(Unlock flags ignored — full catalog is always in the draw pool.)");
        report.AppendLine("(Inventory death-prevention and second chance disabled.)");
        report.AppendLine();

        try
        {
            EditorUtility.DisplayProgressBar("Deck Simulation", "Random policy…", 0f);
            AppendPolicyReport(report, deck, catalog, ChoicePolicy.Random, DefaultRunCount, 0f, 0.5f);

            EditorUtility.DisplayProgressBar("Deck Simulation", "Lowest-stat policy…", 0.5f);
            AppendPolicyReport(report, deck, catalog, ChoicePolicy.LowestStatPriority, DefaultRunCount, 0.5f, 1f);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        report.AppendLine("============================================");
        Debug.Log(report.ToString());
    }

    private static void AppendPolicyReport(
        StringBuilder report,
        List<Card> deck,
        List<Card> catalog,
        ChoicePolicy policy,
        int runCount,
        float progressStart,
        float progressEnd)
    {
        var years = new List<int>(runCount);
        var deathCounts = new Dictionary<DeathCause, int>();
        var drawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int unfinished = 0;

        foreach (Card card in deck)
        {
            if (card != null && !string.IsNullOrEmpty(card.id))
                drawCounts[card.id] = 0;
        }

        var host = new GameObject("DeckSimulation_KingdomStats")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        KingdomStats stats = host.AddComponent<KingdomStats>();
        stats.TryPreventDeath = null;

        try
        {
            for (int i = 0; i < runCount; i++)
            {
                if (i % 25 == 0)
                {
                    float t = progressStart + (progressEnd - progressStart) * (i / (float)runCount);
                    EditorUtility.DisplayProgressBar(
                        "Deck Simulation",
                        $"{policy}: run {i + 1}/{runCount}",
                        t);
                }

                SimResult result = SimulateRun(deck, catalog, stats, policy, new System.Random(i * 7919 + (int)policy));
                years.Add(result.YearsSurvived);

                foreach (KeyValuePair<string, int> pair in result.DrawCounts)
                {
                    if (!drawCounts.ContainsKey(pair.Key))
                        drawCounts[pair.Key] = 0;
                    drawCounts[pair.Key] += pair.Value;
                }

                if (result.Cause == DeathCause.None)
                {
                    unfinished++;
                    continue;
                }

                if (!deathCounts.ContainsKey(result.Cause))
                    deathCounts[result.Cause] = 0;
                deathCounts[result.Cause]++;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        double avgYears = years.Count > 0 ? years.Average() : 0.0;
        years.Sort();
        double medianYears = Median(years);
        int minYears = years.Count > 0 ? years[0] : 0;
        int maxYears = years.Count > 0 ? years[years.Count - 1] : 0;

        report.AppendLine($"--- Policy: {PolicyLabel(policy)} ---");
        report.AppendLine($"Average years survived: {avgYears:0.00}");
        report.AppendLine($"Median / Min / Max years: {medianYears:0.0} / {minYears} / {maxYears}");
        if (unfinished > 0)
            report.AppendLine($"Hit year cap ({MaxYearsPerRun}) without death: {unfinished} ({Pct(unfinished, runCount)})");
        report.AppendLine();

        report.AppendLine("Most common causes of death:");
        int resolvedDeaths = deathCounts.Values.Sum();
        if (resolvedDeaths == 0)
        {
            report.AppendLine("  (no deaths recorded)");
        }
        else
        {
            foreach (KeyValuePair<DeathCause, int> pair in deathCounts.OrderByDescending(p => p.Value))
            {
                report.AppendLine(
                    $"  {FormatDeathCause(pair.Key)} caused {Pct(pair.Value, resolvedDeaths)} of deaths ({pair.Value}/{resolvedDeaths})");
            }
        }

        report.AppendLine();
        report.AppendLine("Underused cards (rarely drawn):");
        long totalDraws = drawCounts.Values.Sum(v => (long)v);
        double avgPerCard = deck.Count > 0 ? totalDraws / (double)deck.Count : 0.0;
        double underusedThreshold = avgPerCard * UnderusedFractionOfAverage;

        var underused = drawCounts
            .OrderBy(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Where(p => p.Value <= underusedThreshold)
            .ToList();

        if (underused.Count == 0)
        {
            report.AppendLine($"  None — all cards were within {UnderusedFractionOfAverage:P0} of average draws ({avgPerCard:0.0}).");
        }
        else
        {
            report.AppendLine($"  Threshold: ≤ {underusedThreshold:0.0} draws (avg {avgPerCard:0.0} per card, total draws {totalDraws}).");
            foreach (KeyValuePair<string, int> pair in underused)
            {
                double share = totalDraws > 0 ? 100.0 * pair.Value / totalDraws : 0.0;
                report.AppendLine($"  {pair.Key}: {pair.Value} draws ({share:0.00}% of all draws)");
            }
        }

        report.AppendLine();
        report.AppendLine("Draw frequency (all cards):");
        foreach (KeyValuePair<string, int> pair in drawCounts.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
        {
            double share = totalDraws > 0 ? 100.0 * pair.Value / totalDraws : 0.0;
            report.AppendLine($"  {pair.Key}: {pair.Value} ({share:0.00}%)");
        }

        report.AppendLine();
    }

    private static SimResult SimulateRun(
        List<Card> deck,
        List<Card> catalog,
        KingdomStats stats,
        ChoicePolicy policy,
        System.Random rng)
    {
        stats.ResetStats();
        var effects = new StatusEffectTracker();
        var drawPile = new List<Card>(deck.Count);
        Refill(drawPile, deck, rng);

        string lastCardId = null;
        string forcedNextId = null;
        int years = 0;
        var draws = new Dictionary<string, int>(StringComparer.Ordinal);

        while (years < MaxYearsPerRun && !stats.IsGameOver)
        {
            if (effects.Tick(stats))
                break;

            if (stats.IsGameOver)
                break;

            Card card = DrawNext(drawPile, deck, catalog, ref forcedNextId, ref lastCardId, rng);
            if (card == null)
                break;

            if (!string.IsNullOrEmpty(card.id))
            {
                if (!draws.ContainsKey(card.id))
                    draws[card.id] = 0;
                draws[card.id]++;
            }

            bool chooseLeft = ChooseLeft(card, stats, policy, rng);
            StatModifiers mods = chooseLeft ? card.leftChoiceModifiers : card.rightChoiceModifiers;
            StatusEffect[] granted = chooseLeft ? card.leftChoiceStatusEffects : card.rightChoiceStatusEffects;
            string nextId = chooseLeft ? card.NextCardID_Left : card.NextCardID_Right;

            mods?.Apply(stats);
            effects.AddRange(granted);
            forcedNextId = string.IsNullOrWhiteSpace(nextId) ? null : nextId.Trim();

            years++;

            if (stats.IsGameOver)
                break;
        }

        return new SimResult
        {
            YearsSurvived = years,
            Cause = stats.IsGameOver ? stats.LastDeathCause : DeathCause.None,
            DrawCounts = draws
        };
    }

    private static bool ChooseLeft(Card card, KingdomStats stats, ChoicePolicy policy, System.Random rng)
    {
        if (policy == ChoicePolicy.Random)
            return rng.Next(2) == 0;

        StatModifiers left = card.leftChoiceModifiers ?? new StatModifiers();
        StatModifiers right = card.rightChoiceModifiers ?? new StatModifiers();

        int leftScore = ScoreForLowestStats(left, stats);
        int rightScore = ScoreForLowestStats(right, stats);

        if (leftScore > rightScore)
            return true;
        if (rightScore > leftScore)
            return false;
        return rng.Next(2) == 0;
    }

    /// <summary>
    /// Prefers the choice that raises the currently lowest stats the most,
    /// with a soft penalty for pushing any already-high/low stat further toward death.
    /// </summary>
    private static int ScoreForLowestStats(StatModifiers mods, KingdomStats stats)
    {
        int r = stats.Religion;
        int p = stats.People;
        int a = stats.Army;
        int w = stats.Wealth;
        int lowest = Mathf.Min(Mathf.Min(r, p), Mathf.Min(a, w));

        int score = 0;
        if (r == lowest) score += mods.religion * 3;
        if (p == lowest) score += mods.people * 3;
        if (a == lowest) score += mods.army * 3;
        if (w == lowest) score += mods.wealth * 3;

        // Mild help for any below-average stat.
        if (r < KingdomStats.DefaultStat) score += mods.religion;
        if (p < KingdomStats.DefaultStat) score += mods.people;
        if (a < KingdomStats.DefaultStat) score += mods.army;
        if (w < KingdomStats.DefaultStat) score += mods.wealth;

        // Penalize moves that drive a threatened high/low extreme harder.
        score -= ExtremePressurePenalty(r, mods.religion);
        score -= ExtremePressurePenalty(p, mods.people);
        score -= ExtremePressurePenalty(a, mods.army);
        score -= ExtremePressurePenalty(w, mods.wealth);

        return score;
    }

    private static int ExtremePressurePenalty(int current, int delta)
    {
        if (delta == 0)
            return 0;

        int projected = Mathf.Clamp(current + delta, KingdomStats.MinStat, KingdomStats.MaxStat);
        int penalty = 0;

        if (current <= 20 && delta < 0)
            penalty += Mathf.Abs(delta) * 2;
        if (current >= 80 && delta > 0)
            penalty += Mathf.Abs(delta) * 2;
        if (projected <= KingdomStats.MinStat || projected >= KingdomStats.MaxStat)
            penalty += 50;

        return penalty;
    }

    private static Card DrawNext(
        List<Card> drawPile,
        List<Card> deck,
        List<Card> catalog,
        ref string forcedNextId,
        ref string lastCardId,
        System.Random rng)
    {
        if (!string.IsNullOrWhiteSpace(forcedNextId))
        {
            string id = forcedNextId;
            forcedNextId = null;
            Card forced = CardLoader.FindById(catalog, id);
            if (forced != null)
            {
                RemoveById(drawPile, id);
                lastCardId = forced.id;
                return forced;
            }
        }

        if (drawPile.Count == 0)
            Refill(drawPile, deck, rng);

        if (drawPile.Count == 0)
            return null;

        int index = 0;
        if (drawPile.Count > 1 && !string.IsNullOrEmpty(lastCardId))
        {
            for (int i = 0; i < drawPile.Count; i++)
            {
                if (drawPile[i] != null && drawPile[i].id != lastCardId)
                {
                    index = i;
                    break;
                }
            }
        }

        Card card = drawPile[index];
        drawPile.RemoveAt(index);
        lastCardId = card != null ? card.id : null;
        return card;
    }

    private static void Refill(List<Card> drawPile, List<Card> deck, System.Random rng)
    {
        drawPile.Clear();
        drawPile.AddRange(deck);
        for (int i = drawPile.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            Card tmp = drawPile[i];
            drawPile[i] = drawPile[j];
            drawPile[j] = tmp;
        }
    }

    private static void RemoveById(List<Card> drawPile, string id)
    {
        for (int i = drawPile.Count - 1; i >= 0; i--)
        {
            if (drawPile[i] != null && drawPile[i].id == id)
                drawPile.RemoveAt(i);
        }
    }

    private static CardDatabase LoadDatabase()
    {
        CardDatabase database = CardLoader.LoadDatabase(CardsResourcePath);
        if (database != null && database.baseDeck != null && database.baseDeck.Length > 0)
            return database;

        string[] candidates =
        {
            Path.Combine(Application.dataPath, "Resources", "Cards", "event_cards.json"),
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, "Resources", "Cards", "event_cards.json"),
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Resources", "Cards", "event_cards.json"))
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;

            string json = File.ReadAllText(path);
            database = CardLoader.ParseDatabase(json);
            if (database?.baseDeck != null && database.baseDeck.Length > 0)
            {
                Debug.Log($"Deck Simulation: Loaded cards from file '{path}'.");
                return database;
            }
        }

        Debug.LogError(
            "Deck Simulation: Could not load card JSON. Expected Resources/Cards/event_cards.json.");
        return null;
    }

    private static string PolicyLabel(ChoicePolicy policy)
    {
        return policy == ChoicePolicy.Random
            ? "Random choices"
            : "Prioritize lowest stats";
    }

    private static string FormatDeathCause(DeathCause cause)
    {
        switch (cause)
        {
            case DeathCause.ReligionEmpty: return "Religion = 0";
            case DeathCause.ReligionFull: return "Religion = 100";
            case DeathCause.PeopleEmpty: return "People = 0";
            case DeathCause.PeopleFull: return "People = 100";
            case DeathCause.ArmyEmpty: return "Army = 0";
            case DeathCause.ArmyFull: return "Army = 100";
            case DeathCause.WealthEmpty: return "Wealth = 0";
            case DeathCause.WealthFull: return "Wealth = 100";
            default: return cause.ToString();
        }
    }

    private static string Pct(int count, int total)
    {
        if (total <= 0)
            return "0%";
        return $"{100.0 * count / total:0.0}%";
    }

    private static double Median(List<int> sorted)
    {
        if (sorted == null || sorted.Count == 0)
            return 0.0;
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
            return sorted[mid];
        return (sorted[mid - 1] + sorted[mid]) * 0.5;
    }

    private struct SimResult
    {
        public int YearsSurvived;
        public DeathCause Cause;
        public Dictionary<string, int> DrawCounts;
    }
}
#endif
