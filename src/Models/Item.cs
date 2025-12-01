namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents an item in the game world with comprehensive stats and properties.
/// Items can be weapons, armor, keys, teleportation objects, consumables, or quest items.
/// </summary>
public class Item
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public int Weight { get; set; }
    public int Value { get; set; } // Base currency worth (legacy, use Pricing for full control)

    /// <summary>
    /// Detailed pricing information for buying and selling.
    /// If null, uses Value field as base price with default multipliers.
    /// </summary>
    public ItemPricing? Pricing { get; set; }

    /// <summary>
    /// Gets the buy price for this item.
    /// </summary>
    public long GetBuyPrice()
    {
        if (Pricing != null)
            return Pricing.GetBuyPrice();
        return Value; // Default: buy at full value
    }

    /// <summary>
    /// Gets the sell price for this item.
    /// </summary>
    public long GetSellPrice()
    {
        if (Pricing != null)
            return Pricing.GetSellPrice();
        return Value / 2; // Default: sell at half value
    }

    /// <summary>
    /// Whether this item can be bought from merchants.
    /// </summary>
    public bool CanBeBought => Pricing?.CanBuy ?? true;

    /// <summary>
    /// Whether this item can be sold to merchants.
    /// </summary>
    public bool CanBeSold => Pricing?.CanSell ?? (Type != ItemType.QuestItem);

    // Combat stats
    public int DamageBonus { get; set; }           // Weapon damage
    public int ArmorBonus { get; set; }            // Armor protection
    public int CriticalChance { get; set; }        // 0-100 percentage

    // Special item properties
    public ItemRarity Rarity { get; set; } = ItemRarity.Common;
    public bool IsUnique { get; set; }             // One of a kind
    public bool IsEquippable { get; set; }         // Can be equipped
    public string? EquipmentSlot { get; set; }     // "head", "chest", "hands", "legs", "feet", "main_hand", "off_hand"

    // Key/Lock system
    public string? UnlocksId { get; set; }         // What this key unlocks (exit ID, door ID, etc.)
    public bool IsKey { get; set; }
    public KeyType KeyType { get; set; } = KeyType.Mechanical;

    // Teleportation system
    public bool IsTeleportation { get; set; }
    public string? TeleportDestinationRoomId { get; set; }
    public string? TeleportDescription { get; set; }

    // Special properties
    public bool IsConsumable { get; set; }
    public int? ConsumableUsesRemaining { get; set; }
    public Dictionary<string, int> ConsumableEffects { get; set; } = new(); // e.g., { "heal": 50, "mana": 20 }

    // Style/theme for the game world
    public string? Theme { get; set; }             // "fantasy", "sci-fi", "steampunk", etc.

    // Crafting material properties
    /// <summary>
    /// Category of material for crafting (ore, herb, wood, etc.).
    /// Use MaterialCategories constants.
    /// </summary>
    public string? MaterialCategory { get; set; }

    /// <summary>
    /// Difficulty to gather this item (1-100). Higher = harder to find.
    /// </summary>
    public int GatherDifficulty { get; set; } = 50;

    /// <summary>
    /// Biomes where this item can be found naturally.
    /// Use Biomes constants.
    /// </summary>
    public List<string>? FoundInBiomes { get; set; }

    /// <summary>
    /// Whether this item is primarily for selling (treasure).
    /// </summary>
    public bool IsTreasure { get; set; } = false;

    /// <summary>
    /// Whether this is a junk/flavor item with low value.
    /// </summary>
    public bool IsJunk { get; set; } = false;

    // General properties
    public bool Stackable { get; set; } = false;
    public bool CanBeTaken { get; set; } = true;
    public bool Cursed { get; set; } = false;
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}

public enum ItemType
{
    Weapon,
    Armor,
    Key,
    Teleportation,
    Consumable,
    QuestItem,
    CraftingMaterial,  // Ore, herbs, wood, etc.
    Treasure,          // Gems, gold bars - valuable for selling
    Junk,              // Low-value flavor items
    Tool,              // Pickaxe, herbalist kit, etc.
    Miscellaneous
}

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum KeyType
{
    Mechanical,      // Simple lock and key
    Magical,         // Magic rune, spell component
    Technological,   // Keycard, biometric scanner
    Biological,      // DNA lock, blood oath
    Puzzle           // Requires solving a puzzle
}
