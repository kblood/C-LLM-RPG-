using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Utils;

/// <summary>
/// Fluent builder for creating rooms with a convenient API.
/// Usage: new RoomBuilder("library")
///     .WithName("Grand Library")
///     .WithDescription("Towering shelves...")
///     .AddExit("North", "study", "A narrow staircase leads north")
///     .AddExit("South", "entrance", "Return to the entrance")
///     .AddNPC("librarian")
///     .Build()
/// </summary>
public class RoomBuilder
{
    private readonly Room _room;

    public RoomBuilder(string roomId)
    {
        _room = new Room { Id = roomId };
    }

    public RoomBuilder WithName(string name)
    {
        _room.Name = name;
        return this;
    }

    public RoomBuilder WithDescription(string description)
    {
        _room.Description = description;
        return this;
    }

    /// <summary>
    /// Add an exit to the room.
    /// </summary>
    public RoomBuilder AddExit(string displayName, string destinationRoomId, string? description = null)
    {
        var exitKey = displayName.ToLower().Replace(" ", "_");
        _room.Exits[exitKey] = new Exit(displayName, destinationRoomId, description);
        return this;
    }

    /// <summary>
    /// Add an NPC to the room.
    /// </summary>
    public RoomBuilder AddNPC(string npcId)
    {
        if (!_room.NPCIds.Contains(npcId))
        {
            _room.NPCIds.Add(npcId);
        }
        return this;
    }

    /// <summary>
    /// Add multiple NPCs to the room.
    /// </summary>
    public RoomBuilder AddNPCs(params string[] npcIds)
    {
        foreach (var npcId in npcIds)
        {
            AddNPC(npcId);
        }
        return this;
    }

    /// <summary>
    /// Add an item to the room floor.
    /// </summary>
    public RoomBuilder AddItem(Item item)
    {
        _room.Items.Add(item);
        return this;
    }

    /// <summary>
    /// Add metadata to the room.
    /// </summary>
    public RoomBuilder WithMetadata(string key, object value)
    {
        _room.Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Set multiple metadata entries.
    /// </summary>
    public RoomBuilder WithMetadata(Dictionary<string, object> metadata)
    {
        foreach (var kvp in metadata)
        {
            _room.Metadata[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <summary>
    /// Set the biome for this room (affects resource gathering).
    /// </summary>
    public RoomBuilder WithBiome(string biome)
    {
        _room.Resources ??= new RoomResources();
        _room.Resources.Biome = biome;
        return this;
    }

    /// <summary>
    /// Add resource tags that describe what can be found here.
    /// </summary>
    public RoomBuilder WithResourceTags(params string[] tags)
    {
        _room.Resources ??= new RoomResources();
        _room.Resources.ResourceTags.AddRange(tags);
        return this;
    }

    /// <summary>
    /// Add a gatherable resource to this room.
    /// </summary>
    public RoomBuilder AddGatherableResource(string itemId, int findChance = 50, int minQty = 1, int maxQty = 1, string? gatherVerb = null, string? requiredTool = null)
    {
        _room.Resources ??= new RoomResources();
        _room.Resources.Resources.Add(new GatherableResource
        {
            ItemId = itemId,
            FindChance = findChance,
            MinQuantity = minQty,
            MaxQuantity = maxQty,
            GatherVerb = gatherVerb ?? "gather",
            RequiredTool = requiredTool
        });
        return this;
    }

    /// <summary>
    /// Add a full gatherable resource configuration.
    /// </summary>
    public RoomBuilder AddGatherableResource(GatherableResource resource)
    {
        _room.Resources ??= new RoomResources();
        _room.Resources.Resources.Add(resource);
        return this;
    }

    /// <summary>
    /// Build and return the room.
    /// </summary>
    public Room Build()
    {
        return _room;
    }
}

/// <summary>
/// Fluent builder for creating NPCs with a convenient API.
/// </summary>
public class NpcBuilder
{
    private readonly Character _character;

    public NpcBuilder(string npcId, string name)
    {
        _character = new Character { Id = npcId, Name = name };
    }

    public NpcBuilder WithHealth(int health, int maxHealth)
    {
        _character.Health = health;
        _character.MaxHealth = maxHealth;
        return this;
    }

    public NpcBuilder WithLevel(int level)
    {
        _character.Level = level;
        return this;
    }

    public NpcBuilder WithAlignment(CharacterAlignment alignment)
    {
        _character.Alignment = alignment;
        return this;
    }

    public NpcBuilder WithPersonalityPrompt(string prompt)
    {
        _character.PersonalityPrompt = prompt;
        return this;
    }

    public NpcBuilder AddSkill(string skillName, int value)
    {
        _character.Skills[skillName] = value;
        return this;
    }

    public NpcBuilder WithStats(int strength, int agility, int armor)
    {
        _character.Strength = strength;
        _character.Agility = agility;
        _character.Armor = armor;
        return this;
    }

    public NpcBuilder WithStrength(int strength)
    {
        _character.Strength = strength;
        return this;
    }

    public NpcBuilder WithAgility(int agility)
    {
        _character.Agility = agility;
        return this;
    }

    public NpcBuilder WithArmor(int armor)
    {
        _character.Armor = armor;
        return this;
    }

    /// <summary>
    /// Add an item that the NPC carries (for looting when defeated).
    /// </summary>
    public NpcBuilder WithLoot(Item item, int quantity = 1)
    {
        _character.CarriedItems[item.Id] = new InventoryItem { Item = item, Quantity = quantity };
        return this;
    }

    /// <summary>
    /// Set the NPC's starting currency (for simple economy).
    /// </summary>
    public NpcBuilder WithCurrency(long amount)
    {
        _character.Wallet.Add(amount);
        return this;
    }

    /// <summary>
    /// Set the NPC's starting currency (for tiered economy).
    /// </summary>
    public NpcBuilder WithTieredCurrency(int platinum = 0, int gold = 0, int silver = 0, int conversionRate = 100)
    {
        _character.Wallet.AddTiered(platinum, gold, silver, conversionRate);
        return this;
    }

    /// <summary>
    /// Make this NPC a crafter with a specialty.
    /// </summary>
    public NpcBuilder AsCrafter(string specialty)
    {
        _character.CanCraft = true;
        _character.CraftingSpecialty = specialty;
        return this;
    }

    /// <summary>
    /// Add a known recipe to this NPC's crafting repertoire.
    /// </summary>
    public NpcBuilder WithRecipe(string recipeId)
    {
        _character.CanCraft = true;
        _character.KnownRecipes.Add(recipeId);
        return this;
    }

    /// <summary>
    /// Add multiple known recipes.
    /// </summary>
    public NpcBuilder WithRecipes(params string[] recipeIds)
    {
        _character.CanCraft = true;
        _character.KnownRecipes.AddRange(recipeIds);
        return this;
    }

    /// <summary>
    /// Add a quest this NPC can offer.
    /// </summary>
    public NpcBuilder OffersQuest(string questId)
    {
        _character.OfferedQuests.Add(questId);
        return this;
    }

    /// <summary>
    /// Allow this NPC to dynamically offer jobs.
    /// </summary>
    public NpcBuilder CanOfferDynamicJobs()
    {
        _character.CanOfferJobs = true;
        return this;
    }

    /// <summary>
    /// Build and return the NPC.
    /// </summary>
    public Character Build()
    {
        return _character;
    }
}
