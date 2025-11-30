# Quick Reference - Game Creation Cheat Sheet

## Game Creation (One-Liner Pattern)

```csharp
var game = new GameBuilder("id")
    .WithTitle("Title").WithStyle(GameStyle.Fantasy).WithStory("...")
    .AddRooms(room1, room2, room3)
    .AddNPCs(npc1, npc2, npc3)
    .AddItems(item1, item2, item3)
    .WithStartingRoom("start")
    .Build();
```

## Room Creation

```csharp
new RoomBuilder("room_id")
    .WithName("Room Name")
    .WithDescription("Vivid description...")
    .AddExit("Direction", "destination_id")
    .AddExit("Action", "destination_id", "Optional description")
    .AddNPCs("npc1", "npc2")
    .WithMetadata("danger_level", 1)
    .Build()
```

## NPC Creation

```csharp
var npc = new NpcBuilder("npc_id", "NPC Name")
    .WithHealth(50, 50)
    .WithLevel(2)
    .WithPersonalityPrompt("Character personality and behavior...")
    .Build();

npc.CurrentRoomId = "starting_room";
npc.Role = NPCRole.Merchant;
npc.CanJoinParty = true;
npc.Description = "Visual description";
npc.PatrolRoomIds = new() { "room1", "room2" };
```

## Item Creation

### Weapon
```csharp
new ItemBuilder("id").WithName("Name")
    .AsWeapon(damage: 10, criticalChance: 15)
    .Build()
```

### Armor
```csharp
new ItemBuilder("id").WithName("Name")
    .AsArmor(armorBonus: 5, slot: "chest")
    .Build()
```

### Key (Any Type)
```csharp
new ItemBuilder("id").WithName("Name")
    .AsKey("what_it_unlocks", KeyType.Mechanical)
    .Build()

// Key types: Mechanical, Magical, Technological, Biological, Puzzle
```

### Teleportation
```csharp
new ItemBuilder("id").WithName("Name")
    .AsTeleportation("destination_room_id", "Optional description")
    .Build()
```

### Consumable
```csharp
new ItemBuilder("id").WithName("Name")
    .AsConsumable(uses: 1)
    .WithConsumableEffect("heal", 50)
    .Build()
```

### Quest Item
```csharp
new ItemBuilder("id").WithName("Name")
    .AsQuestItem()
    .Build()
```

## NPC Roles

```
CommonPerson, Merchant, Guard, Warrior, Mage, Healer, Scholar,
Questgiver, Boss, Ally, Neutral, Companion
```

## Item Types

```
Weapon, Armor, Key, Teleportation, Consumable, QuestItem, Miscellaneous
```

## Item Properties (Chainable)

```csharp
.WithName(string)                    // Item name
.WithDescription(string)             // Full description
.WithWeight(int)                     // Encumbrance
.WithValue(int)                      // Gold value
.WithTheme(string)                   // "fantasy", "sci-fi"
.WithRarity(ItemRarity)              // Common/Uncommon/Rare/Epic/Legendary
.AsUnique()                           // One-of-a-kind
.AsCursed()                           // Cursed item
.WithCustomProperty(key, value)      // Custom property
```

## Game Styles

```
Fantasy, SciFi, Steampunk, Horror, Modern, Western, Mystery, Historical, Custom
```

## Item Rarity

```
Common, Uncommon, Rare, Epic, Legendary
```

## Equipment Slots

```
head, chest, hands, legs, feet, main_hand, off_hand
```

## NPC Movement

```csharp
npc.CanMove = true;                      // Can NPC move?
npc.CurrentRoomId = "current";          // Current location (changes)
npc.HomeRoomId = "home";                // Home base (static)
npc.PatrolRoomIds = new() { "r1", "r2" };  // Patrol route
npc.PatrolIntervalTurns = 3;            // Move every 3 turns
```

## Game Configuration

```csharp
gameBuilder
    .WithCombat(bool)                   // Enable combat?
    .WithInventory(bool)                // Have inventory?
    .WithMagic(bool)                    // Have magic?
    .WithTechnology(bool)               // Have technology?
    .WithNPCRecruitment(bool)          // Can recruit NPCs?
    .WithPermadeath(bool)               // One-hit death?
    .WithPvP(bool)                      // Player vs Player?
    .WithFreeRoam(bool)                 // Can go anywhere?
    .WithStartingRoom(string)           // Starting location
    .AddWinConditionRoom(string)        // Winning room
```

