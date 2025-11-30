# Getting Started with the C# RPG Backend

## Welcome!

You now have a complete, working C# text RPG engine powered by Ollama and local LLMs. This guide will help you get started.

## What You Have

### Core System
- **Game State Management**: Track rooms, NPCs, inventory, quests
- **Exit-Based Navigation**: Rooms connected with narrative descriptions
- **LLM Integration**: Ollama client for NPC AI and game narration
- **Console Game Loop**: Play text adventures in the terminal
- **Builder Pattern**: Easy room creation with fluent API

### Tools (Coming Soon)
- **Windows Forms Designer**: Visual room and NPC editor
- **JSON Import/Export**: Save and load worlds

### Documentation
- `README.md` - Project overview
- `SETUP.md` - Installation and configuration
- `ROOM_DESIGN_GUIDE.md` - How to design rooms and exits
- `ROOM_DESIGNER_QUICKSTART.md` - Designer tool tutorial
- `EXIT_SYSTEM_SUMMARY.md` - Technical details

## 5-Minute Quick Start

### 1. Verify Installation
```bash
cd C:\Devstuff\git\CSharpRPGBackend

# Check Ollama is running
ollama list

# Should show granite4:3b available
```

### 2. Build and Run
```bash
dotnet build
dotnet run
```

### 3. Play!
```
> look
A bustling marketplace...
You can go: North, East, South

> go North
You arrive at the Dark Forest...

> talk ranger
What do you say? Tell me about this forest
```

That's it! You're playing with an LLM-powered NPC.

## Next Steps

### Option A: Play the Default World (5 minutes)
The default world includes:
- Town Square (central hub)
- Tavern (cozy gathering place)
- Forest (dangerous area)
- Goblin Lair (boss fight area)

**Commands:**
```
look              # See room and available exits
go <exit_name>    # Navigate (e.g., "go North")
talk <npc_name>   # Talk to NPCs
status            # Game status narration
inventory         # Check your items
help              # Show available commands
quit              # Exit game
```

### Option B: Design Your Own World (30 minutes)

Using the **RoomBuilder** API:

```csharp
// Create a new file: MyWorld.cs
var myWorld = new RoomBuilder("throne_room")
    .WithName("Grand Throne Room")
    .WithDescription("A magnificent hall with golden pillars...")
    .AddExit("Out", "town_square")
    .AddNPC("king")
    .Build();

gameState.Rooms["throne_room"] = myWorld;
```

Or use the **Windows Forms Designer** (under development).

### Option C: Integrate with Godot/Unity (Later)

The core game logic (`src/Core/` and `src/Models/`) is engine-agnostic. You can:

1. Keep backend as console app or web service
2. Call from Godot/Unity via API
3. Share GameState via serialization
4. Reuse NPC and Room systems

## Project Structure

```
CSharpRPGBackend/
├── src/
│   ├── Core/                    # Game world state
│   │   └── GameState.cs
│   ├── Models/                  # Data classes
│   │   ├── Exit.cs              ← NEW: Narrative exits
│   │   ├── Room.cs              ← UPDATED: Exit-based
│   │   ├── Character.cs
│   │   ├── Item.cs
│   │   ├── Quest.cs
│   │   └── Inventory.cs
│   ├── LLM/                     # Ollama integration
│   │   ├── OllamaClient.cs      # HTTP client
│   │   └── NpcBrain.cs          # NPC personality/memory
│   ├── Services/                # Game orchestration
│   │   └── GameMaster.cs        # Gameplay flow
│   └── Utils/                   # Helpers
│       └── RoomBuilder.cs       ← NEW: Fluent builder
│
├── Program.cs                   # Console game loop
├── CSharpRPGBackend.csproj      # Project configuration
│
├── README.md                    # Project overview
├── SETUP.md                     # Installation guide
├── GETTING_STARTED.md           # This file
├── ROOM_DESIGN_GUIDE.md         # Room design tutorial
├── ROOM_DESIGNER_QUICKSTART.md  # Designer tool guide
├── EXIT_SYSTEM_SUMMARY.md       # Technical details
│
└── test-exits.txt               # Example gameplay session
```

## Key Concepts

### Rooms
Locations with descriptions, NPCs, items, and exits.

```csharp
var room = gameState.Rooms["forest"];
room.GetCurrentRoom().Name           // "Dark Forest"
room.GetAvailableExits()             // List of exits player can use
```

### Exits
Connections between rooms with narrative names.

```csharp
var exit = room.Exits["north"];
exit.DisplayName      // "North" (player sees this)
exit.DestinationRoomId // "cave" (internal identifier)
exit.IsAvailable      // true/false (can player use it?)
```

### NPCs
Characters with personality, driven by LLMs.

```csharp
var npc = gameState.NPCs["ranger"];
npc.PersonalityPrompt // How the LLM acts
npc.ConversationHistory // Memory of past chats
await npcBrain.RespondToPlayerAsync("Hello!"); // Get LLM response
```

### Game Master
Orchestrates gameplay: parses commands, applies logic, narrates outcomes.

```csharp
var narration = await gameMaster.ProcessPlayerActionAsync("go North");
// LLM generates dramatic narration of your action
```

## Common Tasks

### Create a New Room
```csharp
var library = new RoomBuilder("library")
    .WithName("The Grand Library")
    .WithDescription("Ancient books line the walls...")
    .AddExit("North", "study")
    .AddExit("South", "entrance")
    .AddNPC("librarian")
    .Build();

gameState.Rooms["library"] = library;
```

