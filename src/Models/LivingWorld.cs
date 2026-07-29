namespace CSharpRPGBackend.Core;

/// <summary>
/// A structured fact about something that happened in the game world.
/// Events are deliberately data-only so they can be saved, replayed, and consumed
/// by quests, projects, UI news feeds, or an LLM narrator.
/// </summary>
public class WorldEvent
{
    public string Id { get; set; } = string.Empty;
    public WorldEventType Type { get; set; } = WorldEventType.Custom;
    public int TurnNumber { get; set; }
    public string? ActorId { get; set; }
    public string? TargetId { get; set; }
    public string? RoomId { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Message { get; set; }
    public DateTimeOffset? OccurredAtUtc { get; set; }
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static WorldEvent Create(
        WorldEventType type,
        string? targetId = null,
        int quantity = 1,
        string? actorId = null,
        string? roomId = null,
        string? message = null)
    {
        return new WorldEvent
        {
            Type = type,
            TargetId = targetId,
            Quantity = Math.Max(1, quantity),
            ActorId = actorId,
            RoomId = roomId,
            Message = message
        };
    }
}

public enum WorldEventType
{
    PlayerCommand,
    RoomEntered,
    NpcDefeated,
    NpcTalkedTo,
    ItemAcquired,
    ItemGathered,
    ItemCrafted,
    QuestAccepted,
    QuestCompleted,
    QuestTurnedIn,
    PlayerLeveled,
    NpcMoved,
    ResourceRespawned,
    ProjectContributed,
    ProjectAdvanced,
    ProjectCompleted,
    WorldChanged,
    Custom
}

/// <summary>
/// A long-running, staged change to the world, such as rebuilding a town or
/// restoring power to a space station.
/// </summary>
public class WorldProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorldProjectStatus Status { get; set; } = WorldProjectStatus.Available;
    public int CurrentStageIndex { get; set; }
    public List<WorldProjectStage> Stages { get; set; } = new();
    public int? StartedTurn { get; set; }
    public int? CompletedTurn { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public WorldProjectStage? CurrentStage =>
        CurrentStageIndex >= 0 && CurrentStageIndex < Stages.Count
            ? Stages[CurrentStageIndex]
            : null;

    public bool IsComplete => Status == WorldProjectStatus.Completed;
}

public enum WorldProjectStatus
{
    Locked,
    Available,
    Active,
    Completed,
    Failed
}

public class WorldProjectStage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<WorldProjectRequirement> Requirements { get; set; } = new();
    public List<WorldProjectEffect> Effects { get; set; } = new();
    public bool EffectsApplied { get; set; }
    public int? CompletedTurn { get; set; }

    public bool IsComplete => Requirements.All(requirement => requirement.IsMet);
}

public class WorldProjectRequirement
{
    public WorldProjectRequirementType Type { get; set; } = WorldProjectRequirementType.Event;
    public string? TargetId { get; set; }
    public WorldEventType? EventType { get; set; }
    public long RequiredAmount { get; set; } = 1;
    public long CurrentAmount { get; set; }
    public string? Description { get; set; }

    public bool IsMet => CurrentAmount >= Math.Max(1, RequiredAmount);
}

public enum WorldProjectRequirementType
{
    Item,
    Currency,
    Event,
    QuestCompleted
}

/// <summary>
/// A declarative mutation applied when a project stage is completed.
/// TargetId normally identifies the room or NPC; SecondaryTargetId identifies
/// an exit, resource, or item within that target.
/// </summary>
public class WorldProjectEffect
{
    public WorldProjectEffectType Type { get; set; } = WorldProjectEffectType.Custom;
    public string TargetId { get; set; } = string.Empty;
    public string? SecondaryTargetId { get; set; }
    public string? Value { get; set; }
    public int Quantity { get; set; } = 1;
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum WorldProjectEffectType
{
    SetRoomDescription,
    AppendRoomDescription,
    EnableExit,
    DisableExit,
    SetRoomMetadata,
    AddRoomResource,
    AddItemToRoom,
    MoveNpc,
    SetNpcRole,
    Custom
}