## Creating a Complete Minimal Game (Template)

```csharp
using CSharpRPGBackend.Core;
using CSharpRPGBackend.Utils;

namespace CSharpRPGBackend.Games;

public static class MyGame
{
    public static Game Create()
    {
        var gameBuilder = new GameBuilder("my_game")
            .WithTitle("My Game Title")
            .WithStyle(GameStyle.Fantasy)
            .WithStory("Story text that appears when game loads")
            .WithObjective("What the player needs to accomplish");

        // === ROOMS ===
        var room1 = new RoomBuilder("start")
            .WithName("Starting Room")
            .WithDescription("Description...")
            .AddExit("Go", "room2")
            .Build();

        var room2 = new RoomBuilder("room2")
            .WithName("Second Room")
            .WithDescription("Description...")
            .AddExit("Back", "start")
            .Build();

        gameBuilder.AddRooms(room1, room2);

        // === NPCs ===
        var npc = new NpcBuilder("merchant", "Aldous")
            .WithHealth(50, 50)
            .WithPersonalityPrompt("You are friendly...")
            .Build();
        npc.CurrentRoomId = "start";
        npc.Role = NPCRole.Merchant;

        gameBuilder.AddNPC(npc);

        // === ITEMS ===
        var sword = new ItemBuilder("sword")
            .WithName("Iron Sword")
            .AsWeapon(damage: 10)
            .Build();

        gameBuilder.AddItem(sword);

        // === QUESTS ===
        var quest = new Quest
        {
            Id = "main",
            Title = "Main Quest",
            Description = "...",
            GiverNpcId = "merchant",
            RewardExperience = 100,
            RewardGold = 50,
            Objectives = new() { "Goal 1", "Goal 2" }
        };

        gameBuilder.AddQuest(quest);

        // === BUILD ===
        return gameBuilder
            .WithStartingRoom("start")
            .AddWinConditionRoom("room2")
            .Build();
    }
}
```

## Player Commands

```
look              - See room description and available exits
go <direction>    - Move to another room
talk <npc>        - Talk to an NPC
inventory         - Check items
status            - Game status narration
help              - Show available commands
quit              - Exit game
```

## Key Game Properties

```csharp
game.Title                          // Game name
game.Style                          // Game aesthetic
game.StoryIntroduction              // Opening story
game.GameObjective                  // Main goal
game.StartingRoomId                 // Starting location
game.WinConditionRoomIds            // Winning condition(s)
game.Rooms                          // All rooms
game.NPCs                           // All NPCs
game.Items                          // All items
game.Quests                         // All quests
game.InitialPlayerHealth            // Starting HP
game.HasCombat, HasMagic, etc.     // System toggles
```

## Common Mistakes to Avoid

❌ Forgetting to set NPC `CurrentRoomId` or `HomeRoomId`
✅ Always set at least one location for NPCs

❌ Creating exits in only one direction
✅ Make exits bidirectional (A→B and B→A)

❌ Teleportation items without valid destination room
✅ Ensure destination room exists in game.Rooms

❌ Keys that don't unlock anything
✅ Make sure UnlocksId matches an actual door/exit

❌ Starting room not in Rooms dictionary
✅ Add starting room to game.Rooms first

❌ NPC roles without personality prompt
✅ Always provide PersonalityPrompt for NPCs

❌ Items with zero weight (makes game logic confusing)
✅ Set appropriate weights for all items

## Getting Started Checklist

- [ ] Create Game class in `src/Games/`
- [ ] Create at least 3 rooms
- [ ] Add at least 2 NPCs
- [ ] Add at least 3 items
- [ ] Create at least 1 quest
- [ ] Set starting room
- [ ] Set win condition room(s)
- [ ] Call `.Build()`
- [ ] Add to game selection in `Program.cs`
- [ ] Test by running `dotnet run`

## Performance Considerations

- **Rooms**: Can handle 100+ rooms
- **NPCs**: Can handle 50+ NPCs (more with streaming)
- **Items**: Can handle 200+ items
- **LLM Calls**: Use Granite4:3b or smaller for speed

## File Locations

```
Game code:          src/Games/
Item builder:       src/Utils/ItemBuilder.cs
Game builder:       src/Utils/GameBuilder.cs
NPC builder:        src/Utils/RoomBuilder.cs (NpcBuilder)
Room builder:       src/Utils/RoomBuilder.cs
Program entry:      Program.cs
Documentation:      *.md files
```

---

**Print this page for quick reference while designing games!**
