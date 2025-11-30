# Automated Gameplay & Replay System

## Overview

The C# RPG Backend now includes an **automated game player** that can play both games independently and log everything to markdown files! This allows you to:

- ✅ Watch the LLM play the game intelligently
- ✅ See full gameplay logs with every decision
- ✅ Review game strategies and NPC interactions
- ✅ Understand how the LLM interprets game situations
- ✅ Share gameplay recordings as markdown files

## How It Works

### GameReplay Service

The `GameReplay` class automatically:

1. **Generates Player Actions** - Uses LLM to decide what to do next
2. **Executes Actions** - Processes commands through the game engine
3. **Logs Everything** - Records all output to markdown
4. **Continues Until Victory/Defeat** - Plays until win condition or death

### LLM Decision Making

Each turn, the automated player receives:

```
Current Status:
- Location: [Room Name]
- Description: [What's here]
- Available exits: [Where to go]
- NPCs here: [Who's present]
- Inventory: [What you have]
- Health: [Current/Max]

Goal: [Game Objective]
```

The LLM then decides the next action strategically, considering:
- Where to explore next
- Which NPCs to talk to
- What items to examine
- When to use special items
- Overall progress toward the goal

## Running Automated Gameplay

### Command Line

To play both games automatically and generate replay files:

```bash
dotnet run replay
```

This will:
1. Play **Fantasy Quest** - generates `REPLAY_Fantasy_Quest.md`
2. Play **Sci-Fi Adventure** - generates `REPLAY_Sci-Fi_Adventure.md`
3. Save both to the project directory

### Interactive Mode (Default)

To play manually as usual:

```bash
dotnet run
```

Or simply:

```bash
dotnet run
```

## Output Format

### Replay Markdown File Structure

Each replay file contains:

```markdown
# Fantasy Quest - Game Replay

**Game Style:** Fantasy
**Date:** 2024-01-15 14:30:45
**Description:** A fantasy quest game where you must defeat the dragon...

---

## Game Start

> **Narrator:** Once upon a time, in the kingdom of Amalion...

> **Objective:** Defeat the dragon and recover the Crown of Amalion

### Turn 1

**Location:** Town Square
**Health:** 100/100

> **Player:** examine the area

> **Narrator:** You look around the bustling town square...

### Turn 2

**Location:** Town Square
**Health:** 100/100

> **Player:** talk to the blacksmith

> **Narrator:** The blacksmith greets you warmly...

## 🎉 Victory!

The player has reached the goal!

---

*Replay generated on 2024-01-15 14:30:45*
```

## Understanding the Logs

### Player Actions (Natural Language)

```
> **Player:** go north through the forest
> **Player:** examine the merchant's wares
> **Player:** drink the health potion
> **Player:** use the teleport scroll to return home
```

These show the LLM's decisions in natural language - exactly how a human player might phrase actions.

### Narrator Responses

```
> **Narrator:** You push through the ancient trees, the path becoming clearer...
> **Narrator:** The merchant shows you several interesting items...
> **Narrator:** Warmth spreads through your body as your wounds close...
```

These are LLM-generated narrations describing outcomes vividly and immersively.

### Game State Updates

Each turn shows:
- **Location** - Current room
- **Health** - Player status

This lets you track progress and see when the player is in danger.

## Example: What to Expect

### Turn-by-Turn Gameplay

A typical game flow might look like:

```
Turn 1: Explore starting location, look around
Turn 2: Talk to NPCs to understand objectives
Turn 3-5: Gather items and equipment
Turn 6-10: Explore adjacent rooms
Turn 11-15: Use keys to unlock new areas
Turn 16-20: Build relationships with NPCs
Turn 21+: Execute final strategy toward goal
```

### LLM Strategy

The automated player demonstrates intelligent behavior:

1. **Exploration**: Systematically explores new areas
2. **Information Gathering**: Talks to NPCs for clues
3. **Resource Collection**: Finds and uses items strategically
4. **Problem Solving**: Uses keys, scrolls, and consumables appropriately
5. **Goal Orientation**: Works toward stated objectives

## File Locations

Generated replay files appear in the project root:

```
CSharpRPGBackend/
├── REPLAY_Fantasy_Quest.md
├── REPLAY_Sci-Fi_Adventure.md
├── Program.cs
├── ... other files
```

You can move or share these markdown files independently!

## Customizing Replay Games

### Changing Max Turns

Edit `Program.cs` in `RunReplayMode()`:

```csharp
var logContent = await replay.PlayGameAsync(maxTurns: 30);  // Change 30 to desired value
```

### Changing Games Played

In `Program.cs`:

