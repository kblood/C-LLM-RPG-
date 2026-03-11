using System.Text;
using CSharpRPGBackend.Core;
using CSharpRPGBackend.LLM;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Intelligent AI agent that plays RPG games using structured memory, ReAct reasoning,
/// anti-loop detection, and self-reflection. Covers all game systems: combat, looting,
/// equipping, shopping, crafting, gathering, quests, and exploration.
///
/// Key design decisions for reliability with small models (3B):
/// - Deterministic actions for obvious situations (enemy present → attack, loot → take, gear → equip)
/// - LLM only consulted for exploration/strategy decisions
/// - Room oscillation detection prevents back-and-forth loops
/// - Console output so you can see what's happening in real time
/// </summary>
public class AgentReplay
{
    private readonly GameMaster _gameMaster;
    private readonly GameState _gameState;
    private readonly ILlmClient _llmClient;
    private readonly Game _game;

    // Memory systems
    private readonly SpatialMemory _spatial = new();
    private readonly ActionHistory _history = new();
    private readonly Scratchpad _scratchpad = new();
    private readonly GoalStack _goals = new();
    private readonly HashSet<string> _talkedToNpcs = new();

    // Logging
    private readonly StringBuilder _log = new();

    public AgentReplay(GameState gameState, GameMaster gameMaster, ILlmClient llmClient, Game game)
    {
        _gameState = gameState;
        _gameMaster = gameMaster;
        _llmClient = llmClient;
        _game = game;
    }

