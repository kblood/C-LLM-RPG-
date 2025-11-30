using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Utils;

/// <summary>
/// Fluent builder for creating complete games with all content.
/// Usage: new GameBuilder("dragon_quest")
///     .WithTitle("The Dragon Quest")
///     .WithStyle(GameStyle.Fantasy)
///     .WithStory("You must defeat the dragon...")
///     .AddRoom(roomBuilder.Build())
///     .AddNPC(npcBuilder.Build())
///     .AddItem(itemBuilder.Build())
///     .Build()
/// </summary>
public class GameBuilder
{
    private readonly Game _game;

    public GameBuilder(string gameId)
    {
        _game = new Game { Id = gameId };
    }

    public GameBuilder WithTitle(string title)
    {
        _game.Title = title;
        return this;
    }

    public GameBuilder WithSubtitle(string subtitle)
    {
        _game.Subtitle = subtitle;
        return this;
    }

    public GameBuilder WithStyle(GameStyle style)
    {
        _game.Style = style;
        return this;
    }

    public GameBuilder WithDescription(string description)
    {
        _game.Description = description;
        return this;
    }

    public GameBuilder WithStory(string storyIntroduction)
    {
        _game.StoryIntroduction = storyIntroduction;
        return this;
    }

    public GameBuilder WithObjective(string objective)
    {
        _game.GameObjective = objective;
        return this;
    }

    public GameBuilder WithEstimatedPlayTime(int minutes)
    {
        _game.EstimatedPlayTime = minutes;
        return this;
    }

    public GameBuilder WithAuthor(string author)
    {
        _game.Author = author;
        return this;
    }

    public GameBuilder WithVersion(string version)
    {
        _game.Version = version;
        return this;
    }

    /// <summary>
    /// Set initial player stats.
    /// </summary>
    public GameBuilder WithPlayerDefaults(int health = 100, int level = 1, string? description = null)
    {
        _game.InitialPlayerHealth = health;
        _game.InitialPlayerLevel = level;
        if (description != null)
            _game.InitialPlayerDescription = description;
        return this;
    }

    /// <summary>
    /// Set the starting room.
    /// </summary>
    public GameBuilder WithStartingRoom(string roomId)
    {
        _game.StartingRoomId = roomId;
        return this;
    }

    /// <summary>
    /// Add a room to the game.
    /// </summary>
    public GameBuilder AddRoom(Room room)
    {
        _game.Rooms[room.Id] = room;
        return this;
    }

    /// <summary>
    /// Add multiple rooms.
    /// </summary>
    public GameBuilder AddRooms(params Room[] rooms)
    {
        foreach (var room in rooms)
            _game.Rooms[room.Id] = room;
        return this;
    }

    /// <summary>
    /// Add an NPC to the game.
    /// </summary>
    public GameBuilder AddNPC(Character npc)
    {
        _game.NPCs[npc.Id] = npc;
        return this;
    }

    /// <summary>
    /// Add multiple NPCs.
    /// </summary>
    public GameBuilder AddNPCs(params Character[] npcs)
    {
        foreach (var npc in npcs)
            _game.NPCs[npc.Id] = npc;
        return this;
    }

    /// <summary>
    /// Add an item to the game world.
    /// </summary>
    public GameBuilder AddItem(Item item)
    {
        _game.Items[item.Id] = item;
        return this;
    }

    /// <summary>
    /// Add multiple items.
    /// </summary>
    public GameBuilder AddItems(params Item[] items)
    {
        foreach (var item in items)
            _game.Items[item.Id] = item;
        return this;
    }

    /// <summary>
    /// Add a quest to the game.
    /// </summary>
    public GameBuilder AddQuest(Quest quest)
    {
        _game.Quests.Add(quest);
        return this;
    }

    /// <summary>
    /// Enable/disable combat system.
    /// </summary>
    public GameBuilder WithCombat(bool enabled = true)
    {
        _game.HasCombat = enabled;
        return this;
    }

    /// <summary>
    /// Enable/disable inventory system.
    /// </summary>
    public GameBuilder WithInventory(bool enabled = true)
    {
        _game.HasInventory = enabled;
        return this;
    }

    /// <summary>
    /// Enable/disable magic system.
    /// </summary>
    public GameBuilder WithMagic(bool enabled = true)
    {
        _game.HasMagic = enabled;
        return this;
    }

    /// <summary>
    /// Enable/disable technology system.
    /// </summary>
    public GameBuilder WithTechnology(bool enabled = true)
    {
        _game.HasTechnology = enabled;
        return this;
    }

    /// <summary>
    /// Allow player to recruit NPCs into party.
    /// </summary>
    public GameBuilder WithNPCRecruitment(bool allowed = true)
    {
        _game.CanRecruitNPCs = allowed;
        return this;
    }

    /// <summary>
    /// Enable permadeath (game over on player death).
    /// </summary>
    public GameBuilder WithPermadeath(bool enabled = false)
    {
        _game.Permadeath = enabled;
        return this;
    }

    /// <summary>
    /// Enable/disable PvP.
    /// </summary>
    public GameBuilder WithPvP(bool enabled = false)
    {
        _game.PvP = enabled;
        return this;
    }

    /// <summary>
    /// Add a win condition room (reaching this room = victory).
    /// </summary>
    public GameBuilder AddWinConditionRoom(string roomId)
    {
        _game.WinConditionRoomIds ??= new();
        if (!_game.WinConditionRoomIds.Contains(roomId))
            _game.WinConditionRoomIds.Add(roomId);
        return this;
    }

    /// <summary>
    /// Add a new flexible win condition (room, item, npc_defeat, or quest_complete).
    /// </summary>
    public GameBuilder AddWinCondition(WinCondition condition)
    {
        _game.WinConditions ??= new();
        _game.WinConditions.Add(condition);
        return this;
    }

    /// <summary>
    /// Set whether player can go anywhere or is limited.
    /// </summary>
    public GameBuilder WithFreeRoam(bool enabled = true)
    {
        _game.FreeRoam = enabled;
        return this;
    }

    /// <summary>
    /// Add a custom setting.
    /// </summary>
    public GameBuilder WithCustomSetting(string key, string value)
    {
        _game.CustomSettings[key] = value;
        return this;
    }

    /// <summary>
    /// Build and return the game.
    /// </summary>
    public Game Build()
    {
        // Validate
        if (string.IsNullOrWhiteSpace(_game.Title))
            _game.Title = _game.Id;

        if (!_game.Rooms.ContainsKey(_game.StartingRoomId))
            throw new InvalidOperationException($"Starting room '{_game.StartingRoomId}' not found in rooms");

        return _game;
    }
}