```csharp
var games = new[]
{
    ("Fantasy Quest", FantasyQuest.Create()),
    ("Sci-Fi Adventure", SciFiAdventure.Create()),
    ("Custom Game", CustomGame.Create())  // Add custom games here
};
```

### Modifying Action Generation

Edit `GameReplay.cs` `GeneratePlayerActionAsync()` method:

```csharp
var context = $@"You are playing a {_game.Style} RPG game. Your goal: {_game.GameObjective}
...
// Add custom prompt instructions here
```

## Advanced: Understanding LLM Decision-Making

### Action Context Provided

```
Current Location: Dark Forest
Description: Ancient trees block out the sky...
Available exits: South (Back to Path), Deep (Into Cave)
NPCs here: Ranger
Inventory: Iron Sword, Health Potion
Health: 75/100

Based on the game state, what should you do next?
```

### What the LLM Considers

When generating the next action, the LLM thinks about:

1. **Safety**: Avoid dangerous areas if health is low
2. **Exploration**: Visit unexplored exits
3. **Relationships**: Talk to NPCs who haven't been encountered
4. **Items**: Examine and use special items
5. **Progress**: Work toward the stated objective
6. **Strategy**: Make logical decisions about sequencing

### Example Decision Flow

```
Observation: "I'm in the town square with low health (30/100)"
Decision: "I should find healing - a health potion or healer"
Action: "go to the tavern to look for a healer"

Observation: "The healer asks about my quest"
Decision: "I should gather information about the next steps"
Action: "talk to the healer about the dragon's location"

Observation: "The healer mentioned a cave with treasure"
Decision: "I should get better equipment before confronting the dragon"
Action: "examine the sword here"
```

## Technical Details

### GameReplay Class

Located in `src/Services/GameReplay.cs`:

```csharp
public class GameReplay
{
    // Plays game with automated LLM player
    public async Task<string> PlayGameAsync(int maxTurns = 50)

    // Generates the next action
    private async Task<string> GeneratePlayerActionAsync(Room currentRoom)

    // Saves log to markdown file
    public async Task SaveLogAsync(string filePath)
}
```

### Playback Flow

```
PlayGameAsync()
  ├─ Initialize log with game metadata
  ├─ Loop until victory/defeat/max turns:
  │   ├─ GeneratePlayerActionAsync() [LLM decides next action]
  │   ├─ ProcessPlayerActionAsync() [Game executes action]
  │   ├─ Check win condition
  │   ├─ Check death condition
  │   └─ Log turn details
  └─ SaveLogAsync() [Write to markdown file]
```

## Sharing Replays

### Markdown Files Are Portable

Once generated, replay files can be:
- ✅ Shared via email, Discord, GitHub
- ✅ Embedded in documentation
- ✅ Posted on forums
- ✅ Converted to HTML for websites
- ✅ Compared side-by-side (different LLM runs)

### Example Sharing

```markdown
Check out this automated Fantasy Quest playthrough:
- Generated: 2024-01-15
- Turns: 24
- Result: Victory!
- [View Replay →](REPLAY_Fantasy_Quest.md)
```

## Troubleshooting

### Replay Not Generated

**Problem:** Running `dotnet run replay` but no files appear

**Solutions:**
1. Check Ollama is running: `ollama serve`
2. Check internet connection to Ollama
3. Verify `granite4:3b` model is available
4. Check file permissions in project directory

### Game Stops Early

**Problem:** Replay ends before max turns

**Causes:**
- Player died (health reached 0)
- Player won (reached goal room)
- An error occurred (check console output)

**Fix:** Check the markdown file - it will show why the game ended

### Poor Action Decisions

**Problem:** LLM makes silly choices

**Improvement:** Modify the prompt in `GeneratePlayerActionAsync()` to add more strategic guidance

## Future Enhancements

Possible improvements to the replay system:

- [ ] Save/load game state to replay from specific turns
- [ ] Compare replays from different LLM models
- [ ] Track statistics (turns taken, items used, NPCs met)
- [ ] Generate summary statistics
- [ ] Create replay videos with ASCII art
- [ ] Multi-player replays (simultaneous players)
- [ ] Branching paths (what if player chose differently?)

## Summary

The automated gameplay system lets you:

1. **Watch the LLM play** - See how AI handles your game
2. **Generate documentation** - Create proof-of-concept demos
3. **Test game design** - Identify exploits or dead-ends
4. **Archive gameplay** - Keep records of different playthroughs
5. **Share experiences** - Show others how games work

Simply run:
```bash
dotnet run replay
```

And get complete gameplay logs in markdown format! 🎮📝
