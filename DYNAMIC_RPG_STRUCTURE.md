# Dynamic RPG JSON Structure Design

## Overview

A hierarchical JSON-based system for defining complete RPG worlds, campaigns, and stories. This allows non-programmers to create rich RPG experiences without touching C# code.

## Directory Structure

```
games/
├── [game-name]/
│   ├── game.json              # Game metadata & settings
│   ├── story.json             # Campaign story & acts
│   ├── rooms/
│   │   ├── room-1.json
│   │   ├── room-2.json
│   │   └── ...
│   ├── npcs/
│   │   ├── npc-1.json
│   │   ├── npc-2.json
│   │   └── ...
│   ├── items/
│   │   ├── item-1.json
│   │   ├── item-2.json
│   │   └── ...
│   ├── quests/
│   │   ├── quest-1.json
│   │   └── ...
│   └── dialogue/              # Optional: pre-written dialogue trees
│       └── dialogue-1.json
```

## JSON Schema Definitions

### 1. game.json - Game Metadata & Settings

```json
{
  "id": "sci-fi-escape",
  "title": "Escape from Station Zeta",
  "subtitle": "A Science Fiction Thriller",
  "version": "1.0.0",
  "description": "Battle your way through a space station to escape alien invaders",

  "gameSettings": {
    "startingRoomId": "crew_quarters_cell",
    "winConditionRoomIds": ["escape_pod"],
    "maxTurns": 100,
    "playerStartingHealth": 100,
    "playerStartingLevel": 1,
    "aiModel": "granite4:3b",
    "ollamaUrl": "http://localhost:11434"
  },

  "style": {
    "theme": "sci-fi",
    "tonality": "action-horror",
    "narratorVoice": "cinematic"
  },

  "features": {
    "enableCombat": true,
    "enableDialogue": true,
    "enableQuests": true,
    "enableLooting": true,
    "enableEquipment": true,
    "enableNPCPatrol": false,
    "enableDynamicDifficulty": false
  },

  "startingItems": [
    { "itemId": "laser_pistol", "quantity": 1 },
    { "itemId": "combat_suit", "quantity": 1 }
  ],

  "metadata": {
    "author": "Your Name",
    "createdDate": "2025-11-30",
    "lastModified": "2025-11-30",
    "tags": ["sci-fi", "action", "space"]
  }
}
```

### 2. story.json - Campaign Story & Structure

```json
{
  "id": "escape-campaign",
  "title": "The Great Escape",
  "description": "A three-act story of survival",

  "acts": [
    {
      "id": "act-1",
      "title": "The Awakening",
      "description": "You wake up in your cell. The station is under attack.",
      "startingRoomId": "crew_quarters_cell",
      "completionCriteria": {
        "type": "reach_room",
        "roomId": "security_armory"
      },
      "briefing": "Escape your quarters and reach the armory to gather weapons.",
      "objectives": [
        {
          "id": "obj-1",
          "title": "Escape Your Cell",
          "description": "Find a way out of the locked quarters",
          "optional": false
        },
        {
          "id": "obj-2",
          "title": "Reach the Armory",
          "description": "Navigate to the security armory",
          "optional": false
        }
      ]
    },
    {
      "id": "act-2",
      "title": "The Gathering",
      "startingRoomId": "security_armory",
      "completionCriteria": {
        "type": "complete_quest",
        "questId": "gather_allies"
      },
      "briefing": "Gather allies and resources for the final push.",
      "objectives": []
    },
    {
      "id": "act-3",
      "title": "The Escape",
      "startingRoomId": "main_corridor_a",
      "completionCriteria": {
        "type": "reach_room",
        "roomId": "escape_pod"
      },
      "briefing": "Reach the escape pod and leave the station.",
      "objectives": []
    }
  ],

  "globalObjectives": [
    {
      "id": "survive",
      "title": "Survive",
      "description": "Stay alive throughout the mission"
    }
  ]
}
```

### 3. rooms/room-[id].json - Location Definitions

