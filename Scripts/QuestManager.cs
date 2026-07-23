using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks up to 3 active mini-quests per run, evaluates conditions, grants rewards,
/// and asks <see cref="QuestUI"/> to show completion banners / drawer updates.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public const int MaxActiveQuests = 3;
    private const string DefaultResourcePath = "Quests/quests";
    private const string PrefsCompletedPrefix = "QuestCompleted_";

    public static QuestManager Instance { get; private set; }

    [SerializeField] private string questsResourcePath = DefaultResourcePath;
    [SerializeField] private int maxActive = MaxActiveQuests;
    [SerializeField] private KingdomStats kingdomStats;
    [SerializeField] private QuestUI questUi;

    private readonly List<QuestDefinition> pool = new List<QuestDefinition>();
    private readonly List<ActiveQuest> active = new List<ActiveQuest>(MaxActiveQuests);
    private readonly HashSet<string> seenCardIdsThisRun = new HashSet<string>();
    private readonly HashSet<string> completedThisRun = new HashSet<string>();
    private readonly Queue<ActiveQuest> completionQueue = new Queue<ActiveQuest>();

    private bool runActive;

    public IReadOnlyList<ActiveQuest> ActiveQuests => active;
    public event Action OnActiveQuestsChanged;
    public event Action<ActiveQuest> OnQuestCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadPool();

        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (questUi == null)
            questUi = FindObjectOfType<QuestUI>();
    }

    private void OnEnable()
    {
        if (kingdomStats == null)
            kingdomStats = FindObjectOfType<KingdomStats>();

        if (kingdomStats != null)
        {
            kingdomStats.OnStatsChanged -= HandleStatsChanged;
            kingdomStats.OnStatsChanged += HandleStatsChanged;
        }
    }

    private void OnDisable()
    {
        if (kingdomStats != null)
            kingdomStats.OnStatsChanged -= HandleStatsChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Call at the start of a non-tutorial run.</summary>
    public void BeginRun()
    {
        runActive = true;
        seenCardIdsThisRun.Clear();
        completedThisRun.Clear();
        completionQueue.Clear();
        active.Clear();

        FillActiveSlots();
        OnActiveQuestsChanged?.Invoke();

        if (questUi != null)
            questUi.RefreshDrawer();
    }

    public void EndRun()
    {
        runActive = false;
    }

    public void NotifyYearsRuled(int yearsRuled)
    {
        if (!runActive)
            return;

        for (int i = 0; i < active.Count; i++)
        {
            ActiveQuest quest = active[i];
            if (quest == null || quest.definition == null || quest.completed || quest.failed)
                continue;

            switch (quest.definition.conditionType)
            {
                case QuestConditionType.SurviveYears:
                case QuestConditionType.SurviveYearsStatAbove:
                case QuestConditionType.SurviveYearsStatBelow:
                    if (!quest.failed)
                        quest.progress = yearsRuled;
                    break;
            }
        }

        EvaluateCompletions();
        OnActiveQuestsChanged?.Invoke();
    }

    public void NotifyCardSeen(Card card)
    {
        if (!runActive || card == null || string.IsNullOrWhiteSpace(card.id))
            return;

        if (!seenCardIdsThisRun.Add(card.id.Trim()))
            return;

        for (int i = 0; i < active.Count; i++)
        {
            ActiveQuest quest = active[i];
            if (quest?.definition == null || quest.completed || quest.failed)
                continue;

            if (quest.definition.conditionType == QuestConditionType.SeeUniqueCards)
                quest.progress = seenCardIdsThisRun.Count;
        }

        EvaluateCompletions();
        OnActiveQuestsChanged?.Invoke();
    }

    private void HandleStatsChanged()
    {
        if (!runActive || kingdomStats == null)
            return;

        for (int i = 0; i < active.Count; i++)
        {
            ActiveQuest quest = active[i];
            if (quest?.definition == null || quest.completed || quest.failed)
                continue;

            QuestDefinition def = quest.definition;
            int statValue = GetStat(def.GetTargetStatType());

            switch (def.conditionType)
            {
                case QuestConditionType.SurviveYearsStatAbove:
                    if (statValue < def.threshold)
                    {
                        quest.failed = true;
                        quest.progress = 0;
                    }
                    break;

                case QuestConditionType.SurviveYearsStatBelow:
                    if (statValue > def.threshold)
                    {
                        quest.failed = true;
                        quest.progress = 0;
                    }
                    break;

                case QuestConditionType.ReachStatAtLeast:
                    quest.progress = Mathf.Max(quest.progress, statValue);
                    break;
            }
        }

        EvaluateCompletions();
        OnActiveQuestsChanged?.Invoke();
    }

    private void EvaluateCompletions()
    {
        var toReplace = new List<ActiveQuest>();

        for (int i = 0; i < active.Count; i++)
        {
            ActiveQuest quest = active[i];
            if (quest?.definition == null || quest.completed)
                continue;

            if (quest.failed)
            {
                toReplace.Add(quest);
                continue;
            }

            if (!IsConditionMet(quest))
                continue;

            CompleteQuest(quest);
        }

        while (completionQueue.Count > 0)
        {
            ActiveQuest done = completionQueue.Dequeue();
            ReplaceCompletedQuest(done);
        }

        for (int i = 0; i < toReplace.Count; i++)
            ReplaceFailedQuest(toReplace[i]);

        OnActiveQuestsChanged?.Invoke();
        if (questUi != null)
            questUi.RefreshDrawer();
    }

    private void ReplaceFailedQuest(ActiveQuest failed)
    {
        int index = active.IndexOf(failed);
        if (index < 0)
            return;

        // Keep id excluded for this run so the same failed quest doesn't immediately reappear.
        completedThisRun.Add(failed.questId);

        ActiveQuest replacement = DrawNewQuest(ExcludeActiveAndCompleted());
        if (replacement != null)
            active[index] = replacement;
        else
            active.RemoveAt(index);
    }

    private static bool IsConditionMet(ActiveQuest quest)
    {
        QuestDefinition def = quest.definition;
        switch (def.conditionType)
        {
            case QuestConditionType.SurviveYears:
            case QuestConditionType.SurviveYearsStatAbove:
            case QuestConditionType.SurviveYearsStatBelow:
            case QuestConditionType.SeeUniqueCards:
                return !quest.failed && quest.progress >= def.targetValue;

            case QuestConditionType.ReachStatAtLeast:
                return quest.progress >= def.targetValue;

            default:
                return false;
        }
    }

    private void CompleteQuest(ActiveQuest quest)
    {
        if (quest == null || quest.completed)
            return;

        quest.completed = true;
        quest.progress = Mathf.Max(quest.progress, quest.definition.targetValue);
        completedThisRun.Add(quest.questId);
        MarkQuestCompletedLifetime(quest.questId);

        GrantReward(quest.definition);
        completionQueue.Enqueue(quest);
        OnQuestCompleted?.Invoke(quest);
    }

    private void GrantReward(QuestDefinition def)
    {
        if (def == null)
            return;

        switch (def.rewardType)
        {
            case QuestRewardType.UnlockFlag:
                if (!string.IsNullOrWhiteSpace(def.rewardId))
                    MetaProgression.SetFlag(def.rewardId, true);
                break;

            case QuestRewardType.UnlockAchievement:
                if (!string.IsNullOrWhiteSpace(def.rewardId) && AchievementManager.Instance != null)
                    AchievementManager.Instance.Unlock(def.rewardId);
                break;
        }
    }

    private void ReplaceCompletedQuest(ActiveQuest completed)
    {
        int index = active.IndexOf(completed);
        if (index < 0)
            return;

        ActiveQuest replacement = DrawNewQuest(ExcludeActiveAndCompleted());
        if (replacement != null)
            active[index] = replacement;
        else
            active.RemoveAt(index);
    }

    private void FillActiveSlots()
    {
        int cap = Mathf.Clamp(maxActive, 1, MaxActiveQuests);
        var exclude = ExcludeActiveAndCompleted();

        while (active.Count < cap)
        {
            ActiveQuest next = DrawNewQuest(exclude);
            if (next == null)
                break;
            active.Add(next);
            exclude.Add(next.questId);
        }
    }

    private ActiveQuest DrawNewQuest(HashSet<string> exclude)
    {
        var candidates = new List<QuestDefinition>();
        for (int i = 0; i < pool.Count; i++)
        {
            QuestDefinition def = pool[i];
            if (def == null || string.IsNullOrWhiteSpace(def.id))
                continue;
            if (exclude != null && exclude.Contains(def.id))
                continue;
            candidates.Add(def);
        }

        if (candidates.Count == 0)
            return null;

        QuestDefinition pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return new ActiveQuest
        {
            questId = pick.id,
            definition = pick,
            progress = 0,
            failed = false,
            completed = false
        };
    }

    private HashSet<string> ExcludeActiveAndCompleted()
    {
        var set = new HashSet<string>(completedThisRun);
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] != null && !string.IsNullOrEmpty(active[i].questId))
                set.Add(active[i].questId);
        }

        return set;
    }

    private int GetStat(StatType stat)
    {
        if (kingdomStats == null)
            return 0;

        return stat switch
        {
            StatType.Religion => kingdomStats.Religion,
            StatType.People => kingdomStats.People,
            StatType.Army => kingdomStats.Army,
            StatType.Wealth => kingdomStats.Wealth,
            _ => 0
        };
    }

    private void LoadPool()
    {
        pool.Clear();
        TextAsset asset = Resources.Load<TextAsset>(questsResourcePath);
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
        {
            Debug.LogWarning("QuestManager: No quests JSON found — using built-in defaults.");
            pool.AddRange(CreateDefaultQuests());
            return;
        }

        QuestDefinitionCollection collection = JsonUtility.FromJson<QuestDefinitionCollection>(asset.text);
        if (collection?.quests == null || collection.quests.Length == 0)
        {
            pool.AddRange(CreateDefaultQuests());
            return;
        }

        for (int i = 0; i < collection.quests.Length; i++)
        {
            if (collection.quests[i] != null && !string.IsNullOrWhiteSpace(collection.quests[i].id))
                pool.Add(collection.quests[i]);
        }

        if (pool.Count == 0)
            pool.AddRange(CreateDefaultQuests());
    }

    private static List<QuestDefinition> CreateDefaultQuests()
    {
        return new List<QuestDefinition>
        {
            new QuestDefinition
            {
                id = "survive_5",
                description = "Survive 5 years on the throne.",
                conditionType = QuestConditionType.SurviveYears,
                targetValue = 5,
                rewardType = QuestRewardType.UnlockFlag,
                rewardId = "Quest_Survive5",
                rewardDescription = "Unlocked: Seasoned Ruler flag"
            },
            new QuestDefinition
            {
                id = "army_guard_10",
                description = "Survive 10 years without Army dropping below 30.",
                conditionType = QuestConditionType.SurviveYearsStatAbove,
                targetValue = 10,
                targetStat = "Army",
                threshold = 30,
                rewardType = QuestRewardType.UnlockFlag,
                rewardId = "Quest_ArmyGuard",
                rewardDescription = "Unlocked: Iron Garrison flag"
            },
            new QuestDefinition
            {
                id = "wealth_60",
                description = "Raise Wealth to at least 60.",
                conditionType = QuestConditionType.ReachStatAtLeast,
                targetValue = 60,
                targetStat = "Wealth",
                rewardType = QuestRewardType.UnlockFlag,
                rewardId = "Quest_Wealth60",
                rewardDescription = "Unlocked: Full Coffers flag"
            }
        };
    }

    private static void MarkQuestCompletedLifetime(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
            return;

        PlayerPrefs.SetInt(PrefsCompletedPrefix + questId.Trim(), 1);
        PlayerPrefs.Save();
    }

    public static bool HasEverCompleted(string questId)
    {
        if (string.IsNullOrWhiteSpace(questId))
            return false;
        return PlayerPrefs.GetInt(PrefsCompletedPrefix + questId.Trim(), 0) == 1;
    }
}
