# C# RPG Backend - Complete Documentation Index

## Quick Start

1. **New to the project?** → Read `GETTING_STARTED.md`
2. **Want to play?** → Run `dotnet run` and select a game
3. **Want to create games?** → Read `GAME_CREATION_GUIDE.md`

## Documentation Files

### Getting Started
- **[GETTING_STARTED.md](GETTING_STARTED.md)** - 5-minute quick start guide, common tasks, troubleshooting

### Game System
- **[GAME_CREATION_GUIDE.md](GAME_CREATION_GUIDE.md)** - Complete guide to creating games with the GameBuilder
- **[GAME_SYSTEM_SUMMARY.md](GAME_SYSTEM_SUMMARY.md)** - Overview of the game framework and included examples

### Room & Navigation System
- **[ROOM_DESIGN_GUIDE.md](ROOM_DESIGN_GUIDE.md)** - How to design rooms and interconnected exits
- **[EXIT_SYSTEM_SUMMARY.md](EXIT_SYSTEM_SUMMARY.md)** - Technical details of the exit/navigation system

### Project Overview
- **[README.md](README.md)** - Project overview and feature list
- **[SETUP.md](SETUP.md)** - Installation and initial setup

### UI/Tools (Coming Soon)
- **[ROOM_DESIGNER_QUICKSTART.md](ROOM_DESIGNER_QUICKSTART.md)** - Guide to the Windows Forms room designer

## What You Can Do

### Play Games
Two fully-featured games included:
1. **Fantasy Quest** - Slay a dragon and save the kingdom
   - 9 rooms, 10 NPCs, 12 items, full magic system
   - Estimated playtime: 120 minutes

2. **Sci-Fi Adventure** - Escape an alien-infested space station
   - 9 rooms, 5 NPCs, 13 items, permadeath enabled
   - Estimated playtime: 90 minutes

### Create New Games
Use the `GameBuilder` fluent API to create games:

```csharp
var myGame = new GameBuilder("my_game")
    .WithTitle("My Epic Quest")
    .WithStyle(GameStyle.Fantasy)
    .WithStory("Once upon a time...")
    .AddRoom(...)
    .AddNPC(...)
    .AddItem(...)
    .Build();
```

### Design Rooms
Connected rooms with narrative exit descriptions:

```csharp
new RoomBuilder("forest")
    .WithName("Dark Forest")
    .WithDescription("Ancient trees...")
    .AddExit("North", "cave")
    .AddExit("South", "town")
    .AddNPC("ranger")
    .Build()
```

### Create NPCs
NPCs with personality, location, and behavior:

```csharp
new NpcBuilder("merchant", "Aldous")
    .WithPersonalityPrompt("You are a friendly merchant...")
    .Build();

npc.CurrentRoomId = "marketplace";
npc.CanJoinParty = true;
npc.Role = NPCRole.Merchant;
```

### Design Items
Comprehensive item system with multiple categories:

```csharp
// Weapons
new ItemBuilder("sword")
    .WithName("Iron Sword")
    .AsWeapon(damage: 10)
    .Build()

// Keys
new ItemBuilder("key")
    .WithName("Door Key")
    .AsKey("locked_door", KeyType.Mechanical)
    .Build()

// Teleportation
new ItemBuilder("scroll")
    .WithName("Recall Scroll")
    .AsTeleportation("home")
    .Build()

// Consumables
new ItemBuilder("potion")
    .WithName("Health Potion")
    .AsConsumable(uses: 1)
    .WithConsumableEffect("heal", 50)
    .Build()
```

## Source Code Structure

```
src/
├── Core/
│   └── GameState.cs                    Main game world state
├── Models/
│   ├── Character.cs                    NPCs and player character
│   ├── Exit.cs                         Room exits with names
│   ├── Game.cs                         ⭐ NEW Game definition
│   ├── Inventory.cs                    Item management
│   ├── Item.cs                         ⭐ ENHANCED Item system
│   ├── Quest.cs                        Quest definition
│   └── Room.cs                         Room definition
├── LLM/
│   ├── OllamaClient.cs                 Ollama HTTP client
│   └── NpcBrain.cs                     NPC AI with memory
├── Services/
│   └── GameMaster.cs                   Game orchestration
└── Utils/
    ├── GameBuilder.cs                  ⭐ NEW Game builder
    ├── ItemBuilder.cs                  ⭐ NEW Item builder
    ├── NpcBuilder.cs                   ⭐ NEW NPC builder
    └── RoomBuilder.cs                  Room builder

src/Games/
├── FantasyQuest.cs                     ⭐ NEW Fantasy game
└── SciFiAdventure.cs                   ⭐ NEW Sci-Fi game

Program.cs                              ⭐ ENHANCED Game selector
```

## Key Features

### Game System
✅ Multiple complete games in one engine
✅ Game definition with story and objectives
✅ Game style system (Fantasy, Sci-Fi, Horror, etc.)
✅ Custom game settings (combat, magic, technology, etc.)
✅ Win conditions and game progression

### Item System
✅ 7 item types (Weapon, Armor, Key, Teleportation, Consumable, Quest, Misc)
✅ Combat stats (damage, armor, critical chance)
✅ Key system with 5 key types (Mechanical, Magical, Technological, Biological, Puzzle)
✅ Teleportation devices for fast travel
✅ Consumables with effects
✅ Equipment slots system
✅ Item rarity system (Common to Legendary)
✅ Theme support (fantasy, sci-fi, steampunk)

