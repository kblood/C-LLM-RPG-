using CSharpRPGBackend.Core;
using CSharpRPGBackend.Games;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class FantasyQuestCampaignTests
{
    [Fact]
    public async Task FullCampaign_RestoresRavensholmSurvivesSaveReloadAndWinsTheMainQuest()
    {
        var game = FantasyQuest.Create();
        var state = GameStateFactory.Create(game, worldSeed: 24680, playerName: "Mira");

        // Keep this integration test focused on campaign state transitions rather
        // than balance. Combat itself still runs through the production service
        // with a seeded random source.
        state.Player.MaxHealth = 10_000;
        state.Player.Health = 10_000;
        state.Player.Strength = 100;
        state.Player.Agility = 100;

        var gameMaster = CreateGameMaster(state, game, combatSeed: 1001);
        var mainQuest = Assert.Single(state.ActiveQuests, quest => quest.Id == "dragon_quest");
        Assert.Equal(QuestStatus.Accepted, mainQuest.Status);

        await ExecuteSuccessfulAsync(gameMaster, "equip", "iron sword");
        await ExecuteSuccessfulAsync(gameMaster, "equip", "leather armor");
        await ExecuteSuccessfulAsync(gameMaster, "move", "North");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Deeper Into Forest");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Continue Deeper");

        await DefeatAsync(gameMaster, state.NPCs["goblin_king"], "King Gruk");

        var project = Assert.Single(state.WorldProjects, candidate =>
            candidate.Id == "ravensholm_forgeworks");
        Assert.Equal(WorldProjectStatus.Active, project.Status);
        Assert.Equal("supply_forge", project.CurrentStage?.Id);

        for (var attempts = 0;
             (state.PlayerInventory.GetItem("iron_ore")?.Quantity ?? 0) < 3 && attempts < 30;
             attempts++)
        {
            await ExecuteAsync(gameMaster, "gather", "iron ore");
        }

        Assert.True(state.PlayerInventory.GetItem("iron_ore")?.Quantity >= 3);
        await ExecuteSuccessfulAsync(
            gameMaster,
            "contribute",
            "ravensholm_forgeworks",
            "3 iron ore");

        Assert.Equal(WorldProjectStatus.Completed, project.Status);
        Assert.True(state.Rooms["town_square"].Exits["west_to_old_forgeworks"].IsAvailable);
        Assert.Equal("thriving", state.Rooms["forge_district"].Metadata["district_state"].ToString());
        Assert.Equal("forge_district", state.NPCs["blacksmith"].CurrentRoomId);

        // Checkpoint the restored town and prove that all mutable campaign state
        // survives a versioned save before continuing toward the main objective.
        var saveService = new GameSaveService();
        var saveJson = saveService.Serialize(state, game.Id);
        var save = saveService.Deserialize(saveJson);
        state = save.State;
        gameMaster = CreateGameMaster(state, game, combatSeed: 2002);

        project = Assert.Single(state.WorldProjects, candidate =>
            candidate.Id == "ravensholm_forgeworks");
        Assert.Equal(WorldProjectStatus.Completed, project.Status);
        Assert.True(state.Rooms["town_square"].Exits["west_to_old_forgeworks"].IsAvailable);
        Assert.Equal("forge_district", state.NPCs["blacksmith"].CurrentRoomId);

        await ExecuteSuccessfulAsync(gameMaster, "move", "Back To Forest");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Back To Entrance");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Up The Slope");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Higher Into Mountains");
        await ExecuteSuccessfulAsync(gameMaster, "move", "Into Dragon's Lair");
        await DefeatAsync(gameMaster, state.NPCs["dragon"], "Infernus");

        var victory = await ExecuteSuccessfulAsync(gameMaster, "take", "crown");

        Assert.True(victory.IsVictory);
        Assert.Contains("Crown of Amalion", victory.VictoryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(state.PlayerInventory.Items.ContainsKey("crown_of_amalion"));
        Assert.Equal(QuestStatus.Completed,
            Assert.Single(state.ActiveQuests, quest => quest.Id == "dragon_quest").Status);
        Assert.Contains(state.RecentWorldEvents, worldEvent =>
            worldEvent.Type == WorldEventType.ProjectCompleted &&
            worldEvent.TargetId == "ravensholm_forgeworks");
        Assert.Contains(state.RecentWorldEvents, worldEvent =>
            worldEvent.Type == WorldEventType.QuestCompleted &&
            worldEvent.TargetId == "dragon_quest");
    }

    private static GameMaster CreateGameMaster(GameState state, Game game, int combatSeed) =>
        new(
            state,
            new CampaignLlmClient(),
            game: game,
            combatService: new CombatService(new Random(combatSeed)));

    private static async Task DefeatAsync(
        GameMaster gameMaster,
        Character enemy,
        string target)
    {
        for (var attempts = 0; enemy.IsAlive && attempts < 20; attempts++)
            await ExecuteSuccessfulAsync(gameMaster, "attack", target);

        Assert.False(enemy.IsAlive);
    }

    private static async Task<AgentActionResponse> ExecuteSuccessfulAsync(
        GameMaster gameMaster,
        string action,
        string target = "",
        string details = "")
    {
        var response = await ExecuteAsync(gameMaster, action, target, details);
        Assert.True(response.ActionResults.Single().success,
            response.ActionResults.Single().message);
        return response;
    }

    private static Task<AgentActionResponse> ExecuteAsync(
        GameMaster gameMaster,
        string action,
        string target = "",
        string details = "") =>
        gameMaster.ExecuteActionPlansAsync(
            new List<ActionPlan>
            {
                new() { Action = action, Target = target, Details = details }
            },
            $"Campaign action: {action} {target} {details}".Trim());

    private sealed class CampaignLlmClient : ILlmClient
    {
        public string BackendName => "Campaign Test";
        public string DefaultModel => "deterministic";

        public Task<string> ChatAsync(List<ChatMessage> messages, string? model = null) =>
            Task.FromResult("The campaign continues according to the authoritative result.");

        public async IAsyncEnumerable<string> ChatStreamAsync(
            List<ChatMessage> messages,
            string? model = null)
        {
            await Task.CompletedTask;
            yield return "The campaign continues according to the authoritative result.";
        }

        public Task<bool> IsHealthyAsync() => Task.FromResult(true);

        public Task<List<string>> ListModelsAsync() =>
            Task.FromResult(new List<string> { DefaultModel });
    }
}