### Connect Rooms Together
```csharp
// From room A, exit leads to room B
roomA.Exits["north"] = new Exit("North", "roomB");

// From room B, exit leads back to room A
roomB.Exits["south"] = new Exit("South", "roomA");
```

### Create a Locked Door
```csharp
var vaultExit = new Exit("To the Vault", "vault")
{
    IsAvailable = false,
    UnavailableReason = "The vault door is sealed. You need a key."
};

room.Exits["vault"] = vaultExit;

// Unlock it later when player has key
vaultExit.IsAvailable = true;
```

### Add Personality to an NPC
```csharp
var pirate = new NpcBuilder("pirate", "Captain Redbeard")
    .WithHealth(80, 80)
    .WithLevel(3)
    .WithAlignment(CharacterAlignment.Evil)
    .WithPersonalityPrompt(@"
        You are a ruthless pirate captain. Speak in nautical terms.
        You're greedy and always looking for treasure.
        You have no patience for fools.")
    .Build();

gameState.NPCs["pirate"] = pirate;
```

## Customization

### Change the LLM Model
Edit `Program.cs`:
```csharp
const string ollamaModel = "qwen";  // Changed from granite4:3b
```

Available options: mistral, qwen, llama2, neural-chat, orca-mini, etc.

### Modify Game Master Narration Style
Edit `src/Services/GameMaster.cs`:
```csharp
private static string GenerateDefaultGMPrompt()
{
    return @"You are a dark, mysterious narrator. Use gothic language.
             Emphasize danger and suspense. Make everything dramatic.";
}
```

### Add New Commands
Edit `Program.cs` in the main game loop:
```csharp
if (playerInput.StartsWith("cast ", StringComparison.OrdinalIgnoreCase))
{
    var spell = playerInput[5..].Trim();
    Console.WriteLine(await gameMaster.ProcessPlayerActionAsync($"cast {spell}"));
}
```

## Playing Tips

### Exploration
```
> look          # Always look first to see exits
> go <exit>     # Navigate using exit names
```

### NPC Interaction
```
> talk <npc>    # Start conversation
> What is your name?  # Free-form questions
```

### Getting Help
```
> help          # Show available commands
> status        # Get narrative status
```

### Save/Load (Future)
Currently saves as JSON, so you can export worlds and reload them.

## Architecture Decisions

### Why Exits Instead of Room IDs?
- **Narrative**: "Go into the tavern" vs "Go to tavern"
- **Flexible**: Multiple exits can have same name (narrative variations)
- **Accessible**: Players never see internal IDs
- **Designer-friendly**: Non-programmers can create worlds

### Why Dictionary-based Exits?
- **Fast lookup**: O(1) random access
- **Type-safe**: Exit object has properties
- **Serializable**: Easy JSON export/import
- **Flexible**: Can add properties (locked, description, etc.)

### Why Separate NPC Brains?
- **Modularity**: NPCs exist independently of rooms
- **Persistence**: Memory survives room changes
- **Personality**: Each NPC has distinct LLM prompt
- **Testing**: Can test NPCs in isolation

## Troubleshooting

### "Cannot connect to Ollama"
```
# Make sure Ollama is running
ollama serve

# In another terminal
ollama list
```

### "Model not found"
```
ollama pull granite4:3b
```

### Game loops infinitely
```
# Ensure stdin is provided
cat commands.txt | dotnet run
```

### NPCs say generic things
- Update their `PersonalityPrompt`
- Use a better model (qwen, llama2)
- Provide more context in prompts

## Next Learning Steps

1. **Read**: `ROOM_DESIGN_GUIDE.md` - Comprehensive room design
2. **Try**: Create a 5-room dungeon using RoomBuilder
3. **Test**: Run your world and play through it
4. **Explore**: Check `EXIT_SYSTEM_SUMMARY.md` for technical details
5. **Build**: Design something ambitious!

## Resources

### Documentation
- `README.md` - Full feature overview
- `ROOM_DESIGN_GUIDE.md` - Room design deep dive
- `EXIT_SYSTEM_SUMMARY.md` - Technical architecture

### Code Examples
- `src/Core/GameState.cs` - Default world setup
- `src/Utils/RoomBuilder.cs` - Builder API examples
- `test-exits.txt` - Sample gameplay commands

### External
- [Ollama Documentation](https://ollama.ai/)
- [GPT-OSS C# Guide](https://devblogs.microsoft.com/dotnet/gpt-oss-csharp-ollama/)
- [Godot 4 C# Docs](https://docs.godotengine.org/en/stable/getting_started/scripting/c_sharp/index.html)

## What's Coming

- [ ] Windows Forms room designer
- [ ] NPC designer interface
- [ ] Web API wrapper for remote clients
- [ ] Advanced quest system
- [ ] Combat mechanics
- [ ] Godot integration example
- [ ] More LLM models support
- [ ] World visualization

## Need Help?

1. Check the documentation files
2. Look at code examples in `src/`
3. Run test scenarios in `test-exits.txt`
4. Examine default world in `GameState.cs`

## Summary

You have:
- ✅ Working text RPG engine
- ✅ LLM-powered NPCs via Ollama
- ✅ Narrative room navigation system
- ✅ Builder API for world creation
- ✅ Console game loop
- ✅ Comprehensive documentation

Next: **Pick a project and start building!**

Some ideas:
- Dungeon crawler (3 levels, boss fight)
- Town exploration (multiple districts, quests)
- Story branches (different outcomes based on choices)
- NPC relationships (NPCs remember your actions)
- Item economy (buying, selling, crafting)

Have fun! 🎮
