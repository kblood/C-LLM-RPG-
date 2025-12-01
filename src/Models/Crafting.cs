namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a crafting recipe that can create items from materials.
/// </summary>
public class CraftingRecipe
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The item ID that this recipe produces.
    /// </summary>
    public string OutputItemId { get; set; } = string.Empty;

    /// <summary>
    /// How many of the output item are created.
    /// </summary>
    public int OutputQuantity { get; set; } = 1;

    /// <summary>
    /// Required ingredients/materials to craft this item.
    /// </summary>
    public List<RecipeIngredient> Ingredients { get; set; } = new();

    /// <summary>
    /// NPC ID who can craft this. Null means player can craft it themselves.
    /// </summary>
    public string? CrafterId { get; set; }

    /// <summary>
    /// The crafting specialty required (e.g., "blacksmith", "alchemy", "tailoring").
    /// Used to match NPCs who can craft this.
    /// </summary>
    public string? CraftingSpecialty { get; set; }

    /// <summary>
    /// Currency cost to have an NPC craft this item.
    /// </summary>
    public long CraftingCost { get; set; } = 0;

    /// <summary>
    /// Whether the player can craft this themselves (vs needing an NPC).
    /// </summary>
    public bool PlayerCanCraft { get; set; } = false;

    /// <summary>
    /// Required skill level for player crafting (if applicable).
    /// </summary>
    public int RequiredSkillLevel { get; set; } = 0;

    /// <summary>
    /// Location type required to craft (e.g., "forge", "alchemy_table").
    /// Null means can be crafted anywhere.
    /// </summary>
    public string? RequiredLocation { get; set; }

    /// <summary>
    /// Whether player must have learned this recipe first.
    /// </summary>
    public bool RequiresKnowledge { get; set; } = false;

    /// <summary>
    /// Category for organizing recipes (e.g., "weapons", "potions", "armor").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Check if a character can craft this recipe (has ingredients).
    /// </summary>
    public bool CanCraft(Inventory inventory)
    {
        foreach (var ingredient in Ingredients)
        {
            var item = inventory.GetItem(ingredient.ItemId);
            if (item == null || item.Quantity < ingredient.Quantity)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Get a formatted list of ingredients for display.
    /// </summary>
    public string GetIngredientsDisplay()
    {
        return string.Join(", ", Ingredients.Select(i => $"{i.ItemName ?? i.ItemId} x{i.Quantity}"));
    }
}

/// <summary>
/// Represents a single ingredient in a crafting recipe.
/// </summary>
public class RecipeIngredient
{
    /// <summary>
    /// The item ID required.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Display name (for UI, optional - will use item name if null).
    /// </summary>
    public string? ItemName { get; set; }

    /// <summary>
    /// Quantity of this item required.
    /// </summary>
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Configuration for the crafting system in a game.
/// </summary>
public class CraftingConfig
{
    /// <summary>
    /// Whether crafting is enabled in this game.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Whether players can craft items themselves.
    /// </summary>
    public bool PlayerCraftingEnabled { get; set; } = false;

    /// <summary>
    /// Whether NPCs can craft items for the player.
    /// </summary>
    public bool NpcCraftingEnabled { get; set; } = true;

    /// <summary>
    /// All defined recipes in this game.
    /// </summary>
    public Dictionary<string, CraftingRecipe> Recipes { get; set; } = new();

    /// <summary>
    /// Creates a disabled crafting config.
    /// </summary>
    public static CraftingConfig Disabled() => new() { Enabled = false };

    /// <summary>
    /// Creates an NPC-only crafting config (players must ask NPCs to craft).
    /// </summary>
    public static CraftingConfig NpcOnly() => new()
    {
        Enabled = true,
        PlayerCraftingEnabled = false,
        NpcCraftingEnabled = true
    };

    /// <summary>
    /// Creates a full crafting config (both player and NPC crafting).
    /// </summary>
    public static CraftingConfig Full() => new()
    {
        Enabled = true,
        PlayerCraftingEnabled = true,
        NpcCraftingEnabled = true
    };
}
