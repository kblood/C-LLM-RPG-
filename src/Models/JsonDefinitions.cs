using System.Text.Json.Serialization;

namespace CSharpRPGBackend.Core;

/// <summary>
/// JSON-deserializable definitions for game content.
/// These classes map to the JSON schema and can be converted to the main model classes.
/// </summary>

// ============================================================================
// GAME DEFINITION
// ============================================================================

public class GameDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("gameSettings")]
    public GameSettingsDefinition? GameSettings { get; set; }

    [JsonPropertyName("style")]
    public StyleSettingsDefinition? Style { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = new();

    [JsonPropertyName("startingItems")]
    public List<StartingItemDefinition> StartingItems { get; set; } = new();

    [JsonPropertyName("metadata")]
    public MetadataDefinition? Metadata { get; set; }
}

public class GameSettingsDefinition
{
    [JsonPropertyName("startingRoomId")]
    public string StartingRoomId { get; set; } = string.Empty;

    [JsonPropertyName("winConditionRoomIds")]
    public List<string> WinConditionRoomIds { get; set; } = new();

    [JsonPropertyName("maxTurns")]
    public int? MaxTurns { get; set; }

    [JsonPropertyName("playerStartingHealth")]
    public int PlayerStartingHealth { get; set; } = 100;

    [JsonPropertyName("playerStartingLevel")]
    public int PlayerStartingLevel { get; set; } = 1;

    [JsonPropertyName("aiModel")]
    public string? AiModel { get; set; } = "granite4:3b";

    [JsonPropertyName("ollamaUrl")]
    public string? OllamaUrl { get; set; } = "http://localhost:11434";
}

public class StyleSettingsDefinition
{
    [JsonPropertyName("theme")]
    public string? Theme { get; set; } = "fantasy";

    [JsonPropertyName("tonality")]
    public string? Tonality { get; set; }

    [JsonPropertyName("narratorVoice")]
    public string? NarratorVoice { get; set; }
}

public class StartingItemDefinition
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
}

public class MetadataDefinition
{
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; set; }

    [JsonPropertyName("lastModified")]
    public string? LastModified { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

// ============================================================================
// STORY DEFINITION
// ============================================================================

public class StoryDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("acts")]
    public List<ActDefinition> Acts { get; set; } = new();

    [JsonPropertyName("globalObjectives")]
    public List<ObjectiveDefinition> GlobalObjectives { get; set; } = new();
}

public class ActDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("startingRoomId")]
    public string StartingRoomId { get; set; } = string.Empty;

    [JsonPropertyName("completionCriteria")]
    public CompletionCriteriaDefinition? CompletionCriteria { get; set; }

    [JsonPropertyName("briefing")]
    public string? Briefing { get; set; }

    [JsonPropertyName("objectives")]
    public List<ObjectiveDefinition> Objectives { get; set; } = new();
}

public class CompletionCriteriaDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "reach_room", "complete_quest", "recruit_npcs"

    [JsonPropertyName("roomId")]
    public string? RoomId { get; set; }

    [JsonPropertyName("questId")]
    public string? QuestId { get; set; }

    [JsonPropertyName("requiredNpcs")]
    public List<string> RequiredNpcs { get; set; } = new();

    [JsonPropertyName("minRequired")]
    public int? MinRequired { get; set; }
}

public class ObjectiveDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}

// ============================================================================
// ROOM DEFINITION
// ============================================================================

public class RoomDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = "indoor"; // "indoor", "outdoor"

    [JsonPropertyName("exits")]
    public List<ExitDefinition> Exits { get; set; } = new();

    [JsonPropertyName("npcs")]
    public List<RoomNpcDefinition> NPCs { get; set; } = new();

    [JsonPropertyName("items")]
    public List<RoomItemDefinition> Items { get; set; } = new();

    [JsonPropertyName("ambiance")]
    public AmbianceDefinition? Ambiance { get; set; }

    [JsonPropertyName("hazards")]
    public List<HazardDefinition> Hazards { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ExitDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("destinationRoomId")]
    public string DestinationRoomId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("requiresItem")]
    public string? RequiresItem { get; set; }

    [JsonPropertyName("requiresKey")]
    public string? RequiresKey { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; } = false;
}

public class RoomNpcDefinition
{
    [JsonPropertyName("npcId")]
    public string NpcId { get; set; } = string.Empty;

    [JsonPropertyName("spawnOnEnter")]
    public bool SpawnOnEnter { get; set; } = true;

    [JsonPropertyName("hostility")]
    public string? Hostility { get; set; } = "neutral"; // "friendly", "neutral", "hostile"

    [JsonPropertyName("approachMessage")]
    public string? ApproachMessage { get; set; }
}

