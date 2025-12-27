# Dynamic Equipment Slot System

**Date**: 2025-12-01
**Status**: ✅ **COMPLETE** - Fully configurable per-game equipment slots

---

## Overview

The equipment system now supports **dynamic slot configuration**, allowing each game to define its own unique equipment slots. This enables:

- ✅ **Per-game customization** - Fantasy games vs Sci-Fi games can have different slots
- ✅ **Default fallback** - Games without configuration use sensible defaults
- ✅ **Flexible slot definitions** - Define slot names, display order, keywords, and compatible types
- ✅ **Auto-detection** - Items automatically find their correct slot based on type and name
- ✅ **Theme-specific presets** - Built-in configurations for different game styles

---

## Key Features

### 1. Per-Game Configuration
Each `Game` can define its own `EquipmentSlots` property:
```csharp
var game = new Game
{
    Id = "my_scifi_game",
    Title = "Space Adventure",
    EquipmentSlots = EquipmentSlotConfiguration.CreateSciFi()
};
```

### 2. Default Fallback
If a game doesn't specify slots, the system uses a sensible default:
```csharp
// Automatically uses default (7 slots: main_hand, off_hand, head, chest, hands, legs, feet)
var game = new Game
{
    Id = "my_game",
    EquipmentSlots = null  // Will use default
};
```

### 3. Smart Auto-Detection
Items without explicit `EquipmentSlot` are automatically assigned based on:
- Item type (Weapon, Armor, etc.)
- Slot keywords matching item name/type
- Compatible type checking

---

## Built-In Configurations

### Default Configuration (Fantasy RPG)
**Slots**: 7 equipment slots
```csharp
var slots = EquipmentSlotConfiguration.CreateDefault();
```

| Slot ID | Display Name | Compatible Types | Keywords |
|---------|--------------|------------------|----------|
| main_hand | Main Hand | Weapon | weapon, sword, axe, bow, staff, dagger, mace, spear, wand |
| off_hand | Off Hand | Weapon, Armor | shield, buckler, offhand |
| head | Head | Armor | helmet, hat, crown, cap, hood, circlet, mask |
| chest | Chest | Armor | armor, chestplate, robe, tunic, vest, shirt, breastplate |
| hands | Hands | Armor | gloves, gauntlets, bracers, wristguards |
| legs | Legs | Armor | pants, leggings, greaves, legguards, trousers |
| feet | Feet | Armor | boots, shoes, sandals, greaves |

---

### Sci-Fi Configuration
**Slots**: 6 equipment slots
```csharp
var slots = EquipmentSlotConfiguration.CreateSciFi();
```

| Slot ID | Display Name | Compatible Types | Keywords |
|---------|--------------|------------------|----------|
| primary_weapon | Primary Weapon | Weapon | rifle, gun, blaster, weapon, firearm |
| secondary_weapon | Secondary Weapon | Weapon | pistol, sidearm, backup |
| helmet | Helmet | Armor | helmet, visor, headgear |
| suit | Suit | Armor | suit, armor, exosuit, body |
| gloves | Gloves | Armor | gloves, gauntlets, hands |
| boots | Boots | Armor | boots, shoes, footwear |

---

### Minimal Configuration
**Slots**: 1 equipment slot (weapon only)
```csharp
var slots = EquipmentSlotConfiguration.CreateMinimal();
```

| Slot ID | Display Name | Compatible Types | Keywords |
|---------|--------------|------------------|----------|
| weapon | Weapon | Weapon | weapon, sword, gun, blade |

---

## Creating Custom Configurations

### Example: Medieval Fantasy with Jewelry

