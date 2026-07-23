using System;
using UnityEngine;

/// <summary>
/// Definition of a multi-step story arc that unlocks a Legendary Ending.
/// </summary>
[Serializable]
public class StoryArcDefinition
{
    public string id;
    public string flagKey;
    public string displayName;
    public string description;
    public string badgeGlyph;
    public int requiredSteps = 5;
    public string endingTitle;
    public string endingBody;
}

[Serializable]
public class StoryArcDefinitionCollection
{
    public StoryArcDefinition[] arcs;
}

/// <summary>
/// Runtime progress for one arc during / across reigns.
/// </summary>
[Serializable]
public class StoryArcProgress
{
    public string arcId;
    public int progress;
    public bool endingUnlocked;

    [NonSerialized] public StoryArcDefinition definition;
}

[Serializable]
public class StoryArcProgressSave
{
    public string arcId;
    public int progress;
}

/// <summary>
/// Choice-driven arc step applied from card JSON.
/// Prefer Card.leftChoiceStoryArcId + leftChoiceStoryArcDelta for authoring.
/// </summary>
[Serializable]
public class StoryArcDelta
{
    public string arcId;
    public int delta = 1;
}
