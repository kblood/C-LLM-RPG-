using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;
using CSharpRPGBackend.Games;
using System.Security.Cryptography;
using System.Text;

namespace RPGWeb.Services;

public record ChatEntry(string Role, string Content, DateTime Timestamp);

/// <summary>
/// Scoped service (one per Blazor circuit / browser tab).
/// Manages a single game session including state, GameMaster, and chat history.
/// </summary>
public class GameSessionService
{
    private readonly LlmSettings _settings;
    private readonly BrowserSaveSlot _saveSlot;
    private readonly string _dataDirectory;
    private ILlmClient _activeClient;   // recreated when settings change
    private GameState? _gameState;
    private GameMaster? _gameMaster;
    private Game? _game;
    private readonly GameSaveService _saveService = new();

    public bool IsGameActive => _gameState != null && _game != null && _gameMaster != null;
    public GameState? State => _gameState;
    public Game? CurrentGame => _game;
    public List<ChatEntry> ChatHistory { get; } = new();
    public bool IsProcessing { get; private set; }
    public bool IsVictory { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsGameOver => IsVictory || IsDead;
    public string? LlmStatus { get; private set; }
    public string? LastSaveStatus { get; private set; }

    public GameSessionService(
        LlmSettings settings,
        BrowserSaveSlot saveSlot,
        IConfiguration configuration)
    {
        // The registered settings object is server-wide configuration. Each
        // circuit gets an independent runtime copy so one browser cannot change
        // another browser's provider/model selection or rewrite server settings.
        _settings = settings.CreateRuntimeCopy();
        _saveSlot = saveSlot;
        var configuredDataDirectory = configuration["RPGWEB_DATA_DIRECTORY"];
        _dataDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredDataDirectory)
                ? Directory.GetCurrentDirectory()
                : configuredDataDirectory);
        _activeClient = _settings.CreateClient();
    }

    /// <summary>
    /// Get all available games (built-in + JSON-loaded from games/ directory).
    /// </summary>
    public async Task<List<(string name, Game game)>> GetAvailableGamesAsync()
    {
        var games = new List<(string, Game)>
        {
            ("Fantasy Quest", FantasyQuest.Create()),
            ("Sci-Fi Adventure", SciFiAdventure.Create())
        };

        // Load JSON-based games if games/ directory exists
        var gamesDir = Path.Combine(Directory.GetCurrentDirectory(), "games");
        if (Directory.Exists(gamesDir))
        {
            try
            {
                var loader = new GameLoader();
                var gameInfos = loader.FindAvailableGames(gamesDir);
                foreach (var info in gameInfos)
                {
                    try
                    {
                        var loaded = await loader.LoadGameAsync(info.GameDirectory);
                        games.Add((info.Title, loaded));
                    }
                    catch { /* skip broken games */ }
                }
            }
            catch { }
        }

        return games;
    }

    /// <summary>
    /// Check if the LLM backend is reachable.
    /// </summary>
    public async Task<bool> CheckLlmHealthAsync()
    {
        try
        {
            var healthy = await _activeClient.IsHealthyAsync();
            LlmStatus = healthy
                ? $"Connected to {_activeClient.BackendName} ({_activeClient.DefaultModel})"
                : $"Cannot reach {_activeClient.BackendName}";
            return healthy;
        }
        catch (Exception ex)
        {
            LlmStatus = $"Error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Start a new game session with the given game definition.
    /// </summary>
    public void StartGame(Game game)
    {
        _game = game;
        _gameState = GameStateFactory.Create(game);
        _gameMaster = new GameMaster(_gameState, _activeClient, null, game);

        // Reset state
        ChatHistory.Clear();
        IsVictory = false;
        IsDead = false;
        LastSaveStatus = null;

        // Add intro messages
        if (!string.IsNullOrEmpty(game.StoryIntroduction))
            ChatHistory.Add(new ChatEntry("narrator", game.StoryIntroduction, DateTime.Now));
        if (!string.IsNullOrEmpty(game.GameObjective))
            ChatHistory.Add(new ChatEntry("system", $"Objective: {game.GameObjective}", DateTime.Now));
    }

    /// <summary>
    /// Process a player command and return the game response.
    /// </summary>
    public async Task<string> ProcessActionAsync(string command)
    {
        if (_gameMaster == null || _gameState == null || _game == null)
            return "No game is active.";
        if (IsGameOver)
            return "The game is over. Start a new game to continue.";

        IsProcessing = true;
        try
        {
            ChatHistory.Add(new ChatEntry("player", command, DateTime.Now));

            var response = await _gameMaster.ProcessPlayerActionAsync(command);
            ChatHistory.Add(new ChatEntry("narrator", response, DateTime.Now));

            // Check victory
            if (_game.WinConditionRoomIds?.Contains(_gameState.CurrentRoomId) == true)
            {
                IsVictory = true;
                ChatHistory.Add(new ChatEntry("system", "Victory! You have achieved the objective!", DateTime.Now));
            }
            var winCheck = _gameMaster.CheckWinCondition();
            if (winCheck.HasValue && winCheck.Value.isVictory)
            {
                IsVictory = true;
                ChatHistory.Add(new ChatEntry("system", winCheck.Value.message, DateTime.Now));
            }

            // Check death
            if (!_gameState.Player.IsAlive)
            {
                IsDead = true;
                ChatHistory.Add(new ChatEntry("system", "You have been defeated...", DateTime.Now));
            }

            await TryAutoSaveAsync();

            return response;
        }
        catch (Exception ex)
        {
            var error = $"Error: {ex.Message}";
            ChatHistory.Add(new ChatEntry("error", error, DateTime.Now));
            return error;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// End the current game session.
    /// </summary>
    public void EndGame()
    {
        _gameState = null;
        _gameMaster = null;
        _game = null;
        IsVictory = false;
        IsDead = false;
    }

    public bool HasSave(Game game) => File.Exists(GetSavePath(game.Id));

    public async Task SaveGameAsync()
    {
        if (_gameState == null || _game == null)
            throw new InvalidOperationException("No game is active.");

        await _saveService.SaveAsync(GetSavePath(_game.Id), _gameState, _game.Id);
        LastSaveStatus = $"Saved on turn {_gameState.TurnNumber}.";
    }

    public async Task LoadGameAsync(Game game)
    {
        var document = await _saveService.LoadAsync(GetSavePath(game.Id));
        if (!string.IsNullOrWhiteSpace(document.GameId) &&
            !document.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That save belongs to a different game.");
        }

        _game = game;
        _gameState = document.State;
        _gameMaster = new GameMaster(_gameState, _activeClient, null, game);
        ChatHistory.Clear();
        ChatHistory.Add(new ChatEntry(
            "system",
            $"Continued {game.Title} from turn {_gameState.TurnNumber}.",
            DateTime.Now));
        var winCheck = _gameMaster.CheckWinCondition();
        IsVictory = game.WinConditionRoomIds?.Contains(_gameState.CurrentRoomId) == true ||
                    winCheck is { isVictory: true };
        IsDead = !_gameState.Player.IsAlive;
        if (IsVictory)
        {
            ChatHistory.Add(new ChatEntry(
                "system",
                winCheck?.message ?? "Victory! You have achieved the objective!",
                DateTime.Now));
        }
        LastSaveStatus = $"Loaded save from {document.SavedAtUtc.ToLocalTime():g}.";
    }

    // Current room info for the sidebar
    public Room? GetCurrentRoom() => _gameState?.GetCurrentRoom();
    public List<Exit> GetExits() => _gameState?.GetCurrentRoom()?.GetAvailableExits() ?? new();
    public List<Character> GetNpcsInRoom()
    {
        if (_gameState == null) return new();
        var room = _gameState.GetCurrentRoom();
        return room.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id])
            .ToList();
    }

    // Chat mode info
    public bool InChatMode => _gameState?.InChatMode == true;
    public Character? GetChatNpc()
    {
        if (_gameState == null || !_gameState.InChatMode || _gameState.CurrentChatNpcId == null)
            return null;
        return _gameState.NPCs.TryGetValue(_gameState.CurrentChatNpcId, out var npc) ? npc : null;
    }
    public List<string> GetChatSuggestions() => _gameMaster?.GetChatSuggestions() ?? new();

    // LLM settings management
    public LlmSettings GetSettings() => _settings;

    /// <summary>
    /// Recreate the active LLM client from current settings.
    /// Changes take effect for the next game started.
    /// </summary>
    public void ApplySettings()
    {
        _activeClient = _settings.CreateClient();
        LlmStatus = null; // will refresh on next health check
    }

    /// <summary>
    /// List models available on the current backend.
    /// </summary>
    public Task<List<string>> ListModelsAsync() => _activeClient.ListModelsAsync();

    private async Task TryAutoSaveAsync()
    {
        try
        {
            await SaveGameAsync();
        }
        catch (Exception ex)
        {
            LastSaveStatus = $"Autosave failed: {ex.Message}";
        }
    }

    private string GetSavePath(string gameId)
    {
        var safeGameId = string.Concat(gameId.Where(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeGameId))
            safeGameId = "game";
        if (safeGameId.Length > 64)
            safeGameId = safeGameId[..64];

        var gameIdHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(gameId))).ToLowerInvariant();
        var saveFileName = $"{safeGameId}-{gameIdHash}.json";

        return Path.Combine(
            _dataDirectory,
            "saves",
            "web",
            _saveSlot.Id,
            saveFileName);
    }
}
