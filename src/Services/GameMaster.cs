using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Services;

/// <summary>
/// The Game Master (GM) service orchestrates the game world, player actions, and LLM narration.
/// It interprets free-text player commands, applies game logic, and narrates outcomes via LLM.
/// </summary>
public class GameMaster
{
    private readonly GameState _gameState;
    private readonly OllamaClient _ollamaClient;
    private readonly Dictionary<string, NpcBrain> _npcBrains;
    private readonly string _gmSystemPrompt;
    private readonly CombatService _combatService;

    public GameMaster(GameState gameState, OllamaClient ollamaClient, string? gmSystemPrompt = null)
    {
        _gameState = gameState;
        _ollamaClient = ollamaClient;
        _npcBrains = new();
        _gmSystemPrompt = gmSystemPrompt ?? GenerateDefaultGMPrompt();
        _combatService = new CombatService();

        // Initialize NPC brains with custom personalities
        InitializeNpcBrains();
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

        // Step 1: Ask LLM what commands it wants to execute based on player intent and game state
        var commandsToExecute = await DecideActionsAsync(playerCommand);

        // If no actions were decided, return error
        if (commandsToExecute.Count == 0)
        {
            return BuildGameResponse("I don't understand what you're trying to do. Try describing your action more clearly.");
        }

        // Step 2: Execute those commands and collect results
        var executionResults = new List<(string action, ActionResult result)>();
        foreach (var action in commandsToExecute)
        {
            var result = await ApplyActionAsync(action, playerCommand);
            executionResults.Add((action.Action, result));
        }

        // Step 3: Ask LLM to narrate based on original command + execution results
        var narration = await NarrateWithResultsAsync(playerCommand, commandsToExecute, executionResults);

        // Build the complete response with game state
        var response = BuildGameResponse(narration);

        return response;
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

        // Build NPC list distinguishing alive and dead
        var aliveNpcs = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id) && _gameState.NPCs[id].IsAlive)
            .Select(id => _gameState.NPCs[id].Name)
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

        var inventory = _gameState.PlayerInventory.Items.Count > 0
            ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => $"{ii.Item.Name}"))
            : "empty";

        var response = new StringBuilder();
        response.AppendLine(narration);
        response.AppendLine();
        response.AppendLine("---");
        response.AppendLine();
        response.AppendLine($"📍 **Location:** {currentRoom.Name}");
        response.AppendLine($"❤️ **Health:** {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");

        if (exits.Count > 0)
            response.AppendLine($"🚪 **Exits:** {string.Join(", ", exits.Select(e => e.DisplayName))}");

        if (npcsList.Length > 0)
            response.AppendLine($"👥 **NPCs Here:** {npcsList}");

        response.AppendLine($"🎒 **Inventory:** {inventory}");

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

        return await _ollamaClient.ChatAsync(messages);
    }

    /// <summary>
    /// Step 1: Ask the LLM what actions to execute based on player command and game state.
    /// The LLM intelligently decides what sequence of commands would best fulfill the player's intent.
    /// </summary>
    private async Task<List<ActionPlan>> DecideActionsAsync(string playerCommand)
    {
        var currentRoom = _gameState.GetCurrentRoom();

        // Build context about available actions
        var availableExits = currentRoom.GetAvailableExits();
        var npcList = string.Join(", ", currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => $"{_gameState.NPCs[id].Name} ({(_gameState.NPCs[id].IsAlive ? "alive" : "dead")})"));
        var itemList = string.Join(", ", _gameState.PlayerInventory.Items
            .Select(kvp => $"{kvp.Value.Item.Name}"));

        var exitsList = availableExits.Count > 0
            ? string.Join(", ", availableExits.Select(e => e.DisplayName))
            : "None";

        var context = $@"Current Location: {currentRoom.Name}
Available exits: {exitsList}
NPCs here: {(string.IsNullOrEmpty(npcList) ? "None" : npcList)}
Your inventory: {(string.IsNullOrEmpty(itemList) ? "Empty" : itemList)}
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

