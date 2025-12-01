namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a character (player or NPC) in the game world.
/// </summary>
public class Character
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }                     // "the Wise", "the Brave"
    public string? Portrait { get; set; }                 // ASCII art or description

    // Health and Combat
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public Dictionary<string, int> Skills { get; set; } = new();
    public CharacterAlignment Alignment { get; set; } = CharacterAlignment.Neutral;

    // Core Stats
    public int Strength { get; set; } = 10;           // Affects damage
    public int Agility { get; set; } = 10;            // Affects dodge/critical chance
    public int Armor { get; set; } = 0;               // Damage reduction from armor rating

    // Equipment slots
    public Dictionary<string, string?> EquipmentSlots { get; set; } = new()
    {
        { "head", null },
        { "chest", null },
        { "hands", null },
        { "legs", null },
        { "feet", null },
        { "main_hand", null },
        { "off_hand", null }
    };

    // NPC-specific: For NPCs only: system prompt and personality traits for LLM.
    public string? PersonalityPrompt { get; set; }

    /// <summary>
    /// For NPCs only: conversation history and memory.
    /// </summary>
    public List<ConversationEntry> ConversationHistory { get; set; } = new();

    /// <summary>
    /// Items that an NPC has (for loot when defeated, or items they carry).
    /// </summary>
    public Dictionary<string, InventoryItem> CarriedItems { get; set; } = new();

    /// <summary>
    /// The character's wallet containing currency.
    /// </summary>
    public Wallet Wallet { get; set; } = new();

    // NPC Location and Movement
    public string? CurrentRoomId { get; set; }             // Where NPC is currently located
    public string? HomeRoomId { get; set; }                // Where NPC normally stays
    public List<string>? PatrolRoomIds { get; set; }      // Rooms NPC patrols through
    public bool CanMove { get; set; } = true;             // Can NPC move between rooms?
    public bool CanJoinParty { get; set; } = false;       // Can player recruit this NPC?
    public int? PatrolIntervalTurns { get; set; }         // Turn interval between moves
    public int TurnsSinceLastMove { get; set; } = 0;      // Track for patrol timing

    // NPC Behavior
    public NPCRole Role { get; set; } = NPCRole.CommonPerson;
    public string? Description { get; set; }              // Full character description
    public string? Backstory { get; set; }                // NPC's history
    public List<string>? Relationships { get; set; }      // Other NPC IDs they interact with
    public Dictionary<string, int> Reputation { get; set; } = new(); // Reputation with factions

    // Crafting capabilities
    /// <summary>
    /// Whether this NPC can craft items.
    /// </summary>
    public bool CanCraft { get; set; } = false;

    /// <summary>
    /// Recipe IDs this NPC knows how to craft.
    /// </summary>
    public List<string> KnownRecipes { get; set; } = new();

    /// <summary>
    /// Crafting specialty (e.g., "blacksmith", "alchemy", "tailoring").
    /// NPCs can craft any recipe matching their specialty.
    /// </summary>
    public string? CraftingSpecialty { get; set; }

    // Quest giving
    /// <summary>
    /// Quest IDs this NPC can offer.
    /// </summary>
    public List<string> OfferedQuests { get; set; } = new();

    /// <summary>
    /// Whether this NPC can dynamically generate jobs/quests.
    /// </summary>
    public bool CanOfferJobs { get; set; } = false;

    public bool IsAlive => Health > 0;
    public bool IsPlayer { get; set; } = false;

    public void TakeDamage(int amount)
    {
        Health = Math.Max(0, Health - amount);
    }

    public void Heal(int amount)
    {
        Health = Math.Min(MaxHealth, Health + amount);
    }

    public void GainExperience(int amount)
    {
        Experience += amount;
        // TODO: Check for level up
    }

    /// <summary>
    /// Get the NPC's current location room ID.
    /// </summary>
    public string GetCurrentLocation()
    {
        return CurrentRoomId ?? HomeRoomId ?? string.Empty;
    }

    /// <summary>
    /// Move NPC to a new room.
    /// </summary>
    public void MoveToRoom(string roomId)
    {
        if (CanMove)
            CurrentRoomId = roomId;
    }

    /// <summary>
    /// Get the total damage including strength modifier and equipped weapon.
    /// </summary>
    public int GetTotalDamage(Item? equippedWeapon = null)
    {
        // Base damage from strength (10 = 0 bonus, each point is +0.5 damage)
        int baseDamage = 5 + (Strength - 10) / 2;
        baseDamage = Math.Max(1, baseDamage); // Minimum 1 damage

        if (equippedWeapon != null && equippedWeapon.DamageBonus > 0)
            return baseDamage + equippedWeapon.DamageBonus;

        return baseDamage;
    }

    /// <summary>
    /// Get the total armor rating including equipped armor.
    /// </summary>
    public int GetTotalArmor(Dictionary<string, Item> equippedItems)
    {
        int totalArmor = Armor;

        foreach (var item in equippedItems.Values)
        {
            if (item.IsEquippable && item.ArmorBonus > 0)
                totalArmor += item.ArmorBonus;
        }

        return totalArmor;
    }

    /// <summary>
    /// Get the dodge/critical chance modifier from agility.
    /// </summary>
    public int GetAgilityModifier()
    {
        // Each point of agility above 10 gives 1% critical chance and 0.5% dodge
        return (Agility - 10);
    }

    /// <summary>
    /// Equip an item in the specified slot.
    /// </summary>
    public bool EquipItem(Item item, string slot)
    {
        if (!item.IsEquippable || item.EquipmentSlot != slot)
            return false;

        EquipmentSlots[slot] = item.Id;
        return true;
    }

    /// <summary>
    /// Unequip an item from the specified slot.
    /// </summary>
    public Item? UnequipItem(string slot)
    {
        if (EquipmentSlots.ContainsKey(slot) && EquipmentSlots[slot] != null)
        {
            var itemId = EquipmentSlots[slot];
            EquipmentSlots[slot] = null;
            return itemId != null ? new Item { Id = itemId } : null;
        }

        return null;
    }
}

/// <summary>
/// Represents a single message exchange in an NPC's conversation history.
/// </summary>
public class ConversationEntry
{
    public string Role { get; set; } = "user"; // "user", "assistant", or "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum CharacterAlignment
{
    Good,
    Neutral,
    Evil
}

public enum NPCRole
{
    CommonPerson,        // Tavern goer, villager
    Merchant,           // Sells items
    Guard,              // Protects a location
    Warrior,            // Combat-focused
    Mage,               // Magic user
    Healer,             // Provides healing
    Scholar,            // Provides information
    Questgiver,         // Offers quests
    Boss,               // Major antagonist
    Ally,               // Joins the party
    Neutral,            // No particular role
    Companion           // Already in party
}
