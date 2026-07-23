using UnityEngine;

/// <summary>
/// Builds the three fixed first-run tutorial cards.
/// </summary>
public static class TutorialCards
{
    public const string PrefsHasCompletedTutorial = "HasCompletedTutorial";

    public static bool HasCompletedTutorial =>
        PlayerPrefs.GetInt(PrefsHasCompletedTutorial, 0) == 1;

    public static void MarkTutorialCompleted()
    {
        PlayerPrefs.SetInt(PrefsHasCompletedTutorial, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Card 0: swipe teaching (right locked).
    /// Card 1: extremes teaching (left locked).
    /// Card 2: free choice into the main loop.
    /// </summary>
    public static Card Create(int index)
    {
        return index switch
        {
            0 => CreateSwipeLesson(),
            1 => CreateDeathLesson(),
            _ => CreateFreeChoice()
        };
    }

    public static SwipeDirectionLock GetRequiredLock(int index)
    {
        return index switch
        {
            0 => SwipeDirectionLock.RightOnly,
            1 => SwipeDirectionLock.LeftOnly,
            _ => SwipeDirectionLock.Both
        };
    }

    private static Card CreateSwipeLesson()
    {
        return new Card
        {
            id = "tutorial_swipe",
            scenarioText = "tutorial.swipe.scenario",
            leftChoiceText = "tutorial.swipe.left",
            rightChoiceText = "tutorial.swipe.right",
            leftChoiceModifiers = new StatModifiers { people = -5 },
            rightChoiceModifiers = new StatModifiers { people = 5, religion = 0, army = 0, wealth = 0 },
            leftChoiceStatusEffects = new StatusEffect[0],
            rightChoiceStatusEffects = new StatusEffect[0]
        };
    }

    private static Card CreateDeathLesson()
    {
        return new Card
        {
            id = "tutorial_death",
            scenarioText = "tutorial.death.scenario",
            leftChoiceText = "tutorial.death.left",
            rightChoiceText = "tutorial.death.right",
            leftChoiceModifiers = new StatModifiers { army = -5 },
            rightChoiceModifiers = new StatModifiers { army = 5 },
            leftChoiceStatusEffects = new StatusEffect[0],
            rightChoiceStatusEffects = new StatusEffect[0]
        };
    }

    private static Card CreateFreeChoice()
    {
        return new Card
        {
            id = "tutorial_begin",
            scenarioText = "tutorial.begin.scenario",
            leftChoiceText = "tutorial.begin.left",
            rightChoiceText = "tutorial.begin.right",
            leftChoiceModifiers = new StatModifiers { religion = 5, people = 5 },
            rightChoiceModifiers = new StatModifiers { army = 5, wealth = 5 },
            leftChoiceStatusEffects = new StatusEffect[0],
            rightChoiceStatusEffects = new StatusEffect[0]
        };
    }
}

/// <summary>
/// Restricts which horizontal swipe directions can commit a choice.
/// </summary>
public enum SwipeDirectionLock
{
    Both = 0,
    LeftOnly = 1,
    RightOnly = 2
}
