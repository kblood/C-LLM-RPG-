namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a complete game definition including story, style, rooms, NPCs, and items.
/// This allows creating different games (fantasy RPG, sci-fi adventure, etc.) with distinct themes.
/// </summary>
public class Game
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    // Game Style and Theme
    public GameStyle Style { get; set; } = GameStyle.Fantasy;
    public string? Description { get; set; }              // Full game description
    public string? StoryIntroduction { get; set; }       // Opening story/lore
    public string? GameObjective { get; set; }           // What the player is trying to achieve
    public int? EstimatedPlayTime { get; set; }          // Minutes

    // Game Rules and Settings
    public int InitialPlayerHealth { get; set; } = 100;
    public int InitialPlayerLevel { get; set; } = 1;
    public string? InitialPlayerDescription { get; set; } = "A brave adventurer";
    public bool Permadeath { get; set; } = false;        // Does player lose on death?
    public bool PvP { get; set; } = false;               // Can player fight NPCs?
    public bool CanRecruitNPCs { get; set; } = true;     // Can NPCs join party?

    // Game Content
    public Dictionary<string, Room> Rooms { get; set; } = new();
    public Dictionary<string, Character> NPCs { get; set; } = new();
    public Dictionary<string, Item> Items { get; set; } = new();
    public List<Quest> Quests { get; set; } = new();

    // Game Progression
    public string StartingRoomId { get; set; } = "start";
    public List<string>? WinConditionRoomIds { get; set; }  // Rooms that indicate victory
    public bool FreeRoam { get; set; } = true;             // Can player go anywhere?

    // Gameplay Features
    public bool HasCombat { get; set; } = true;
    public bool HasInventory { get; set; } = true;
    public bool HasMagic { get; set; } = false;
    public bool HasTechnology { get; set; } = false;

    // Metadata
    public string? Author { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? Version { get; set; } = "1.0.0";
    public Dictionary<string, string> CustomSettings { get; set; } = new();
}

public enum GameStyle
{
    Fantasy,           // Swords, magic, dragons
    SciFi,            // Lasers, technology, aliens
    Steampunk,        // Steam-powered machinery
    Horror,           // Monsters, survival
    Modern,           // Contemporary setting
    Western,          // Cowboys, old west
    Mystery,          // Investigation, puzzles
    Historical,       // Based on real history
    Custom            // User-defined
}