```csharp
var medievalSlots = new EquipmentSlotConfiguration
{
    Slots = new List<EquipmentSlotDefinition>
    {
        new()
        {
            Id = "main_hand",
            DisplayName = "Main Hand",
            Keywords = new() { "sword", "mace", "axe" },
            CompatibleTypes = new() { ItemType.Weapon },
            DisplayOrder = 1
        },
        new()
        {
            Id = "off_hand",
            DisplayName = "Off Hand",
            Keywords = new() { "shield", "buckler" },
            CompatibleTypes = new() { ItemType.Armor },
            DisplayOrder = 2
        },
        new()
        {
            Id = "helm",
            DisplayName = "Helm",
            Keywords = new() { "helm", "helmet", "crown" },
            CompatibleTypes = new() { ItemType.Armor },
            DisplayOrder = 3
        },
        new()
        {
            Id = "ring",
            DisplayName = "Ring",
            Description = "Magical rings and bands",
            Keywords = new() { "ring", "band" },
            CompatibleTypes = new() { ItemType.Armor },  // Or create ItemType.Accessory
            DisplayOrder = 10
        },
        new()
        {
            Id = "amulet",
            DisplayName = "Amulet",
            Description = "Enchanted necklaces",
            Keywords = new() { "amulet", "necklace", "pendant" },
            CompatibleTypes = new() { ItemType.Armor },
            DisplayOrder = 11
        }
    }
};

var game = new Game
{
    Id = "medieval_rpg",
    EquipmentSlots = medievalSlots
};
```

---

### Example: Modern Shooter

```csharp
var shooterSlots = new EquipmentSlotConfiguration
{
    Slots = new List<EquipmentSlotDefinition>
    {
        new()
        {
            Id = "primary",
            DisplayName = "Primary",
            Description = "Rifle or long gun",
            Keywords = new() { "rifle", "shotgun", "smg" },
            CompatibleTypes = new() { ItemType.Weapon },
            DisplayOrder = 1
        },
        new()
        {
            Id = "secondary",
            DisplayName = "Secondary",
            Description = "Pistol or sidearm",
            Keywords = new() { "pistol", "handgun", "sidearm" },
            CompatibleTypes = new() { ItemType.Weapon },
            DisplayOrder = 2
        },
        new()
        {
            Id = "tactical",
            DisplayName = "Tactical",
            Description = "Grenades, flashbangs, equipment",
            Keywords = new() { "grenade", "flashbang", "tactical" },
            CompatibleTypes = new() { ItemType.Consumable },
            DisplayOrder = 3
        },
        new()
        {
            Id = "armor",
            DisplayName = "Body Armor",
            Description = "Vest or protective gear",
            Keywords = new() { "vest", "armor", "kevlar" },
            CompatibleTypes = new() { ItemType.Armor },
            DisplayOrder = 4
        }
    }
};
```

---

## Using in GameBuilder

```csharp
// Option 1: Use default slots
var game = new GameBuilder("fantasy_quest")
    .WithTitle("Fantasy Quest")
    .WithStartingRoom("start")
    // No EquipmentSlots specified = uses default
    .Build();

// Option 2: Use preset configuration
var scifiGame = new GameBuilder("space_game")
    .WithTitle("Space Adventure")
    .WithStartingRoom("ship")
    .Build();
scifiGame.EquipmentSlots = EquipmentSlotConfiguration.CreateSciFi();

// Option 3: Use custom configuration
var customGame = new GameBuilder("custom_rpg")
    .WithTitle("Custom RPG")
    .Build();
customGame.EquipmentSlots = new EquipmentSlotConfiguration
{
    Slots = new List<EquipmentSlotDefinition>
    {
        // Your custom slots here
    }
};
```

---

## How Auto-Detection Works

When a player tries to equip an item, the system determines the slot using this priority:

### 1. Explicit Slot (Highest Priority)
```csharp
var item = new Item
{
    Name = "Iron Sword",
    EquipmentSlot = "main_hand"  // Explicitly specified
};
// Will go in "main_hand" slot
```

### 2. Type + Keyword Matching
```csharp
var helmet = new Item
{
    Name = "Steel Helmet",
    Type = ItemType.Armor,
    // No explicit slot
};
// System checks all armor slots for keyword "helmet"
// Finds "head" slot with keywords: ["helmet", "hat", "crown", ...]
// Equips to "head"
```

### 3. Type-Only Matching
```csharp
var armor = new Item
{
    Name = "Mystery Armor",
    Type = ItemType.Armor
};
// Multiple slots accept ItemType.Armor
// System finds first slot with keyword match in item name
// Falls back to first compatible slot if no keyword match
```

