using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

// ─── Valid Action Filter ─────────────────────────────────────────────────────

/// <summary>
/// Builds a context-sensitive list of valid actions based on current game state.
/// Prunes the action space so the LLM only sees relevant choices.
/// </summary>
public static class ValidActionFilter
{
    public static List<string> GetValidActions(
        GameState state, SpatialMemory spatial, Game game,
        HashSet<string> talkedToNpcs)
    {
        var actions = new List<string>();
        var room = state.GetCurrentRoom();
        var exits = room.GetAvailableExits();
        var npcsInRoom = room.NPCIds
            .Where(id => state.NPCs.ContainsKey(id))
            .Select(id => state.NPCs[id])
            .ToList();
        var economyEnabled = game.Economy?.Enabled == true;
        var craftingEnabled = game.Crafting?.Enabled == true;

        // In combat: limited action set
        if (state.InCombatMode && state.CurrentCombatNpcId != null &&
            state.NPCs.TryGetValue(state.CurrentCombatNpcId, out var combatNpc))
        {
            actions.Add("auto");
            actions.Add($"attack {combatNpc.Name}");
            actions.Add("flee");
            foreach (var ii in state.PlayerInventory.Items.Values)
            {
                if (ii.Item.IsConsumable && ii.Item.ConsumableEffects.ContainsKey("heal"))
                    actions.Add($"use {ii.Item.Name}");
            }
            return actions;
        }

        // PRIORITY 1: Enemies in room (listed first so LLM sees them first)
        var enemyRoles = new HashSet<NPCRole> { NPCRole.Warrior, NPCRole.Boss };
        var aliveEnemies = npcsInRoom.Where(n => n.IsAlive &&
            (enemyRoles.Contains(n.Role) || n.Alignment == CharacterAlignment.Evil)).ToList();
        foreach (var npc in aliveEnemies)
            actions.Add($"attack {npc.Name}");

        // PRIORITY 2: Dead NPCs with loot
        foreach (var npc in npcsInRoom.Where(n => !n.IsAlive && n.CarriedItems.Count > 0))
        {
            foreach (var ci in npc.CarriedItems.Values)
                actions.Add($"take {ci.Item.Name} from {npc.Name}");
        }

        // Items on floor
        foreach (var item in room.Items.Where(i => i.CanBeTaken))
            actions.Add($"take {item.Name}");

        // Alive friendly NPCs: talk (if not already talked to)
        foreach (var npc in npcsInRoom.Where(n => n.IsAlive && !enemyRoles.Contains(n.Role) &&
            n.Alignment != CharacterAlignment.Evil))
        {
            if (!talkedToNpcs.Contains(npc.Id))
                actions.Add($"talk to {npc.Name}");
        }

        // Merchant
        if (economyEnabled)
        {
            foreach (var npc in npcsInRoom.Where(n => n.IsAlive && n.Role == NPCRole.Merchant))
            {
                actions.Add($"shop {npc.Name}");
                foreach (var ci in npc.CarriedItems.Values)
                    actions.Add($"buy {ci.Item.Name}");
            }
        }

        // Crafter
        if (craftingEnabled)
        {
            foreach (var npc in npcsInRoom.Where(n => n.IsAlive && n.CanCraft))
                actions.Add($"recipes {npc.Name}");
        }

        // Gatherable resources
        if (room.Resources?.Resources != null)
        {
            foreach (var resource in room.Resources.Resources)
                actions.Add($"gather {resource.ItemId}");
        }

        // Exits: UNEXPLORED first, then visited (ordered by least visits)
        var unexploredExits = exits.Where(e => !spatial.KnownRooms.ContainsKey(e.DestinationRoomId)).ToList();
        var visitedExits = exits.Where(e => spatial.KnownRooms.ContainsKey(e.DestinationRoomId))
            .OrderBy(e => spatial.KnownRooms[e.DestinationRoomId].VisitCount).ToList();
        foreach (var exit in unexploredExits)
            actions.Add($"go {exit.DisplayName} (UNEXPLORED)");
        foreach (var exit in visitedExits)
            actions.Add($"go {exit.DisplayName} (visited)");

        // Always available
        actions.Add("look");
        actions.Add("inventory");

        return actions;
    }

