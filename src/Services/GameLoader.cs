using System.Text.Json;
using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Loads RPG games from JSON files in a game directory.
/// Converts JSON definitions to the main Game model while reusing existing classes.
/// </summary>
public class GameLoader
{
    private readonly JsonSerializerOptions _jsonOptions;

    public GameLoader()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    /// <summary>
    /// Load a game from a directory containing JSON files.
    /// Expected structure:
    /// gameDirectory/
    ///   ├── game.json
    ///   ├── story.json (optional)
    ///   ├── rooms/
    ///   │   ├── room-1.json
    ///   │   └── ...
    ///   ├── npcs/
    ///   │   ├── npc-1.json
    ///   │   └── ...
    ///   ├── items/
    ///   │   ├── item-1.json
    ///   │   └── ...
    ///   └── quests/
    ///       ├── quest-1.json
    ///       └── ...
    /// </summary>
    public async Task<Game> LoadGameAsync(string gameDirectory)
    {
        if (!Directory.Exists(gameDirectory))
            throw new DirectoryNotFoundException($"Game directory not found: {gameDirectory}");

        // Load game definition
        var gameJsonPath = Path.Combine(gameDirectory, "game.json");
        if (!File.Exists(gameJsonPath))
            throw new FileNotFoundException($"game.json not found in {gameDirectory}");

        var gameJson = await File.ReadAllTextAsync(gameJsonPath);
        var gameDef = JsonSerializer.Deserialize<GameDefinition>(gameJson, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize game.json");

        // Create the Game object
        var game = new Game
        {
            Id = gameDef.Id,
            Title = gameDef.Title,
            Subtitle = gameDef.Subtitle,
            Version = gameDef.Version,
            Description = gameDef.Description,
            Author = gameDef.Metadata?.Author,
            InitialPlayerHealth = gameDef.GameSettings?.PlayerStartingHealth ?? 100,
            InitialPlayerLevel = gameDef.GameSettings?.PlayerStartingLevel ?? 1,
            StartingRoomId = gameDef.GameSettings?.StartingRoomId ?? "start",
            WinConditionRoomIds = gameDef.GameSettings?.WinConditionRoomIds,
        };

        // Apply style settings
        if (gameDef.Style != null)
        {
            if (Enum.TryParse<GameStyle>(gameDef.Style.Theme, ignoreCase: true, out var style))
                game.Style = style;
            game.CustomSettings["tonality"] = gameDef.Style.Tonality ?? string.Empty;
            game.CustomSettings["narratorVoice"] = gameDef.Style.NarratorVoice ?? string.Empty;
        }

        // Apply feature flags
        if (gameDef.Features != null)
        {
            game.HasCombat = gameDef.Features.GetValueOrDefault("enableCombat", true);
            game.HasInventory = gameDef.Features.GetValueOrDefault("enableInventory", true);
            game.HasMagic = gameDef.Features.GetValueOrDefault("enableMagic", false);
            game.HasTechnology = gameDef.Features.GetValueOrDefault("enableTechnology", false);
        }

        // Store settings for later use
        if (gameDef.GameSettings != null)
        {
            game.CustomSettings["aiModel"] = gameDef.GameSettings.AiModel ?? "granite4:3b";
            game.CustomSettings["ollamaUrl"] = gameDef.GameSettings.OllamaUrl ?? "http://localhost:11434";
            if (gameDef.GameSettings.MaxTurns.HasValue)
                game.CustomSettings["maxTurns"] = gameDef.GameSettings.MaxTurns.Value.ToString();
        }

        // Load items first (needed by rooms and NPCs)
        var itemsDir = Path.Combine(gameDirectory, "items");
        if (Directory.Exists(itemsDir))
        {
            foreach (var itemFile in Directory.GetFiles(itemsDir, "*.json"))
            {
                var itemJson = await File.ReadAllTextAsync(itemFile);
                var itemDef = JsonSerializer.Deserialize<ItemDefinition>(itemJson, _jsonOptions);
                if (itemDef != null)
                {
                    var item = ConvertItemDefinitionToItem(itemDef);
                    game.Items[item.Id] = item;
                }
            }
        }

        // Load NPCs (needed by rooms)
        var npcsDir = Path.Combine(gameDirectory, "npcs");
        var npcDefinitions = new Dictionary<string, NpcDefinition>();
        if (Directory.Exists(npcsDir))
        {
            foreach (var npcFile in Directory.GetFiles(npcsDir, "*.json"))
            {
                var npcJson = await File.ReadAllTextAsync(npcFile);
                var npcDef = JsonSerializer.Deserialize<NpcDefinition>(npcJson, _jsonOptions);
                if (npcDef != null)
                {
                    npcDefinitions[npcDef.Id] = npcDef;
                    var npc = ConvertNpcDefinitionToCharacter(npcDef, game.Items);
                    game.NPCs[npc.Id] = npc;
                }
            }
        }

        // Load rooms
        var roomsDir = Path.Combine(gameDirectory, "rooms");
        if (Directory.Exists(roomsDir))
        {
            foreach (var roomFile in Directory.GetFiles(roomsDir, "*.json"))
            {
                var roomJson = await File.ReadAllTextAsync(roomFile);
                var roomDef = JsonSerializer.Deserialize<RoomDefinition>(roomJson, _jsonOptions);
                if (roomDef != null)
                {
                    var room = ConvertRoomDefinitionToRoom(roomDef, game.Items, npcDefinitions);
                    game.Rooms[room.Id] = room;
                }
            }
        }

        // Load quests (optional)
        var questsDir = Path.Combine(gameDirectory, "quests");
        if (Directory.Exists(questsDir))
        {
            foreach (var questFile in Directory.GetFiles(questsDir, "*.json"))
            {
                var questJson = await File.ReadAllTextAsync(questFile);
                var questDef = JsonSerializer.Deserialize<QuestDefinition>(questJson, _jsonOptions);
                if (questDef != null)
                {
                    var quest = ConvertQuestDefinitionToQuest(questDef);
                    game.Quests.Add(quest);
                }
            }
        }

        // Add starting items to player inventory
        if (gameDef.StartingItems != null)
        {
            // We'll need to handle this in GameMaster when initializing the game state
            game.CustomSettings["startingItems"] = JsonSerializer.Serialize(gameDef.StartingItems);
        }

        return game;
    }

    /// <summary>
    /// Find all available games in a games/ directory.
    /// </summary>
    public List<GameInfo> FindAvailableGames(string gamesDirectory)
    {
        var games = new List<GameInfo>();

        if (!Directory.Exists(gamesDirectory))
            return games;

        foreach (var directory in Directory.GetDirectories(gamesDirectory))
        {
            var gameJsonPath = Path.Combine(directory, "game.json");
            if (File.Exists(gameJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(gameJsonPath);
                    var gameDef = JsonSerializer.Deserialize<GameDefinition>(json, _jsonOptions);
                    if (gameDef != null)
                    {
                        games.Add(new GameInfo
                        {
                            Id = gameDef.Id,
                            Title = gameDef.Title,
                            Subtitle = gameDef.Subtitle,
                            Description = gameDef.Description,
                            Author = gameDef.Metadata?.Author,
                            GameDirectory = directory
                        });
                    }
                }
                catch
                {
                    // Skip invalid game directories
                }
            }
        }

        return games.OrderBy(g => g.Title).ToList();
    }

    // ========================================================================
    // CONVERSION METHODS
    // ========================================================================

    private Item ConvertItemDefinitionToItem(ItemDefinition def)
    {
        var itemType = Enum.TryParse<ItemType>(def.Type, ignoreCase: true, out var type)
            ? type
            : ItemType.Miscellaneous;

        var rarity = Enum.TryParse<ItemRarity>(def.Rarity, ignoreCase: true, out var r)
            ? r
            : ItemRarity.Common;

        return new Item
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
            Type = itemType,
            Weight = (int)(def.Equipment?.Weight ?? 0),
            Value = def.Value?.GoldValue ?? 0,
            DamageBonus = def.Stats?.DamageBonus ?? 0,
            ArmorBonus = def.Stats?.ArmorBonus ?? 0,
            CriticalChance = def.Stats?.CriticalChance ?? 0,
            Rarity = rarity,
            IsEquippable = def.Equipment?.IsEquippable ?? false,
            EquipmentSlot = def.Equipment?.EquipmentSlot,
            Theme = def.Metadata.ContainsKey("theme") ? def.Metadata["theme"].ToString() : null,
            CanBeTaken = true,
            Stackable = itemType == ItemType.Consumable,
            CustomProperties = def.Metadata
        };
    }

