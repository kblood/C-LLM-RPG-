# Status, Inventory, and Display Mode - Implementation Summary

## Changes Made

### 1. Simple Status Command (Default)
**Usage:** `status`

**Display:**
- 📍 Location name
- ❤️ Health bar (current/max)
- 🚪 Available exits
- 👥 NPCs in the room (with ☠️ for dead NPCs)

**Example Output:**
```
📍 Town Square
❤️ Health: 85/100
🚪 Exits: North, East, South
👥 NPCs: Merchant 🛒, Guard
```

### 2. Advanced Status Command
**Usage:** `status detailed`, `status advanced`, `status stats`, `status full`

**Display:**
- All simple status info, plus:
- ⚔️ Combat Stats:
  - Base Strength (with calculated base damage)
  - Base Agility (with crit % and dodge % modifiers)
  - Base Armor rating
- 🛡️ Equipment Bonuses:
  - Weapon damage bonus (if equipped)
  - Armor bonus from equipped items (if equipped)
  - Total damage (base + equipment)
  - Total armor (base + equipment)
- ⭐ Level and Experience
- 💰 Currency (if economy enabled)
- Formatted with borders for readability

**Example Output:**
```
═══════════════════════════════════
📍 Location: Dark Forest
❤️  Health: 85/100

⚔️  Combat Stats:
   Strength: 14 (Base Damage: 7)
   Agility: 12 (Crit: +2%, Dodge: +1.0%)
   Base Armor: 0

🛡️  Equipment Bonuses:
   Weapon Damage: +5
   Armor Rating: +8

💪 Total Damage: 12
🛡️  Total Armor: 8

⭐ Level: 3 | XP: 450
💰 Currency: 10 Gold, 50 Silver

🚪 Exits: North, South, East
👥 NPCs: Goblin, Bandit ☠️
═══════════════════════════════════
```

### 3. Simple Inventory Command (Default)
**Usage:** `inventory`

**Display:**
- Comma-separated list with quantities
- No indicators

**Example Output:**
```
Inventory: Iron Sword x1, Health Potion x3, Leather Armor x1
```

### 4. Detailed Inventory Command
**Usage:** `inventory detailed`, `inventory equipped`

**Display:**
- Bulleted list
- ⚔️ icon next to equipped items
- Item quantities

**Example Output:**
```
Inventory:
  • Iron Sword x1 ⚔️
  • Health Potion x3
  • Leather Armor x1 ⚔️
  • Gold Ring x1
```

## Implementation Details

### Modified Files
1. **GameMaster.cs:**
   - Updated `HandleStatus(string? mode)` - now accepts optional mode parameter
   - Updated `HandleInventory(string? mode)` - now accepts optional mode parameter
   - Updated action router to pass `plan.Target` to both methods
   - Updated `GetAvailableActions()` help text
   - Updated LLM decision prompt with examples

### How It Works
1. Player says "status" → LLM returns `{"action":"status","target":"","details":""}`
2. Player says "detailed status" → LLM returns `{"action":"status","target":"detailed","details":""}`
3. `HandleStatus(mode)` checks if mode is "detailed", "advanced", "stats", or "full"
4. If yes, shows full stats with equipment breakdowns
5. If no, shows simple health/exits/NPCs only

Same pattern for inventory commands.

### LLM Prompt Updates
Added to system prompt:
```
COMMAND MODIFIERS:
- 'status' has optional target: empty = simple status (health, exits, NPCs), 'detailed'/'stats'/'advanced' = full combat stats
- 'inventory' has optional target: empty = simple list, 'detailed'/'equipped' = show equipped items with ⚔️ icon
```

Added examples:
```
Player says 'status' -> [{"action":"status","target":"","details":""}]
Player says 'detailed status' OR 'stats' -> [{"action":"status","target":"detailed","details":""}]
Player says 'inventory' -> [{"action":"inventory","target":"","details":""}]
Player says 'detailed inventory' OR 'show equipped' -> [{"action":"inventory","target":"detailed","details":""}]
```

## Testing Recommendations

1. **Test Simple Status:**
   - Type: `status`
   - Expected: Simple output with health, exits, NPCs

2. **Test Advanced Status:**
   - Type: `detailed status` or `stats` or `show my stats`
   - Expected: Full breakdown with strength, agility, armor, equipment bonuses

3. **Test Simple Inventory:**
   - Type: `inventory`
   - Expected: Comma-separated list

4. **Test Detailed Inventory:**
   - Type: `detailed inventory` or `show equipped items`
   - Expected: Bulleted list with ⚔️ icons on equipped items

5. **Test in Combat:**
   - Both commands should work in combat mode
   - Status defaults to combat status display when in combat

## Display Mode Control

### Purpose
Control how much information is shown in the game state footer after each action.

### Available Modes

**Minimal Mode** (`display minimal`):
- Location
- Health
- Exits
- NPCs

**Standard Mode** (`display standard`) - DEFAULT:
- All Minimal info, plus:
- Currency (if economy enabled)
- Inventory

**Detailed Mode** (`display detailed`):
- Reserved for future features (currently same as Standard)

### Usage Examples
- `display minimal` - Switch to minimal footer
- `display standard` - Switch to standard footer
- `display` - Show current mode

### When to Use
- **Minimal**: Clean interface, less screen clutter, focus on narrative
- **Standard**: Normal gameplay, convenient access to inventory and currency

## Benefits

1. **Beginner-Friendly:** Default commands are simple and concise
2. **Power Users:** Can access detailed stats when needed
3. **Equipment Visibility:** Clear indication of what's equipped
4. **Combat Analysis:** Advanced status helps players optimize builds
5. **Natural Language:** Works with various phrasings (LLM handles interpretation)
6. **Customizable UI:** Adjust footer information density to preference