    public async Task<string> PlayGameAsync(int maxTurns = 50)
    {
        InitializeLog();

        _goals.InitializeFromGame(_game, _gameState);
        var startRoom = _gameState.GetCurrentRoom();
        _spatial.UpdateFromRoom(startRoom, _gameState.CurrentRoomId, 0);
        _spatial.UpdateRoomFeatures(startRoom, _gameState.CurrentRoomId, _gameState.NPCs);

        _log.AppendLine("\n## Game Start\n");
        _log.AppendLine($"> **Narrator:** {_game.StoryIntroduction}\n");
        _log.AppendLine($"> **Objective:** {_game.GameObjective}\n");

        Console.WriteLine($"\n  Objective: {_game.GameObjective}");

        for (int turn = 1; turn <= maxTurns; turn++)
        {
            _log.AppendLine($"\n### Turn {turn}\n");

            var room = _gameState.GetCurrentRoom();
            var roomId = _gameState.CurrentRoomId;

            LogTurnHeader(room, turn);

            // ── Console status line ──
            var hpPct = _gameState.Player.MaxHealth > 0 ? 100 * _gameState.Player.Health / _gameState.Player.MaxHealth : 100;
            Console.Write($"  T{turn,2} | {room.Name,-25} | HP:{_gameState.Player.Health}/{_gameState.Player.MaxHealth} ({hpPct}%)");

            // ── OBSERVE + REMEMBER ──
            _spatial.UpdateFromRoom(room, roomId, turn);
            _spatial.UpdateRoomFeatures(room, roomId, _gameState.NPCs);
            _goals.Reprioritize(_gameState, room);

            // ── AUTO-ACTIONS (deterministic, no LLM) ──
            var victoryFromAuto = await ExecuteAutoActions(turn);
            if (victoryFromAuto) { Console.WriteLine(" → VICTORY!"); break; }
            if (!_gameState.Player.IsAlive) { Console.WriteLine(" → DIED!"); LogDeath(); break; }

            // Re-read room after auto-actions
            room = _gameState.GetCurrentRoom();
            roomId = _gameState.CurrentRoomId;

            // ── DETERMINISTIC PRIORITY ACTIONS (skip LLM for obvious choices) ──
            ActionPlan? plan = GetDeterministicAction(room);
            string thought;

            if (plan != null)
            {
                // Obvious action — no LLM needed
                thought = "Deterministic: " + plan.Action + " " + plan.Target;
            }
            else
            {
                // ── DETECT loop state ──
                var warnings = AntiLoopDetector.GetWarnings(_history, roomId);

                // ── Check for oscillation override BEFORE calling LLM ──
                if (AntiLoopDetector.IsOscillating(_history))
                {
                    plan = AntiLoopDetector.GetOverrideAction(_spatial, _gameState);
                    thought = "Loop override: oscillation detected, forcing new direction.";
                }
                else
                {
                    // ── REASON (LLM call) ──
                    var validActions = ValidActionFilter.GetValidActions(_gameState, _spatial, _game, _talkedToNpcs);
                    var (systemPrompt, userPrompt) = ReActPromptBuilder.BuildPrompt(
                        _gameState, _game, _spatial, _history, _scratchpad, _goals,
                        validActions, warnings, _talkedToNpcs);

                    try
                    {
                        var messages = new List<ChatMessage>
                        {
                            new() { Role = "system", Content = systemPrompt },
                            new() { Role = "user", Content = userPrompt }
                        };
                        var response = await _llmClient.ChatAsync(messages);
                        (thought, var actionStr) = ParseReActResponse(response);

                        // Validate against anti-loop
                        var (parsedAction, parsedTarget) = SplitActionTarget(actionStr);
                        var severity = AntiLoopDetector.Assess(_history, parsedAction, parsedTarget, roomId);

                        if (severity >= LoopSeverity.Preventive)
                        {
                            // One retry with nudge
                            messages.Add(new ChatMessage { Role = "assistant", Content = actionStr });
                            messages.Add(new ChatMessage { Role = "user", Content = "That action was tried recently. Pick a DIFFERENT action." });
                            response = await _llmClient.ChatAsync(messages);
                            (thought, actionStr) = ParseReActResponse(response);
                            (parsedAction, parsedTarget) = SplitActionTarget(actionStr);
                            severity = AntiLoopDetector.Assess(_history, parsedAction, parsedTarget, roomId);
                        }

                        if (severity >= LoopSeverity.HardOverride)
                        {
                            plan = AntiLoopDetector.GetOverrideAction(_spatial, _gameState);
                            thought = "Loop override after LLM retry.";
                        }
                        else
                        {
                            plan = ParseActionToActionPlan(actionStr, room);
                        }
                    }
                    catch
                    {
                        plan = GetFallbackAction(room);
                        thought = "LLM failed, using fallback.";
                    }
                }
            }

            plan ??= GetFallbackAction(room);

            // ── Console action output ──
            Console.WriteLine($" → {plan.Action} {plan.Target}");

            // ── ACT ──
            _log.AppendLine($"> **Thought:** {thought}\n");
            _log.AppendLine($"> **Action:** {plan.Action} {plan.Target} {plan.Details}\n");

            var intentDescription = thought.Length > 150 ? thought[..150] : thought;

            var result = await _gameMaster.ExecuteActionPlansAsync(
                new List<ActionPlan> { plan }, intentDescription);

            var narrativeLog = result.Response.Length > 500
                ? result.Response[..500] + "..." : result.Response;
            _log.AppendLine($"> **Result:** {narrativeLog}\n");

            // ── UPDATE ──
            var (actionName, targetName) = SplitActionTarget($"{plan.Action} {plan.Target}");
            bool actionSucceeded = result.ActionResults.Count > 0 && result.ActionResults[0].success;
            string actionSummary = result.ActionResults.Count > 0
                ? result.ActionResults[0].message : result.Response;
            if (actionSummary.Length > 100) actionSummary = actionSummary[..100];

            _history.Add(turn, roomId, actionName, targetName, actionSucceeded, actionSummary);

            // Track talked NPCs
            if (plan.Action.Equals("talk", StringComparison.OrdinalIgnoreCase))
            {
                var npc = room.NPCIds
                    .Where(id => _gameState.NPCs.ContainsKey(id))
                    .Select(id => _gameState.NPCs[id])
                    .FirstOrDefault(n => n.Name.Contains(plan.Target, StringComparison.OrdinalIgnoreCase)
                                        || plan.Target.Contains(n.Name, StringComparison.OrdinalIgnoreCase));
                if (npc != null) _talkedToNpcs.Add(npc.Id);
            }

            // Update spatial and goals
            var postRoom = _gameState.GetCurrentRoom();
            _spatial.UpdateFromRoom(postRoom, _gameState.CurrentRoomId, turn);
            _spatial.UpdateRoomFeatures(postRoom, _gameState.CurrentRoomId, _gameState.NPCs);
            _goals.UpdateProgress(_gameState, turn);

            // ── REFLECT (rarely, to save LLM calls) ──
            if (ReflectionEngine.ShouldReflect(_history, _goals, _gameState, turn))
            {
                Console.WriteLine($"         ... reflecting");
                await RunReflection(turn);
            }

            // ── END CHECKS ──
            if (result.IsVictory)
            {
                Console.WriteLine($"\n  🏆 VICTORY! {result.VictoryMessage}\n");
                _log.AppendLine("\n## 🏆 Victory!\n");
                _log.AppendLine($"{result.VictoryMessage}\n");
                break;
            }

            if (!_gameState.Player.IsAlive)
            {
                Console.WriteLine($"\n  💀 GAME OVER\n");
                LogDeath();
                break;
            }

            var winCheck = _gameMaster.CheckWinCondition();
            if (winCheck.HasValue && winCheck.Value.isVictory)
            {
                Console.WriteLine($"\n  🏆 VICTORY! {winCheck.Value.message}\n");
                _log.AppendLine("\n## 🏆 Victory!\n");
                _log.AppendLine($"{winCheck.Value.message}\n");
                break;
            }
        }

        _log.AppendLine("\n---\n");
        _log.AppendLine($"*Agent replay generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*\n");
        _log.AppendLine($"*Rooms explored: {_spatial.KnownRooms.Count}, Frontier remaining: {_spatial.Frontier.Count}*\n");

        Console.WriteLine($"\n  Summary: Explored {_spatial.KnownRooms.Count} rooms, {_spatial.Frontier.Count} unexplored exits remaining");

        return _log.ToString();
    }

