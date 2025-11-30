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
        if (!Items.ContainsKey(itemId))
            return false;

        Items[itemId].Quantity -= quantity;
        if (Items[itemId].Quantity <= 0)
        {
            CurrentWeight -= Items[itemId].Item.Weight * quantity;
            Items.Remove(itemId);
        }
        else
        {
            CurrentWeight -= Items[itemId].Item.Weight * quantity;
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
