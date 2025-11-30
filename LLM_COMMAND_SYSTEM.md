# LLM-Powered Free-Form Command System

## Overview

The C# RPG Backend now features a completely LLM-driven command system that processes player input through natural language understanding. Instead of rigid command parsing, **all player actions go through the LLM**, allowing for flexible, natural-language gameplay.

## How It Works

### 1. Player Input → LLM Parsing

When a player enters a command, the GameMaster:

1. **Sends the command to the LLM** with full game context
2. **LLM parses the intent** into a structured action
3. **Game applies the action** using validated game logic
4. **LLM narrates the outcome** with vivid descriptions

### 2. Command Flow

```
Player Input
    ↓
GameMaster.ProcessPlayerActionAsync()
    ↓
ParseActionAsync() [LLM parses intent & context]
    ↓
ApplyAction() [Game logic validates & executes]
    ↓
NarrateOutcomeAsync() [LLM describes what happened]
    ↓
Response to Player
```

## Supported Actions

The LLM can parse and execute these action types:

| Action | Examples | Result |
|--------|----------|--------|
| **move** | "go north", "walk into the tavern", "head toward the forest" | Navigate to adjacent rooms via exits |
| **look** | "examine surroundings", "what's here?", "look around" | Describe current room, NPCs, exits |
| **inventory** | "check inventory", "what am I carrying?", "my items" | List inventory contents |
| **talk** | "talk to merchant", "speak with the guard", "chat with aldous" | Initiate dialogue with NPC |
| **examine** | "look at the sword", "inspect the merchant", "examine the potion" | Get detailed description of item/NPC |
| **take** | "pick up the sword", "grab the potion", "take the key" | Attempt to pick up an item |
| **drop** | "drop the sword", "leave the armor behind", "discard the potion" | Remove item from inventory |
| **use** | "drink the potion", "use the scroll", "activate the key" | Use consumables or special items |
| **attack** | "fight the goblin", "attack the enemy", "strike the dragon" | Combat action |
| **help** | "help", "commands", "what can I do?" | Show available actions |

## Natural Language Examples

### Movement
```
Player: "Head north through the forest path"
LLM:    → Parses as "move" with target "north"
Game:   → Moves player to forest room
Narration: "You push through the ancient trees, following the winding path north..."
```

### Examination
```
Player: "What's that merchant's name and what do they look like?"
LLM:    → Parses as "examine" with target "merchant"
Game:   → Retrieves merchant description from NPC
Narration: "The Merchant: A portly man with a friendly smile and weathered hands..."
```

### Item Usage
```
Player: "I'm hurt, let me drink one of those health potions"
LLM:    → Parses as "use" with target "health potion"
Game:   → Applies consumable effect, heals player, removes empty bottle
Narration: "You uncork the shimmering blue potion and gulp it down. Warmth spreads through you as your wounds close..."
```

### Teleportation
```
Player: "I want to use the scroll to get back home"
LLM:    → Parses as "use" with target "scroll"
Game:   → Checks if item is teleportation type, moves player to destination
Narration: "The ancient scroll glows with arcane energy. Reality warps and... you're back home!"
```

## Key Features

### 1. Context-Aware Parsing

The LLM receives full game context when parsing commands:

```
Current Location: Town Square
Available exits: North (Forest), East (Tavern), South (Market)
NPCs here: Merchant Aldous, Old Mage
Your inventory: Iron Sword, Health Potion, Door Key
Your health: 85/100
```

This means the LLM can:
- Understand "go tavern" even though the exact exit name is "East"
- Recognize "talk to the old one" refers to "Old Mage"
- Suggest available items when player says "use something"

### 2. Action Validation

Even though commands are natural language, the game still validates everything:

```csharp
// Player: "go to the purple castle"
// If castle doesn't exist as exit → "You can't go there"

// Player: "pick up a diamond"
// If no diamond in room → "You don't see a diamond here"

// Player: "use the teleport crystal"
// If player doesn't have it → "You don't have that item"
```

### 3. Intelligent Narration

The GameMaster provides contextual narration based on success/failure:

**Successful action:**
- Vivid description of what happened
- Atmospheric language matching the game style
- Updates to player state reflected

