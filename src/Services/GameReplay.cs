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
    private readonly ILlmClient _ollamaClient;
    private readonly GameState _gameState;
    private readonly StringBuilder _log;
    private readonly Game _game;

    // Replay bot state tracking to avoid repetitive behaviour
    private readonly HashSet<string> _visitedRooms = new();
    private readonly HashSet<string> _talkedToNpcs = new();
    private readonly List<string> _recentActions = new();
    private int _turnsSinceExplored = 0;
    private int _turnsSinceCombat = 0;

    public GameReplay(GameState gameState, GameMaster gameMaster, ILlmClient ollamaClient, Game game)
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
            _visitedRooms.Add(currentRoom.Id);

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

            // Track this action for repetition avoidance
            _recentActions.Add(action);
            if (_recentActions.Count > 8) _recentActions.RemoveAt(0);

            // Track exploration and combat timing
            var actionLower = action.ToLowerInvariant();
            if (actionLower.StartsWith("go ") || actionLower.StartsWith("move ") || actionLower.StartsWith("enter ") || actionLower.StartsWith("head "))
                _turnsSinceExplored = 0;
            else
                _turnsSinceExplored++;

            if (actionLower.Contains("attack") || actionLower.Contains("auto") || actionLower.Contains("fight"))
                _turnsSinceCombat = 0;
            else
                _turnsSinceCombat++;

            // Process the action
            var result = await _gameMaster.ProcessPlayerActionAsync(action);
            _log.AppendLine($"> **Narrator:** {result}\n");

            // After talking to an NPC, mark them as talked-to
            if (actionLower.StartsWith("talk") || actionLower.StartsWith("speak") || actionLower.StartsWith("ask"))
            {
                foreach (var npcId in currentRoom.NPCIds.Where(id => _gameState.NPCs.ContainsKey(id)))
                {
                    var npcName = _gameState.NPCs[npcId].Name.ToLowerInvariant();
                    if (actionLower.Contains(npcName) || currentRoom.NPCIds.Count == 1)
                        _talkedToNpcs.Add(npcId);
                }
            }

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
    /// Tracks visited rooms, talked-to NPCs, and recent actions to drive varied, goal-oriented play.
    /// </summary>
    private async Task<string> GeneratePlayerActionAsync(Room currentRoom)
    {
        var exits = currentRoom.GetAvailableExits();
        var allNpcsInRoom = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id])
            .ToList();

        var inventory = _gameState.PlayerInventory.Items.Count > 0
            ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => $"{ii.Item.Name} x{ii.Quantity}"))
            : "empty";

        // Classify NPCs in the room
        var enemyRoles = new HashSet<NPCRole> { NPCRole.Warrior, NPCRole.Boss };
        var aliveEnemies = allNpcsInRoom
            .Where(n => n.IsAlive && (enemyRoles.Contains(n.Role) || n.Alignment == CharacterAlignment.Evil))
            .ToList();
        var aliveAllies = allNpcsInRoom
            .Where(n => n.IsAlive && !enemyRoles.Contains(n.Role) && n.Alignment != CharacterAlignment.Evil)
            .ToList();
        var freshAllies = aliveAllies.Where(n => !_talkedToNpcs.Contains(n.Id)).ToList();

        // Unvisited exits lead to unexplored rooms
        var unvisitedExits = exits
            .Where(e => !_visitedRooms.Contains(e.DestinationRoomId))
            .ToList();
        var visitedExits = exits
            .Where(e => _visitedRooms.Contains(e.DestinationRoomId))
            .ToList();

        // Items lying on the floor
        var floorItems = currentRoom.Items.Where(i => i != null).ToList();

        // Active quests
        var activeQuestSummary = _gameState.ActiveQuests.Count > 0
            ? string.Join("; ", _gameState.ActiveQuests.Select(q => $"{q.Title}: {q.Description}"))
            : "none";

        // Health status
        var healthPct = _gameState.Player.MaxHealth > 0
            ? (double)_gameState.Player.Health / _gameState.Player.MaxHealth
            : 1.0;
        var healthStatus = healthPct < 0.25 ? "CRITICAL - below 25%"
            : healthPct < 0.5 ? "LOW - below 50%"
            : "OK";

        // Consumables in inventory
        var healingItems = _gameState.PlayerInventory.Items.Values
            .Where(ii => ii.Item.Type == ItemType.Consumable || ii.Item.IsConsumable)
            .Select(ii => ii.Item.Name)
            .ToList();

        // Build prioritised strategic hints for the bot
        var strategicHints = new List<string>();

        if (_gameState.InCombatMode && _gameState.CurrentCombatNpcId != null
            && _gameState.NPCs.TryGetValue(_gameState.CurrentCombatNpcId, out var combatTarget))
        {
            strategicHints.Add($"⚔️  YOU ARE IN COMBAT with {combatTarget.Name}. Use 'auto' to fight automatically until the battle ends OR 'attack {combatTarget.Name}' for a single strike. DO NOT flee unless health is critical.");
        }
        else if (aliveEnemies.Count > 0)
        {
            var enemy = aliveEnemies.First();
            strategicHints.Add($"⚔️  There is a hostile enemy here: {enemy.Name} (HP: {enemy.Health}/{enemy.MaxHealth}). ATTACK them! Use: 'attack {enemy.Name}' or 'auto'");
        }

        if (healthPct < 0.25)
        {
            if (healingItems.Count > 0)
                strategicHints.Add($"🩹 Health is CRITICAL. Use a healing item: 'use {healingItems.First()}'");
            else
                strategicHints.Add($"🩹 Health is critical and you have no healing items. Consider fleeing if in combat: 'flee'");
        }

        if (floorItems.Count > 0)
            strategicHints.Add($"📦 Items on the floor: {string.Join(", ", floorItems.Select(i => i.Name))}. Pick them up with 'take [item name]'");

        if (_gameState.ActiveQuests.Count > 0)
            strategicHints.Add($"📜 Active quests: {activeQuestSummary}. Work toward completing them.");

        if (unvisitedExits.Count > 0)
            strategicHints.Add($"🗺️  Unexplored exits (PRIORITISE THESE): {string.Join(", ", unvisitedExits.Select(e => $"'{e.DisplayName}'"))}");

        if (freshAllies.Count > 0 && !_gameState.InCombatMode)
            strategicHints.Add($"💬 NPCs you haven't spoken to yet: {string.Join(", ", freshAllies.Select(n => $"{n.Name} (use 'talk to {n.Name}')"))}");

        if (_turnsSinceExplored > 3 && visitedExits.Count > 0)
            strategicHints.Add($"🔄 You've been in this area for a while. Move somewhere new: {string.Join(" or ", visitedExits.Take(2).Select(e => $"'go {e.DisplayName}'"))}");

        // Recent actions to avoid
        var recentActionsText = _recentActions.Count > 0
            ? string.Join(", ", _recentActions.TakeLast(5).Select(a => $"'{a}'"))
            : "none";

        var strategicSection = strategicHints.Count > 0
            ? "\n\nSTRATEGIC PRIORITIES (follow these in order):\n" + string.Join("\n", strategicHints.Select((h, i) => $"{i + 1}. {h}"))
            : "";

        var context = $@"You are playing a {_game.Style} RPG. Your overall goal: {_game.GameObjective}