public class RoomItemDefinition
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("spawnOnEnter")]
    public bool SpawnOnEnter { get; set; } = true;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    [JsonPropertyName("pickupMessage")]
    public string? PickupMessage { get; set; }
}

public class AmbianceDefinition
{
    [JsonPropertyName("soundscape")]
    public string? Soundscape { get; set; }

    [JsonPropertyName("lightingLevel")]
    public string? LightingLevel { get; set; } // "bright", "dim", "dark"

    [JsonPropertyName("temperature")]
    public string? Temperature { get; set; } // "warm", "cool", "freezing"
}

public class HazardDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "radiation", "fire", "water", etc.

    [JsonPropertyName("damagePerTurn")]
    public int DamagePerTurn { get; set; }

    [JsonPropertyName("avoidable")]
    public bool Avoidable { get; set; } = true;

    [JsonPropertyName("avoidDescription")]
    public string? AvoidDescription { get; set; }
}

// ============================================================================
// NPC DEFINITION
// ============================================================================

public class NpcDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("portrait")]
    public string? Portrait { get; set; }

    [JsonPropertyName("stats")]
    public NpcStatsDefinition? Stats { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; } = "common_person";

    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; } = "neutral"; // "good", "neutral", "evil"

    [JsonPropertyName("personality")]
    public PersonalityDefinition? Personality { get; set; }

    [JsonPropertyName("location")]
    public LocationDefinition? Location { get; set; }

    [JsonPropertyName("inventory")]
    public List<InventoryItemDefinition> Inventory { get; set; } = new();

    [JsonPropertyName("dialogue")]
    public DialogueDefinition? Dialogue { get; set; }

    [JsonPropertyName("relationships")]
    public RelationshipsDefinition? Relationships { get; set; }

    [JsonPropertyName("quests")]
    public List<NpcQuestDefinition> Quests { get; set; } = new();

    [JsonPropertyName("behaviors")]
    public BehaviorsDefinition? Behaviors { get; set; }
}

public class NpcStatsDefinition
{
    [JsonPropertyName("health")]
    public int Health { get; set; } = 60;

    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; } = 60;

    [JsonPropertyName("level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("experience")]
    public int Experience { get; set; } = 0;

    [JsonPropertyName("strength")]
    public int Strength { get; set; } = 10;

    [JsonPropertyName("agility")]
    public int Agility { get; set; } = 10;

    [JsonPropertyName("armor")]
    public int Armor { get; set; } = 0;
}

public class PersonalityDefinition
{
    [JsonPropertyName("personalityPrompt")]
    public string? PersonalityPrompt { get; set; }

    [JsonPropertyName("traits")]
    public List<string> Traits { get; set; } = new();

    [JsonPropertyName("favoriteTopics")]
    public List<string> FavoriteTopics { get; set; } = new();

    [JsonPropertyName("dislikedTopics")]
    public List<string> DislikedTopics { get; set; } = new();
}

public class LocationDefinition
{
    [JsonPropertyName("currentRoomId")]
    public string CurrentRoomId { get; set; } = string.Empty;

    [JsonPropertyName("homeRoomId")]
    public string HomeRoomId { get; set; } = string.Empty;

    [JsonPropertyName("patrolRoomIds")]
    public List<string>? PatrolRoomIds { get; set; }

    [JsonPropertyName("patrolIntervalTurns")]
    public int? PatrolIntervalTurns { get; set; }

    [JsonPropertyName("canMove")]
    public bool CanMove { get; set; } = true;

    [JsonPropertyName("canJoinParty")]
    public bool CanJoinParty { get; set; } = false;
}

public class InventoryItemDefinition
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;

    [JsonPropertyName("loot")]
    public bool Loot { get; set; } = false; // Can player loot this item?
}

public class DialogueDefinition
{
    [JsonPropertyName("greeting")]
    public string? Greeting { get; set; }

    [JsonPropertyName("farewell")]
    public string? Farewell { get; set; }

    [JsonPropertyName("defaultResponse")]
    public string? DefaultResponse { get; set; }

    [JsonPropertyName("reactions")]
    public Dictionary<string, string> Reactions { get; set; } = new();
}

public class RelationshipsDefinition
{
    [JsonPropertyName("allies")]
    public List<string> Allies { get; set; } = new();

    [JsonPropertyName("enemies")]
    public List<string> Enemies { get; set; } = new();

    [JsonPropertyName("neutral")]
    public List<string> Neutral { get; set; } = new();
}

public class NpcQuestDefinition
{
    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;

    [JsonPropertyName("questType")]
    public string QuestType { get; set; } = string.Empty;

    [JsonPropertyName("offered")]
    public bool Offered { get; set; } = false;
}

