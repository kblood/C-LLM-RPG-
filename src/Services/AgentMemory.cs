using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

// ─── Spatial Memory ──────────────────────────────────────────────────────────

/// <summary>
/// Tracks the agent's knowledge of the game world as a graph of visited rooms,
/// connections between them, and a frontier of unexplored exits.
/// </summary>
public class SpatialMemory
{
    public Dictionary<string, RoomNode> KnownRooms { get; } = new();
    public List<FrontierExit> Frontier { get; } = new();

    /// <summary>
    /// Update spatial memory from the current room. Adds/updates the room node,
    /// discovers new frontier exits, and removes frontier entries pointing to this room.
    /// </summary>
    public void UpdateFromRoom(Room room, string roomId, int turn)
    {
        if (!KnownRooms.TryGetValue(roomId, out var node))
        {
            node = new RoomNode { RoomId = roomId, Name = room.Name };
            KnownRooms[roomId] = node;
        }

        node.VisitCount++;
        node.LastVisitedTurn = turn;

        // Update exits
        var exits = room.GetAvailableExits();
        foreach (var exit in exits)
        {
            node.KnownExits[exit.DisplayName] = exit.DestinationRoomId;

            // If destination not yet visited, add to frontier
            if (!KnownRooms.ContainsKey(exit.DestinationRoomId))
            {
                if (!Frontier.Any(f => f.DestinationRoomId == exit.DestinationRoomId))
                {
                    Frontier.Add(new FrontierExit
                    {
                        FromRoomId = roomId,
                        ExitDisplayName = exit.DisplayName,
                        DestinationRoomId = exit.DestinationRoomId,
                        DiscoveredOnTurn = turn
                    });
                }
            }
        }

        // Update NPCs seen
        node.NpcsSeen.Clear();
        foreach (var npcId in room.NPCIds)
            node.NpcsSeen.Add(npcId);

        // Update items seen
        node.ItemsSeen.Clear();
        foreach (var item in room.Items)
            node.ItemsSeen.Add(item.Id);

        // Remove frontier entries that point to this room (we've now visited it)
        Frontier.RemoveAll(f => f.DestinationRoomId == roomId);
    }

    /// <summary>
    /// Update NPC feature flags (merchant/crafter/healer) from game state NPCs.
    /// </summary>
    public void UpdateRoomFeatures(Room room, string roomId, Dictionary<string, Character> npcs)
    {
        if (!KnownRooms.TryGetValue(roomId, out var node)) return;

        node.HasMerchant = false;
        node.HasCrafter = false;
        node.HasHealer = false;

        foreach (var npcId in room.NPCIds)
        {
            if (!npcs.TryGetValue(npcId, out var npc) || !npc.IsAlive) continue;
            if (npc.Role == NPCRole.Merchant) node.HasMerchant = true;
            if (npc.CanCraft) node.HasCrafter = true;
            if (npc.Role == NPCRole.Healer) node.HasHealer = true;
        }
    }

    /// <summary>
    /// Get unexplored exits sorted by oldest discovery (explore breadth-first).
    /// </summary>
    public List<FrontierExit> GetUnexploredExits()
        => Frontier.OrderBy(f => f.DiscoveredOnTurn).ToList();

    /// <summary>
    /// BFS pathfinding on the known room graph. Returns list of exit display names to follow,
    /// or null if the target is unreachable in the known graph.
    /// </summary>
    public List<string>? FindPathTo(string targetRoomId, string fromRoomId)
    {
        if (fromRoomId == targetRoomId) return new List<string>();

        var visited = new HashSet<string> { fromRoomId };
        var queue = new Queue<(string roomId, List<string> path)>();
        queue.Enqueue((fromRoomId, new List<string>()));

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            if (!KnownRooms.TryGetValue(current, out var node)) continue;

            foreach (var (exitName, destId) in node.KnownExits)
            {
                if (visited.Contains(destId)) continue;
                visited.Add(destId);

                var newPath = new List<string>(path) { exitName };
                if (destId == targetRoomId) return newPath;

                queue.Enqueue((destId, newPath));
            }
        }

        return null; // unreachable
    }

    /// <summary>
    /// Find a room matching a predicate (e.g., has merchant, has crafter).
    /// Returns room ID or null.
    /// </summary>
    public string? FindRoomWith(Func<RoomNode, bool> predicate)
        => KnownRooms.Values.FirstOrDefault(predicate)?.RoomId;

    /// <summary>
    /// Get the least-visited known room (for anti-loop fallback exploration).
    /// </summary>
    public string? GetLeastVisitedRoom(string currentRoomId)
        => KnownRooms.Values
            .Where(r => r.RoomId != currentRoomId)
            .OrderBy(r => r.VisitCount)
            .ThenBy(r => r.LastVisitedTurn)
            .FirstOrDefault()?.RoomId;
}

