using CSharpRPGBackend.Core;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class WorldSimulationTests
{
    [Fact]
    public void EventJournal_KeepsGeneratingUniqueIdsAfterSameTurnEventsAreTrimmed()
    {
        var state = new GameState { TurnNumber = 7, WorldSeed = 42 };
        var recorded = new List<WorldEvent>();

        for (var index = 0; index < 4; index++)
        {
            var worldEvent = WorldEvent.Create(WorldEventType.Custom, $"event-{index}");
            WorldEventJournal.Record(state, worldEvent, maxRecentEvents: 2);
            recorded.Add(worldEvent);
        }

        Assert.Equal(2, state.RecentWorldEvents.Count);
        Assert.Equal(4, recorded.Select(worldEvent => worldEvent.Id).Distinct().Count());
        Assert.Equal(new[] { "event-2", "event-3" },
            state.RecentWorldEvents.Select(worldEvent => worldEvent.TargetId));
    }

    [Fact]
    public void AdvanceTurn_MovesPatrolAndRespawnsOnlyRenewableResources()
    {
        var state = new GameState
        {
            Rooms = new Dictionary<string, Room>
            {
                ["a"] = new()
                {
                    Id = "a",
                    Name = "Ash Grove",
                    NPCIds = new() { "warden" },
                    Resources = new RoomResources
                    {
                        Resources = new()
                        {
                            new GatherableResource
                            {
                                ItemId = "moonpetal",
                                DisplayName = "Moonpetal",
                                Renewable = true,
                                RespawnTurns = 2
                            },
                            new GatherableResource
                            {
                                ItemId = "fallen_star",
                                DisplayName = "Fallen Star",
                                Renewable = false
                            }
                        },
                        DepletedResources = new()
                        {
                            ["moonpetal"] = 2,
                            ["fallen_star"] = -1
                        }
                    }
                },
                ["b"] = new() { Id = "b", Name = "Birch Crossing" }
            },
            NPCs = new Dictionary<string, Character>
            {
                ["warden"] = new()
                {
                    Id = "warden",
                    Name = "The Warden",
                    Health = 20,
                    MaxHealth = 20,
                    CurrentRoomId = "a",
                    CanMove = true,
                    PatrolRoomIds = new() { "a", "b" },
                    PatrolIntervalTurns = 2
                }
            },
            Companions = new(),
            RecentWorldEvents = new(),
            TurnNumber = 0,
            WorldSeed = 77
        };
        var simulation = new WorldSimulationService();

        var firstTurn = simulation.AdvanceTurn(state);

        Assert.Equal(1, firstTurn.TurnNumber);
        Assert.Equal(1, state.Rooms["a"].Resources!.DepletedResources["moonpetal"]);
        Assert.Equal(-1, state.Rooms["a"].Resources!.DepletedResources["fallen_star"]);
        Assert.Equal("a", state.NPCs["warden"].CurrentRoomId);

        var secondTurn = simulation.AdvanceTurn(state);

        Assert.Equal(2, secondTurn.TurnNumber);
        Assert.False(state.Rooms["a"].Resources!.DepletedResources.ContainsKey("moonpetal"));
        Assert.Equal(-1, state.Rooms["a"].Resources!.DepletedResources["fallen_star"]);
        Assert.Equal("b", state.NPCs["warden"].CurrentRoomId);
        Assert.DoesNotContain("warden", state.Rooms["a"].NPCIds);
        Assert.Contains("warden", state.Rooms["b"].NPCIds);
        Assert.Contains(secondTurn.Events, worldEvent =>
            worldEvent.Type == WorldEventType.ResourceRespawned && worldEvent.TargetId == "moonpetal");
        Assert.Contains(secondTurn.Events, worldEvent =>
            worldEvent.Type == WorldEventType.NpcMoved && worldEvent.ActorId == "warden");
        Assert.All(state.RecentWorldEvents, worldEvent => Assert.False(string.IsNullOrWhiteSpace(worldEvent.Id)));
    }

    [Fact]
    public void AdvanceTurn_DoesNotMoveNpcEngagedWithPlayer()
    {
        var state = new GameState
        {
            CurrentRoomId = "a",
            Rooms = new()
            {
                ["a"] = new Room { Id = "a", Name = "A", NPCIds = new() { "guide" } },
                ["b"] = new Room { Id = "b", Name = "B" }
            },
            NPCs = new()
            {
                ["guide"] = new Character
                {
                    Id = "guide",
                    Name = "Guide",
                    Health = 10,
                    MaxHealth = 10,
                    CurrentRoomId = "a",
                    CanMove = true,
                    PatrolRoomIds = new() { "a", "b" },
                    PatrolIntervalTurns = 1
                }
            },
            InChatMode = true,
            CurrentChatNpcId = "guide"
        };

        var result = new WorldSimulationService().AdvanceTurn(state);

        Assert.Equal("a", state.NPCs["guide"].CurrentRoomId);
        Assert.Contains("guide", state.Rooms["a"].NPCIds);
        Assert.DoesNotContain(result.Events, worldEvent => worldEvent.Type == WorldEventType.NpcMoved);
    }
}