    public async Task SaveLogAsync(string filePath)
    {
        try
        {
            await File.WriteAllTextAsync(filePath, _log.ToString(), new System.Text.UTF8Encoding(false));
            Console.WriteLine($"  ✓ Agent replay saved to: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error saving agent replay: {ex.Message}");
        }
    }

    // ─── Deterministic Priority Actions ──────────────────────────────────────

    /// <summary>
    /// Returns an action plan for situations where LLM consultation is unnecessary:
    /// - In combat → auto attack
    /// - Enemy in room → attack
    /// - Dead NPC with loot → take (handled by auto-actions, but just in case)
    /// - Untouched friendly NPC → talk
    /// Returns null when the LLM should decide (exploration choices).
    /// </summary>
    private ActionPlan? GetDeterministicAction(Room room)
    {
        // In active combat: always auto-attack
        if (_gameState.InCombatMode && _gameState.CurrentCombatNpcId != null)
            return new ActionPlan { Action = "auto", Target = "" };

        // Enemy in room: always attack (this is the key fix for the oscillation bug)
        var enemy = ValidActionFilter.GetEnemyInRoom(_gameState);
        if (enemy != null)
            return new ActionPlan { Action = "attack", Target = enemy.Name };

        // If we've been stuck in the same room doing the same thing, bail out to LLM/anti-loop
        int sameRoomTurns = _history.ConsecutiveSameRoomTurns(_gameState.CurrentRoomId);
        if (sameRoomTurns >= 2)
            return null;

        // Floor items: pick them up (but only if we haven't been failing at it)
        var floorItem = room.Items.FirstOrDefault(i => i.CanBeTaken);
        if (floorItem != null && !_history.HasTriedAndFailed("take", floorItem.Name, 3))
            return new ActionPlan { Action = "take", Target = floorItem.Name };

        // Friendly NPC we haven't talked to: talk
        var untouchedNpc = room.NPCIds
            .Where(id => _gameState.NPCs.TryGetValue(id, out var npc) && npc.IsAlive &&
                !_talkedToNpcs.Contains(id) &&
                npc.Role != NPCRole.Warrior && npc.Role != NPCRole.Boss &&
                npc.Alignment != CharacterAlignment.Evil)
            .Select(id => _gameState.NPCs[id])
            .FirstOrDefault();
        if (untouchedNpc != null)
            return new ActionPlan { Action = "talk", Target = untouchedNpc.Name, Details = "" };

        // No obvious action — let LLM decide (exploration, shopping, etc.)
        return null;
    }

