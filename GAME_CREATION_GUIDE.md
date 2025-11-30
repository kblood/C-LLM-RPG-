# Game Creation Guide

## Overview

The C# RPG Backend now includes a complete game definition system that allows you to create fully-fledged games with:

- **Game metadata** (title, story, objectives, style)
- **Rooms** with detailed descriptions and interconnected exits
- **NPCs** with locations, movement patterns, personalities, and recruitment options
- **Items** with comprehensive stats (weapons, armor, keys, teleportation devices, consumables)
- **Quests** with objectives and rewards
- **Game settings** (combat system, permadeath, magic, technology, etc.)

## Creating a Game: Step-by-Step

### 1. Create a Game Class

Create a new file: `src/Games/MyGame.cs`

```csharp
using CSharpRPGBackend.Core;
using CSharpRPGBackend.Utils;

namespace CSharpRPGBackend.Games;

public static class MyGame
{
    public static Game Create()
    {
        var gameBuilder = new GameBuilder("my_game_id")
            .WithTitle("My Game Title")
            .WithStyle(GameStyle.Fantasy)
            .WithDescription("A description of the game")
            .WithStory("The story introduction the player sees")
            .WithObjective("What the player is trying to achieve")
            .WithEstimatedPlayTime(60);

        // Build and return
        return gameBuilder.Build();
    }
}
```

### 2. Add Rooms

```csharp
var startingRoom = new RoomBuilder("start")
    .WithName("Starting Location")
    .WithDescription("A vivid description of the room...")
    .AddExit("North", "forest", "The forest path leads north")
    .AddExit("East", "town", "Town is to the east")
    .AddNPCs("guide", "merchant")
    .WithMetadata("danger_level", 0)
    .WithMetadata("lighting", "bright")
    .Build();

var forest = new RoomBuilder("forest")
    .WithName("Dark Forest")
    .WithDescription("Ancient trees block out the sky...")
    .AddExit("South", "start")
    .AddExit("Deep", "cave")
    .AddNPCs("ranger")
    .WithMetadata("danger_level", 2)
    .Build();

gameBuilder
    .AddRoom(startingRoom)
    .AddRoom(forest);
```

**Room Design Best Practices:**
- Vivid descriptions set the atmosphere
- Clear exit names guide exploration
- Metadata helps with gameplay logic
- NPCs bring rooms alive
- Interconnected exits create a navigable world

### 3. Add NPCs

NPCs define your game's cast of characters.

```csharp
var shopkeeper = new NpcBuilder("shopkeeper", "Merchant Aldous")
    .WithHealth(50, 50)
    .WithLevel(1)
    .WithAlignment(CharacterAlignment.Good)
    .WithPersonalityPrompt(@"You are Aldous, a shopkeeper. You're friendly and enjoy trading.
        You know the town well and can provide information about local happenings.")
    .Build();

// Set NPC location and behavior
shopkeeper.CurrentRoomId = "town";        // Where NPC starts
shopkeeper.HomeRoomId = "town";          // Where NPC normally is
shopkeeper.Role = NPCRole.Merchant;      // NPC's function
shopkeeper.CanJoinParty = true;          // Can player recruit?
shopkeeper.CanMove = true;               // Does NPC move around?
shopkeeper.PatrolRoomIds = new() { "town", "market" };  // Where it patrols
shopkeeper.Description = "A portly merchant with a friendly smile";
shopkeeper.Backstory = "Aldous came to town 20 years ago...";

gameBuilder.AddNPC(shopkeeper);
```

**NPC Roles (Choose One):**
- `CommonPerson` - Tavern goer, villager
- `Merchant` - Sells items
- `Guard` - Protects locations
- `Warrior` - Combat-focused
- `Mage` - Magic user
- `Healer` - Provides healing
- `Scholar` - Provides information
- `Questgiver` - Offers quests
- `Boss` - Major antagonist
- `Ally` - Joins the party
- `Companion` - Already in party

