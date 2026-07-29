using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Advances deterministic, non-player world systems by one turn.
/// </summary>
public class WorldSimulationService
{
    private readonly WorldSimulationOptions _options;

    public WorldSimulationService(WorldSimulationOptions? options = null)
    {
        _options = options ?? new WorldSimulationOptions();
    }

    /// <summary>
    /// Advances the world exactly once. Input events describe the player action
    /// that caused the turn and are recorded alongside generated world events.
    /// </summary>
    public WorldSimulationResult AdvanceTurn(
        GameState state,
        IEnumerable<WorldEvent>? inputEvents = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.TurnNumber++;
        var result = new WorldSimulationResult { TurnNumber = state.TurnNumber };

        foreach (var worldEvent in inputEvents ?? Enumerable.Empty<WorldEvent>())
        {
            if (worldEvent.TurnNumber <= 0)
                worldEvent.TurnNumber = state.TurnNumber;

            WorldEventJournal.Record(state, worldEvent, "action", _options.MaxRecentEvents);
            result.Events.Add(worldEvent);
            AddMessage(result, worldEvent.Message);
        }

        TickResourceRespawns(state, result);
        TickNpcPatrols(state, result);

        return result;
    }

    private void TickResourceRespawns(GameState state, WorldSimulationResult result)
    {
        foreach (var roomPair in state.Rooms.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var room = roomPair.Value;
            var depleted = room.Resources?.DepletedResources;
            if (depleted == null || depleted.Count == 0)
                continue;

            foreach (var resourceId in depleted.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList())
            {
                // Negative values represent permanently depleted, nonrenewable resources.
                if (depleted[resourceId] < 0)
                    continue;

                var remainingTurns = depleted[resourceId] - 1;
                if (remainingTurns > 0)
                {
                    depleted[resourceId] = remainingTurns;
                    continue;
                }

                depleted.Remove(resourceId);
                var resource = room.Resources?.Resources.FirstOrDefault(candidate =>
                    candidate.ItemId.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
                var resourceName = resource?.DisplayName ?? resourceId;
                var message = $"{resourceName} is available again in {room.Name}.";
                var worldEvent = WorldEvent.Create(
                    WorldEventType.ResourceRespawned,
                    resourceId,
                    roomId: room.Id,
                    message: message);

                RecordGeneratedEvent(state, result, worldEvent);
            }
        }
    }

    private void TickNpcPatrols(GameState state, WorldSimulationResult result)
    {
        foreach (var npcPair in state.NPCs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var npcId = npcPair.Key;
            var npc = npcPair.Value;
            var isEngagedWithPlayer =
                (state.InChatMode && npcId.Equals(state.CurrentChatNpcId, StringComparison.OrdinalIgnoreCase)) ||
                (state.InCombatMode && npcId.Equals(state.CurrentCombatNpcId, StringComparison.OrdinalIgnoreCase));
            if (!npc.IsAlive || !npc.CanMove || state.Companions.Contains(npcId) || isEngagedWithPlayer)
                continue;

            var patrol = npc.PatrolRoomIds?
                .Where(state.Rooms.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (patrol == null || patrol.Count == 0)
                continue;

            npc.TurnsSinceLastMove++;
            var interval = Math.Max(1, npc.PatrolIntervalTurns ?? _options.DefaultPatrolIntervalTurns);
            if (npc.TurnsSinceLastMove < interval)
                continue;

            npc.TurnsSinceLastMove = 0;
            var oldRoomId = ResolveNpcRoom(state, npcId, npc);
            var destinationId = GetNextPatrolRoom(patrol, oldRoomId);
            if (string.IsNullOrEmpty(destinationId) ||
                destinationId.Equals(oldRoomId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var room in state.Rooms.Values)
                room.NPCIds.RemoveAll(id => id.Equals(npcId, StringComparison.OrdinalIgnoreCase));

            npc.CurrentRoomId = destinationId;
            var destination = state.Rooms[destinationId];
            if (!destination.NPCIds.Contains(npcId, StringComparer.OrdinalIgnoreCase))
                destination.NPCIds.Add(npcId);

            var originName = oldRoomId != null && state.Rooms.TryGetValue(oldRoomId, out var origin)
                ? origin.Name
                : "elsewhere";
            var message = $"{npc.Name} travels from {originName} to {destination.Name}.";
            var worldEvent = WorldEvent.Create(
                WorldEventType.NpcMoved,
                destinationId,
                actorId: npcId,
                roomId: destinationId,
                message: message);
            if (!string.IsNullOrEmpty(oldRoomId))
                worldEvent.Data["fromRoomId"] = oldRoomId;

            RecordGeneratedEvent(state, result, worldEvent);
        }
    }

    private void RecordGeneratedEvent(
        GameState state,
        WorldSimulationResult result,
        WorldEvent worldEvent)
    {
        worldEvent.TurnNumber = state.TurnNumber;
        WorldEventJournal.Record(state, worldEvent, "world", _options.MaxRecentEvents);
        result.Events.Add(worldEvent);
        AddMessage(result, worldEvent.Message);
    }

    private static string? ResolveNpcRoom(GameState state, string npcId, Character npc)
    {
        if (!string.IsNullOrEmpty(npc.CurrentRoomId) && state.Rooms.ContainsKey(npc.CurrentRoomId))
            return npc.CurrentRoomId;

        return state.Rooms
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .FirstOrDefault(pair => pair.Value.NPCIds.Contains(npcId, StringComparer.OrdinalIgnoreCase))
            .Key;
    }

    private static string GetNextPatrolRoom(IReadOnlyList<string> patrol, string? currentRoomId)
    {
        if (patrol.Count == 1)
            return patrol[0];

        var currentIndex = -1;
        for (var index = 0; index < patrol.Count; index++)
        {
            if (patrol[index].Equals(currentRoomId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = index;
                break;
            }
        }

        return currentIndex < 0 ? patrol[0] : patrol[(currentIndex + 1) % patrol.Count];
    }

    private static void AddMessage(WorldSimulationResult result, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            result.Messages.Add(message);
    }
}

public class WorldSimulationOptions
{
    public int DefaultPatrolIntervalTurns { get; set; } = 3;
    public int MaxRecentEvents { get; set; } = 50;
}

public class WorldSimulationResult
{
    public int TurnNumber { get; set; }
    public List<WorldEvent> Events { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Shared bounded event journal used by world services and available to hosts
/// that need to record their own structured events.
/// </summary>
public static class WorldEventJournal
{
    public static void Record(
        GameState state,
        WorldEvent worldEvent,
        string source = "event",
        int maxRecentEvents = 50)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(worldEvent);

        if (worldEvent.TurnNumber <= 0)
            worldEvent.TurnNumber = state.TurnNumber;

        if (string.IsNullOrWhiteSpace(worldEvent.Id))
        {
            var sequence = state.RecentWorldEvents.Count(candidate =>
                candidate.TurnNumber == worldEvent.TurnNumber) + 1;
            var idPrefix = $"{source}-{state.WorldSeed:X8}-{worldEvent.TurnNumber:D8}-";
            var candidateId = $"{idPrefix}{sequence:D3}";

            // The journal is bounded. Once old events from this turn are
            // trimmed, Count + 1 can reuse an ID that is still present. Advance
            // until the generated deterministic ID is genuinely unused.
            while (state.RecentWorldEvents.Any(candidate =>
                       candidate.Id.Equals(candidateId, StringComparison.Ordinal)))
            {
                sequence++;
                candidateId = $"{idPrefix}{sequence:D3}";
            }

            worldEvent.Id = candidateId;
        }

        if (!state.RecentWorldEvents.Any(candidate =>
                candidate.Id.Equals(worldEvent.Id, StringComparison.Ordinal)))
        {
            state.RecentWorldEvents.Add(worldEvent);
        }

        var limit = Math.Max(1, maxRecentEvents);
        if (state.RecentWorldEvents.Count > limit)
        {
            state.RecentWorldEvents.RemoveRange(
                0,
                state.RecentWorldEvents.Count - limit);
        }
    }
}
