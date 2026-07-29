using System.Text.Json;

namespace CSharpRPGBackend.Core;

/// <summary>
/// Creates an isolated runtime state from a reusable <see cref="Game"/> definition.
/// </summary>
public static class GameStateFactory
{
    private const string LegacyStartingItemsSetting = "startingItems";

    /// <summary>
    /// Creates a fresh game session. All mutable rooms, NPCs, quests, and item
    /// occurrences are copied so a session cannot mutate the game definition or
    /// another session created from it.
    /// </summary>
    /// <param name="game">The reusable game definition.</param>
    /// <param name="playerName">Optional player display name.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="game"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the starting room or a configured starting item is invalid.
    /// </exception>
    public static GameState Create(Game game, string? playerName = null) =>
        Create(game, 1, playerName);

    /// <summary>
    /// Creates a fresh game session with an explicit deterministic world seed.
    /// </summary>
    public static GameState Create(Game game, int worldSeed, string? playerName = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        var rooms = (game.Rooms ?? new Dictionary<string, Room>())
            .ToDictionary(pair => pair.Key, pair => CloneRoom(pair.Value));

        if (string.IsNullOrWhiteSpace(game.StartingRoomId) || !rooms.ContainsKey(game.StartingRoomId))
        {
            throw new InvalidOperationException(
                $"Game '{game.Id}' has an invalid starting room ID '{game.StartingRoomId}'.");
        }

        var player = CreatePlayer(game, playerName);
        var state = new GameState
        {
            CurrentRoomId = game.StartingRoomId,
            Rooms = rooms,
            Player = player,
            NPCs = (game.NPCs ?? new Dictionary<string, Character>())
                .ToDictionary(pair => pair.Key, pair => CloneCharacter(pair.Value)),
            PlayerInventory = new Inventory(),
            // Keep definition status intact. Offered quests remain discoverable here,
            // while quests authored as accepted/in-progress start active immediately.
            ActiveQuests = (game.Quests ?? new List<Quest>()).Select(CloneQuest).ToList(),
            WorldProjects = (game.WorldProjects ?? new List<WorldProject>())
                .Select(CloneWorldProject)
                .ToList(),
            RecentPlayerCommands = new List<string>(),
            TurnNumber = 0,
            WorldSeed = worldSeed,
            RecentWorldEvents = new List<WorldEvent>(),
            Companions = new List<string>(),
            InCombatMode = false,
            CurrentCombatNpcId = null,
            InChatMode = false,
            CurrentChatNpcId = null
        };

        AddStartingItems(game, state.PlayerInventory);
        return state;
    }

    /// <summary>
    /// Creates an independent runtime item from a reusable item definition.
    /// </summary>
    public static Item CreateItemInstance(Item itemDefinition)
    {
        ArgumentNullException.ThrowIfNull(itemDefinition);

        return new Item
        {
            Id = itemDefinition.Id,
            Name = itemDefinition.Name,
            Description = itemDefinition.Description,
            Type = itemDefinition.Type,
            Weight = itemDefinition.Weight,
            Value = itemDefinition.Value,
            Pricing = itemDefinition.Pricing == null
                ? null
                : new ItemPricing
                {
                    BasePrice = itemDefinition.Pricing.BasePrice,
                    BuyMultiplier = itemDefinition.Pricing.BuyMultiplier,
                    SellMultiplier = itemDefinition.Pricing.SellMultiplier,
                    CanBuy = itemDefinition.Pricing.CanBuy,
                    CanSell = itemDefinition.Pricing.CanSell
                },
            DamageBonus = itemDefinition.DamageBonus,
            ArmorBonus = itemDefinition.ArmorBonus,
            CriticalChance = itemDefinition.CriticalChance,
            Rarity = itemDefinition.Rarity,
            IsUnique = itemDefinition.IsUnique,
            IsEquippable = itemDefinition.IsEquippable,
            EquipmentSlot = itemDefinition.EquipmentSlot,
            UnlocksId = itemDefinition.UnlocksId,
            IsKey = itemDefinition.IsKey,
            KeyType = itemDefinition.KeyType,
            IsTeleportation = itemDefinition.IsTeleportation,
            TeleportDestinationRoomId = itemDefinition.TeleportDestinationRoomId,
            TeleportDescription = itemDefinition.TeleportDescription,
            IsConsumable = itemDefinition.IsConsumable,
            ConsumableUsesRemaining = itemDefinition.ConsumableUsesRemaining,
            ConsumableEffects = new Dictionary<string, int>(
                itemDefinition.ConsumableEffects ?? new Dictionary<string, int>()),
            Theme = itemDefinition.Theme,
            MaterialCategory = itemDefinition.MaterialCategory,
            GatherDifficulty = itemDefinition.GatherDifficulty,
            FoundInBiomes = itemDefinition.FoundInBiomes?.ToList(),
            IsTreasure = itemDefinition.IsTreasure,
            IsJunk = itemDefinition.IsJunk,
            Stackable = itemDefinition.Stackable,
            CanBeTaken = itemDefinition.CanBeTaken,
            Cursed = itemDefinition.Cursed,
            CustomProperties = CloneObjectDictionary(itemDefinition.CustomProperties)
        };
    }

