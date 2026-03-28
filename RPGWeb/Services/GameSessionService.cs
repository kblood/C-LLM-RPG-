using System.Text.Json;
using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;
using CSharpRPGBackend.Games;

namespace RPGWeb.Services;

public record ChatEntry(string Role, string Content, DateTime Timestamp);

/// <summary>
/// Scoped service (one per Blazor circuit / browser tab).
/// Manages a single game session including state, GameMaster, and chat history.
/// </summary>
public class GameSessionService
{
    private readonly ILlmClient _llmClient;
    private readonly LlmSettings _settings;
    private GameState? _gameState;
    private GameMaster? _gameMaster;
    private Game? _game;

    public bool IsGameActive => _gameState != null && _game != null && _gameMaster != null;
    public GameState? State => _gameState;
    public Game? CurrentGame => _game;
    public List<ChatEntry> ChatHistory { get; } = new();
    public bool IsProcessing { get; private set; }
    public bool IsVictory { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsGameOver => IsVictory || IsDead;
    public string? LlmStatus { get; private set; }

    public GameSessionService(ILlmClient llmClient, LlmSettings settings)
    {
        _llmClient = llmClient;
        _settings = settings;
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
            var healthy = await _llmClient.IsHealthyAsync();
            LlmStatus = healthy
                ? $"Connected to {_llmClient.BackendName} ({_settings.Model})"
                : $"Cannot reach {_llmClient.BackendName}";
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
        _gameState = new GameState
        {
            Rooms = game.Rooms,
            NPCs = game.NPCs,
            CurrentRoomId = game.StartingRoomId
        };

        // Initialize starting currency
        if (game.Economy?.Enabled == true && game.StartingCurrency > 0)
            _gameState.Player.Wallet.Add(game.StartingCurrency);

        // Add starting items
        if (game.CustomSettings.ContainsKey("startingItems"))
        {
            try
            {
                var json = game.CustomSettings["startingItems"];
                var items = JsonSerializer.Deserialize<List<StartingItemDefinition>>(json);
                if (items != null)
                {
                    foreach (var si in items)
                    {
                        if (game.Items.TryGetValue(si.ItemId, out var item))
                            _gameState.PlayerInventory.AddItem(item, si.Quantity);
                    }
                }
            }
            catch
            {
                AddDefaultStartingItems(game);
            }
        }
        else
        {
            AddDefaultStartingItems(game);
        }

        // Set player health from game definition
        if (game.InitialPlayerHealth > 0)
        {
            _gameState.Player.Health = game.InitialPlayerHealth;
            _gameState.Player.MaxHealth = game.InitialPlayerHealth;
        }

        _gameMaster = new GameMaster(_gameState, _llmClient, null, game);

        // Reset state
        ChatHistory.Clear();
        IsVictory = false;
        IsDead = false;

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

    private void AddDefaultStartingItems(Game game)
    {
        foreach (var item in game.Items.Values)
        {
            if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor)
                _gameState!.PlayerInventory.AddItem(item, 1);
        }
    }
}
