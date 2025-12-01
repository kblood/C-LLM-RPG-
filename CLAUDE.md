# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**C# RPG Backend with Ollama** is a modular, LLM-powered RPG backend using local language models via Ollama. It implements a complete game loop with deterministic game logic (rooms, NPCs, inventory, quests) orchestrated by a Game Master service that uses an LLM for natural language parsing and narrative narration.

The architecture cleanly separates:
- **Game Logic** (GameState, Room, Character, Item, Quest) - deterministic and rules-based
- **LLM Integration** (OllamaClient, NpcBrain) - handles HTTP communication with Ollama
- **Game Orchestration** (GameMaster) - bridges player input to game logic via LLM interpretation
- **Game Definitions** (Game class, GameBuilder, FantasyQuest, SciFiAdventure) - reusable game configurations

## Building, Running & Testing

### Build
```bash
dotnet build
```

### Run Interactive Mode
```bash
dotnet run
```
Launches a game selector where the player can choose between available games (Fantasy Quest, Sci-Fi Adventure).

### Run Replay Mode
```bash
dotnet run replay
```
Automatically plays both games using randomized player actions, generates markdown logs for verification and testing.

### Run WinForms Room Designer/Editor
```bash
dotnet run --project RPGGameEditor/RPGGameEditor.csproj
```
Launches a visual Windows Forms application for creating and editing game worlds without writing code. Allows you to:
- Create, edit, and delete rooms
- Add and manage exits between rooms
- Assign NPCs to rooms
- Edit room metadata and properties
- Save/load world data as JSON files

For detailed usage instructions, see `ROOM_DESIGNER_QUICKSTART.md`.