    private Character ConvertNpcDefinitionToCharacter(NpcDefinition def, Dictionary<string, Item> allItems)
    {
        var alignment = Enum.TryParse<CharacterAlignment>(def.Alignment, ignoreCase: true, out var a)
            ? a
            : CharacterAlignment.Neutral;

        var role = Enum.TryParse<NPCRole>(def.Role?.Replace("-", "_"), ignoreCase: true, out var r)
            ? r
            : NPCRole.CommonPerson;

        var npc = new Character
        {
            Id = def.Id,
            Name = def.Name,
            Title = def.Title,
            Portrait = def.Portrait,
            Description = def.Description,
            Health = def.Stats?.Health ?? 60,
            MaxHealth = def.Stats?.MaxHealth ?? 60,
            Level = def.Stats?.Level ?? 1,
            Experience = def.Stats?.Experience ?? 0,
            Strength = def.Stats?.Strength ?? 10,
            Agility = def.Stats?.Agility ?? 10,
            Armor = def.Stats?.Armor ?? 0,
            Alignment = alignment,
            Role = role,
            PersonalityPrompt = def.Personality?.PersonalityPrompt,
            IsPlayer = false,
        };

        // Add location info
        if (def.Location != null)
        {
            npc.CurrentRoomId = def.Location.CurrentRoomId;
            npc.HomeRoomId = def.Location.HomeRoomId;
            npc.PatrolRoomIds = def.Location.PatrolRoomIds;
            npc.PatrolIntervalTurns = def.Location.PatrolIntervalTurns;
            npc.CanMove = def.Location.CanMove;
            npc.CanJoinParty = def.Location.CanJoinParty;
        }

        // Add inventory (loot items)
        if (def.Inventory != null)
        {
            foreach (var invItem in def.Inventory)
            {
                if (allItems.TryGetValue(invItem.ItemId, out var item))
                {
                    npc.CarriedItems[item.Id] = new InventoryItem
                    {
                        Item = item,
                        Quantity = invItem.Quantity
                    };
                }
            }
        }

        // Add relationships
        if (def.Relationships != null)
        {
            npc.Relationships = new List<string>();
            if (def.Relationships.Allies != null)
                npc.Relationships.AddRange(def.Relationships.Allies);
            if (def.Relationships.Enemies != null)
                npc.Relationships.AddRange(def.Relationships.Enemies);
            if (def.Relationships.Neutral != null)
                npc.Relationships.AddRange(def.Relationships.Neutral);
        }

        return npc;
    }