public class RoomNode
{
    public string RoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public int VisitCount { get; set; }
    public int LastVisitedTurn { get; set; }
    public Dictionary<string, string> KnownExits { get; set; } = new(); // exitName → destRoomId
    public HashSet<string> NpcsSeen { get; set; } = new();
    public HashSet<string> ItemsSeen { get; set; } = new();
    public bool HasMerchant { get; set; }
    public bool HasCrafter { get; set; }
    public bool HasHealer { get; set; }
}

public class FrontierExit
{
    public string FromRoomId { get; set; } = "";
    public string ExitDisplayName { get; set; } = "";
    public string DestinationRoomId { get; set; } = "";
    public int DiscoveredOnTurn { get; set; }
}

// ─── Action History ──────────────────────────────────────────────────────────

/// <summary>
/// Tracks all agent actions with outcomes for loop detection and context.
/// </summary>
public class ActionHistory
{
    private const int MaxRecords = 50;
    public List<ActionRecord> Records { get; } = new();

    public void Add(int turn, string roomId, string action, string target, bool success, string summary)
    {
        Records.Add(new ActionRecord
        {
            Turn = turn,
            RoomId = roomId,
            Action = action,
            Target = target,
            Succeeded = success,
            ResultSummary = summary.Length > 100 ? summary[..100] : summary
        });
        if (Records.Count > MaxRecords)
            Records.RemoveAt(0);
    }