    // ─── Auto-Actions ────────────────────────────────────────────────────────

    private async Task<bool> ExecuteAutoActions(int turn)
    {
        // 1. Auto-loot: dead NPCs with items
        var lootPlans = GetAutoLootActions();
        foreach (var plan in lootPlans)
        {
            var result = await _gameMaster.ExecuteActionPlansAsync(
                new List<ActionPlan> { plan }, $"Auto-loot: {plan.Action} {plan.Target}");
            var ok = result.ActionResults.FirstOrDefault().success;
            if (ok) Console.Write($" [loot:{plan.Target}]");
            _log.AppendLine($"> **Auto:** {plan.Action} {plan.Target} → {(ok ? "OK" : "fail")}\n");
            if (result.IsVictory) return true;
        }

        // 2. Auto-equip: better gear
        var equipPlans = GetAutoEquipActions();
        foreach (var plan in equipPlans)
        {
            var result = await _gameMaster.ExecuteActionPlansAsync(
                new List<ActionPlan> { plan }, $"Auto-equip: {plan.Target}");
            var ok = result.ActionResults.FirstOrDefault().success;
            if (ok) Console.Write($" [equip:{plan.Target}]");
            _log.AppendLine($"> **Auto:** equip {plan.Target} → {(ok ? "OK" : "fail")}\n");
            if (result.IsVictory) return true;
        }

        // 3. Auto-heal: in combat with health < 15%
        if (_gameState.InCombatMode && _gameState.Player.MaxHealth > 0 &&
            (double)_gameState.Player.Health / _gameState.Player.MaxHealth < 0.15)
        {
            var healPlan = GetAutoHealAction();
            if (healPlan != null)
            {
                var result = await _gameMaster.ExecuteActionPlansAsync(
                    new List<ActionPlan> { healPlan }, $"Auto-heal: use {healPlan.Target}");
                var ok = result.ActionResults.FirstOrDefault().success;
                if (ok) Console.Write($" [heal]");
                _log.AppendLine($"> **Auto:** use {healPlan.Target} → {(ok ? "OK" : "fail")}\n");
                if (result.IsVictory) return true;
            }
        }

        return false;
    }

    private List<ActionPlan> GetAutoLootActions()
    {
        var plans = new List<ActionPlan>();
        var room = _gameState.GetCurrentRoom();

        foreach (var npcId in room.NPCIds)
        {
            if (!_gameState.NPCs.TryGetValue(npcId, out var npc)) continue;
            if (npc.IsAlive || npc.CarriedItems.Count == 0) continue;

            foreach (var ci in npc.CarriedItems.Values.ToList())
            {
                plans.Add(new ActionPlan
                {
                    Action = "take",
                    Target = ci.Item.Name,
                    Details = npc.Name
                });
            }
        }

        return plans;
    }