Valid actions: move, look, inventory, talk, follow, examine, take, drop, use, attack, give, stop, status, help

Rules:
1. Return ONLY the JSON array - no explanation, no code blocks, no markdown
2. The 'target' field is CRITICAL - it must contain the exact thing the player wants to interact with
3. Match the player's intent EXACTLY - DO NOT add extra actions the player didn't ask for
4. Each action must directly correspond to something the player explicitly stated
5. Only return multiple commands if the player explicitly asked for multiple actions
6. If the command is unclear, return an empty array: []

Examples of CORRECT responses:
Player says 'attack chen' -> [{""action"":""attack"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'ask chen for stims' -> [{""action"":""give"",""target"":""Dr. Sarah Chen"",""details"":""stims""}]
Player says 'ask chen to follow' -> [{""action"":""follow"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'search her body' -> [{""action"":""examine"",""target"":""Dr. Sarah Chen"",""details"":""""}]
Player says 'take the loot' -> [{""action"":""take"",""target"":""loot"",""details"":""""}]
Player says 'go out' -> [{""action"":""move"",""target"":""Out Into Corridor"",""details"":""""}]
Player says 'look around' -> [{""action"":""look"",""target"":"""",""details"":""""}]

[]"
            },
            new() { Role = "user", Content = $"{context}\n\nPlayer command: {playerCommand}" }
        };

        try
        {
            var response = await _ollamaClient.ChatAsync(messages);

            // DEBUG: Log the raw LLM response
            Console.WriteLine("[DEBUG] LLM Response:");
            Console.WriteLine(response);
            Console.WriteLine("[DEBUG] ---");

            var actionPlans = ParseActionJsonArray(response);

            Console.WriteLine($"[DEBUG] Parsed {actionPlans.Count} actions from LLM response");
            foreach (var plan in actionPlans)
            {
                Console.WriteLine($"[DEBUG]   Action: {plan.Action}, Target: {plan.Target}");
            }

            // Always fallback to fallback parser if LLM returns empty or fails
            if (actionPlans.Count == 0)
            {
                Console.WriteLine("[DEBUG] LLM returned no actions, trying fallback parser");
                var fallbackPlan = TryParseFallback(playerCommand, currentRoom);
                if (fallbackPlan != null)
                {
                    Console.WriteLine($"[DEBUG] Fallback parser returned: {fallbackPlan.Action} -> {fallbackPlan.Target}");
                    actionPlans.Add(fallbackPlan);
                }
                else
                {
                    Console.WriteLine("[DEBUG] Fallback parser returned null");
                }
            }

            return actionPlans;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception in DecideActionsAsync: {ex.Message}");
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

            Console.WriteLine($"[DEBUG] ParseActionJsonArray: startIdx={startIdx}, endIdx={endIdx}");

            if (startIdx >= 0 && endIdx > startIdx)
            {
                var jsonStr = jsonResponse.Substring(startIdx, endIdx - startIdx + 1);
                Console.WriteLine($"[DEBUG] Extracted JSON: {jsonStr}");
                var actions = System.Text.Json.JsonSerializer.Deserialize<List<ActionPlan>>(jsonStr);
                if (actions != null)
                {
                    Console.WriteLine($"[DEBUG] Successfully deserialized {actions.Count} actions");
                    result.AddRange(actions);
                }
                else
                {
                    Console.WriteLine("[DEBUG] Deserialization returned null");
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] Could not find JSON array brackets in response");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception parsing JSON: {ex.Message}");
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
            else
            {
                // All other actions (examine, attack, move, use, etc) get narrated
                narratedActions.Add((action, result));
            }
        }

        Console.WriteLine("[DEBUG] NarrateWithResultsAsync:");
        Console.WriteLine($"[DEBUG]   Original command: {playerCommand}");
        Console.WriteLine($"[DEBUG]   Current location: {currentRoom.Name}");
        Console.WriteLine($"[DEBUG]   Player health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
        Console.WriteLine($"[DEBUG]   Dialogue lines: {dialogueLines.Count}");
        Console.WriteLine($"[DEBUG]   Narrated actions: {narratedActions.Count}");

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
                    Content = @"You are a fantasy RPG narrator. Based on the player's original intent and the actual game results, create an engaging narrative description of what happened. Be creative but accurate to the actual results. If an action failed, describe why. If multiple actions were performed, weave them together into a cohesive narrative."
                },
                new() { Role = "user", Content = context }
            };

            var narrative = await _ollamaClient.ChatAsync(messages);
            output.AppendLine(narrative);
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
        status.AppendLine("Commands: attack|fight|flee|status|stop");

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
            "inventory - Check your items",
            "examine [item/npc] - Look closely at something",
            "go [direction] - Travel in a direction (natural language works!)",
            "talk [npc] - Speak with an NPC",
            "give [npc] [items] - Ask an NPC to give you items",
            "use [item] - Use an item (potion, scroll, etc)",
            "take [item] - Pick up an item",
            "drop [item] - Drop an item from inventory",
            "attack [npc] - Attack an NPC (enters COMBAT MODE)",
            "status - Show current game status",
            "",
        };

        if (_gameState.InCombatMode)
        {
            actions.Insert(actions.Count - 1, "=== COMBAT COMMANDS ===");
            actions.Insert(actions.Count - 1, "attack/fight - Attack the enemy again");
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

        actions.Add("");
        actions.Add("💡 Tip: You can use natural language! Try: 'go to the tavern', 'examine the sword', 'drink the potion', etc.");

        return string.Join("\n", actions);
    }

    private void InitializeNpcBrains()
    {
        foreach (var npc in _gameState.NPCs.Values)
        {
            var systemPrompt = npc.PersonalityPrompt ?? GenerateNpcPersonality(npc);
            _npcBrains[npc.Id] = new NpcBrain(_ollamaClient, npc, systemPrompt);
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
            var response = await _ollamaClient.ChatAsync(messages);
            Console.WriteLine($"[DEBUG] NPC Give Decision Response:\n{response}\n[DEBUG] ---");

            return ParseGiveDecisionJson(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error getting NPC give decision: {ex.Message}");
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
            Console.WriteLine($"[DEBUG] Error parsing give decision JSON: {ex.Message}");
        }

        return new NpcGiveDecision { WillGive = false, Reason = "Unable to understand" };
    }

    /// <summary>
    /// Fallback parser for simple commands that the LLM might miss
    /// </summary>
    private ActionPlan? TryParseFallback(string playerCommand, Room currentRoom)
    {
        var lower = playerCommand.ToLower().Trim();
        Console.WriteLine($"[DEBUG] TryParseFallback: '{playerCommand}' -> '{lower}'");

        // Check for direction keywords
        var directions = new[] { "north", "south", "east", "west", "up", "down", "left", "right", "forward" };
        foreach (var dir in directions)
        {
            if (lower == dir || lower.Contains($"go {dir}") || lower.Contains($"travel {dir}") || lower.Contains($"{dir}"))
            {
                // Check if this direction exists in current room
                var exit = currentRoom.FindExit(dir);
                if (exit != null)
                {
                    return new ActionPlan { Action = "move", Target = dir, Details = "" };
                }
            }
        }

        // Check for direction names from available exits (fuzzy keyword matching)
        var exits = currentRoom.GetAvailableExits();
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
  ""action"": ""move|look|inventory|talk|examine|take|drop|use|attack|help|unknown"",
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
            var response = await _ollamaClient.ChatAsync(messages);
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

        return plan.Action.ToLower() switch
        {
            "move" => HandleMove(plan.Target),
            "look" => HandleLook(),
            "inventory" => HandleInventory(),
            "talk" => await HandleTalkAsync(plan.Target, string.IsNullOrEmpty(plan.Details) ? playerCommand : plan.Details),
            "follow" => await HandleFollowAsync(plan.Target),
            "examine" => HandleExamine(plan.Target, playerCommand),
            "attack" => HandleAttack(plan.Target),
            "take" => HandleTake(plan.Target),
            "drop" => HandleDrop(plan.Target),
            "use" => HandleUse(plan.Target),
            "give" => await HandleGiveAsync(plan.Target, string.IsNullOrEmpty(plan.Details) ? playerCommand : plan.Details),
            "help" => HandleHelp(),
            "status" => HandleStatus(),
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

        return await _ollamaClient.ChatAsync(messages);
    }

    private ActionResult HandleMove(string exitName)
    {
        var currentRoom = _gameState.GetCurrentRoom();
        var availableExits = currentRoom.GetAvailableExits();

        Console.WriteLine($"[DEBUG] HandleMove: exitName='{exitName}', current room='{currentRoom.Name}'");
        Console.WriteLine($"[DEBUG] Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}");

        // Try to find the exit
        var exit = currentRoom.FindExit(exitName);
        if (exit == null)
        {
            Console.WriteLine($"[DEBUG] HandleMove: exit '{exitName}' not found");
            return new ActionResult
            {
                Success = false,
                Message = $"You can't go \"{exitName}\". Available exits: {string.Join(", ", availableExits.Select(e => e.DisplayName))}"
            };
        }

        Console.WriteLine($"[DEBUG] HandleMove: found exit, destination room='{exit.DestinationRoomId}'");

        if (_gameState.MoveToRoomByExit(exitName))
        {
            var newRoom = _gameState.GetCurrentRoom();
            Console.WriteLine($"[DEBUG] HandleMove: successfully moved to '{newRoom.Name}'");

            // Move all companions with the player
            _gameState.MoveCompanionsToCurrentRoom();

            return new ActionResult
            {
                Success = true,
                Message = $"You go {exitName}. You arrive at {newRoom.Name}."
            };
        }

        Console.WriteLine($"[DEBUG] HandleMove: MoveToRoomByExit returned false");
        return new ActionResult { Success = false, Message = "Cannot move there." };
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

        // Add NPCs
        if (room.NPCIds.Count > 0)
        {
            var npcNames = room.NPCIds
                .Where(id => _gameState.NPCs.ContainsKey(id))
                .Select(id => _gameState.NPCs[id].Name)
                .ToList();

            if (npcNames.Count > 0)
            {
                message += "You see: " + string.Join(", ", npcNames);
            }
        }

        return new ActionResult { Success = true, Message = message };
    }

    private ActionResult HandleInventory()
    {
        if (_gameState.PlayerInventory.Items.Count == 0)
            return new ActionResult { Success = true, Message = "Your inventory is empty." };

        var items = string.Join(", ",
            _gameState.PlayerInventory.Items.Values.Select(i => $"{i.Item.Name} x{i.Quantity}"));
        return new ActionResult { Success = true, Message = $"Inventory: {items}" };
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

        // Get the NPC's response via their brain
        string npcResponse = "";
        if (_npcBrains.ContainsKey(npc.Id))
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

        // Return the NPC's actual response as the message
        var message = $"{npc.Name} says: \"{npcResponse}\"";
        return new ActionResult { Success = true, Message = message };
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

        // Ask NPC if they will join via LLM
        var prompt = $"The player asks you to follow them and join their party. Will you accept and follow them?";
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = $"You are {npc.Name}. Decide whether you want to follow the player and join their party." },
            new() { Role = "user", Content = prompt }
        };

        try
        {
            var response = await _ollamaClient.ChatAsync(messages);
            var lowerResponse = response.ToLower();

            // Simple yes/no detection
            var willFollow = lowerResponse.Contains("yes") || lowerResponse.Contains("accept") ||
                           lowerResponse.Contains("agree") || lowerResponse.Contains("glad") ||
                           lowerResponse.Contains("definitely") || lowerResponse.Contains("sure") ||
                           lowerResponse.Contains("absolutely") || !lowerResponse.Contains("no");

            if (willFollow)
            {
                _gameState.AddCompanion(npc.Id);
                Console.WriteLine($"[DEBUG] {npc.Name} joined the party");
                return new ActionResult { Success = true, Message = $"{npc.Name} says: \"{response}\"\n\n{npc.Name} joins your party and will follow you." };
            }
            else
            {
                return new ActionResult { Success = true, Message = $"{npc.Name} says: \"{response}\"" };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error asking NPC to follow: {ex.Message}");
            return new ActionResult { Success = false, Message = $"{npc.Name} seems confused." };
        }
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

        // Resolve combat using CombatService
        var combatResult = _combatService.ResolveAttack(_gameState.Player, npc);

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
            message.AppendLine($"The {npc.Name}'s body remains here for you to search or examine.");

            // Exit combat mode
            _gameState.InCombatMode = false;
            _gameState.CurrentCombatNpcId = null;

            return new ActionResult { Success = true, Message = message.ToString() };
        }

        // NPC counter-attacks!
        message.AppendLine($"\n{npc.Name} retaliates!");
        var counterAttack = _combatService.ResolveAttack(npc, _gameState.Player);
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

            // Show additional info if the NPC is dead
            if (!npcObj.IsAlive)
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
                    _gameState.PlayerInventory.AddItem(lootItem.Item, lootItem.Quantity);
                    npc.CarriedItems.Remove(lootItem.Item.Id);
                    itemsTaken.Add(lootItem.Item.Name);
                }

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

        // Check if player already has this item
        var playerItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (playerItem != null)
            return new ActionResult { Success = false, Message = $"You already have {playerItem.Item.Name}." };

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
                    _gameState.PlayerInventory.AddItem(lootItem.Item, lootItem.Quantity);
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

        _gameState.PlayerInventory.RemoveItem(item.Item.Id);
        return new ActionResult { Success = true, Message = $"You dropped {item.Item.Name}." };
    }

    private ActionResult HandleUse(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return new ActionResult { Success = false, Message = "Use what?" };

        var itemLower = itemName.ToLower();
        var inventoryItem = _gameState.PlayerInventory.Items.Values
            .FirstOrDefault(ii => ii.Item.Name.ToLower().Contains(itemLower));

        if (inventoryItem == null)
            return new ActionResult { Success = false, Message = $"You don't have '{itemName}'." };

        var item = inventoryItem.Item;

        // Handle teleportation items
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

        // Handle consumables
        if (item.IsConsumable && item.ConsumableUsesRemaining > 0)
        {
            var healAmount = item.ConsumableEffects?.ContainsKey("heal") == true ? item.ConsumableEffects["heal"] : 0;
            if (healAmount > 0)
            {
                _gameState.Player.Heal(healAmount);
            }
            item.ConsumableUsesRemaining--;
            if (item.ConsumableUsesRemaining == 0)
            {
                _gameState.PlayerInventory.RemoveItem(item.Id);
            }
            return new ActionResult { Success = true, Message = $"You use {item.Name}." };
        }

        return new ActionResult { Success = false, Message = $"You can't use {item.Name} like that." };
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

    private ActionResult HandleStatus()
    {
        if (_gameState.InCombatMode)
        {
            return new ActionResult { Success = true, Message = GetCombatStatus() };
        }
        else
        {
            var currentRoom = _gameState.GetCurrentRoom();
            var message = new StringBuilder();
            message.AppendLine($"Location: {currentRoom.Name}");
            message.AppendLine($"Health: {_gameState.Player.Health}/{_gameState.Player.MaxHealth}");
            message.AppendLine($"Level: {_gameState.Player.Level}");
            message.AppendLine($"Experience: {_gameState.Player.Experience}");
            return new ActionResult { Success = true, Message = message.ToString() };
        }
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
            var counterAttack = _combatService.ResolveAttack(npc, _gameState.Player);
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
            var response = await _ollamaClient.ChatAsync(messages);
            var convertedMessage = response.Trim();

            // Ensure it's not empty
            if (string.IsNullOrWhiteSpace(convertedMessage))
                return $"The player says: \"{playerCommand}\"";

            Console.WriteLine($"[DEBUG] Converted command '{playerCommand}' to message: '{convertedMessage}'");
            return convertedMessage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error converting player command to NPC message: {ex.Message}");
            // Fallback: simple formatting
            return $"The player says: \"{playerCommand}\"";
        }
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
