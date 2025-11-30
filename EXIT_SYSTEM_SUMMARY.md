# Exit System Implementation Summary

## What Was Built

A complete **room navigation system** with **custom exit display names** that separates the player-facing description from the internal room ID.

## Key Components

### 1. Exit Model (`src/Models/Exit.cs`)

```csharp
public class Exit
{
    public string DisplayName { get; set; }      // "North", "Into the Tavern"
    public string DestinationRoomId { get; set; } // actual room ID
    public string? Description { get; set; }     // flavor text
    public bool IsAvailable { get; set; }        // can use this exit?
    public string? UnavailableReason { get; set; }
}
```

**Why this design?** Separates what the player sees from the internal game data.

### 2. Enhanced Room Model (`src/Models/Room.cs`)

```csharp
public Dictionary<string, Exit> Exits { get; set; }  // Named exits

// Helper methods:
public List<Exit> GetAvailableExits()      // Only active exits
public Exit? FindExit(string displayName)  // Case-insensitive search
```

**Replaced**: The old `ConnectedRooms` list-based system

### 3. RoomBuilder Utility (`src/Utils/RoomBuilder.cs`)

Fluent API for easy room creation:

```csharp
new RoomBuilder("forest")
    .WithName("Dark Forest")
    .WithDescription("Towering trees...")
    .AddExit("North", "cave")
    .AddExit("South", "start", "Returns to town")
    .AddNPC("ranger")
    .WithMetadata("danger_level", 2)
    .Build()
```

### 4. Updated GameState (`src/Core/GameState.cs`)

- Uses Exit system for all default rooms
- New methods:
  - `MoveToRoomByExit(string exitName)` - navigate using display name
  - `MoveToRoom(string roomId)` - direct navigation (internal)
- Includes 5 example rooms with interconnected exits

### 5. Enhanced GameMaster (`src/Services/GameMaster.cs`)

- `HandleMove()` now uses exit names
- Shows available exits when player uses "look"
- Better error messages if exit doesn't exist
- Provides list of valid exits as suggestions

### 6. Console Interface Improvements

- `go <exit_name>` command for navigation
- `look` command shows available exits
- Case-insensitive exit matching
- Helpful error messages

## Example: Game in Action

```
> look

A bustling marketplace with vendors and adventurers.
You can go: North, East, South

You see: Market Vendor, Old Barrick

> go North
Moving...

You arrive at the Dark Forest...
You can go: South, Deeper Into The Forest
You see: Sylva the Ranger

> go Deeper Into The Forest
Moving...

You arrive at the Goblin Lair...
```

Notice how:
- Exit names are narrative ("North", "Deeper Into The Forest")
- Room IDs stay internal (forest, cave)
- Player never sees room IDs
- Exits are reusable (multiple rooms can have "Back to Town Square")

## Project Structure

```
src/
├── Core/
│   └── GameState.cs              (updated for exits)
├── Models/
│   ├── Exit.cs                   (NEW - exit model)
│   ├── Room.cs                   (updated)
│   ├── Character.cs              (unchanged)
│   ├── Item.cs                   (unchanged)
│   ├── Quest.cs                  (unchanged)
│   └── Inventory.cs              (unchanged)
├── LLM/
│   ├── OllamaClient.cs           (unchanged)
│   └── NpcBrain.cs               (unchanged)
├── Services/
│   └── GameMaster.cs             (updated for exits)
└── Utils/
    └── RoomBuilder.cs            (NEW - fluent builder)

Program.cs                         (updated for exits)

tools/
└── RoomDesigner/                 (NEW - Windows Forms tool)
    ├── RoomDesigner.csproj
    ├── Program.cs
    ├── RoomDesignerForm.cs       (main UI)
    └── InputDialog.cs            (helper dialogs)
```

## Features

### For Game Designers

✅ **Fluent Room Builder API**
- Easy programmatic room creation
- Type-safe exit management
- Chainable method calls

✅ **JSON Save/Load**
- Export worlds as JSON
- Import worlds from JSON
- Edit worlds in text editors

✅ **Windows Forms Designer** (Coming with tools/)
- Visual room creation
- Point-and-click exit management
- NPC assignment UI
- Metadata editor

### For Game Master (LLM)

✅ **Dynamic Exit Listing**
- Knows all available exits
- Shows player-friendly names
- Suggests exits if player input is invalid

✅ **Conditional Exits**
- Locked doors with reasons
- Dynamic exit creation during gameplay
- Quest-gated areas

### For Players

✅ **Narrative Navigation**
- Exits describe where you're going
- Case-insensitive commands
- Helpful error messages
- Visual exit list with `look`

