# Interactive Game Demo - What You'll See

## Running the Game

```bash
dotnet run
```

Then select game `1` for Fantasy Quest.

---

## Sample Interactive Session

### Game Start

```
=== C# RPG Backend - Game Selector ===

Available Games:
1. Fantasy Quest - The Dragon's Hoard
2. Sci-Fi Adventure - Escape from Station Zeta

Select a game (1-2): 1

Loading: The Dragon's Hoard

Long ago, the kingdom of Amalion prospered under the protection of the Crown,
a magical artifact imbued with ancient power. But a dragon, drawn by its magic,
descended upon the kingdom and stole it away.

Now the realm suffers. Crops fail. Animals sicken. The people cry out for a hero.

You have answered that call. Your journey begins in the small border town of Ravensholm...

Press any key to begin...

=== The Dragon's Hoard ===
This is a console-based adventure with LLM-powered NPCs.

Type 'help' for available actions.
```

---

### First Turn - Looking Around

```
╔════════════════════════════════════════╗
║ Ravensholm Town Square                 ║
╚════════════════════════════════════════╝
❤️  Health: 100/100
🚪 Exits: North, East, South
👥 NPCs: Gruff the Blacksmith, Herald Aldous
🎒 Inventory: Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor

> What do you do? look

⏳ Processing your action...

You find yourself in the bustling Ravensholm Town Square, where merchants hawk
their wares and townsfolk gather around the central fountain. The air is filled
with the scent of fresh bread and the sound of hammer strikes from Gruff's forge
to your west. Herald Aldous stands tall near the fountain, observing the crowd
with a watchful eye. The tavern's warm glow beckons from the east, while the
market sprawls to the south. A forest path leads northward toward distant mountains.

---

📍 **Location:** Ravensholm Town Square
❤️ **Health:** 100/100
🚪 **Exits:** North, East, South
👥 **NPCs Here:** Gruff the Blacksmith, Herald Aldous
🎒 **Inventory:** Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor
```

---

### Second Turn - Talking to NPC

```
╔════════════════════════════════════════╗
║ Ravensholm Town Square                 ║
╚════════════════════════════════════════╝
❤️  Health: 100/100
🚪 Exits: North, East, South
👥 NPCs: Gruff the Blacksmith, Herald Aldous
🎒 Inventory: Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor

> What do you do? talk to the blacksmith

⏳ Processing your action...

Gruff grunts as he wipes sweat from his brow. "Aye, what brings ye to my forge?
Looking for a weapon, or just passing through?" He eyes your sword with approval.
"That's a fine blade ye have there, though if ye ever need repairs or upgrades,
I'm your man. The mountains to the north are dangerous - folk say there's a dragon
up there now. Best be careful if ye venture that way."

---

📍 **Location:** Ravensholm Town Square
❤️ **Health:** 100/100
🚪 **Exits:** North, East, South
👥 **NPCs Here:** Gruff the Blacksmith, Herald Aldous
🎒 **Inventory:** Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor
```

---

### Third Turn - Moving to New Location

```
╔════════════════════════════════════════╗
║ Ravensholm Town Square                 ║
╚════════════════════════════════════════╝
❤️  Health: 100/100
🚪 Exits: North, East, South
👥 NPCs: Gruff the Blacksmith, Herald Aldous
🎒 Inventory: Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor

> What do you do? go east to the tavern

⏳ Processing your action...

You push open the heavy wooden doors and step into the warmth of the tavern.
The smell of ale and roasted meat fills your nostrils. A crackling fire warms
the hearth, and patrons sit at scattered tables, nursing their drinks and
exchanging stories. The bartender nods in greeting as you approach. In the corner,
you notice an old mage studying an ancient tome, seemingly lost in thought.

---

📍 **Location:** Tavern
❤️ **Health:** 100/100
🚪 **Exits:** South
👥 **NPCs Here:** Old Mage, Tavern Keeper
🎒 **Inventory:** Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor
```