    private Room ConvertRoomDefinitionToRoom(RoomDefinition def, Dictionary<string, Item> allItems,
        Dictionary<string, NpcDefinition> npcDefinitions)
    {
        var room = new Room
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
        };

        // Add exits
        if (def.Exits != null)
        {
            foreach (var exitDef in def.Exits)
            {
                var exit = new Exit
                {
                    Id = exitDef.Id,
                    DisplayName = exitDef.DisplayName,
                    DestinationRoomId = exitDef.DestinationRoomId,
                    Description = exitDef.Description,
                    IsAvailable = !exitDef.Locked,
                    UnavailableReason = exitDef.Locked ? "The exit is locked" : null
                };
                room.Exits[exitDef.DisplayName] = exit;
            }
        }

        // Add NPCs
        if (def.NPCs != null)
        {
            foreach (var roomNpc in def.NPCs)
            {
                room.NPCIds.Add(roomNpc.NpcId);
                // Update NPC location if it spawns here
                if (roomNpc.SpawnOnEnter && npcDefinitions.ContainsKey(roomNpc.NpcId))
                {
                    var npcDef = npcDefinitions[roomNpc.NpcId];
                    npcDef.Location ??= new LocationDefinition();
                    if (npcDef.Location != null)
                    {
                        npcDef.Location.CurrentRoomId = def.Id;
                    }
                }
            }
        }

        // Add items
        if (def.Items != null)
        {
            foreach (var roomItem in def.Items)
            {
                if (allItems.TryGetValue(roomItem.ItemId, out var item))
                {
                    room.Items.Add(item);
                }
            }
        }

        // Add metadata
        if (def.Ambiance != null)
        {
            if (!string.IsNullOrEmpty(def.Ambiance.Soundscape))
                room.Metadata["soundscape"] = def.Ambiance.Soundscape;
            if (!string.IsNullOrEmpty(def.Ambiance.LightingLevel))
                room.Metadata["lighting"] = def.Ambiance.LightingLevel;
            if (!string.IsNullOrEmpty(def.Ambiance.Temperature))
                room.Metadata["temperature"] = def.Ambiance.Temperature;
        }

        if (def.Metadata != null)
        {
            foreach (var kvp in def.Metadata)
            {
                if (!room.Metadata.ContainsKey(kvp.Key))
                    room.Metadata[kvp.Key] = kvp.Value;
            }
        }

        return room;
    }

    private Quest ConvertQuestDefinitionToQuest(QuestDefinition def)
    {
        var quest = new Quest
        {
            Id = def.Id,
            Title = def.Title,
            Description = def.Description,
            GiverNpcId = def.Giver ?? string.Empty,
            RewardExperience = def.ExperienceReward,
            Status = QuestStatus.Offered
        };

        // Add objectives
        foreach (var obj in def.Objectives)
        {
            quest.Objectives.Add(obj.Title);
        }

        return quest;
    }
}

/// <summary>
/// Information about a game that can be loaded.
/// </summary>
public class GameInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string GameDirectory { get; set; } = string.Empty;

    public override string ToString() => $"{Title}" + (string.IsNullOrEmpty(Subtitle) ? "" : $" - {Subtitle}");
}
