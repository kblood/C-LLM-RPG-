namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents an exit from a room to another room.
/// The exit can have a custom display name (e.g., "North", "Into the tavern", "Back to town")
/// separate from the target room ID.
/// </summary>
public class Exit
{
    /// <summary>
    /// Unique identifier for this exit
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name shown to the player (e.g., "North", "Into the tavern", "Through the gate")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the room this exit leads to
    /// </summary>
    public string DestinationRoomId { get; set; } = string.Empty;

    /// <summary>
    /// Optional description shown when player looks at available exits
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this exit is available (can be used for locked doors, collapsed passages, etc.)
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Optional: reason why the exit is unavailable (e.g., "Door is locked", "The path is blocked")
    /// </summary>
    public string? UnavailableReason { get; set; }

    public Exit() { }

    public Exit(string displayName, string destinationRoomId, string? description = null)
    {
        DisplayName = displayName;
        DestinationRoomId = destinationRoomId;
        Description = description;
    }
}
