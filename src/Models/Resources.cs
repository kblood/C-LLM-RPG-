namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a resource that can be gathered from a location.
/// </summary>
public class GatherableResource
{
    /// <summary>
    /// The item ID that can be gathered.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the resource (for narration).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Minimum quantity found on success.
    /// </summary>
    public int MinQuantity { get; set; } = 1;

    /// <summary>
    /// Maximum quantity found on success.
    /// </summary>
    public int MaxQuantity { get; set; } = 1;

    /// <summary>
    /// Base chance to find this resource (0-100).
    /// </summary>
    public int FindChance { get; set; } = 50;

    /// <summary>
    /// Whether this resource regenerates over time.
    /// </summary>
    public bool Renewable { get; set; } = true;

    /// <summary>
    /// Turns until resource respawns (if renewable).
    /// </summary>
    public int? RespawnTurns { get; set; }

    /// <summary>
    /// Required tool item ID to gather (e.g., "pickaxe", "herbalist_kit").
    /// Null means no tool required.
    /// </summary>
    public string? RequiredTool { get; set; }

    /// <summary>
    /// The verb used for gathering (e.g., "mine", "pick", "chop", "forage").
    /// Used for narration and command matching.
    /// </summary>
    public string GatherVerb { get; set; } = "gather";

    /// <summary>
    /// Skill that affects success chance (e.g., "mining", "herbalism").
    /// </summary>
    public string? RelatedSkill { get; set; }

    /// <summary>
    /// Difficulty modifier for gathering (affects success rate).
    /// </summary>
    public int Difficulty { get; set; } = 0;
}

/// <summary>
/// Configuration for resources in a specific room.
/// </summary>
public class RoomResources
{
    /// <summary>
    /// Defined gatherable resources in this location.
    /// </summary>
    public List<GatherableResource> Resources { get; set; } = new();

    /// <summary>
    /// Biome type of this location (affects what can be found).
    /// Examples: "forest", "cave", "mountain", "swamp", "desert", "plains"
    /// </summary>
    public string? Biome { get; set; }

    /// <summary>
    /// Tags describing what types of resources might exist here.
    /// Examples: "ore", "herbs", "wood", "water", "minerals"
    /// Used by dynamic resource generation.
    /// </summary>
    public List<string> ResourceTags { get; set; } = new();

    /// <summary>
    /// Whether this room has been searched (for one-time resources).
    /// </summary>
    public bool HasBeenSearched { get; set; } = false;

    /// <summary>
    /// Tracks depleted resources and when they respawn.
    /// Key = resource item ID, Value = turns until respawn.
    /// </summary>
    public Dictionary<string, int> DepletedResources { get; set; } = new();
}

/// <summary>
/// Result of a gathering attempt.
/// </summary>
public class GatherResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<(string ItemId, string ItemName, int Quantity)> ItemsGathered { get; set; } = new();

    /// <summary>
    /// Whether this result was determined by the LLM (dynamic) vs predefined resources.
    /// </summary>
    public bool WasDynamic { get; set; } = false;

    public static GatherResult Failure(string message) => new()
    {
        Success = false,
        Message = message
    };

    public static GatherResult Found(string itemId, string itemName, int quantity, string message) => new()
    {
        Success = true,
        Message = message,
        ItemsGathered = new() { (itemId, itemName, quantity) }
    };
}

/// <summary>
/// Categories of gatherable materials.
/// </summary>
public static class MaterialCategories
{
    public const string Ore = "ore";
    public const string Herb = "herb";
    public const string Wood = "wood";
    public const string Leather = "leather";
    public const string Gem = "gem";
    public const string Cloth = "cloth";
    public const string Food = "food";
    public const string Alchemical = "alchemical";
    public const string Magical = "magical";
    public const string Bone = "bone";
    public const string Scale = "scale";
}

/// <summary>
/// Common biome types for resource generation.
/// </summary>
public static class Biomes
{
    public const string Forest = "forest";
    public const string Cave = "cave";
    public const string Mountain = "mountain";
    public const string Swamp = "swamp";
    public const string Desert = "desert";
    public const string Plains = "plains";
    public const string Underwater = "underwater";
    public const string Volcanic = "volcanic";
    public const string Tundra = "tundra";
    public const string Jungle = "jungle";
    public const string Urban = "urban";
    public const string Ruins = "ruins";
}
