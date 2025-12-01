using CSharpRPGBackend.Core;

namespace CSharpRPGBackend.Utils;

/// <summary>
/// Fluent builder for creating items with various properties.
/// Usage: new ItemBuilder("iron_sword")
///     .WithName("Iron Sword")
///     .AsWeapon(damage: 10)
///     .Build()
/// </summary>
public class ItemBuilder
{
    private readonly Item _item;

    public ItemBuilder(string itemId)
    {
        _item = new Item { Id = itemId };
    }

    public ItemBuilder WithName(string name)
    {
        _item.Name = name;
        return this;
    }

    public ItemBuilder WithDescription(string description)
    {
        _item.Description = description;
        return this;
    }

    public ItemBuilder WithWeight(int weight)
    {
        _item.Weight = weight;
        return this;
    }

    public ItemBuilder WithValue(int goldValue)
    {
        _item.Value = goldValue;
        return this;
    }

    /// <summary>
    /// Set detailed pricing with buy/sell multipliers.
    /// </summary>
    public ItemBuilder WithPricing(long basePrice, double buyMultiplier = 1.0, double sellMultiplier = 0.5)
    {
        _item.Value = (int)basePrice; // Keep legacy value in sync
        _item.Pricing = new ItemPricing
        {
            BasePrice = basePrice,
            BuyMultiplier = buyMultiplier,
            SellMultiplier = sellMultiplier
        };
        return this;
    }

    /// <summary>
    /// Mark item as not buyable (quest rewards, unique loot only).
    /// </summary>
    public ItemBuilder NotBuyable()
    {
        _item.Pricing ??= new ItemPricing { BasePrice = _item.Value };
        _item.Pricing.CanBuy = false;
        return this;
    }

    /// <summary>
    /// Mark item as not sellable (quest items, keys, etc).
    /// </summary>
    public ItemBuilder NotSellable()
    {
        _item.Pricing ??= new ItemPricing { BasePrice = _item.Value };
        _item.Pricing.CanSell = false;
        return this;
    }

    public ItemBuilder WithTheme(string theme)
    {
        _item.Theme = theme;
        return this;
    }

    public ItemBuilder WithRarity(ItemRarity rarity)
    {
        _item.Rarity = rarity;
        return this;
    }

    public ItemBuilder AsUnique()
    {
        _item.IsUnique = true;
        return this;
    }

    /// <summary>
    /// Create a weapon with damage stats.
    /// </summary>
    public ItemBuilder AsWeapon(int damage = 5, int criticalChance = 10)
    {
        _item.Type = ItemType.Weapon;
        _item.DamageBonus = damage;
        _item.CriticalChance = criticalChance;
        _item.IsEquippable = true;
        _item.EquipmentSlot = "main_hand";
        return this;
    }

    /// <summary>
    /// Create armor with defense stats.
    /// </summary>
    public ItemBuilder AsArmor(int armorBonus = 5, string slot = "chest")
    {
        _item.Type = ItemType.Armor;
        _item.ArmorBonus = armorBonus;
        _item.IsEquippable = true;
        _item.EquipmentSlot = slot;
        return this;
    }

    /// <summary>
    /// Create a key that unlocks something.
    /// </summary>
    public ItemBuilder AsKey(string unlocksId, KeyType keyType = KeyType.Mechanical, string? description = null)
    {
        _item.Type = ItemType.Key;
        _item.IsKey = true;
        _item.UnlocksId = unlocksId;
        _item.KeyType = keyType;
        if (description != null)
            _item.Description = description;
        return this;
    }

    /// <summary>
    /// Create a teleportation device.
    /// </summary>
    public ItemBuilder AsTeleportation(string destinationRoomId, string? teleportDescription = null)
    {
        _item.Type = ItemType.Teleportation;
        _item.IsTeleportation = true;
        _item.TeleportDestinationRoomId = destinationRoomId;
        _item.TeleportDescription = teleportDescription ?? $"Teleports to {destinationRoomId}";
        return this;
    }

    /// <summary>
    /// Create a consumable item (potion, food, etc).
    /// </summary>
    public ItemBuilder AsConsumable(int uses = 1)
    {
        _item.Type = ItemType.Consumable;
        _item.IsConsumable = true;
        _item.ConsumableUsesRemaining = uses;
        _item.Stackable = true;
        return this;
    }

    /// <summary>
    /// Add an effect to a consumable (heal, mana, buff, etc).
    /// </summary>
    public ItemBuilder WithConsumableEffect(string effectName, int value)
    {
        _item.ConsumableEffects[effectName] = value;
        return this;
    }

    /// <summary>
    /// Make the item a quest item.
    /// </summary>
    public ItemBuilder AsQuestItem()
    {
        _item.Type = ItemType.QuestItem;
        _item.CanBeTaken = true;
        return this;
    }

    /// <summary>
    /// Make the item a crafting material.
    /// </summary>
    public ItemBuilder AsCraftingMaterial(string category, int gatherDifficulty = 50)
    {
        _item.Type = ItemType.CraftingMaterial;
        _item.MaterialCategory = category;
        _item.GatherDifficulty = gatherDifficulty;
        _item.Stackable = true;
        return this;
    }

    /// <summary>
    /// Set biomes where this item can be found.
    /// </summary>
    public ItemBuilder FoundIn(params string[] biomes)
    {
        _item.FoundInBiomes = biomes.ToList();
        return this;
    }

    /// <summary>
    /// Make the item a treasure (valuable for selling only).
    /// </summary>
    public ItemBuilder AsTreasure(int value)
    {
        _item.Type = ItemType.Treasure;
        _item.IsTreasure = true;
        _item.Value = value;
        _item.Stackable = true;
        return this;
    }

    /// <summary>
    /// Make the item junk (low-value flavor item).
    /// </summary>
    public ItemBuilder AsJunk(int value = 1)
    {
        _item.Type = ItemType.Junk;
        _item.IsJunk = true;
        _item.Value = value;
        return this;
    }

    /// <summary>
    /// Make the item a tool (pickaxe, herbalist kit, etc.).
    /// </summary>
    public ItemBuilder AsTool(string? useFor = null)
    {
        _item.Type = ItemType.Tool;
        if (useFor != null)
            _item.CustomProperties["tool_use"] = useFor;
        return this;
    }

    /// <summary>
    /// Mark the item as cursed.
    /// </summary>
    public ItemBuilder AsCursed()
    {
        _item.Cursed = true;
        return this;
    }

    /// <summary>
    /// Add a custom property.
    /// </summary>
    public ItemBuilder WithCustomProperty(string key, object value)
    {
        _item.CustomProperties[key] = value;
        return this;
    }

    /// <summary>
    /// Build and return the item.
    /// </summary>
    public Item Build()
    {
        // Validate
        if (string.IsNullOrWhiteSpace(_item.Name))
            _item.Name = _item.Id;

        return _item;
    }
}