    private static Character CreatePlayer(Game game, string? playerName)
    {
        var health = Math.Max(1, game.InitialPlayerHealth);
        var level = Math.Max(1, game.InitialPlayerLevel);
        var slotIds = game.GetEquipmentSlots().Slots?
            .Where(slot => !string.IsNullOrWhiteSpace(slot.Id))
            .OrderBy(slot => slot.DisplayOrder)
            .Select(slot => slot.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        return new Character
        {
            Id = "player",
            Name = string.IsNullOrWhiteSpace(playerName) ? "Adventurer" : playerName.Trim(),
            Description = game.InitialPlayerDescription,
            Health = health,
            MaxHealth = health,
            Level = level,
            Experience = Character.GetExperienceRequiredForLevel(level),
            Strength = 12,
            Agility = 11,
            Armor = 0,
            IsPlayer = true,
            CanMove = true,
            CurrentRoomId = game.StartingRoomId,
            EquipmentSlots = slotIds.ToDictionary(
                slotId => slotId,
                _ => (string?)null,
                StringComparer.OrdinalIgnoreCase),
            Wallet = new Wallet { TotalBaseUnits = game.StartingCurrency }
        };
    }

    private static void AddStartingItems(Game game, Inventory inventory)
    {
        var startingItems = GetStartingItems(game);
        foreach (var (itemId, quantity) in startingItems)
        {
            if (quantity <= 0)
            {
                throw new InvalidOperationException(
                    $"Starting item '{itemId}' must have a positive quantity (was {quantity}).");
            }

            var definition = FindItemDefinition(game.Items, itemId)
                ?? throw new InvalidOperationException(
                    $"Starting item '{itemId}' is not present in game '{game.Id}'.");

            if (!inventory.AddItem(CreateItemInstance(definition), quantity))
            {
                throw new InvalidOperationException(
                    $"Starting item '{itemId}' exceeds the player's inventory capacity.");
            }
        }
    }

    private static IReadOnlyDictionary<string, int> GetStartingItems(Game game)
    {
        if (game.StartingItems is { Count: > 0 })
            return game.StartingItems;

        // Compatibility for games loaded before Game.StartingItems was introduced.
        // This is deliberately not an all-weapons/all-armor fallback: no explicit
        // starting item configuration means an empty inventory.
        if (game.CustomSettings?.TryGetValue(LegacyStartingItemsSetting, out var json) != true ||
            string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, int>();
        }

        try
        {
            var definitions = JsonSerializer.Deserialize<List<StartingItemDefinition>>(json);
            if (definitions == null)
                return new Dictionary<string, int>();

            return definitions
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemId))
                .GroupBy(item => item.ItemId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Quantity),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Game '{game.Id}' contains invalid legacy starting item data.", exception);
        }
    }

    private static Item? FindItemDefinition(Dictionary<string, Item>? items, string itemId)
    {
        if (items == null)
            return null;

        if (items.TryGetValue(itemId, out var exactMatch))
            return exactMatch;

        return items.FirstOrDefault(pair =>
            pair.Key.Equals(itemId, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static Room CloneRoom(Room source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Room
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Exits = (source.Exits ?? new Dictionary<string, Exit>())
                .ToDictionary(pair => pair.Key, pair => CloneExit(pair.Value)),
            NPCIds = source.NPCIds?.ToList() ?? new List<string>(),
            Items = source.Items?.Select(CreateItemInstance).ToList() ?? new List<Item>(),
            Metadata = CloneObjectDictionary(source.Metadata),
            Resources = source.Resources == null ? null : CloneRoomResources(source.Resources)
        };
    }

    private static Exit CloneExit(Exit source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Exit
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            DestinationRoomId = source.DestinationRoomId,
            Description = source.Description,
            IsAvailable = source.IsAvailable,
            UnavailableReason = source.UnavailableReason,
            RequiredItemId = source.RequiredItemId,
            RequiredKeyId = source.RequiredKeyId
        };
    }

    private static RoomResources CloneRoomResources(RoomResources source) => new()
    {
        Resources = (source.Resources ?? new List<GatherableResource>())
            .Select(CloneGatherableResource)
            .ToList(),
        Biome = source.Biome,
        ResourceTags = source.ResourceTags?.ToList() ?? new List<string>(),
        HasBeenSearched = source.HasBeenSearched,
        DepletedResources = new Dictionary<string, int>(
            source.DepletedResources ?? new Dictionary<string, int>())
    };

    private static GatherableResource CloneGatherableResource(GatherableResource source) => new()
    {
        ItemId = source.ItemId,
        DisplayName = source.DisplayName,
        MinQuantity = source.MinQuantity,
        MaxQuantity = source.MaxQuantity,
        FindChance = source.FindChance,
        Renewable = source.Renewable,
        RespawnTurns = source.RespawnTurns,
        RequiredTool = source.RequiredTool,
        GatherVerb = source.GatherVerb,
        RelatedSkill = source.RelatedSkill,
        Difficulty = source.Difficulty
    };

    private static Character CloneCharacter(Character source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Character
        {
            Id = source.Id,
            Name = source.Name,
            Title = source.Title,
            Portrait = source.Portrait,
            Health = source.Health,
            MaxHealth = source.MaxHealth,
            Level = source.Level,
            Experience = source.Experience,
            Skills = new Dictionary<string, int>(source.Skills ?? new Dictionary<string, int>()),
            Alignment = source.Alignment,
            Strength = source.Strength,
            Agility = source.Agility,
            Armor = source.Armor,
            EquipmentSlots = new Dictionary<string, string?>(
                source.EquipmentSlots ?? new Dictionary<string, string?>()),
            PersonalityPrompt = source.PersonalityPrompt,
            ConversationHistory = (source.ConversationHistory ?? new List<ConversationEntry>())
                .Select(entry => new ConversationEntry
                {
                    Role = entry.Role,
                    Content = entry.Content,
                    Timestamp = entry.Timestamp
                })
                .ToList(),
            CarriedItems = (source.CarriedItems ?? new Dictionary<string, InventoryItem>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => new InventoryItem
                    {
                        Item = CreateItemInstance(pair.Value.Item),
                        Quantity = pair.Value.Quantity
                    }),
            Wallet = new Wallet { TotalBaseUnits = source.Wallet?.TotalBaseUnits ?? 0 },
            CurrentRoomId = source.CurrentRoomId,
            HomeRoomId = source.HomeRoomId,
            PatrolRoomIds = source.PatrolRoomIds?.ToList(),
            CanMove = source.CanMove,
            CanJoinParty = source.CanJoinParty,
            PatrolIntervalTurns = source.PatrolIntervalTurns,
            TurnsSinceLastMove = source.TurnsSinceLastMove,
            Role = source.Role,
            Description = source.Description,
            Backstory = source.Backstory,
            Relationships = source.Relationships?.ToList(),
            Reputation = new Dictionary<string, int>(
                source.Reputation ?? new Dictionary<string, int>()),
            CanCraft = source.CanCraft,
            KnownRecipes = source.KnownRecipes?.ToList() ?? new List<string>(),
            CraftingSpecialty = source.CraftingSpecialty,
            OfferedQuests = source.OfferedQuests?.ToList() ?? new List<string>(),
            CanOfferJobs = source.CanOfferJobs,
            IsPlayer = source.IsPlayer
        };
    }

    private static Quest CloneQuest(Quest source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Quest
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description,
            GiverNpcId = source.GiverNpcId,
            Status = source.Status,
            Type = source.Type,
            IsRepeatable = source.IsRepeatable,
            IsDynamic = source.IsDynamic,
            Requirements = (source.Requirements ?? new List<QuestRequirement>())
                .Select(requirement => new QuestRequirement
                {
                    Type = requirement.Type,
                    TargetId = requirement.TargetId,
                    TargetName = requirement.TargetName,
                    Quantity = requirement.Quantity,
                    CurrentProgress = requirement.CurrentProgress,
                    ConsumedOnCompletion = requirement.ConsumedOnCompletion,
                    Description = requirement.Description
                })
                .ToList(),
            Rewards = new QuestRewards
            {
                Experience = source.Rewards?.Experience ?? 0,
                Currency = source.Rewards?.Currency ?? 0,
                Items = source.Rewards?.Items
                    .Select(item => (item.ItemId, item.Quantity))
                    .ToList() ?? new List<(string ItemId, int Quantity)>(),
                ReputationChanges = new Dictionary<string, int>(
                    source.Rewards?.ReputationChanges ?? new Dictionary<string, int>()),
                RecipesLearned = source.Rewards?.RecipesLearned?.ToList() ?? new List<string>()
            },
            Objectives = source.Objectives?.ToList() ?? new List<string>(),
            CompletedObjectives = source.CompletedObjectives?.ToList() ?? new List<string>(),
            CreatedAt = source.CreatedAt,
            CompletedAt = source.CompletedAt,
            RequestedItemId = source.RequestedItemId,
            RequestedQuantity = source.RequestedQuantity,
            OfferDialogue = source.OfferDialogue,
            CompletionDialogue = source.CompletionDialogue
        };
    }

    private static WorldProject CloneWorldProject(WorldProject source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new WorldProject
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Status = source.Status,
            CurrentStageIndex = source.CurrentStageIndex,
            StartedTurn = source.StartedTurn,
            CompletedTurn = source.CompletedTurn,
            Metadata = new Dictionary<string, string>(
                source.Metadata ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
            Stages = (source.Stages ?? new List<WorldProjectStage>())
                .Select(stage => new WorldProjectStage
                {
                    Id = stage.Id,
                    Name = stage.Name,
                    Description = stage.Description,
                    EffectsApplied = stage.EffectsApplied,
                    CompletedTurn = stage.CompletedTurn,
                    Requirements = (stage.Requirements ?? new List<WorldProjectRequirement>())
                        .Select(requirement => new WorldProjectRequirement
                        {
                            Type = requirement.Type,
                            TargetId = requirement.TargetId,
                            EventType = requirement.EventType,
                            RequiredAmount = requirement.RequiredAmount,
                            CurrentAmount = requirement.CurrentAmount,
                            Description = requirement.Description
                        })
                        .ToList(),
                    Effects = (stage.Effects ?? new List<WorldProjectEffect>())
                        .Select(effect => new WorldProjectEffect
                        {
                            Type = effect.Type,
                            TargetId = effect.TargetId,
                            SecondaryTargetId = effect.SecondaryTargetId,
                            Value = effect.Value,
                            Quantity = effect.Quantity,
                            Data = new Dictionary<string, string>(
                                effect.Data ?? new Dictionary<string, string>(),
                                StringComparer.OrdinalIgnoreCase)
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static Dictionary<string, object> CloneObjectDictionary(
        Dictionary<string, object>? source)
    {
        if (source == null)
            return new Dictionary<string, object>();

        return source.ToDictionary(pair => pair.Key, pair => CloneObjectValue(pair.Value)!);
    }

    private static object? CloneObjectValue(object? value)
    {
        if (value == null || value is string || value.GetType().IsValueType)
            return value;

        return value switch
        {
            JsonElement jsonElement => jsonElement.Clone(),
            Dictionary<string, object> dictionary => CloneObjectDictionary(dictionary),
            _ => CloneUnknownValue(value)
        };
    }

    private static object CloneUnknownValue(object value)
    {
        var serialized = JsonSerializer.Serialize(value, value.GetType());
        return JsonSerializer.Deserialize(serialized, value.GetType()) ?? value;
    }
}