**Failed action:**
- Explanation of why it failed
- Suggestions for valid alternatives
- Encouragement to try something else

### 4. Item Type Handling

The system intelligently handles different item categories:

**Teleportation Items:**
```
Player: "activate the recall scroll"
→ Checks if item is teleportation type
→ Verifies destination room exists
→ Moves player instantly
→ Narrates the transport experience
```

**Consumables:**
```
Player: "drink the mana potion"
→ Checks if item is consumable
→ Applies consumable effects (heal, damage bonus, etc)
→ Decrements uses remaining
→ Removes item if uses exhausted
→ Narrates the effect
```

**Equipment:**
```
Player: "equip the sword"
→ Checks if item is equippable
→ Assigns to equipment slot
→ Updates combat stats
→ Narrates the equipping
```

## Behind the Scenes

### Parsing Prompt

When parsing commands, the LLM receives this instruction:

```
Parse the player's action into structured format. Understand natural language
commands flexibly. Respond with ONLY valid JSON:
{
  "action": "move|look|inventory|talk|examine|take|drop|use|attack|help|unknown",
  "target": "the name/direction the player referred to",
  "details": "additional context from their command"
}

Action meanings:
- move: travel via an exit (target is exit name or room name)
- look: examine surroundings
- talk: speak to an NPC (target is NPC name)
- examine: inspect something closely (target is item/NPC/object name)
- take/grab: pick up an item (target is item name)
- drop: remove from inventory (target is item name)
- use/drink/activate: use an item (target is item name)
- attack/fight: combat (target is NPC name)
- unknown: any action you cannot categorize
```

### Narration Prompt

After executing an action, the LLM narrates with this system prompt:

```
You are the Game Master for a fantasy RPG. Your role is to narrate game events vividly.
Describe outcomes in 1-2 sentences with atmosphere and drama.
Stay true to the player's action and game logic.
Use vivid, engaging language.
```

For failed actions, it adds:
```
The player attempted an action that failed or was unusual.
Describe what they tried and what happened in an engaging way.
```

## Command Processing Flow (Technical)

### Step 1: Receive Input
```csharp
// Program.cs - Main game loop
var playerInput = Console.ReadLine();
var actionNarration = await gameMaster.ProcessPlayerActionAsync(playerInput);
```

### Step 2: Parse with LLM
```csharp
// GameMaster.cs
private async Task<ActionPlan> ParseActionAsync(string playerCommand)
{
    // Build context (available exits, NPCs, inventory)
    var context = BuildGameContext();

    // Send to LLM with system prompt
    var response = await _ollamaClient.ChatAsync(
        systemPrompt: "Parse into JSON: action, target, details",
        userMessage: $"{context}\n\nPlayer command: {playerCommand}"
    );

    // Parse JSON response into ActionPlan
    return ParseActionJson(response);
}
```

### Step 3: Apply Action
```csharp
// GameMaster.cs
private ActionResult ApplyAction(ActionPlan plan)
{
    return plan.Action switch
    {
        "move" => HandleMove(plan.Target),
        "examine" => HandleExamine(plan.Target),
        "use" => HandleUse(plan.Target),
        // ... etc
    };
}
```

### Step 4: Narrate
```csharp
// GameMaster.cs
private async Task<string> NarrateOutcomeAsync(ActionPlan plan, ActionResult result)
{
    // Build rich context
    var context = $@"
Game State:
- Location: {room.Name}
- NPCs: {npcList}
- Inventory: {inventory}
- Player Action: {plan.Action}
- Result: {result.Message}";

    // LLM narrates with GM voice
    var narration = await _ollamaClient.ChatAsync(
        systemPrompt: _gmSystemPrompt,
        userMessage: $"Narrate this outcome:\n{context}"
    );

    return narration;
}
```

## New Handler Methods

### HandleExamine()
Allows players to examine items in inventory or NPCs in the room with detailed descriptions:

```csharp
// Player: "examine the sword"
// Response: "Iron Sword: A well-crafted iron blade (Damage: 10)"

// Player: "look at the merchant"
// Response: "Merchant Aldous: A portly man with a friendly smile and weathered hands..."
```

### HandleUse()
Intelligently handles different item types:

