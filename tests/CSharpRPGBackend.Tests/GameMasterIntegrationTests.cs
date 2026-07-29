using CSharpRPGBackend.Core;
using CSharpRPGBackend.Games;
using CSharpRPGBackend.LLM;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class GameMasterIntegrationTests
{
    [Fact]
    public async Task ExecuteActionPlans_SuccessfulMoveConsumesExactlyOneTurnAndLookConsumesNone()
    {
        var game = CreateTwoRoomGame();
        var state = GameStateFactory.Create(game);
        var llm = new FakeLlmClient();
        var gameMaster = new GameMaster(state, llm, game: game);

        var move = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "move", Target = "North Road" } },
            "Walk north");

        Assert.True(move.ActionResults.Single().success);
        Assert.Equal("north", state.CurrentRoomId);
        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(1, llm.ChatCalls);

        var look = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "look" } },
            "Look around");

        Assert.True(look.ActionResults.Single().success);
        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(1, llm.ChatCalls);
        Assert.Contains("quiet northern room", look.Response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteActionPlans_TakeAndDropPreserveFloorItemAsRuntimeState()
    {
        var floorDefinition = new Item
        {
            Id = "clockwork_orb",
            Name = "Clockwork Orb",
            Description = "A delicate brass puzzle.",
            Type = ItemType.Miscellaneous,
            Weight = 2,
            Value = 19,
            IsConsumable = true,
            ConsumableUsesRemaining = 3,
            ConsumableEffects = new() { ["focus"] = 2 },
            CustomProperties = new() { ["maker"] = "Ilyra" }
        };
        var game = CreateTwoRoomGame();
        game.Rooms["start"].Items.Add(floorDefinition);
        var state = GameStateFactory.Create(game);
        var gameMaster = new GameMaster(state, new FakeLlmClient(), game: game);

        var take = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "take", Target = "clockwork orb" } },
            "Take the orb");

        Assert.True(take.ActionResults.Single().success);
        Assert.Empty(state.Rooms["start"].Items);
        var carried = state.PlayerInventory.Items["clockwork_orb"].Item;
        Assert.NotSame(floorDefinition, carried);
        Assert.Equal(3, carried.ConsumableUsesRemaining);
        Assert.Equal(2, carried.ConsumableEffects["focus"]);

        var drop = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "drop", Target = "clockwork orb" } },
            "Put the orb down");

        Assert.True(drop.ActionResults.Single().success);
        Assert.False(state.PlayerInventory.Items.ContainsKey("clockwork_orb"));
        var dropped = Assert.Single(state.Rooms["start"].Items);
        Assert.NotSame(carried, dropped);
        Assert.Equal("clockwork_orb", dropped.Id);
        Assert.Equal("Clockwork Orb", dropped.Name);
        Assert.Equal(3, dropped.ConsumableUsesRemaining);
        Assert.Equal(2, dropped.ConsumableEffects["focus"]);
        Assert.Equal("Ilyra", dropped.CustomProperties["maker"]);
    }

    [Fact]
    public async Task ExecuteActionPlans_LockedExitRejectsWithoutKeyThenUnlocksWithKey()
    {
        var game = CreateTwoRoomGame();
        var lockedExit = game.Rooms["start"].Exits["north"];
        lockedExit.Id = "sealed_gate";
        lockedExit.IsAvailable = false;
        lockedExit.UnavailableReason = "The gate is sealed.";
        lockedExit.RequiredKeyId = "gate_key";
        game.Items["gate_key"] = new Item
        {
            Id = "gate_key",
            Name = "Gate Key",
            Type = ItemType.Key,
            IsKey = true,
            UnlocksId = "sealed_gate",
            Weight = 1
        };
        var state = GameStateFactory.Create(game);
        var gameMaster = new GameMaster(state, new FakeLlmClient(), game: game);

        var rejected = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "move", Target = "North Road" } },
            "Try the sealed gate");

        Assert.False(rejected.ActionResults.Single().success);
        Assert.Equal("start", state.CurrentRoomId);
        Assert.False(state.Rooms["start"].Exits["north"].IsAvailable);
        Assert.Equal("gate_key", state.Rooms["start"].Exits["north"].RequiredKeyId);

        Assert.True(state.PlayerInventory.AddItem(game.Items["gate_key"].CloneRuntime()));
        var unlocked = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "move", Target = "North Road" } },
            "Unlock the gate and go north");

        Assert.True(unlocked.ActionResults.Single().success);
        Assert.Equal("north", state.CurrentRoomId);
        Assert.True(state.Rooms["start"].Exits["north"].IsAvailable);
        Assert.Null(state.Rooms["start"].Exits["north"].UnavailableReason);
        Assert.Null(state.Rooms["start"].Exits["north"].RequiredKeyId);
        Assert.True(state.PlayerInventory.Items.ContainsKey("gate_key"));
    }

    [Fact]
    public async Task ExecuteActionPlans_NarrationFailureStillCommitsAndReturnsAuthoritativeResult()
    {
        var game = CreateTwoRoomGame();
        game.Rooms["start"].Items.Add(new Item
        {
            Id = "silver_bell",
            Name = "Silver Bell",
            Weight = 1
        });
        var state = GameStateFactory.Create(game);
        var gameMaster = new GameMaster(state, new ThrowingLlmClient(), game: game);

        var response = await gameMaster.ExecuteActionPlansAsync(
            new() { new ActionPlan { Action = "take", Target = "silver bell" } },
            "Take the silver bell");

        Assert.True(response.ActionResults.Single().success);
        Assert.Contains("take Silver Bell", response.Response, StringComparison.OrdinalIgnoreCase);
        Assert.True(state.PlayerInventory.Items.ContainsKey("silver_bell"));
        Assert.Empty(state.Rooms["start"].Items);
        Assert.Equal(1, state.TurnNumber);
    }

    [Fact]
    public async Task ExecuteActionPlans_ClassifiesInventoryGainsPerConcreteAction()
    {
        var game = CreateGatheringGame();
        game.Rooms["start"].Items.Add(new Item { Id = "relic", Name = "Old Relic" });
        var state = GameStateFactory.Create(game, worldSeed: 73);
        var gameMaster = new GameMaster(state, new FakeLlmClient(), game: game);

        await gameMaster.ExecuteActionPlansAsync(
            new()
            {
                new ActionPlan { Action = "gather", Target = "iron ore" },
                new ActionPlan { Action = "take", Target = "old relic" }
            },
            "Mine some ore, then take the relic");

        Assert.Contains(state.RecentWorldEvents, worldEvent =>
            worldEvent.Type == WorldEventType.ItemGathered && worldEvent.TargetId == "iron_ore");
        Assert.Contains(state.RecentWorldEvents, worldEvent =>
            worldEvent.Type == WorldEventType.ItemAcquired && worldEvent.TargetId == "relic");
        Assert.DoesNotContain(state.RecentWorldEvents, worldEvent =>
            worldEvent.Type == WorldEventType.ItemGathered && worldEvent.TargetId == "relic");
    }

    [Fact]
    public async Task ExecuteActionPlans_GatherRollReplaysFromTheSameSavedState()
    {
        var game = CreateGatheringGame();
        var original = GameStateFactory.Create(game, worldSeed: 9182);
        var saves = new GameSaveService();
        var restored = saves.Deserialize(saves.Serialize(original, game.Id)).State;
        var plan = new List<ActionPlan> { new() { Action = "gather", Target = "iron ore" } };

        var first = await new GameMaster(original, new FakeLlmClient(), game: game)
            .ExecuteActionPlansAsync(plan, "Mine iron ore");
        var replay = await new GameMaster(restored, new FakeLlmClient(), game: game)
            .ExecuteActionPlansAsync(plan, "Mine iron ore");

        Assert.Equal(first.ActionResults.Single().success, replay.ActionResults.Single().success);
        Assert.Equal(first.ActionResults.Single().message, replay.ActionResults.Single().message);
        Assert.Equal(
            original.PlayerInventory.GetItem("iron_ore")?.Quantity,
            restored.PlayerInventory.GetItem("iron_ore")?.Quantity);
    }

    [Fact]
    public async Task ExecuteActionPlans_ContributionUsesTheTurnItCompletesOn()
    {
        var game = FantasyQuest.Create();
        var state = GameStateFactory.Create(game);
        state.TurnNumber = 5;
        var project = Assert.Single(state.WorldProjects);
        new WorldProjectService().Process(
            state,
            new[] { WorldEvent.Create(WorldEventType.NpcDefeated, "goblin_king") });
        Assert.True(state.PlayerInventory.AddItem(game.Items["iron_ore"].CloneRuntime(), 3));

        var gameMaster = new GameMaster(state, new FakeLlmClient(), game: game);
        var response = await gameMaster.ExecuteActionPlansAsync(
            new()
            {
                new ActionPlan
                {
                    Action = "contribute",
                    Target = "ravensholm_forgeworks",
                    Details = "3 iron ore"
                }
            },
            "Contribute 3 iron ore to the forgeworks");

        Assert.True(response.ActionResults.Single().success);
        Assert.Equal(6, state.TurnNumber);
        Assert.Equal(5, project.Stages[0].CompletedTurn);
        Assert.Equal(6, project.Stages[1].CompletedTurn);
        Assert.Equal(6, project.CompletedTurn);
    }

    private static Game CreateTwoRoomGame()
    {
        return new Game
        {
            Id = "game_master_test",
            StartingRoomId = "start",
            InitialPlayerHealth = 30,
            Rooms = new Dictionary<string, Room>
            {
                ["start"] = new()
                {
                    Id = "start",
                    Name = "Starting Room",
                    Description = "A simple starting room.",
                    Exits = new()
                    {
                        ["north"] = new Exit("North Road", "north", "A road leading north")
                        {
                            Id = "north_road"
                        }
                    }
                },
                ["north"] = new()
                {
                    Id = "north",
                    Name = "Northern Room",
                    Description = "A quiet northern room."
                }
            }
        };
    }

    private static Game CreateGatheringGame()
    {
        var game = CreateTwoRoomGame();
        game.Items["iron_ore"] = new Item
        {
            Id = "iron_ore",
            Name = "Iron Ore",
            Type = ItemType.CraftingMaterial,
            Stackable = true
        };
        game.Rooms["start"].Resources = new RoomResources
        {
            Biome = "mine",
            ResourceTags = new() { "ore" },
            Resources = new()
            {
                new GatherableResource
                {
                    ItemId = "iron_ore",
                    DisplayName = "Iron Ore",
                    FindChance = 100,
                    MinQuantity = 1,
                    MaxQuantity = 3,
                    Renewable = false,
                    GatherVerb = "mine"
                }
            }
        };
        return game;
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        public int ChatCalls { get; private set; }
        public string BackendName => "Fake";
        public string DefaultModel => "fake-model";

        public Task<string> ChatAsync(List<ChatMessage> messages, string? model = null)
        {
            ChatCalls++;
            return Task.FromResult("The world answers your action.");
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(
            List<ChatMessage> messages,
            string? model = null)
        {
            await Task.CompletedTask;
            yield return "The world answers your action.";
        }

        public Task<bool> IsHealthyAsync() => Task.FromResult(true);

        public Task<List<string>> ListModelsAsync() =>
            Task.FromResult(new List<string> { DefaultModel });
    }

    private sealed class ThrowingLlmClient : ILlmClient
    {
        public string BackendName => "Unavailable";
        public string DefaultModel => "none";

        public Task<string> ChatAsync(List<ChatMessage> messages, string? model = null) =>
            Task.FromException<string>(new HttpRequestException("Provider unavailable"));

        public async IAsyncEnumerable<string> ChatStreamAsync(
            List<ChatMessage> messages,
            string? model = null)
        {
            await Task.Yield();
            throw new HttpRequestException("Provider unavailable");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<bool> IsHealthyAsync() => Task.FromResult(false);
        public Task<List<string>> ListModelsAsync() => Task.FromResult(new List<string>());
    }
}
