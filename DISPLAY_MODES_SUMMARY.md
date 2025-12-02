# Display Modes and Command Options - Complete Guide

## Overview
The RPG Backend now supports flexible display options for both one-time information queries and persistent UI preferences.

---

## 🎮 Display Mode (Persistent Footer Control)

Controls what information appears in the footer after EVERY action.

### Commands
- `display` - Show current mode
- `display minimal` - Minimal footer
- `display standard` - Standard footer (default)
- `display detailed` - Detailed footer (reserved)

### Display Modes

#### **Minimal** (`display minimal`)
```
📍 Location: Town Square
❤️ Health: 100/100
🚪 Exits: North, East, South
👥 NPCs Here: Merchant, Guard
```

#### **Standard** (`display standard`) - DEFAULT
```
📍 Location: Town Square
❤️ Health: 100/100
💰 Currency: 🪙 10 Gold, 🥈 50 Silver
🚪 Exits: North, East, South
👥 NPCs Here: Merchant, Guard
🎒 Inventory: Iron Sword, Health Potion x3, Leather Armor
```

#### **Detailed** (`display detailed`)
```
📍 Location: Town Square
❤️ Health: 100/100 | ⚔️ DMG: 12 | 🛡️ ARM: 8
💰 Currency: 🪙 10 Gold, 🥈 50 Silver
🚪 Exits: North, East, South
👥 NPCs Here: Merchant, Guard
🎒 Inventory: Iron Sword ⚔️, Health Potion x3, Leather Armor ⚔️
```

### Use Cases
- **Minimal**: Narrative-focused play, reduce UI clutter, speedruns
- **Standard**: Normal gameplay, convenient reference
- **Detailed**: Combat-focused play, quickly check total damage/armor at a glance

---

## 📊 Status Command (One-Time Information)

Query your current status without changing persistent display.

### Simple Status (`status`)
```
📍 Town Square
❤️ Health: 100/100
🚪 Exits: North, East, South
👥 NPCs: Merchant, Guard
```

### Advanced Status (`status detailed`, `status stats`, `show my stats`)
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
   Armor Rating: +10

💪 Total Damage: 12
🛡️  Total Armor: 10

⭐ Level: 3 | XP: 450
💰 Currency: 10 Gold, 50 Silver

🚪 Exits: North, South, East
👥 NPCs: Goblin, Bandit ☠️
═══════════════════════════════════
```

---

## 🎒 Inventory Command (One-Time Information)

Query your inventory without changing persistent display.

### Simple Inventory (`inventory`)
```
Inventory: Iron Sword x1, Health Potion x3, Leather Armor x1, Dragon Plate Armor x1
```

### Detailed Inventory (`inventory detailed`, `inventory equipped`, `show equipped`)
```
Inventory:
  • Iron Sword x1
  • Health Potion x3
  • Leather Armor x1
  • Dragon Plate Armor x1 ⚔️
  • Dragon Slayer Sword x1 ⚔️
```
**⚔️ icon indicates equipped items**

---

## 🔧 Implementation Details

### Modified Files
- `src/Services/GameMaster.cs`:
  - Added `GameStateDisplayMode` enum
  - Added `DisplayMode` property
  - Modified `BuildGameResponse()` to respect DisplayMode
  - Added `HandleDisplayMode()` for changing modes
  - Updated `HandleStatus()` with mode parameter
  - Updated `HandleInventory()` with mode parameter
  - Fixed all `Player.CarriedItems` → `PlayerInventory.Items` references

### Key Fixes
1. **Equipment Display Bug**: Changed all player equipment lookups from `_gameState.Player.CarriedItems` (NPC storage) to `_gameState.PlayerInventory.Items` (player storage)
2. **Informational Commands**: Status, inventory, and display commands now skip narration and return directly
3. **LLM Prompts**: Updated decision prompts to recognize command modifiers

---

## 💡 Usage Examples

### Scenario 1: Clean Narrative Experience
```
> display minimal
Display mode changed to: Minimal