```json
{
  "id": "medical_bay",
  "name": "Medical Bay",
  "description": "A sterile medical facility with surgical equipment, diagnostic beds, and medicine cabinets. The air smells of antiseptic and recycled oxygen.",
  "type": "indoor",

  "exits": [
    {
      "id": "exit-1",
      "displayName": "Back To Corridor",
      "direction": "west",
      "destinationRoomId": "main_corridor_a",
      "description": "Return to the main corridor",
      "requiresItem": null,
      "requiresKey": null,
      "locked": false
    }
  ],

  "npcs": [
    {
      "npcId": "dr_chen",
      "spawnOnEnter": true,
      "hostility": "neutral",
      "approachMessage": "Dr. Chen looks up from the medical console"
    }
  ],

  "items": [
    {
      "itemId": "medical_supplies",
      "spawnOnEnter": true,
      "quantity": 3,
      "pickupMessage": "You gather the medical supplies"
    }
  ],

  "ambiance": {
    "soundscape": "beeping-medical-equipment",
    "lightingLevel": "bright",
    "temperature": "cool"
  },

  "hazards": [
    {
      "id": "radiation",
      "type": "radiation",
      "damagePerTurn": 5,
      "avoidable": true,
      "avoidDescription": "Avoid the irradiated section"
    }
  ],

  "metadata": {
    "difficulty": "medium",
    "tags": ["medical", "science"],
    "visitedCount": 0
  }
}
```

### 4. npcs/npc-[id].json - NPC Definitions

```json
{
  "id": "dr_chen",
  "name": "Dr. Sarah Chen",
  "title": "Medical Officer",
  "description": "A skilled medical officer with a calm demeanor despite the chaos. She wears a bloodstained lab coat.",
  "portrait": "A woman with dark hair, wearing a white lab coat",

  "stats": {
    "health": 60,
    "maxHealth": 60,
    "level": 2,
    "experience": 0,
    "strength": 8,
    "agility": 10,
    "armor": 2
  },

  "role": "healer",
  "alignment": "good",

  "personality": {
    "personalityPrompt": "You are a compassionate doctor who values life. You're cautious but willing to help those in need. You speak softly but with authority.",
    "traits": ["compassionate", "cautious", "knowledgeable"],
    "favoriteTopics": ["medicine", "survival", "ethics"],
    "dislikedTopics": ["violence", "cruelty"]
  },

  "location": {
    "currentRoomId": "medical_bay",
    "homeRoomId": "medical_bay",
    "patrolRoomIds": null,
    "patrolIntervalTurns": null,
    "canMove": true,
    "canJoinParty": true
  },

  "inventory": [
    {
      "itemId": "combat_stimulant",
      "quantity": 2,
      "loot": true
    },
    {
      "itemId": "nanobot_repair_kit",
      "quantity": 1,
      "loot": true
    }
  ],

  "dialogue": {
    "greeting": "Hello, I'm Dr. Chen. Are you here for medical assistance?",
    "farewell": "Good luck out there. Stay safe.",
    "defaultResponse": "I'm not sure I can help with that.",
    "reactions": {
      "onPlayerHurt": "You're injured! Let me help you.",
      "onPlayerHealed": "You should be better now.",
      "onCombatStart": "No, violence won't solve this!"
    }
  },

  "relationships": {
    "allies": ["commander_martinez"],
    "enemies": [],
    "neutral": ["security_chief"]
  },

  "quests": [
    {
      "questId": "save_dr_chen",
      "questType": "companion",
      "offered": true
    }
  ],

  "behaviors": {
    "combatStyle": "defensive",
    "aiAggression": "low",
    "fleePath": "medical_bay"
  }
}
```

### 5. items/item-[id].json - Item Definitions

```json
{
  "id": "laser_pistol",
  "name": "Laser Pistol",
  "description": "A compact energy weapon that fires concentrated laser beams",
  "type": "weapon",
  "rarity": "common",

  "stats": {
    "damageBonus": 6,
    "armorBonus": 0,
    "criticalChance": 0
  },

  "equipment": {
    "isEquippable": true,
    "equipmentSlot": "main_hand",
    "weight": 2.5,
    "durability": 100,
    "maxDurability": 100
  },

  "effects": {
    "onEquip": "You feel the familiar weight of the laser pistol in your hand",
    "onUse": "You fire the laser pistol",
    "onUnequip": "You holster the laser pistol"
  },

  "requirements": {
    "minLevel": 1,
    "minStrength": 8,
    "restrictions": null
  },

  "value": {
    "goldValue": 100,
    "sellPrice": 75
  },

  "metadata": {
    "tags": ["weapon", "energy", "sci-fi"],
    "craftable": false,
    "tradeable": true
  }
}
```

