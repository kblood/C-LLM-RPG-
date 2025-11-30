# C# RPG Backend with Ollama

A modular C# console-based RPG backend designed to work with local LLMs via Ollama. Built for extensibility into Godot, Unity, or other game engines.

## Overview

This project provides:
- **Game State Management**: Pure C# deterministic game logic (rooms, NPCs, inventory, quests)
- **LLM Integration**: Ollama HTTP client for local model inference
- **NPC AI**: Individual "brain" components for each NPC with personality and memory
- **Game Master (GM)**: Orchestrates player actions, applies game logic, and narrates outcomes
- **Console Interface**: Interactive game loop (extensible to Godot/Unity)

## Architecture

```
src/
├── Core/
│   └── GameState.cs          # Main game world state
├── Models/
│   ├── Character.cs          # Player & NPC definitions
│   ├── Room.cs              # Location definitions
│   ├── Inventory.cs         # Item management
│   ├── Item.cs              # Item data
│   └── Quest.cs             # Quest tracking
├── LLM/
│   ├── OllamaClient.cs      # HTTP client for local models
│   └── NpcBrain.cs          # Individual NPC AI behavior
└── Services/
    └── GameMaster.cs        # Game orchestration & narration
```

## Prerequisites

1. **Ollama** running locally:
   ```bash
   # Install from https://ollama.ai
   ollama serve
   # In another terminal, pull a model:
   ollama pull mistral  # or qwen, llama2, etc.
   ```

2. **.NET 8.0 or later**

## Quick Start

### 1. Set Up Ollama

```bash
# Start Ollama server (listens on http://localhost:11434 by default)
ollama serve

# In another terminal, download a model
ollama pull mistral
```

### 2. Run the Game

```bash
cd C:\Devstuff\git\CSharpRPGBackend
dotnet run
```

### 3. Play

```
> Town Square
What do you do? look

Narrating your action...
You find yourself in a bustling marketplace...

What do you do? talk merchant
What do you say? Hello, do you have any quests?

Thinking...
Thadeus the Merchant: I've heard rumors of bandits in the forest...
```

## Game Commands

| Command | Example | Description |
|---------|---------|-------------|
| `look` | `look` | Examine current surroundings |
| `move` | `move forest` | Travel to adjacent location |
| `talk` | `talk merchant` | Speak with an NPC |
| `inventory` | `inventory` | Check your items |
| `take` | `take sword` | Pick up an item |
| `drop` | `drop coin` | Drop an item |
| `attack` | `attack goblin` | Combat action |
| `status` | `status` | Get narrative game status |
| `help` | `help` | Show available actions |
| `quit` | `quit` | Exit the game |

## Customization

### Changing the LLM Model

Edit `Program.cs`:
```csharp
const string ollamaModel = "qwen";  // Options: mistral, qwen, llama2, neural-chat, etc.
```

### Customizing NPC Personalities

In `GameMaster.cs`, modify `GenerateNpcPersonality()` or set custom prompts:
```csharp
var npc = gameState.NPCs["merchant"];
npc.PersonalityPrompt = @"You are a greedy merchant obsessed with profit...";
```

### Adding New Rooms

In `GameState.cs`, add to `InitializeDefaultGame()`:
```csharp
Rooms["dungeon"] = new Room
{
    Id = "dungeon",
    Name = "Dark Dungeon",
    Description = "A damp, foreboding cave...",
    ConnectedRooms = new() { "forest" },
    NPCIds = new() { "dungeon_master" }
};
```

### Adding New NPCs

Add to the `NPCs` dictionary in `GameState.cs` and initialize their brain in `GameMaster.InitializeNpcBrains()`.

## Integration with Godot/Unity

To integrate into a game engine:

1. **Keep Core Logic**: Move `src/Core/` and `src/Models/` to a shared library
2. **Replace Console**: Implement `IGameInterface` instead of `Program.cs`
3. **Attach to Game Objects**: Create Godot/Unity components that use `OllamaClient` and `NpcBrain`

Example Godot integration:
```csharp
[GlobalClass]
public partial class NpcCharacter : CharacterBody3D
{
    private NpcBrain _brain;
    private OllamaClient _ollamaClient;

    public override void _Ready()
    {
        _ollamaClient = new OllamaClient();
        var npc = GetNpc(); // from game state
        _brain = new NpcBrain(_ollamaClient, npc);
    }

    public async void TalkToNpc(string playerMessage)
    {
        var response = await _brain.RespondToPlayerAsync(playerMessage);
        DisplayDialogue(response);
    }
}
```

## Extending Features

### Adding Combat System
Implement a `CombatService` that uses the `Character` stats:
```csharp
public class CombatService
{
    public int CalculateDamage(Character attacker, Character defender)
    {
        // Roll dice, apply modifiers, etc.
    }
}
```

### Adding Quest System
Use the `Quest` model to create a `QuestService`:
```csharp
public class QuestService
{
    public void CompleteObjective(string questId, string objectiveId) { }
    public void RewardPlayer(Character player, Quest quest) { }
}
```

### Streaming Responses
Use `ChatStreamAsync` for long NPC speeches:
```csharp
await foreach (var chunk in _brain.RespondToPlayerStreamAsync(message))
{
    Console.Write(chunk); // Stream output to UI
}
```

## LLM Selection Guide

| Model | Best For | Speed | Memory |
|-------|----------|-------|--------|
| `mistral` | General RPG narration | Fast | ~7GB |
| `qwen` | Coherent dialogue | Medium | ~8GB |
| `llama2` | Roleplay-heavy | Medium | ~13GB |
| `neural-chat` | Conversational NPCs | Fast | ~7GB |
| `orca-mini` | Lightweight | Very Fast | ~3GB |

## Project Structure for Future Expansion

```
CSharpRPGBackend/
├── Backend/
│   ├── src/
│   ├── Program.cs
│   └── CSharpRPGBackend.csproj
├── GodotIntegration/
│   └── (Godot 4 C# project)
├── UnityIntegration/
│   └── (Unity C# project)
└── Shared/
    └── (Common core library)
```

## Troubleshooting

**"ERROR: Cannot connect to Ollama"**
- Make sure `ollama serve` is running
- Check that Ollama is on `http://localhost:11434`
- Modify `ollamaUrl` in `Program.cs` if needed

**"Model not found"**
```bash
ollama pull mistral
```

**LLM responses are slow**
- Use a faster, smaller model like `neural-chat` or `orca-mini`
- Reduce conversation history kept in `NpcBrain.cs`

## Future Enhancements

- [ ] Full quest system with branching dialogue
- [ ] Dynamic NPC relationship tracking
- [ ] Persistent world state (save/load)
- [ ] Multi-turn narrative sequences
- [ ] Web API layer (for remote Godot/Unity clients)
- [ ] LLamaSharp integration for in-process models
- [ ] Support for multiple LLM providers (OpenAI, Anthropic)

## References

- [Ollama Documentation](https://ollama.ai/)
- [GPT-OSS C# + Ollama Guide](https://devblogs.microsoft.com/dotnet/gpt-oss-csharp-ollama/)
- [Local LLM NPC with Godot 4](https://github.com/code-forge-temple/local-llm-npc)
- [LLamaSharp Repository](https://github.com/SciSharp/LLamaSharp)

## License

MIT - Feel free to extend and use in your projects.

---

**Next Steps**: Follow the instructions you'll paste to customize this backend further!
