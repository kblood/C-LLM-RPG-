namespace CSharpRPGBackend.Core;

/// <summary>
/// Versioned save-file envelope. The envelope lets future releases migrate old
/// state without changing the runtime GameState API.
/// </summary>
public class GameSaveDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string? GameId { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; }
    public GameState State { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
