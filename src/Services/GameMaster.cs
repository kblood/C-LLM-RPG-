using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Display modes for the game state footer shown after each action.
/// </summary>
public enum GameStateDisplayMode
{
    Minimal,      // Location, Health, Exits, NPCs only
    Standard,     // Minimal + Currency and Inventory
    Detailed      // Standard + additional context
}

/// <summary>
/// The Game Master (GM) service orchestrates the game world, player actions, and LLM narration.
/// It interprets free-text player commands, applies game logic, and narrates outcomes via LLM.
/// </summary>
public class GameMaster
{
    private readonly GameState _gameState;
    private readonly ILlmClient _llmClient;
    private readonly Dictionary<string, NpcBrain> _npcBrains;
    private readonly string _gmSystemPrompt;
    private readonly CombatService _combatService;
    private readonly Game? _game;  // Optional reference to the game definition for win conditions
    private readonly EquipmentSlotConfiguration _equipmentSlots;  // Equipment slot configuration for this game
    private readonly EconomyConfig _economy;  // Economy configuration for this game
    private readonly GameMasterAuthority _authority;  // GM authority configuration
    private readonly CraftingConfig _crafting;  // Crafting configuration
    private readonly WorldSimulationService _worldSimulationService;
    private readonly QuestProgressionService _questProgressionService;
    private readonly WorldProjectService _worldProjectService;
    public bool DebugMode { get; set; } = false;  // Toggle for debug output
    public GameStateDisplayMode DisplayMode { get; set; } = GameStateDisplayMode.Standard;  // Footer display mode

    public GameMaster(
        GameState gameState,
        ILlmClient llmClient,
        string? gmSystemPrompt = null,
        Game? game = null,
        WorldSimulationService? worldSimulationService = null,
        QuestProgressionService? questProgressionService = null,
        WorldProjectService? worldProjectService = null,
        CombatService? combatService = null)
    {
        _gameState = gameState;
        _llmClient = llmClient;
        _npcBrains = new();
        _gmSystemPrompt = gmSystemPrompt ?? GenerateDefaultGMPrompt();
        _combatService = combatService ?? new CombatService();
        _game = game;
        _equipmentSlots = game?.GetEquipmentSlots() ?? EquipmentSlotConfiguration.CreateDefault();
        _economy = game?.GetEconomy() ?? EconomyConfig.Disabled();
        _authority = game?.GetAuthority() ?? GameMasterAuthority.Balanced();
        _crafting = game?.GetCrafting() ?? CraftingConfig.Disabled();
        _worldSimulationService = worldSimulationService ?? new WorldSimulationService();
        _questProgressionService = questProgressionService ?? new QuestProgressionService();
        _worldProjectService = worldProjectService ?? new WorldProjectService();

        // Initialize NPC brains with custom personalities
        InitializeNpcBrains();
    }

    /// <summary>
    /// Log debug information only if DebugMode is enabled.
    /// </summary>
    private void DebugLog(string message)
    {
        if (DebugMode)
            Console.WriteLine(message);
    }

    /// <summary>
    /// Process a free-text player command and return the game's response.
    /// This is the main game loop entry point.
    /// Uses a two-step process:
    /// 1. LLM decides what commands to execute based on player intent and game state
    /// 2. Execute commands and get results
    /// 3. LLM creates narrative based on original intent + actual outcomes
    /// </summary>
    public async Task<string> ProcessPlayerActionAsync(string playerCommand)
    {
        // Add command to history for future context
        _gameState.AddRecentCommand(playerCommand);

        // In chat mode: route most input directly as chat messages to the NPC
        if (_gameState.InChatMode && _gameState.CurrentChatNpcId != null)
        {
            var cmdLower = playerCommand.Trim().ToLower();
            // Check for exit commands
            if (cmdLower == "leave" || cmdLower == "bye" || cmdLower == "goodbye" || cmdLower == "end chat" || cmdLower == "exit")
            {
                var endResult = HandleEndChat();
                return endResult.Message;
            }
            // Check for game actions that should break out of chat
            if (cmdLower.StartsWith("go ") || cmdLower.StartsWith("move ") || cmdLower.StartsWith("attack ") ||
                cmdLower.StartsWith("take ") || cmdLower.StartsWith("equip ") || cmdLower.StartsWith("use ") ||
                cmdLower == "look" || cmdLower == "inventory" || cmdLower == "status" || cmdLower == "quests" ||
                cmdLower == "projects" || cmdLower.StartsWith("accept ") || cmdLower.StartsWith("turn in ") ||
                cmdLower.StartsWith("contribute ") || cmdLower.StartsWith("donate ") ||
                cmdLower == "flee" || cmdLower == "auto")
            {
                HandleEndChat();
                // Fall through to normal processing below
            }
            else
            {
                // Send directly to NPC as a chat message (skip LLM decision step)
                var beforeChat = CaptureActionSnapshot();
                var chatResult = await HandleChatAsync(playerCommand.Trim());
                var chatPlan = new ActionPlan
                {
                    Action = "chat",
                    Target = _gameState.CurrentChatNpcId ?? string.Empty,
                    Details = playerCommand.Trim()
                };
                var chatWorldUpdate = AdvanceWorld(
                    playerCommand,
                    new[] { chatPlan },
                    new[] { ("chat", chatResult) },
                    beforeChat);
                var chatMessage = chatResult.Success ? chatResult.Message : $"❌ {chatResult.Message}";
                return AppendWorldUpdates(chatMessage, chatWorldUpdate.Messages);
            }
        }

        // Step 1: Ask LLM what commands it wants to execute based on player intent and game state
        var commandsToExecute = await DecideActionsAsync(playerCommand);

        // If no actions were decided, return error
        if (commandsToExecute.Count == 0)
        {
            return BuildGameResponse("I don't understand what you're trying to do. Try describing your action more clearly.");
        }

        // Step 2: Execute those commands and collect results
        var beforeAction = CaptureActionSnapshot();
        var executionResults = new List<(string action, ActionResult result)>();
        foreach (var action in commandsToExecute)
        {
            var inventoryBeforeAction = CaptureInventoryQuantities();
            var result = await ApplyActionAsync(action, playerCommand);
            AddInventoryGainEvents(action, result, inventoryBeforeAction);
            executionResults.Add((action.Action, result));
        }

        // For informational commands (status, inventory, look, equipped, quests, recipes, shop, help, display), 
        // return the result directly without narration or game state footer
        var informationalCommands = new[] { "status", "inventory", "look", "equipped", "quests", "projects", "recipes", "shop", "help", "display" };
        if (commandsToExecute.Count == 1 && informationalCommands.Contains(commandsToExecute[0].Action.ToLower()))
        {
            var result = executionResults[0].result;
            return result.Success ? result.Message : $"❌ {result.Message}";
        }

        // A player intent advances deterministic world systems exactly once,
        // even when the intent expands into multiple concrete actions.
        var worldUpdate = AdvanceWorld(playerCommand, commandsToExecute, executionResults, beforeAction);

        // Step 3: Ask LLM to narrate based on original command + execution results
        var narration = await NarrateWithResultsAsync(playerCommand, commandsToExecute, executionResults);
        narration = AppendWorldUpdates(narration, worldUpdate.Messages);

        // Step 4: Check for win condition after executing actions
        var victoryCheck = CheckWinCondition();
        if (victoryCheck.HasValue && victoryCheck.Value.isVictory)
        {
            var victoryNarration = victoryCheck.Value.message;
            return BuildGameResponse($"🏆 **VICTORY!** 🏆\n\n{victoryNarration}");
        }

        // Build the complete response with game state
        var response = BuildGameResponse(narration);

        return response;
    }

    private ActionSnapshot CaptureActionSnapshot()
    {
        return new ActionSnapshot
        {
            RoomId = _gameState.CurrentRoomId,
            PlayerLevel = _gameState.Player.Level,
            NpcAlive = _gameState.NPCs.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.IsAlive,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private Dictionary<string, int> CaptureInventoryQuantities() =>
        _gameState.PlayerInventory.Items.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Quantity,
            StringComparer.OrdinalIgnoreCase);

    private void AddInventoryGainEvents(
        ActionPlan plan,
        ActionResult result,
        IReadOnlyDictionary<string, int> quantitiesBeforeAction)
    {
        if (!result.Success)
            return;

        var eventType = plan.Action.Trim().ToLowerInvariant() switch
        {
            "craft" => WorldEventType.ItemCrafted,
            "gather" or "search" => WorldEventType.ItemGathered,
            _ => WorldEventType.ItemAcquired
        };

        foreach (var (itemId, inventoryItem) in _gameState.PlayerInventory.Items)
        {
            var gained = inventoryItem.Quantity - quantitiesBeforeAction.GetValueOrDefault(itemId);
            if (gained <= 0)
                continue;

            result.Events.Add(WorldEvent.Create(
                eventType,
                targetId: itemId,
                quantity: gained,
                actorId: _gameState.Player.Id,
                roomId: _gameState.CurrentRoomId));
        }
    }

    private WorldTurnOutcome AdvanceWorld(
        string playerCommand,
        IReadOnlyList<ActionPlan> plans,
        IReadOnlyList<(string action, ActionResult result)> executionResults,
        ActionSnapshot before)
    {
        if (!plans.Any(ConsumesTurn))
            return new WorldTurnOutcome();

        var events = new List<WorldEvent>
        {
            WorldEvent.Create(
                WorldEventType.PlayerCommand,
                targetId: playerCommand,
                actorId: _gameState.Player.Id,
                roomId: _gameState.CurrentRoomId)
        };

        var actionEvents = executionResults
            .Where(execution => execution.result.Success)
            .SelectMany(execution => execution.result.Events)
            .ToList();
        foreach (var actionEvent in actionEvents)
        {
            // Some domain services journal immediately. Re-stamp those events on
            // the intent's turn before the simulation records the shared object.
            actionEvent.Id = string.Empty;
            actionEvent.TurnNumber = 0;
            events.Add(actionEvent);
        }

        if (!before.RoomId.Equals(_gameState.CurrentRoomId, StringComparison.OrdinalIgnoreCase))
        {
            events.Add(WorldEvent.Create(
                WorldEventType.RoomEntered,
                targetId: _gameState.CurrentRoomId,
                actorId: _gameState.Player.Id,
                roomId: _gameState.CurrentRoomId));
        }

        foreach (var (npcId, wasAlive) in before.NpcAlive)
        {
            if (wasAlive && _gameState.NPCs.TryGetValue(npcId, out var npc) && !npc.IsAlive)
            {
                events.Add(WorldEvent.Create(
                    WorldEventType.NpcDefeated,
                    targetId: npcId,
                    actorId: _gameState.Player.Id,
                    roomId: _gameState.CurrentRoomId));
            }
        }

        for (var index = 0; index < Math.Min(plans.Count, executionResults.Count); index++)
        {
            if (!executionResults[index].result.Success)
                continue;

            var action = plans[index].Action.Trim().ToLowerInvariant();
            if (action is not ("talk" or "chat" or "say"))
                continue;

            var npcId = ResolveNpcId(plans[index].Target) ?? _gameState.CurrentChatNpcId;
            if (!string.IsNullOrWhiteSpace(npcId))
            {
                events.Add(WorldEvent.Create(
                    WorldEventType.NpcTalkedTo,
                    targetId: npcId,
                    actorId: _gameState.Player.Id,
                    roomId: _gameState.CurrentRoomId));
            }
        }

        if (_gameState.Player.Level > before.PlayerLevel)
        {
            events.Add(WorldEvent.Create(
                WorldEventType.PlayerLeveled,
                targetId: _gameState.Player.Level.ToString(),
                actorId: _gameState.Player.Id,
                roomId: _gameState.CurrentRoomId,
                message: $"You reached level {_gameState.Player.Level}!"));
        }

        var simulationResult = _worldSimulationService.AdvanceTurn(_gameState, events);
        var levelBeforeQuestRewards = _gameState.Player.Level;
        var questResult = _questProgressionService.Process(_gameState, _game, simulationResult.Events);
        var postQuestEvents = questResult.Events.ToList();
        var progressionMessages = new List<string>();
        if (_gameState.Player.Level > levelBeforeQuestRewards)
        {
            var levelEvent = WorldEvent.Create(
                WorldEventType.PlayerLeveled,
                targetId: _gameState.Player.Level.ToString(),
                actorId: _gameState.Player.Id,
                roomId: _gameState.CurrentRoomId,
                message: $"You reached level {_gameState.Player.Level}!");
            levelEvent.TurnNumber = _gameState.TurnNumber;
            WorldEventJournal.Record(_gameState, levelEvent, "progression");
            postQuestEvents.Add(levelEvent);
            progressionMessages.Add(levelEvent.Message!);
        }

        var projectResult = _worldProjectService.Process(
            _gameState,
            simulationResult.Events.Concat(postQuestEvents));
        var messages = simulationResult.Messages
            .Concat(questResult.Messages)
            .Concat(progressionMessages)
            .Concat(projectResult.Messages)
            .ToList();

        return new WorldTurnOutcome
        {
            Events = simulationResult.Events
                .Concat(postQuestEvents)
                .Concat(projectResult.Events)
                .ToList(),
            Messages = messages.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static bool ConsumesTurn(ActionPlan plan)
    {
        return plan.Action.Trim().ToLowerInvariant() is not
            ("status" or "inventory" or "look" or "equipped" or "quests" or
             "recipes" or "shop" or "help" or "display" or "projects" or "unknown" or
             "end_chat" or "bye");
    }

    private string? ResolveNpcId(string? npcNameOrId)
    {
        if (string.IsNullOrWhiteSpace(npcNameOrId))
            return null;

        if (_gameState.NPCs.ContainsKey(npcNameOrId))
            return npcNameOrId;

        return _gameState.NPCs.FirstOrDefault(pair =>
            pair.Value.Name.Contains(npcNameOrId, StringComparison.OrdinalIgnoreCase) ||
            npcNameOrId.Contains(pair.Value.Name, StringComparison.OrdinalIgnoreCase)).Key;
    }

    private WorldProject? ResolveProject(string? projectNameOrId)
    {
        if (string.IsNullOrWhiteSpace(projectNameOrId))
            return null;

        var normalized = projectNameOrId.Replace('_', ' ').Trim();
        return _gameState.WorldProjects.FirstOrDefault(project =>
            project.Id.Equals(projectNameOrId, StringComparison.OrdinalIgnoreCase) ||
            project.Name.Equals(projectNameOrId, StringComparison.OrdinalIgnoreCase) ||
            project.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            project.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 4)
                .Any(word => normalized.Contains(word.Trim('\'', ',', '.'), StringComparison.OrdinalIgnoreCase)));
    }

    private Quest? ResolveQuest(string? questNameOrId)
    {
        if (string.IsNullOrWhiteSpace(questNameOrId))
            return null;

        return _gameState.ActiveQuests.FirstOrDefault(quest =>
            quest.Id.Equals(questNameOrId, StringComparison.OrdinalIgnoreCase) ||
            quest.Title.Equals(questNameOrId, StringComparison.OrdinalIgnoreCase) ||
            quest.Title.Contains(questNameOrId, StringComparison.OrdinalIgnoreCase) ||
            questNameOrId.Contains(quest.Title, StringComparison.OrdinalIgnoreCase));
    }

    private static string AppendWorldUpdates(string narrative, IEnumerable<string> messages)
    {
        var updates = messages.Where(message => !string.IsNullOrWhiteSpace(message)).ToList();
        if (updates.Count == 0)
            return narrative;

        return $"{narrative.TrimEnd()}\n\n🌍 World updates:\n{string.Join("\n", updates.Select(message => $"• {message}"))}";
    }

    private sealed class ActionSnapshot
    {
        public string RoomId { get; init; } = string.Empty;
        public int PlayerLevel { get; init; }
        public Dictionary<string, bool> NpcAlive { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WorldTurnOutcome
    {
        public List<WorldEvent> Events { get; init; } = new();
        public List<string> Messages { get; init; } = new();
    }

    /// <summary>
    /// Build complete response including narration and game state.
    /// </summary>
    private string BuildGameResponse(string narration)
    {
        // Combat mode narration already includes all necessary info
        if (_gameState.InCombatMode)
        {
            return narration;
        }

        var currentRoom = _gameState.GetCurrentRoom();
        var exits = currentRoom.GetAvailableExits();

        // Build NPC list distinguishing alive and dead, with merchant indicators
        var aliveNpcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
            .Select(id => {
                var npc = _gameState.NPCs[id];
                // Add merchant indicator if economy is enabled and NPC is a merchant
                if (_economy.Enabled && npc.Role == NPCRole.Merchant)
                    return $"{npc.Name} 🛒";
                return npc.Name;
            })
            .ToList();
        var deadNpcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && !_gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id].Name)
            .ToList();

        var npcsList = "";
        if (aliveNpcs.Count > 0)
            npcsList = string.Join(", ", aliveNpcs);
        if (deadNpcs.Count > 0)
            npcsList += (npcsList.Length > 0 ? " | ☠️ " : "☠️ ") + string.Join(", ", deadNpcs);

        // Build inventory list with equipped indicators
        string inventory;
        if (_gameState.PlayerInventory.Items.Count > 0)
        {
            var equippedIds = _gameState.Player.EquipmentSlots.Values.Where(id => id != null).ToHashSet();
            inventory = string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii =>
            {
                bool isEquipped = equippedIds.Contains(ii.Item.Id);
                string equippedIcon = isEquipped ? " ⚔️" : "";
                return $"{ii.Item.Name}{equippedIcon}";
            }));
        }
        else
        {
            inventory = "empty";
        }

        var response = new StringBuilder();
        response.AppendLine(narration);
        response.AppendLine();
        response.AppendLine("---");
        response.AppendLine();

        // Always show location and health
        response.AppendLine($"📍 **Location:** {currentRoom.Name}");
        response.AppendLine($"🕰️ **Turn:** {_gameState.TurnNumber} | ⭐ **Level:** {_gameState.Player.Level}");
        
        // Health line - add combat stats in Detailed mode
        if (DisplayMode == GameStateDisplayMode.Detailed)
        {
            // Calculate total combat stats
            var equippedItems = new Dictionary<string, Item>();
            foreach (var slot in _gameState.Player.EquipmentSlots)
            {
                if (slot.Value != null && _gameState.PlayerInventory.Items.TryGetValue(slot.Value, out var invItem))
                {
                    equippedItems[slot.Key] = invItem.Item;
                }
            }

            int totalDamage = _gameState.Player.GetTotalDamage(equippedItems.Values.FirstOrDefault(i => i.DamageBonus > 0));
            int totalArmor = _gameState.Player.GetTotalArmor(equippedItems);

            response.AppendLine($"❤️ **Health:** {_gameState.Player.Health}/{_gameState.Player.MaxHealth} | ⚔️ **DMG:** {totalDamage} | 🛡️ **ARM:** {totalArmor}");
        }
        else
        {
            response.AppendLine($"❤️ **Health:** {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
        }

        // Standard and Detailed modes show currency if economy is enabled
        if (DisplayMode >= GameStateDisplayMode.Standard && _economy.Enabled)
        {
            var currencyDisplay = _gameState.Player.Wallet.Format(_economy);
            response.AppendLine($"💰 **Currency:** {currencyDisplay}");
        }

        // Always show exits and NPCs (part of minimal mode)
        if (exits.Count > 0)
            response.AppendLine($"🚪 **Exits:** {string.Join(", ", exits.Select(e => e.DisplayName))}");

        if (npcsList.Length > 0)
            response.AppendLine($"👥 **NPCs Here:** {npcsList}");

        // Standard and Detailed modes show inventory
        if (DisplayMode >= GameStateDisplayMode.Standard)
        {
            response.AppendLine($"🎒 **Inventory:** {inventory}");
        }

        return response.ToString();
    }

    /// <summary>
    /// Get a response from an NPC the player is talking to.
    /// </summary>
    public async Task<string> TalkToNpcAsync(string npcId, string playerMessage)
    {
        var npc = _gameState.GetNPCInRoom(npcId);
        if (npc == null)
            return "That NPC is not here.";

        if (!_npcBrains.ContainsKey(npcId))
        {
            return $"{npc.Name} doesn't respond.";
        }

        var response = await _npcBrains[npcId].RespondToPlayerAsync(playerMessage);
        return response;
    }

    /// <summary>
    /// Get the current game status as narrative text.
    /// </summary>
    public async Task<string> GetGameStatusAsync()
    {
        var currentRoom = _gameState.GetCurrentRoom();
        var context = $@"
Current Location: {currentRoom.Name}
Description: {currentRoom.Description}

Player Status:
- Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}
- Level: {_gameState.Player.Level}
- Experience: {_gameState.Player.Experience}

NPCs here: {string.Join(", ", currentRoom.NPCIds.Select(id => _gameState.NPCs.ContainsKey(id) ? _gameState.NPCs[id].Name : "Unknown"))}

Adjacent locations: {string.Join(", ", currentRoom.GetAvailableExits().Select(e => e.DisplayName))}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are a fantasy RPG narrator. Describe the current game state vividly and engagingly in 2-3 sentences." },
            new() { Role = "user", Content = $"Describe this game state:\n{context}" }
        };

