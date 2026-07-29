using CSharpRPGBackend.Core;
using CSharpRPGBackend.Games;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class WorldProjectTests
{
    [Fact]
    public void RavensholmProject_ReclaimsMineAndReopensForgeAfterContribution()
    {
        var game = FantasyQuest.Create();
        var state = GameStateFactory.Create(game, worldSeed: 31415);
        var projects = new WorldProjectService();
        var project = state.WorldProjects.Single(candidate => candidate.Id == "ravensholm_forgeworks");
        var forgeGate = state.Rooms["town_square"].Exits.Values.Single(exit => exit.Id == "forgeworks_gate");

        Assert.False(forgeGate.IsAvailable);

        var secureMineResult = projects.Process(
            state,
            new[] { WorldEvent.Create(WorldEventType.NpcDefeated, "goblin_king") });

        Assert.Equal(WorldProjectStatus.Active, project.Status);
        Assert.Equal(1, project.CurrentStageIndex);
        Assert.Equal("supply_forge", project.CurrentStage!.Id);
        Assert.Equal("1", state.Rooms["goblin_cave"].Metadata["danger_level"].ToString());
        Assert.Contains("reclaimed cave", state.Rooms["goblin_cave"].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ravensholm_forgeworks", secureMineResult.ChangedProjectIds);
        Assert.False(forgeGate.IsAvailable);

        Assert.True(state.PlayerInventory.AddItem(game.Items["iron_ore"].CloneRuntime(), 3));
        var contribution = projects.Contribute(
            state,
            project.Id,
            WorldProjectRequirementType.Item,
            "iron_ore",
            3);

        Assert.Equal(WorldProjectStatus.Completed, project.Status);
        Assert.Equal(state.TurnNumber, project.CompletedTurn);
        Assert.Contains(project.Id, contribution.CompletedProjectIds);
        Assert.False(state.PlayerInventory.Items.ContainsKey("iron_ore"));
        Assert.True(forgeGate.IsAvailable);
        Assert.Null(forgeGate.UnavailableReason);
        Assert.Equal("thriving", state.Rooms["forge_district"].Metadata["district_state"].ToString());
        Assert.Contains("thunder with new life", state.Rooms["forge_district"].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("smoke rises", state.Rooms["town_square"].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("forge_district", state.NPCs["blacksmith"].CurrentRoomId);
        Assert.Contains("blacksmith", state.Rooms["forge_district"].NPCIds);
        Assert.Single(contribution.Events, worldEvent => worldEvent.Type == WorldEventType.ProjectContributed);
        Assert.Single(contribution.Events, worldEvent => worldEvent.Type == WorldEventType.ProjectCompleted);
    }
}