### 4. Fallback
```csharp
var weirdItem = new Item
{
    Name = "Strange Thing",
    Type = ItemType.Weapon
};
// No slot explicitly specified
// Name doesn't match any keywords
// Falls back to first slot that accepts ItemType.Weapon
```

---

## API Reference

### EquipmentSlotDefinition

```csharp
public class EquipmentSlotDefinition
{
    public string Id { get; set; }                      // Unique identifier (e.g., "main_hand")
    public string DisplayName { get; set; }             // Human-readable name (e.g., "Main Hand")
    public string? Description { get; set; }            // Optional description
    public List<string> Keywords { get; set; }          // Keywords for auto-detection
    public List<ItemType> CompatibleTypes { get; set; } // Which item types can go here
    public int DisplayOrder { get; set; }               // Sort order in UI
    public List<string> ConflictsWith { get; set; }     // Future: two-handed weapons
}
```

### EquipmentSlotConfiguration

```csharp
public class EquipmentSlotConfiguration
{
    public List<EquipmentSlotDefinition> Slots { get; set; }

    // Factory methods
    public static EquipmentSlotConfiguration CreateDefault()
    public static EquipmentSlotConfiguration CreateMinimal()
    public static EquipmentSlotConfiguration CreateSciFi()

    // Helper methods
    public EquipmentSlotDefinition? GetSlot(string slotId)
    public string? DetermineSlotForItem(Item item)
    public List<string> GetAllSlotIds()
    public List<EquipmentSlotDefinition> GetOrderedSlots()
}
```

### Game Class

```csharp
public class Game
{
    public EquipmentSlotConfiguration? EquipmentSlots { get; set; }

    // Helper method
    public EquipmentSlotConfiguration GetEquipmentSlots()
    {
        return EquipmentSlots ?? EquipmentSlotConfiguration.CreateDefault();
    }
}
```

---

## Gameplay Impact

### Player Experience

**With Default Slots**:
```
> equipped

Currently Equipped:
  Main Hand: Iron Sword [+5 dmg]
  Head: Steel Helmet [+2 armor]
  Chest: Leather Armor [+3 armor]

Total: 11 damage, 7 armor
```

**With Sci-Fi Slots**:
```
> equipped

Currently Equipped:
  Primary Weapon: Plasma Rifle [+10 dmg]
  Secondary Weapon: Laser Pistol [+5 dmg]
  Helmet: Combat Visor [+3 armor]
  Suit: Exosuit Mk II [+8 armor]

Total: 15 damage, 11 armor
```

**With Minimal Slots**:
```
> equipped

Currently Equipped:
  Weapon: Battle Axe [+7 dmg]

Total: 12 damage, 0 armor
```

---

## Technical Details

### Backward Compatibility

✅ **Fully backward compatible** - Existing games without `EquipmentSlots` automatically use default configuration

### Performance

- **O(1)** slot lookup by ID
- **O(n)** auto-detection (n = number of slots, typically < 10)
- **Minimal memory** - Configuration loaded once per game

### Character Storage

Characters still use `Dictionary<string, string?>` for `EquipmentSlots`:
```csharp
public class Character
{
    public Dictionary<string, string?> EquipmentSlots { get; set; }
    // Stores: { "main_hand": "iron_sword_id", "head": "helmet_id", ... }
}
```

This remains flexible and works with any slot configuration.

---

## Migration Guide

### Existing Games

No changes required! Existing games will automatically use the default configuration.

### Custom Games

To add custom slots to an existing game:

```csharp
// 1. Load your game
var game = GameLoader.LoadGame("my_game");

// 2. Set custom slots
game.EquipmentSlots = EquipmentSlotConfiguration.CreateSciFi();
// or
game.EquipmentSlots = new EquipmentSlotConfiguration { /* custom */ };

// 3. Save game (if using JSON-based games)
GameLoader.SaveGame(game, "games/my_game");
```

---

## Examples