    /// <summary>
    /// Check if there are enemies in the current room that should be fought.
    /// Used for deterministic force-attack before LLM reasoning.
    /// </summary>
    public static Character? GetEnemyInRoom(GameState state)
    {
        var room = state.GetCurrentRoom();
        var enemyRoles = new HashSet<NPCRole> { NPCRole.Warrior, NPCRole.Boss };
        return room.NPCIds
            .Where(id => state.NPCs.TryGetValue(id, out var npc) && npc.IsAlive &&
                (enemyRoles.Contains(npc.Role) || npc.Alignment == CharacterAlignment.Evil))
            .Select(id => state.NPCs[id])
            .FirstOrDefault();
    }

    public static int GetEquippedWeaponDamage(GameState state)
    {
        if (state.Player.EquipmentSlots.TryGetValue("main_hand", out var weaponId) && weaponId != null)
        {
            if (state.PlayerInventory.Items.TryGetValue(weaponId, out var ii))
                return ii.Item.DamageBonus;
        }
        return 0;
    }

    public static int GetEquippedArmorTotal(GameState state)
    {
        int total = 0;
        foreach (var (slot, itemId) in state.Player.EquipmentSlots)
        {
            if (itemId != null && state.PlayerInventory.Items.TryGetValue(itemId, out var ii))
                total += ii.Item.ArmorBonus;
        }
        return total;
    }
}

// ─── ReAct Prompt Builder ────────────────────────────────────────────────────

/// <summary>
/// Builds structured prompts for the agent's LLM decision call.
/// Designed to be concise enough for small models (3B parameters).
/// </summary>
public static class ReActPromptBuilder
{
    public static (string system, string user) BuildPrompt(
        GameState state, Game game,
        SpatialMemory spatial, ActionHistory history,
        Scratchpad scratchpad, GoalStack goals,
        List<string> validActions, List<string> warnings,
        HashSet<string> talkedToNpcs)
    {
        // Short, direct system prompt - small models need simplicity
        var system = @"You are playing an RPG. Pick the best action.
Reply with ONLY the action command, nothing else.
Rules: Attack enemies. Take loot. Explore UNEXPLORED exits. Never repeat failed actions.";

        var room = state.GetCurrentRoom();
        var exits = room.GetAvailableExits();
        var npcsInRoom = room.NPCIds
            .Where(id => state.NPCs.ContainsKey(id))
            .Select(id => state.NPCs[id])
            .ToList();

        double healthPct = state.Player.MaxHealth > 0
            ? (double)state.Player.Health / state.Player.MaxHealth : 1.0;

        var user = new System.Text.StringBuilder();

        // Keep prompt compact - small models lose context with long prompts
        user.AppendLine($"Location: {room.Name}");
        user.AppendLine($"Health: {state.Player.Health}/{state.Player.MaxHealth}");
        if (state.InCombatMode)
            user.AppendLine("IN COMBAT!");

        // NPCs - critical info
        var aliveEnemies = npcsInRoom.Where(n => n.IsAlive &&
            (n.Role == NPCRole.Warrior || n.Role == NPCRole.Boss || n.Alignment == CharacterAlignment.Evil)).ToList();
        if (aliveEnemies.Count > 0)
            user.AppendLine($"ENEMIES: {string.Join(", ", aliveEnemies.Select(e => $"{e.Name} HP:{e.Health}/{e.MaxHealth}"))}");

        var deadWithLoot = npcsInRoom.Where(n => !n.IsAlive && n.CarriedItems.Count > 0).ToList();
        if (deadWithLoot.Count > 0)
            user.AppendLine($"DEAD (has loot): {string.Join(", ", deadWithLoot.Select(n => n.Name))}");

        var friendlyNpcs = npcsInRoom.Where(n => n.IsAlive && !aliveEnemies.Contains(n))
            .Where(n => !talkedToNpcs.Contains(n.Id)).ToList();
        if (friendlyNpcs.Count > 0)
            user.AppendLine($"NPCs to talk to: {string.Join(", ", friendlyNpcs.Select(n => n.Name))}");

        // Floor items
        if (room.Items.Count > 0)
            user.AppendLine($"Items here: {string.Join(", ", room.Items.Select(i => i.Name))}");

        // Goals (just the top one)
        var topGoal = goals.GetCurrentGoal();
        if (topGoal != null)
            user.AppendLine($"Goal: {topGoal.Description}");

        // Recent actions (last 3 only)
        var recent = history.GetRecent(3);
        if (recent.Count > 0)
        {
            user.AppendLine("Recent:");
            foreach (var rec in recent)
                user.AppendLine($"  {rec.Action} {rec.Target} [{(rec.Succeeded ? "OK" : "FAIL")}]");
        }

        // Warnings
        foreach (var w in warnings)
            user.AppendLine($"WARNING: {w}");

        // Valid actions list
        user.AppendLine("\nChoose ONE action:");
        foreach (var a in validActions)
            user.AppendLine($"  {a}");

        return (system, user.ToString());
    }
}

