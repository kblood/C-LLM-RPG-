namespace CSharpRPGBackend.Core;

/// <summary>
/// Configures what the Game Master (LLM) is allowed to dynamically create or decide.
/// Some games want strict rule-following; others want creative freedom.
/// </summary>
public class GameMasterAuthority
{
    /// <summary>
    /// Can the GM create new items that weren't predefined in the game?
    /// Example: Finding a "rusty dagger" in a bandit camp even if not in Items dictionary.
    /// </summary>
    public bool CanCreateItems { get; set; } = false;

    /// <summary>
    /// Can the GM generate quests dynamically based on NPC conversations?
    /// Example: Apothecary mentions needing herbs → GM creates a gathering quest.
    /// </summary>
    public bool CanCreateQuests { get; set; } = false;

    /// <summary>
    /// Can the GM decide if resources exist in a location based on context?
    /// Example: "Search for ore" in a cave - GM decides based on description.
    /// If false, only predefined GatherableResources can be found.
    /// </summary>
    public bool CanDecideResources { get; set; } = false;

    /// <summary>
    /// Can the GM create crafting recipes dynamically?
    /// Example: Blacksmith offers to make a custom weapon if you bring materials.
    /// </summary>
    public bool CanCreateRecipes { get; set; } = false;

    /// <summary>
    /// Can NPCs offer services/jobs not predefined in the game?
    /// Example: Guard asks you to patrol the walls for payment.
    /// </summary>
    public bool CanCreateJobs { get; set; } = false;

    /// <summary>
    /// Can the GM introduce new NPCs not in the game definition?
    /// Example: A wandering merchant appears on the road.
    /// </summary>
    public bool CanCreateNPCs { get; set; } = false;

    /// <summary>
    /// Can the GM modify room descriptions or add details dynamically?
    /// Example: Adding weather effects, time-of-day changes.
    /// </summary>
    public bool CanModifyEnvironment { get; set; } = false;

    /// <summary>
    /// How creative can the GM be with narration? (0-100)
    /// 0 = Strict, factual narration only
    /// 50 = Moderate embellishment allowed
    /// 100 = Full creative freedom in descriptions
    /// </summary>
    public int NarrationCreativity { get; set; } = 50;

    /// <summary>
    /// Creates a strict ruleset where GM can only use predefined content.
    /// Good for puzzle games or tightly designed experiences.
    /// </summary>
    public static GameMasterAuthority Strict() => new()
    {
        CanCreateItems = false,
        CanCreateQuests = false,
        CanDecideResources = false,
        CanCreateRecipes = false,
        CanCreateJobs = false,
        CanCreateNPCs = false,
        CanModifyEnvironment = false,
        NarrationCreativity = 25
    };

    /// <summary>
    /// Creates a balanced ruleset with some creative freedom.
    /// Good for most RPG experiences.
    /// </summary>
    public static GameMasterAuthority Balanced() => new()
    {
        CanCreateItems = false,
        CanCreateQuests = false,
        CanDecideResources = true,  // Can find contextual resources
        CanCreateRecipes = false,
        CanCreateJobs = true,       // NPCs can offer simple jobs
        CanCreateNPCs = false,
        CanModifyEnvironment = true,
        NarrationCreativity = 50
    };

    /// <summary>
    /// Creates a fully dynamic ruleset where GM has creative control.
    /// Good for sandbox/emergent gameplay experiences.
    /// </summary>
    public static GameMasterAuthority Dynamic() => new()
    {
        CanCreateItems = true,
        CanCreateQuests = true,
        CanDecideResources = true,
        CanCreateRecipes = true,
        CanCreateJobs = true,
        CanCreateNPCs = true,
        CanModifyEnvironment = true,
        NarrationCreativity = 75
    };

    /// <summary>
    /// Creates a fully open world where GM has complete freedom.
    /// Good for pure roleplay or experimental games.
    /// </summary>
    public static GameMasterAuthority OpenWorld() => new()
    {
        CanCreateItems = true,
        CanCreateQuests = true,
        CanDecideResources = true,
        CanCreateRecipes = true,
        CanCreateJobs = true,
        CanCreateNPCs = true,
        CanModifyEnvironment = true,
        NarrationCreativity = 100
    };
}