**NPC Location Options:**
```csharp
npc.CurrentRoomId = "town";              // Current location (changes)
npc.HomeRoomId = "tavern";              // Default location
npc.PatrolRoomIds = new() { "town", "market", "square" };  // Regular patrol
npc.PatrolIntervalTurns = 3;            // Move every N turns
npc.CanMove = true;                     // Can NPC move?
```

### 4. Add Items

Items are the backbone of inventory and gameplay.

#### Weapons

```csharp
var sword = new ItemBuilder("iron_sword")
    .WithName("Iron Sword")
    .WithDescription("A well-crafted iron blade")
    .AsWeapon(damage: 10, criticalChance: 15)
    .WithWeight(5)
    .WithValue(50)
    .WithTheme("fantasy")
    .Build();
```

#### Armor

```csharp
var chestplate = new ItemBuilder("steel_plate")
    .WithName("Steel Plate Armor")
    .WithDescription("Heavy protective armor")
    .AsArmor(armorBonus: 8, slot: "chest")
    .WithWeight(10)
    .WithValue(100)
    .WithRarity(ItemRarity.Uncommon)
    .Build();
```

#### Keys (Mechanical, Magical, Technological)

```csharp
// Mechanical key (classic lock and key)
var key = new ItemBuilder("wooden_door_key")
    .WithName("Wooden Door Key")
    .AsKey("wooden_door", KeyType.Mechanical)
    .Build();

// Magical key (for magic locks)
var magicKey = new ItemBuilder("rune_key")
    .WithName("Mystic Rune Key")
    .WithDescription("A key glowing with magical energy")
    .AsKey("magic_barrier", KeyType.Magical)
    .WithRarity(ItemRarity.Rare)
    .Build();

// Technological key (keycards, codes)
var keycard = new ItemBuilder("access_card")
    .WithName("Security Access Card")
    .AsKey("vault_door", KeyType.Technological)
    .Build();
```

**Key Types:**
- `Mechanical` - Simple locks, door keys
- `Magical` - Magic runes, spell components
- `Technological` - Keycards, biometric scanners
- `Biological` - DNA locks, blood oaths
- `Puzzle` - Requires solving a puzzle

#### Teleportation Devices

```csharp
var scroll = new ItemBuilder("recall_scroll")
    .WithName("Recall Scroll")
    .WithDescription("A magical scroll that returns you home")
    .AsTeleportation("home", "The scroll glows. You're transported away!")
    .WithWeight(0)
    .WithValue(100)
    .WithRarity(ItemRarity.Rare)
    .Build();
```

#### Consumables (Potions, Food, etc.)

```csharp
var healPotion = new ItemBuilder("health_potion")
    .WithName("Health Potion")
    .WithDescription("Restores vitality")
    .AsConsumable(uses: 1)
    .WithConsumableEffect("heal", 50)
    .WithWeight(1)
    .WithValue(25)
    .Build();

// Consumable with multiple effects
var mightPotion = new ItemBuilder("might_potion")
    .WithName("Potion of Might")
    .AsConsumable(uses: 1)
    .WithConsumableEffect("damage_bonus", 10)
    .WithConsumableEffect("duration", 5)
    .Build();
```

#### Quest Items

```csharp
var artifact = new ItemBuilder("sacred_relic")
    .WithName("Sacred Relic")
    .WithDescription("An artifact needed to complete the quest")
    .AsQuestItem()
    .WithValue(1000)
    .WithRarity(ItemRarity.Legendary)
    .Build();
```

**Item Properties:**
```csharp
.WithName(string)                    // Item name
.WithDescription(string)             // Full description
.AsWeapon(damage, criticalChance)   // Weapon stats
.AsArmor(armorBonus, slot)          // Armor stats
.AsKey(unlocksId, keyType)          // Key that unlocks something
.AsTeleportation(roomId, desc)      // Teleportation item
.AsConsumable(uses)                 // Consumable with uses
.AsQuestItem()                       // Quest item
.WithWeight(int)                     // Encumbrance
.WithValue(int)                      // Gold value
.WithRarity(ItemRarity)              // Common/Uncommon/Rare/Epic/Legendary
.WithTheme(string)                   // "fantasy", "sci-fi", "steampunk"
.AsUnique()                          // One-of-a-kind
.AsCursed()                          // Cursed item
.WithCustomProperty(key, value)      // Custom data
```

