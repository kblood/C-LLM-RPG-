using CSharpRPGBackend.Core;
using CSharpRPGBackend.Services;

namespace CSharpRPGBackend.Tests;

public class QuestProgressionTests
{
    [Fact]
    public void Process_ItemRequirementMatchesAndConsumesInventoryIdsCaseInsensitively()
    {
        var game = new Game
        {
            Id = "case_insensitive_quest",
            StartingRoomId = "camp",
            Rooms = new Dictionary<string, Room>
            {
                ["camp"] = new() { Id = "camp", Name = "Camp" }
            },
            Items = new Dictionary<string, Item>
            {
                ["moon_herb"] = new()
                {
                    Id = "moon_herb",
                    Name = "Moon Herb",
                    Type = ItemType.QuestItem,
                    Stackable = true
                },
                ["healer_token"] = new()
                {
                    Id = "healer_token",
                    Name = "Healer Token",
                    Type = ItemType.Treasure,
                    Stackable = true
                }
            },
            Quests = new()
            {
                new Quest
                {
                    Id = "herbal_remedy",
                    Title = "Herbal Remedy",
                    Status = QuestStatus.Accepted,
                    Requirements = new()
                    {
                        new QuestRequirement
                        {
                            Type = "item",
                            TargetId = "MOON_HERB",
                            Quantity = 2,
                            ConsumedOnCompletion = true
                        }
                    },
                    Rewards = new QuestRewards
                    {
                        Items = new() { ("HEALER_TOKEN", 1) }
                    }
                }
            }
        };
        var state = GameStateFactory.Create(game);
        Assert.True(state.PlayerInventory.AddItem(game.Items["moon_herb"].CloneRuntime(), 2));

        var result = new QuestProgressionService().Process(
            state,
            game,
            new[] { WorldEvent.Create(WorldEventType.ItemAcquired, "moon_herb", quantity: 2) });

        Assert.Equal(QuestStatus.Completed, state.ActiveQuests.Single().Status);
        Assert.False(state.PlayerInventory.Items.ContainsKey("moon_herb"));
        Assert.Equal("Healer Token", state.PlayerInventory.Items["healer_token"].Item.Name);
        Assert.Contains("herbal_remedy", result.CompletedQuestIds);
    }

    [Fact]
    public void Process_CompletesQuestAndGrantsRewardsExactlyOnce()
    {
        var game = new Game
        {
            Id = "quest_test",
            StartingRoomId = "camp",
            Rooms = new Dictionary<string, Room>
            {
                ["camp"] = new() { Id = "camp", Name = "Camp" }
            },
            Items = new Dictionary<string, Item>
            {
                ["hunter_token"] = new()
                {
                    Id = "hunter_token",
                    Name = "Hunter Token",
                    Type = ItemType.Treasure,
                    Weight = 1,
                    Stackable = true
                }
            },
            Quests = new()
            {
                new Quest
                {
                    Id = "wolf_hunt",
                    Title = "Cull the Pack",
                    Status = QuestStatus.Accepted,
                    Requirements = new()
                    {
                        new QuestRequirement
                        {
                            Type = "kill",
                            TargetId = "dire_wolf",
                            Quantity = 2
                        }
                    },
                    Rewards = new QuestRewards
                    {
                        Experience = 40,
                        Currency = 25,
                        Items = new() { ("hunter_token", 2) },
                        ReputationChanges = new() { ["rangers"] = 3 },
                        RecipesLearned = new() { "wolfskin_cloak" }
                    }
                }
            }
        };
        var state = GameStateFactory.Create(game);
        var progression = new QuestProgressionService();
        var defeatedWolves = WorldEvent.Create(WorldEventType.NpcDefeated, "dire_wolf", quantity: 2);

        var firstResult = progression.Process(state, game, new[] { defeatedWolves });

        Assert.Equal(QuestStatus.Completed, state.ActiveQuests.Single().Status);
        Assert.Equal(2, state.ActiveQuests.Single().Requirements.Single().CurrentProgress);
        Assert.Equal(40, state.Player.Experience);
        Assert.Equal(25, state.Player.Wallet.TotalBaseUnits);
        Assert.Equal(2, state.PlayerInventory.Items["hunter_token"].Quantity);
        Assert.Equal(3, state.Player.Reputation["rangers"]);
        Assert.Contains("wolfskin_cloak", state.Player.KnownRecipes);
        Assert.Equal(new[] { "wolf_hunt" }, firstResult.CompletedQuestIds);
        Assert.Single(firstResult.Events, worldEvent => worldEvent.Type == WorldEventType.QuestCompleted);

        var secondResult = progression.Process(state, game, new[] { defeatedWolves });

        Assert.Empty(secondResult.CompletedQuestIds);
        Assert.Equal(40, state.Player.Experience);
        Assert.Equal(25, state.Player.Wallet.TotalBaseUnits);
        Assert.Equal(2, state.PlayerInventory.Items["hunter_token"].Quantity);
        Assert.Equal(3, state.Player.Reputation["rangers"]);
        Assert.Single(state.RecentWorldEvents, worldEvent => worldEvent.Type == WorldEventType.QuestCompleted);
    }
}
