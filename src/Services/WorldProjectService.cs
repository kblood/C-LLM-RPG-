using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Advances staged world projects from structured events or player donations and
/// applies their declarative effects to the runtime world.
/// </summary>
public class WorldProjectService
{
    private const int MaxRecentEvents = 50;

    public WorldProjectProgressionResult Activate(GameState state, string projectId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new WorldProjectProgressionResult();
        var project = FindProject(state, projectId);

        if (project == null)
        {
            result.Messages.Add($"Unknown world project '{projectId}'.");
            return result;
        }

        if (project.Status != WorldProjectStatus.Available)
            return result;

        StartProject(state, project, result, state.TurnNumber);
        AdvanceCompletedStages(state, project, result, state.TurnNumber);
        return result;
    }

    public WorldProjectProgressionResult Process(
        GameState state,
        IEnumerable<WorldEvent>? events)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new WorldProjectProgressionResult();
        var inputEvents = (events ?? Enumerable.Empty<WorldEvent>()).ToList();

        foreach (var project in state.WorldProjects
                     .Where(IsProgressable)
                     .OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
        {
            var stage = project.CurrentStage;
            if (stage == null)
            {
                CompleteProject(state, project, result, state.TurnNumber);
                continue;
            }

            var changed = false;
            foreach (var worldEvent in inputEvents)
            {
                foreach (var requirement in stage.Requirements)
                {
                    if (requirement.IsMet || !Matches(requirement, worldEvent))
                        continue;

                    var previous = requirement.CurrentAmount;
                    requirement.CurrentAmount = Math.Min(
                        Math.Max(1, requirement.RequiredAmount),
                        requirement.CurrentAmount + Math.Max(1, worldEvent.Quantity));
                    changed |= previous != requirement.CurrentAmount;
                }
            }

            if (changed)
            {
                if (project.Status == WorldProjectStatus.Available)
                    StartProject(state, project, result, state.TurnNumber);

                result.ChangedProjectIds.Add(project.Id);
                result.Messages.Add($"Project updated: {project.Name} — {stage.Name}.");
            }

            if (project.Status == WorldProjectStatus.Active)
                AdvanceCompletedStages(state, project, result, state.TurnNumber);
        }

        return result;
    }

    /// <summary>
    /// Consumes an item or currency contribution and applies it to the current
    /// stage's matching requirement.
    /// </summary>
    public WorldProjectProgressionResult Contribute(
        GameState state,
        string projectId,
        WorldProjectRequirementType contributionType,
        string? targetId,
        long amount,
        int? effectiveTurn = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new WorldProjectProgressionResult();
        var project = FindProject(state, projectId);
        var progressionTurn = effectiveTurn ?? state.TurnNumber;

        if (project == null)
        {
            result.Messages.Add($"Unknown world project '{projectId}'.");
            return result;
        }

        if (!IsProgressable(project) || amount <= 0)
        {
            result.Messages.Add($"{project.Name} cannot accept that contribution.");
            return result;
        }

        if (contributionType is not (WorldProjectRequirementType.Item or WorldProjectRequirementType.Currency))
        {
            result.Messages.Add("Only item and currency requirements accept direct contributions.");
            return result;
        }

        var stage = project.CurrentStage;
        if (stage == null)
        {
            CompleteProject(state, project, result, progressionTurn);
            return result;
        }

        var requirement = stage.Requirements.FirstOrDefault(candidate =>
            !candidate.IsMet &&
            candidate.Type == contributionType &&
            (contributionType == WorldProjectRequirementType.Currency ||
             candidate.TargetId?.Equals(targetId, StringComparison.OrdinalIgnoreCase) == true));
        if (requirement == null)
        {
            result.Messages.Add("That is not needed for the current project stage.");
            return result;
        }

        var acceptedAmount = Math.Min(amount, Math.Max(1, requirement.RequiredAmount) - requirement.CurrentAmount);
        if (contributionType == WorldProjectRequirementType.Item)
        {
            var inventoryPair = state.PlayerInventory.Items.FirstOrDefault(pair =>
                pair.Key.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(inventoryPair.Key) || inventoryPair.Value.Quantity <= 0)
            {
                result.Messages.Add($"You do not have any {targetId} to contribute.");
                return result;
            }

            acceptedAmount = Math.Min(acceptedAmount, inventoryPair.Value.Quantity);
            if (acceptedAmount <= 0 ||
                !state.PlayerInventory.RemoveItem(inventoryPair.Key, checked((int)acceptedAmount)))
            {
                result.Messages.Add("The item contribution could not be completed.");
                return result;
            }
        }
        else
        {
            acceptedAmount = Math.Min(acceptedAmount, state.Player.Wallet.TotalBaseUnits);
            if (acceptedAmount <= 0 || !state.Player.Wallet.Remove(acceptedAmount))
            {
                result.Messages.Add("You do not have enough currency to contribute.");
                return result;
            }
        }

        requirement.CurrentAmount += acceptedAmount;
        if (project.Status == WorldProjectStatus.Available)
            StartProject(state, project, result, progressionTurn);

        result.ChangedProjectIds.Add(project.Id);
        var label = contributionType == WorldProjectRequirementType.Currency
            ? "currency"
            : targetId ?? "items";
        var message = $"Contributed {acceptedAmount} {label} to {project.Name}.";
        result.Messages.Add(message);

        var contributionEvent = WorldEvent.Create(
            WorldEventType.ProjectContributed,
            project.Id,
            quantity: acceptedAmount > int.MaxValue ? int.MaxValue : (int)acceptedAmount,
            actorId: state.Player.Id,
            roomId: state.CurrentRoomId,
            message: message);
        contributionEvent.TurnNumber = progressionTurn;
        contributionEvent.Data["stageId"] = stage.Id;
        contributionEvent.Data["requirementType"] = contributionType.ToString();
        contributionEvent.Data["amount"] = acceptedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(targetId))
            contributionEvent.Data["contributionId"] = targetId;
        RecordEvent(state, result, contributionEvent, "project");

