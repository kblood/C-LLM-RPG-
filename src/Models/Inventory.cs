namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents a character's inventory.
/// </summary>
public class Inventory
{
    public Dictionary<string, InventoryItem> Items { get; set; } = new();
    public int MaxWeight { get; set; } = 100;
    public int CurrentWeight { get; private set; }

    public bool AddItem(Item item, int quantity = 1)
    {
        if (quantity <= 0)
            return false;

        int weight = item.Weight * quantity;
        if (CurrentWeight + weight > MaxWeight)
            return false;

        string key = item.Id;
        if (Items.ContainsKey(key))
        {
            Items[key].Quantity += quantity;
        }
        else
        {
            Items[key] = new InventoryItem { Item = item, Quantity = quantity };
        }

        CurrentWeight += weight;
        return true;
    }

    public bool RemoveItem(string itemId, int quantity = 1)
    {
        if (quantity <= 0 || !Items.TryGetValue(itemId, out var inventoryItem) || inventoryItem.Quantity < quantity)
            return false;

        inventoryItem.Quantity -= quantity;
        CurrentWeight -= inventoryItem.Item.Weight * quantity;
        if (inventoryItem.Quantity == 0)
        {
            Items.Remove(itemId);
        }

        return true;
    }

    public InventoryItem? GetItem(string itemId)
    {
        return Items.ContainsKey(itemId) ? Items[itemId] : null;
    }
}

public class InventoryItem
{
    public Item Item { get; set; } = new();
    public int Quantity { get; set; }
}
