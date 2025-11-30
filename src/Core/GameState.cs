namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents the overall game state including current room, characters, and inventory.
/// This is the deterministic core that remains independent of LLM decisions.
/// </summary>
public class GameState
{
    public string CurrentRoomId { get; set; } = "start";
    public Dictionary<string, Room> Rooms { get; set; } = new();
    public Character Player { get; set; } = new();
    public Dictionary<string, Character> NPCs { get; set; } = new();
    public Inventory PlayerInventory { get; set; } = new();
    public List<Quest> ActiveQuests { get; set; } = new();
    public List<string> RecentPlayerCommands { get; set; } = new(); // Track last N player commands for LLM context

    // Party and Companions
    public List<string> Companions { get; set; } = new(); // NPC IDs of companions following the player

    // Combat Mode
    public bool InCombatMode { get; set; } = false;
    public string? CurrentCombatNpcId { get; set; } = null;

    public GameState()
    {
        InitializeDefaultGame();
    }

    private void InitializeDefaultGame()
    {
        // Initialize default rooms with exits
        Rooms["start"] = new Room
        {
            Id = "start",
            Name = "Town Square",
            Description = "A bustling marketplace with vendors and adventurers. The air smells of fresh bread and exotic spices.",
            Exits = new()
            {
                { "north", new Exit("North", "forest", "A path leads north into the dark forest") },
                { "east", new Exit("East", "tavern", "You see the warm glow of the Rusty Tankard tavern to the east") },
                { "south", new Exit("South", "merchant_stall", "A merchant's stall stands to the south") }
            },
            NPCIds = new() { "merchant", "bartender" }
        };

        Rooms["tavern"] = new Room
        {
            Id = "tavern",
            Name = "The Rusty Tankard Tavern",
            Description = "A cozy tavern filled with the smell of ale and roasted meat. A roaring fireplace warms the room.",
            Exits = new()
            {
                { "exit", new Exit("Back to Town Square", "start", "Return to the bustling town square") }
            },
            NPCIds = new() { "bartender", "bard" }
        };

        Rooms["forest"] = new Room
        {
            Id = "forest",
            Name = "Dark Forest",
            Description = "A dense, mysterious forest with towering trees that block out most of the sunlight. Strange sounds echo around you.",
            Exits = new()
            {
                { "south", new Exit("South", "start", "Back to the town square") },
                { "deep", new Exit("Deeper Into The Forest", "cave", "A narrow path leads deeper into the forest toward a cave entrance") }
            },
            NPCIds = new() { "ranger" }
        };

        Rooms["cave"] = new Room
        {
            Id = "cave",
            Name = "Goblin Lair",
            Description = "A damp cave reeking of sulfur and danger. Glowing eyes watch you from the darkness.",
            Exits = new()
            {
                { "out", new Exit("Back To The Forest", "forest", "Return to the forest outside") }
            },
            NPCIds = new() { "goblin_shaman" }
        };

        Rooms["merchant_stall"] = new Room
        {
            Id = "merchant_stall",
            Name = "Thadeus's Merchant Stall",
            Description = "A well-organized stall filled with exotic goods. Thadeus stands behind the counter, examining a mysterious artifact.",
            Exits = new()
            {
                { "back", new Exit("Back to Town Square", "start", "Return to the town square") }
            },
            NPCIds = new() { "thadeus" }
        };

        // Initialize player
        Player = new Character
        {
            Id = "player",
            Name = "Adventurer",
            Health = 100,
            MaxHealth = 100,
            Level = 1,
            Experience = 0,
            Strength = 12,
            Agility = 11,
            Armor = 0
        };

        // Initialize NPCs with combat stats
        NPCs["merchant"] = new Character
        {
            Id = "merchant",
            Name = "Market Vendor",
            Health = 50,
            MaxHealth = 50,
            Level = 1,
            Strength = 10,
            Agility = 9,
            Armor = 0
        };

        NPCs["bartender"] = new Character
        {
            Id = "bartender",
            Name = "Old Barrick",
            Health = 45,
            MaxHealth = 45,
            Level = 1,
            Strength = 11,
            Agility = 8,
            Armor = 1
        };

        NPCs["bard"] = new Character
        {
            Id = "bard",
            Name = "Melody the Bard",
            Health = 40,
            MaxHealth = 40,
            Level = 1,
            Strength = 8,
            Agility = 13,
            Armor = 0
        };

        NPCs["ranger"] = new Character
        {
            Id = "ranger",
            Name = "Sylva the Ranger",
            Health = 80,
            MaxHealth = 80,
            Level = 3,
            Strength = 13,
            Agility = 14,
            Armor = 2
        };

        NPCs["goblin_shaman"] = new Character
        {
            Id = "goblin_shaman",
            Name = "Grak the Shaman",
            Health = 60,
            MaxHealth = 60,
            Level = 2,
            Strength = 12,
            Agility = 10,
            Armor = 3
        };

        NPCs["thadeus"] = new Character
        {
            Id = "thadeus",
            Name = "Thadeus the Merchant",
            Health = 50,
            MaxHealth = 50,
            Level = 2,
            Strength = 9,
            Agility = 11,
            Armor = 1
        };
    }