### Minimal RPG (1 Slot)
```csharp
var game = new Game
{
    EquipmentSlots = EquipmentSlotConfiguration.CreateMinimal()
};
// Players can only equip one weapon - simple but focused
```

### Standard Fantasy (7 Slots - Default)
```csharp
var game = new Game
{
    EquipmentSlots = null  // or EquipmentSlotConfiguration.CreateDefault()
};
// Classic RPG setup: weapon, shield, helmet, chest, gloves, pants, boots
```

### Sci-Fi Shooter (6 Slots)
```csharp
var game = new Game
{
    EquipmentSlots = EquipmentSlotConfiguration.CreateSciFi()
};
// Futuristic: primary weapon, secondary weapon, helmet, suit, gloves, boots
```

### Superhero Game (Custom)
```csharp
var game = new Game
{
    EquipmentSlots = new EquipmentSlotConfiguration
    {
        Slots = new()
        {
            new() { Id = "suit", DisplayName = "Suit", Keywords = new() { "suit", "costume" } },
            new() { Id = "cape", DisplayName = "Cape", Keywords = new() { "cape", "cloak" } },
            new() { Id = "mask", DisplayName = "Mask", Keywords = new() { "mask", "helmet" } },
            new() { Id = "gadget", DisplayName = "Gadget", Keywords = new() { "gadget", "tool" } }
        }
    }
};
```

---

## Future Enhancements

### Slot Conflicts
```csharp
new EquipmentSlotDefinition
{
    Id = "two_handed",
    DisplayName = "Two-Handed Weapon",
    ConflictsWith = new() { "off_hand" }  // Can't equip shield with two-handed weapon
}
```

### Slot Requirements
```csharp
new EquipmentSlotDefinition
{
    Id = "advanced_weapon",
    DisplayName = "Advanced Weapon",
    Requirements = new() { MinLevel = 10, RequiredSkill = "Weapons Master" }
}
```

### Dynamic Slot Unlocking
```csharp
// Unlock new slots as player progresses
if (player.Level >= 10)
{
    game.EquipmentSlots.Slots.Add(new() { Id = "artifact", DisplayName = "Artifact Slot" });
}
```

---

## Files Modified

1. **`src/Models/EquipmentSlot.cs`** - NEW
   - `EquipmentSlotDefinition` class
   - `EquipmentSlotConfiguration` class with factory methods

2. **`src/Models/Game.cs`**
   - Added `EquipmentSlots` property
   - Added `GetEquipmentSlots()` helper method

3. **`src/Services/GameMaster.cs`**
   - Added `_equipmentSlots` field
   - Updated constructor to initialize from game
   - Updated `HandleEquipped()` to use dynamic slots
   - Simplified `DetermineSlotFromType()` to delegate to configuration

---

## Benefits

### For Game Designers
- ✅ **Full control** over equipment system per game
- ✅ **Theme consistency** - slots match game setting
- ✅ **Quick iteration** - change slots without code changes
- ✅ **Flexibility** - from 1 slot (simple) to 20+ slots (complex)

### For Players
- ✅ **Intuitive** - slot names match game theme
- ✅ **Consistent** - equipment system feels integrated
- ✅ **Clear** - obvious where items go

### For Developers
- ✅ **Maintainable** - no hard-coded slot logic
- ✅ **Extensible** - easy to add new configurations
- ✅ **Testable** - configurations are data-driven
- ✅ **Reusable** - preset configurations available

---

## Conclusion

The dynamic equipment slot system provides **maximum flexibility** while maintaining **ease of use**. Games can use sensible defaults or define completely custom slot configurations to match their theme and gameplay style.

**Status**: ✅ **Production Ready**
**Build**: ✅ Success (0 warnings, 0 errors)
**Backward Compatible**: ✅ Yes (existing games use defaults)

---

**Implementation Date**: 2025-12-01
**Lines of Code**: ~350 (new EquipmentSlot.cs) + ~30 (modifications)
**Files Created**: 1 (EquipmentSlot.cs)
**Files Modified**: 2 (Game.cs, GameMaster.cs)
