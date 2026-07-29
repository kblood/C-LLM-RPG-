using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Reads and writes complete, versioned JSON snapshots of runtime game state.
/// Files are replaced atomically so an interrupted save does not corrupt the
/// previous snapshot.
/// </summary>
public class GameSaveService
{
    public const int CurrentSchemaVersion = 1;

    private readonly JsonSerializerOptions _jsonOptions;

    public GameSaveService(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions == null
            ? CreateDefaultOptions()
            : new JsonSerializerOptions(jsonOptions);
    }

    public string Serialize(
        GameState state,
        string? gameId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = new GameSaveDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            GameId = gameId,
            SavedAtUtc = DateTimeOffset.UtcNow,
            State = state,
            Metadata = metadata == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        };

        return JsonSerializer.Serialize(document, _jsonOptions);
    }

    public GameSaveDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var parsed = JsonDocument.Parse(json);
        if (!parsed.RootElement.TryGetProperty("schemaVersion", out var versionElement) &&
            !parsed.RootElement.TryGetProperty("SchemaVersion", out versionElement))
        {
            throw new GameSaveVersionException("The save file has no schema version.");
        }

        var version = versionElement.GetInt32();
        if (version > CurrentSchemaVersion)
        {
            throw new GameSaveVersionException(
                $"Save schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        }

        if (version < 1)
            throw new GameSaveVersionException($"Save schema {version} is not supported.");

        var migratedJson = Migrate(json, version);
        var document = JsonSerializer.Deserialize<GameSaveDocument>(migratedJson, _jsonOptions)
            ?? throw new JsonException("The save file did not contain a game state.");

        if (document.State == null)
            throw new JsonException("The save file's state was null.");

        NormalizeAfterLoad(document);
        return document;
    }

    public void Save(
        string path,
        GameState state,
        string? gameId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var json = Serialize(state, gameId, metadata);
        WriteAtomically(path, json);
    }

    public GameSaveDocument Load(string path)
    {
        var fullPath = ValidatePath(path);
        return Deserialize(File.ReadAllText(fullPath, Encoding.UTF8));
    }

    public GameState LoadState(string path) => Load(path).State;

    public async Task SaveAsync(
        string path,
        GameState state,
        string? gameId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var json = Serialize(state, gameId, metadata);
        var fullPath = PreparePath(path);
        var temporaryPath = GetTemporaryPath(fullPath);

        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<GameSaveDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ValidatePath(path);
        var json = await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
        return Deserialize(json);
    }

    public async Task<GameState> LoadStateAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        (await LoadAsync(path, cancellationToken)).State;

    public static JsonSerializerOptions CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string Migrate(string json, int sourceVersion)
    {
        // Schema 1 is the initial format. Add sequential migrations here when
        // changing the persisted shape; callers never need to know the details.
        return sourceVersion switch
        {
            CurrentSchemaVersion => json,
            _ => throw new GameSaveVersionException(
                $"No migration exists from save schema {sourceVersion}.")
        };
    }

    private static void NormalizeAfterLoad(GameSaveDocument document)
    {
        var state = document.State;
        state.Rooms ??= new Dictionary<string, Room>();
        state.NPCs ??= new Dictionary<string, Character>();
        state.ActiveQuests ??= new List<Quest>();
        state.Companions ??= new List<string>();
        state.RecentPlayerCommands ??= new List<string>();
        state.RecentWorldEvents ??= new List<WorldEvent>();
        state.WorldProjects ??= new List<WorldProject>();
        state.Player ??= new Character();
        state.PlayerInventory ??= new Inventory();

        // Inventory.CurrentWeight has a private setter. Re-adding items restores
        // that derived value instead of trusting serialized, potentially stale data.
        var serializedInventory = state.PlayerInventory;
        serializedInventory.Items ??= new Dictionary<string, InventoryItem>();
        var requiredCapacity = serializedInventory.Items.Values.Aggregate(
            0L,
            (total, inventoryItem) => total +
                (long)Math.Max(0, inventoryItem.Item?.Weight ?? 0) * Math.Max(0, inventoryItem.Quantity));
        var restoredInventory = new Inventory
        {
            MaxWeight = (int)Math.Min(int.MaxValue, Math.Max(serializedInventory.MaxWeight, requiredCapacity))
        };
        foreach (var inventoryItem in serializedInventory.Items.Values)
        {
            if (inventoryItem.Item != null && inventoryItem.Quantity > 0)
                restoredInventory.AddItem(inventoryItem.Item, inventoryItem.Quantity);
        }
        restoredInventory.MaxWeight = serializedInventory.MaxWeight;
        state.PlayerInventory = restoredInventory;

        document.Metadata = document.Metadata == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(document.Metadata, StringComparer.OrdinalIgnoreCase);

        foreach (var worldEvent in state.RecentWorldEvents)
        {
            worldEvent.Data = worldEvent.Data == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(worldEvent.Data, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var project in state.WorldProjects)
        {
            project.Metadata = project.Metadata == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(project.Metadata, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var fullPath = PreparePath(path);
        var temporaryPath = GetTemporaryPath(fullPath);
        try
        {
            File.WriteAllText(temporaryPath, contents, Encoding.UTF8);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string PreparePath(string path)
    {
        var fullPath = ValidatePath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return fullPath;
    }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static string GetTemporaryPath(string fullPath) =>
        $"{fullPath}.{Guid.NewGuid():N}.tmp";
}

public class GameSaveVersionException : Exception
{
    public GameSaveVersionException(string message) : base(message)
    {
    }
}
