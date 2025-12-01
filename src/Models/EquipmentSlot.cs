namespace CSharpRPGBackend.Core;

/// <summary>
/// Defines a single equipment slot with its properties and compatible item types.
/// </summary>
public class EquipmentSlotDefinition
{
    /// <summary>
    /// Unique identifier for this slot (e.g., "main_hand", "head", "chest")
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g., "Main Hand", "Head", "Chest")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what can be equipped here
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Keywords that help identify if an item belongs in this slot.
    /// Used by auto-detection system when item doesn't specify a slot.
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Compatible item types that can be equipped in this slot
    /// </summary>
    public List<ItemType> CompatibleTypes { get; set; } = new();

    /// <summary>
    /// Display order in equipment screens (lower numbers first)
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this slot conflicts with other slots (e.g., two-handed weapons block off-hand)
    /// </summary>
    public List<string> ConflictsWith { get; set; } = new();
}

/// <summary>
/// Configuration for all equipment slots in a game.
/// Each game can define its own slot system.
/// </summary>
public class EquipmentSlotConfiguration
{
    /// <summary>
    /// All available equipment slots for this game
    /// </summary>
    public List<EquipmentSlotDefinition> Slots { get; set; } = new();

    /// <summary>
    /// Creates the default equipment slot configuration used by most RPGs.
    /// Includes: Main Hand, Off Hand, Head, Chest, Hands, Legs, Feet
    /// </summary>
    public static EquipmentSlotConfiguration CreateDefault()
    {
        return new EquipmentSlotConfiguration
        {
            Slots = new List<EquipmentSlotDefinition>
            {
                new()
                {
                    Id = "main_hand",
                    DisplayName = "Main Hand",
                    Description = "Primary weapon or tool",
                    Keywords = new() { "weapon", "sword", "axe", "bow", "staff", "dagger", "mace", "spear", "wand" },
                    CompatibleTypes = new() { ItemType.Weapon },
                    DisplayOrder = 1
                },
                new()
                {
                    Id = "off_hand",
                    DisplayName = "Off Hand",
                    Description = "Secondary weapon, shield, or tool",
                    Keywords = new() { "shield", "buckler", "offhand" },
                    CompatibleTypes = new() { ItemType.Weapon, ItemType.Armor },
                    DisplayOrder = 2
                },
                new()
                {
                    Id = "head",
                    DisplayName = "Head",
                    Description = "Helmet, hat, crown, or headgear",
                    Keywords = new() { "helmet", "hat", "crown", "cap", "hood", "circlet", "mask" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 3
                },
                new()
                {
                    Id = "chest",
                    DisplayName = "Chest",
                    Description = "Armor, robe, tunic, or clothing",
                    Keywords = new() { "armor", "chestplate", "robe", "tunic", "vest", "shirt", "breastplate" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 4
                },
                new()
                {
                    Id = "hands",
                    DisplayName = "Hands",
                    Description = "Gloves or gauntlets",
                    Keywords = new() { "gloves", "gauntlets", "bracers", "wristguards" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 5
                },
                new()
                {
                    Id = "legs",
                    DisplayName = "Legs",
                    Description = "Pants, leggings, or leg armor",
                    Keywords = new() { "pants", "leggings", "greaves", "legguards", "trousers" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 6
                },
                new()
                {
                    Id = "feet",
                    DisplayName = "Feet",
                    Description = "Boots, shoes, or footwear",
                    Keywords = new() { "boots", "shoes", "sandals", "greaves" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 7
                }
            }
        };
    }

    /// <summary>
    /// Creates a minimal slot configuration (weapon only).
    /// Useful for simple games or testing.
    /// </summary>
    public static EquipmentSlotConfiguration CreateMinimal()
    {
        return new EquipmentSlotConfiguration
        {
            Slots = new List<EquipmentSlotDefinition>
            {
                new()
                {
                    Id = "weapon",
                    DisplayName = "Weapon",
                    Keywords = new() { "weapon", "sword", "gun", "blade" },
                    CompatibleTypes = new() { ItemType.Weapon },
                    DisplayOrder = 1
                }
            }
        };
    }

    /// <summary>
    /// Creates a sci-fi themed slot configuration.
    /// Includes: Primary Weapon, Secondary Weapon, Helmet, Suit, Gloves, Boots
    /// </summary>
    public static EquipmentSlotConfiguration CreateSciFi()
    {
        return new EquipmentSlotConfiguration
        {
            Slots = new List<EquipmentSlotDefinition>
            {
                new()
                {
                    Id = "primary_weapon",
                    DisplayName = "Primary Weapon",
                    Description = "Main firearm or energy weapon",
                    Keywords = new() { "rifle", "gun", "blaster", "weapon", "firearm" },
                    CompatibleTypes = new() { ItemType.Weapon },
                    DisplayOrder = 1
                },
                new()
                {
                    Id = "secondary_weapon",
                    DisplayName = "Secondary Weapon",
                    Description = "Sidearm or backup weapon",
                    Keywords = new() { "pistol", "sidearm", "backup" },
                    CompatibleTypes = new() { ItemType.Weapon },
                    DisplayOrder = 2
                },
                new()
                {
                    Id = "helmet",
                    DisplayName = "Helmet",
                    Description = "Protective headgear with HUD",
                    Keywords = new() { "helmet", "visor", "headgear" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 3
                },
                new()
                {
                    Id = "suit",
                    DisplayName = "Suit",
                    Description = "Full body armor or spacesuit",
                    Keywords = new() { "suit", "armor", "exosuit", "body" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 4
                },
                new()
                {
                    Id = "gloves",
                    DisplayName = "Gloves",
                    Description = "Hand protection and grip enhancement",
                    Keywords = new() { "gloves", "gauntlets", "hands" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 5
                },
                new()
                {
                    Id = "boots",
                    DisplayName = "Boots",
                    Description = "Magnetic boots or mobility enhancers",
                    Keywords = new() { "boots", "shoes", "footwear" },
                    CompatibleTypes = new() { ItemType.Armor },
                    DisplayOrder = 6
                }
            }
        };
    }

    /// <summary>
    /// Gets a slot definition by its ID.
    /// </summary>
    public EquipmentSlotDefinition? GetSlot(string slotId)
    {
        return Slots.FirstOrDefault(s => s.Id.Equals(slotId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines the best slot for an item based on its properties.
    /// Returns null if no suitable slot is found.
    /// </summary>
    public string? DetermineSlotForItem(Item item)
    {
        // First check if item explicitly specifies a slot
        if (!string.IsNullOrWhiteSpace(item.EquipmentSlot))
        {
            var explicitSlot = GetSlot(item.EquipmentSlot);
            if (explicitSlot != null)
                return explicitSlot.Id;
        }

        // Check item type
        var typeMatches = Slots.Where(s => s.CompatibleTypes.Contains(item.Type)).ToList();

        if (typeMatches.Count == 1)
            return typeMatches[0].Id;

        // If multiple slots match the type, use keywords to narrow down
        if (typeMatches.Count > 1)
        {
            var itemNameLower = item.Name?.ToLower() ?? "";
            var itemTypeLower = item.Type.ToString().ToLower();

            foreach (var slot in typeMatches)
            {
                // Check if any slot keyword appears in item name or type
                if (slot.Keywords.Any(k => itemNameLower.Contains(k) || itemTypeLower.Contains(k)))
                {
                    return slot.Id;
                }
            }

            // If still no match, return first compatible slot
            return typeMatches[0].Id;
        }

        // Fallback: search all slots for keyword matches
        var itemName = item.Name?.ToLower() ?? "";
        var itemType = item.Type.ToString().ToLower();

        foreach (var slot in Slots.OrderBy(s => s.DisplayOrder))
        {
            if (slot.Keywords.Any(k => itemName.Contains(k) || itemType.Contains(k)))
            {
                return slot.Id;
            }
        }

        return null; // No suitable slot found
    }

    /// <summary>
    /// Gets all slot IDs in display order.
    /// </summary>
    public List<string> GetAllSlotIds()
    {
        return Slots.OrderBy(s => s.DisplayOrder).Select(s => s.Id).ToList();
    }

    /// <summary>
    /// Gets all slots ordered by display order.
    /// </summary>
    public List<EquipmentSlotDefinition> GetOrderedSlots()
    {
        return Slots.OrderBy(s => s.DisplayOrder).ToList();
    }
}
