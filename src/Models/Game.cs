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
    public List<string>? WinConditionRoomIds { get; set; }  // Rooms that indicate victory (legacy)
    public List<WinCondition>? WinConditions { get; set; }  // New flexible win condition system
    public bool FreeRoam { get; set; } = true;             // Can player go anywhere?

    // Gameplay Features
    public bool HasCombat { get; set; } = true;
    public bool HasInventory { get; set; } = true;
    public bool HasMagic { get; set; } = false;
    public bool HasTechnology { get; set; } = false;

    // Equipment System
    /// <summary>
    /// Defines the equipment slots available in this game.
    /// If null, will use the default configuration.
    /// </summary>
    public EquipmentSlotConfiguration? EquipmentSlots { get; set; }

    /// <summary>
    /// Gets the equipment slot configuration for this game, using default if not specified.
    /// </summary>
    public EquipmentSlotConfiguration GetEquipmentSlots()
    {
        return EquipmentSlots ?? EquipmentSlotConfiguration.CreateDefault();
    }

    // Economy System
    /// <summary>
    /// Economy configuration for this game. If null, economy is disabled.
    /// </summary>
    public EconomyConfig? Economy { get; set; }

    /// <summary>
    /// Gets the economy configuration, defaulting to disabled if not set.
    /// </summary>
    public EconomyConfig GetEconomy()
    {
        return Economy ?? EconomyConfig.Disabled();
    }

    /// <summary>
    /// Starting currency for the player (in base units).
    /// </summary>
    public long StartingCurrency { get; set; } = 0;

    // Game Master Authority
    /// <summary>
    /// Configures what the Game Master can dynamically create or decide.
    /// </summary>
    public GameMasterAuthority? Authority { get; set; }

    /// <summary>
    /// Gets the GM authority, defaulting to Balanced if not set.
    /// </summary>
    public GameMasterAuthority GetAuthority()
    {
        return Authority ?? GameMasterAuthority.Balanced();
    }

    // Crafting System
    /// <summary>
    /// Crafting configuration for this game.
    /// </summary>
    public CraftingConfig? Crafting { get; set; }

    /// <summary>
    /// Gets the crafting config, defaulting to disabled if not set.
    /// </summary>
    public CraftingConfig GetCrafting()
    {
        return Crafting ?? CraftingConfig.Disabled();
    }

    // Metadata
    public string? Author { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? Version { get; set; } = "1.0.0";
    public Dictionary<string, string> CustomSettings { get; set; } = new();
}

/// <summary>
/// Represents a win condition that determines victory in the game.
/// Can be based on entering a room, obtaining an item, defeating an NPC, or completing a quest.
/// </summary>
public class WinCondition
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Type can be "room", "item", "npc_defeat", or "quest_complete"
    public string Type { get; set; } = "room";

    // The target depends on type:
    // - "room": Room ID to enter
    // - "item": Item ID to obtain
    // - "npc_defeat": NPC ID to defeat
    // - "quest_complete": Quest ID to complete
    public string? TargetId { get; set; }

    // Narrative for victory
    public string? VictoryNarration { get; set; }
    public string? VictoryMessage { get; set; }
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
