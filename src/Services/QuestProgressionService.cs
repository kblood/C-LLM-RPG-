using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Services;

/// <summary>
/// Updates structured quest requirements from world events and grants rewards
/// exactly once when a quest becomes complete.
/// </summary>
public class QuestProgressionService
{
    private const int MaxRecentEvents = 50;

    public QuestProgressionResult Process(
        GameState state,
        Game? game,
        IEnumerable<WorldEvent>? events)
    {
        ArgumentNullException.ThrowIfNull(state);

        var inputEvents = (events ?? Enumerable.Empty<WorldEvent>()).ToList();
        var result = new QuestProgressionResult();

        foreach (var worldEvent in inputEvents)
            ApplyLifecycleEvent(state, game, worldEvent, result);

        foreach (var quest in state.ActiveQuests
                     .Where(IsTrackable)
                     .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
                     .ToList())
        {
            var questChanged = false;
            foreach (var worldEvent in inputEvents)
            {
                foreach (var requirement in quest.Requirements)
                {
                    if (requirement.IsMet || !Matches(requirement, worldEvent))
                        continue;

                    var previous = requirement.CurrentProgress;
                    requirement.CurrentProgress = Math.Min(
                        Math.Max(1, requirement.Quantity),
                        requirement.CurrentProgress + Math.Max(1, worldEvent.Quantity));
                    questChanged |= previous != requirement.CurrentProgress;
                }
            }

            // "item" requirements describe what the player currently carries,
            // unlike "gather" requirements which track cumulative collection.
            foreach (var requirement in quest.Requirements.Where(requirement =>
                         requirement.Type.Equals("item", StringComparison.OrdinalIgnoreCase)))
            {
                var inventoryEntry = FindInventoryEntry(state.PlayerInventory, requirement.TargetId);
                var inventoryQuantity = string.IsNullOrEmpty(inventoryEntry.Key)
                    ? 0
                    : inventoryEntry.Value.Quantity;
                var synchronizedProgress = Math.Min(Math.Max(1, requirement.Quantity), inventoryQuantity);
                if (requirement.CurrentProgress != synchronizedProgress)
                {
                    requirement.CurrentProgress = synchronizedProgress;
                    questChanged = true;
                }
            }

            if (questChanged && quest.Status == QuestStatus.Accepted)
                quest.Status = QuestStatus.InProgress;

            if (questChanged)
            {
                result.ChangedQuestIds.Add(quest.Id);
                result.Messages.Add($"Quest updated: {quest.Title}.");
            }

            if (quest.AreRequirementsMet(BuildProgress(quest)))
                CompleteQuest(state, game, quest, inputEvents, result);
        }

        return result;
    }

