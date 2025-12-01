namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a location/room in the game world.
/// </summary>
public class Room
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Exits from this room with custom display names
    /// Key: exit display name (e.g., "North", "Into the tavern")
    /// Value: Exit object containing destination and metadata
    /// </summary>
    public Dictionary<string, Exit> Exits { get; set; } = new();

    /// <summary>
    /// NPCs present in this room
    /// </summary>
    public List<string> NPCIds { get; set; } = new();

    /// <summary>
    /// Items lying on the ground in this room
    /// </summary>
    public List<Item> Items { get; set; } = new();

    /// <summary>
    /// Custom metadata for the room (lighting, temperature, danger level, etc.)
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gatherable resources in this location.
    /// </summary>
    public RoomResources? Resources { get; set; }

    /// <summary>
    /// Biome type for this room (for resource generation).
    /// </summary>
    public string? Biome
    {
        get => Resources?.Biome ?? Metadata.GetValueOrDefault("biome") as string;
        set
        {
            Resources ??= new RoomResources();
            Resources.Biome = value;
        }
    }

    /// <summary>
    /// Get available exits (only those marked as available)
    /// </summary>
    public List<Exit> GetAvailableExits()
    {
        return Exits.Values.Where(e => e.IsAvailable).ToList();
    }

    /// <summary>
    /// Try to find an exit by display name (case-insensitive, with fuzzy matching)
    /// </summary>
    public Exit? FindExit(string displayName)
    {
        var lowerInput = displayName.ToLower();

        // First try exact match
        var exit = Exits.Values.FirstOrDefault(e =>
            e.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase) && e.IsAvailable);

        if (exit != null)
            return exit;

        // Try partial/fuzzy match - check if exit name contains the input
        exit = Exits.Values.FirstOrDefault(e =>
            e.DisplayName.ToLower().Contains(lowerInput) && e.IsAvailable);

        if (exit != null)
            return exit;

        // Try if input contains the exit name (for short forms like "north" matching "North")
        exit = Exits.Values.FirstOrDefault(e =>
            lowerInput.Contains(e.DisplayName.ToLower()) && e.IsAvailable);

        return exit;
    }
}