    /// <summary>
    /// Moves the player to a new room using an exit name (e.g., "North", "Into the tavern").
    /// </summary>
    public bool MoveToRoomByExit(string exitName)
    {
        var currentRoom = Rooms[CurrentRoomId];
        var exit = currentRoom.FindExit(exitName);

        if (exit == null)
            return false;

        if (!Rooms.ContainsKey(exit.DestinationRoomId))
            return false;

        CurrentRoomId = exit.DestinationRoomId;
        return true;
    }

    /// <summary>
    /// Moves the player to a new room by room ID (internal use).
    /// </summary>
    public bool MoveToRoom(string roomId)
    {
        if (!Rooms.ContainsKey(roomId))
            return false;

        CurrentRoomId = roomId;
        return true;
    }

    /// <summary>
    /// Gets the current room the player is in.
    /// </summary>
    public Room GetCurrentRoom() => Rooms[CurrentRoomId];

    /// <summary>
    /// Gets an NPC in the current room.
    /// </summary>
    public Character? GetNPCInRoom(string npcId)
    {
        var room = GetCurrentRoom();
        if (!room.NPCIds.Contains(npcId))
            return null;

        return NPCs.ContainsKey(npcId) ? NPCs[npcId] : null;
    }

    /// <summary>
    /// Add a player command to the recent history (keeps last 5 commands).
    /// </summary>
    public void AddRecentCommand(string command)
    {
        RecentPlayerCommands.Add(command);
        // Keep only the last 5 commands
        if (RecentPlayerCommands.Count > 5)
            RecentPlayerCommands.RemoveAt(0);
    }

    /// <summary>
    /// Get formatted string of recent commands for LLM context.
    /// </summary>
    public string GetRecentCommandsContext()
    {
        if (RecentPlayerCommands.Count == 0)
            return "";

        return "Recent actions: " + string.Join(", ", RecentPlayerCommands);
    }

    /// <summary>
    /// Add a companion to follow the player.
    /// </summary>
    public void AddCompanion(string npcId)
    {
        if (!Companions.Contains(npcId) && NPCs.ContainsKey(npcId))
        {
            Companions.Add(npcId);
            if (NPCs[npcId].CanJoinParty)
                NPCs[npcId].Role = NPCRole.Companion;
        }
    }

    /// <summary>
    /// Remove a companion from the party.
    /// </summary>
    public void RemoveCompanion(string npcId)
    {
        if (Companions.Contains(npcId))
        {
            Companions.Remove(npcId);
            if (NPCs.ContainsKey(npcId) && NPCs[npcId].Role == NPCRole.Companion)
                NPCs[npcId].Role = NPCRole.Ally;
        }
    }

    /// <summary>
    /// Move all companions to the player's current room.
    /// </summary>
    public void MoveCompanionsToCurrentRoom()
    {
        var currentRoom = CurrentRoomId;
        foreach (var companionId in Companions)
        {
            if (NPCs.ContainsKey(companionId))
            {
                NPCs[companionId].CurrentRoomId = currentRoom;
            }
        }
    }

    /// <summary>
    /// Get a formatted list of companion names.
    /// </summary>
    public string GetCompanionsList()
    {
        if (Companions.Count == 0)
            return "none";

        var companionNames = Companions
            .Where(id => NPCs.ContainsKey(id))
            .Select(id => NPCs[id].Name)
            .ToList();

        return companionNames.Count > 0 ? string.Join(", ", companionNames) : "none";
    }
}