---

### Fourth Turn - Using an Item

```
╔════════════════════════════════════════╗
║ Tavern                                 ║
╚════════════════════════════════════════╝
❤️  Health: 100/100
🚪 Exits: South
👥 NPCs: Old Mage, Tavern Keeper
🎒 Inventory: Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor

> What do you do? examine the Dragon Slayer Sword

⏳ Processing your action...

The Dragon Slayer Sword gleams with ethereal blue light as you hold it up to
examine it closely. The blade is perfectly balanced, and ancient runes along
the hilt seem to pulse with arcane power. This is clearly a legendary weapon,
forged specifically for slaying dragons. Even just holding it fills you with
confidence and determination.

---

📍 **Location:** Tavern
❤️ **Health:** 100/100
🚪 **Exits:** South
👥 **NPCs Here:** Old Mage, Tavern Keeper
🎒 **Inventory:** Iron Sword, Wooden Staff, Dragon Slayer Sword, Leather Armor, Dragon Plate Armor
```

---

## What You See Each Turn

### Before Input
```
╔════════════════════════════════════════╗
║ [Current Room Name]                    ║
╚════════════════════════════════════════╝
❤️  Health: 100/100
🚪 Exits: [Available directions]
👥 NPCs: [Who's here]
🎒 Inventory: [Your items]
```

### After Action
```
[LLM-generated narration of what happens]

---

📍 **Location:** [New location]
❤️ **Health:** [Updated health]
🚪 **Exits:** [New available exits]
👥 **NPCs Here:** [NPCs in this room]
🎒 **Inventory:** [Updated inventory]
```

---

## Key Features

✅ **Clear Status Display**
- Room name in formatted box
- Health always visible
- Available exits clearly listed
- NPCs in room shown
- Full inventory displayed

✅ **Natural Language Input**
- "go north" or "go to the tavern"
- "talk to the blacksmith"
- "examine the sword"
- "look around"
- "check my inventory"

✅ **LLM-Powered Responses**
- Every action narrated by the AI
- Responses vary each time
- Context-aware dialogue
- Atmospheric descriptions

✅ **Game State Tracking**
- Health updates
- Inventory changes
- Location changes
- NPC interactions tracked

---

## Available Commands

Type these at the prompt:

- **`look`** - Examine your surroundings
- **`go [direction]`** - Travel (e.g., "go north", "go to the tavern")
- **`talk [npc]`** - Speak with someone (e.g., "talk to the merchant")
- **`examine [item]`** - Look closely at something
- **`inventory`** - Check your items
- **`use [item]`** - Use something (potion, scroll, etc.)
- **`help`** - Show available actions
- **`quit`** - Exit the game

Or just use natural language:
- "examine the Dragon Slayer Sword"
- "drink the healing potion"
- "go east into the tavern"
- "attack the goblin"

---

## Tips for Playing

1. **Explore Thoroughly** - Talk to NPCs, examine items, explore different rooms
2. **Use Natural Language** - The LLM understands various phrasings
3. **Pay Attention to Exits** - They show you where you can go
4. **Manage Your Inventory** - Keep track of what you have
5. **Talk to NPCs** - They provide clues and can join your party
6. **Use Special Items** - Keys unlock doors, scrolls teleport you, potions heal

---

## Example Complete Session

**Turn 1:** Look around, talk to NPC
**Turns 2-5:** Explore town, gather items
**Turns 6-10:** Head north to forest
**Turns 11-15:** Solve puzzle or get key
**Turns 16-20:** Travel to cave or dungeon
**Turns 21-30:** Final confrontation
**Victory:** Reach win condition!

---

## Now You Can Play!

The game now displays all important information each turn:

✅ Current location (clearly named)
✅ Health status
✅ Available exits to explore
✅ NPCs present to interact with
✅ Full inventory contents
✅ Vivid narration of your actions

Enjoy your adventure! 🎮