    private List<ActionPlan> GetAutoEquipActions()
    {
        var plans = new List<ActionPlan>();

        var currentWeaponDmg = ValidActionFilter.GetEquippedWeaponDamage(_gameState);
        Item? bestWeapon = null;
        foreach (var ii in _gameState.PlayerInventory.Items.Values)
        {
            if (ii.Item.IsEquippable && ii.Item.Type == ItemType.Weapon && ii.Item.DamageBonus > currentWeaponDmg)
            {
                if (bestWeapon == null || ii.Item.DamageBonus > bestWeapon.DamageBonus)
                    bestWeapon = ii.Item;
            }
        }
        if (bestWeapon != null)
            plans.Add(new ActionPlan { Action = "equip", Target = bestWeapon.Name });

        var equippedArmorBySlot = new Dictionary<string, int>();
        foreach (var (slot, itemId) in _gameState.Player.EquipmentSlots)
        {
            if (slot == "main_hand" || slot == "off_hand") continue;
            int bonus = 0;
            if (itemId != null && _gameState.PlayerInventory.Items.TryGetValue(itemId, out var equipped))
                bonus = equipped.Item.ArmorBonus;
            equippedArmorBySlot[slot] = bonus;
        }

        foreach (var ii in _gameState.PlayerInventory.Items.Values)
        {
            if (!ii.Item.IsEquippable || ii.Item.Type != ItemType.Armor) continue;
            var slot = ii.Item.EquipmentSlot;
            if (string.IsNullOrEmpty(slot)) continue;
            if (equippedArmorBySlot.TryGetValue(slot, out var currentBonus) && ii.Item.ArmorBonus > currentBonus)
            {
                plans.Add(new ActionPlan { Action = "equip", Target = ii.Item.Name });
                equippedArmorBySlot[slot] = ii.Item.ArmorBonus;
            }
        }

        return plans;
    }

    private ActionPlan? GetAutoHealAction()
    {
        foreach (var ii in _gameState.PlayerInventory.Items.Values)
        {
            if (ii.Item.IsConsumable && ii.Item.ConsumableEffects.ContainsKey("heal"))
                return new ActionPlan { Action = "use", Target = ii.Item.Name };
        }
        return null;
    }

    // ─── ReAct Response Parsing ──────────────────────────────────────────────