public class BehaviorsDefinition
{
    [JsonPropertyName("combatStyle")]
    public string? CombatStyle { get; set; } = "balanced"; // "aggressive", "defensive", "balanced"

    [JsonPropertyName("aiAggression")]
    public string? AiAggression { get; set; } = "medium"; // "low", "medium", "high"

    [JsonPropertyName("fleePath")]
    public string? FleePath { get; set; } // Room ID to flee to
}

// ============================================================================
// ITEM DEFINITION
// ============================================================================

public class ItemDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "miscellaneous"; // "weapon", "armor", "key", "consumable", etc.

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; } = "common";

    [JsonPropertyName("stats")]
    public ItemStatsDefinition? Stats { get; set; }

    [JsonPropertyName("equipment")]
    public EquipmentDefinition? Equipment { get; set; }

    [JsonPropertyName("effects")]
    public EffectsDefinition? Effects { get; set; }

    [JsonPropertyName("requirements")]
    public RequirementsDefinition? Requirements { get; set; }

    [JsonPropertyName("value")]
    public ItemValueDefinition? Value { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ItemStatsDefinition
{
    [JsonPropertyName("damageBonus")]
    public int DamageBonus { get; set; } = 0;

    [JsonPropertyName("armorBonus")]
    public int ArmorBonus { get; set; } = 0;

    [JsonPropertyName("criticalChance")]
    public int CriticalChance { get; set; } = 0;
}

public class EquipmentDefinition
{
    [JsonPropertyName("isEquippable")]
    public bool IsEquippable { get; set; } = false;

    [JsonPropertyName("equipmentSlot")]
    public string? EquipmentSlot { get; set; } // "main_hand", "head", "chest", etc.

    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 0;

    [JsonPropertyName("durability")]
    public int Durability { get; set; } = 100;

    [JsonPropertyName("maxDurability")]
    public int MaxDurability { get; set; } = 100;
}

public class EffectsDefinition
{
    [JsonPropertyName("onEquip")]
    public string? OnEquip { get; set; }

    [JsonPropertyName("onUse")]
    public string? OnUse { get; set; }

    [JsonPropertyName("onUnequip")]
    public string? OnUnequip { get; set; }
}

public class RequirementsDefinition
{
    [JsonPropertyName("minLevel")]
    public int MinLevel { get; set; } = 1;

    [JsonPropertyName("minStrength")]
    public int MinStrength { get; set; } = 0;

    [JsonPropertyName("restrictions")]
    public string? Restrictions { get; set; }
}

public class ItemValueDefinition
{
    [JsonPropertyName("goldValue")]
    public int GoldValue { get; set; } = 0;

    [JsonPropertyName("sellPrice")]
    public int SellPrice { get; set; } = 0;
}

// ============================================================================
// QUEST DEFINITION
// ============================================================================

public class QuestDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("giver")]
    public string? Giver { get; set; }

    [JsonPropertyName("giverDialog")]
    public string? GiverDialog { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "companion", "kill", "collect", etc.

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; } = "medium";

    [JsonPropertyName("experience_reward")]
    public int ExperienceReward { get; set; } = 0;

    [JsonPropertyName("item_rewards")]
    public List<QuestRewardItemDefinition> ItemRewards { get; set; } = new();

    [JsonPropertyName("objectives")]
    public List<QuestObjectiveDefinition> Objectives { get; set; } = new();

    [JsonPropertyName("completionCriteria")]
    public CompletionCriteriaDefinition? CompletionCriteria { get; set; }

    [JsonPropertyName("timeLimit")]
    public int? TimeLimit { get; set; }

    [JsonPropertyName("repeatable")]
    public bool Repeatable { get; set; } = false;

    [JsonPropertyName("prerequisites")]
    public List<PrerequisiteDefinition> Prerequisites { get; set; } = new();

    [JsonPropertyName("failConditions")]
    public List<FailConditionDefinition> FailConditions { get; set; } = new();
}

public class QuestRewardItemDefinition
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 1;
}

public class QuestObjectiveDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "location", "dialogue", "item", etc.

    [JsonPropertyName("targetRoomId")]
    public string? TargetRoomId { get; set; }

    [JsonPropertyName("targetNpcId")]
    public string? TargetNpcId { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; } = false;

    [JsonPropertyName("requiresPreviousObjective")]
    public string? RequiresPreviousObjective { get; set; }
}

public class PrerequisiteDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "complete_quest", "reach_level", etc.

    [JsonPropertyName("questId")]
    public string? QuestId { get; set; }

    [JsonPropertyName("levelRequired")]
    public int? LevelRequired { get; set; }
}

public class FailConditionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "npc_death", "time_expired", etc.

    [JsonPropertyName("npcId")]
    public string? NpcId { get; set; }
}