=== CURRENT GAME STATE ===
Location: {currentRoom.Name}
Description: {currentRoom.Description}
Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth} ({healthStatus})
In Combat: {(_gameState.InCombatMode ? $"YES - fighting {(_gameState.CurrentCombatNpcId != null && _gameState.NPCs.TryGetValue(_gameState.CurrentCombatNpcId, out var c) ? c.Name : "enemy")}" : "No")}

Available exits:
{(exits.Count > 0 ? string.Join("\n", exits.Select(e => $"  - '{e.DisplayName}' → {(e.Description ?? e.DestinationRoomId)}{(_visitedRooms.Contains(e.DestinationRoomId) ? " [visited]" : " [NEW - unexplored]")}")) : "  None")}

NPCs here:
{(allNpcsInRoom.Count > 0 ? string.Join("\n", allNpcsInRoom.Select(n => $"  - {n.Name} (Role: {n.Role}, HP: {n.Health}/{n.MaxHealth}, Alignment: {n.Alignment}){(!_talkedToNpcs.Contains(n.Id) && n.IsAlive ? " [not yet spoken to]" : n.IsAlive ? " [already talked to]" : " [DEAD]")}")) : "  None")}

Items on floor: {(floorItems.Count > 0 ? string.Join(", ", floorItems.Select(i => i.Name)) : "none")}
Your inventory: {inventory}
Active quests: {activeQuestSummary}

Recent actions (DO NOT repeat these): {recentActionsText}
{strategicSection}

=== HOW TO PLAY ===
- EXPLORATION: Use exact exit names like 'go north', 'go deeper into the forest', 'enter tavern', 'head south'
- COMBAT: 'attack [name]' to start a fight, 'auto' to auto-battle to the end, 'flee' to escape
- ITEMS: 'take [item]' to pick up, 'use [item]' to use a consumable, 'examine [item]' to inspect
- NPCS: 'talk to [name]' once per NPC - do not repeat conversations with the same NPC
- INFO: 'look' to survey surroundings, 'inventory' to check items, 'quests' to see quest log

=== BEHAVIOUR RULES ===
1. Always attack ENEMIES (warriors, bosses, evil-aligned NPCs) on sight
2. Use 'auto' when in combat - let the fight play out automatically  
3. Explore ALL unvisited exits before revisiting known rooms
4. Pick up items you find on the floor
5. Talk to each NPC only ONCE unless a quest requires follow-up
6. If low on health and no healing items, flee combat and find a healer
7. NEVER repeat the same action twice in a row

Choose ONE action. Respond with only the action, no explanation:";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = $"You are an expert RPG player making smart decisions in a {_game.Style} game. You prioritise: (1) surviving combat, (2) completing quests, (3) exploring new areas, (4) collecting useful items, (5) gathering information from NPCs. Always respond with a SINGLE short action command. Never explain your reasoning." },
            new() { Role = "user", Content = context }
        };

        try
        {
            var response = await _ollamaClient.ChatAsync(messages);
            var action = response.Split('\n')[0].Trim();
            // Strip any preamble like "Action:" or "I will..."
            foreach (var prefix in new[] { "action:", "i will", "i'll", "player:", "> " })
            {
                if (action.ToLowerInvariant().StartsWith(prefix))
                    action = action[(prefix.Length)..].Trim();
            }
            return action.Length > 200 ? action[..200] : action;
        }
        catch
        {
            // Smart fallback: prefer unvisited exits, then combat, then look
            if (_gameState.InCombatMode && _gameState.CurrentCombatNpcId != null
                && _gameState.NPCs.ContainsKey(_gameState.CurrentCombatNpcId))
                return "auto";
            if (aliveEnemies.Count > 0)
                return $"attack {aliveEnemies.First().Name}";
            if (unvisitedExits.Count > 0)
                return $"go {unvisitedExits.First().DisplayName}";
            if (exits.Count > 0)
                return $"go {exits.First().DisplayName}";
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