```csharp
// Teleportation items - instantly move to destination
// Consumables - apply effects and decrement uses
// Equipment - assign to equipment slot
// Other items - generic "can't use like that" response
```

### HandleUnknownAction()
For commands the LLM can't classify, passes to narration system for creative interpretation:

```csharp
// Player: "sing a song to the merchant"
// LLM: "You try singing, but your voice cracks. The merchant chuckles kindly..."
```

## Game Context Passed to LLM

Every command includes this information:

```
Current Location: [Room Name]
Available exits: [Exit Names]
NPCs here: [NPC Names]
Your inventory: [Item Names]
Your health: [Current/Max]
```

This allows the LLM to:
- Understand "go tavern" as navigating via the "East" exit to the tavern
- Autocomplete "talk to the m..." as "talk to merchant"
- Know which items are available to use/drop
- Understand spatial relationships in the game world

## Examples of Flexible Commands

All of these work and produce natural responses:

```
// Movement variations
"go north"
"head to the tavern"
"walk east through the gate"
"go back to town"
"follow the path north"

// Examination variations
"what's the merchant look like?"
"examine the potion"
"look at the sword more closely"
"tell me about the guard"
"describe that key"

// Item usage variations
"drink the potion"
"use the scroll"
"activate the recall crystal"
"consume the healing drink"
"use the teleportation device"

// Combat variations
"attack the goblin"
"fight the enemy"
"strike with the sword"
"battle the dragon"

// General variations
"what can I do?"
"show me the help"
"check my stuff"
"look around"
"what's here?"
```

## Error Handling

The system gracefully handles various error cases:

```
Player: "go to the moon"
LLM:    Parses as "move" with target "moon"
Game:   Checks available exits - moon doesn't exist
Result: "You can't go \"moon\". Available: north, east, south"

Player: "use the sword"
LLM:    Parses as "use" with target "sword"
Game:   Checks if sword is consumable/teleportation - it's not
Result: "You can't use Iron Sword like that."

Player: "drink the armor"
LLM:    Parses as "use" with target "armor"
Game:   Checks inventory - armor exists but isn't consumable
Result: "You can't use Steel Plate Armor like that."
```

## JSON Response Format

The LLM must respond with valid JSON in this format:

```json
{
  "action": "move",
  "target": "north",
  "details": "the player wants to go north"
}
```

If the response is invalid JSON, the system defaults to "unknown" action:

```csharp
catch (Exception)
{
    return new ActionPlan { Action = "unknown", Target = playerCommand, Details = "" };
}
```

## Game Modification Points

To extend the command system:

### Add New Item Type Handling
```csharp
// In HandleUse()
if (item.IsMyCustomType && item.CustomCondition)
{
    // Handle custom item behavior
    return new ActionResult { Success = true, Message = "..." };
}
```

### Add New Action Type
```csharp
// In ApplyAction()
"cast" => HandleCast(plan.Target),

// Add handler
private ActionResult HandleCast(string spell)
{
    // Implement spell casting
}
```

### Modify Parsing Behavior
```csharp
// In ParseActionAsync() - adjust context provided:
var context = $@"
Current Location: {currentRoom.Name}
Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}
NPCs here: {npcList}
Your inventory: {itemList}
Special status: {GetSpecialStatus()}"; // Add custom status
```

## Performance Considerations

- **One LLM call per action**: Every player command makes ~2 LLM calls (parse + narrate)
- **Context size**: Game context is ~200-500 tokens per command
- **Model choice**: Granite 4 3B is fast (~1-3 seconds per command)
- **Streaming**: Long narrations can be streamed for better UX

## Summary

The free-form command system makes the RPG feel alive and responsive. Players can:
- ✅ Use natural language without memorizing syntax
- ✅ Try creative actions that fail gracefully
- ✅ Have every action narrated by an LLM Game Master
- ✅ Experience flexible gameplay without rigid command structure
- ✅ Enjoy immersive dialogue and narration

The system validates all actions against game state, ensuring game integrity while maximizing player freedom.

---

**Next Steps:**
- Play the games with natural language commands
- Observe how the LLM interprets various inputs
- Add custom action types for your game mechanics
- Experiment with custom narration prompts for different game styles
