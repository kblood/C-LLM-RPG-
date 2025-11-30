# Room & Exit Design Guide

## Overview

The C# RPG Backend now features a sophisticated room and exit system that allows for rich, descriptive navigation. Instead of using simple compass directions or room IDs, exits have **display names** that tell the player where they're going.

### Key Concepts

- **Rooms**: Locations in the game world with descriptions, NPCs, items, and exits
- **Exits**: Connections between rooms with custom display names (e.g., "North", "Into the tavern", "Through the gate")
- **Exit Display Names**: What the player sees (narrative descriptions of where they're going)
- **Destination Room ID**: The actual room the exit leads to (internal identifier)

## Exit System Architecture

### Exit Model

```csharp
public class Exit
{
    public string Id { get; set; }                    // Unique ID for this exit
    public string DisplayName { get; set; }           // "North", "Into the tavern", etc.
    public string DestinationRoomId { get; set; }    // Target room ID
    public string? Description { get; set; }         // Optional flavor text
    public bool IsAvailable { get; set; }            // Can player use this exit?
    public string? UnavailableReason { get; set; }   // "Door is locked", etc.
}
```

### Room Navigation

Rooms now have a dictionary of exits:

```csharp
public Dictionary<string, Exit> Exits { get; set; } = new();

// Helper methods:
public List<Exit> GetAvailableExits()              // Get only available exits
public Exit? FindExit(string displayName)          // Find exit by display name (case-insensitive)
```

### GameState Movement

```csharp
// Moves player using an exit name (narrative direction)
gameState.MoveToRoomByExit("North");
gameState.MoveToRoomByExit("Into the tavern");

// Moves player directly by room ID (internal use)
gameState.MoveToRoom("tavern");
```

## Creating Rooms Programmatically

### Using RoomBuilder (Fluent API)

The easiest way to create rooms:

```csharp
var room = new RoomBuilder("library")
    .WithName("Grand Library")
    .WithDescription("Towering bookshelves stretch to the ceiling, filled with ancient tomes and scrolls.")
    .AddExit("North", "study", "A narrow staircase leads upward to the study")
    .AddExit("South", "entrance", "The entrance doors are to the south")
    .AddExit("East", "restricted_section", "A locked door bars entry to the restricted section")
    .AddNPC("librarian")
    .AddNPC("scholar")
    .WithMetadata("lighting", "dim")
    .WithMetadata("danger_level", 1)
    .Build();

gameState.Rooms["library"] = room;
```

### Manual Creation

```csharp
var room = new Room
{
    Id = "tavern",
    Name = "The Rusty Tankard",
    Description = "A warm, inviting tavern smelling of ale and roasted meat.",
    Exits = new()
    {
        { "out", new Exit("Back to Town Square", "start") },
        { "upstairs", new Exit("Upstairs to Rooms", "tavern_rooms") }
    },
    NPCIds = new() { "bartender", "bard" }
};
```

## Exit Design Examples

### Directional Exits

```csharp
.AddExit("North", "forest")
.AddExit("East", "tavern")
.AddExit("South", "market")
.AddExit("West", "temple")
```

### Descriptive Exits (Recommended)

```csharp
.AddExit("Into the Forest", "forest", "A tree-lined path disappears into shadows")
.AddExit("To the Tavern", "tavern", "Warm light and laughter spill from the tavern doors")
.AddExit("Through the Market Stalls", "market", "The bustling marketplace lies ahead")
.AddExit("Up the Temple Steps", "temple", "Grand marble steps lead to the temple")
```

### Building Exits

```csharp
.AddExit("Into the Library", "library")
.AddExit("Down to the Cellar", "cellar")
.AddExit("Up to the Tower", "tower")
```

### Conditional Exits

```csharp
// Create an exit that's locked
var caveExit = new Exit("Into the Cave", "cave_entrance")
{
    IsAvailable = false,
    UnavailableReason = "The cave entrance is sealed by a massive stone door"
};

room.Exits["cave"] = caveExit;

// Later, unlock it
caveExit.IsAvailable = true;
```

## Displaying Exits to Players

When the player uses `look`:

```
You can go: North, East To The Tavern, South, Into The Forest
```

The Game Master automatically:
1. Lists all available exits with their display names
2. Shows NPCs in the room
3. Describes the room's appearance

## Player Navigation Commands

### Command Syntax

```
go <exit_name>
```

### Examples

```
> go North
> go Into the tavern
> go Through the gate
> go Back to town square
```

The system is case-insensitive:
```
> go north           ✓ Works
> go NORTH           ✓ Works
> go InTo ThE TaVeRn ✓ Works
```

## Windows Forms Designer Tool

### Location

Once built, the Room Designer will be available at:
```
tools/RoomDesigner/RoomDesigner.csproj
```

### Features

- **Room List**: Browse all rooms in the world
- **Room Editor**: Edit name, description, and metadata
- **Exit Management**: Add, edit, delete exits with GUI
- **NPC Management**: Assign NPCs to rooms
- **Save/Load**: Export and import world data as JSON
- **Metadata Editor**: Store custom room properties (danger level, lighting, etc.)

### Using the Designer

#### Create a New Room

1. Click "New Room"
2. Enter a room ID (e.g., "throne_room")
3. Click OK
4. Edit the name, description, and metadata
5. Click "Save Room"

#### Add an Exit

1. Select a room from the left panel
2. Click "Add Exit" in the right panel
3. Fill in:
   - **Display Name**: "Into the Throne Room" (what player sees)
   - **Destination**: Select target room from dropdown
   - **Description**: Optional flavor text (e.g., "Golden doors open before you")
4. Click OK

#### Add NPCs to a Room

1. Select a room
2. Click "Add NPC" in the right panel
3. Select NPCs from the list
4. Click OK

#### Save World

**File → Save All Rooms**
- Exports entire world as `world_data.json`
- Includes all rooms, exits, NPCs, metadata

#### Load World

**File → Load World**
- Import previously saved world data
- Merges with existing data

## Example: Building a Small Town

Using the RoomBuilder fluent API:

```csharp
// Town Square
var townSquare = new RoomBuilder("town_square")
    .WithName("Town Square")
    .WithDescription("A bustling marketplace at the heart of the town.")
    .AddExit("North", "forest", "A path leads north into the forest")
    .AddExit("East", "tavern", "The warm glow of the tavern to the east")
    .AddExit("South", "merchant_stall", "A merchant's stall to the south")
    .AddExit("West", "temple", "The temple rises majestically to the west")
    .AddNPCs("merchant", "guard")
    .WithMetadata("danger_level", 0)
    .Build();

// Tavern
var tavern = new RoomBuilder("tavern")
    .WithName("The Rusty Tankard Tavern")
    .WithDescription("A cozy tavern filled with the smell of ale and roasted meat.")
    .AddExit("Back to Town Square", "town_square")
    .AddExit("Upstairs", "tavern_rooms")
    .AddNPCs("bartender", "bard")
    .WithMetadata("danger_level", 0)
    .Build();

// Tavern Rooms
var tavernRooms = new RoomBuilder("tavern_rooms")
    .WithName("Tavern Guest Rooms")
    .WithDescription("Simple but clean rooms available for weary travelers.")
    .AddExit("Downstairs", "tavern")
    .WithMetadata("danger_level", 0)
    .Build();

// Forest
var forest = new RoomBuilder("forest")
    .WithName("Dark Forest")
    .WithDescription("A dense, mysterious forest with towering trees.")
    .AddExit("South", "town_square")
    .AddExit("Deeper Into The Forest", "cave", "A narrow path leads to a cave")
    .AddNPCs("ranger")
    .WithMetadata("danger_level", 2)
    .Build();

// Cave
var cave = new RoomBuilder("cave")
    .WithName("Goblin Lair")
    .WithDescription("A damp cave reeking of sulfur and danger.")
    .AddExit("Back To The Forest", "forest")
    .AddNPCs("goblin_shaman")
    .WithMetadata("danger_level", 3)
    .Build();

gameState.Rooms["town_square"] = townSquare;
gameState.Rooms["tavern"] = tavern;
gameState.Rooms["tavern_rooms"] = tavernRooms;
gameState.Rooms["forest"] = forest;
gameState.Rooms["cave"] = cave;
```

## Best Practices

### 1. Meaningful Exit Names

❌ **Bad:**
```csharp
.AddExit("door1", "room2")
.AddExit("path", "room3")
```

✅ **Good:**
```csharp
.AddExit("Into the Library", "library")
.AddExit("Through the Iron Gate", "castle")
.AddExit("Back to Town Square", "start")
```

### 2. Bidirectional Exits

Always make exits go both ways:

```csharp
// From Town Square to Forest
.AddExit("North to the Forest", "forest")

// From Forest back to Town Square
.AddExit("South to Town Square", "town_square")
```

### 3. Use Descriptions for Atmosphere

```csharp
.AddExit(
    "Into the Tavern",
    "tavern",
    "You hear laughter and the clinking of mugs. Warm light spills from the open door."
)
```

### 4. Organize Exit Keys Consistently

```csharp
// These are internal keys (not shown to player)
{ "north", new Exit("North", ...) }
{ "east", new Exit("East", ...) }
{ "building_entrance", new Exit("Through the Doors", ...) }
```

### 5. Use Metadata for World Logic

```csharp
.WithMetadata("danger_level", 1)
.WithMetadata("lighting", "torch")
.WithMetadata("temperature", "cold")
.WithMetadata("hostile_to", new[] { "undead" })
```

## Advanced Features

### Locked Doors

```csharp
var lockedExit = new Exit("Through the Vault Door", "vault")
{
    IsAvailable = false,
    UnavailableReason = "The vault door is sealed. You need a key."
};

room.Exits["vault"] = lockedExit;

// Later, in game logic:
if (player.HasItem("vault_key"))
{
    room.Exits["vault"].IsAvailable = true;
}
```

### Dynamic Exit Creation

```csharp
// Add exits during gameplay
void OpenNewPath(GameState gameState)
{
    var room = gameState.GetCurrentRoom();
    room.Exits["secret_passage"] = new Exit(
        "A Secret Passage Opens",
        "hidden_chamber",
        "A previously hidden passage appears before you!"
    );
}
```

### Room Metadata Queries

```csharp
var room = gameState.GetCurrentRoom();

if (room.Metadata.TryGetValue("danger_level", out var danger) && (int)danger > 2)
{
    Console.WriteLine("⚠️ This is a dangerous area!");
}

if (room.Metadata.TryGetValue("lighting", out var light) && light.ToString() == "dark")
{
    Console.WriteLine("It's pitch black. You need a light source.");
}
```

## Testing Your World

Run the game and test:

```
go <exit_name>    # Test navigation
look              # Verify exits display correctly
status            # Check available exits in status
```

## Saving and Sharing Worlds

Export your world:
```
File → Save All Rooms → world_data.json
```

This JSON can be:
- Shared with other developers
- Version controlled with git
- Modified externally
- Loaded into the game

## Next Steps

Now that you have a powerful room and exit system, you can:

1. **Add Complex Dungeons**: Multi-level structures with locked doors
2. **Create Quests**: NPCs that refer to exits and locations
3. **Build Story Branches**: Different areas based on game state
4. **Design Puzzles**: Conditional exits that unlock based on inventory
5. **Populate with NPCs**: Use the NPC assignment feature to bring rooms alive

The Room Designer tool makes all of this easy without writing code!