## Example Worlds

### Small 5-Room Town
```
town_square ──north──> forest
    │                     │
  east                  deep
    │                     │
  tavern              goblin_cave
    │
  south
    │
merchant_stall
```

### Multi-Level Dungeon
```
entrance_hall
    ├─ down ──> level_2_corridor
    │               ├─ down ──> level_3_throne
    │               │
    │               └─ treasury
    │
    ├─ left ──> guard_barracks
    │
    └─ right ──> library
```

### Non-Linear Story
```
village_square ──many paths──> different_quest_areas
    │                               │
    └─ unique ───> convergence ─────┘
         rooms        point
```

## Testing

Run with test script:

```bash
cd C:\Devstuff\git\CSharpRPGBackend
cat test-exits.txt | dotnet run
```

The test demonstrates:
- Navigation using exit names
- Room descriptions
- Available exits listing
- NPC interaction
- Movement narration

## Code Examples

### Create a Room Programmatically

```csharp
var library = new RoomBuilder("library")
    .WithName("Grand Library")
    .WithDescription("Towering shelves...")
    .AddExit("North", "study", "Stairs lead up")
    .AddExit("South", "entrance", "Return to main hall")
    .AddExit("East", "restricted", "Locked section")
    .AddNPC("librarian")
    .WithMetadata("danger_level", 0)
    .Build();

gameState.Rooms["library"] = library;
```

### Navigate to an Exit

```csharp
// From console command "go north"
if (gameState.MoveToRoomByExit("north"))
{
    Console.WriteLine($"You arrive at {gameState.GetCurrentRoom().Name}");
}
else
{
    var room = gameState.GetCurrentRoom();
    var exits = room.GetAvailableExits();
    Console.WriteLine($"Can't go that way. Available: {string.Join(", ", exits.Select(e => e.DisplayName))}");
}
```

### Get Available Exits for Display

```csharp
var room = gameState.GetCurrentRoom();
var exits = room.GetAvailableExits();

foreach (var exit in exits)
{
    Console.WriteLine($"- {exit.DisplayName}" +
        (exit.Description != null ? $": {exit.Description}" : ""));
}
```

### Lock/Unlock Doors Dynamically

```csharp
var vaultExit = room.Exits["vault"];
vaultExit.IsAvailable = false;
vaultExit.UnavailableReason = "You need the vault key";

// Later when player has key:
vaultExit.IsAvailable = true;
```

## Comparison: Before vs After

### Before (Room IDs Only)
```
> move forest
You go to forest.

Available rooms: start, tavern, cave
```

### After (Narrative Exits)
```
> go North
You go North. You arrive at the Dark Forest.

You can go: South, Deeper Into The Forest
You see: Sylva the Ranger
```

## Migration Path

Old code using ConnectedRooms:
```csharp
if (currentRoom.ConnectedRooms.Contains(targetRoom))
    gameState.MoveToRoom(targetRoom);
```

New code using Exits:
```csharp
if (gameState.MoveToRoomByExit(exitName))
    // Success
```

## Next Steps

1. **Build More Complex Worlds**
   - Multi-level dungeons
   - Branching narratives
   - Non-linear quests

2. **Add Dynamic Elements**
   - Conditional exits (quest gates, locked doors)
   - Timed events (doors that close)
   - Environmental hazards

3. **Enhance NPC Interactions**
   - NPCs blocking exits
   - NPCs giving directions
   - Quest-specific paths

4. **Polish the Designer Tool**
   - Visual world map
   - Drag-and-drop connections
   - Room templates
   - Preview game in designer

5. **Create Adventure Modules**
   - Starter dungeon
   - Town exploration
   - Multi-chapter campaign

## Documentation

- `ROOM_DESIGN_GUIDE.md` - Comprehensive room design guide
- `ROOM_DESIGNER_QUICKSTART.md` - Designer tool tutorial
- `EXIT_SYSTEM_SUMMARY.md` - This file

## Performance

- Exit lookup: O(1) dictionary access
- Available exit filtering: O(n) where n = exits in room (usually < 10)
- No performance degradation vs old system

## Backward Compatibility

- Old `ConnectedRooms` property removed (breaking change)
- All old GameState calls updated
- Game logic tested with Granite 4 3B model

## Summary

The exit system transforms basic room navigation into a narrative experience where:
- Players see human-readable directions
- Designers can create complex world structures
- Game master can dynamically manage access
- Worlds can be designed with or without code

This foundation supports everything from simple 5-room demos to sprawling multi-level dungeons with branching quests.
