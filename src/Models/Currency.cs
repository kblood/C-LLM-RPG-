namespace CSharpRPGBackend.Core;

/// <summary>
/// Represents the economy configuration for a game.
/// Supports two modes:
/// - Tiered: Platinum, Gold, Silver (100 silver = 1 gold, 100 gold = 1 platinum)
/// - Simple: A single currency (credits, bottlecaps, coins, etc.)
/// </summary>
public class EconomyConfig
{
    /// <summary>
    /// Whether the economy system is enabled for this game.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The type of economy (Tiered or Simple).
    /// </summary>
    public EconomyType Type { get; set; } = EconomyType.Simple;

    /// <summary>
    /// For Simple economy: the name of the currency (e.g., "Credits", "Bottlecaps", "Coins").
    /// </summary>
    public string CurrencyName { get; set; } = "Gold";

    /// <summary>
    /// For Simple economy: the symbol/emoji to display (e.g., "💰", "🪙", "$").
    /// </summary>
    public string CurrencySymbol { get; set; } = "💰";

    /// <summary>
    /// For Tiered economy: names for each tier.
    /// </summary>
    public TieredCurrencyNames TieredNames { get; set; } = new();

    /// <summary>
    /// The conversion rate between tiers (default: 100).
    /// 100 silver = 1 gold, 100 gold = 1 platinum.
    /// </summary>
    public int TierConversionRate { get; set; } = 100;

    /// <summary>
    /// Creates a default disabled economy.
    /// </summary>
    public static EconomyConfig Disabled() => new() { Enabled = false };

    /// <summary>
    /// Creates a simple single-currency economy.
    /// </summary>
    public static EconomyConfig Simple(string currencyName, string symbol = "💰")
        => new()
        {
            Enabled = true,
            Type = EconomyType.Simple,
            CurrencyName = currencyName,
            CurrencySymbol = symbol
        };

    /// <summary>
    /// Creates a tiered platinum/gold/silver economy.
    /// </summary>
    public static EconomyConfig Tiered(
        string platinumName = "Platinum",
        string goldName = "Gold",
        string silverName = "Silver",
        int conversionRate = 100)
        => new()
        {
            Enabled = true,
            Type = EconomyType.Tiered,
            TieredNames = new TieredCurrencyNames
            {
                Platinum = platinumName,
                Gold = goldName,
                Silver = silverName
            },
            TierConversionRate = conversionRate
        };
}

/// <summary>
/// Names for tiered currency system.
/// </summary>
public class TieredCurrencyNames
{
    public string Platinum { get; set; } = "Platinum";
    public string Gold { get; set; } = "Gold";
    public string Silver { get; set; } = "Silver";

    public string PlatinumSymbol { get; set; } = "💎";
    public string GoldSymbol { get; set; } = "🪙";
    public string SilverSymbol { get; set; } = "🥈";
}

/// <summary>
/// Type of economy system.
/// </summary>
public enum EconomyType
{
    /// <summary>
    /// Single currency system (credits, bottlecaps, coins).
    /// </summary>
    Simple,

    /// <summary>
    /// Three-tier system (platinum, gold, silver).
    /// </summary>
    Tiered
}

/// <summary>
/// Represents a character's wallet/money pouch.
/// Handles both simple and tiered currency internally.
/// </summary>
public class Wallet
{
    /// <summary>
    /// For Simple economy: total currency amount.
    /// For Tiered economy: stores the base unit (silver equivalent).
    /// </summary>
    public long TotalBaseUnits { get; set; } = 0;

    /// <summary>
    /// Add currency to the wallet.
    /// </summary>
    public void Add(long amount)
    {
        TotalBaseUnits += amount;
    }

    /// <summary>
    /// Add tiered currency (converts to base units).
    /// </summary>
    public void AddTiered(int platinum, int gold, int silver, int conversionRate = 100)
    {
        long total = silver;
        total += gold * conversionRate;
        total += platinum * (long)conversionRate * conversionRate;
        TotalBaseUnits += total;
    }

