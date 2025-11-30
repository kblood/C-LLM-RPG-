namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a quest in the game world.
/// </summary>
public class Quest
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GiverNpcId { get; set; } = string.Empty;
    public QuestStatus Status { get; set; } = QuestStatus.Offered;
    public int RewardExperience { get; set; }
    public int RewardGold { get; set; }
    public List<string> Objectives { get; set; } = new();
    public List<string> CompletedObjectives { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public bool IsComplete => CompletedObjectives.Count == Objectives.Count;
}

public enum QuestStatus
{
    Offered,
    Accepted,
    InProgress,
    Completed,
    Failed
}