        return await _llmClient.ChatAsync(messages);
    }

    /// <summary>
    /// Step 1: Ask the LLM what actions to execute based on player command and game state.
    /// The LLM intelligently decides what sequence of commands would best fulfill the player's intent.
    /// </summary>
    private async Task<List<ActionPlan>> DecideActionsAsync(string playerCommand)
    {
        var currentRoom = _gameState.GetCurrentRoom();

        // Build context about available actions
        var knownExits = currentRoom.Exits.Values.ToList();
        var npcList = string.Join(", ", currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => $"{_gameState.NPCs[id].Name} ({(_gameState.NPCs[id].IsAlive ? "alive" : "dead")})"));

        // Enhanced inventory display with item types
        var playerItemList = string.Join(", ", _gameState.PlayerInventory.Items
            .Select(kvp => {
                var item = kvp.Value.Item;
                var suffix = "";
                if (item.IsConsumable) suffix = " (consumable)";
                else if (item.IsEquippable) suffix = " (equipment)";
                return $"{item.Name}{suffix}";
            }));

        // NPC inventory context - show what NPCs are carrying
        var npcInventoryList = "";
        var npcsInRoom = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id])
            .ToList();

        if (npcsInRoom.Any(npc => npc.CarriedItems.Count > 0))
        {
            var npcItems = npcsInRoom
                .Where(npc => npc.CarriedItems.Count > 0)
                .Select(npc => $"{npc.Name} has: {string.Join(", ", npc.CarriedItems.Values.Select(ii => ii.Item.Name))}")
                .ToList();
            npcInventoryList = "\nNPC inventory: " + string.Join("; ", npcItems);
        }

        var exitsList = knownExits.Count > 0
            ? string.Join(", ", knownExits.Select(exit =>
                exit.IsAvailable ? exit.DisplayName : $"{exit.DisplayName} [blocked: {exit.UnavailableReason ?? "unavailable"}]"))
            : "None";
        var questList = _gameState.ActiveQuests.Count > 0
            ? string.Join(", ", _gameState.ActiveQuests.Select(quest => $"{quest.Title} [{quest.Id}, {quest.Status}]"))
            : "None";
        var projectList = _gameState.WorldProjects.Count > 0
            ? string.Join(", ", _gameState.WorldProjects.Select(project => $"{project.Name} [{project.Id}, {project.Status}]"))
            : "None";

        var context = $@"Current Location: {currentRoom.Name}
Available exits: {exitsList}
NPCs here: {(string.IsNullOrEmpty(npcList) ? "None" : npcList)}{npcInventoryList}
Your inventory: {(string.IsNullOrEmpty(playerItemList) ? "Empty" : playerItemList)}
Quests: {questList}
World projects: {projectList}
Player health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}
In combat: {_gameState.InCombatMode}";

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = @"You are a game AI. Your ONLY job is to decide what commands to execute.

