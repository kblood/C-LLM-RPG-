using CSharpRPGBackend.Core;
using CSharpRPGBackend.Games;

namespace CSharpRPGBackend.Tests;

public class GameStateFactoryTests
{
    [Theory]
    [InlineData("fantasy", "iron_sword", 1, "leather_armor", 1, "health_potion", 2)]
    [InlineData("scifi", "laser_pistol", 1, "combat_suit", 1, "stim_pack", 2)]
    public void Create_UsesOnlyTheExplicitStartingLoadout(
        string gameName,
        string firstItemId,
        int firstQuantity,
        string secondItemId,
        int secondQuantity,
        string thirdItemId,
        int thirdQuantity)
    {
        var game = gameName == "fantasy" ? FantasyQuest.Create() : SciFiAdventure.Create();

        var state = GameStateFactory.Create(game, worldSeed: 8675309, playerName: "Mira");

        Assert.Equal(3, state.PlayerInventory.Items.Count);
        Assert.Equal(firstQuantity, state.PlayerInventory.Items[firstItemId].Quantity);
        Assert.Equal(secondQuantity, state.PlayerInventory.Items[secondItemId].Quantity);
        Assert.Equal(thirdQuantity, state.PlayerInventory.Items[thirdItemId].Quantity);
        Assert.Equal(
            new[] { firstItemId, secondItemId, thirdItemId }.OrderBy(id => id),
            state.PlayerInventory.Items.Keys.OrderBy(id => id));
        Assert.True(state.Player.IsPlayer);
        Assert.Equal("Mira", state.Player.Name);
        Assert.Equal(game.StartingRoomId, state.Player.CurrentRoomId);
        Assert.Equal(8675309, state.WorldSeed);
    }

    [Fact]
    public void Create_DeepClonesMutableRuntimeStateAcrossSessions()
    {
        var game = FantasyQuest.Create();
        var first = GameStateFactory.Create(game, worldSeed: 11);
        var second = GameStateFactory.Create(game, worldSeed: 22);

        var originalTownDescription = game.Rooms["town_square"].Description;
        var originalBlacksmithHealth = game.NPCs["blacksmith"].Health;
        var originalPotionUses = game.Items["health_potion"].ConsumableUsesRemaining;

        first.Rooms["town_square"].Description = "Changed in the first session";
        first.Rooms["town_square"].Exits.Values.First().IsAvailable = false;
        first.Rooms["goblin_cave"].Resources!.DepletedResources["iron_ore"] = 7;
        first.NPCs["blacksmith"].Health = 0;
        first.PlayerInventory.Items["health_potion"].Item.ConsumableUsesRemaining = 0;
        first.ActiveQuests[0].Requirements[0].CurrentProgress = 1;
        first.WorldProjects[0].Stages[0].Requirements[0].CurrentAmount = 1;

        Assert.Equal(originalTownDescription, game.Rooms["town_square"].Description);
        Assert.Equal(originalTownDescription, second.Rooms["town_square"].Description);
        Assert.True(game.Rooms["town_square"].Exits.Values.First().IsAvailable);
        Assert.True(second.Rooms["town_square"].Exits.Values.First().IsAvailable);
        Assert.Empty(game.Rooms["goblin_cave"].Resources!.DepletedResources);
        Assert.Empty(second.Rooms["goblin_cave"].Resources!.DepletedResources);
        Assert.Equal(originalBlacksmithHealth, game.NPCs["blacksmith"].Health);
        Assert.Equal(originalBlacksmithHealth, second.NPCs["blacksmith"].Health);
        Assert.Equal(originalPotionUses, game.Items["health_potion"].ConsumableUsesRemaining);
        Assert.Equal(originalPotionUses, second.PlayerInventory.Items["health_potion"].Item.ConsumableUsesRemaining);
        Assert.Equal(0, game.Quests[0].Requirements[0].CurrentProgress);
        Assert.Equal(0, second.ActiveQuests[0].Requirements[0].CurrentProgress);
        Assert.Equal(0, game.WorldProjects[0].Stages[0].Requirements[0].CurrentAmount);
        Assert.Equal(0, second.WorldProjects[0].Stages[0].Requirements[0].CurrentAmount);

        Assert.NotSame(game.Rooms["town_square"], first.Rooms["town_square"]);
        Assert.NotSame(first.Rooms["town_square"], second.Rooms["town_square"]);
        Assert.NotSame(game.NPCs["blacksmith"], first.NPCs["blacksmith"]);
        Assert.NotSame(
            first.PlayerInventory.Items["health_potion"].Item,
            second.PlayerInventory.Items["health_potion"].Item);
    }
}