    private static void ApplyLifecycleEvent(
        GameState state,
        Game? game,
        WorldEvent worldEvent,
        QuestProgressionResult result)
    {
        var questId = GetEventTarget(worldEvent);
        if (string.IsNullOrWhiteSpace(questId))
            return;

        if (worldEvent.Type == WorldEventType.QuestAccepted)
        {
            var existing = state.ActiveQuests.FirstOrDefault(quest =>
                quest.Id.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var definition = game?.Quests.FirstOrDefault(quest =>
                    quest.Id.Equals(questId, StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                    return;

                existing = CloneQuestForAcceptance(definition);
                state.ActiveQuests.Add(existing);
                result.AcceptedQuestIds.Add(existing.Id);
                result.ChangedQuestIds.Add(existing.Id);
                result.Messages.Add($"Quest accepted: {existing.Title}.");
            }
            else if (existing.IsRepeatable &&
                     existing.Status is QuestStatus.Completed or QuestStatus.TurnedIn)
            {
                ResetQuest(existing);
                result.AcceptedQuestIds.Add(existing.Id);
                result.ChangedQuestIds.Add(existing.Id);
                result.Messages.Add($"Quest accepted again: {existing.Title}.");
            }
            else if (existing.Status == QuestStatus.Offered)
            {
                existing.Status = QuestStatus.Accepted;
                result.AcceptedQuestIds.Add(existing.Id);
                result.ChangedQuestIds.Add(existing.Id);
                result.Messages.Add($"Quest accepted: {existing.Title}.");
            }
        }
        else if (worldEvent.Type == WorldEventType.QuestCompleted)
        {
            var quest = state.ActiveQuests.FirstOrDefault(candidate =>
                candidate.Id.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (quest != null && IsTrackable(quest))
                CompleteQuest(state, game, quest, new[] { worldEvent }, result);
        }
        else if (worldEvent.Type == WorldEventType.QuestTurnedIn)
        {
            var quest = state.ActiveQuests.FirstOrDefault(candidate =>
                candidate.Id.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (quest?.Status == QuestStatus.Completed)
            {
                quest.Status = QuestStatus.TurnedIn;
                result.ChangedQuestIds.Add(quest.Id);
                result.Messages.Add($"Quest turned in: {quest.Title}.");
            }
        }
    }

    private static bool IsTrackable(Quest quest) =>
        quest.Status is QuestStatus.Accepted or QuestStatus.InProgress;

    private static bool Matches(QuestRequirement requirement, WorldEvent worldEvent)
    {
        var requirementType = requirement.Type.Trim().ToLowerInvariant();
        var eventTarget = GetRequirementTarget(worldEvent);
        if (!requirement.TargetId.Equals(eventTarget, StringComparison.OrdinalIgnoreCase))
            return false;

        return requirementType switch
        {
            "kill" => worldEvent.Type == WorldEventType.NpcDefeated,
            "location" => worldEvent.Type == WorldEventType.RoomEntered,
            "talk" => worldEvent.Type == WorldEventType.NpcTalkedTo,
            "gather" => worldEvent.Type == WorldEventType.ItemGathered,
            "craft" => worldEvent.Type == WorldEventType.ItemCrafted,
            "item" => worldEvent.Type is WorldEventType.ItemAcquired
                or WorldEventType.ItemGathered
                or WorldEventType.ItemCrafted,
            _ => worldEvent.Type == WorldEventType.Custom &&
                 worldEvent.Data.TryGetValue("requirementType", out var customType) &&
                 customType.Equals(requirement.Type, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? GetRequirementTarget(WorldEvent worldEvent)
    {
        return worldEvent.Type == WorldEventType.RoomEntered
            ? worldEvent.RoomId ?? worldEvent.TargetId
            : worldEvent.TargetId;
    }

    private static string? GetEventTarget(WorldEvent worldEvent)
    {
        if (!string.IsNullOrWhiteSpace(worldEvent.TargetId))
            return worldEvent.TargetId;

        return worldEvent.Data.TryGetValue("questId", out var questId) ? questId : null;
    }

    private static QuestProgress BuildProgress(Quest quest)
    {
        var progress = new QuestProgress { QuestId = quest.Id };
        for (var index = 0; index < quest.Requirements.Count; index++)
        {
            var requirement = quest.Requirements[index];
            progress.RequirementProgress[index] = requirement.CurrentProgress;

            switch (requirement.Type.Trim().ToLowerInvariant())
            {
                case "kill":
                    progress.KillCounts[requirement.TargetId] = requirement.CurrentProgress;
                    break;
                case "location" when requirement.IsMet:
                    progress.VisitedLocations.Add(requirement.TargetId);
                    break;
                case "item":
                case "gather":
                    progress.CollectedItems[requirement.TargetId] = requirement.CurrentProgress;
                    break;
            }
        }

        return progress;
    }

    private static void CompleteQuest(
        GameState state,
        Game? game,
        Quest quest,
        IReadOnlyCollection<WorldEvent> sourceEvents,
        QuestProgressionResult result)
    {
        if (!IsTrackable(quest))
            return;

        quest.Status = QuestStatus.Completed;
        quest.CompletedAt = sourceEvents
            .Where(worldEvent => worldEvent.OccurredAtUtc.HasValue)
            .Select(worldEvent => (DateTime?)worldEvent.OccurredAtUtc!.Value.UtcDateTime)
            .LastOrDefault();

        ConsumeRequiredItems(state, quest);
        GrantRewards(state, game, quest, result);
        result.CompletedQuestIds.Add(quest.Id);
        result.ChangedQuestIds.Add(quest.Id);

        var message = $"Quest completed: {quest.Title}.";
        result.Messages.Add(message);
        var completionEvent = WorldEvent.Create(
            WorldEventType.QuestCompleted,
            quest.Id,
            actorId: state.Player.Id,
            roomId: state.CurrentRoomId,
            message: message);
        completionEvent.TurnNumber = state.TurnNumber;
        WorldEventJournal.Record(state, completionEvent, "quest", MaxRecentEvents);
        result.Events.Add(completionEvent);
    }

    private static void ConsumeRequiredItems(GameState state, Quest quest)
    {
        foreach (var requirement in quest.Requirements.Where(requirement =>
                     requirement.ConsumedOnCompletion &&
                     requirement.Type.Equals("item", StringComparison.OrdinalIgnoreCase)))
        {
            var inventoryEntry = FindInventoryEntry(state.PlayerInventory, requirement.TargetId);
            if (!string.IsNullOrEmpty(inventoryEntry.Key))
                state.PlayerInventory.RemoveItem(inventoryEntry.Key, requirement.Quantity);
        }
    }

    private static void GrantRewards(
        GameState state,
        Game? game,
        Quest quest,
        QuestProgressionResult result)
    {
        if (quest.Rewards.Experience != 0)
            state.Player.GainExperience(quest.Rewards.Experience);

        if (quest.Rewards.Currency != 0)
            state.Player.Wallet.Add(quest.Rewards.Currency);

        foreach (var (itemId, quantity) in quest.Rewards.Items)
        {
            if (quantity <= 0)
                continue;

            var definition = game?.Items.FirstOrDefault(pair =>
                pair.Key.Equals(itemId, StringComparison.OrdinalIgnoreCase)).Value;
            var item = definition?.CloneRuntime() ?? new Item
                {
                    Id = itemId,
                    Name = itemId,
                    Type = ItemType.Miscellaneous,
                    Stackable = true
                };

            if (!state.PlayerInventory.AddItem(item, quantity) &&
                state.Rooms.TryGetValue(state.CurrentRoomId, out var room))
            {
                for (var index = 0; index < quantity; index++)
                    room.Items.Add(item.CloneRuntime());
                result.Messages.Add($"Your inventory is full; {quantity}x {item.Name} was placed nearby.");
            }
        }

        foreach (var reputationChange in quest.Rewards.ReputationChanges)
        {
            state.Player.Reputation[reputationChange.Key] =
                state.Player.Reputation.GetValueOrDefault(reputationChange.Key) + reputationChange.Value;
        }

        foreach (var recipeId in quest.Rewards.RecipesLearned)
        {
            if (!state.Player.KnownRecipes.Contains(recipeId, StringComparer.OrdinalIgnoreCase))
                state.Player.KnownRecipes.Add(recipeId);
        }
    }

    private static Quest CloneQuestForAcceptance(Quest source)
    {
        return new Quest
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description,
            GiverNpcId = source.GiverNpcId,
            Status = QuestStatus.Accepted,
            Type = source.Type,
            IsRepeatable = source.IsRepeatable,
            IsDynamic = source.IsDynamic,
            Requirements = source.Requirements.Select(requirement => new QuestRequirement
            {
                Type = requirement.Type,
                TargetId = requirement.TargetId,
                TargetName = requirement.TargetName,
                Quantity = requirement.Quantity,
                CurrentProgress = 0,
                ConsumedOnCompletion = requirement.ConsumedOnCompletion,
                Description = requirement.Description
            }).ToList(),
            Rewards = new QuestRewards
            {
                Experience = source.Rewards.Experience,
                Currency = source.Rewards.Currency,
                Items = source.Rewards.Items.ToList(),
                ReputationChanges = new Dictionary<string, int>(
                    source.Rewards.ReputationChanges,
                    StringComparer.OrdinalIgnoreCase),
                RecipesLearned = source.Rewards.RecipesLearned.ToList()
            },
            Objectives = source.Objectives.ToList(),
            CompletedObjectives = new List<string>(),
            CreatedAt = source.CreatedAt,
            RequestedItemId = source.RequestedItemId,
            RequestedQuantity = source.RequestedQuantity,
            OfferDialogue = source.OfferDialogue,
            CompletionDialogue = source.CompletionDialogue
        };
    }

    private static void ResetQuest(Quest quest)
    {
        quest.Status = QuestStatus.Accepted;
        quest.CompletedAt = null;
        quest.CompletedObjectives.Clear();
        foreach (var requirement in quest.Requirements)
            requirement.CurrentProgress = 0;
    }

    private static KeyValuePair<string, InventoryItem> FindInventoryEntry(
        Inventory inventory,
        string itemId) =>
        inventory.Items.FirstOrDefault(pair =>
            pair.Key.Equals(itemId, StringComparison.OrdinalIgnoreCase));

}

public class QuestProgressionResult
{
    public List<string> AcceptedQuestIds { get; set; } = new();
    public List<string> ChangedQuestIds { get; set; } = new();
    public List<string> CompletedQuestIds { get; set; } = new();
    public List<WorldEvent> Events { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}