RESPONSE FORMAT - Return ONLY a JSON array with no markdown, no explanation, no code blocks:
[{""action"":""ACTION"",""target"":""TARGET"",""details"":""""}]

IMPORTANT: 'target' MUST contain the specific thing the player referred to:
- For 'examine body' -> ""target"":""body""
- For 'take sword' -> ""target"":""sword""
- For 'attack Dr. Chen' -> ""target"":""Dr. Sarah Chen""
- For 'move north' -> ""target"":""north""
- For 'look' -> ""target"":""""
- For 'inventory' -> ""target"":""""

Valid actions: move, look, inventory, talk, follow, examine, take, drop, use, attack, auto, give, equip, unequip, equipped, buy, sell, shop, gather, search, craft, recipes, quests, accept, turnin, projects, contribute, stop, status, display, help

COMMAND MODIFIERS:
- 'status' has optional target: empty = simple status (health, exits, NPCs), 'detailed'/'stats'/'advanced' = full combat stats
- 'inventory' has optional target: empty = simple list, 'detailed'/'equipped' = show equipped items with ⚔️ icon
- 'display' has optional target: 'minimal'/'standard'/'detailed' = changes footer display mode for all subsequent actions

Rules:
1. Return ONLY the JSON array - no explanation, no code blocks, no markdown
2. The 'target' field is CRITICAL - it must contain the exact thing the player wants to interact with (use full NPC names from context)
3. The 'details' field should contain the full item/target name - resolve abbreviations to full names from available inventory
4. Match the player's intent EXACTLY - DO NOT add extra actions the player didn't ask for
5. Each action must directly correspond to something the player explicitly stated
6. Only return multiple commands if the player explicitly asked for multiple actions
7. If the command is unclear, return an empty array: []

ITEM RESOLUTION:
- If player says 'use stim' and you see 'Combat Stimulant (consumable)' in context, use the FULL name 'Combat Stimulant'
- If player says 'give stims to chen' and Chen has 'Combat Stimulant', put 'Combat Stimulant' in details
- Always resolve abbreviations and short names to exact item names from the provided context
- If you cannot resolve the item name from context, still make your best guess based on player intent

ECONOMY COMMANDS (only if merchant NPCs are present):
- buy: Player wants to purchase an item from a merchant. Target = NPC name, Details = item name
- sell: Player wants to sell an item to a merchant. Target = NPC name, Details = item name  
- shop: Player wants to see what a merchant has for sale. Target = NPC name

GATHERING & CRAFTING:
- gather/search: Player wants to search for resources. Target = what they're looking for (ore, herbs, etc). Details = location context
- craft: Player wants to craft an item or ask NPC to craft. Target = NPC name (if asking NPC). Details = item to craft
- recipes: Player wants to see available recipes. Target = NPC name (optional, for NPC recipes)
- quests: Player wants to see their quest log
- accept: Player wants to accept a quest. Target = quest ID or title
- turnin: Player wants to turn in a completed quest. Target = quest ID or title
- projects: Player wants to view long-running world projects
- contribute: Player wants to donate materials to a project. Target = project ID or name, Details = item name and optional quantity

Examples of CORRECT responses:
Player says 'attack chen' -> [{""action"":""attack"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'flee' -> [{""action"":""flee"",""target"":"""",""details"":""""}]
Player says 'check chen health' -> [{""action"":""examine"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'ask chen for stims' AND context shows 'Dr. Sarah Chen has: Combat Stimulant' -> [{""action"":""give"",""target"":""Dr. Sarah Chen"",""details"":""Combat Stimulant""}]
Player says 'use stim' AND context shows 'Your inventory: Combat Stimulant (consumable)' -> [{""action"":""use"",""target"":"""",""details"":""Combat Stimulant""}]
Player says 'use stim on chen' AND context shows both items -> [{""action"":""use"",""target"":""Dr. Sarah Chen"",""details"":""Combat Stimulant""}]
Player says 'ask chen to follow' -> [{""action"":""follow"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'search her body' -> [{""action"":""examine"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'take the loot' -> [{""action"":""take"",""target"":""loot"",""details"":""""}]
Player says 'go out' -> [{""action"":""move"",""target"":""Out Into Corridor"",""details"":""""}]
Player says 'look around' -> [{""action"":""look"",""target"":"""",""details"":""""}]
Player says 'equip sword' AND context shows 'Iron Sword (equipment)' -> [{""action"":""equip"",""target"":""Iron Sword"",""details"":""""}]
Player says 'wear the helmet' -> [{""action"":""equip"",""target"":""helmet"",""details"":""""}]
Player says 'unequip sword' -> [{""action"":""unequip"",""target"":""sword"",""details"":""""}]
Player says 'remove boots' -> [{""action"":""unequip"",""target"":""boots"",""details"":""""}]
Player says 'show equipment' -> [{""action"":""equipped"",""target"":"""",""details"":""""}]
Player says 'what am I wearing' -> [{""action"":""equipped"",""target"":"""",""details"":""""}]
Player says 'buy sword from merchant' -> [{""action"":""buy"",""target"":""Silara the Merchant"",""details"":""Iron Sword""}]
Player says 'sell potion' AND context shows merchant -> [{""action"":""sell"",""target"":""Silara the Merchant"",""details"":""Health Potion""}]
Player says 'what do you have for sale' AND talking to merchant -> [{""action"":""shop"",""target"":""Silara the Merchant"",""details"":""""}]
Player says 'browse wares' -> [{""action"":""shop"",""target"":"""",""details"":""""}]
Player says 'search for ore' -> [{""action"":""gather"",""target"":""ore"",""details"":""""}]
Player says 'look for herbs' -> [{""action"":""gather"",""target"":""herbs"",""details"":""""}]
Player says 'forage for mushrooms' -> [{""action"":""gather"",""target"":""mushrooms"",""details"":""""}]
Player says 'ask blacksmith to forge a sword' -> [{""action"":""craft"",""target"":""Gruff the Blacksmith"",""details"":""sword""}]
Player says 'what can you craft' -> [{""action"":""recipes"",""target"":"""",""details"":""""}]
Player says 'status' -> [{""action"":""status"",""target"":"""",""details"":""""}]
Player says 'detailed status' OR 'stats' -> [{""action"":""status"",""target"":""detailed"",""details"":""""}]
Player says 'inventory' -> [{""action"":""inventory"",""target"":"""",""details"":""""}]
Player says 'detailed inventory' OR 'show equipped' -> [{""action"":""inventory"",""target"":""detailed"",""details"":""""}]
Player says 'check my quests' -> [{""action"":""quests"",""target"":"""",""details"":""""}]
Player says 'show projects' -> [{""action"":""projects"",""target"":"""",""details"":""""}]
Player says 'contribute 3 iron ore to the forgeworks' -> [{""action"":""contribute"",""target"":""ravensholm_forgeworks"",""details"":""3 iron ore""}]
Player says 'display minimal' OR 'minimal ui' -> [{""action"":""display"",""target"":""minimal"",""details"":""""}]
Player says 'display standard' -> [{""action"":""display"",""target"":""standard"",""details"":""""}]

COMBAT ACTIONS (only available when In combat: true):
- attack [npc]: Attack once. Target = NPC name, Details = empty
- auto/autofight/autoattack: Automatically attack until health drops to 5%. Target = empty, Details = empty
- flee/stop/retreat: Exit combat mode. Target = empty, Details = empty
- status: Show combat health bars. Target = empty, Details = empty

Combat Examples:
Player says 'attack' (IN COMBAT) -> [{""action"":""attack"",""target"":""Enemy Name"",""details"":""""}]
Player says 'auto' (IN COMBAT) -> [{""action"":""auto"",""target"":"""",""details"":""""}]
Player says 'autofight' (IN COMBAT) -> [{""action"":""auto"",""target"":"""",""details"":""""}]
Player says 'keep attacking' (IN COMBAT) -> [{""action"":""auto"",""target"":"""",""details"":""""}]
Player says 'status' (IN COMBAT) -> [{""action"":""status"",""target"":"""",""details"":""""}]
Player says 'flee' (IN COMBAT) -> [{""action"":""stop"",""target"":"""",""details"":""""}]

[]"
            },
            new() { Role = "user", Content = $"{context}\n\nPlayer command: {playerCommand}" }
        };

        try
        {
            var response = await _llmClient.ChatAsync(messages);

            // DEBUG: Log the raw LLM response
            DebugLog("[DEBUG] LLM Response:");
            DebugLog(response);
            DebugLog("[DEBUG] ---");

            var actionPlans = ParseActionJsonArray(response);

            DebugLog($"[DEBUG] Parsed {actionPlans.Count} actions from LLM response");
            foreach (var plan in actionPlans)
            {
                DebugLog($"[DEBUG]   Action: {plan.Action}, Target: {plan.Target}, Details: {plan.Details}");
            }

            // Always fallback to fallback parser if LLM returns empty or fails
            if (actionPlans.Count == 0)
            {
                DebugLog("[DEBUG] LLM returned no actions, trying fallback parser");
                var fallbackPlan = TryParseFallback(playerCommand, currentRoom);
                if (fallbackPlan != null)
                {
                    DebugLog($"[DEBUG] Fallback parser returned: {fallbackPlan.Action} -> {fallbackPlan.Target}");
                    actionPlans.Add(fallbackPlan);
                }
                else
                {
                    DebugLog("[DEBUG] Fallback parser returned null");
                }
            }

            return actionPlans;
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Exception in DecideActionsAsync: {ex.Message}");
            // Fallback on error
            var fallbackPlan = TryParseFallback(playerCommand, currentRoom);
            return fallbackPlan != null ? new List<ActionPlan> { fallbackPlan } : new List<ActionPlan>();
        }
    }

    /// <summary>
    /// Parse JSON array response from LLM into list of action plans.
    /// </summary>
    private List<ActionPlan> ParseActionJsonArray(string jsonResponse)
    {
        var result = new List<ActionPlan>();
        try
        {
            // Find JSON array in response
            var startIdx = jsonResponse.IndexOf('[');
            var endIdx = jsonResponse.LastIndexOf(']');

            DebugLog($"[DEBUG] ParseActionJsonArray: startIdx={startIdx}, endIdx={endIdx}");

            if (startIdx >= 0 && endIdx > startIdx)
            {
                var jsonStr = jsonResponse.Substring(startIdx, endIdx - startIdx + 1);
                DebugLog($"[DEBUG] Extracted JSON: {jsonStr}");
                var actions = System.Text.Json.JsonSerializer.Deserialize<List<ActionPlan>>(jsonStr);
                if (actions != null)
                {
                    DebugLog($"[DEBUG] Successfully deserialized {actions.Count} actions");
                    result.AddRange(actions);
                }
                else
                {
                    DebugLog("[DEBUG] Deserialization returned null");
                }
            }
            else
            {
                DebugLog("[DEBUG] Could not find JSON array brackets in response");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Exception parsing JSON: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Step 3: Ask the LLM to narrate based on the player's original intent and the actual action results.
    /// Dialogue actions (talk) are shown directly without narration.
    /// Other actions with side effects (give, attack, move, etc) are narrated.
    /// </summary>
    private async Task<string> NarrateWithResultsAsync(string playerCommand, List<ActionPlan> actions, List<(string action, ActionResult result)> results)
    {
        var currentRoom = _gameState.GetCurrentRoom();

        // Separate dialogue actions from other actions
        var dialogueLines = new List<string>();
        var narratedActions = new List<(string action, ActionResult result)>();

        foreach (var (action, result) in results)
        {
            var actionLower = action.ToLower();

            // Dialogue/talk actions are shown directly, not narrated
            if (actionLower == "talk")
            {
                dialogueLines.Add(result.Message);
            }
            // Follow actions: show NPC's dialogue directly, don't narrate
            else if (actionLower == "follow")
            {
                // HandleFollowAsync formats as: "NPC says: \"dialogue\"\n\nNPC joins your party and will follow you."
                // Show the NPC's response and follow confirmation directly
                dialogueLines.Add(result.Message);
            }
            // Give actions: show NPC's dialogue directly, narrate the item transfer
            else if (actionLower == "give")
            {
                // HandleGiveAsync formats as: "NPC says: \"dialogue\"\n\n✓ You received: items"
                // Split on blank line to separate dialogue from item transfer
                var parts = result.Message.Split(new[] { "\n\n" }, StringSplitOptions.None);
                if (parts.Length > 0)
                {
                    dialogueLines.Add(parts[0]); // NPC dialogue shown directly

                    // If there are items, create a narrated action for the transfer
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        narratedActions.Add(("item transfer", new ActionResult { Success = true, Message = parts[1] }));
                    }
                }
            }
            // Attack and combat actions should show the actual result, not be narrated
            else if (actionLower == "attack" || actionLower == "flee")
            {
                // Combat actions show results directly without narration
                // The action result already contains combat status from HandleAttack/HandleFlee
                dialogueLines.Add(result.Message);
            }
            else
            {
                // All other actions (examine, move, use, etc) get narrated
                narratedActions.Add((action, result));
            }
        }

        DebugLog("[DEBUG] NarrateWithResultsAsync:");
        DebugLog($"[DEBUG]   Original command: {playerCommand}");
        DebugLog($"[DEBUG]   Current location: {currentRoom.Name}");
        DebugLog($"[DEBUG]   Player health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
        DebugLog($"[DEBUG]   Dialogue lines: {dialogueLines.Count}");
        DebugLog($"[DEBUG]   Narrated actions: {narratedActions.Count}");

        // Build combined output: dialogue first, then narrated actions
        var output = new StringBuilder();

        // Show dialogue directly
        if (dialogueLines.Count > 0)
        {
            output.AppendLine(string.Join("\n\n", dialogueLines));
        }

        // Narrate non-dialogue actions
        if (narratedActions.Count > 0)
        {
            if (dialogueLines.Count > 0)
            {
                output.AppendLine();
            }

            var resultsSummary = new StringBuilder();
            resultsSummary.AppendLine("Action Results:");
            foreach (var (action, result) in narratedActions)
            {
                resultsSummary.AppendLine($"- {action}: {result.Message}");
            }

            var context = $@"Player's Original Request: {playerCommand}

Current Game State:
- Location: {currentRoom.Name}
- Description: {currentRoom.Description}
- Player Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}
- Inventory: {(string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(i => i.Item.Name)))}

{resultsSummary}

Now create vivid, engaging narrative (2-3 sentences) that describes what happened based on the player's intent and the actual results.";

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "system",
                    Content = @"You are a fantasy RPG narrator. Based on the player's original intent and the actual game results, create an engaging narrative description (2-3 sentences) of what happened.

CRITICAL RULES:
1. ONLY mention NPCs and locations that are already in the current location context
2. NEVER invent new NPCs, creatures, or characters that weren't mentioned
3. NEVER invent new rooms or locations beyond the current room
4. If an action failed, describe why based on the actual results message
5. If an action succeeded, describe what actually happened based on the results
6. Be creative with descriptions but strictly accurate to the game state
7. Do NOT add details about NPCs doing things not mentioned in the action results"
                },
                new() { Role = "user", Content = context }
            };

            try
            {
                var narrative = await _llmClient.ChatAsync(messages);
                output.AppendLine(narrative);
            }
            catch (Exception ex)
            {
                // The authoritative action has already committed. Preserve its
                // result (and allow hosts to autosave it) when narration is down.
                DebugLog($"[DEBUG] Narration unavailable; using action results: {ex.Message}");
                foreach (var (_, result) in narratedActions)
                    output.AppendLine(result.Success ? result.Message : $"❌ {result.Message}");
            }
        }

        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// Display current combat status with health bars.
    /// </summary>
    public string GetCombatStatus()
    {
        if (!_gameState.InCombatMode || string.IsNullOrEmpty(_gameState.CurrentCombatNpcId))
            return "Not in combat.";

        var npc = _gameState.NPCs[_gameState.CurrentCombatNpcId];
        var room = _gameState.GetCurrentRoom();

        var status = new StringBuilder();
        status.AppendLine("=== COMBAT MODE ===");
        status.AppendLine();
        status.AppendLine($"Location: {room.Name}");
        status.AppendLine();

        // Player health bar
        var playerHealthPercent = (_gameState.Player.Health * 100) / _gameState.Player.MaxHealth;
        var playerBar = BuildHealthBar(playerHealthPercent, 20);
        status.AppendLine($"You:      {playerBar} {_gameState.Player.Health}/{_gameState.Player.MaxHealth} HP");

        // Show companions if any
        var aliveCompanions = _gameState.Companions
            .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id])
            .ToList();

        if (aliveCompanions.Count > 0)
        {
            foreach (var companion in aliveCompanions)
            {
                var companionHealthPercent = (companion.Health * 100) / companion.MaxHealth;
                var companionBar = BuildHealthBar(companionHealthPercent, 20);
                status.AppendLine($"{companion.Name}: {companionBar} {companion.Health}/{companion.MaxHealth} HP 👥");
            }
        }

        status.AppendLine();

        // Enemy health bar
        var enemyHealthPercent = (npc.Health * 100) / npc.MaxHealth;
        var enemyBar = BuildHealthBar(enemyHealthPercent, 20);
        status.AppendLine($"{npc.Name}: {enemyBar} {npc.Health}/{npc.MaxHealth} HP");
        status.AppendLine();
        status.AppendLine("Commands: attack|auto|flee|status|stop");

        return status.ToString();
    }

    private string BuildHealthBar(int percent, int width)
    {
        int filledWidth = (percent * width) / 100;
        int emptyWidth = width - filledWidth;
        return $"[{new string('█', filledWidth)}{new string('░', emptyWidth)}] {percent:D3}%";
    }

    /// <summary>
    /// Get a list of available commands/actions for the player.
    /// </summary>
    public string GetAvailableActions()
    {
        var room = _gameState.GetCurrentRoom();
        var exits = room.GetAvailableExits();

        var actions = new List<string>
        {
            "=== Available Commands ===",
            "look - Examine your surroundings",
            "inventory - Check your items (simple list)",
            "inventory detailed - Show items with equipped indicators ⚔️",
            "examine [item/npc] - Look closely at something",
            "go [direction] - Travel in a direction (natural language works!)",
            "talk [npc] - Speak with an NPC",
            "give [npc] [items] - Ask an NPC to give you items",
            "use [item] - Use an item (potion, scroll, etc)",
            "take [item] - Pick up an item",
            "drop [item] - Drop an item from inventory",
            "attack [npc] - Attack an NPC (enters COMBAT MODE)",
            "status - Show simple status (health, exits, NPCs)",
            "status detailed - Show advanced stats (strength, agility, armor, bonuses)",
            "",
            "=== DISPLAY SETTINGS ===",
            "display - Show current display mode",
            "display minimal - Minimal footer (location, health, exits, NPCs)",
            "display standard - Standard footer (adds currency and inventory)",
            "display detailed - Shows combat stats (DMG/ARM) on health line",
            "",
        };

        // Add economy commands if enabled
        if (_economy.Enabled)
        {
            actions.Insert(actions.Count - 1, "=== ECONOMY COMMANDS ===");
            actions.Insert(actions.Count - 1, "shop - View a merchant's wares");
            actions.Insert(actions.Count - 1, "buy [item] - Buy an item from a merchant");
            actions.Insert(actions.Count - 1, "sell [item] - Sell an item to a merchant");
            actions.Insert(actions.Count - 1, "");
        }

        // Add crafting commands if enabled
        if (_crafting.Enabled)
        {
            actions.Insert(actions.Count - 1, "=== CRAFTING COMMANDS ===");
            actions.Insert(actions.Count - 1, "recipes - View available recipes from a crafter");
            actions.Insert(actions.Count - 1, "craft [item] - Ask a crafter to make something");
            actions.Insert(actions.Count - 1, "");
        }

        // Add gathering commands if room has resources or GM can create them
        if (room.Resources != null || _authority.CanDecideResources)
        {
            actions.Insert(actions.Count - 1, "=== GATHERING COMMANDS ===");
            actions.Insert(actions.Count - 1, "search [resource] - Search for resources (ore, herbs, etc.)");
            actions.Insert(actions.Count - 1, "gather/forage - Gather materials from the environment");
            actions.Insert(actions.Count - 1, "");
        }

        // Quest commands
        actions.Insert(actions.Count - 1, "quests - View your quest log");
        actions.Insert(actions.Count - 1, "accept [quest] - Accept an available quest");
        actions.Insert(actions.Count - 1, "turn in [quest] - Turn in a completed quest");

        if (_gameState.WorldProjects.Count > 0)
        {
            actions.Insert(actions.Count - 1, "projects - View changes you can help bring to the world");
            actions.Insert(actions.Count - 1, "contribute [quantity] [item] to [project] - Supply a world project");
        }

        if (_gameState.InCombatMode)
        {
            actions.Insert(actions.Count - 1, "=== COMBAT COMMANDS ===");
            actions.Insert(actions.Count - 1, "attack - Attack the enemy once");
            actions.Insert(actions.Count - 1, "auto - Keep fighting until you reach 5% health");
            actions.Insert(actions.Count - 1, "status - Show combat status with health bars");
            actions.Insert(actions.Count - 1, "stop - Exit combat mode (flee)");
            actions.Insert(actions.Count - 1, "");
        }

        actions.AddRange(new[]
        {
            "=== Current Location ===",
            $"Room: {room.Name}",
            $"Exits: {(exits.Count > 0 ? string.Join(", ", exits.Select(e => e.DisplayName)) : "None")}",
        });

        if (room.NPCIds.Count > 0)
        {
            var npcNames = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name)
                .ToList();
            if (npcNames.Count > 0)
                actions.Add($"NPCs: {string.Join(", ", npcNames)}");
        }

        // Show biome/resource info if available
        if (room.Resources?.Biome != null)
        {
            actions.Add($"Biome: {room.Resources.Biome}");
        }
        if (room.Resources?.ResourceTags.Count > 0)
        {
            actions.Add($"Resources: {string.Join(", ", room.Resources.ResourceTags)}");
        }

        actions.Add("");
        actions.Add("💡 Tip: You can use natural language! Try: 'go to the tavern', 'search for ore', 'ask blacksmith to craft sword', etc.");

        return string.Join("\n", actions);
    }

    private void InitializeNpcBrains()
    {
        foreach (var npc in _gameState.NPCs.Values)
        {
            var systemPrompt = npc.PersonalityPrompt ?? GenerateNpcPersonality(npc);
            _npcBrains[npc.Id] = new NpcBrain(_llmClient, npc, systemPrompt);
        }
    }

    /// <summary>
    /// Ask an NPC what items they're willing to give (decision step).
    /// Returns structured JSON decision, not dialogue.
    /// </summary>
    private async Task<NpcGiveDecision> GetNpcGiveDecisionAsync(Character npc, string playerRequest)
    {
        var itemsList = npc.CarriedItems.Count > 0
            ? string.Join(", ", npc.CarriedItems.Values.Select(ii => $"{ii.Item.Name}"))
            : "nothing";

        var prompt = $@"The player asks: ""{playerRequest}""

Your inventory: {itemsList}

Respond ONLY with JSON (no markdown, no explanation):
{{
  ""willGive"": true/false,
  ""itemsToGive"": [""item1"", ""item2""],
  ""reason"": ""why you will or won't give"",
  ""narrative"": ""what you say to the player about this""
}}

RULES:
1. willGive = true ONLY if you have items the player requested
2. itemsToGive = list of items you actually have from what they requested
3. reason = brief explanation (e.g. ""I don't have food"" or ""Happy to help"")
4. narrative = your dialogue response to them (1-2 sentences, stay in character)
5. Return ONLY the JSON - no other text
6. Never claim to have items not in your inventory list above";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = $"You are {npc.Name}. Decide what items to give the player based on your inventory." },
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _llmClient.ChatAsync(messages);
            DebugLog($"[DEBUG] NPC Give Decision Response:\n{response}\n[DEBUG] ---");

            return ParseGiveDecisionJson(response);
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Error getting NPC give decision: {ex.Message}");
            return new NpcGiveDecision { WillGive = false, Reason = "I'm confused", Narrative = "Sorry, I'm not sure what you want." };
        }
    }

    /// <summary>
    /// Parse NPC's JSON give decision response.
    /// </summary>
    private NpcGiveDecision ParseGiveDecisionJson(string jsonResponse)
    {
        try
        {
            // Try to extract JSON from response
            var jsonStart = jsonResponse.IndexOf('{');
            var jsonEnd = jsonResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var decision = System.Text.Json.JsonSerializer.Deserialize<NpcGiveDecision>(jsonStr, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return decision ?? new NpcGiveDecision { WillGive = false, Reason = "Error parsing response" };
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Error parsing give decision JSON: {ex.Message}");
        }

        return new NpcGiveDecision { WillGive = false, Reason = "Unable to understand" };
    }

    /// <summary>
    /// Fallback parser for simple commands that the LLM might miss
    /// </summary>
    private ActionPlan? TryParseFallback(string playerCommand, Room currentRoom)
    {
        var lower = playerCommand.ToLower().Trim();
        DebugLog($"[DEBUG] TryParseFallback: '{playerCommand}' -> '{lower}'");

        if (lower is "projects" or "project" or "world projects" or "show projects")
            return new ActionPlan { Action = "projects" };

        if (lower.StartsWith("accept "))
        {
            var questName = Regex.Replace(playerCommand.Trim(), "^accept\\s+(quest\\s+)?", "", RegexOptions.IgnoreCase);
            return new ActionPlan { Action = "accept", Target = questName };
        }

        if (lower.StartsWith("turn in ") || lower.StartsWith("turnin ") || lower.StartsWith("complete quest "))
        {
            var questName = Regex.Replace(
                playerCommand.Trim(),
                "^(turn\\s+in|turnin|complete\\s+quest)\\s+",
                "",
                RegexOptions.IgnoreCase);
            return new ActionPlan { Action = "turnin", Target = questName };
        }

        if (lower.StartsWith("contribute ") || lower.StartsWith("donate "))
        {
            var contribution = Regex.Replace(
                playerCommand.Trim(),
                "^(contribute|donate)\\s+",
                "",
                RegexOptions.IgnoreCase);
            var toIndex = contribution.LastIndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            var contributionDetails = toIndex >= 0 ? contribution[..toIndex].Trim() : contribution;
            var requestedProject = toIndex >= 0 ? contribution[(toIndex + 4)..].Trim() : string.Empty;
            var project = ResolveProject(requestedProject) ??
                          _gameState.WorldProjects.FirstOrDefault(candidate =>
                              candidate.Status is WorldProjectStatus.Available or WorldProjectStatus.Active);

            return new ActionPlan
            {
                Action = "contribute",
                Target = project?.Id ?? requestedProject,
                Details = contributionDetails
            };
        }

        // Check for direction keywords
        var directions = new[] { "north", "south", "east", "west", "up", "down", "left", "right", "forward" };
        foreach (var dir in directions)
        {
            if (lower == dir || lower.Contains($"go {dir}") || lower.Contains($"travel {dir}") || lower.Contains($"{dir}"))
            {
                // Check if this direction exists in current room
                var exit = currentRoom.FindExit(dir, availableOnly: false);
                if (exit != null)
                {
                    return new ActionPlan { Action = "move", Target = dir, Details = "" };
                }
            }
        }

        // Check for direction names from available exits (fuzzy keyword matching)
        var exits = currentRoom.Exits.Values.ToList();
        foreach (var exit in exits)
        {
            var exitNameLower = exit.DisplayName.ToLower();
            var exitWords = exitNameLower.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

            // Exact match
            if (lower == exitNameLower)
                return new ActionPlan { Action = "move", Target = exit.DisplayName, Details = "" };

            // Check if any keyword from exit name is in the command (with "go" prefix variations)
            var commandWords = lower.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var commonWords = new[] { "the", "a", "an", "to", "into", "at", "in", "on", "and" };
            var significantExitWords = exitWords.Where(w => !commonWords.Contains(w)).ToList();

            // Check if command contains go + any significant exit word
            foreach (var word in significantExitWords)
            {
                if (commandWords.Contains("go") && commandWords.Any(cw => cw == word || cw.StartsWith(word)))
                {
                    return new ActionPlan { Action = "move", Target = exit.DisplayName, Details = "" };
                }
            }

            // Also check if any significant exit word appears alone in the command
            foreach (var word in significantExitWords)
            {
                if (commandWords.Any(cw => cw == word || cw.StartsWith(word)))
                {
                    return new ActionPlan { Action = "move", Target = exit.DisplayName, Details = "" };
                }
            }

            // Check if command just contains the full exit name
            if (lower.Contains(exitNameLower))
            {
                return new ActionPlan { Action = "move", Target = exit.DisplayName, Details = "" };
            }
        }

        // Fallback: If player says "go" with no recognized exit, match the first available exit
        if ((lower == "go" || lower == "go out") && exits.Count > 0)
        {
            // Default to the first/most obvious exit
            var defaultExit = exits.FirstOrDefault(e => e.DisplayName.ToLower().Contains("out")) ?? exits[0];
            return new ActionPlan { Action = "move", Target = defaultExit.DisplayName, Details = "" };
        }

        // Check for single-word commands
        if (lower == "look" || lower == "look around")
            return new ActionPlan { Action = "look", Target = "", Details = "" };

        if (lower == "inventory" || lower == "inv" || lower == "i")
            return new ActionPlan { Action = "inventory", Target = "", Details = "" };

        if (lower == "help" || lower == "?")
            return new ActionPlan { Action = "help", Target = "", Details = "" };

        if (lower == "status" || lower == "stats")
            return new ActionPlan { Action = "status", Target = "", Details = "" };

        if (lower == "stop" || lower == "flee" || lower == "run" || lower == "exit combat")
            return new ActionPlan { Action = "stop", Target = "", Details = "" };

        // Check for equipment commands
        if (lower == "equipped" || lower == "equipment" || lower == "show equipment" ||
            lower == "what am i wearing" || lower == "what's equipped")
            return new ActionPlan { Action = "equipped", Target = "", Details = "" };

        if (lower.StartsWith("equip") || lower.StartsWith("wear") || lower.StartsWith("wield"))
        {
            // Extract item name from command
            var itemKeywords = new[] { "equip", "wear", "wield", "the", "a", "an", "my" };
            var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemWords = words.Where(w => !itemKeywords.Contains(w)).ToList();
            var itemName = string.Join(" ", itemWords);

            if (!string.IsNullOrWhiteSpace(itemName))
            {
                return new ActionPlan { Action = "equip", Target = itemName, Details = "" };
            }
        }

        if (lower.StartsWith("unequip") || lower.StartsWith("remove") || lower.StartsWith("dequip") ||
            lower.StartsWith("take off"))
        {
            // Extract item name or slot from command
            var itemKeywords = new[] { "unequip", "remove", "dequip", "take", "off", "the", "a", "an", "my" };
            var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var itemWords = words.Where(w => !itemKeywords.Contains(w)).ToList();
            var itemName = string.Join(" ", itemWords);

            if (!string.IsNullOrWhiteSpace(itemName))
            {
                return new ActionPlan { Action = "unequip", Target = itemName, Details = "" };
            }
        }

        // Check for attack commands
        if (lower.StartsWith("attack") || lower.StartsWith("fight") || lower.StartsWith("kill") || lower.StartsWith("hit"))
        {
            // In combat mode, if just "attack" or "fight" with no target, attack the current enemy
            if (_gameState.InCombatMode && !string.IsNullOrEmpty(_gameState.CurrentCombatNpcId))
            {
                var combatNpc = _gameState.NPCs[_gameState.CurrentCombatNpcId];
                return new ActionPlan
                {
                    Action = "attack",
                    Target = combatNpc.Name,
                    Details = lower
                };
            }
        }

        // Check for auto attack command
        if (lower.StartsWith("auto"))
        {
            // In combat mode, if in combat, use auto action
            if (_gameState.InCombatMode && !string.IsNullOrEmpty(_gameState.CurrentCombatNpcId))
            {
                return new ActionPlan
                {
                    Action = "auto",
                    Details = lower
                };
            }

            // Extract NPC name from command
            var npcNames = currentRoom.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name.ToLower())
                .ToList();

            foreach (var npcName in npcNames)
            {
                if (lower.Contains(npcName))
                {
                    // Extract the actual NPC name (not lowercased)
                    var actualNpc = currentRoom.NPCIds
                        .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                            _gameState.NPCs[id].Name.ToLower() == npcName);

                    if (actualNpc != null)
                    {
                        return new ActionPlan
                        {
                            Action = "attack",
                            Target = _gameState.NPCs[actualNpc].Name,
                            Details = lower
                        };
                    }
                }
            }
        }

        // Check for talk commands
        if (lower.StartsWith("talk") || lower.StartsWith("ask") || lower.StartsWith("speak"))
        {
            // Extract NPC name from command
            var npcNames = currentRoom.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name.ToLower())
                .ToList();

            foreach (var npcName in npcNames)
            {
                if (lower.Contains(npcName))
                {
                    // Extract the actual NPC name (not lowercased)
                    var actualNpc = currentRoom.NPCIds
                        .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                            _gameState.NPCs[id].Name.ToLower() == npcName);

                    if (actualNpc != null)
                    {
                        return new ActionPlan
                        {
                            Action = "talk",
                            Target = _gameState.NPCs[actualNpc].Name,
                            Details = lower // Full command as details for context
                        };
                    }
                }
            }
        }

        // Check for give/request commands (ask for items)
        if (lower.Contains("give") || lower.Contains("ask for") || lower.Contains("request") ||
            (lower.Contains("ask") && (lower.Contains("item") || lower.Contains("potion") || lower.Contains("food") ||
             lower.Contains("stim") || lower.Contains("kit") || lower.Contains("equipment"))))
        {
            // Extract NPC name from command
            var npcNames = currentRoom.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name.ToLower())
                .ToList();

            foreach (var npcName in npcNames)
            {
                if (lower.Contains(npcName))
                {
                    // Extract the actual NPC name (not lowercased)
                    var actualNpc = currentRoom.NPCIds
                        .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                            _gameState.NPCs[id].Name.ToLower() == npcName);

                    if (actualNpc != null)
                    {
                        return new ActionPlan
                        {
                            Action = "give",
                            Target = _gameState.NPCs[actualNpc].Name,
                            Details = lower // Full command as details for context
                        };
                    }
                }
            }
        }

        // Check for economy commands (buy, sell, shop)
        if (_economy.Enabled)
        {
            // Check for shop/browse commands
            if (lower == "shop" || lower == "browse" || lower == "wares" ||
                lower.Contains("what do you have") || lower.Contains("for sale") ||
                lower.Contains("show wares") || lower.Contains("show shop"))
            {
                return new ActionPlan { Action = "shop", Target = "", Details = "" };
            }

            // Check for buy commands
            if (lower.StartsWith("buy") || lower.StartsWith("purchase"))
            {
                var buyKeywords = new[] { "buy", "purchase", "from", "the", "a", "an" };
                var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var itemWords = words.Where(w => !buyKeywords.Contains(w)).ToList();
                var itemName = string.Join(" ", itemWords);

                // Try to find merchant in room
                var merchantNpc = currentRoom.NPCIds
                    .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                        _gameState.NPCs[id].Role == NPCRole.Merchant && _gameState.NPCs[id].IsAlive);
                var merchantName = merchantNpc != null ? _gameState.NPCs[merchantNpc].Name : "";

                return new ActionPlan { Action = "buy", Target = merchantName, Details = itemName };
            }

            // Check for sell commands
            if (lower.StartsWith("sell"))
            {
                var sellKeywords = new[] { "sell", "to", "the", "a", "an", "my" };
                var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var itemWords = words.Where(w => !sellKeywords.Contains(w)).ToList();
                var itemName = string.Join(" ", itemWords);

                // Try to find merchant in room
                var merchantNpc = currentRoom.NPCIds
                    .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                        _gameState.NPCs[id].Role == NPCRole.Merchant && _gameState.NPCs[id].IsAlive);
                var merchantName = merchantNpc != null ? _gameState.NPCs[merchantNpc].Name : "";

                return new ActionPlan { Action = "sell", Target = merchantName, Details = itemName };
            }
        }

        // Check for gathering commands
        if (lower.StartsWith("search") || lower.StartsWith("gather") || lower.StartsWith("forage") ||
            lower.StartsWith("mine") || lower.StartsWith("pick") || lower.Contains("look for"))
        {
            var gatherKeywords = new[] { "search", "gather", "forage", "mine", "pick", "look", "for", "the", "some", "any" };
            var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var targetWords = words.Where(w => !gatherKeywords.Contains(w)).ToList();
            var target = string.Join(" ", targetWords);
            if (string.IsNullOrWhiteSpace(target)) target = "resources";
            return new ActionPlan { Action = "gather", Target = target, Details = "" };
        }

        // Check for crafting commands
        if (lower.StartsWith("craft") || lower.Contains("forge") || lower.Contains("brew") || lower.Contains("make"))
        {
            var craftKeywords = new[] { "craft", "forge", "brew", "make", "create", "a", "an", "the", "me" };
            var words = lower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var targetWords = words.Where(w => !craftKeywords.Contains(w)).ToList();
            var target = string.Join(" ", targetWords);

            // Find crafter in room
            var crafterNpc = currentRoom.NPCIds
                .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                    _gameState.NPCs[id].CanCraft && _gameState.NPCs[id].IsAlive);
            var crafterName = crafterNpc != null ? _gameState.NPCs[crafterNpc].Name : "";

            return new ActionPlan { Action = "craft", Target = crafterName, Details = target };
        }

        // Check for recipe viewing
        if (lower.Contains("recipe") || lower.Contains("what can you make") || lower.Contains("what can you craft"))
        {
            var crafterNpc = currentRoom.NPCIds
                .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                    _gameState.NPCs[id].CanCraft && _gameState.NPCs[id].IsAlive);
            var crafterName = crafterNpc != null ? _gameState.NPCs[crafterNpc].Name : "";
            return new ActionPlan { Action = "recipes", Target = crafterName, Details = "" };
        }

        // Check for quest log
        if (lower.Contains("quest") || lower == "quests" || lower.Contains("quest log") || lower.Contains("my quests"))
        {
            return new ActionPlan { Action = "quests", Target = "", Details = "" };
        }

        return null;
    }

    private async Task<ActionPlan> ParseActionAsync(string playerCommand)
    {
        var currentRoom = _gameState.GetCurrentRoom();

        // Build context about available actions
        var availableExits = currentRoom.GetAvailableExits();
        var npcList = string.Join(", ", currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id].Name));
        var itemList = string.Join(", ", _gameState.PlayerInventory.Items
            .Select(kvp => $"{kvp.Value.Item.Name}"));

        var recentCommandsContext = _gameState.GetRecentCommandsContext();
        var context = $@"Current Location: {currentRoom.Name}
Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}
NPCs here: {(string.IsNullOrEmpty(npcList) ? "None" : npcList)}
Your inventory: {(string.IsNullOrEmpty(itemList) ? "Empty" : itemList)}
{(string.IsNullOrEmpty(recentCommandsContext) ? "" : recentCommandsContext)}";

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = @"You are parsing a player's action in a text adventure game and matching it to available game state.

Return ONLY valid JSON (no markdown, no code blocks, just raw JSON):
{
  ""action"": ""move|look|inventory|talk|examine|take|drop|use|attack|auto|help|unknown"",
  ""target"": ""the name/direction/item the player referred to"",
  ""details"": ""additional context from their command""
}

PROCESS:
1. Look at the AVAILABLE EXITS, NPCs, and INVENTORY provided below
2. Understand the player's INTENT from their command
3. Match their intent to the closest available target/action

ACTION RULES:
- move: Player wants to go somewhere. Target = exit name or direction they mentioned
- attack/fight/kill: Player wants to harm an NPC. Target = NPC name from the room
- auto/autoattack/autofight: Player wants to automatically attack until health drops to 5%. In combat mode only. Target = empty string
- talk/ask/speak: Player wants to speak with an NPC. Target = NPC name from the room
- examine/inspect: Player wants to look closely at something. Target = item/NPC/object name
- take/grab: Player wants to pick something up. Target = item name
- drop/throw: Player wants to discard something. Target = item name
- use/drink/eat/activate: Player wants to use an item. Target = item name
- look/search: Player wants to look around. Target = empty string
- inventory: Player wants to check items. Target = empty string
- help: Player asks for help. Target = empty string
- unknown: You cannot determine the intent

MATCHING STRATEGY:
- If player mentions 'auto', 'autoattack', or 'autofight' -> auto action (only in combat mode)
- If player mentions an NPC name with aggressive verbs (attack, fight, kill, hit, strike) -> attack action
- If player mentions an NPC name with social verbs (talk, ask, speak, chat) -> talk action
- If player mentions an exit or direction -> move action
- If player mentions an inventory item -> take/drop/use/examine action based on context
- Use fuzzy matching: 'gruk' matches 'King Gruk', 'north' matches 'North' exit, etc."
            },
            new() { Role = "user", Content = $"{context}\n\nPlayer command: {playerCommand}" }
        };

        try
        {
            var response = await _llmClient.ChatAsync(messages);
            var plan = ParseActionJson(response);

            // Fallback parser: If the LLM couldn't parse it or returned "unknown",
            // try to detect simple commands
            if (plan.Action == "unknown" || plan.Action == "help")
            {
                var fallbackPlan = TryParseFallback(playerCommand, currentRoom);
                if (fallbackPlan != null)
                    return fallbackPlan;
            }

            return plan;
        }
        catch
        {
            // Try fallback parser if LLM parsing fails entirely
            var fallbackPlan = TryParseFallback(playerCommand, currentRoom);
            if (fallbackPlan != null)
                return fallbackPlan;

            return new ActionPlan { Action = "unknown", Target = playerCommand, Details = "" };
        }
    }

    private async Task<ActionResult> ApplyActionAsync(ActionPlan plan, string playerCommand = "")
    {
        // In combat mode, prioritize combat commands
        if (_gameState.InCombatMode)
        {
            var actionLower = plan.Action.ToLower();
            if (actionLower == "stop" || actionLower == "flee")
            {
                return HandleStopCombat();
            }
            if (actionLower == "status")
            {
                return new ActionResult { Success = true, Message = GetCombatStatus() };
            }
        }

        // In chat mode, route chat commands
        if (_gameState.InChatMode)
        {
            var actionLower = plan.Action.ToLower();
            if (actionLower == "end_chat" || actionLower == "leave" || actionLower == "bye")
            {
                return HandleEndChat();
            }
            if (actionLower == "chat" || actionLower == "say")
            {
                var message = !string.IsNullOrEmpty(plan.Details) ? plan.Details : plan.Target;
                return await HandleChatAsync(message);
            }
            // If player types something that isn't a recognized action while in chat mode,
            // treat it as a chat message to the NPC
            if (actionLower != "move" && actionLower != "attack" && actionLower != "take" &&
                actionLower != "equip" && actionLower != "use" && actionLower != "inventory" &&
                actionLower != "status" && actionLower != "look" && actionLower != "quests" &&
                actionLower != "accept" && actionLower != "turnin" && actionLower != "projects" &&
                actionLower != "contribute" &&
                actionLower != "help" && actionLower != "shop" && actionLower != "buy" &&
                actionLower != "sell" && actionLower != "drop" && actionLower != "examine" &&
                actionLower != "unequip" && actionLower != "equipped" && actionLower != "gather" &&
                actionLower != "craft" && actionLower != "recipes" && actionLower != "display" &&
                actionLower != "auto" && actionLower != "give" && actionLower != "follow" &&
                actionLower != "search" && actionLower != "unknown")
            {
                // Treat the entire action as chat text
                var chatText = $"{plan.Action} {plan.Target} {plan.Details}".Trim();
                return await HandleChatAsync(chatText);
            }
            // For recognized game actions, exit chat mode first
            HandleEndChat();
        }

        return plan.Action.ToLower() switch
        {
            "move" => HandleMove(plan.Target),
            "look" => HandleLook(),
            "inventory" => HandleInventory(plan.Target),
            "talk" => await HandleTalkAsync(plan.Target, string.IsNullOrEmpty(plan.Details) ? playerCommand : plan.Details),
            "follow" => await HandleFollowAsync(plan.Target),
            "examine" => HandleExamine(plan.Target, playerCommand),
            "attack" => HandleAttack(plan.Target),
            "auto" => HandleAuto(),
            "take" => HandleTake(plan.Target),
            "drop" => HandleDrop(plan.Target),
            "use" => HandleUse(plan.Target, plan.Details),
            "give" => await HandleGiveAsync(plan.Target, string.IsNullOrEmpty(plan.Details) ? playerCommand : plan.Details),
            "equip" => HandleEquip(plan.Target),
            "unequip" => HandleUnequip(plan.Target),
            "equipped" => HandleEquipped(),
            "buy" => HandleBuy(plan.Target, plan.Details),
            "sell" => HandleSell(plan.Target, plan.Details),
            "shop" => HandleShop(plan.Target),
            "gather" => await HandleGatherAsync(plan.Target, plan.Details),
            "search" => await HandleGatherAsync(plan.Target, plan.Details),
            "craft" => HandleCraft(plan.Target, plan.Details),
            "recipes" => HandleRecipes(plan.Target),
            "chat" => await HandleChatAsync(!string.IsNullOrEmpty(plan.Details) ? plan.Details : plan.Target),
            "say" => await HandleChatAsync(!string.IsNullOrEmpty(plan.Details) ? plan.Details : plan.Target),
            "end_chat" => HandleEndChat(),
            "bye" => HandleEndChat(),
            "quests" => HandleQuests(),
            "accept" => HandleAcceptQuest(plan.Target),
            "turnin" => HandleTurnInQuest(plan.Target),
            "projects" => HandleProjects(),
            "contribute" => HandleProjectContribution(plan.Target, plan.Details),
            "help" => HandleHelp(),
            "status" => HandleStatus(plan.Target),
            "display" => HandleDisplayMode(plan.Target),
            "unknown" => HandleUnknownAction(plan.Target),
            _ => new ActionResult { Success = false, Message = "I don't understand that action." }
        };
    }

    private async Task<string> NarrateOutcomeAsync(ActionPlan plan, ActionResult result)
    {
        var currentRoom = _gameState.GetCurrentRoom();

        // Build rich context for narration
        var inventory = _gameState.PlayerInventory.Items.Count > 0
            ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => ii.Item.Name))
            : "empty";

        var npcList = "";
        var aliveNpcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id].Name)
            .ToList();
        var deadNpcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && !_gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id].Name)
            .ToList();

        if (aliveNpcs.Count > 0)
            npcList = "Alive: " + string.Join(", ", aliveNpcs);
        if (deadNpcs.Count > 0)
            npcList += (npcList.Length > 0 ? " | " : "") + "Dead: " + string.Join(", ", deadNpcs);
        if (npcList.Length == 0)
            npcList = "none";

        var exits = currentRoom.GetAvailableExits();
        var exitList = exits.Count > 0
            ? string.Join(", ", exits.Select(e => e.DisplayName))
            : "none";

        var context = $@"
Game State:
- Location: {currentRoom.Name}
- Room: {currentRoom.Description}
- Exits: {exitList}
- NPCs here: {npcList}
- Inventory: {inventory}
- Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}

Player Action: {plan.Action}
Target/Details: {plan.Target}
Action Result: {result.Message}";

        var systemPrompt = result.Success ? _gmSystemPrompt :
            _gmSystemPrompt + "\nThe player attempted an action that failed or was unusual. Describe what they tried and what happened in an engaging way.";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = $"Narrate this game outcome:\n{context}" }
        };

        return await _llmClient.ChatAsync(messages);
    }

    private ActionResult HandleMove(string exitName)
    {
        // Exit chat mode when moving
        if (_gameState.InChatMode)
        {
            _gameState.InChatMode = false;
            _gameState.CurrentChatNpcId = null;
        }

        var currentRoom = _gameState.GetCurrentRoom();
        var availableExits = currentRoom.GetAvailableExits();

        DebugLog($"[DEBUG] HandleMove: exitName='{exitName}', current room='{currentRoom.Name}'");
        DebugLog($"[DEBUG] Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}");

        // Try to find the exit
        var exit = currentRoom.FindExit(exitName, availableOnly: false);
        if (exit == null)
        {
            DebugLog($"[DEBUG] HandleMove: exit '{exitName}' not found");
            return new ActionResult
            {
                Success = false,
                Message = $"You can't go \"{exitName}\". Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}"
            };
        }

        var unlockMessage = string.Empty;
        var requiresAccessItem = !string.IsNullOrWhiteSpace(exit.RequiredItemId) ||
                                 !string.IsNullOrWhiteSpace(exit.RequiredKeyId);
        if (!exit.IsAvailable || requiresAccessItem)
        {
            if (!TryUnlockExit(exit, out unlockMessage))
            {
                var reason = exit.UnavailableReason ?? "That route is unavailable.";
                return new ActionResult { Success = false, Message = $"You cannot use {exit.DisplayName}: {reason}" };
            }
        }

        DebugLog($"[DEBUG] HandleMove: found exit, destination room='{exit.DestinationRoomId}'");

        if (_gameState.MoveToRoomByExit(exit.DisplayName))
        {
            var newRoom = _gameState.GetCurrentRoom();
            DebugLog($"[DEBUG] HandleMove: successfully moved to '{newRoom.Name}'");
            DebugLog($"[DEBUG] HandleMove: Player is now in room {_gameState.CurrentRoomId}");

            // Move all companions with the player
            DebugLog($"[DEBUG] HandleMove: Before moving companions, there are {_gameState.Companions.Count} companions");
            _gameState.MoveCompanionsToCurrentRoom();
            DebugLog($"[DEBUG] HandleMove: After moving companions");

            return new ActionResult
            {
                Success = true,
                Message = string.IsNullOrWhiteSpace(unlockMessage)
                    ? $"You go {exit.DisplayName}. You arrive at {newRoom.Name}."
                    : $"{unlockMessage} You go {exit.DisplayName} and arrive at {newRoom.Name}."
            };
        }

        DebugLog($"[DEBUG] HandleMove: MoveToRoomByExit returned false");
        return new ActionResult { Success = false, Message = "Cannot move there." };
    }

    private bool TryUnlockExit(Exit exit, out string message)
    {
        message = string.Empty;
        var inventory = _gameState.PlayerInventory.Items.Values;

        if (!string.IsNullOrWhiteSpace(exit.RequiredItemId) &&
            !inventory.Any(entry => entry.Item.Id.Equals(exit.RequiredItemId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(exit.RequiredKeyId))
        {
            var hasKey = inventory.Any(entry =>
                entry.Item.Id.Equals(exit.RequiredKeyId, StringComparison.OrdinalIgnoreCase) ||
                (entry.Item.IsKey &&
                 (entry.Item.UnlocksId?.Equals(exit.Id, StringComparison.OrdinalIgnoreCase) == true ||
                  entry.Item.UnlocksId?.Equals(exit.RequiredKeyId, StringComparison.OrdinalIgnoreCase) == true)));
            if (!hasKey)
                return false;
        }

        // Some exits are disabled by world state rather than a portable key.
        if (string.IsNullOrWhiteSpace(exit.RequiredItemId) && string.IsNullOrWhiteSpace(exit.RequiredKeyId))
            return false;

        exit.IsAvailable = true;
        exit.UnavailableReason = null;
        exit.RequiredItemId = null;
        exit.RequiredKeyId = null;
        message = "The way unlocks with the item you carry.";
        return true;
    }

    private ActionResult HandleLook()
    {
        var room = _gameState.GetCurrentRoom();
        var message = $"{room.Description}\n\n";

        // Add available exits
        var exits = room.GetAvailableExits();
        if (exits.Count > 0)
        {
            message += "You can go: " + string.Join(", ", exits.Select(e => e.DisplayName)) + "\n";
        }

        var blockedExits = room.Exits.Values.Where(exit => !exit.IsAvailable).ToList();
        if (blockedExits.Count > 0)
        {
            message += "Blocked routes: " + string.Join(", ", blockedExits.Select(exit =>
                $"{exit.DisplayName} ({exit.UnavailableReason ?? "unavailable"})")) + "\n";
        }

        if (room.Items.Count > 0)
            message += "On the ground: " + string.Join(", ", room.Items.Select(item => item.Name)) + "\n";

        // Add NPCs
        if (room.NPCIds.Count > 0)
        {
            var npcNames = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => {
                    var npc = _gameState.NPCs[id];
                    string marker = "";
                    if (!npc.IsAlive && HasLoot(npc))
                    {
                        marker = " 💰";
                    }
                    return npc.Name + marker;
                })
                .ToList();

            if (npcNames.Count > 0)
            {
                message += "You see: " + string.Join(", ", npcNames);
            }
        }

        return new ActionResult { Success = true, Message = message };
    }

    private ActionResult HandleInventory(string? mode = null)
    {
        if (_gameState.PlayerInventory.Items.Count == 0)
            return new ActionResult { Success = true, Message = "Your inventory is empty." };

        // Check if equipped items indicator is requested
        bool showEquipped = mode?.ToLower() == "equipped" || mode?.ToLower() == "detailed";

        if (!showEquipped)
        {
            // Simple mode - just list items with quantities
            var items = string.Join(", ",
                _gameState.PlayerInventory.Items.Values.Select(i => $"{i.Item.Name} x{i.Quantity}"));
            return new ActionResult { Success = true, Message = $"Inventory: {items}" };
        }

        // Detailed mode - show items with equipped indicator
        var message = new StringBuilder();
        message.AppendLine("Inventory:");

        // Get equipped item IDs
        var equippedIds = _gameState.Player.EquipmentSlots.Values.Where(id => id != null).ToHashSet();

        foreach (var inv in _gameState.PlayerInventory.Items.Values)
        {
            bool isEquipped = equippedIds.Contains(inv.Item.Id);
            string equippedIcon = isEquipped ? " ⚔️" : "";
            message.AppendLine($"  • {inv.Item.Name} x{inv.Quantity}{equippedIcon}");
        }

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    private async Task<ActionResult> HandleTalkAsync(string npcName, string playerQuestion)
    {
        var room = _gameState.GetCurrentRoom();
        var npcNameLower = npcName.ToLower();

        // Try to find NPC by ID first
        var npc = _gameState.GetNPCInRoom(npcName);

        // If not found by ID, try finding by name
        if (npc == null)
        {
            var npcId = room.NPCIds.FirstOrDefault(id =>
                _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(npcNameLower));

            if (npcId != null)
                npc = _gameState.NPCs[npcId];
        }

        if (npc == null)
            return new ActionResult { Success = false, Message = $"You don't see '{npcName}' here to talk to." };

        // Enter chat mode with this NPC
        _gameState.InChatMode = true;
        _gameState.CurrentChatNpcId = npc.Id;

        // Get the NPC's response via their brain. Dialogue generation is
        // presentation: a provider outage must not leave a half-applied action.
        var npcResponse = "I hear you, but I cannot find the words just now.";
        if (_npcBrains.ContainsKey(npc.Id))
        {
            try
            {
                // STEP 1: Use LLM to convert player's raw command into a natural message for the NPC
                string messageToNpc;
                if (string.IsNullOrWhiteSpace(playerQuestion))
                {
                    messageToNpc = "The player greets you.";
                }
                else
                {
                    messageToNpc = await ConvertPlayerCommandToNpcMessageAsync(playerQuestion, npc.Name);
                }

                // STEP 2: Send the converted message to the NPC for their response
                npcResponse = await _npcBrains[npc.Id].RespondToPlayerAsync(messageToNpc);
            }
            catch (Exception ex)
            {
                DebugLog($"[DEBUG] NPC dialogue unavailable; using fallback: {ex.Message}");
            }
        }

        // Return the NPC's actual response as the message
        var message = $"[Chatting with {npc.Name}]\n\n{npc.Name} says: \"{npcResponse}\"";
        return new ActionResult { Success = true, Message = message };
    }

    /// <summary>
    /// Handle a follow-up chat message while in chat mode with an NPC.
    /// </summary>
    private async Task<ActionResult> HandleChatAsync(string message)
    {
        if (!_gameState.InChatMode || _gameState.CurrentChatNpcId == null)
            return new ActionResult { Success = false, Message = "You aren't talking to anyone. Use 'talk to [name]' first." };

        if (!_gameState.NPCs.TryGetValue(_gameState.CurrentChatNpcId, out var npc))
        {
            _gameState.InChatMode = false;
            _gameState.CurrentChatNpcId = null;
            return new ActionResult { Success = false, Message = "That NPC is no longer available." };
        }

        if (!_gameState.GetCurrentRoom().NPCIds.Contains(npc.Id, StringComparer.OrdinalIgnoreCase))
        {
            _gameState.InChatMode = false;
            _gameState.CurrentChatNpcId = null;
            return new ActionResult { Success = false, Message = $"{npc.Name} is no longer here." };
        }

        if (string.IsNullOrWhiteSpace(message))
            return new ActionResult { Success = false, Message = "Say something! Or type 'leave' to end the conversation." };

        var npcResponse = "I hear you, but I cannot find the words just now.";
        if (_npcBrains.TryGetValue(npc.Id, out var brain))
        {
            try
            {
                npcResponse = await brain.RespondToPlayerAsync(message);
            }
            catch (Exception ex)
            {
                DebugLog($"[DEBUG] NPC dialogue unavailable; using fallback: {ex.Message}");
            }
        }

        return new ActionResult { Success = true, Message = $"{npc.Name} says: \"{npcResponse}\"" };
    }

    /// <summary>
    /// End the current chat with an NPC.
    /// </summary>
    private ActionResult HandleEndChat()
    {
        if (!_gameState.InChatMode)
            return new ActionResult { Success = true, Message = "You aren't chatting with anyone." };

        var npcName = "the NPC";
        if (_gameState.CurrentChatNpcId != null && _gameState.NPCs.TryGetValue(_gameState.CurrentChatNpcId, out var npc))
            npcName = npc.Name;

        _gameState.InChatMode = false;
        _gameState.CurrentChatNpcId = null;

        return new ActionResult { Success = true, Message = $"You end your conversation with {npcName}." };
    }

    /// <summary>
    /// Get contextual chat suggestions for the current NPC conversation.
    /// Returns topic suggestions based on the NPC's role, the game state, and quests.
    /// </summary>
    public List<string> GetChatSuggestions()
    {
        var suggestions = new List<string>();
        if (!_gameState.InChatMode || _gameState.CurrentChatNpcId == null)
            return suggestions;

        if (!_gameState.NPCs.TryGetValue(_gameState.CurrentChatNpcId, out var npc))
            return suggestions;

        var room = _gameState.GetCurrentRoom();

        // General suggestions everyone gets
        suggestions.Add("What can you tell me about this place?");
        suggestions.Add("Have you heard any rumors?");

        // Role-specific suggestions
        switch (npc.Role)
        {
            case NPCRole.Merchant:
                suggestions.Add("What do you have for sale?");
                suggestions.Add("I'd like to buy something");
                break;
            case NPCRole.Questgiver:
                suggestions.Add("Do you have any work for me?");
                suggestions.Add("Tell me about the quest");
                break;
            case NPCRole.Healer:
                suggestions.Add("Can you heal me?");
                suggestions.Add("Do you have any potions?");
                break;
            case NPCRole.Guard:
                suggestions.Add("Is there any danger nearby?");
                suggestions.Add("What are you guarding?");
                break;
            case NPCRole.CommonPerson:
                suggestions.Add("How's life around here?");
                break;
            case NPCRole.Warrior:
            case NPCRole.Boss:
                suggestions.Add("I don't want to fight");
                suggestions.Add("Why are you hostile?");
                break;
        }

        // If NPC can craft
        if (npc.CanCraft)
        {
            suggestions.Add("What can you craft for me?");
        }

        // If player has active quests, suggest asking about them
        if (_gameState.ActiveQuests.Count > 0)
        {
            var quest = _gameState.ActiveQuests.FirstOrDefault(q =>
                q.Status != QuestStatus.Completed && q.Status != QuestStatus.TurnedIn);
            if (quest != null)
                suggestions.Add($"Do you know anything about {quest.Title}?");
        }

        // If game has an objective
        if (_game != null && !string.IsNullOrEmpty(_game.GameObjective))
        {
            suggestions.Add("Can you help me with my quest?");
        }

        // Always offer goodbye
        suggestions.Add("Goodbye");

        return suggestions;
    }

    private async Task<ActionResult> HandleFollowAsync(string npcName)
    {
        var room = _gameState.GetCurrentRoom();
        var npcNameLower = npcName.ToLower();

        // Find the NPC
        var npc = _gameState.GetNPCInRoom(npcName);
        if (npc == null)
        {
            var npcId = room.NPCIds.FirstOrDefault(id =>
                _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(npcNameLower));

            if (npcId != null)
                npc = _gameState.NPCs[npcId];
        }

        if (npc == null)
            return new ActionResult { Success = false, Message = $"You don't see '{npcName}' here to ask to follow." };

        // Check if NPC can join the party
        if (!npc.CanJoinParty)
            return new ActionResult { Success = false, Message = $"{npc.Name} doesn't want to follow you." };

        // Check if NPC is already a companion
        if (_gameState.Companions.Contains(npc.Id))
            return new ActionResult { Success = true, Message = $"{npc.Name} is already with you." };

        // Get NPC's follow decision using structured JSON (like give system)
        var decision = await GetNpcFollowDecisionAsync(npc);

        if (decision.WillFollow)
        {
            _gameState.AddCompanion(npc.Id);

            // Move companion to current room
            var oldRoomId = npc.CurrentRoomId;
            npc.CurrentRoomId = _gameState.CurrentRoomId;
            var newRoom = _gameState.GetCurrentRoom();

            // Update room NPC lists
            if (!string.IsNullOrEmpty(oldRoomId) && _gameState.Rooms.ContainsKey(oldRoomId))
            {
                _gameState.Rooms[oldRoomId].NPCIds.Remove(npc.Id);
            }
            if (_gameState.Rooms.ContainsKey(newRoom.Id) && !newRoom.NPCIds.Contains(npc.Id))
            {
                newRoom.NPCIds.Add(npc.Id);
            }

            DebugLog($"[DEBUG] {npc.Name} joined the party and moved to {newRoom.Name}");
            return new ActionResult { Success = true, Message = $"{npc.Name} says: \"{decision.Response}\"\n\n{npc.Name} joins your party and will follow you." };
        }
        else
        {
            return new ActionResult { Success = true, Message = $"{npc.Name} says: \"{decision.Response}\"" };
        }
    }

    /// <summary>
    /// Get structured decision from NPC about whether to follow.
    /// </summary>
    private async Task<FollowDecision> GetNpcFollowDecisionAsync(Character npc)
    {
        var prompt = @"The player asks you to follow them and join their party.

Respond ONLY with JSON (no markdown, no explanation):
{
  ""willFollow"": true/false,
  ""response"": ""your dialogue response to the player (1-2 sentences)""
}

RULES:
1. willFollow = true ONLY if you want to join the party
2. willFollow = false if you decline
3. response = what you say to the player in character (1-2 sentences)
4. Return ONLY the JSON - no other text
5. Be concise and stay in character";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = $"You are {npc.Name}. Answer whether you will follow the player. Be direct and honest." },
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _llmClient.ChatAsync(messages);
            DebugLog($"[DEBUG] NPC Follow Decision Response:\n{response}\n[DEBUG] ---");

            return ParseFollowDecisionJson(response);
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Error getting NPC follow decision: {ex.Message}");
            return new FollowDecision { WillFollow = false, Response = "I'm not sure about that." };
        }
    }

    /// <summary>
    /// Parse JSON follow decision from NPC.
    /// </summary>
    private FollowDecision ParseFollowDecisionJson(string jsonResponse)
    {
        try
        {
            var jsonStart = jsonResponse.IndexOf('{');
            var jsonEnd = jsonResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var decision = System.Text.Json.JsonSerializer.Deserialize<FollowDecision>(jsonStr,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return decision ?? new FollowDecision { WillFollow = false, Response = "I cannot decide right now." };
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Error parsing follow decision JSON: {ex.Message}");
        }

        return new FollowDecision { WillFollow = false, Response = "I'm uncertain about this." };
    }

    private async Task<ActionResult> HandleGiveAsync(string npcName, string requestText)
    {
        var room = _gameState.GetCurrentRoom();
        var npcNameLower = npcName.ToLower();

        // Find the NPC
        var npc = _gameState.GetNPCInRoom(npcName);
        if (npc == null)
        {
            var npcId = room.NPCIds.FirstOrDefault(id =>
                _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(npcNameLower));

            if (npcId != null)
                npc = _gameState.NPCs[npcId];
        }

        if (npc == null)
            return new ActionResult { Success = false, Message = $"You don't see '{npcName}' here to ask for items." };

        // STEP 1: Get NPC's decision about what items to give (structured JSON)
        var decision = await GetNpcGiveDecisionAsync(npc, requestText);

        var resultMessage = new StringBuilder();
        resultMessage.AppendLine($"{npc.Name} says: \"{decision.Narrative}\"");

        // STEP 2: If NPC decided to give items, transfer them
        if (decision.WillGive && decision.ItemsToGive.Count > 0)
        {
            var itemsGiven = new List<string>();

            foreach (var requestedItemName in decision.ItemsToGive)
            {
                // Find the item in NPC's inventory
                var npcItem = npc.CarriedItems.FirstOrDefault(kvp =>
                    kvp.Value.Item.Name.Equals(requestedItemName, StringComparison.OrdinalIgnoreCase));

                if (npcItem.Key != null)
                {
                    var item = npcItem.Value.Item;
                    var quantity = npcItem.Value.Quantity;

                    // Transfer from NPC to player
                    _gameState.PlayerInventory.AddItem(item, quantity);
                    npc.CarriedItems.Remove(item.Id);
                    itemsGiven.Add($"{item.Name} (x{quantity})");
                }
            }

            if (itemsGiven.Count > 0)
            {
                resultMessage.AppendLine();
                resultMessage.AppendLine($"✓ You received: {string.Join(", ", itemsGiven)}");
            }
        }

        return new ActionResult { Success = true, Message = resultMessage.ToString().TrimEnd() };
    }

    private ActionResult HandleAttack(string npcName)
    {
        // Exit chat mode when attacking
        if (_gameState.InChatMode)
        {
            _gameState.InChatMode = false;
            _gameState.CurrentChatNpcId = null;
        }

        var room = _gameState.GetCurrentRoom();
        var npcNameLower = npcName.ToLower();

        // Try to find NPC by name matching in the current room
        var npcId = room.NPCIds.FirstOrDefault(id =>
            _gameState.NPCs.ContainsKey(id) &&
            _gameState.NPCs[id].Name.ToLower().Contains(npcNameLower));

        if (npcId == null)
            return new ActionResult { Success = false, Message = $"You can't attack '{npcName}' - they're not here." };

        var npc = _gameState.NPCs[npcId];

        // Check if NPC is already dead
        if (!npc.IsAlive)
        {
            return new ActionResult { Success = false, Message = $"{npc.Name} is already dead. You can examine their body if you'd like." };
        }

        // Enter combat mode
        _gameState.InCombatMode = true;
        _gameState.CurrentCombatNpcId = npcId;

        // Get companion assistance
        var companionCharacters = _gameState.Companions
            .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id])
            .ToList();

        var companionAssistance = _combatService.CalculateCompanionAssistance(companionCharacters);

        // Resolve combat using CombatService (pass player inventory for equipment lookup)
        var combatResult = _combatService.ResolveAttack(_gameState.Player, _gameState.PlayerInventory.Items, npc, null);

        var message = new StringBuilder();
        message.AppendLine(combatResult.Message);

        // Show companion assistance messages
        if (companionAssistance.HasCompanions)
        {
            foreach (var companionMsg in companionAssistance.CompanionMessages)
            {
                message.AppendLine(companionMsg);
            }
        }

        if (!combatResult.WasHit)
        {
            message.AppendLine();
            message.AppendLine(GetCombatStatus());
            return new ActionResult { Success = true, Message = message.ToString() };
        }

        // Apply damage to NPC (plus companion bonus)
        int totalDamage = combatResult.DamageAfterArmor + companionAssistance.DamageBonus;
        _combatService.ApplyDamage(npc, totalDamage);

        if (companionAssistance.DamageBonus > 0)
        {
            message.AppendLine($"✦ Companion bonus damage: +{companionAssistance.DamageBonus}");
        }

        // Check if NPC is defeated
        if (!npc.IsAlive)
        {
            // Leave the body as a searchable NPC (but mark it as dead/defeated)
            // The NPC stays in the room but can't attack or move
            npc.CanMove = false;  // Can't move anymore
            var xpGain = (npc.Level * 10) + (npc.MaxHealth / 5);
            _gameState.Player.GainExperience(xpGain);
            message.AppendLine($"\n🎉 {npc.Name} is defeated! You gain {xpGain} experience.");

            // Loot currency if economy is enabled and NPC had money
            if (_economy.Enabled && npc.Wallet.TotalBaseUnits > 0)
            {
                var lootedMoney = npc.Wallet.TotalBaseUnits;
                _gameState.Player.Wallet.Add(lootedMoney);
                npc.Wallet.TotalBaseUnits = 0;
                var moneyDisplay = Wallet.FormatAmount(lootedMoney, _economy);
                message.AppendLine($"💰 You loot {moneyDisplay} from the body!");
            }

            message.AppendLine($"The {npc.Name}'s body remains here for you to search or examine.");

            // Exit combat mode
            _gameState.InCombatMode = false;
            _gameState.CurrentCombatNpcId = null;

            return new ActionResult { Success = true, Message = message.ToString() };
        }

        // NPC counter-attacks!
        message.AppendLine($"\n{npc.Name} retaliates!");
        var counterAttack = _combatService.ResolveAttack(npc, null, _gameState.Player, _gameState.PlayerInventory.Items);
        message.AppendLine(counterAttack.Message);

        if (counterAttack.WasHit)
        {
            _combatService.ApplyDamage(_gameState.Player, counterAttack.DamageAfterArmor);

            if (!_gameState.Player.IsAlive)
            {
                message.AppendLine("\n💀 YOU HAVE BEEN DEFEATED! Game Over.");
                _gameState.InCombatMode = false;
                _gameState.CurrentCombatNpcId = null;
                return new ActionResult { Success = true, Message = message.ToString() };
            }
        }

        message.AppendLine();
        message.AppendLine(GetCombatStatus());

        // Check if any other hostile NPCs in the room want to join the combat
        CheckForHostileNpcIntervention(npc, message);

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    /// <summary>
    /// Handle automatic combat mode - keeps attacking until player reaches 5% health or enemy is defeated.
    /// </summary>
    private ActionResult HandleAuto()
    {
        if (!_gameState.InCombatMode || string.IsNullOrEmpty(_gameState.CurrentCombatNpcId))
            return new ActionResult { Success = false, Message = "You're not in combat!" };

        var npc = _gameState.NPCs[_gameState.CurrentCombatNpcId];
        var message = new StringBuilder();
        message.AppendLine("⚔️ AUTOMATIC COMBAT ENGAGED");
        message.AppendLine("Fighting until health drops to 5% or enemy is defeated...");
        message.AppendLine();

        int roundCount = 0;
        int playerHealthThreshold = (int)(_gameState.Player.MaxHealth * 0.05); // 5% health
        int playerStartingHealth = _gameState.Player.Health;
        int npcStartingHealth = npc.Health;
        int totalPlayerDamage = 0;
        int totalNpcDamage = 0;

        // Keep attacking while player is above 5% health, alive, and enemy is alive
        while (_gameState.Player.Health > playerHealthThreshold && _gameState.Player.IsAlive && npc.IsAlive)
        {
            roundCount++;

            // Get companion assistance
            var companionCharacters = _gameState.Companions
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
                .Select(id => _gameState.NPCs[id])
                .ToList();

            var companionAssistance = _combatService.CalculateCompanionAssistance(companionCharacters);

            // Player attacks
            var combatResult = _combatService.ResolveAttack(_gameState.Player, _gameState.PlayerInventory.Items, npc, null);

            if (combatResult.WasHit)
            {
                // Apply damage to NPC (plus companion bonus)
                int totalDamage = combatResult.DamageAfterArmor + companionAssistance.DamageBonus;
                _combatService.ApplyDamage(npc, totalDamage);
                totalPlayerDamage += totalDamage;

                // Check if NPC is defeated
                if (!npc.IsAlive)
                {
                    npc.CanMove = false;
                    var xpGain = (npc.Level * 10) + (npc.MaxHealth / 5);
                    _gameState.Player.GainExperience(xpGain);

                    message.AppendLine($"⚔️ Auto Combat Summary");
                    message.AppendLine($"Rounds fought: {roundCount}");
                    message.AppendLine($"Damage dealt: {totalPlayerDamage}");
                    message.AppendLine($"Damage taken: {totalNpcDamage}");
                    message.AppendLine();
                    message.AppendLine($"🎉 {npc.Name} is defeated! You gain {xpGain} experience.");

                    // Loot currency
                    if (_economy.Enabled && npc.Wallet.TotalBaseUnits > 0)
                    {
                        var lootedMoney = npc.Wallet.TotalBaseUnits;
                        _gameState.Player.Wallet.Add(lootedMoney);
                        npc.Wallet.TotalBaseUnits = 0;
                        var moneyDisplay = Wallet.FormatAmount(lootedMoney, _economy);
                        message.AppendLine($"💰 You loot {moneyDisplay} from the body!");
                    }

                    message.AppendLine($"The {npc.Name}'s body remains here for you to search or examine.");
                    _gameState.InCombatMode = false;
                    _gameState.CurrentCombatNpcId = null;
                    return new ActionResult { Success = true, Message = message.ToString() };
                }
            }

            // NPC counter-attacks
            var counterAttack = _combatService.ResolveAttack(npc, null, _gameState.Player, _gameState.PlayerInventory.Items);

            if (counterAttack.WasHit)
            {
                _combatService.ApplyDamage(_gameState.Player, counterAttack.DamageAfterArmor);
                totalNpcDamage += counterAttack.DamageAfterArmor;

                if (!_gameState.Player.IsAlive)
                {
                    message.AppendLine($"⚔️ Auto Combat Summary");
                    message.AppendLine($"Rounds fought: {roundCount}");
                    message.AppendLine($"Damage dealt: {totalPlayerDamage}");
                    message.AppendLine($"Damage taken: {totalNpcDamage}");
                    message.AppendLine();
                    message.AppendLine("\n💀 YOU HAVE BEEN DEFEATED! Game Over.");
                    _gameState.InCombatMode = false;
                    _gameState.CurrentCombatNpcId = null;
                    return new ActionResult { Success = true, Message = message.ToString() };
                }
            }
        }

        // Auto-fight ended - show summary
        message.AppendLine($"⚔️ Auto Combat Summary");
        message.AppendLine($"Rounds fought: {roundCount}");
        message.AppendLine($"Damage dealt: {totalPlayerDamage}");
        message.AppendLine($"Damage taken: {totalNpcDamage}");
        message.AppendLine($"Your Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
        message.AppendLine($"{npc.Name}'s Health: {npc.Health}/{npc.MaxHealth}");

        if (_gameState.Player.Health <= playerHealthThreshold)
        {
            message.AppendLine($"\n⚠️ Automatic combat ended! Health dropped to {(_gameState.Player.Health * 100) / _gameState.Player.MaxHealth}%");
        }

        message.AppendLine();
        message.AppendLine(GetCombatStatus());

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    /// <summary>
    /// Check if any other NPCs in the room are hostile and would intervene in combat.
    /// NPCs might join based on alignment, relationships, or if they're companions of the enemy.
    /// </summary>
    private void CheckForHostileNpcIntervention(Character enemy, StringBuilder message)
    {
        var room = _gameState.GetCurrentRoom();
        var otherNpcsInRoom = room.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && id != enemy.Id && _gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id])
            .ToList();

        foreach (var npc in otherNpcsInRoom)
        {
            // Skip if this NPC is a companion of the player
            if (_gameState.Companions.Contains(npc.Id))
                continue;

            // Determine if NPC is hostile
            bool isHostile = false;
            string reason = "";

            // Check alignment: Good NPCs won't attack Good player attacking Evil NPCs
            // Evil NPCs will attack Good/Neutral players
            if (npc.Alignment == CharacterAlignment.Evil)
            {
                isHostile = true;
                reason = $"{npc.Name} sees an opportunity to cause trouble!";
            }
            else if (npc.Alignment == CharacterAlignment.Good && enemy.Alignment == CharacterAlignment.Evil)
            {
                // Good NPC defending against evil attacker - joins on player's side, not against
                isHostile = false;
            }
            else if (enemy.Relationships != null && enemy.Relationships.Contains(npc.Id))
            {
                // Enemy has a relationship with this NPC (ally or friend)
                isHostile = true;
                reason = $"{npc.Name} rushes to help {enemy.Name}!";
            }

            if (isHostile)
            {
                message.AppendLine();
                message.AppendLine($"⚠️ {reason}");
                message.AppendLine($"🚨 {npc.Name} joins the combat against you!");
                // Note: Actual multi-NPC combat would require expanding the combat system
                // For now, just warn the player
            }
        }
    }

    private ActionResult HandleExamine(string targetName, string? playerCommand = null)
    {
        var room = _gameState.GetCurrentRoom();

        if (string.IsNullOrWhiteSpace(targetName))
        {
            // Try to infer target from player's original command
            if (!string.IsNullOrWhiteSpace(playerCommand))
            {
                var commandLower = playerCommand.ToLower();

                // Check if any NPC name is mentioned in the command
                foreach (var npcId in room.NPCIds)
                {
                    if (_gameState.NPCs.ContainsKey(npcId))
                    {
                        var npcName = _gameState.NPCs[npcId].Name.ToLower();
                        if (commandLower.Contains(npcName))
                        {
                            return HandleExamine(npcName, null);
                        }
                    }
                }

                // Check for keywords like "body", "corpse", "dead", etc. - find a dead NPC
                if (commandLower.Contains("body") || commandLower.Contains("corpse") || commandLower.Contains("search") || commandLower.Contains("loot"))
                {
                    var deadNpc = room.NPCIds
                        .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) && !_gameState.NPCs[id].IsAlive);
                    if (deadNpc != null)
                    {
                        var npcObj = _gameState.NPCs[deadNpc];
                        return HandleExamine(npcObj.Name, null);
                    }
                }
            }

            // Fallback: find any dead NPC
            var deadNpc2 = room.NPCIds
                .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) && !_gameState.NPCs[id].IsAlive);
            if (deadNpc2 != null)
            {
                var npcObj = _gameState.NPCs[deadNpc2];
                return HandleExamine(npcObj.Name, null);
            }

            return new ActionResult { Success = false, Message = "Examine what?" };
        }
        var targetLower = targetName.ToLower();

        // Check inventory
        var inventoryItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(targetLower));
        if (inventoryItem != null)
        {
            var details = $"{inventoryItem.Item.Name}: {inventoryItem.Item.Description}";
            if (inventoryItem.Item.Type == ItemType.Weapon)
                details += $" (Damage: {inventoryItem.Item.DamageBonus})";
            else if (inventoryItem.Item.Type == ItemType.Armor)
                details += $" (Armor: {inventoryItem.Item.ArmorBonus})";
            // Show value if economy is enabled
            if (_economy.Enabled)
            {
                var sellPrice = inventoryItem.Item.GetSellPrice();
                details += $"\nSell value: {Wallet.FormatAmount(sellPrice, _economy)}";
            }
            return new ActionResult { Success = true, Message = details };
        }

        // Check NPCs
        var npc = room.NPCIds
            .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(targetLower));
        if (npc != null)
        {
            var npcObj = _gameState.NPCs[npc];
            var npcDesc = $"{npcObj.Name}: {npcObj.Description ?? "An NPC"}";

            // Show health for alive NPCs
            if (npcObj.IsAlive)
            {
                var healthPercent = (npcObj.Health * 100) / npcObj.MaxHealth;
                var healthBar = BuildHealthBar(healthPercent, 20);
                npcDesc += $"\n\nHealth: {healthBar} {npcObj.Health}/{npcObj.MaxHealth} HP";
                npcDesc += $"\nLevel: {npcObj.Level} | Strength: {npcObj.Strength} | Agility: {npcObj.Agility} | Armor: {npcObj.Armor}";

                // Show merchant indicator
                if (_economy.Enabled && npcObj.Role == NPCRole.Merchant)
                {
                    npcDesc += $"\n\n🛒 This is a merchant. Use 'shop' to see their wares.";
                }
            }
            // Show additional info if the NPC is dead
            else
            {
                npcDesc += $"\n\n☠️ {npcObj.Name} is dead. Their body lies here lifeless. (HP: {npcObj.Health}/{npcObj.MaxHealth})";

                // Show available loot on the corpse
                if (npcObj.CarriedItems.Count > 0)
                {
                    npcDesc += $"\nOn their body, you find: {string.Join(", ", npcObj.CarriedItems.Values.Select(ii => ii.Item.Name))}";
                    npcDesc += $"\nYou can use 'take [item name]' to loot these items.";
                }
                else
                {
                    npcDesc += $"\nTheir body has no items on it.";
                }
            }

            return new ActionResult { Success = true, Message = npcDesc };
        }

        return new ActionResult { Success = false, Message = $"You don't see '{targetName}' here." };
    }

    private ActionResult HandleTake(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Take what?" };

        var room = _gameState.GetCurrentRoom();
        var itemLower = itemName.ToLower();

        // Items authored on the room floor are real runtime objects and can be
        // picked up independently of NPC loot.
        var floorItem = room.Items.FirstOrDefault(item =>
            item.Id.Equals(itemName, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase));
        if (floorItem != null)
        {
            if (!floorItem.CanBeTaken)
                return new ActionResult { Success = false, Message = $"You cannot take {floorItem.Name}." };
            if (!_gameState.PlayerInventory.AddItem(floorItem))
                return new ActionResult { Success = false, Message = $"You cannot carry {floorItem.Name}; your inventory is too heavy." };

            room.Items.Remove(floorItem);
            return new ActionResult { Success = true, Message = $"You take {floorItem.Name}." };
        }

        // Check if the target is actually an NPC name (LLM confusion)
        var targetNpc = room.NPCIds
            .FirstOrDefault(id => _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(itemLower));

        if (targetNpc != null)
        {
            var npc = _gameState.NPCs[targetNpc];

            // If it's a dead NPC with loot, take all items
            if (!npc.IsAlive && npc.CarriedItems.Count > 0)
            {
                var itemsTaken = new List<string>();
                var items = npc.CarriedItems.Values.ToList();

                foreach (var lootItem in items)
                {
                    if (_gameState.PlayerInventory.AddItem(lootItem.Item, lootItem.Quantity))
                    {
                        npc.CarriedItems.Remove(lootItem.Item.Id);
                        itemsTaken.Add(lootItem.Item.Name);
                    }
                }

                if (itemsTaken.Count == 0)
                    return new ActionResult { Success = false, Message = "You cannot carry any more loot." };

                return new ActionResult
                {
                    Success = true,
                    Message = $"You take {string.Join(", ", itemsTaken)} from {npc.Name}'s body."
                };
            }
            else if (!npc.IsAlive)
            {
                return new ActionResult { Success = false, Message = $"{npc.Name}'s body has no items to take." };
            }
        }

        // Check dead NPCs in the room for loot
        foreach (var npcId in room.NPCIds)
        {
            if (!_gameState.NPCs.ContainsKey(npcId))
                continue;

            var npc = _gameState.NPCs[npcId];

            // Can only loot from dead NPCs
            if (!npc.IsAlive && npc.CarriedItems.Count > 0)
            {
                // Try to find the item on this NPC's body
                var lootItem = npc.CarriedItems.Values
                    .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

                if (lootItem != null)
                {
                    // Transfer item from NPC to player inventory
                    if (!_gameState.PlayerInventory.AddItem(lootItem.Item, lootItem.Quantity))
                        return new ActionResult { Success = false, Message = $"You cannot carry {lootItem.Item.Name}; your inventory is too heavy." };
                    npc.CarriedItems.Remove(lootItem.Item.Id);
                    return new ActionResult { Success = true, Message = $"You take {lootItem.Item.Name} from {npc.Name}'s body." };
                }
            }
        }

        return new ActionResult { Success = false, Message = $"You don't see '{itemName}' here to take." };
    }

    private ActionResult HandleDrop(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Drop what?" };

        var itemLower = itemName.ToLower();
        var item = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (item == null)
            return new ActionResult { Success = false, Message = $"You don't have '{itemName}'." };

        if (_gameState.Player.EquipmentSlots.Values.Any(equippedItemId =>
                equippedItemId?.Equals(item.Item.Id, StringComparison.OrdinalIgnoreCase) == true))
        {
            return new ActionResult { Success = false, Message = $"Unequip {item.Item.Name} before dropping it." };
        }

        if (!_gameState.PlayerInventory.RemoveItem(item.Item.Id))
            return new ActionResult { Success = false, Message = $"You could not drop {item.Item.Name}." };

        _gameState.GetCurrentRoom().Items.Add(item.Item.CloneRuntime());
        return new ActionResult { Success = true, Message = $"You dropped {item.Item.Name}." };
    }

    private ActionResult HandleUse(string itemNameOrTarget, string itemNameFromDetails = "")
    {
        var room = _gameState.GetCurrentRoom();

        // Determine item name - prefer details (from LLM), fallback to target
        var itemName = !string.IsNullOrWhiteSpace(itemNameFromDetails) ? itemNameFromDetails : itemNameOrTarget;
        DebugLog($"[DEBUG] HandleUse called: itemNameOrTarget='{itemNameOrTarget}', itemNameFromDetails='{itemNameFromDetails}', resolved itemName='{itemName}'");
        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Use what?" };

        // Determine if we're using on a character (target might be NPC name)
        Character? targetCharacter = null;
        if (!string.IsNullOrWhiteSpace(itemNameOrTarget) && string.IsNullOrWhiteSpace(itemNameFromDetails))
        {
            // No details, so target might be both item and character name - check for NPC first
            var npcTarget = room.NPCIds.FirstOrDefault(id =>
                _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(itemNameOrTarget.ToLower()));

            if (npcTarget != null)
                targetCharacter = _gameState.NPCs[npcTarget];
        }
        else if (!string.IsNullOrWhiteSpace(itemNameOrTarget) && !string.IsNullOrWhiteSpace(itemNameFromDetails))
        {
            // Has details, so target is the character
            var npcTarget = room.NPCIds.FirstOrDefault(id =>
                _gameState.NPCs.ContainsKey(id) &&
                _gameState.NPCs[id].Name.ToLower().Contains(itemNameOrTarget.ToLower()));

            if (npcTarget != null)
                targetCharacter = _gameState.NPCs[npcTarget];
        }

        // If no target character, use on player
        if (targetCharacter == null)
            targetCharacter = _gameState.Player;

        // Find the item in inventory
        var itemLower = itemName.ToLower();
        DebugLog($"[DEBUG] Looking for item: '{itemLower}' in inventory ({_gameState.PlayerInventory.Items.Count} items)");
        foreach (var ii in _gameState.PlayerInventory.Items.Values)
        {
            DebugLog($"[DEBUG]   Inventory contains: '{ii.Item.Name}' (contains check: {ii.Item.Name.ToLower().Contains(itemLower)})");
        }
        var inventoryItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (inventoryItem == null)
        {
            DebugLog($"[DEBUG] Item '{itemName}' not found in inventory");
            return new ActionResult { Success = false, Message = $"You don't have '{itemName}'." };
        }
        DebugLog($"[DEBUG] Found item: '{inventoryItem.Item.Name}'");

        var item = inventoryItem.Item;

        // Handle teleportation items (always on player)
        if (item.IsTeleportation && !string.IsNullOrEmpty(item.TeleportDestinationRoomId))
        {
            if (_gameState.Rooms.ContainsKey(item.TeleportDestinationRoomId))
            {
                _gameState.CurrentRoomId = item.TeleportDestinationRoomId;
                return new ActionResult
                {
                    Success = true,
                    Message = $"You use {item.Name} and are transported to {_gameState.GetCurrentRoom().Name}!"
                };
            }
        }

        // Handle consumables (can be used on self or other character)
        if (item.IsConsumable && item.ConsumableUsesRemaining > 0)
        {
            var message = new StringBuilder();
            var healAmount = item.ConsumableEffects?.ContainsKey("heal") == true ? item.ConsumableEffects["heal"] : 0;

            if (healAmount > 0)
            {
                targetCharacter.Heal(healAmount);

                if (targetCharacter.IsPlayer)
                {
                    message.AppendLine($"You use {item.Name} and restore {healAmount} health.");
                }
                else
                {
                    message.AppendLine($"You give {item.Name} to {targetCharacter.Name}.");
                    message.AppendLine($"{targetCharacter.Name} uses it and recovers {healAmount} health.");
                }
            }
            else
            {
                if (targetCharacter.IsPlayer)
                    message.AppendLine($"You use {item.Name}.");
                else
                    message.AppendLine($"You use {item.Name} on {targetCharacter.Name}.");
            }

            item.ConsumableUsesRemaining--;
            if (item.ConsumableUsesRemaining == 0)
            {
                _gameState.PlayerInventory.RemoveItem(item.Id);
                if (_gameState.PlayerInventory.Items.TryGetValue(item.Id, out var remainingStack))
                {
                    var usesPerItem = _game?.Items.TryGetValue(item.Id, out var definition) == true
                        ? definition.ConsumableUsesRemaining
                        : 1;
                    remainingStack.Item.ConsumableUsesRemaining = Math.Max(1, usesPerItem ?? 1);
                }
            }

            return new ActionResult { Success = true, Message = message.ToString().TrimEnd() };
        }

        return new ActionResult { Success = false, Message = $"You can't use {item.Name} like that." };
    }

    private ActionResult HandleEquip(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Equip what?" };

        // Find item in inventory
        var itemLower = itemName.ToLower();
        var inventoryItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower) ||
                                  ii.Item.Id.ToLower().Contains(itemLower));

        if (inventoryItem == null)
            return new ActionResult { Success = false, Message = $"You don't have '{itemName}' in your inventory." };

        var item = inventoryItem.Item;

        // Check if item is equippable
        if (!item.IsEquippable)
            return new ActionResult { Success = false, Message = $"{item.Name} cannot be equipped." };

        // Determine equipment slot
        string slot = item.EquipmentSlot ?? DetermineSlotFromType(item);

        if (string.IsNullOrEmpty(slot))
            return new ActionResult { Success = false, Message = $"{item.Name} doesn't have a valid equipment slot." };

        // Check if slot is already occupied
        string? previousItemId = null;
        if (_gameState.Player.EquipmentSlots.TryGetValue(slot, out var occupiedItemId) && occupiedItemId != null)
        {
            previousItemId = occupiedItemId;
        }

        // Equip the item
        bool success = _gameState.Player.EquipItem(item, slot);

        if (success)
        {
            var message = new StringBuilder();
            message.Append($"You equip {item.Name}");

            // Mention if we unequipped something
            if (previousItemId != null && _gameState.PlayerInventory.Items.TryGetValue(previousItemId, out var prevItem))
            {
                message.Append($" (unequipped {prevItem.Item.Name})");
            }

            message.Append(".");

            // Show stat bonuses
            if (item.DamageBonus > 0)
                message.Append($" [+{item.DamageBonus} damage]");
            if (item.ArmorBonus > 0)
                message.Append($" [+{item.ArmorBonus} armor]");

            return new ActionResult { Success = true, Message = message.ToString() };
        }
        else
        {
            return new ActionResult { Success = false, Message = $"Cannot equip {item.Name} in the {slot} slot." };
        }
    }

    private ActionResult HandleUnequip(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return new ActionResult { Success = false, Message = "Unequip what?" };

        var targetLower = target.ToLower();

        // Try to match by item name first
        string? slotToUnequip = null;
        Item? itemToUnequip = null;

        foreach (var kvp in _gameState.Player.EquipmentSlots)
        {
            if (kvp.Value != null && _gameState.PlayerInventory.Items.TryGetValue(kvp.Value, out var inventoryItem))
            {
                if (inventoryItem.Item.Name.ToLower().Contains(targetLower))
                {
                    slotToUnequip = kvp.Key;
                    itemToUnequip = inventoryItem.Item;
                    break;
                }
            }
        }

        // If not found by name, try by slot name
        if (slotToUnequip == null)
        {
            var knownSlots = new[] { "main_hand", "off_hand", "head", "chest", "hands", "legs", "feet" };
            var matchedSlot = knownSlots.FirstOrDefault(s => s.Replace("_", "").Contains(targetLower) ||
                                                              s.Contains(targetLower));

            if (matchedSlot != null && _gameState.Player.EquipmentSlots.TryGetValue(matchedSlot, out var itemId) && itemId != null)
            {
                if (_gameState.PlayerInventory.Items.TryGetValue(itemId, out var inventoryItem))
                {
                    slotToUnequip = matchedSlot;
                    itemToUnequip = inventoryItem.Item;
                }
            }
        }

        if (slotToUnequip == null || itemToUnequip == null)
            return new ActionResult { Success = false, Message = $"You don't have '{target}' equipped." };

        // Unequip the item
        var unequippedItem = _gameState.Player.UnequipItem(slotToUnequip);

        if (unequippedItem != null)
        {
            return new ActionResult { Success = true, Message = $"You unequip {itemToUnequip.Name}." };
        }
        else
        {
            return new ActionResult { Success = false, Message = $"Cannot unequip {itemToUnequip.Name}." };
        }
    }

    private ActionResult HandleEquipped()
    {
        var equipped = new List<string>();

        // Use the dynamic equipment slot configuration
        foreach (var slotDef in _equipmentSlots.GetOrderedSlots())
        {
            if (_gameState.Player.EquipmentSlots.TryGetValue(slotDef.Id, out var itemId) && itemId != null)
            {
                if (_gameState.PlayerInventory.Items.TryGetValue(itemId, out var inventoryItem))
                {
                    var item = inventoryItem.Item;
                    var bonuses = new List<string>();
                    if (item.DamageBonus > 0) bonuses.Add($"+{item.DamageBonus} dmg");
                    if (item.ArmorBonus > 0) bonuses.Add($"+{item.ArmorBonus} armor");

                    string bonusText = bonuses.Count > 0 ? $" [{string.Join(", ", bonuses)}]" : "";
                    equipped.Add($"  {slotDef.DisplayName}: {item.Name}{bonusText}");
                }
            }
        }

        if (equipped.Count == 0)
            return new ActionResult { Success = true, Message = "You have nothing equipped." };

        var message = new StringBuilder();
        message.AppendLine("Currently Equipped:");
        foreach (var line in equipped)
        {
            message.AppendLine(line);
        }

        // Add total bonuses - calculate total damage and armor
        int totalDamage = _gameState.Player.GetTotalDamage(null);

        // Find equipped weapon from any weapon slot
        Item? equippedWeapon = null;
        foreach (var slotDef in _equipmentSlots.Slots)
        {
            if (slotDef.CompatibleTypes.Contains(ItemType.Weapon))
            {
                if (_gameState.Player.EquipmentSlots.TryGetValue(slotDef.Id, out var weaponId) && weaponId != null)
                {
                    equippedWeapon = _gameState.PlayerInventory.Items.GetValueOrDefault(weaponId)?.Item;
                    if (equippedWeapon != null) break;
                }
            }
        }

        int totalDamageWithWeapon = _gameState.Player.GetTotalDamage(equippedWeapon);
        int totalArmor = _gameState.Player.Armor;

        // Calculate equipped armor bonus from all armor slots
        int equippedArmorBonus = 0;
        foreach (var slotDef in _equipmentSlots.Slots)
        {
            if (slotDef.CompatibleTypes.Contains(ItemType.Armor))
            {
                if (_gameState.Player.EquipmentSlots.TryGetValue(slotDef.Id, out var armorItemId) && armorItemId != null)
                {
                    if (_gameState.PlayerInventory.Items.TryGetValue(armorItemId, out var armorItem))
                    {
                        equippedArmorBonus += armorItem.Item.ArmorBonus;
                    }
                }
            }
        }
        totalArmor += equippedArmorBonus;

        message.AppendLine();
        message.Append($"Total: {totalDamageWithWeapon} damage, {totalArmor} armor");

        return new ActionResult { Success = true, Message = message.ToString().TrimEnd() };
    }

    /// <summary>
    /// Handle buying an item from a merchant.
    /// </summary>
    private ActionResult HandleBuy(string npcName, string itemName)
    {
        // Check if economy is enabled
        if (!_economy.Enabled)
            return new ActionResult { Success = false, Message = "This game doesn't have an economy system." };

        var room = _gameState.GetCurrentRoom();

        // Find the merchant NPC
        Character? merchant = FindNpcInRoom(npcName, room);
        if (merchant == null)
        {
            // If no specific NPC given, find any merchant in the room
            merchant = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive && _gameState.NPCs[id].Role == NPCRole.Merchant)
                .Select(id => _gameState.NPCs[id])
                .FirstOrDefault();
        }

        if (merchant == null)
            return new ActionResult { Success = false, Message = "There's no merchant here to buy from." };

        if (!merchant.IsAlive)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is dead." };

        if (merchant.Role != NPCRole.Merchant)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is not a merchant." };

        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Buy what?" };

        // Find the item in merchant's inventory
        var itemLower = itemName.ToLower();
        var merchantItem = merchant.CarriedItems.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (merchantItem == null)
            return new ActionResult { Success = false, Message = $"{merchant.Name} doesn't have '{itemName}' for sale." };

        var item = merchantItem.Item;
        var price = item.GetBuyPrice();

        // Check if player can afford it
        if (!_gameState.Player.Wallet.CanAfford(price))
        {
            var playerMoney = _gameState.Player.Wallet.Format(_economy);
            var priceFormatted = Wallet.FormatAmount(price, _economy);
            return new ActionResult
            {
                Success = false,
                Message = $"{item.Name} costs {priceFormatted}. You only have {playerMoney}."
            };
        }

        var purchasedItem = item.CloneRuntime();
        if (!_gameState.PlayerInventory.AddItem(purchasedItem, 1))
        {
            return new ActionResult
            {
                Success = false,
                Message = $"You cannot carry {item.Name}; your inventory is too heavy."
            };
        }

        // Complete the transaction
        _gameState.Player.Wallet.Remove(price);
        merchant.Wallet.Add(price);

        // Remove from merchant (if quantity = 1, remove entirely)
        if (merchantItem.Quantity <= 1)
            merchant.CarriedItems.Remove(item.Id);
        else
            merchantItem.Quantity--;

        var priceDisplay = Wallet.FormatAmount(price, _economy);
        return new ActionResult
        {
            Success = true,
            Message = $"You bought {item.Name} from {merchant.Name} for {priceDisplay}."
        };
    }

    /// <summary>
    /// Handle selling an item to a merchant.
    /// </summary>
    private ActionResult HandleSell(string npcName, string itemName)
    {
        // Check if economy is enabled
        if (!_economy.Enabled)
            return new ActionResult { Success = false, Message = "This game doesn't have an economy system." };

        var room = _gameState.GetCurrentRoom();

        // Find the merchant NPC
        Character? merchant = FindNpcInRoom(npcName, room);
        if (merchant == null)
        {
            // If no specific NPC given, find any merchant in the room
            merchant = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive && _gameState.NPCs[id].Role == NPCRole.Merchant)
                .Select(id => _gameState.NPCs[id])
                .FirstOrDefault();
        }

        if (merchant == null)
            return new ActionResult { Success = false, Message = "There's no merchant here to sell to." };

        if (!merchant.IsAlive)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is dead." };

        if (merchant.Role != NPCRole.Merchant)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is not a merchant." };

        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Sell what?" };

        // Find the item in player's inventory
        var itemLower = itemName.ToLower();
        var playerItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (playerItem == null)
            return new ActionResult { Success = false, Message = $"You don't have '{itemName}' to sell." };

        var item = playerItem.Item;

        // Check if item can be sold
        if (!item.CanBeSold)
            return new ActionResult { Success = false, Message = $"{item.Name} cannot be sold." };

        if (_gameState.Player.EquipmentSlots.Values.Any(equippedItemId =>
                string.Equals(equippedItemId, item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return new ActionResult { Success = false, Message = $"Unequip {item.Name} before selling it." };
        }

        var sellPrice = item.GetSellPrice();

        // Check if merchant can afford it
        if (!merchant.Wallet.CanAfford(sellPrice))
        {
            return new ActionResult
            {
                Success = false,
                Message = $"{merchant.Name} doesn't have enough money to buy {item.Name}."
            };
        }

        // Complete the transaction
        if (!_gameState.PlayerInventory.RemoveItem(item.Id, 1))
            return new ActionResult { Success = false, Message = $"You no longer have {item.Name} to sell." };

        merchant.Wallet.Remove(sellPrice);
        _gameState.Player.Wallet.Add(sellPrice);

        // Add to merchant's inventory
        if (merchant.CarriedItems.ContainsKey(item.Id))
            merchant.CarriedItems[item.Id].Quantity++;
        else
            merchant.CarriedItems[item.Id] = new InventoryItem { Item = item.CloneRuntime(), Quantity = 1 };

        var priceDisplay = Wallet.FormatAmount(sellPrice, _economy);
        return new ActionResult
        {
            Success = true,
            Message = $"You sold {item.Name} to {merchant.Name} for {priceDisplay}."
        };
    }

    /// <summary>
    /// Handle viewing a merchant's wares.
    /// </summary>
    private ActionResult HandleShop(string npcName)
    {
        // Check if economy is enabled
        if (!_economy.Enabled)
            return new ActionResult { Success = false, Message = "This game doesn't have an economy system." };

        var room = _gameState.GetCurrentRoom();

        // Find the merchant NPC
        Character? merchant = FindNpcInRoom(npcName, room);
        if (merchant == null)
        {
            // If no specific NPC given, find any merchant in the room
            merchant = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive && _gameState.NPCs[id].Role == NPCRole.Merchant)
                .Select(id => _gameState.NPCs[id])
                .FirstOrDefault();
        }

        if (merchant == null)
            return new ActionResult { Success = false, Message = "There's no merchant here." };

        if (!merchant.IsAlive)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is dead." };

        if (merchant.Role != NPCRole.Merchant)
            return new ActionResult { Success = false, Message = $"{merchant.Name} is not a merchant." };

        if (merchant.CarriedItems.Count == 0)
            return new ActionResult { Success = true, Message = $"{merchant.Name} has nothing for sale right now." };

        var message = new StringBuilder();
        message.AppendLine($"🛒 {merchant.Name}'s Wares:");
        message.AppendLine();

        foreach (var inventoryItem in merchant.CarriedItems.Values)
        {
            var item = inventoryItem.Item;
            var price = item.GetBuyPrice();
            var priceDisplay = Wallet.FormatAmount(price, _economy);

            var itemInfo = $"  • {item.Name}";
            if (inventoryItem.Quantity > 1)
                itemInfo += $" (x{inventoryItem.Quantity})";
            itemInfo += $" - {priceDisplay}";

            // Add item type info
            if (item.Type == ItemType.Weapon)
                itemInfo += $" [+{item.DamageBonus} dmg]";
            else if (item.Type == ItemType.Armor)
                itemInfo += $" [+{item.ArmorBonus} armor]";
            else if (item.IsConsumable)
                itemInfo += " [consumable]";

            message.AppendLine(itemInfo);
        }

        message.AppendLine();
        var playerMoney = _gameState.Player.Wallet.Format(_economy);
        message.Append($"Your money: {playerMoney}");

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    /// <summary>
    /// Find an NPC in the current room by name.
    /// </summary>
    private Character? FindNpcInRoom(string npcName, Room room)
    {
        if (string.IsNullOrWhiteSpace(npcName))
            return null;

        var npcNameLower = npcName.ToLower();
        var npcId = room.NPCIds.FirstOrDefault(id =>
            _gameState.NPCs.ContainsKey(id) &&
            _gameState.NPCs[id].Name.ToLower().Contains(npcNameLower));

        return npcId != null ? _gameState.NPCs[npcId] : null;
    }

    // ========== GATHERING SYSTEM ==========

    /// <summary>
    /// Handle gathering/searching for resources.
    /// </summary>
    private async Task<ActionResult> HandleGatherAsync(string target, string details)
    {
        var room = _gameState.GetCurrentRoom();
        var targetLower = target?.ToLower() ?? "";

        // Check for defined resources in this room
        if (room.Resources?.Resources.Count > 0)
        {
            // Try to find a matching resource
            var matchingResource = room.Resources.Resources
                .FirstOrDefault(r => 
                    r.ItemId.ToLower().Contains(targetLower) ||
                    (r.DisplayName?.ToLower().Contains(targetLower) ?? false) ||
                    targetLower.Contains(r.GatherVerb.ToLower()));

            if (matchingResource != null)
            {
                return TryGatherResource(matchingResource, room);
            }

            // Check if resource tags match
            var hasMatchingTag = room.Resources.ResourceTags
                .Any(t => t.ToLower().Contains(targetLower) || targetLower.Contains(t.ToLower()));

            if (hasMatchingTag && room.Resources.Resources.Count > 0)
            {
                // Pick reproducibly from the authored resources for this turn.
                var resourceSelector = CreateTurnRandom($"resource-selection:{room.Id}:{targetLower}");
                var randomResource = room.Resources.Resources[resourceSelector.Next(room.Resources.Resources.Count)];
                return TryGatherResource(randomResource, room);
            }
        }

        // If GM has authority to decide resources dynamically
        if (_authority.CanDecideResources)
        {
            return await TryDynamicGatherAsync(target ?? "resources", room);
        }

        // No resources found and no dynamic gathering allowed
        var biomeInfo = room.Resources?.Biome ?? "this area";
        return new ActionResult
        {
            Success = false,
            Message = $"You search {biomeInfo} thoroughly but find no {target}."
        };
    }

    /// <summary>
    /// Attempt to gather a defined resource.
    /// </summary>
    private ActionResult TryGatherResource(GatherableResource resource, Room room)
    {
        // Check for required tool
        if (!string.IsNullOrEmpty(resource.RequiredTool))
        {
            var hasTool = _gameState.PlayerInventory.Items.Values
                .Any(ii => ii.Item.Id == resource.RequiredTool || 
                           ii.Item.Name.ToLower().Contains(resource.RequiredTool.ToLower()));
            if (!hasTool)
            {
                return new ActionResult
                {
                    Success = false,
                    Message = $"You need a {resource.RequiredTool} to gather this resource."
                };
            }
        }

        // Check if resource is depleted
        if (room.Resources?.DepletedResources.TryGetValue(resource.ItemId, out var turnsRemaining) == true)
        {
            return new ActionResult
            {
                Success = false,
                Message = turnsRemaining < 0
                    ? $"The supply of {resource.DisplayName ?? resource.ItemId} here has been exhausted."
                    : $"This area has been searched recently. Try again in {turnsRemaining} turn(s)."
            };
        }

        // Rolls are derived from persisted world state, so saving/reloading before
        // an action cannot change its outcome.
        var random = CreateTurnRandom($"gather:{room.Id}:{resource.ItemId}");
        var roll = random.Next(100);
        var successChance = resource.FindChance;

        // Apply skill bonus if applicable
        if (!string.IsNullOrEmpty(resource.RelatedSkill) && 
            _gameState.Player.Skills.TryGetValue(resource.RelatedSkill, out var skillLevel))
        {
            successChance += skillLevel * 2;
        }

        successChance = Math.Clamp(successChance, 0, 100);
        if (roll >= successChance)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"You search carefully but don't find any {resource.DisplayName ?? resource.ItemId}."
            };
        }

        // Success! Determine quantity
        var minimumQuantity = Math.Max(1, resource.MinQuantity);
        var maximumQuantity = Math.Max(minimumQuantity, resource.MaxQuantity);
        var quantity = minimumQuantity == maximumQuantity
            ? minimumQuantity
            : (int)random.NextInt64(minimumQuantity, (long)maximumQuantity + 1);

        // Get or create the item
        Item? item = null;
        if (_game?.Items.TryGetValue(resource.ItemId, out item) != true || item == null)
        {
            // Create a basic item if not defined
            item = new Item
            {
                Id = resource.ItemId,
                Name = resource.DisplayName ?? resource.ItemId,
                Type = ItemType.CraftingMaterial,
                Stackable = true
            };
        }

        // Add an independent runtime instance to inventory.
        if (!_gameState.PlayerInventory.AddItem(item.CloneRuntime(), quantity))
        {
            return new ActionResult
            {
                Success = false,
                Message = $"You find {quantity}x {item.Name}, but cannot carry the extra weight."
            };
        }

        // Positive values count down each world turn; -1 represents permanent
        // depletion for a non-renewable resource.
        if (room.Resources != null)
        {
            if (resource.Renewable)
                room.Resources.DepletedResources[resource.ItemId] = Math.Max(1, resource.RespawnTurns ?? 10);
            else
                room.Resources.DepletedResources[resource.ItemId] = -1;
        }

        var verb = resource.GatherVerb.ToLowerInvariant() switch
        {
            "gather" => "found",
            "mine" => "mined",
            "forage" => "foraged",
            "pick" => "picked",
            "chop" => "chopped",
            var customVerb when customVerb.EndsWith('e') => $"{customVerb}d",
            var customVerb => $"{customVerb}ed"
        };
        return new ActionResult
        {
            Success = true,
            Message = $"You {verb} {quantity}x {item.Name}!"
        };
    }

    /// <summary>
    /// Attempt dynamic resource gathering using LLM.
    /// </summary>
    private async Task<ActionResult> TryDynamicGatherAsync(string target, Room room)
    {
        var biome = room.Resources?.Biome ?? "unknown";
        var tags = room.Resources?.ResourceTags ?? new List<string>();
        
        var prompt = $@"You are a Game Master deciding if a player can find resources.

Location: {room.Name}
Description: {room.Description}
Biome: {biome}
Resource tags: {string.Join(", ", tags)}

Player is searching for: {target}

Based on the location, decide:
1. Can this resource reasonably be found here? (yes/no)
2. If yes, what exactly did they find? (item name)
3. Quantity found (1-3)
4. A brief narration of finding it

Respond in JSON format only:
{{""found"": true/false, ""itemName"": ""name"", ""quantity"": 1, ""narration"": ""..."" }}";

        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = "You are a Game Master. Respond only with valid JSON." },
                new() { Role = "user", Content = prompt }
            };

            var response = await _llmClient.ChatAsync(messages);
            
            // Parse response
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"\{[^}]+\}");
            if (jsonMatch.Success)
            {
                var json = System.Text.Json.JsonDocument.Parse(jsonMatch.Value);
                var found = json.RootElement.GetProperty("found").GetBoolean();
                
                if (!found)
                {
                    return new ActionResult
                    {
                        Success = false,
                        Message = $"You search the area but don't find any {target} here."
                    };
                }

                var itemName = json.RootElement.GetProperty("itemName").GetString() ?? target;
                var quantity = json.RootElement.TryGetProperty("quantity", out var qtyElem) ? qtyElem.GetInt32() : 1;
                var narration = json.RootElement.TryGetProperty("narration", out var narrElem) ? narrElem.GetString() : null;

                // Create a dynamic item
                var itemId = itemName.ToLower().Replace(" ", "_");
                var item = new Item
                {
                    Id = itemId,
                    Name = itemName,
                    Type = ItemType.CraftingMaterial,
                    Stackable = true,
                    Value = 5
                };

                if (!_gameState.PlayerInventory.AddItem(item, quantity))
                {
                    return new ActionResult
                    {
                        Success = false,
                        Message = $"You find {quantity}x {itemName}, but cannot carry the extra weight."
                    };
                }

                return new ActionResult
                {
                    Success = true,
                    Message = narration ?? $"You found {quantity}x {itemName}!"
                };
            }
        }
        catch
        {
            // Fall through to default
        }

        return new ActionResult
        {
            Success = false,
            Message = $"You search but don't find any {target} in this location."
        };
    }

    private Random CreateTurnRandom(string scope)
    {
        var material = Encoding.UTF8.GetBytes(
            $"{_gameState.WorldSeed}\u001F{_gameState.TurnNumber + 1}\u001F{_gameState.CurrentRoomId}\u001F{scope}");
        var hash = System.Security.Cryptography.SHA256.HashData(material);
        var seed = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(hash);
        return new Random(seed);
    }

    // ========== CRAFTING SYSTEM ==========

    /// <summary>
    /// Handle crafting requests.
    /// </summary>
    private ActionResult HandleCraft(string npcName, string itemToCraft)
    {
        if (!_crafting.Enabled)
            return new ActionResult { Success = false, Message = "Crafting is not available in this game." };

        var room = _gameState.GetCurrentRoom();

        // Find crafter NPC
        Character? crafter = null;
        if (!string.IsNullOrWhiteSpace(npcName))
        {
            crafter = FindNpcInRoom(npcName, room);
        }
        else
        {
            // Find any crafter in the room
            crafter = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].CanCraft && _gameState.NPCs[id].IsAlive)
                .Select(id => _gameState.NPCs[id])
                .FirstOrDefault();
        }

        if (crafter == null)
            return new ActionResult { Success = false, Message = "There's no one here who can craft." };

        if (!crafter.CanCraft)
            return new ActionResult { Success = false, Message = $"{crafter.Name} doesn't know how to craft." };

        if (!crafter.IsAlive)
            return new ActionResult { Success = false, Message = $"{crafter.Name} is dead." };

        // If no item specified, show what they can craft
        if (string.IsNullOrWhiteSpace(itemToCraft))
        {
            return HandleRecipes(crafter.Name);
        }

        // Find the recipe
        var itemLower = itemToCraft.ToLower();
        var recipe = _crafting.Recipes.Values.FirstOrDefault(r =>
            r.Name.Contains(itemToCraft, StringComparison.OrdinalIgnoreCase) ||
            r.OutputItemId.Replace('_', ' ').Contains(itemLower, StringComparison.OrdinalIgnoreCase) ||
            (_game?.Items.TryGetValue(r.OutputItemId, out var authoredOutput) == true &&
             authoredOutput.Name.Contains(itemToCraft, StringComparison.OrdinalIgnoreCase)));

        if (recipe == null)
        {
            return new ActionResult { Success = false, Message = $"{crafter.Name} doesn't know how to craft '{itemToCraft}'." };
        }

        // Check if this crafter can make this recipe
        var canMake = crafter.KnownRecipes.Contains(recipe.Id) ||
                      (crafter.CraftingSpecialty != null && recipe.CraftingSpecialty == crafter.CraftingSpecialty);

        if (!canMake)
            return new ActionResult { Success = false, Message = $"{crafter.Name} can't craft {recipe.Name}." };

        // Check if player has ingredients
        if (!recipe.CanCraft(_gameState.PlayerInventory))
        {
            var missing = recipe.Ingredients
                .Where(i => (_gameState.PlayerInventory.GetItem(i.ItemId)?.Quantity ?? 0) < i.Quantity)
                .Select(i => $"{i.ItemName ?? i.ItemId} x{i.Quantity}")
                .ToList();

            return new ActionResult
            {
                Success = false,
                Message = $"You don't have the required materials. Need: {string.Join(", ", missing)}"
            };
        }

        // Check if player can afford crafting cost
        if (recipe.CraftingCost > 0 && _economy.Enabled)
        {
            if (!_gameState.Player.Wallet.CanAfford(recipe.CraftingCost))
            {
                var costDisplay = Wallet.FormatAmount(recipe.CraftingCost, _economy);
                return new ActionResult
                {
                    Success = false,
                    Message = $"Crafting {recipe.Name} costs {costDisplay}. You can't afford it."
                };
            }
        }

        var outputItem = _game?.Items.TryGetValue(recipe.OutputItemId, out var authoredItem) == true
            ? authoredItem.CloneRuntime()
            : new Item
            {
                Id = recipe.OutputItemId,
                Name = recipe.Name,
                Type = ItemType.Miscellaneous
            };
        var ingredientWeight = recipe.Ingredients.Sum(ingredient =>
            (_gameState.PlayerInventory.GetItem(ingredient.ItemId)?.Item.Weight ?? 0) * ingredient.Quantity);
        var resultingWeight = _gameState.PlayerInventory.CurrentWeight - ingredientWeight +
                              outputItem.Weight * recipe.OutputQuantity;
        if (resultingWeight > _gameState.PlayerInventory.MaxWeight)
        {
            return new ActionResult
            {
                Success = false,
                Message = $"You would not be able to carry {recipe.OutputQuantity}x {outputItem.Name}."
            };
        }

        // Consume ingredients
        foreach (var ingredient in recipe.Ingredients)
        {
            _gameState.PlayerInventory.RemoveItem(ingredient.ItemId, ingredient.Quantity);
        }

        // Pay crafting cost
        if (recipe.CraftingCost > 0 && _economy.Enabled)
        {
            _gameState.Player.Wallet.Remove(recipe.CraftingCost);
            crafter.Wallet.Add(recipe.CraftingCost);
        }

        _gameState.PlayerInventory.AddItem(outputItem, recipe.OutputQuantity);

        var message = new StringBuilder();
        message.Append($"{crafter.Name} crafts {recipe.OutputQuantity}x {recipe.Name} for you!");
        if (recipe.CraftingCost > 0 && _economy.Enabled)
        {
            message.Append($" (Cost: {Wallet.FormatAmount(recipe.CraftingCost, _economy)})");
        }

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    /// <summary>
    /// Handle viewing available recipes.
    /// </summary>
    private ActionResult HandleRecipes(string npcName)
    {
        if (!_crafting.Enabled)
            return new ActionResult { Success = false, Message = "Crafting is not available in this game." };

        var room = _gameState.GetCurrentRoom();

        // Find crafter NPC
        Character? crafter = null;
        if (!string.IsNullOrWhiteSpace(npcName))
        {
            crafter = FindNpcInRoom(npcName, room);
        }
        else
        {
            crafter = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].CanCraft && _gameState.NPCs[id].IsAlive)
                .Select(id => _gameState.NPCs[id])
                .FirstOrDefault();
        }

        if (crafter == null)
            return new ActionResult { Success = false, Message = "There's no crafter here." };

        // Get recipes this crafter can make
        var recipes = _crafting.Recipes.Values
            .Where(r => crafter.KnownRecipes.Contains(r.Id) ||
                        (crafter.CraftingSpecialty != null && r.CraftingSpecialty == crafter.CraftingSpecialty))
            .ToList();

        if (recipes.Count == 0)
            return new ActionResult { Success = true, Message = $"{crafter.Name} doesn't have any recipes available." };

        var message = new StringBuilder();
        message.AppendLine($"⚒️ {crafter.Name}'s Recipes:");
        message.AppendLine();

        foreach (var recipe in recipes)
        {
            message.Append($"  • {recipe.Name}");
            if (recipe.CraftingCost > 0 && _economy.Enabled)
            {
                message.Append($" - {Wallet.FormatAmount(recipe.CraftingCost, _economy)}");
            }
            message.AppendLine();
            message.AppendLine($"    Requires: {recipe.GetIngredientsDisplay()}");
        }

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    // ========== QUEST SYSTEM ==========

    /// <summary>
    /// Handle viewing quest log.
    /// </summary>
    private ActionResult HandleQuests()
    {
        var offeredQuests = _gameState.ActiveQuests.Where(q => q.Status == QuestStatus.Offered).ToList();
        var activeQuests = _gameState.ActiveQuests.Where(q => q.Status == QuestStatus.Accepted || q.Status == QuestStatus.InProgress).ToList();
        var completedQuests = _gameState.ActiveQuests.Where(q => q.Status == QuestStatus.Completed || q.Status == QuestStatus.TurnedIn).ToList();

        if (offeredQuests.Count == 0 && activeQuests.Count == 0 && completedQuests.Count == 0)
            return new ActionResult { Success = true, Message = "You have no quests. Talk to NPCs to find opportunities." };

        var message = new StringBuilder();
        message.AppendLine("📜 Quest Log:");
        message.AppendLine();

        if (offeredQuests.Count > 0)
        {
            message.AppendLine("=== Available Quests ===");
            foreach (var quest in offeredQuests)
            {
                message.AppendLine($"○ {quest.Title} [{quest.Id}]");
                message.AppendLine($"   {quest.Description}");
                message.AppendLine($"   Use 'accept {quest.Id}' to begin.");
                message.AppendLine();
            }
        }

        if (activeQuests.Count > 0)
        {
            message.AppendLine("=== Active Quests ===");
            foreach (var quest in activeQuests)
            {
                var typeIcon = quest.Type switch
                {
                    QuestType.Story => "⭐",
                    QuestType.Job => "💼",
                    QuestType.CraftingOrder => "⚒️",
                    QuestType.Bounty => "⚔️",
                    _ => "📋"
                };
                message.AppendLine($"{typeIcon} {quest.Title}");
                message.AppendLine($"   {quest.Description}");

                if (quest.Requirements.Count > 0)
                {
                    foreach (var req in quest.Requirements)
                    {
                        var checkmark = req.IsMet ? "✓" : "○";
                        message.AppendLine($"   {checkmark} {req.GetDisplayText()}");
                    }
                }
                else if (quest.Objectives.Count > 0)
                {
                    for (int i = 0; i < quest.Objectives.Count; i++)
                    {
                        var completed = i < quest.CompletedObjectives.Count;
                        var checkmark = completed ? "✓" : "○";
                        message.AppendLine($"   {checkmark} {quest.Objectives[i]}");
                    }
                }

                if (quest.Rewards.Currency > 0 && _economy.Enabled)
                {
                    message.AppendLine($"   Reward: {Wallet.FormatAmount(quest.Rewards.Currency, _economy)}");
                }
                message.AppendLine();
            }
        }

        if (completedQuests.Count > 0)
        {
            message.AppendLine("=== Completed ===");
            foreach (var quest in completedQuests.Take(5))
            {
                message.AppendLine($"✓ {quest.Title}");
            }
        }

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    private ActionResult HandleAcceptQuest(string questName)
    {
        var quest = ResolveQuest(questName);
        if (quest == null)
            return new ActionResult { Success = false, Message = $"No quest matches '{questName}'." };

        if (quest.Status != QuestStatus.Offered)
            return new ActionResult { Success = false, Message = $"{quest.Title} is already {quest.Status.ToString().ToLowerInvariant()}." };

        if (!string.IsNullOrWhiteSpace(quest.GiverNpcId) &&
            !_gameState.GetCurrentRoom().NPCIds.Contains(quest.GiverNpcId, StringComparer.OrdinalIgnoreCase))
        {
            var giverName = _gameState.NPCs.TryGetValue(quest.GiverNpcId, out var giver)
                ? giver.Name
                : quest.GiverNpcId;
            return new ActionResult { Success = false, Message = $"Speak with {giverName} to accept {quest.Title}." };
        }

        return new ActionResult
        {
            Success = true,
            Message = $"You accept the quest: {quest.Title}.",
            Events = new()
            {
                WorldEvent.Create(
                    WorldEventType.QuestAccepted,
                    quest.Id,
                    actorId: _gameState.Player.Id,
                    roomId: _gameState.CurrentRoomId)
            }
        };
    }

    private ActionResult HandleTurnInQuest(string questName)
    {
        var quest = ResolveQuest(questName);
        if (quest == null)
            return new ActionResult { Success = false, Message = $"No quest matches '{questName}'." };
        if (quest.Status != QuestStatus.Completed)
            return new ActionResult { Success = false, Message = $"{quest.Title} is not ready to turn in." };

        if (!string.IsNullOrWhiteSpace(quest.GiverNpcId) &&
            !_gameState.GetCurrentRoom().NPCIds.Contains(quest.GiverNpcId, StringComparer.OrdinalIgnoreCase))
        {
            var giverName = _gameState.NPCs.TryGetValue(quest.GiverNpcId, out var giver)
                ? giver.Name
                : quest.GiverNpcId;
            return new ActionResult { Success = false, Message = $"Return to {giverName} to turn in {quest.Title}." };
        }

        return new ActionResult
        {
            Success = true,
            Message = $"You turn in the quest: {quest.Title}.",
            Events = new()
            {
                WorldEvent.Create(
                    WorldEventType.QuestTurnedIn,
                    quest.Id,
                    actorId: _gameState.Player.Id,
                    roomId: _gameState.CurrentRoomId)
            }
        };
    }

    private ActionResult HandleProjects()
    {
        if (_gameState.WorldProjects.Count == 0)
            return new ActionResult { Success = true, Message = "There are no world projects underway." };

        var message = new StringBuilder("🏗️ World Projects:\n\n");
        foreach (var project in _gameState.WorldProjects)
        {
            var icon = project.Status switch
            {
                WorldProjectStatus.Completed => "✓",
                WorldProjectStatus.Active => "⚒️",
                WorldProjectStatus.Locked => "🔒",
                _ => "○"
            };
            message.AppendLine($"{icon} {project.Name} [{project.Id}] — {project.Status}");
            message.AppendLine($"   {project.Description}");

            var stage = project.CurrentStage;
            if (stage != null && project.Status != WorldProjectStatus.Locked)
            {
                message.AppendLine($"   Current stage: {stage.Name}");
                message.AppendLine($"   {stage.Description}");
                foreach (var requirement in stage.Requirements)
                {
                    var requirementIcon = requirement.IsMet ? "✓" : "○";
                    var label = requirement.Description ?? requirement.TargetId ?? requirement.Type.ToString();
                    message.AppendLine($"   {requirementIcon} {label} ({requirement.CurrentAmount}/{requirement.RequiredAmount})");
                }
            }

            message.AppendLine();
        }

        message.Append("Contribute materials with: contribute [quantity] [item] to [project]");
        return new ActionResult { Success = true, Message = message.ToString() };
    }

    private ActionResult HandleProjectContribution(string projectName, string contributionDetails)
    {
        var project = ResolveProject(projectName);
        if (project == null)
            return new ActionResult { Success = false, Message = $"No world project matches '{projectName}'." };

        var stage = project.CurrentStage;
        if (stage == null)
            return new ActionResult { Success = false, Message = $"{project.Name} is already complete." };

        var amountMatch = Regex.Match(contributionDetails ?? string.Empty, @"\b(\d+)\b");
        var requestedAmount = amountMatch.Success && long.TryParse(amountMatch.Value, out var parsedAmount)
            ? parsedAmount
            : 0;
        var normalizedDetails = Regex.Replace(contributionDetails ?? string.Empty, @"\b\d+\b", " ")
            .Replace('_', ' ')
            .Trim();

        var currencyRequirement = stage.Requirements.FirstOrDefault(requirement =>
            !requirement.IsMet && requirement.Type == WorldProjectRequirementType.Currency);
        var isCurrency = currencyRequirement != null &&
            (normalizedDetails.Contains("currency", StringComparison.OrdinalIgnoreCase) ||
             normalizedDetails.Contains("coin", StringComparison.OrdinalIgnoreCase) ||
             normalizedDetails.Contains("gold", StringComparison.OrdinalIgnoreCase) ||
             normalizedDetails.Contains("silver", StringComparison.OrdinalIgnoreCase));

        WorldProjectProgressionResult result;
        if (isCurrency)
        {
            var amount = requestedAmount > 0 ? requestedAmount : _gameState.Player.Wallet.TotalBaseUnits;
            result = _worldProjectService.Contribute(
                _gameState,
                project.Id,
                WorldProjectRequirementType.Currency,
                null,
                amount,
                effectiveTurn: _gameState.TurnNumber + 1);
        }
        else
        {
            var itemRequirement = stage.Requirements.FirstOrDefault(requirement =>
                !requirement.IsMet &&
                requirement.Type == WorldProjectRequirementType.Item &&
                (string.IsNullOrWhiteSpace(normalizedDetails) ||
                 (requirement.TargetId?.Replace('_', ' ').Contains(normalizedDetails, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 normalizedDetails.Contains(requirement.TargetId?.Replace('_', ' ') ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                ?? stage.Requirements.FirstOrDefault(requirement =>
                    !requirement.IsMet && requirement.Type == WorldProjectRequirementType.Item);

            if (itemRequirement?.TargetId == null)
                return new ActionResult { Success = false, Message = "That project stage does not need an item contribution." };

            var inventoryItem = _gameState.PlayerInventory.Items.FirstOrDefault(pair =>
                pair.Key.Equals(itemRequirement.TargetId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(inventoryItem.Key) && !string.IsNullOrWhiteSpace(normalizedDetails))
            {
                inventoryItem = _gameState.PlayerInventory.Items.FirstOrDefault(pair =>
                    pair.Value.Item.Name.Contains(normalizedDetails, StringComparison.OrdinalIgnoreCase));
            }
            var amount = requestedAmount > 0
                ? requestedAmount
                : inventoryItem.Value?.Quantity ?? 0;
            result = _worldProjectService.Contribute(
                _gameState,
                project.Id,
                WorldProjectRequirementType.Item,
                itemRequirement.TargetId,
                amount,
                effectiveTurn: _gameState.TurnNumber + 1);
        }

        var success = result.Events.Any(worldEvent => worldEvent.Type == WorldEventType.ProjectContributed);
        return new ActionResult
        {
            Success = success,
            Message = string.Join("\n", result.Messages),
            Events = result.Events
        };
    }

    /// <summary>
    /// Determines equipment slot based on item type and the game's slot configuration.
    /// Uses the dynamic equipment slot system.
    /// </summary>
    private string DetermineSlotFromType(Item item)
    {
        return _equipmentSlots.DetermineSlotForItem(item) ?? "";
    }

    private ActionResult HandleUnknownAction(string command)
    {
        // For unknown actions, let the LLM narrate what happened
        return new ActionResult
        {
            Success = false,
            Message = $"You tried to: {command}"
        };
    }

    private ActionResult HandleHelp()
    {
        return new ActionResult { Success = true, Message = GetAvailableActions() };
    }

    private ActionResult HandleDisplayMode(string? mode)
    {
        var modeLower = mode?.ToLower() ?? "";

        // Check if showing current mode
        if (string.IsNullOrWhiteSpace(mode))
        {
            return new ActionResult 
            { 
                Success = true, 
                Message = $"Current display mode: {DisplayMode}\n\nAvailable modes:\n• minimal - Location, health, exits, NPCs only\n• standard - Adds currency and inventory\n• detailed - Adds combat stats (DMG/ARM) on health line"
            };
        }

        // Try to parse the mode
        GameStateDisplayMode? newMode = modeLower switch
        {
            "minimal" or "min" or "simple" => GameStateDisplayMode.Minimal,
            "standard" or "normal" or "default" => GameStateDisplayMode.Standard,
            "detailed" or "full" or "verbose" => GameStateDisplayMode.Detailed,
            _ => null  // Invalid mode
        };

        // If mode was not recognized
        if (newMode == null)
        {
            return new ActionResult 
            { 
                Success = false, 
                Message = $"Invalid mode '{mode}'. Use: minimal, standard, or detailed"
            };
        }

        // Change the mode
        DisplayMode = newMode.Value;
        return new ActionResult 
        { 
            Success = true, 
            Message = $"Display mode changed to: {DisplayMode}"
        };
    }

    private ActionResult HandleStatus(string? mode = null)
    {
        if (_gameState.InCombatMode)
        {
            return new ActionResult { Success = true, Message = GetCombatStatus() };
        }

        var currentRoom = _gameState.GetCurrentRoom();
        var message = new StringBuilder();
        
        // Determine display mode: simple, detailed, or full
        string displayMode = mode?.ToLower() ?? "simple";
        
        if (displayMode == "detailed" || displayMode == "full" || displayMode == "advanced" || displayMode == "stats")
        {
            // Advanced status with detailed stats
            message.AppendLine("═══════════════════════════════════");
            message.AppendLine($"📍 Location: {currentRoom.Name}");
            message.AppendLine($"❤️  Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
            message.AppendLine();
            
            // Calculate equipped bonuses
            var equippedItems = new Dictionary<string, Item>();
            foreach (var slot in _gameState.Player.EquipmentSlots)
            {
                if (slot.Value != null && _gameState.PlayerInventory.Items.TryGetValue(slot.Value, out var invItem))
                {
                    equippedItems[slot.Key] = invItem.Item;
                }
            }

            int equipmentDamage = equippedItems.Values.Sum(i => i.DamageBonus);
            int equipmentArmor = equippedItems.Values.Sum(i => i.ArmorBonus);
            
            // Base stats
            int baseStrength = _gameState.Player.Strength;
            int baseAgility = _gameState.Player.Agility;
            int baseArmor = _gameState.Player.Armor;
            
            // Total stats (base + equipment)
            int totalDamage = _gameState.Player.GetTotalDamage(equippedItems.Values.FirstOrDefault(i => i.DamageBonus > 0));
            int totalArmor = _gameState.Player.GetTotalArmor(equippedItems);
            
            message.AppendLine("⚔️  Combat Stats:");
            message.AppendLine($"   Strength: {baseStrength} (Base Damage: {5 + (baseStrength - 10) / 2})");
            message.AppendLine($"   Agility: {baseAgility} (Crit: +{Math.Max(0, baseAgility - 10)}%, Dodge: +{Math.Max(0, (baseAgility - 10) * 0.5):F1}%)");
            message.AppendLine($"   Base Armor: {baseArmor}");
            
            if (equipmentDamage > 0 || equipmentArmor > 0)
            {
                message.AppendLine();
                message.AppendLine("🛡️  Equipment Bonuses:");
                if (equipmentDamage > 0)
                    message.AppendLine($"   Weapon Damage: +{equipmentDamage}");
                if (equipmentArmor > 0)
                    message.AppendLine($"   Armor Rating: +{equipmentArmor}");
                message.AppendLine();
                message.AppendLine($"💪 Total Damage: {totalDamage}");
                message.AppendLine($"🛡️  Total Armor: {totalArmor}");
            }

            message.AppendLine();
            message.AppendLine($"⭐ Level: {_gameState.Player.Level} | XP: {_gameState.Player.Experience}");

            // Show currency if economy is enabled
            if (_economy.Enabled)
            {
                var currencyDisplay = _gameState.Player.Wallet.Format(_economy);
                message.AppendLine($"💰 Currency: {currencyDisplay}");
            }

            // Show exits
            var exits = currentRoom.GetAvailableExits();
            if (exits.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("🚪 Exits: " + string.Join(", ", exits.Select(e => e.DisplayName)));
            }

            // Show NPCs
            if (currentRoom.NPCIds.Count > 0)
            {
                var npcNames = currentRoom.NPCIds
                    .Where(id => _gameState.NPCs.ContainsKey(id))
                    .Select(id =>
                    {
                        var npc = _gameState.NPCs[id];
                        string markers = "";
                        if (!npc.IsAlive)
                        {
                            markers += " ☠️";
                            if (HasLoot(npc))
                                markers += " 💰";
                        }
                        return npc.Name + markers;
                    })
                    .ToList();

                if (npcNames.Count > 0)
                {
                    message.AppendLine("👥 NPCs: " + string.Join(", ", npcNames));
                }
            }

            message.AppendLine("═══════════════════════════════════");
        }
        else
        {
            // Simple status - just health, exits, and NPCs
            message.AppendLine($"📍 {currentRoom.Name}");
            message.AppendLine($"❤️  Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
            
            // Show exits
            var exits = currentRoom.GetAvailableExits();
            if (exits.Count > 0)
            {
                message.AppendLine("🚪 Exits: " + string.Join(", ", exits.Select(e => e.DisplayName)));
            }

            // Show NPCs
            if (currentRoom.NPCIds.Count > 0)
            {
                var npcNames = currentRoom.NPCIds
                    .Where(id => _gameState.NPCs.ContainsKey(id))
                    .Select(id =>
                    {
                        var npc = _gameState.NPCs[id];
                        string markers = "";
                        if (!npc.IsAlive)
                        {
                            markers += " ☠️";
                            if (HasLoot(npc))
                                markers += " 💰";
                        }
                        return npc.Name + markers;
                    })
                    .ToList();

                if (npcNames.Count > 0)
                {
                    message.AppendLine("👥 NPCs: " + string.Join(", ", npcNames));
                }
            }
        }

        return new ActionResult { Success = true, Message = message.ToString() };
    }

    /// <summary>
    /// Check if an NPC has any lootable items or currency.
    /// </summary>
    private bool HasLoot(Character npc)
    {
        return npc.CarriedItems.Count > 0 || npc.Wallet.TotalBaseUnits > 0;
    }

    private ActionResult HandleStopCombat()
    {
        if (!_gameState.InCombatMode || string.IsNullOrEmpty(_gameState.CurrentCombatNpcId) || !_gameState.NPCs.ContainsKey(_gameState.CurrentCombatNpcId))
        {
            return new ActionResult { Success = false, Message = "You're not in combat." };
        }

        var npc = _gameState.NPCs[_gameState.CurrentCombatNpcId];

        // Attempt to flee using agility skill check
        var fleeResult = _combatService.AttemptFlee(_gameState.Player, npc);

        var message = new StringBuilder();
        message.AppendLine(fleeResult.Message);

        if (fleeResult.Succeeded)
        {
            // Successfully fled
            _gameState.InCombatMode = false;
            _gameState.CurrentCombatNpcId = null;
            return new ActionResult { Success = true, Message = message.ToString() };
        }
        else
        {
            // Failed to flee - NPC counter-attacks!
            message.AppendLine();
            message.AppendLine($"{npc.Name} seizes the opportunity to attack!");
            var counterAttack = _combatService.ResolveAttack(npc, null, _gameState.Player, _gameState.PlayerInventory.Items);
            message.AppendLine(counterAttack.Message);

            if (counterAttack.WasHit)
            {
                _combatService.ApplyDamage(_gameState.Player, counterAttack.DamageAfterArmor);

                if (!_gameState.Player.IsAlive)
                {
                    message.AppendLine("\n💀 YOU HAVE BEEN DEFEATED! Game Over.");
                    _gameState.InCombatMode = false;
                    _gameState.CurrentCombatNpcId = null;
                    return new ActionResult { Success = true, Message = message.ToString() };
                }
            }

            message.AppendLine();
            message.AppendLine(GetCombatStatus());
            return new ActionResult { Success = true, Message = message.ToString() };
        }
    }

    private static string GenerateDefaultGMPrompt()
    {
        return @"You are the Game Master for a fantasy RPG. Your role is to narrate game events vividly.
Describe outcomes in 1-2 sentences with atmosphere and drama.
Stay true to the player's action and game logic.
Use vivid, engaging language.";
    }

    private static string GenerateNpcPersonality(Character npc)
    {
        var itemsList = npc.CarriedItems.Count > 0
            ? string.Join(", ", npc.CarriedItems.Values.Select(ii => ii.Item.Name))
            : "nothing";

        var canFollow = npc.CanJoinParty ? "YES - You can agree to follow the player" : "NO - You cannot follow anyone";

        return $@"You are {npc.Name}, an NPC in a fantasy RPG world.
Health: {npc.Health}/{npc.MaxHealth}, Level: {npc.Level}
You are carrying: {itemsList}

WHAT YOU CAN DO:
1. TALK: Respond naturally to what the player says
2. GIVE ITEMS: You can give items to the player IF you have them
3. FOLLOW: {canFollow}

IMPORTANT CONSTRAINTS:
- You can ONLY give or trade items that you are actually carrying: {itemsList}
- If asked for something you don't have, clearly state you don't have it
- If asked for items you do have, you can offer to give them
- When you agree to give items, respond with: GIVE_ITEMS: [item1, item2, ...]
- Do NOT pretend to have items you don't actually have listed above
- If you cannot follow, politely decline if asked

Keep responses brief (1-3 sentences) and stay in character.
Be helpful and realistic about your inventory constraints and abilities.
Respond naturally to player interactions.";
    }

    private static ActionPlan ParseActionJson(string jsonString)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(jsonString);
            var root = json.RootElement;

            return new ActionPlan
            {
                Action = root.GetProperty("action").GetString() ?? "help",
                Target = root.GetProperty("target").GetString() ?? "",
                Details = root.GetProperty("details").GetString() ?? ""
            };
        }
        catch
        {
            return new ActionPlan { Action = "help", Target = "", Details = "" };
        }
    }

    /// <summary>
    /// Convert raw player command into a natural message for the NPC.
    /// Example: "Ask Chen to follow" → "The player asks you to follow"
    /// This prevents the NPC from interpreting player commands as instructions.
    /// </summary>
    private async Task<string> ConvertPlayerCommandToNpcMessageAsync(string playerCommand, string npcName)
    {
        var prompt = $@"Convert this player command into a natural message from the player to the NPC named {npcName}.
The message should be in third person and frame it as the player saying/asking something.

Examples:
- ""ask Chen for stims"" → ""The player asks you for stims""
- ""ask Chen to follow"" → ""The player asks you to follow""
- ""tell Chen I'm ready"" → ""The player tells you they're ready""
- ""Chen, help me"" → ""The player asks for your help""

Player command: ""{playerCommand}""

Return ONLY the converted message - no other text, no markdown, just the message itself.";

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are a message converter. Convert player commands to natural messages for NPCs." },
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _llmClient.ChatAsync(messages);
            var convertedMessage = response.Trim();

            // Ensure it's not empty
            if (string.IsNullOrWhiteSpace(convertedMessage))
                return $"The player says: \"{playerCommand}\"";

            DebugLog($"[DEBUG] Converted command '{playerCommand}' to message: '{convertedMessage}'");
            return convertedMessage;
        }
        catch (Exception ex)
        {
            DebugLog($"[DEBUG] Error converting player command to NPC message: {ex.Message}");
            // Fallback: simple formatting
            return $"The player says: \"{playerCommand}\"";
        }
    }

    /// <summary>
    /// Check if any win condition is satisfied and return victory info if so.
    /// Returns null if no win condition is met.
    /// </summary>
    /// <summary>
    /// Execute pre-built ActionPlan objects, skipping the LLM decision step.
    /// Runs steps 2+3 of the pipeline (execute + narrate) and returns structured feedback.
    /// Used by AgentReplay to bypass the LLM parsing step since the agent builds plans directly.
    /// </summary>
    public async Task<AgentActionResponse> ExecuteActionPlansAsync(
        List<ActionPlan> plans,
        string playerIntentDescription = "Agent action")
    {
        var response = new AgentActionResponse();

        _gameState.AddRecentCommand(playerIntentDescription);

        // Execute each plan and collect results
        var beforeAction = CaptureActionSnapshot();
        var executionResults = new List<(string action, ActionResult result)>();
        foreach (var plan in plans)
        {
            var inventoryBeforeAction = CaptureInventoryQuantities();
            var result = await ApplyActionAsync(plan, playerIntentDescription);
            AddInventoryGainEvents(plan, result, inventoryBeforeAction);
            executionResults.Add((plan.Action, result));
            response.ActionResults.Add((plan.Action, result.Success, result.Message));
        }

        // For informational commands, return result directly without narration
        var informationalCommands = new[] { "status", "inventory", "look", "equipped", "quests", "projects", "recipes", "shop", "help", "display" };
        if (plans.Count == 1 && informationalCommands.Contains(plans[0].Action.ToLower()))
        {
            var result = executionResults[0].result;
            response.Response = result.Success ? result.Message : $"❌ {result.Message}";
        }
        else
        {
            var worldUpdate = AdvanceWorld(
                playerIntentDescription,
                plans,
                executionResults,
                beforeAction);
            // Narrate the results
            var narration = await NarrateWithResultsAsync(playerIntentDescription, plans, executionResults);
            narration = AppendWorldUpdates(narration, worldUpdate.Messages);
            response.Response = BuildGameResponse(narration);
        }

        // Check win condition
        var victoryCheck = CheckWinCondition();
        if (victoryCheck.HasValue && victoryCheck.Value.isVictory)
        {
            response.IsVictory = true;
            response.VictoryMessage = victoryCheck.Value.message;
            response.Response = BuildGameResponse($"🏆 **VICTORY!** 🏆\n\n{victoryCheck.Value.message}");
        }

        return response;
    }

    public (bool isVictory, string message)? CheckWinCondition()
    {
        // If no game reference, cannot check win conditions
        if (_game == null)
            return null;

        // If no win conditions defined, use legacy room-based system
        if (_game.WinConditions == null || _game.WinConditions.Count == 0)
        {
            // Legacy: check if player is in a win condition room
            if (_game.WinConditionRoomIds != null &&
                _game.WinConditionRoomIds.Contains(_gameState.CurrentRoomId))
            {
                return (true, "You have achieved victory!");
            }
            return null;
        }

        // Check each win condition
        foreach (var condition in _game.WinConditions)
        {
            bool conditionMet = condition.Type switch
            {
                "room" => _gameState.CurrentRoomId == condition.TargetId,

                "item" => !string.IsNullOrEmpty(condition.TargetId) &&
                         _gameState.PlayerInventory.Items.Values.Any(ii => ii.Item.Id == condition.TargetId),

                "npc_defeat" => !string.IsNullOrEmpty(condition.TargetId) &&
                               _gameState.NPCs.ContainsKey(condition.TargetId) &&
                               !_gameState.NPCs[condition.TargetId].IsAlive,

                "quest_complete" => !string.IsNullOrEmpty(condition.TargetId) &&
                                   _gameState.ActiveQuests.Any(q => q.Id == condition.TargetId && q.IsComplete),

                _ => false
            };

            if (conditionMet)
            {
                var message = !string.IsNullOrEmpty(condition.VictoryMessage)
                    ? condition.VictoryMessage
                    : condition.VictoryNarration ?? "You have achieved victory!";

                return (true, message);
            }
        }

        return null;
    }
}

public class ActionPlan
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;
}

public class ActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<WorldEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents an NPC's decision about whether to follow the player.
/// </summary>
public class FollowDecision
{
    [JsonPropertyName("willFollow")]
    public bool WillFollow { get; set; } = false;

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;
}

/// <summary>
/// Represents an NPC's decision about what items they're willing to give.
/// Decouples item transfer from dialogue/narrative.
/// </summary>
public class NpcGiveDecision
{
    [JsonPropertyName("willGive")]
    public bool WillGive { get; set; } = false;

    [JsonPropertyName("itemsToGive")]
    public List<string> ItemsToGive { get; set; } = new();

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("narrative")]
    public string Narrative { get; set; } = string.Empty;
}

/// <summary>
/// Structured response from ExecuteActionPlansAsync, providing per-action feedback
/// and victory detection for the AI agent.
/// </summary>
public class AgentActionResponse
{
    public string Response { get; set; } = "";
    public bool IsVictory { get; set; }
    public string? VictoryMessage { get; set; }
    public List<(string action, bool success, string message)> ActionResults { get; set; } = new();
}