// ─── Reflection Engine ───────────────────────────────────────────────────────

public static class ReflectionEngine
{
    public static bool ShouldReflect(ActionHistory history, GoalStack goals, GameState state, int turn)
    {
        // Only reflect every 5+ turns to avoid burning LLM calls
        if (turn < 5) return false;
        if (turn % 5 != 0) return false;

        // No goal progress for 5 turns
        if (goals.IsStalled(turn, 5))
            return true;

        // Low novelty
        if (history.GetNoveltyScore(6) < 0.4f)
            return true;

        return false;
    }

    public static (string system, string user) BuildReflectionPrompt(
        ActionHistory history, GoalStack goals, Scratchpad scratchpad,
        GameState state, SpatialMemory spatial, int turn)
    {
        var system = @"You are reflecting on RPG gameplay. What should change?
Reply with a short sentence about what to try next.";

        var user = new System.Text.StringBuilder();
        user.AppendLine($"Turn {turn}, Health: {state.Player.Health}/{state.Player.MaxHealth}");
        user.AppendLine($"Room: {state.GetCurrentRoom().Name}");
        user.AppendLine($"Explored {spatial.KnownRooms.Count} rooms, {spatial.Frontier.Count} unexplored exits");

        user.AppendLine("Recent:");
        foreach (var rec in history.GetRecent(6))
            user.AppendLine($"  {rec.Action} {rec.Target} [{(rec.Succeeded ? "OK" : "FAIL")}]");

        return (system, user.ToString());
    }

    public static void ApplyReflection(string llmResponse, Scratchpad scratchpad, GoalStack goals)
    {
        // Just add the response as a note - don't try to parse JSON from small models
        var trimmed = llmResponse.Trim();
        if (trimmed.Length > 150) trimmed = trimmed[..150];
        if (!string.IsNullOrWhiteSpace(trimmed))
            scratchpad.AddNote($"Strategy: {trimmed}");
    }
}

// ─── Anti-Loop Detector ──────────────────────────────────────────────────────

public enum LoopSeverity
{
    None,
    Advisory,
    Warning,
    Preventive,
    Reactive,
    HardOverride
}

/// <summary>
/// Multi-layer loop detection including room oscillation detection.
/// </summary>
public static class AntiLoopDetector
{
    public static LoopSeverity Assess(ActionHistory history, string action, string target, string roomId)
    {
        int repeats = history.CountRecent(action, target, 6);
        int sameRoom = history.ConsecutiveSameRoomTurns(roomId);
        float novelty = history.GetNoveltyScore(6);

        // Detect room oscillation (A→B→A→B pattern) - the main bug
        if (IsOscillating(history))
            return LoopSeverity.HardOverride;

        // Hard override: stuck 5+ turns with low novelty (even across rooms)
        if (novelty < 0.35f && history.Records.Count >= 5)
            return LoopSeverity.HardOverride;

        // Hard override: stuck 4+ turns in same room
        if (sameRoom >= 4)
            return LoopSeverity.HardOverride;

        // Reactive: 3+ turns same room
        if (sameRoom >= 3)
            return LoopSeverity.Reactive;

        // Preventive: same action 3+ times recently
        if (repeats >= 3)
            return LoopSeverity.Preventive;

        // Warning: same action 2 times in window
        if (repeats >= 2)
            return LoopSeverity.Warning;

        return LoopSeverity.None;
    }