    /// <summary>
    /// Remove currency from the wallet. Returns false if insufficient funds.
    /// </summary>
    public bool Remove(long amount)
    {
        if (TotalBaseUnits < amount)
            return false;
        TotalBaseUnits -= amount;
        return true;
    }

    /// <summary>
    /// Check if wallet has enough currency.
    /// </summary>
    public bool CanAfford(long amount) => TotalBaseUnits >= amount;

    /// <summary>
    /// Get tiered breakdown (platinum, gold, silver).
    /// </summary>
    public (int platinum, int gold, int silver) GetTiered(int conversionRate = 100)
    {
        long remaining = TotalBaseUnits;

        long platinumUnits = (long)conversionRate * conversionRate;
        int platinum = (int)(remaining / platinumUnits);
        remaining %= platinumUnits;

        int gold = (int)(remaining / conversionRate);
        remaining %= conversionRate;

        int silver = (int)remaining;

        return (platinum, gold, silver);
    }

    /// <summary>
    /// Format the wallet contents for display based on economy config.
    /// </summary>
    public string Format(EconomyConfig config)
    {
        if (!config.Enabled)
            return "";

        if (config.Type == EconomyType.Simple)
        {
            return $"{config.CurrencySymbol} {TotalBaseUnits:N0} {config.CurrencyName}";
        }
        else
        {
            var (platinum, gold, silver) = GetTiered(config.TierConversionRate);
            var parts = new List<string>();

            if (platinum > 0)
                parts.Add($"{config.TieredNames.PlatinumSymbol} {platinum} {config.TieredNames.Platinum}");
            if (gold > 0 || platinum > 0)
                parts.Add($"{config.TieredNames.GoldSymbol} {gold} {config.TieredNames.Gold}");
            parts.Add($"{config.TieredNames.SilverSymbol} {silver} {config.TieredNames.Silver}");

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Format a specific amount for display.
    /// </summary>
    public static string FormatAmount(long amount, EconomyConfig config)
    {
        if (!config.Enabled)
            return "";

        if (config.Type == EconomyType.Simple)
        {
            return $"{config.CurrencySymbol} {amount:N0} {config.CurrencyName}";
        }
        else
        {
            var temp = new Wallet { TotalBaseUnits = amount };
            var (platinum, gold, silver) = temp.GetTiered(config.TierConversionRate);
            var parts = new List<string>();

            if (platinum > 0)
                parts.Add($"{platinum} {config.TieredNames.Platinum}");
            if (gold > 0)
                parts.Add($"{gold} {config.TieredNames.Gold}");
            if (silver > 0 || parts.Count == 0)
                parts.Add($"{silver} {config.TieredNames.Silver}");

            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Represents pricing information for an item.
/// </summary>
public class ItemPricing
{
    /// <summary>
    /// The base price of the item (in base currency units).
    /// </summary>
    public long BasePrice { get; set; } = 0;

    /// <summary>
    /// The buy price multiplier (what players pay to buy from merchants).
    /// Default 1.0 means base price.
    /// </summary>
    public double BuyMultiplier { get; set; } = 1.0;

    /// <summary>
    /// The sell price multiplier (what players get when selling to merchants).
    /// Default 0.5 means half of base price.
    /// </summary>
    public double SellMultiplier { get; set; } = 0.5;

    /// <summary>
    /// Whether this item can be bought.
    /// </summary>
    public bool CanBuy { get; set; } = true;

    /// <summary>
    /// Whether this item can be sold.
    /// </summary>
    public bool CanSell { get; set; } = true;

    /// <summary>
    /// Get the buy price.
    /// </summary>
    public long GetBuyPrice() => (long)(BasePrice * BuyMultiplier);

    /// <summary>
    /// Get the sell price.
    /// </summary>
    public long GetSellPrice() => (long)(BasePrice * SellMultiplier);
}