### 6. quests/quest-[id].json - Quest Definitions

```json
{
  "id": "gather_allies",
  "title": "Gather Allies",
  "description": "Recruit help from other survivors on the station",
  "giver": "commander_martinez",
  "giverDialog": "We need to work together if we're going to survive this.",

  "type": "companion",
  "difficulty": "medium",
  "experience_reward": 500,
  "item_rewards": [
    {
      "itemId": "access_card",
      "quantity": 1
    }
  ],

  "objectives": [
    {
      "id": "find_dr_chen",
      "title": "Find Dr. Chen",
      "description": "Locate Dr. Sarah Chen in the Medical Bay",
      "type": "location",
      "targetRoomId": "medical_bay",
      "optional": false,
      "completed": false
    },
    {
      "id": "recruit_dr_chen",
      "title": "Recruit Dr. Chen",
      "description": "Convince Dr. Chen to join your cause",
      "type": "dialogue",
      "targetNpcId": "dr_chen",
      "optional": false,
      "completed": false,
      "requiresPreviousObjective": "find_dr_chen"
    }
  ],

  "completionCriteria": {
    "type": "recruit_npcs",
    "requiredNpcs": ["dr_chen", "chief_engineer"],
    "minRequired": 2
  },

  "timeLimit": null,
  "repeatable": false,
  "prerequisites": [
    {
      "type": "complete_quest",
      "questId": "escape_quarters"
    }
  ],

  "failConditions": [
    {
      "type": "npc_death",
      "npcId": "commander_martinez"
    }
  ]
}
```

## C# Data Classes to Load JSON

```csharp
// In src/Models/JsonGame.cs
[JsonSerializable]
public class GameDefinition
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Subtitle { get; set; }
    public GameSettings GameSettings { get; set; }
    public StyleSettings Style { get; set; }
    public Dictionary<string, bool> Features { get; set; }
    public List<StartingItem> StartingItems { get; set; }
}

[JsonSerializable]
public class GameSettings
{
    public string StartingRoomId { get; set; }
    public List<string> WinConditionRoomIds { get; set; }
    public string AiModel { get; set; }
    public string OllamaUrl { get; set; }
}

// Similar classes for Room, NPC, Item, Quest, Story
```

## Loading System

```csharp
// In src/Services/GameLoader.cs
public class GameLoader
{
    public static async Task<Game> LoadGameFromJsonAsync(string gameDirectory)
    {
        var gameDefJson = await File.ReadAllTextAsync(Path.Combine(gameDirectory, "game.json"));
        var gameDef = JsonSerializer.Deserialize<GameDefinition>(gameDefJson);

        var game = new Game
        {
            Id = gameDef.Id,
            Title = gameDef.Title,
            // ... populate from JSON
        };

        // Load all rooms
        var roomsDir = Path.Combine(gameDirectory, "rooms");
        foreach (var roomFile in Directory.GetFiles(roomsDir, "*.json"))
        {
            var roomJson = await File.ReadAllTextAsync(roomFile);
            var roomDef = JsonSerializer.Deserialize<RoomDefinition>(roomJson);
            game.Rooms[roomDef.Id] = ConvertToRoom(roomDef);
        }

        // Load NPCs, Items, Quests similarly

        return game;
    }
}
```

## Advantages

1. **No Code Changes** - Create new games without touching C#
2. **Easy to Version Control** - JSON diffs are readable
3. **Collaborative** - Non-programmers can design content
4. **Reusable** - Share rooms, NPCs, items across games
5. **Mod-Friendly** - Players can create and share games
6. **Extensible** - Easy to add new properties without code changes
7. **Human-Readable** - Easy to edit and debug

## Next Steps

1. Create `src/Services/GameLoader.cs` to load JSON files
2. Create `src/Models/JsonDefinitions.cs` for JSON data classes
3. Create `games/` directory structure
4. Convert existing FantasyQuest and SciFiAdventure to JSON format
5. Update `Program.cs` to load games dynamically

Would you like me to implement this system?