### 5. Add Quests

```csharp
var mainQuest = new Quest
{
    Id = "dragon_slayer",
    Title = "Slay the Dragon",
    Description = "The dragon threatens the kingdom",
    GiverNpcId = "king",
    RewardExperience = 500,
    RewardGold = 300,
    Objectives = new()
    {
        "Reach the dragon's lair",
        "Defeat the dragon",
        "Return the dragon's heart to the king"
    }
};

gameBuilder.AddQuest(mainQuest);
```

### 6. Configure Game Settings

```csharp
gameBuilder
    .WithStyle(GameStyle.Fantasy)           // Game aesthetic
    .WithCombat(true)                       // Has combat?
    .WithInventory(true)                    // Has inventory?
    .WithMagic(true)                        // Has magic system?
    .WithTechnology(false)                  // Has technology?
    .WithNPCRecruitment(true)              // Can recruit NPCs?
    .WithPermadeath(false)                  // One-hit death?
    .WithPvP(false)                         // Player vs Player?
    .WithFreeRoam(true)                     // Can go anywhere?
    .AddWinConditionRoom("throne_room")    // Win by reaching room
    .WithStartingRoom("village_square");    // Starting location
```

## Complete Example: Creating a Mini Game

```csharp
using CSharpRPGBackend.Core;
using CSharpRPGBackend.Utils;

namespace CSharpRPGBackend.Games;

public static class MiniGame
{
    public static Game Create()
    {
        var gameBuilder = new GameBuilder("mini_adventure")
            .WithTitle("The Lost Amulet")
            .WithStyle(GameStyle.Fantasy)
            .WithStory("You must find the Lost Amulet hidden in the ancient ruins...")
            .WithObjective("Recover the amulet and return it to the temple")
            .WithPlayerDefaults(100, 1);

        // ========== ROOMS ==========
        var temple = new RoomBuilder("temple")
            .WithName("The Temple")
            .WithDescription("A sacred place. The elder stands here.")
            .AddExit("To Ruins", "ruins_entrance")
            .AddNPCs("elder")
            .Build();

        var ruinsEntrance = new RoomBuilder("ruins_entrance")
            .WithName("Ancient Ruins Entrance")
            .WithDescription("Crumbling stone walls. A dark passage ahead.")
            .AddExit("Back To Temple", "temple")
            .AddExit("Into The Ruins", "ruins_chamber")
            .AddNPCs("guard")
            .WithMetadata("danger_level", 1)
            .Build();

        var ruinsChamber = new RoomBuilder("ruins_chamber")
            .WithName("Ruins Chamber")
            .WithDescription("A large chamber. An amulet glows on a pedestal.")
            .AddExit("Back Out", "ruins_entrance")
            .WithMetadata("danger_level", 2)
            .Build();

        gameBuilder
            .AddRoom(temple)
            .AddRoom(ruinsEntrance)
            .AddRoom(ruinsChamber);

        // ========== NPCs ==========
        var elder = new NpcBuilder("elder", "The Elder")
            .WithHealth(80, 80)
            .WithLevel(2)
            .WithPersonalityPrompt("You are the temple elder. Wise and kind.")
            .Build();
        elder.CurrentRoomId = "temple";
        elder.Role = NPCRole.Questgiver;

        var guard = new NpcBuilder("guard", "Stone Guard")
            .WithHealth(100, 100)
            .WithLevel(2)
            .WithPersonalityPrompt("You guard the ruins. You're cautious but not hostile.")
            .Build();
        guard.CurrentRoomId = "ruins_entrance";
        guard.Role = NPCRole.Guard;

        gameBuilder.AddNPCs(elder, guard);

        // ========== ITEMS ==========
        var amulet = new ItemBuilder("lost_amulet")
            .WithName("The Lost Amulet")
            .WithDescription("An ancient amulet radiating power")
            .AsQuestItem()
            .WithValue(1000)
            .WithRarity(ItemRarity.Legendary)
            .Build();

        var sword = new ItemBuilder("bronze_sword")
            .WithName("Bronze Sword")
            .AsWeapon(damage: 8)
            .Build();

        gameBuilder.AddItems(amulet, sword);

        // ========== QUESTS ==========
        var quest = new Quest
        {
            Id = "find_amulet",
            Title = "The Lost Amulet",
            Description = "The Elder needs the Lost Amulet",
            GiverNpcId = "elder",
            RewardExperience = 100,
            RewardGold = 50,
            Objectives = new() { "Find the amulet", "Return to temple" }
        };

        gameBuilder.AddQuest(quest);
        gameBuilder.AddWinConditionRoom("temple");

        return gameBuilder
            .WithStartingRoom("temple")
            .Build();
    }
}
```

