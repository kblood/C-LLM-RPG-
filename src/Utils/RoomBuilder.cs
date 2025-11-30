using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Utils;

/// <summary>
/// Fluent builder for creating rooms with a convenient API.
/// Usage: new RoomBuilder("library")
///     .WithName("Grand Library")
///     .WithDescription("Towering shelves...")
///     .AddExit("North", "study", "A narrow staircase leads north")
///     .AddExit("South", "entrance", "Return to the entrance")
///     .AddNPC("librarian")
///     .Build()
/// </summary>
public class RoomBuilder
{
    private readonly Room _room;

    public RoomBuilder(string roomId)
    {
        _room = new Room { Id = roomId };
    }

    public RoomBuilder WithName(string name)
    {
        _room.Name = name;
        return this;
    }

    public RoomBuilder WithDescription(string description)
    {
        _room.Description = description;
        return this;
    }

    /// <summary>
    /// Add an exit to the room.
    /// </summary>
    public RoomBuilder AddExit(string displayName, string destinationRoomId, string? description = null)
    {
        var exitKey = displayName.ToLower().Replace(" ", "_");
        _room.Exits[exitKey] = new Exit(displayName, destinationRoomId, description);
        return this;
    }

    /// <summary>
    /// Add an NPC to the room.
    /// </summary>
    public RoomBuilder AddNPC(string npcId)
    {
        if (!_room.NPCIds.Contains(npcId))
        {
            _room.NPCIds.Add(npcId);
        }
        return this;
    }

    /// <summary>
    /// Add multiple NPCs to the room.
    /// </summary>
    public RoomBuilder AddNPCs(params string[] npcIds)
    {
        foreach (var npcId in npcIds)
        {
            AddNPC(npcId);
        }
        return this;
    }

    /// <summary>
    /// Add an item to the room floor.
    /// </summary>
    public RoomBuilder AddItem(Item item)
    {
        _room.Items.Add(item);
        return this;
    }

    /// <summary>
    /// Add metadata to the room.
    /// </summary>
    public RoomBuilder WithMetadata(string key, object value)
    {
        _room.Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Set multiple metadata entries.
    /// </summary>
    public RoomBuilder WithMetadata(Dictionary<string, object> metadata)
    {
        foreach (var kvp in metadata)
        {
            _room.Metadata[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Build and return the room.
    /// </summary>
    public Room Build()
    {
        return _room;
    }
}

/// <summary>
/// Fluent builder for creating NPCs with a convenient API.
/// </summary>
public class NpcBuilder
{
    private readonly Character _character;

    public NpcBuilder(string npcId, string name)
    {
        _character = new Character { Id = npcId, Name = name };
    }

    public NpcBuilder WithHealth(int health, int maxHealth)
    {
        _character.Health = health;
        _character.MaxHealth = maxHealth;
        return this;
    }

    public NpcBuilder WithLevel(int level)
    {
        _character.Level = level;
        return this;
    }

    public NpcBuilder WithAlignment(CharacterAlignment alignment)
    {
        _character.Alignment = alignment;
        return this;
    }

    public NpcBuilder WithPersonalityPrompt(string prompt)
    {
        _character.PersonalityPrompt = prompt;
        return this;
    }

    public NpcBuilder AddSkill(string skillName, int value)
    {
        _character.Skills[skillName] = value;
        return this;
    }

    public NpcBuilder WithStats(int strength, int agility, int armor)
    {
        _character.Strength = strength;
        _character.Agility = agility;
        _character.Armor = armor;
        return this;
    }

    public NpcBuilder WithStrength(int strength)
    {
        _character.Strength = strength;
        return this;
    }

    public NpcBuilder WithAgility(int agility)
    {
        _character.Agility = agility;
        return this;
    }

    public NpcBuilder WithArmor(int armor)
    {
        _character.Armor = armor;
        return this;
    }

    /// <summary>
    /// Add an item that the NPC carries (for looting when defeated).
    /// </summary>
    public NpcBuilder WithLoot(Item item, int quantity = 1)
    {
        _character.CarriedItems[item.Id] = new InventoryItem { Item = item, Quantity = quantity };
        return this;
    }

    /// <summary>
    /// Build and return the NPC.
    /// </summary>
    public Character Build()
    {
        return _character;
    }
}