> go north
You walk through the forest path...

📍 Dark Forest
❤️ Health: 100/100
🚪 Exits: North, South, East
👥 NPCs: Goblin
```

### Scenario 2: Check Detailed Stats Mid-Game
```
> status detailed
═══════════════════════════════════
📍 Location: Boss Arena
❤️  Health: 45/100
⚔️  Combat Stats:
   Strength: 16 (Base Damage: 8)
   ...
```

### Scenario 3: Verify Equipment
```
> inventory detailed
Inventory:
  • Steel Sword x1 ⚔️
  • Dragon Armor x1 ⚔️
  • Health Potion x5
  • Mana Potion x2
```

### Scenario 4: Normal Gameplay
```
> equip dragon armor
You don the heavy Dragon Plate Armor...

📍 Town Square
❤️ Health: 100/100
💰 Currency: 🪙 10 Gold
🚪 Exits: North, East, South
👥 NPCs: Blacksmith 🛒
🎒 Inventory: Sword, Dragon Plate Armor, Potion
```

---

## ⚡ Quick Reference

| Command | Result | Persistent? |
|---------|--------|-------------|
| `display minimal` | Change footer to minimal | ✅ Yes |
| `display standard` | Change footer to standard | ✅ Yes |
| `status` | Simple status check | ❌ No |
| `status detailed` | Advanced stats check | ❌ No |
| `inventory` | Simple inventory list | ❌ No |
| `inventory detailed` | Inventory with ⚔️ icons | ❌ No |

---

## 🚀 Future Enhancements

Potential additions (new "Verbose" display mode):
- Active quest objectives in footer
- Active buffs/debuffs
- Companion status
- Time/weather system
- Faction reputation
- Equipment durability

---

## What Changed from Before

**Previous Detailed Mode**: Same as Standard (no difference)
**New Detailed Mode**: Shows combined combat stats (Total Damage and Armor) directly on the health line

This makes it perfect for combat-heavy gameplay where you want to see your effective combat power at all times without running `status detailed`.

---

## 🐛 Bug Fixes in This Update

### Combat Damage Bug Fixed
- **Issue**: Displayed damage stats (DMG: 26) didn't match actual combat damage (~6)
- **Cause**: Combat system was looking for equipped items in `Character.CarriedItems` (NPC storage) instead of `PlayerInventory.Items` (player storage)
- **Fix**: Updated `CombatService.ResolveAttack()` to accept optional inventory parameter, passed `PlayerInventory.Items` for player combat
- **Result**: Combat damage now correctly uses equipped weapons and armor

### Equipment Display Fixed
- **Issue**: Equipped items weren't showing ⚔️ icons in inventory footer
- **Fix**: Updated `BuildGameResponse()` to check `EquipmentSlots` and add ⚔️ icons to equipped items
- **Result**: All inventory displays (footer, `inventory detailed` command) now show equipped status

### Crown of Amalion Missing
- **Issue**: Crown wasn't in dragon's loot despite being the main quest objective
- **Fix**: Added crown to dragon's `CarriedItems` in FantasyQuest.cs
- **Result**: Players can now loot the crown after defeating the dragon and complete the quest

---

## 📝 Summary of All Changes

1. ✅ **Display Mode System** - Control footer information density (Minimal/Standard/Detailed)
2. ✅ **Enhanced Status Commands** - Simple and detailed status with full combat breakdowns
3. ✅ **Enhanced Inventory Commands** - Simple and detailed inventory with equipped indicators
4. ✅ **Equipped Item Icons** - ⚔️ shows on all equipped items in all displays
5. ✅ **Detailed Mode Combat Stats** - Shows DMG and ARM on health line
6. ✅ **Combat Damage Fixed** - Equipment now properly affects combat damage
7. ✅ **Crown Quest Fixed** - Crown now properly lootable from dragon