## Game Styles

Choose the style that fits your game's theme:

```csharp
GameStyle.Fantasy        // Swords, magic, dragons
GameStyle.SciFi         // Lasers, technology, aliens
GameStyle.Steampunk     // Steam-powered machinery
GameStyle.Horror        // Monsters, survival
GameStyle.Modern        // Contemporary setting
GameStyle.Western       // Cowboys, old west
GameStyle.Mystery       // Investigation, puzzles
GameStyle.Historical    // Based on real history
GameStyle.Custom        // User-defined
```

## Item Rarity System

Items can have different rarity levels affecting their value and uniqueness:

```csharp
ItemRarity.Common       // Standard items
ItemRarity.Uncommon     // Better quality
ItemRarity.Rare         // Special items
ItemRarity.Epic         // Powerful items
ItemRarity.Legendary    // Unique, game-changing items
```

## Using Your Game

Once created, add it to `Program.cs`:

```csharp
var gameChoice = Console.ReadLine();
Game game = gameChoice switch
{
    "1" => FantasyQuest.Create(),
    "2" => SciFiAdventure.Create(),
    "3" => MyGame.Create(),     // Your new game
    _ => FantasyQuest.Create()
};
```

## Advanced: NPC Relationships

Create dynamic NPC interactions:

```csharp
var hero = gameBuilder.NPCs["hero"];
var villain = gameBuilder.NPCs["villain"];

hero.Relationships = new() { "villain" };
villain.Relationships = new() { "hero" };

// Add reputation tracking
hero.Reputation["kingdom"] = 100;
villain.Reputation["dark_lords"] = 50;
```

## Advanced: Custom Item Properties

Add special properties to items:

```csharp
var artifact = new ItemBuilder("cursed_ring")
    .WithName("Cursed Ring")
    .WithCustomProperty("curse_type", "strength_drain")
    .WithCustomProperty("curse_severity", "moderate")
    .WithCustomProperty("remove_method", "holy_water")
    .AsCursed()
    .Build();
```

## Tips for Great Game Design

1. **Atmosphere**: Use vivid descriptions to set mood
2. **Exploration**: Create interconnected rooms that invite exploration
3. **Progression**: Make challenges gradually harder
4. **NPCs**: Give each NPC personality and purpose
5. **Items**: Make finding items rewarding
6. **Quests**: Chain quests to create narrative progression
7. **Balance**: Mix peaceful and dangerous areas
8. **Rewards**: Make victories feel earned

## Template Checklist

When creating a game, include:

- [ ] Game title and description
- [ ] Story introduction
- [ ] Game objective
- [ ] At least 5-10 rooms
- [ ] At least 5 NPCs with personalities
- [ ] At least 10 items (mix of types)
- [ ] At least 2 quests
- [ ] Win conditions
- [ ] Appropriate game settings

Enjoy creating your game world!