### NPC System
✅ NPCs with locations and movement patterns
✅ NPC roles (Merchant, Guard, Mage, Questgiver, Boss, etc.)
✅ NPC recruitment into party
✅ Relationship tracking
✅ Reputation system
✅ Patrol routes and home locations
✅ Equipment slots for NPCs
✅ LLM-powered personalities

### Room & Navigation
✅ Rooms with vivid descriptions
✅ Narrative exit names ("Into the tavern" vs "east")
✅ Interconnected room system
✅ Room metadata (danger level, lighting, temperature)
✅ Conditional exits (locked, available/unavailable)

### Builder APIs
✅ `RoomBuilder` - Fluent room creation
✅ `ItemBuilder` - Fluent item creation
✅ `NpcBuilder` - Fluent NPC creation
✅ `GameBuilder` - Fluent game creation

## Technology Stack

- **Language**: C# .NET 8.0
- **LLM Integration**: Ollama (local open-source models)
- **Game Definition**: Fluent builders (no config files)
- **Serialization**: System.Text.Json
- **Game Loop**: Synchronous console application

## Included Models

- Granite 4 3B (default - lightweight)
- Compatible with: Mistral, Qwen, Llama2, Neural-Chat, Orca-Mini, etc.

## Game Selection

Run the application:
```bash
dotnet run
```

Choose from available games:
```
=== C# RPG Backend - Game Selector ===

Available Games:
1. Fantasy Quest - The Dragon's Hoard
2. Sci-Fi Adventure - Escape from Station Zeta

Select a game (1-2): _
```

## Example Game: Minimal Version

```csharp
var game = new GameBuilder("simple")
    .WithTitle("The Simple Quest")
    .WithStyle(GameStyle.Fantasy)

    // Add rooms
    .AddRoom(new RoomBuilder("start")
        .WithName("Start")
        .WithDescription("You are here.")
        .AddExit("Go", "end")
        .Build())
    .AddRoom(new RoomBuilder("end")
        .WithName("End")
        .WithDescription("You won!")
        .Build())

    // Add NPC
    .AddNPC(new NpcBuilder("guide", "A Guide")
        .WithPersonalityPrompt("Help the player.")
        .Build())

    // Add item
    .AddItem(new ItemBuilder("sword")
        .WithName("Sword")
        .AsWeapon(damage: 10)
        .Build())

    .WithStartingRoom("start")
    .Build();
```

## Common Patterns

### Create a Game with Story
```csharp
new GameBuilder("my_game")
    .WithTitle("Title")
    .WithStory("Opening story that appears when game loads")
    .WithObjective("What the player needs to achieve")
    .WithEstimatedPlayTime(60)
    // ... add content ...
    .Build()
```

### Create a Key-Based Progression
```csharp
// Create a locked exit
var lockedExit = new Exit("To Vault", "vault_room")
{
    IsAvailable = false,
    UnavailableReason = "Door is sealed"
};
room.Exits["vault"] = lockedExit;

// Create a key
new ItemBuilder("key")
    .WithName("Golden Key")
    .AsKey("vault_room", KeyType.Magical)
    .Build()

// In game logic: if player has key, unlock the exit
if (player.HasItem("golden_key"))
{
    room.Exits["vault"].IsAvailable = true;
}
```

### Create an Ally NPC
```csharp
var ally = new NpcBuilder("warrior", "Sir Lancelot")
    .WithHealth(100, 100)
    .WithLevel(3)
    .WithPersonalityPrompt("You are a brave knight...")
    .Build();

ally.CanJoinParty = true;
ally.Role = NPCRole.Ally;
ally.CurrentRoomId = "tavern";
ally.Description = "A noble knight in shining armor";
```

## Next Steps

1. **Learn the basics**: Read `GETTING_STARTED.md`
2. **Try playing**: Run `dotnet run` and explore the games
3. **Understand the system**: Read `GAME_CREATION_GUIDE.md`
4. **Create your game**: Use `GameBuilder` to make something new
5. **Share it**: Add your game to the games folder and invite others to play

## Help & Support

- Check the relevant documentation file for your task
- Review the example games (`FantasyQuest.cs`, `SciFiAdventure.cs`)
- Look at the builder classes for API reference
- Read code comments in the source files

## Recent Changes (Latest Update)

✨ **Game System Added**
- Complete game definition framework
- Multi-game support with game selection
- Enhanced item system with stats and categories
- NPC location and movement system
- Two example games (Fantasy and Sci-Fi)
- Comprehensive documentation

📚 **Documentation**
- GAME_CREATION_GUIDE.md - Complete game design tutorial
- GAME_SYSTEM_SUMMARY.md - Framework overview
- INDEX.md - This file

## Summary

You now have a **complete, production-ready game creation framework** with:
- 2 fully-playable example games
- Comprehensive item, NPC, and room systems
- Easy-to-use fluent builders
- LLM-powered NPC personalities
- Multiple game styles and themes
- Full documentation

**Start playing or start creating - it's your choice!** 🎮

---

**Latest Build**: ✅ Successful
**Total Files**: 25 source/documentation files
**Lines of Code**: ~3,000+ (core + examples)
**Games Included**: 2 (Fantasy, Sci-Fi)
**Documentation Pages**: 8