    private (string thought, string action) ParseReActResponse(string response)
    {
        string thought = "";
        string action = "";

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Thought:", StringComparison.OrdinalIgnoreCase))
                thought = trimmed["Thought:".Length..].Trim();
            else if (trimmed.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
                action = trimmed["Action:".Length..].Trim();
        }

        // Small models often don't follow the format — extract the action more aggressively
        if (string.IsNullOrEmpty(action))
        {
            // Try to find a line that looks like a game command
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Skip lines that are clearly reasoning, not commands
                if (trimmed.StartsWith("Thought:", StringComparison.OrdinalIgnoreCase)) continue;
                if (trimmed.Length > 80) continue; // commands are short
                if (trimmed.Contains("because") || trimmed.Contains("should") || trimmed.Contains("need to")) continue;

                // Check if line starts with a known command verb
                var lower = trimmed.ToLowerInvariant();
                foreach (var verb in new[] { "go ", "attack ", "auto", "talk ", "take ", "look", "examine ",
                    "use ", "equip ", "buy ", "sell ", "shop", "gather ", "craft ", "flee", "inventory", "quests",
                    "status", "recipes" })
                {
                    if (lower.StartsWith(verb) || lower == verb.Trim())
                    {
                        action = trimmed;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(action)) break;
            }
        }

        // Still nothing — use last line
        if (string.IsNullOrEmpty(action) && lines.Length > 0)
            action = lines.Last().Trim();

        // Strip common preamble
        foreach (var prefix in new[] { "action:", "i will ", "i'll ", "player:", "> ", "- ", "* " })
        {
            if (action.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                action = action[prefix.Length..].Trim();
        }

        // Remove quotes
        if ((action.StartsWith("'") && action.EndsWith("'")) || (action.StartsWith("\"") && action.EndsWith("\"")))
            action = action[1..^1];

        // Strip visited/UNEXPLORED tags
        action = System.Text.RegularExpressions.Regex.Replace(action, @"\s*\((visited|UNEXPLORED|visited \d+x)\)\s*", " ").Trim();

        return (thought, action);
    }

    // ─── Action String → ActionPlan Parser ───────────────────────────────────

    private ActionPlan? ParseActionToActionPlan(string action, Room currentRoom)
    {
        if (string.IsNullOrWhiteSpace(action))
            return null;

        var lower = action.ToLowerInvariant().Trim();
        var npcsInRoom = currentRoom.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id => _gameState.NPCs[id])
            .ToList();
        var exits = currentRoom.GetAvailableExits();

        // Movement
        if (lower.StartsWith("go ") || lower.StartsWith("move ") || lower.StartsWith("enter ") || lower.StartsWith("head "))
        {
            var target = action.Substring(action.IndexOf(' ') + 1).Trim();
            var exit = FuzzyMatchExit(target, exits);
            return new ActionPlan { Action = "move", Target = exit?.DisplayName ?? target };
        }

        // Attack
        if (lower.StartsWith("attack ") || lower.StartsWith("fight "))
        {
            var target = action.Substring(action.IndexOf(' ') + 1).Trim();
            var npc = FuzzyMatchNpc(target, npcsInRoom.Where(n => n.IsAlive).ToList());
            return new ActionPlan { Action = "attack", Target = npc?.Name ?? target };
        }

        // Auto
        if (lower == "auto" || lower == "auto attack" || lower == "auto-attack")
            return new ActionPlan { Action = "auto", Target = "" };

        // Talk
        if (lower.StartsWith("talk to ") || lower.StartsWith("talk ") || lower.StartsWith("speak to ") || lower.StartsWith("speak "))
        {
            var target = System.Text.RegularExpressions.Regex.Replace(action, @"^(talk|speak)\s*(to\s*)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var npc = FuzzyMatchNpc(target, npcsInRoom.Where(n => n.IsAlive).ToList());
            return new ActionPlan { Action = "talk", Target = npc?.Name ?? target, Details = "" };
        }

        // Take / loot
        if (lower.StartsWith("take ") || lower.StartsWith("pick up ") || lower.StartsWith("loot ") || lower.StartsWith("grab "))
        {
            var target = System.Text.RegularExpressions.Regex.Replace(action, @"^(take|pick up|loot|grab)\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var fromIdx = target.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
            if (fromIdx >= 0)
            {
                var itemPart = target[..fromIdx].Trim();
                var npcPart = target[(fromIdx + 6)..].Trim();
                var npc = FuzzyMatchNpc(npcPart, npcsInRoom);
                return new ActionPlan { Action = "take", Target = itemPart, Details = npc?.Name ?? npcPart };
            }
            return new ActionPlan { Action = "take", Target = target };
        }

        // Examine
        if (lower.StartsWith("examine ") || lower.StartsWith("look at ") || lower.StartsWith("inspect "))
        {
            var target = System.Text.RegularExpressions.Regex.Replace(action, @"^(examine|look at|inspect)\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return new ActionPlan { Action = "examine", Target = target };
        }

        // Use
        if (lower.StartsWith("use "))
            return new ActionPlan { Action = "use", Target = action[4..].Trim(), Details = "" };

        // Equip
        if (lower.StartsWith("equip "))
            return new ActionPlan { Action = "equip", Target = action[6..].Trim() };

        // Unequip
        if (lower.StartsWith("unequip "))
            return new ActionPlan { Action = "unequip", Target = action[8..].Trim() };

        // Buy
        if (lower.StartsWith("buy "))
        {
            var merchant = npcsInRoom.FirstOrDefault(n => n.IsAlive && n.Role == NPCRole.Merchant);
            return new ActionPlan { Action = "buy", Target = merchant?.Name ?? "", Details = action[4..].Trim() };
        }

        // Sell
        if (lower.StartsWith("sell "))
        {
            var merchant = npcsInRoom.FirstOrDefault(n => n.IsAlive && n.Role == NPCRole.Merchant);
            return new ActionPlan { Action = "sell", Target = merchant?.Name ?? "", Details = action[5..].Trim() };
        }

        // Shop
        if (lower.StartsWith("shop"))
        {
            var target = lower.Length > 5 ? action[5..].Trim() : "";
            var merchant = npcsInRoom.FirstOrDefault(n => n.IsAlive && n.Role == NPCRole.Merchant);
            return new ActionPlan { Action = "shop", Target = merchant?.Name ?? target };
        }

        // Gather
        if (lower.StartsWith("gather ") || lower.StartsWith("mine ") || lower.StartsWith("harvest "))
            return new ActionPlan { Action = "gather", Target = action.Substring(action.IndexOf(' ') + 1).Trim() };

        // Craft
        if (lower.StartsWith("craft "))
        {
            var crafter = npcsInRoom.FirstOrDefault(n => n.IsAlive && n.CanCraft);
            return new ActionPlan { Action = "craft", Target = crafter?.Name ?? "", Details = action[6..].Trim() };
        }

        // Recipes
        if (lower.StartsWith("recipes"))
        {
            var crafter = npcsInRoom.FirstOrDefault(n => n.IsAlive && n.CanCraft);
            return new ActionPlan { Action = "recipes", Target = crafter?.Name ?? "" };
        }

        // Flee
        if (lower == "flee" || lower == "run" || lower == "escape")
            return new ActionPlan { Action = "stop", Target = "" };

        // Simple commands
        if (lower == "look" || lower == "look around" || lower == "survey")
            return new ActionPlan { Action = "look", Target = "" };
        if (lower == "inventory" || lower == "inv" || lower == "i")
            return new ActionPlan { Action = "inventory", Target = "" };
        if (lower == "quests" || lower == "quest log" || lower == "journal")
            return new ActionPlan { Action = "quests", Target = "" };
        if (lower == "status" || lower == "stats")
            return new ActionPlan { Action = "status", Target = "" };

        // Try as exit name
        var bareExit = FuzzyMatchExit(action.Trim(), exits);
        if (bareExit != null)
            return new ActionPlan { Action = "move", Target = bareExit.DisplayName };

        // Final fallback: pass raw
        var parts = action.Split(' ', 2);
        return new ActionPlan { Action = parts[0], Target = parts.Length > 1 ? parts[1] : "" };
    }

    // ─── Fuzzy Matching ──────────────────────────────────────────────────────

    private static Exit? FuzzyMatchExit(string input, List<Exit> exits)
    {
        if (exits.Count == 0) return null;
        var lower = input.ToLowerInvariant();

        var exact = exits.FirstOrDefault(e => e.DisplayName.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var contains = exits.FirstOrDefault(e => e.DisplayName.ToLowerInvariant().Contains(lower) ||
                                                  lower.Contains(e.DisplayName.ToLowerInvariant()));
        if (contains != null) return contains;

        var inputWords = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Exit? best = null;
        int bestScore = 0;
        foreach (var exit in exits)
        {
            var exitWords = exit.DisplayName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var overlap = inputWords.Intersect(exitWords).Count();
            if (overlap > bestScore) { bestScore = overlap; best = exit; }
        }
        return bestScore > 0 ? best : null;
    }

    private static Character? FuzzyMatchNpc(string input, List<Character> npcs)
    {
        if (npcs.Count == 0) return null;
        var lower = input.ToLowerInvariant();

        var exact = npcs.FirstOrDefault(n => n.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var contains = npcs.FirstOrDefault(n => n.Name.ToLowerInvariant().Contains(lower) ||
                                                 lower.Contains(n.Name.ToLowerInvariant()));
        return contains ?? npcs.FirstOrDefault();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (string action, string target) SplitActionTarget(string actionStr)
    {
        var parts = actionStr.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 0 ? parts[0].ToLowerInvariant() : "", parts.Length > 1 ? parts[1] : "");
    }

    private ActionPlan GetFallbackAction(Room currentRoom)
    {
        if (_gameState.InCombatMode && _gameState.CurrentCombatNpcId != null)
            return new ActionPlan { Action = "auto", Target = "" };

        var enemy = ValidActionFilter.GetEnemyInRoom(_gameState);
        if (enemy != null)
            return new ActionPlan { Action = "attack", Target = enemy.Name };

        var exits = currentRoom.GetAvailableExits();
        var unexplored = exits.FirstOrDefault(e => !_spatial.KnownRooms.ContainsKey(e.DestinationRoomId));
        if (unexplored != null)
            return new ActionPlan { Action = "move", Target = unexplored.DisplayName };

        // Path to frontier
        foreach (var fe in _spatial.GetUnexploredExits())
        {
            var directExit = exits.FirstOrDefault(e => e.DestinationRoomId == fe.DestinationRoomId);
            if (directExit != null)
                return new ActionPlan { Action = "move", Target = directExit.DisplayName };

            var path = _spatial.FindPathTo(fe.FromRoomId, _gameState.CurrentRoomId);
            if (path != null && path.Count > 0)
                return new ActionPlan { Action = "move", Target = path[0] };
        }

        if (exits.Count > 0)
            return new ActionPlan { Action = "move", Target = exits[0].DisplayName };

        return new ActionPlan { Action = "look", Target = "" };
    }

    private async Task RunReflection(int turn)
    {
        try
        {
            var (sys, usr) = ReflectionEngine.BuildReflectionPrompt(
                _history, _goals, _scratchpad, _gameState, _spatial, turn);
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = sys },
                new() { Role = "user", Content = usr }
            };
            var reflectionResponse = await _llmClient.ChatAsync(messages);
            ReflectionEngine.ApplyReflection(reflectionResponse, _scratchpad, _goals);
            _log.AppendLine($"> **Reflection (T{turn}):** {(reflectionResponse.Length > 200 ? reflectionResponse[..200] + "..." : reflectionResponse)}\n");
        }
        catch
        {
            _log.AppendLine($"> **Reflection (T{turn}):** (failed)\n");
        }
    }

    // ─── Logging ─────────────────────────────────────────────────────────────

    private void InitializeLog()
    {
        _log.Clear();
        _log.AppendLine($"# {_game.Title} - Agent Replay\n");
        _log.AppendLine($"**Game Style:** {_game.Style}\n");
        _log.AppendLine($"**Date:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        _log.AppendLine($"**Mode:** Intelligent Agent (ReAct + Memory + Anti-Loop)\n");
        if (!string.IsNullOrEmpty(_game.Description))
            _log.AppendLine($"**Description:** {_game.Description}\n");
        _log.AppendLine("---\n");
    }

    private void LogTurnHeader(Room room, int turn)
    {
        var exits = room.GetAvailableExits();
        var npcs = room.NPCIds
            .Where(id => _gameState.NPCs.ContainsKey(id))
            .Select(id =>
            {
                var npc = _gameState.NPCs[id];
                return npc.IsAlive ? npc.Name : $"☠️{npc.Name}";
            }).ToList();
        var inventory = _gameState.PlayerInventory.Items.Count > 0
            ? string.Join(", ", _gameState.PlayerInventory.Items.Values.Select(ii => ii.Item.Name))
            : "empty";

        _log.AppendLine($"**Location:** {room.Name}\n");
        _log.AppendLine($"**Health:** {_gameState.Player.Health}/{_gameState.Player.MaxHealth}\n");
        if (exits.Count > 0)
            _log.AppendLine($"**Exits:** {string.Join(", ", exits.Select(e => e.DisplayName))}\n");
        if (npcs.Count > 0)
            _log.AppendLine($"**NPCs:** {string.Join(", ", npcs)}\n");
        _log.AppendLine($"**Inventory:** {inventory}\n");
    }

    private void LogDeath()
    {
        _log.AppendLine("\n## 💀 Game Over\n");
        _log.AppendLine("The agent has fallen...\n");
    }
}