    /// <summary>
    /// Detect A→B→A→B room oscillation pattern in the last 6 moves.
    /// </summary>
    public static bool IsOscillating(ActionHistory history)
    {
        var recent = history.GetRecent(6);
        if (recent.Count < 4) return false;

        // Check if we're moving back and forth: count unique rooms in last 6 actions
        var recentMoves = recent.Where(r => r.Action == "move").ToList();
        if (recentMoves.Count < 3) return false;

        var recentRooms = recent.Select(r => r.RoomId).ToList();
        var uniqueRooms = recentRooms.Distinct().Count();

        // If 6+ actions but only visiting 2-3 rooms, we're oscillating
        if (recent.Count >= 4 && uniqueRooms <= 2 && recentMoves.Count >= 3)
            return true;

        // Also detect if last 6 rooms are just 3 rooms repeated
        if (recent.Count >= 6 && uniqueRooms <= 3 && recentMoves.Count >= 4)
            return true;

        return false;
    }

    public static List<string> GetWarnings(ActionHistory history, string roomId)
    {
        var warnings = new List<string>();
        int sameRoom = history.ConsecutiveSameRoomTurns(roomId);
        float novelty = history.GetNoveltyScore(6);

        if (IsOscillating(history))
            warnings.Add("You are going back and forth between the same rooms! Go somewhere NEW.");

        if (sameRoom >= 2)
            warnings.Add($"You have been in this room for {sameRoom} turns. Do something different!");

        if (novelty < 0.5f && history.Records.Count >= 4)
            warnings.Add("Your actions are repetitive. Try something completely different.");

        return warnings;
    }

    /// <summary>
    /// For hard override: pick an action that breaks the loop.
    /// Prioritizes: enemies in room > unexplored exits > least-visited room.
    /// </summary>
    public static ActionPlan? GetOverrideAction(SpatialMemory spatial, GameState state)
    {
        // First: fight any enemy in the room
        var enemy = ValidActionFilter.GetEnemyInRoom(state);
        if (enemy != null)
            return new ActionPlan { Action = "attack", Target = enemy.Name };

        var room = state.GetCurrentRoom();
        var exits = room.GetAvailableExits();

        // Second: pick an unexplored exit directly from this room
        var unexplored = exits.FirstOrDefault(e => !spatial.KnownRooms.ContainsKey(e.DestinationRoomId));
        if (unexplored != null)
            return new ActionPlan { Action = "move", Target = unexplored.DisplayName };

        // Third: path to an unexplored frontier
        var frontierExits = spatial.GetUnexploredExits();
        foreach (var fe in frontierExits)
        {
            // Try direct exit
            var directExit = exits.FirstOrDefault(e => e.DestinationRoomId == fe.DestinationRoomId);
            if (directExit != null)
                return new ActionPlan { Action = "move", Target = directExit.DisplayName };

            // Try pathing
            var path = spatial.FindPathTo(fe.FromRoomId, state.CurrentRoomId);
            if (path != null && path.Count > 0)
                return new ActionPlan { Action = "move", Target = path[0] };
        }

        // Fourth: least-visited room
        var leastVisited = spatial.GetLeastVisitedRoom(state.CurrentRoomId);
        if (leastVisited != null)
        {
            var path = spatial.FindPathTo(leastVisited, state.CurrentRoomId);
            if (path != null && path.Count > 0)
                return new ActionPlan { Action = "move", Target = path[0] };
        }

        // Last resort: any exit we haven't been to recently
        if (exits.Count > 0)
        {
            var recentRoomIds = spatial.KnownRooms.Values
                .OrderByDescending(r => r.LastVisitedTurn)
                .Take(3)
                .Select(r => r.RoomId)
                .ToHashSet();
            var freshExit = exits.FirstOrDefault(e => !recentRoomIds.Contains(e.DestinationRoomId));
            return new ActionPlan { Action = "move", Target = (freshExit ?? exits[0]).DisplayName };
        }

        return new ActionPlan { Action = "look", Target = "" };
    }
}
