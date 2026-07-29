using CSharpRPGBackend.Core;
using CSharpRPGBackend.Games;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class GameSaveServiceTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsCompleteLivingWorldState()
    {
        var game = FantasyQuest.Create();
        var state = GameStateFactory.Create(game, worldSeed: 4242, playerName: "Sable");
        state.TurnNumber = 17;
        state.CurrentRoomId = "goblin_cave";
        state.Player.CurrentRoomId = "goblin_cave";
        state.NPCs["blacksmith"].CurrentRoomId = "forge_district";
        state.Rooms["goblin_cave"].Resources!.DepletedResources["iron_ore"] = 2;
        state.ActiveQuests[0].Requirements[0].CurrentProgress = 1;
        state.WorldProjects[0].Status = WorldProjectStatus.Active;
        state.WorldProjects[0].Stages[0].Requirements[0].CurrentAmount = 1;
        state.PlayerInventory.Items["health_potion"].Item.ConsumableUsesRemaining = 1;
        state.RecentWorldEvents.Add(new WorldEvent
        {
            Id = "action-4242-17-1",
            Type = WorldEventType.NpcDefeated,
            TurnNumber = 17,
            ActorId = "player",
            TargetId = "goblin_king",
            RoomId = "goblin_cave",
            Message = "King Gruk fell.",
            Data = new() { ["weapon"] = "iron_sword" }
        });
        var expectedWeight = state.PlayerInventory.CurrentWeight;
        var saves = new GameSaveService();

        var json = saves.Serialize(
            state,
            game.Id,
            new Dictionary<string, string> { ["slot"] = "autosave" });
        var loaded = saves.Deserialize(json);

        Assert.Equal(GameSaveService.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(game.Id, loaded.GameId);
        Assert.Equal("autosave", loaded.Metadata["slot"]);
        Assert.Equal(17, loaded.State.TurnNumber);
        Assert.Equal(4242, loaded.State.WorldSeed);
        Assert.Equal("goblin_cave", loaded.State.CurrentRoomId);
        Assert.Equal("Sable", loaded.State.Player.Name);
        Assert.Equal("forge_district", loaded.State.NPCs["blacksmith"].CurrentRoomId);
        Assert.Equal(2, loaded.State.Rooms["goblin_cave"].Resources!.DepletedResources["iron_ore"]);
        Assert.Equal(1, loaded.State.ActiveQuests[0].Requirements[0].CurrentProgress);
        Assert.Equal(WorldProjectStatus.Active, loaded.State.WorldProjects[0].Status);
        Assert.Equal(1, loaded.State.WorldProjects[0].Stages[0].Requirements[0].CurrentAmount);
        Assert.Equal(1, loaded.State.PlayerInventory.Items["health_potion"].Item.ConsumableUsesRemaining);
        Assert.Equal(expectedWeight, loaded.State.PlayerInventory.CurrentWeight);
        Assert.Equal("iron_sword", loaded.State.RecentWorldEvents.Single().Data["weapon"]);
        Assert.NotSame(state, loaded.State);
        Assert.NotSame(state.Rooms["goblin_cave"], loaded.State.Rooms["goblin_cave"]);
    }
}