### Project Structure
- **Program.cs** - Entry point with interactive and replay modes
- **RPGGameEditor/** - Windows Forms application for visual game world editing
  - **GameEditorForm.cs** - Main editor UI for creating/editing rooms, NPCs, exits, and metadata
  - **Program.cs** - WinForms entry point
- **src/Core/GameState.cs** - Central game state management
- **src/Services/GameMaster.cs** - Main orchestration service (two-step action system: decide → execute → narrate)
- **src/Services/CombatService.cs** - Combat resolution system with hit/damage/flee mechanics
- **src/LLM/** - Ollama integration (OllamaClient, NpcBrain, ChatMessage/Response models)
- **src/Models/** - Data structures (Character, Room, Item, Quest, Game, etc.)
- **src/Utils/** - Builder patterns for fluent game configuration (GameBuilder, RoomBuilder, ItemBuilder, NpcBuilder)
- **src/Games/** - Predefined game scenarios (FantasyQuest, SciFiAdventure)

## Architecture & Key Concepts

### Game Loop Flow (Two-Step LLM Decision Architecture)
```
Player Input → Step 1: LLM Decides Actions → Step 2: Execute Game Logic → Step 3: LLM Narrates Results → Game State Display
```

1. **Step 1: Decision Making** (GameMaster.DecideActionsAsync):
   - Sends natural language command to LLM with full game context
   - Provides available commands, NPCs (with alive/dead status), exits, inventory, and combat state
   - LLM returns JSON array of ActionPlans: `[{ "action": "examine", "target": "body" }, { "action": "take", "target": "sword" }]`
   - Supports chaining multiple actions from a single player command
   - Falls back to TryParseFallback() if LLM fails to return valid JSON

2. **Step 2: Action Execution** (GameMaster.ApplyActionAsync):
   - Each ActionPlan is routed to its deterministic handler (move, talk, attack, use item, etc.)
   - Updates GameState directly (no LLM involvement)
   - Returns ActionResult with success/failure and game mechanics message
   - Execution is grounded in actual game rules (damage calculations, item availability, etc.)

3. **Step 3: Intelligent Narration** (GameMaster.NarrateWithResultsAsync):
   - Receives player's original intent and actual execution results
   - LLM creates engaging narrative based on ACTUAL outcomes (not fictional)
   - Narration cannot contradict what actually happened in the game
   - NPC dialogue uses separate NpcBrain system (always LLM-powered)

### Critical Dependencies
- **Ollama Instance**: Must be running on `http://localhost:11434` (configurable in Program.cs)
- **Model Selection**: Program.cs uses "granite4:3b" by default; changeable per game or prompt

### NPC Interaction
- Each NPC has an NpcBrain instance initialized in GameMaster.InitializeNpcBrains()
- NpcBrain.RespondToPlayerAsync handles all NPC dialogue via separate LLM prompts
- NPCs can have custom PersonalityPrompt; falls back to auto-generated if not provided
- NPCs have full combat stats (Strength, Agility, Armor) and can be attacked
- Defeated NPCs remain in room as bodies that can be examined and looted
- Dead NPCs display with ☠️ skull emoji to indicate death status

### Game Definition System
Games are created using GameBuilder (fluent pattern) and consist of:
- **Rooms** with exits (bidirectional connections)
- **NPCs** with basic stats and optional personality prompts
- **Items** (weapons, armor, consumables, teleportation)
- **Quests** (tracked in GameState, not yet fully integrated)
- **Game Metadata** (title, style, objective, feature flags)

Example: FantasyQuest is defined in src/Games/FantasyQuest.cs using GameBuilder to configure the entire game world.

## Important Implementation Details

### Character Stats System
- **Health/MaxHealth**: Character vitality; IsAlive = Health > 0
- **Strength** (10 default): Base 5 damage + (Strength - 10) / 2; minimum 1 damage
- **Agility** (10 default): Affects dodge/critical/flee chances; each point above 10 = +1% critical, +0.5% dodge, +5% flee
- **Armor** (0 default): 0.5 damage reduction per point (max 15% reduction)
- Equipment slots can provide additional Armor and Damage bonuses
- Both player and NPCs use same combat stat system

### Combat System (CombatService.cs)
- **ResolveAttack**: 70% base hit chance + agility mods, 5% base crit + 1% per agility above 10
- **Damage Calculation**: Base damage from Strength + weapon bonus, reduced by armor
- **AttemptFlee**: Base 50% + 5% per agility difference, clamped 15-95%
- **Combat Mode**: InCombatMode flag + CurrentCombatNpcId tracks active fights
- **NPC Death**: Bodies remain in rooms, marked with CanMove=false, can be examined and looted

### Action Handlers (GameMaster.cs)
- **DecideActionsAsync**: LLM receives game context and returns array of ActionPlans
- **ApplyActionAsync**: Routes actions to handlers (move, talk, attack, use item, etc.)
  - **HandleMove**: Validates exit exists, updates CurrentRoomId
  - **HandleTalk**: Finds NPC in current room, calls NpcBrain for dialogue
  - **HandleAttack**: Initiates combat, uses CombatService for damage resolution
  - **HandleStatus**: Shows combat health bars for player and current enemy
  - **HandleStopCombat**: Exits combat mode if conditions met
  - **HandleFlee**: Attempts flee with agility-based skill check
  - **HandleInventory/Look/Examine**: Direct game state queries; shows dead NPC indicator and loot
  - **HandleUse**: Supports teleportation items and consumables with effects
  - **HandleTake/Drop**: Item management; can loot from dead NPCs
- **NarrateWithResultsAsync**: Creates narrative based on actual execution results
- **Fallback Parser**: TryParseFallback handles common commands without LLM (emergency fallback)

### Room Exit System
- Rooms store exits in a Dictionary: `{ exitKey, Exit(displayName, destinationRoomId, description) }`
- FindExit() performs fuzzy matching on display name
- Room.GetAvailableExits() returns ordered list for UI display

### Inventory & Loot System
- Player inventory is a separate Inventory class with item stacking (InventoryItem tracks quantity)
- NPCs can carry items in CarriedItems dictionary (for looting when defeated)
- Items persist in rooms until taken (ItemType: Weapon, Armor, Consumable, Utility)
- Can examine dead NPCs to see available loot items
- Take action transfers items from NPC body to player inventory

### Chat Message Format
All LLM calls use structured ChatMessage objects with Role/Content pairs. OllamaClient.ChatAsync wraps messages in OllamaChatRequest before posting to `/api/chat` endpoint.

### Game State Isolation
- Each game session has its own GameState instance (no global state)
- Replay mode creates independent GameState per game to avoid cross-contamination
- InCombatMode flag ensures game state is properly maintained during fights

## Development Guidelines

### Using the Room Designer/Editor
The WinForms Room Designer (`RPGGameEditor`) provides a visual interface for world building:
1. Launch: `dotnet run --project RPGGameEditor/RPGGameEditor.csproj`
2. Create rooms, exits, and assign NPCs without coding
3. Save world data as JSON files (e.g., `world_data.json`)
4. Load these files back into the editor for iteration
5. Use JSON files as templates for programmatic game definitions

See `ROOM_DESIGNER_QUICKSTART.md` for complete usage guide.

### Adding New Game Actions
1. Create handler method in GameMaster: `private ActionResult Handle[Action](...)`
2. Add case in ApplyActionAsync switch statement
3. Update DecideActionsAsync system prompt to teach LLM the new action
4. Test with replay mode to verify action routing
5. For combat actions, consider whether they should show health bars (combat mode actions)

### Adding New Rooms/NPCs
Use GameBuilder, RoomBuilder, NpcBuilder and define in Game subclass:
```csharp
var room = new RoomBuilder("room_id")
    .WithName("Display Name")
    .WithDescription("Description")
    .AddExit("Direction", "destination_id", "exit description")
    .AddNPCs("npc_id_1", "npc_id_2")
    .Build();
gameBuilder.AddRoom(room);

var npc = new NpcBuilder("npc_id", "NPC Name")
    .WithStats(strength: 12, agility: 11, armor: 2)
    .WithHealth(30, 30)
    .WithLoot(sword, 1)
    .WithPersonalityPrompt("Your custom NPC personality...")
    .Build();
```

### Modifying LLM Prompts
- **Decision Prompt** (DecideActionsAsync): Controls which actions LLM decides to execute; lists available commands and game state
- **Narration Prompt** (NarrateWithResultsAsync): Controls narrative generation; grounded in actual outcomes
- **NPC Personalities**: GenerateNpcPersonality() or set Character.PersonalityPrompt
- **Fallback Parser** (TryParseFallback): Hardcoded command recognition for reliability

### Testing Considerations
- Replay mode useful for verifying action parsing and execution without manual input
- LLM responses are non-deterministic; fallback parser ensures basic commands work
- Two-step architecture ensures combat results are deterministic even if narration varies
- Test with multiple models (granite4:3b, mistral, etc.) as decision quality varies
- Dead NPC bodies should persist and be lootable; verify in replay logs

## Model & Configuration

The project uses **Ollama** for local LLM inference. Current configuration:
- **URL**: `http://localhost:11434` (Program.cs)
- **Model**: `granite4:3b` - lightweight, good for RPG dialogue and action decisions
- **Alternatives**: mistral, qwen, llama2, neural-chat (see README for specs)

**Note**: The two-step architecture (decide → execute → narrate) works best with models that can consistently return valid JSON. granite4:3b is recommended for balance of speed and accuracy.

Ollama must be running: `ollama serve` in separate terminal before launching game.

## Known Limitations & Future Improvements

### Current Limitations
- No persistent game saving/loading (each session starts fresh)
- Quests are tracked but not fully integrated into game logic
- NPC patrol system exists but not fully utilized in current games
- Equipment system exists but not fully leveraged (can equip but NPCs don't use equipped items)
- No party/companion following system yet (CanJoinParty flag exists but unused)

### Architecture Notes
- DecideActionsAsync relies on LLM JSON parsing; occasional failures fall back to TryParseFallback
- Two-step architecture adds ~2 LLM calls per action (decide + narrate); suitable for turn-based games
- Combat health bar display only available during InCombatMode
- NPC bodies remain indefinitely; no despawn/cleanup mechanism
