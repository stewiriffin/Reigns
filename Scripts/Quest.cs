using System;
using UnityEngine;

/// <summary>How a quest's win condition is evaluated during a run.</summary>
public enum QuestConditionType
{
    /// <summary>Survive until YearsRuled reaches targetValue.</summary>
    SurviveYears = 0,

    /// <summary>
    /// Survive targetValue years without <see cref="QuestDefinition.targetStat"/>
    /// dropping below <see cref="QuestDefinition.threshold"/>.
    /// </summary>
    SurviveYearsStatAbove = 1,

    /// <summary>
    /// Survive targetValue years without the target stat rising above threshold.
    /// </summary>
    SurviveYearsStatBelow = 2,

    /// <summary>Reach targetValue on the target stat at any point this run.</summary>
    ReachStatAtLeast = 3,

    /// <summary>Draw / display targetValue unique cards this run.</summary>
    SeeUniqueCards = 4
}

/// <summary>What is granted when a quest completes.</summary>
public enum QuestRewardType
{
    None = 0,
    /// <summary>Sets a MetaProgression flag (can unlock cards via prerequisiteFlag).</summary>
    UnlockFlag = 1,
    /// <summary>Unlocks an Achievement by id.</summary>
    UnlockAchievement = 2
}

/// <summary>
/// Static quest definition (from JSON). Runtime progress lives on <see cref="ActiveQuest"/>.
/// </summary>
[Serializable]
public class QuestDefinition
{
    public string id;
    public string description;
    public QuestConditionType conditionType = QuestConditionType.SurviveYears;
    public int targetValue = 10;
    public string targetStat = "Army";
    public int threshold = 30;
    public QuestRewardType rewardType = QuestRewardType.None;
    public string rewardId = "";
    public string rewardDescription = "";

    public StatType GetTargetStatType()
    {
        if (string.IsNullOrWhiteSpace(targetStat))
            return StatType.Army;

        return Enum.TryParse(targetStat.Trim(), ignoreCase: true, out StatType parsed)
            ? parsed
            : StatType.Army;
    }
}

[Serializable]
public class QuestDefinitionCollection
{
    public QuestDefinition[] quests;
}

/// <summary>Live progress for one slot in the active quest drawer.</summary>
[Serializable]
public class ActiveQuest
{
    public string questId;
    public int progress;
    public bool failed;
    public bool completed;

    [NonSerialized] public QuestDefinition definition;

    public float NormalizedProgress
    {
        get
        {
            if (definition == null || definition.targetValue <= 0)
                return 0f;
            return Mathf.Clamp01(progress / (float)definition.targetValue);
        }
    }

    public string ProgressLabel
    {
        get
        {
            if (definition == null)
                return string.Empty;

            if (failed)
                return "Failed";

            if (completed)
                return "Complete";

            return $"{Mathf.Min(progress, definition.targetValue)}/{definition.targetValue}";
        }
    }
}