    /// <summary>
    /// Check if the same action+target has been attempted recently.
    /// </summary>
    public bool IsRepeating(string action, string target, int window = 3)
    {
        var recent = Records.TakeLast(window);
        return recent.Any(r =>
            r.Action.Equals(action, StringComparison.OrdinalIgnoreCase) &&
            r.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Count consecutive turns spent in the same room.
    /// </summary>
    public int ConsecutiveSameRoomTurns(string roomId)
    {
        int count = 0;
        for (int i = Records.Count - 1; i >= 0; i--)
        {
            if (Records[i].RoomId == roomId) count++;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Check if a specific action+target has been tried and failed recently.
    /// </summary>
    public bool HasTriedAndFailed(string action, string target, int window = 5)
    {
        return Records.TakeLast(window).Any(r =>
            r.Action.Equals(action, StringComparison.OrdinalIgnoreCase) &&
            r.Target.Equals(target, StringComparison.OrdinalIgnoreCase) &&
            !r.Succeeded);
    }

    public List<ActionRecord> GetRecent(int count)
        => Records.TakeLast(count).ToList();

    /// <summary>
    /// Novelty score: ratio of unique (action,target) pairs in the recent window.
    /// 1.0 = all unique, 0.0 = all identical.
    /// </summary>
    public float GetNoveltyScore(int window = 5)
    {
        var recent = Records.TakeLast(window).ToList();
        if (recent.Count == 0) return 1.0f;
        var unique = recent.Select(r => $"{r.Action}:{r.Target}".ToLower()).Distinct().Count();
        return (float)unique / recent.Count;
    }

    /// <summary>
    /// Count how many times a specific action+target appears in the recent window.
    /// </summary>
    public int CountRecent(string action, string target, int window = 6)
    {
        return Records.TakeLast(window).Count(r =>
            r.Action.Equals(action, StringComparison.OrdinalIgnoreCase) &&
            r.Target.Equals(target, StringComparison.OrdinalIgnoreCase));
    }
}

public class ActionRecord
{
    public int Turn { get; set; }
    public string RoomId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public bool Succeeded { get; set; }
    public string ResultSummary { get; set; } = "";
}

// ─── Scratchpad ──────────────────────────────────────────────────────────────

/// <summary>
/// Agent-written persistent notes that carry across turns.
/// The LLM adds notes during reflection to guide future decisions.
/// </summary>
public class Scratchpad
{
    private const int MaxNotes = 30;
    public List<string> Notes { get; } = new();

    /// <summary>
    /// Add a note, deduplicating if a very similar note already exists.
    /// </summary>
    public void AddNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        note = note.Trim();

        // Deduplicate: skip if an existing note contains this one or vice versa
        if (Notes.Any(n => n.Contains(note, StringComparison.OrdinalIgnoreCase) ||
                          note.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return;

        Notes.Add(note);
        if (Notes.Count > MaxNotes)
            Notes.RemoveAt(0);
    }

    public void RemoveNotesContaining(string keyword)
    {
        Notes.RemoveAll(n => n.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public List<string> GetAllNotes() => new(Notes);

    public void Clear() => Notes.Clear();
}

// ─── Goal Stack ──────────────────────────────────────────────────────────────

public enum AgentGoalType
{
    Explore, Kill, Collect, Craft, Talk, Reach, Buy, Equip, Heal, Loot, Gather
}

public class AgentGoal
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public AgentGoalType GoalType { get; set; }
    public string? TargetId { get; set; }
    public int TargetQuantity { get; set; } = 1;
    public int CurrentProgress { get; set; }
    public int Priority { get; set; } // lower = higher priority
    public int CreatedOnTurn { get; set; }
    public int LastProgressTurn { get; set; }
    public bool IsCompleted { get; set; }
}

/// <summary>
/// Decomposes game objectives and quests into prioritized atomic goals.
/// Dynamically reprioritizes based on game state (heal if low HP, loot if dead NPC nearby, etc.).
/// </summary>
public class GoalStack
{
    public List<AgentGoal> Goals { get; } = new();
    private int _nextId = 1;

    /// <summary>
    /// Initialize goals from game win conditions and active quests.
    /// </summary>
    public void InitializeFromGame(Game game, GameState state)
    {
        Goals.Clear();
        _nextId = 1;

        // Create goals from WinConditions
        if (game.WinConditions != null)
        {
            foreach (var wc in game.WinConditions)
            {
                var goal = wc.Type switch
                {
                    "room" => new AgentGoal
                    {
                        GoalType = AgentGoalType.Reach,
                        TargetId = wc.TargetId,
                        Description = $"Reach room: {wc.Description}",
                        Priority = 10
                    },
                    "item" => new AgentGoal
                    {
                        GoalType = AgentGoalType.Collect,
                        TargetId = wc.TargetId,
                        Description = $"Collect item: {wc.Description}",
                        Priority = 10
                    },
                    "npc_defeat" => new AgentGoal
                    {
                        GoalType = AgentGoalType.Kill,
                        TargetId = wc.TargetId,
                        Description = $"Defeat: {wc.Description}",
                        Priority = 10
                    },
                    "quest_complete" => new AgentGoal
                    {
                        GoalType = AgentGoalType.Collect, // generic
                        TargetId = wc.TargetId,
                        Description = $"Complete quest: {wc.Description}",
                        Priority = 10
                    },
                    _ => null
                };

                if (goal != null)
                {
                    goal.Id = $"win_{_nextId++}";
                    Goals.Add(goal);
                }
            }
        }

        // Legacy room-based win conditions
        if ((game.WinConditions == null || game.WinConditions.Count == 0) && game.WinConditionRoomIds != null)
        {
            foreach (var roomId in game.WinConditionRoomIds)
            {
                Goals.Add(new AgentGoal
                {
                    Id = $"win_{_nextId++}",
                    GoalType = AgentGoalType.Reach,
                    TargetId = roomId,
                    Description = $"Reach room: {roomId}",
                    Priority = 10
                });
            }
        }

        // Create goals from active quests
        foreach (var quest in state.ActiveQuests)
        {
            foreach (var req in quest.Requirements)
            {
                var goalType = req.Type switch
                {
                    "item" => AgentGoalType.Collect,
                    "kill" => AgentGoalType.Kill,
                    "location" => AgentGoalType.Reach,
                    "talk" => AgentGoalType.Talk,
                    "craft" => AgentGoalType.Craft,
                    "gather" => AgentGoalType.Gather,
                    _ => AgentGoalType.Collect
                };

                Goals.Add(new AgentGoal
                {
                    Id = $"quest_{_nextId++}",
                    GoalType = goalType,
                    TargetId = req.TargetId,
                    TargetQuantity = req.Quantity,
                    CurrentProgress = req.CurrentProgress,
                    Description = req.Description ?? $"{req.Type}: {req.TargetId} x{req.Quantity}",
                    Priority = 20
                });
            }
        }

        // Default exploration goal at lowest priority
        Goals.Add(new AgentGoal
        {
            Id = $"explore_{_nextId++}",
            GoalType = AgentGoalType.Explore,
            Description = "Explore all rooms",
            Priority = 100
        });
    }

    /// <summary>
    /// Get the highest priority incomplete goal.
    /// </summary>
    public AgentGoal? GetCurrentGoal()
        => Goals.Where(g => !g.IsCompleted).OrderBy(g => g.Priority).FirstOrDefault();

    /// <summary>
    /// Update goal progress from current game state.
    /// </summary>
    public void UpdateProgress(GameState state, int turn)
    {
        foreach (var goal in Goals.Where(g => !g.IsCompleted))
        {
            bool wasComplete = goal.IsCompleted;
            int oldProgress = goal.CurrentProgress;

            switch (goal.GoalType)
            {
                case AgentGoalType.Reach:
                    if (goal.TargetId != null && state.CurrentRoomId == goal.TargetId)
                    {
                        goal.CurrentProgress = 1;
                        goal.IsCompleted = true;
                    }
                    break;

                case AgentGoalType.Collect:
                    if (goal.TargetId != null)
                    {
                        var invItem = state.PlayerInventory.Items.Values
                            .FirstOrDefault(ii => ii.Item.Id == goal.TargetId);
                        goal.CurrentProgress = invItem?.Quantity ?? 0;
                        if (goal.CurrentProgress >= goal.TargetQuantity)
                            goal.IsCompleted = true;
                    }
                    break;

                case AgentGoalType.Kill:
                    if (goal.TargetId != null && state.NPCs.TryGetValue(goal.TargetId, out var npc))
                    {
                        if (!npc.IsAlive)
                        {
                            goal.CurrentProgress = 1;
                            goal.IsCompleted = true;
                        }
                    }
                    break;

                case AgentGoalType.Equip:
                    // Equip goals auto-complete when any equipment is worn
                    if (state.Player.EquipmentSlots.Values.Any(v => v != null))
                    {
                        goal.CurrentProgress = 1;
                        goal.IsCompleted = true;
                    }
                    break;
            }

            if (goal.CurrentProgress != oldProgress)
                goal.LastProgressTurn = turn;
        }
    }

    /// <summary>
    /// Add a dynamic goal (e.g., from reflection).
    /// </summary>
    public void AddGoal(AgentGoal goal)
    {
        goal.Id = $"dynamic_{_nextId++}";
        Goals.Add(goal);
    }

    public void CompleteGoal(string goalId)
    {
        var goal = Goals.FirstOrDefault(g => g.Id == goalId);
        if (goal != null) goal.IsCompleted = true;
    }

    /// <summary>
    /// Check if the agent is stalled (no progress on any goal for N turns).
    /// </summary>
    public bool IsStalled(int turn, int maxTurnsWithoutProgress = 5)
    {
        var activeGoals = Goals.Where(g => !g.IsCompleted).ToList();
        if (activeGoals.Count == 0) return false;
        return activeGoals.All(g => turn - g.LastProgressTurn > maxTurnsWithoutProgress);
    }

    /// <summary>
    /// Reprioritize goals based on urgency (heal, loot, etc.).
    /// Call each turn after observing game state.
    /// </summary>
    public void Reprioritize(GameState state, Room currentRoom)
    {
        // Heal goal: urgent if health < 25%
        double healthPct = state.Player.MaxHealth > 0
            ? (double)state.Player.Health / state.Player.MaxHealth : 1.0;
        if (healthPct < 0.25)
        {
            if (!Goals.Any(g => g.GoalType == AgentGoalType.Heal && !g.IsCompleted))
            {
                AddGoal(new AgentGoal
                {
                    GoalType = AgentGoalType.Heal,
                    Description = "Heal - health is critical",
                    Priority = 1
                });
            }
        }
        else
        {
            // Remove heal goals if health is fine
            Goals.RemoveAll(g => g.GoalType == AgentGoalType.Heal && !g.IsCompleted);
        }

        // Loot goal: if dead NPC with items in current room
        var deadWithLoot = currentRoom.NPCIds
            .Where(id => state.NPCs.TryGetValue(id, out var npc) && !npc.IsAlive && npc.CarriedItems.Count > 0)
            .ToList();
        if (deadWithLoot.Count > 0)
        {
            if (!Goals.Any(g => g.GoalType == AgentGoalType.Loot && !g.IsCompleted))
            {
                AddGoal(new AgentGoal
                {
                    GoalType = AgentGoalType.Loot,
                    Description = "Loot dead NPCs in room",
                    Priority = 5
                });
            }
        }
    }

    /// <summary>
    /// Get top N incomplete goals for display in the prompt.
    /// </summary>
    public List<AgentGoal> GetTopGoals(int count = 5)
        => Goals.Where(g => !g.IsCompleted).OrderBy(g => g.Priority).Take(count).ToList();
}
