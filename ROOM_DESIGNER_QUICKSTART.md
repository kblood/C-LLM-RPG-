# Room Designer Tool - Quick Start

## Overview

The Room Designer is a Windows Forms application that lets you visually create and edit your game world without writing code.

## Building the Designer

```bash
cd C:\Devstuff\git\CSharpRPGBackend
dotnet build
```

The designer will compile as a separate application (currently requires manual setup in VS or your IDE).

## UI Layout

The designer has 3 main panels:

```
┌─────────────────┬──────────────────────┬─────────────────┐
│   Room List     │    Room Editor       │  Exits & NPCs   │
│   (Left)        │    (Center)          │   (Right)       │
├─────────────────┼──────────────────────┼─────────────────┤
│ • Town Square   │ Room ID: start       │ Exits:          │
│ • Tavern        │                      │ • North         │
│ • Forest        │ Name: Town Square    │ • East          │
│ • Cave          │                      │ • South         │
│                 │ Description:         │                 │
│ [New Room]      │ A bustling...        │ NPCs:           │
│ [Delete Room]   │                      │ • merchant      │
│                 │ Metadata:            │ • guard         │
│                 │ {...}                │                 │
│                 │                      │ [Add NPC]       │
│                 │ [Save Room]          │ [Remove NPC]    │
│                 │                      │                 │
│                 │                      │ [Add Exit]      │
│                 │                      │ [Edit Exit]     │
│                 │                      │ [Delete Exit]   │
└─────────────────┴──────────────────────┴─────────────────┘
```

## Workflow

### 1. Load or Create World

**File → Load World** to load existing `world_data.json`

OR start with the default example world (Town Square, Tavern, Forest, Cave)

### 2. Create a New Room

```
1. Click "New Room" in left panel
2. Enter room ID (e.g., "throne_room")
3. Click OK
4. Room appears in list
```

### 3. Edit Room Details

```
1. Select room from list
2. Edit Name: "Grand Throne Room"
3. Edit Description: "Gilded pillars support..."
4. Edit Metadata (JSON): {"danger_level": 2, "lighting": "torch"}
5. Click "Save Room"
```

### 4. Add Exits

```
1. Select room
2. Click "Add Exit"
3. Fill dialog:
   - Display Name: "Into the Throne Room"
   - Destination: Select from dropdown
   - Description: "Golden doors open"
4. Click OK
```

### 5. Manage NPCs

```
1. Select room
2. Click "Add NPC"
3. Select NPCs from list
4. Click OK

Or click "Remove NPC" to remove
```

### 6. Save World

```
File → Save All Rooms → Choose location → world_data.json
```

## Common Tasks

### Connect Two Rooms

```
Room A:
  Add Exit → "To Room B" → (select Room B)

Room B:
  Add Exit → "Back to Room A" → (select Room A)
```

### Make a Locked Door

```
In Room A:
  Add Exit → Display Name: "To Vault"
  (Set via code as IsAvailable = false initially)

OR use Metadata:
  {"locked": true, "key_required": "vault_key"}
```

### Create Multi-Level Dungeon

```
Level 1:
  - Add Exit "Down the Stairs" → level2_room1

Level 2:
  - Add Exit "Up the Stairs" → level1_room1
  - Add Exit "Deeper Into Dungeon" → level3_room1

Level 3:
  - Add Exit "Back Up" → level2_room1
  - Add Exit "Boss Chamber" → level3_boss
```

### Add Atmosphere with Metadata

```json
{
  "danger_level": 3,
  "lighting": "pitch_black",
  "temperature": "freezing",
  "ambient_sound": "wind_howling",
  "hostile_creatures": ["zombie", "skeleton"],
  "treasure_rating": 5
}
```

## Tips

### Exit Naming Convention

**Internal Key** (in dictionary):
```
"north" "east" "south" "west"        // Compass
"up" "down" "left" "right"           // Direction
"building_entrance" "path_forest"    // Location
```

**Display Name** (player sees):
```
"North"
"To the Forest"
"Into the Castle"
"Up the Stairs"
"Back to Town"
```

### Example Room Progression

```
Start → Town Square → Tavern → Tavern Rooms
         ↓
         Forest → Deep Forest → Goblin Cave
         ↓
         Market → Merchant Stall → Restricted Section
         ↓
         Temple → Holy Chamber → Underground Shrine
```

### Organizing Large Worlds

Group related rooms with naming:
```
forest_path
forest_clearing
forest_river
forest_cave
forest_ancient_tree

dungeon_level1_entrance
dungeon_level1_hall
dungeon_level1_treasury
dungeon_level2_entrance
dungeon_level2_hall
dungeon_level2_boss
```

## Troubleshooting

### Can't Add NPC

**Problem**: "No available NPCs to add"

**Solution**:
- Create NPCs in code first, or
- All NPCs already assigned to this room

### Exit Not Showing

**Problem**: Added exit but can't go there

**Solution**:
- Destination room doesn't exist in Rooms list
- Exit not properly saved (click "Save Room")

### World Won't Load

**Problem**: "Error loading..."

**Solution**:
- File is not valid JSON
- File permissions issue
- Try File → Save All Rooms first to create valid file

## Advanced: JSON Format

Saved world file structure:

```json
{
  "CurrentRoomId": "start",
  "Rooms": {
    "start": {
      "Id": "start",
      "Name": "Town Square",
      "Description": "...",
      "Exits": {
        "north": {
          "Id": "...",
          "DisplayName": "North",
          "DestinationRoomId": "forest",
          "Description": "...",
          "IsAvailable": true
        }
      },
      "NPCIds": ["merchant", "guard"],
      "Metadata": {"danger_level": 0}
    }
  },
  "NPCs": {
    "merchant": {
      "Id": "merchant",
      "Name": "Town Merchant",
      "Health": 50,
      "MaxHealth": 50,
      "Level": 1
    }
  }
}
```

You can edit this file directly in a text editor!

## Next Steps

1. **Create your first room** - Start with a simple 5-room world
2. **Add interconnected exits** - Make sure you can navigate between them
3. **Assign NPCs** - Use the NPC system to bring rooms alive
4. **Play your world** - Run the game and test your designs
5. **Iterate** - Use the designer to refine and expand

## Resources

- `ROOM_DESIGN_GUIDE.md` - Detailed room design documentation
- `README.md` - Overall project overview
- `test-exits.txt` - Example game session showing navigation
