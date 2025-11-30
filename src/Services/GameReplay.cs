using System.Text;
using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Automated game player that plays games and logs sessions to markdown.
/// </summary>
public class GameReplay
{
    private readonly GameMaster _gameMaster;
    private readonly OllamaClient _ollamaClient;
    private readonly GameState _gameState;
    private readonly StringBuilder _log;
    private readonly Game _game;

    public GameReplay(GameState gameState, GameMaster gameMaster, OllamaClient ollamaClient, Game game)
    {
        _gameState = gameState;
        _gameMaster = gameMaster;
        _ollamaClient = ollamaClient;
        _game = game;
        _log = new StringBuilder();
    }

    /// <summary>
    /// Play the game with automated LLM-controlled player.
    /// </summary>
    public async Task<string> PlayGameAsync(int maxTurns = 50)
    {
        InitializeLog();

        _log.AppendLine("\n## Game Start\n");
        _log.AppendLine("> **Narrator:** " + _game.StoryIntroduction + "\n");
        _log.AppendLine($"> **Objective:** {_game.GameObjective}\n");

        var turn = 0;
        while (turn < maxTurns)
        {
            turn++;
            _log.AppendLine($"\n### Turn {turn}\n");

            var currentRoom = _gameState.GetCurrentRoom();
            var exits = currentRoom.GetAvailableExits();
            var npcs = currentRoom.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name)
                .ToList();
            var inventory = _gameState.PlayerInventory.Items.Count > 0
                ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => ii.Item.Name))
                : "empty";

            _log.AppendLine($"**Location:** {currentRoom.Name}\n");
            _log.AppendLine($"**Health:** {_gameState.Player.Health}/{_gameState.Player.MaxHealth}\n");

            if (exits.Count > 0)
                _log.AppendLine($"**Available Exits:** {string.Join(", ", exits.Select(e => e.DisplayName))}\n");

            if (npcs.Count > 0)
                _log.AppendLine($"**NPCs Here:** {string.Join(", ", npcs)}\n");

            _log.AppendLine($"**Inventory:** {inventory}\n");

            // Generate the next action through LLM
            var action = await GeneratePlayerActionAsync(currentRoom);
            _log.AppendLine($"> **Player:** {action}\n");

            // Process the action
            var result = await _gameMaster.ProcessPlayerActionAsync(action);
            _log.AppendLine($"> **Narrator:** {result}\n");

            // Check win condition
            if (_game.WinConditionRoomIds?.Contains(_gameState.CurrentRoomId) == true)
            {
                _log.AppendLine("\n## 🎉 Victory!\n");
                _log.AppendLine("The player has reached the goal!\n");
                break;
            }

            // Check if player died
            if (_gameState.Player.Health <= 0)
            {
                _log.AppendLine("\n## 💀 Game Over\n");
                _log.AppendLine("The player has fallen...\n");
                break;
            }
        }

        if (turn >= maxTurns)
        {
            _log.AppendLine($"\n## ⏱️ Session Ended\n");
            _log.AppendLine($"Game ended after {maxTurns} turns.\n");
        }

        _log.AppendLine("\n---\n");
        _log.AppendLine($"*Replay generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*\n");

        return _log.ToString();
    }

    /// <summary>
    /// Generate the next action for the player using LLM intelligence.
    /// </summary>
    private async Task<string> GeneratePlayerActionAsync(Room currentRoom)
    {
        var exits = currentRoom.GetAvailableExits();
        var npcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id].Name)
            .ToList();

        var inventory = _gameState.PlayerInventory.Items.Count > 0
            ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => ii.Item.Name))
            : "empty";

        var context = $@"You are playing a {_game.Style} RPG game. Your goal: {_game.GameObjective}

Current Status:
- Location: {currentRoom.Name}
- Description: {currentRoom.Description}
- Available exits: {(exits.Count > 0 ? string.Join(", ", exits.Select(e => e.DisplayName)) : "none")}
- NPCs here: {(npcs.Count > 0 ? string.Join(", ", npcs) : "none")}
- Inventory: {inventory}
- Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}

Based on the game state, what should you do next? Respond with a SINGLE ACTION only (no explanation).
Be strategic - explore new areas, talk to NPCs, examine items, and work toward the goal.
Use natural language like a real player would: 'go north', 'examine the sword', 'talk to the merchant', etc.

Your action:";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are an intelligent RPG player. Make strategic decisions to achieve the game objective." },
            new() { Role = "user", Content = context }
        };

        try
        {
            var response = await _ollamaClient.ChatAsync(messages);
            // Extract just the action (first 100 chars typically)
            var action = response.Split('\n')[0].Trim();
            return action.Length > 200 ? action[..200] : action;
        }
        catch
        {
            return "look around";
        }
    }

    /// <summary>
    /// Save the game log to a markdown file.
    /// </summary>
    public async Task SaveLogAsync(string filePath)
    {
        try
        {
            await File.WriteAllTextAsync(filePath, _log.ToString());
            Console.WriteLine($"✓ Game replay saved to: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error saving replay: {ex.Message}");
        }
    }

    private void InitializeLog()
    {
        _log.Clear();
        _log.AppendLine($"# {_game.Title} - Game Replay\n");
        _log.AppendLine($"**Game Style:** {_game.Style}\n");
        _log.AppendLine($"**Date:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        if (!string.IsNullOrEmpty(_game.Description))
            _log.AppendLine($"**Description:** {_game.Description}\n");

        _log.AppendLine("---\n");
    }
}