        AdvanceCompletedStages(state, project, result, progressionTurn);
        return result;
    }

    private static bool Matches(WorldProjectRequirement requirement, WorldEvent worldEvent)
    {
        if (requirement.Type == WorldProjectRequirementType.QuestCompleted)
        {
            return worldEvent.Type == WorldEventType.QuestCompleted &&
                   TargetMatches(requirement.TargetId, worldEvent);
        }

        if (requirement.Type != WorldProjectRequirementType.Event)
            return false;

        return (!requirement.EventType.HasValue || requirement.EventType.Value == worldEvent.Type) &&
               TargetMatches(requirement.TargetId, worldEvent);
    }

    private static bool TargetMatches(string? requiredTargetId, WorldEvent worldEvent)
    {
        if (string.IsNullOrWhiteSpace(requiredTargetId))
            return true;

        var eventTarget = worldEvent.Type == WorldEventType.RoomEntered
            ? worldEvent.RoomId ?? worldEvent.TargetId
            : worldEvent.TargetId;
        return requiredTargetId.Equals(eventTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static void AdvanceCompletedStages(
        GameState state,
        WorldProject project,
        WorldProjectProgressionResult result,
        int progressionTurn)
    {
        var guard = project.Stages.Count + 1;
        while (project.Status == WorldProjectStatus.Active && guard-- > 0)
        {
            var stage = project.CurrentStage;
            if (stage == null)
            {
                CompleteProject(state, project, result, progressionTurn);
                return;
            }

            if (!stage.IsComplete)
                return;

            ApplyEffects(state, project, stage, result);
            stage.CompletedTurn ??= progressionTurn;
            var completedStageId = stage.Id;
            var completedStageName = stage.Name;
            project.CurrentStageIndex++;
            result.ChangedProjectIds.Add(project.Id);

            if (project.CurrentStage == null)
            {
                CompleteProject(state, project, result, progressionTurn);
                return;
            }

            var message = $"{project.Name} advanced: {completedStageName} completed; " +
                          $"{project.CurrentStage.Name} is now underway.";
            result.Messages.Add(message);
            var advancedEvent = WorldEvent.Create(
                WorldEventType.ProjectAdvanced,
                project.Id,
                roomId: state.CurrentRoomId,
                message: message);
            advancedEvent.TurnNumber = progressionTurn;
            advancedEvent.Data["completedStageId"] = completedStageId;
            advancedEvent.Data["nextStageId"] = project.CurrentStage.Id;
            RecordEvent(state, result, advancedEvent, "project");
        }
    }

    private static void ApplyEffects(
        GameState state,
        WorldProject project,
        WorldProjectStage stage,
        WorldProjectProgressionResult result)
    {
        if (stage.EffectsApplied)
            return;

        foreach (var effect in stage.Effects)
        {
            switch (effect.Type)
            {
                case WorldProjectEffectType.SetRoomDescription:
                    if (state.Rooms.TryGetValue(effect.TargetId, out var describedRoom) && effect.Value != null)
                        describedRoom.Description = effect.Value;
                    break;

                case WorldProjectEffectType.AppendRoomDescription:
                    if (state.Rooms.TryGetValue(effect.TargetId, out var appendedRoom) &&
                        !string.IsNullOrWhiteSpace(effect.Value) &&
                        !appendedRoom.Description.Contains(effect.Value, StringComparison.Ordinal))
                    {
                        appendedRoom.Description = $"{appendedRoom.Description.TrimEnd()} {effect.Value}".Trim();
                    }
                    break;

                case WorldProjectEffectType.EnableExit:
                    SetExitAvailability(state, effect, true);
                    break;

                case WorldProjectEffectType.DisableExit:
                    SetExitAvailability(state, effect, false);
                    break;

                case WorldProjectEffectType.SetRoomMetadata:
                    if (state.Rooms.TryGetValue(effect.TargetId, out var metadataRoom) &&
                        !string.IsNullOrWhiteSpace(effect.SecondaryTargetId))
                    {
                        metadataRoom.Metadata[effect.SecondaryTargetId] = effect.Value ?? string.Empty;
                    }
                    break;

                case WorldProjectEffectType.AddRoomResource:
                    AddRoomResource(state, effect);
                    break;

                case WorldProjectEffectType.AddItemToRoom:
                    AddItemToRoom(state, effect);
                    break;

                case WorldProjectEffectType.MoveNpc:
                    MoveNpc(state, effect.TargetId, effect.Value ?? effect.SecondaryTargetId);
                    break;

                case WorldProjectEffectType.SetNpcRole:
                    if (state.NPCs.TryGetValue(effect.TargetId, out var npc) &&
                        Enum.TryParse<NPCRole>(effect.Value, true, out var role))
                    {
                        npc.Role = role;
                    }
                    break;
            }
        }

        stage.EffectsApplied = true;
        if (stage.Effects.Count > 0)
            result.Messages.Add($"The world changed as {project.Name} completed {stage.Name}.");
    }

    private static void SetExitAvailability(GameState state, WorldProjectEffect effect, bool isAvailable)
    {
        if (!state.Rooms.TryGetValue(effect.TargetId, out var room) ||
            string.IsNullOrWhiteSpace(effect.SecondaryTargetId))
        {
            return;
        }

        var exit = room.Exits.FirstOrDefault(pair =>
                pair.Key.Equals(effect.SecondaryTargetId, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.Id.Equals(effect.SecondaryTargetId, StringComparison.OrdinalIgnoreCase) ||
                pair.Value.DisplayName.Equals(effect.SecondaryTargetId, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (exit == null)
            return;

        exit.IsAvailable = isAvailable;
        exit.UnavailableReason = isAvailable
            ? null
            : effect.Data.GetValueOrDefault("reason", "This route is currently unavailable.");
    }

    private static void AddRoomResource(GameState state, WorldProjectEffect effect)
    {
        if (!state.Rooms.TryGetValue(effect.TargetId, out var room))
            return;

        var itemId = effect.SecondaryTargetId ?? effect.Value;
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        room.Resources ??= new RoomResources();
        var resource = room.Resources.Resources.FirstOrDefault(candidate =>
            candidate.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (resource == null)
        {
            resource = new GatherableResource
            {
                ItemId = itemId,
                DisplayName = itemId
            };
            room.Resources.Resources.Add(resource);
        }

        resource.DisplayName = effect.Data.GetValueOrDefault("displayName", resource.DisplayName ?? itemId);
        resource.MinQuantity = GetInt(effect.Data, "minQuantity", resource.MinQuantity);
        resource.MaxQuantity = GetInt(effect.Data, "maxQuantity", Math.Max(resource.MaxQuantity, effect.Quantity));
        resource.FindChance = GetInt(effect.Data, "findChance", resource.FindChance);
        resource.Renewable = GetBool(effect.Data, "renewable", resource.Renewable);
        resource.RespawnTurns = GetNullableInt(effect.Data, "respawnTurns") ?? resource.RespawnTurns;
        if (effect.Data.TryGetValue("requiredTool", out var requiredTool))
            resource.RequiredTool = requiredTool;
        resource.GatherVerb = effect.Data.GetValueOrDefault("gatherVerb", resource.GatherVerb);
        if (effect.Data.TryGetValue("relatedSkill", out var relatedSkill))
            resource.RelatedSkill = relatedSkill;
        resource.Difficulty = GetInt(effect.Data, "difficulty", resource.Difficulty);

        room.Resources.DepletedResources.Remove(itemId);
    }

    private static void AddItemToRoom(GameState state, WorldProjectEffect effect)
    {
        if (!state.Rooms.TryGetValue(effect.TargetId, out var room))
            return;

        var itemId = effect.SecondaryTargetId ?? effect.Value;
        if (string.IsNullOrWhiteSpace(itemId))
            return;

        var itemType = Enum.TryParse<ItemType>(effect.Data.GetValueOrDefault("type"), true, out var parsedType)
            ? parsedType
            : ItemType.Miscellaneous;
        for (var index = 0; index < Math.Max(1, effect.Quantity); index++)
        {
            room.Items.Add(new Item
            {
                Id = itemId,
                Name = effect.Data.GetValueOrDefault("name", itemId),
                Description = effect.Data.GetValueOrDefault("description", string.Empty),
                Type = itemType,
                Weight = GetInt(effect.Data, "weight", 0),
                Value = GetInt(effect.Data, "value", 0),
                Stackable = GetBool(effect.Data, "stackable", true)
            });
        }
    }

    private static void MoveNpc(GameState state, string npcId, string? destinationRoomId)
    {
        if (string.IsNullOrWhiteSpace(destinationRoomId) ||
            !state.NPCs.TryGetValue(npcId, out var npc) ||
            !state.Rooms.TryGetValue(destinationRoomId, out var destination))
        {
            return;
        }

        foreach (var room in state.Rooms.Values)
            room.NPCIds.RemoveAll(id => id.Equals(npcId, StringComparison.OrdinalIgnoreCase));
        npc.CurrentRoomId = destinationRoomId;
        if (!destination.NPCIds.Contains(npcId, StringComparer.OrdinalIgnoreCase))
            destination.NPCIds.Add(npcId);
    }

    private static void StartProject(
        GameState state,
        WorldProject project,
        WorldProjectProgressionResult result,
        int progressionTurn)
    {
        project.Status = WorldProjectStatus.Active;
        project.StartedTurn ??= progressionTurn;
        result.ChangedProjectIds.Add(project.Id);
        result.Messages.Add($"World project started: {project.Name}.");
    }

    private static void CompleteProject(
        GameState state,
        WorldProject project,
        WorldProjectProgressionResult result,
        int progressionTurn)
    {
        if (project.Status == WorldProjectStatus.Completed)
            return;

        project.Status = WorldProjectStatus.Completed;
        project.CompletedTurn = progressionTurn;
        result.ChangedProjectIds.Add(project.Id);
        result.CompletedProjectIds.Add(project.Id);

        var message = $"World project completed: {project.Name}.";
        result.Messages.Add(message);
        var completionEvent = WorldEvent.Create(
            WorldEventType.ProjectCompleted,
            project.Id,
            roomId: state.CurrentRoomId,
            message: message);
        completionEvent.TurnNumber = progressionTurn;
        RecordEvent(state, result, completionEvent, "project");
    }

    private static void RecordEvent(
        GameState state,
        WorldProjectProgressionResult result,
        WorldEvent worldEvent,
        string source)
    {
        WorldEventJournal.Record(state, worldEvent, source, MaxRecentEvents);
        result.Events.Add(worldEvent);
    }

    private static WorldProject? FindProject(GameState state, string projectId) =>
        state.WorldProjects.FirstOrDefault(project =>
            project.Id.Equals(projectId, StringComparison.OrdinalIgnoreCase));

    private static bool IsProgressable(WorldProject project) =>
        project.Status is WorldProjectStatus.Available or WorldProjectStatus.Active;

    private static int GetInt(Dictionary<string, string> data, string key, int fallback) =>
        data.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static int? GetNullableInt(Dictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;

    private static bool GetBool(Dictionary<string, string> data, string key, bool fallback) =>
        data.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
}

public class WorldProjectProgressionResult
{
    public HashSet<string> ChangedProjectIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CompletedProjectIds { get; set; } = new();
    public List<WorldEvent> Events { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}
